using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace AlternativeWeaponsControl
{
    /// <summary>
    /// The beta game: a weapon that no rule of the unit's Standing Orders covers (no rule matches the weapon class x target pair) never fires by itself - the end of
    /// AutoEngageRules.PermitsFrom is "return false", and the end of ResolveUnconditional (the resolved grid of the Standing Orders window) is "Never". Here both ends ask the
    /// option Fire/UncoveredWeapons instead: Free (default) = such a weapon fires on Weapons Free; Tight = on Weapons Tight and Free; Hold = at any status (the policy Always). The game itself: never.
    /// It is the policy of an imaginary last rule that matches everything; a conditional rule that matches but whose conditions fail falls through to it as it did to "no".
    /// The patch is a transpiler of the two methods (the last "ldc.i4.0; ret" of each): a game that has other bodies is left alone (a warning), nothing depends on a version.
    /// </summary>
    public partial class AwcPlugin
    {
        internal static readonly string[] UncoveredNames = { "Free", "Tight", "Hold" };   // the weapon status it fires from; the policies of the game: WeaponsFree = 1, WeaponsTight = 2, Always = 3 (Hold)
        static readonly int[] UncoveredPolicy = { 1, 2, 3 };

        static int UncoveredIndex()
        {
            var a = Instance;
            string v = a == null || a._uncovered == null ? "Free" : a._uncovered.Value;
            for (int i = 0; i < UncoveredNames.Length; i++) if (string.Equals(UncoveredNames[i], v, StringComparison.OrdinalIgnoreCase)) return i;
            return 0;
        }

        /// <summary>The policy of the imaginary last rule (the game's AutoEngagePolicy as a number).</summary>
        static int UncoveredPolicyNow() { return UncoveredPolicy[UncoveredIndex()]; }

        // called from the game's PermitsFrom: no rule matched
        public static bool UncoveredAllows(int domain, int weaponClass, int target, int status)
        {
            switch (UncoveredPolicyNow())
            {
                case 3: return true;
                case 2: return status == 0 || status == 1;   // WeaponStatus: Tight = 0, Free = 1, Hold = 2
                case 1: return status == 1;
                default: return false;
            }
        }

        // called from the game's ResolveUnconditional: no rule decided
        public static int UncoveredResolved(int domain, int weaponClass, int target) { return UncoveredPolicyNow(); }

        void HookUncovered(Harmony harmony)
        {
            if (_tRules == null || _legacy) return;
            const BindingFlags any = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var pf = _tRules.GetMethod("PermitsFrom", any); var ru = _tRules.GetMethod("ResolveUnconditional", any);
            if (pf == null || ru == null) { Warn("AutoEngageRules.PermitsFrom / ResolveUnconditional not found: the option for weapons that no Standing Orders rule covers does nothing"); return; }
            try
            {
                harmony.Patch(pf, transpiler: Hm("UncoveredTranspilePermits"));
                harmony.Patch(ru, transpiler: Hm("UncoveredTranspileResolve"));
            }
            catch (Exception e) { Warn("patching the Standing Orders end of rules failed: " + e.Message); }
        }

        static IEnumerable<CodeInstruction> UncoveredTranspilePermits(IEnumerable<CodeInstruction> ins)
        {
            // rules 0, start 1, domain 2, weaponClass 3, target 4, ammoName 5, rangeNm 6, status 7, context 8, anyConditionOutcome 9
            var call = typeof(AwcPlugin).GetMethod("UncoveredAllows", BindingFlags.Static | BindingFlags.Public);
            return Replace(ins, "PermitsFrom", new[] { new CodeInstruction(OpCodes.Ldarg_2), new CodeInstruction(OpCodes.Ldarg_3), new CodeInstruction(OpCodes.Ldarg_S, (byte)4),
                                                       new CodeInstruction(OpCodes.Ldarg_S, (byte)7), new CodeInstruction(OpCodes.Call, call) });
        }

        static IEnumerable<CodeInstruction> UncoveredTranspileResolve(IEnumerable<CodeInstruction> ins)
        {
            // rules 0, domain 1, weaponClass 2, target 3, conditional 4
            var call = typeof(AwcPlugin).GetMethod("UncoveredResolved", BindingFlags.Static | BindingFlags.Public);
            return Replace(ins, "ResolveUnconditional", new[] { new CodeInstruction(OpCodes.Ldarg_1), new CodeInstruction(OpCodes.Ldarg_2), new CodeInstruction(OpCodes.Ldarg_3), new CodeInstruction(OpCodes.Call, call) });
        }

        /// <summary>The last "ldc.i4.0; ret" of the method becomes the given instructions (the ldc.i4.0 itself becomes the first one, so a jump to it still works); the method as it was when the end is another one.</summary>
        static List<CodeInstruction> Replace(IEnumerable<CodeInstruction> ins, string what, CodeInstruction[] repl)
        {
            var l = new List<CodeInstruction>(ins);
            int n = l.Count;
            if (n < 2 || l[n - 1].opcode != OpCodes.Ret || l[n - 2].opcode != OpCodes.Ldc_I4_0) { Warn(what + ": the end of the method is not 'return false / Never' (the game has changed): the option for weapons that no Standing Orders rule covers does nothing"); return l; }
            var first = l[n - 2];   // the instruction itself stays (a jump may lead to it): it becomes the first of the new ones
            first.opcode = repl[0].opcode; first.operand = repl[0].operand;
            for (int i = 1; i < repl.Length; i++) l.Insert(n - 2 + i, repl[i]);
            return l;
        }
    }
}
