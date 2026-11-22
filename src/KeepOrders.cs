using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace AlternativeWeaponsControl
{
    public partial class AwcPlugin
    {
        // The game's aircraft AI wipes the engage tasks of its aircraft when it picks its own target (AI.OnFixedUpdate): the whole list is cleared
        // (ObjectBase.ClearEngageTasksKeepGuiding) or every task that is not for the new target and not executed is removed. A waiting order of the
        // player (for instance a gun order that waits for the target to come into range) goes with them. The orders of the player stay; the AI's own
        // tasks are cleared as before.
        static MethodInfo _miRemoveAt, _miEndAlign;

        void HookKeepOrders(Harmony harmony, Assembly asm)
        {
            if (_tObjectBase == null) return;
            const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var clear = _tObjectBase.GetMethod("ClearEngageTasksKeepGuiding", inst, null, Type.EmptyTypes, null);
            _miRemoveAt = _tObjectBase.GetMethod("RemoveEngageTask", inst, null, new[] { typeof(int) }, null);
            _miEndAlign = _tObjectBase.GetMethod("EndAlignment", inst, null, new[] { typeof(int), typeof(string) }, null);
            var ai = asm.GetType("SeaPower.AI");
            var fixedUpdate = ai == null ? null : ai.GetMethod("OnFixedUpdate", inst, null, Type.EmptyTypes, null);
            if (clear == null || _miRemoveAt == null || _miEndAlign == null || fixedUpdate == null) { Warn("AI.OnFixedUpdate / ObjectBase.ClearEngageTasksKeepGuiding not found: the orders of the player are not protected from the AI of aircraft"); return; }
            PatchOne(harmony, clear, "ObjectBase.ClearEngageTasksKeepGuiding", "KeepOrdersClearPrefix");
            try { harmony.Patch(fixedUpdate, transpiler: Hm("KeepOrdersTranspile")); }
            catch (Exception e) { Warn("patching AI.OnFixedUpdate (orders of the player) failed: " + e.Message); }
        }

        /// <summary>A task of the player's unit that the player gave (not an automatic one).</summary>
        static bool IsPlayerOrder(object unit, object task)
        {
            return task != null && !R.B(R.F(task, "_isAutoAttack")) && R.B(R.P(unit, "IsPlayerObject"));
        }

        static bool KeepOrdersClearPrefix(object __instance)
        {
            try
            {
                var tasks = R.F(__instance, "_currentEngageTasks") as IList;
                var wss = R.F(R.F(__instance, "_obp"), "_weaponSystems") as IList;
                if (tasks == null || wss == null || !R.B(R.P(__instance, "IsPlayerObject"))) return true;
                var keepUids = new HashSet<int>();
                for (int i = tasks.Count - 1; i >= 0; i--)
                {
                    if (IsPlayerOrder(__instance, tasks[i])) keepUids.Add(R.I(R.F(tasks[i], "_uid")));
                    else tasks.RemoveAt(i);
                }
                bool held = false;
                foreach (var ws in wss)
                {
                    if (ws == null) continue;
                    var taken = R.B(R.F(ws, "_executingEngageTask")) ? R.F(ws, "_takenEngageTask") : null;
                    if (IsPlayerOrder(__instance, taken)) { keepUids.Add(R.I(R.F(ws, "_alignmentUID"))); held = true; continue; }
                    R.Call(ws, "ClearEngageTask");
                }
                if (keepUids.Count == 0 && !held) return true;   // nothing of the player to keep: the game's own method does all of it (its alignment tasks too)
                var al = R.F(__instance, "_alignmentTasks") as IList;
                if (al != null)
                    for (int i = al.Count - 1; i >= 0; i--)
                    {
                        var a = al[i];
                        if (a == null || R.B(R.F(a, "_isGuidingWeapon")) || keepUids.Contains(R.I(R.F(a, "_uid")))) continue;
                        R.Call(__instance, "EndAlignment", R.I(R.F(a, "_uid")), "ClearEngageTasksKeepGuiding");
                    }
                Trace("AI cleared the tasks of " + UName(__instance) + ": the orders of the player stay (" + keepUids.Count + ")");
                return false;
            }
            catch (Exception e) { Trace("keep orders failed: " + e.Message); return true; }
        }

        /// <summary>In OnFixedUpdate the loop "not for the new target and not executed: RemoveEngageTask(i); EndAlignment" skips the orders of the player.</summary>
        static IEnumerable<CodeInstruction> KeepOrdersTranspile(IEnumerable<CodeInstruction> ins)
        {
            var l = new List<CodeInstruction>(ins);
            var rm = typeof(AwcPlugin).GetMethod("KeepOrdersRemove", BindingFlags.Static | BindingFlags.NonPublic);
            var ea = typeof(AwcPlugin).GetMethod("KeepOrdersEndAlignment", BindingFlags.Static | BindingFlags.NonPublic);
            int found = 0;
            for (int i = 0; i < l.Count; i++)
            {
                var mi = l[i].operand as MethodInfo;
                if (mi == null || mi.Name != "AnySystemExecutingEngageTask") continue;
                int r = -1, e = -1;
                for (int k = i + 1; k < l.Count && k < i + 40 && e < 0; k++)
                {
                    var m2 = l[k].operand as MethodInfo;
                    if (m2 == null) continue;
                    if (r < 0 && m2.Name == "RemoveEngageTask" && m2.GetParameters().Length == 1 && m2.GetParameters()[0].ParameterType == typeof(int)) r = k;
                    else if (r >= 0 && m2.Name == "EndAlignment") e = k;
                }
                if (r < 0 || e < 0) continue;
                l[r].opcode = OpCodes.Call; l[r].operand = rm;
                l[e].opcode = OpCodes.Call; l[e].operand = ea;
                found++;
            }
            if (found == 0) Warn("AI.OnFixedUpdate: the task removal loop was not found: the orders of the player are not protected there");
            return l;
        }

        static void KeepOrdersRemove(object self, int index)
        {
            try
            {
                var tasks = R.F(self, "_currentEngageTasks") as IList;
                if (tasks != null && index >= 0 && index < tasks.Count && IsPlayerOrder(self, tasks[index])) return;
            }
            catch { }
            _miRemoveAt.Invoke(self, new object[] { index });
        }

        static void KeepOrdersEndAlignment(object self, int uid, string why)
        {
            try
            {
                var tasks = R.F(self, "_currentEngageTasks") as IList;
                if (tasks != null) foreach (var t in tasks) if (t != null && R.I(R.F(t, "_uid")) == uid && IsPlayerOrder(self, t)) return;
            }
            catch { }
            _miEndAlign.Invoke(self, new object[] { uid, why });
        }
    }
}
