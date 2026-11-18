using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace AlternativeWeaponsControl
{
    /// <summary>
    /// An attack order on the map (a point of a sonobuoy pattern, a bombing point: AttackAtWaypoint) turns into an engage task of the unit when it runs, and from then on
    /// the engage task lives its own life: the game's X on the map deletes the order but leaves its engage tasks in the unit's list, so the unit goes on with
    /// them and the new orders wait behind. Here every engage task remembers the order that made it; deleting an order that has not finished (the player's X, Cease fire)
    /// takes its engage tasks out of the list and stops the mounts that have taken them (as Stop does). An order that finished by itself leaves its tasks alone.
    /// </summary>
    public partial class AwcPlugin
    {
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, object> WpOwner = new System.Runtime.CompilerServices.ConditionalWeakTable<object, object>();   // engage task -> the order that made it
        static bool _wpFinishing;

        void HookWaypointFix(Harmony harmony, Assembly asm)
        {
            const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            var aw = asm.GetType("SeaPower.AttackAtWaypoint"); var ur = asm.GetType("SeaPower.UserRootNode");
            PatchOne(harmony, aw == null ? null : aw.GetMethod("SingleAttack", any), "AttackAtWaypoint.SingleAttack", null, "WpMadePostfix");
            PatchOne(harmony, ur == null ? null : ur.GetMethod("DeleteTask", any), "UserRootNode.DeleteTask", "WpDeletePrefix", null);
            PatchOne(harmony, ur == null ? null : ur.GetMethod("RemoveFinishedConnections", any), "UserRootNode.RemoveFinishedConnections", "WpFinishPrefix", null, "WpFinishFinalizer");
        }

        // the order has just made an engage task: the newest task of the unit (the highest uid) is it
        static void WpMadePostfix(object __instance, object baseObject)
        {
            try
            {
                var tasks = R.F(baseObject, "_currentEngageTasks") as IList; if (tasks == null) return;
                object best = null; int uid = -1; object tmp;
                foreach (var t in tasks)
                {
                    if (t == null || WpOwner.TryGetValue(t, out tmp)) continue;
                    int u = R.I(R.F(t, "_uid"));
                    if (u > uid) { uid = u; best = t; }
                }
                if (best != null) WpOwner.Add(best, __instance);
            }
            catch (Exception e) { TraceLimited("wp-made", 10f, "waypoint order: " + e.Message); }
        }

        static void WpFinishPrefix() { _wpFinishing = true; }
        static void WpFinishFinalizer() { _wpFinishing = false; }

        // an unfinished order is deleted (the player's X): what Stop does - the mounts that have taken its engage tasks drop them, the tasks go from the unit's list
        static void WpDeletePrefix(object toRemove)
        {
            if (_wpFinishing || toRemove == null || !Enabled || toRemove.GetType().Name != "AttackAtWaypoint") return;
            try
            {
                var unit = R.F(toRemove, "_baseObject"); if (unit == null) return;
                var tasks = R.F(unit, "_currentEngageTasks") as IList; if (tasks == null) return;
                var wss = R.F(R.F(unit, "_obp"), "_weaponSystems") as IList;
                var mine = new List<object>(); object owner;
                foreach (var t in tasks) if (t != null && WpOwner.TryGetValue(t, out owner) && ReferenceEquals(owner, toRemove)) mine.Add(t);
                int removed = 0, ended = 0;
                var ammo = new HashSet<string>();
                foreach (var t in mine)
                {
                    ammo.Add(R.S(R.F(t, "_ammoId")));
                    if (wss != null)
                        foreach (var ws in wss)
                        {
                            if (ws == null || !ReferenceEquals(R.F(ws, "_takenEngageTask"), t)) continue;
                            try { R.Call(ws, "EndEngageTask", true); ended++; } catch (Exception e) { Trace("order delete: " + e.Message); }
                            if (R.Is(ws, _tCiws)) { try { R.Call(ws, "StopEngage"); } catch { } }
                        }
                    if (tasks.Contains(t)) { try { R.Call(unit, "RemoveEngageTask", t); } catch { tasks.Remove(t); } }
                    removed++;
                }
                if (ammo.Count > 0) ForgetTargets(unit, ammo);
                if (removed > 0) Trace("order deleted on the map: " + UName(unit) + ": " + removed + " engage task(s) of it removed, " + ended + " mount(s) stopped");
            }
            catch (Exception e) { TraceLimited("wp-del", 10f, "waypoint order delete: " + e.Message); }
        }
    }
}
