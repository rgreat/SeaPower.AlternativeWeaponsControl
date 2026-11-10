using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace AlternativeWeaponsControl
{
    /// <summary>
    /// [Guns] RememberTargets. The game hands every new order of the player to ALL guns with the ammunition, overwriting what they
    /// shot at, and forgets the old order. Here the guns (WeaponSystemGun, not CIWS) keep the player's orders of a unit in a list
    /// (newest first): the new order is taken by every gun as before, but a gun that cannot reach that target (range, firing
    /// sector) goes on with the newest remembered target it can reach, and returns to the newest one when it can. Cease fire
    /// (the tab's stop, the unit's Cease fire, Hold) clears the list; targets that are gone drop out of it.
    /// </summary>
    public partial class AwcPlugin
    {
        sealed class GunMem { public object Task, Target; public string Ammo; }
        sealed class RefCmp : IEqualityComparer<object> { public new bool Equals(object a, object b) { return ReferenceEquals(a, b); } public int GetHashCode(object o) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o); } }
        static readonly Dictionary<object, List<GunMem>> GunMems = new Dictionary<object, List<GunMem>>(new RefCmp());
        static readonly Dictionary<object, HashSet<object>> GunSeen = new Dictionary<object, HashSet<object>>(new RefCmp());   // the orders already put into the list (one per order, not per gun)
        static float _nextGunMem;
        static Type _tGunExact;

        void HookGunTargets(Harmony harmony, Assembly asm)
        {
            const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _tGunExact = asm.GetType("SeaPower.WeaponSystemGun");
            var cf = _tObjectBase == null ? null : _tObjectBase.GetMethod("CeaseFire", inst | BindingFlags.DeclaredOnly);
            if (cf != null) harmony.Patch(cf, postfix: Hm("GunMemCeasePostfix")); else Trace("ObjectBase.CeaseFire not found: the remembered targets are not cleared by Cease fire");
        }

        static void GunMemCeasePostfix(object __instance) { if (__instance != null) { Trace("CeaseFire of " + R.S(R.Call(__instance, "getUIDAndName"))); GunMems.Remove(__instance); GunSeen.Remove(__instance); } }

        /// <summary>Cease fire of the tab for these ammunition: forget the unit's remembered targets of it.</summary>
        internal static void ForgetTargets(object unit, ICollection<string> ammo)
        {
            List<GunMem> l;
            if (unit == null || !GunMems.TryGetValue(unit, out l)) return;
            l.RemoveAll(m => ammo == null || ammo.Contains(m.Ammo));
            if (l.Count == 0) { GunMems.Remove(unit); GunSeen.Remove(unit); }
        }

        static bool Gone(object t)
        {
            if (t == null) return true;
            var uo = t as UnityEngine.Object; if (uo != null && !uo) return true;
            try { return R.B(R.P(t, "IsDestroyed")); } catch { return false; }
        }

        static bool IsPlainGun(object ws) { return ws != null && ws.GetType() == _tGunExact; }

        /// <summary>After the game's hand-out of orders: the player's (not automatic, not tied to a launcher) orders the guns hold go to the list.</summary>
        static void RememberGunOrders(object unit)
        {
            var a = Active; if (a == null || !Enabled || a._gunMemory == null || !a._gunMemory.Value) return;
            if (!R.B(R.P(unit, "IsPlayerObject"))) return;
            var guns = R.F(R.F(unit, "_obp"), "_gunWeapons") as IList; if (guns == null) return;
            foreach (var g in guns)
            {
                if (!IsPlainGun(g) || !R.B(R.F(g, "_executingEngageTask"))) continue;
                var task = R.F(g, "_takenEngageTask");
                if (task == null || R.B(R.F(task, "_isAutoAttack")) || PinOf(task) != null) continue;
                var tgt = R.F(task, "_targetObject"); if (tgt == null) continue;
                // every gun holds the order: it goes to the list once (the guns that switched to an older target hold the older order, which must not move to the front again)
                HashSet<object> seen; if (!GunSeen.TryGetValue(unit, out seen)) { seen = new HashSet<object>(new RefCmp()); GunSeen[unit] = seen; }
                if (!seen.Add(task)) continue;
                List<GunMem> l; if (!GunMems.TryGetValue(unit, out l)) { l = new List<GunMem>(); GunMems[unit] = l; }
                string ammo = R.S(R.F(task, "_ammoId"));
                l.RemoveAll(m => ReferenceEquals(m.Target, tgt) && m.Ammo == ammo);
                l.Insert(0, new GunMem { Task = task, Target = tgt, Ammo = ammo });
                if (l.Count > 8) l.RemoveAt(l.Count - 1);
                Trace("remembered targets of " + R.S(R.Call(unit, "getUIDAndName")) + ": " + l.Count);
            }
        }

        /// <summary>Can the gun hit the target now: in range (and the direct-fire limit for aircraft) and in its firing sector.</summary>
        static bool GunCanReach(object gun, GunMem m)
        {
            try
            {
                if (Gone(m.Target)) return false;
                var tr = R.P(m.Target, "transform") as Transform; if (tr == null) return false;
                var ap = R.F(R.F(m.Task, "_ammunition"), "_ap"); if (ap == null) return false;
                var mount = R.F(gun, "_mountObject") as GameObject;
                Vector3 from = mount != null ? mount.transform.position : ((R.P(R.F(gun, "_baseObject"), "transform") as Transform) ?? tr).position;
                float max = ((Vector2)R.F(ap, "_launchRangesInUnity")).y;
                Vector3 d = tr.position - from;
                if (R.B(R.Call(m.Target, "isAirTarget"))) max = Mathf.Min(max, AirDirectFire(ap, d.y) * 1.15f);
                if (max <= 0f || d.magnitude > max) return false;
                // the game ends a gun's order at once when its side has lost the target (no contact)
                if (!R.B(R.F(gun, "_ignoreIfUndetected")) && R.Call(m.Target, "GetMapVehicle", R.F(R.F(gun, "_baseObject"), "_taskforce")) == null) return false;
                return R.B(R.Call(gun, "targetIsInFiringArc", tr.position));
            }
            catch { return false; }
        }

        static bool GunUsesAmmo(object gun, string ammo)
        {
            try
            {
                var cur = R.S(R.F(R.F(R.F(gun, "_ammoForEngage"), "_ap"), "_ammunitionFileName"));
                return cur == ammo || R.I(R.Call(gun, "getLoadedAmmoCount", ammo)) > 0 || R.I(R.Call(gun, "getMagazineAmmoCount", ammo)) > 0;
            }
            catch { return false; }
        }

        // trace only: a gun that has held an order for 20 s and is still not shooting - what it waits for
        static void GunWaitTrace(object g, object unit, object cur)
        {
            if (!_debug) return;
            try
            {
                double age = MissionTime() - Convert.ToDouble(R.F(g, "_engageStartTime"));
                if (age < 20) return;
                var fc = R.Call(g, "GetFireControlSensor");
                string st = R.S(R.F(g, "_engageState"));
                if (st == "Firing" || st == "Shooting") return;
                TraceLimited("gw" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(g), 20f,
                    "gun " + R.S(R.F(g, "_systemName")) + " of " + R.S(R.Call(unit, "getUIDAndName")) + " waits " + (int)age + " s for " + R.S(R.Call(cur, "getUIDAndName")) +
                    ": state=" + st + " reload=" + R.B(R.F(g, "_isReloading")) + " inArc=" + R.B(R.Call(g, "targetIsInFiringArc", ((Transform)R.P(cur, "transform")).position)) +
                    " director=" + (fc == null ? "none(local)" : (Holds(fc, cur) ? "holds" : "busy")) + " ignoreUndet=" + R.B(R.F(g, "_ignoreIfUndetected")));
            }
            catch { }
        }

        static void GunTargetsStep(float now)
        {
            if (now < _nextGunMem) return;
            _nextGunMem = now + 0.5f;
            var a = Active; if (a == null || !Enabled || GunMems.Count == 0) return;
            if (a._gunMemory == null || !a._gunMemory.Value) { GunMems.Clear(); return; }
            List<object> dead = null;
            foreach (var kv in GunMems)
            {
                var unit = kv.Key; var l = kv.Value;
                if (Gone(unit)) { (dead ?? (dead = new List<object>())).Add(unit); continue; }
                l.RemoveAll(m => Gone(m.Target));
                if (l.Count == 0) { (dead ?? (dead = new List<object>())).Add(unit); continue; }
                var guns = R.F(R.F(unit, "_obp"), "_gunWeapons") as IList; if (guns == null) continue;
                foreach (var g in guns)
                {
                    if (!IsPlainGun(g)) continue;
                    bool exec = R.B(R.F(g, "_executingEngageTask"));
                    var taken = exec ? R.F(g, "_takenEngageTask") : null;
                    if (exec && (taken == null || R.B(R.F(taken, "_isAutoAttack")) || PinOf(taken) != null)) continue;   // the AI's or a launcher's own order
                    var cur = exec ? R.F(g, "_targetObject") : null;
                    if (exec && cur != null) GunWaitTrace(g, unit, cur);
                    int ci = -1, bi = -1;
                    for (int i = 0; i < l.Count; i++)
                    {
                        var m = l[i];
                        if (ci < 0 && cur != null && ReferenceEquals(m.Target, cur)) ci = i;
                        if (bi < 0 && GunUsesAmmo(g, m.Ammo) && GunCanReach(g, m)) bi = i;
                    }
                    if (bi < 0) continue;                                         // nothing it can hit: it stays as it is
                    if (ci >= 0 && ci <= bi && GunCanReach(g, l[ci])) continue;    // already on the newest one it can hit
                    if (ci < 0 && exec && cur != null) { var cm = new GunMem { Task = taken, Target = cur }; if (GunCanReach(g, cm)) continue; }
                    GunTake(g, l[bi]);
                    Trace("gun " + R.S(R.F(g, "_systemName")) + " of " + R.S(R.Call(unit, "getUIDAndName")) + " goes on to " + R.S(R.Call(l[bi].Target, "getUIDAndName")));
                }
            }
            if (dead != null) foreach (var u in dead) { GunMems.Remove(u); GunSeen.Remove(u); }
        }

        /// <summary>The gun takes the order as the game's hand-out of orders gives it to a gun.</summary>
        static void GunTake(object g, GunMem m)
        {
            var t = m.Task;
            R.SetF(g, "_takenEngageTask", t);
            R.SetF(g, "_executingEngageTask", true);
            R.SetF(g, "_engageTaskPriority", R.F(t, "_priority"));
            R.SetF(g, "_ammoForEngage", R.F(t, "_ammunition"));
            R.SetF(g, "_targetObject", m.Target);
            R.SetF(g, "_currentTargetPoint", R.F(t, "_curentTargetPoint"));
            R.SetF(g, "_targetPosition", R.P(t, "TargetPosition"));
            R.SetF(g, "_alignmentUID", R.F(t, "_uid"));
            R.SetF(g, "_engageStartTime", MissionTime());
            R.SetF(g, "_engageDelay", 0f);
        }
    }
}
