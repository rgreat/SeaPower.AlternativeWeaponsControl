using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace AlternativeWeaponsControl
{
    public partial class AwcPlugin
    {
        // The game's AI of an aircraft asks "does the loadout have an air-to-air missile" (LoadoutCapabilities.HasAAM). Only an aircraft without one
        // pursues a target for the cannon and uses the gun reach (AI.cs: the gun pursuit, the gun reaction range, the chase reach). Missiles the player
        // turned off (the circles of the Weapons tab, the Standing Orders) stay in the loadout, so the AI kept waiting for a missile shot and never
        // went for the guns. The reads of the AI are replaced with a question that counts only the missiles the unit may fire by itself.
        // Written against the pattern of the game's code (a read of _currentLoadoutCapabilities.HasAAM in the AI class): when an update of the game
        // changes it, nothing is patched and the line "Aircraft guns: ..." in the trace says so.
        sealed class AamBox { public double Until; public bool Usable, Seen; }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, AamBox> AamCache = new System.Runtime.CompilerServices.ConditionalWeakTable<object, AamBox>();

        void HookAircraftGuns(Harmony harmony, Assembly asm)
        {
            if (_tObjectBase == null || _aamGuns == null) return;   // patched always: AamNow reads the option, so no restart
            var ai = asm.GetType("SeaPower.AI");
            if (ai == null || _tObjectBase.GetField("_currentLoadoutCapabilities") == null) { Warn("Aircraft guns: AI / _currentLoadoutCapabilities not found: the option does nothing"); return; }
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            int methods = 0, sites = 0;
            foreach (var m in ai.GetMethods(all))
            {
                if (m.IsAbstract || m.GetMethodBody() == null) continue;
                int n = 0;
                n = CountHasAamCalls(m);
                if (n == 0) continue;
                try { harmony.Patch(m, transpiler: Hm("AamTranspile")); methods++; sites += n; }
                catch (Exception e) { Warn("Aircraft guns: patching " + m.Name + " failed: " + e.Message); }
            }
            if (sites == 0) Warn("Aircraft guns: the reads of HasAAM are not found in the AI of the game (its code changed): the option does nothing");
            else Trace("Aircraft guns when missiles are off: " + sites + " place(s) in " + methods + " method(s) of the AI patched");
        }

        /// <summary>How many calls of LoadoutCapabilities.HasAAM the IL of the method has (the call opcode and a token that resolves to that getter).</summary>
        static int CountHasAamCalls(MethodInfo m)
        {
            int n = 0;
            try
            {
                var il = m.GetMethodBody().GetILAsByteArray();
                for (int i = 0; i + 4 < il.Length; i++)
                {
                    if (il[i] != 0x28 && il[i] != 0x6F) continue;
                    try
                    {
                        var t = m.Module.ResolveMethod(BitConverter.ToInt32(il, i + 1), m.DeclaringType.IsGenericType ? m.DeclaringType.GetGenericArguments() : null, null);
                        if (t.Name == "get_HasAAM" && t.DeclaringType != null && t.DeclaringType.Name == "LoadoutCapabilities") n++;
                    }
                    catch { }
                }
            }
            catch { }
            return n;
        }

        static bool IsHasAamRead(CodeInstruction ci)
        {
            var mi = ci.operand as MethodInfo;
            return mi != null && mi.Name == "get_HasAAM" && mi.DeclaringType != null && mi.DeclaringType.Name == "LoadoutCapabilities";
        }

        static IEnumerable<CodeInstruction> AamTranspile(IEnumerable<CodeInstruction> ins)
        {
            var l = new List<CodeInstruction>(ins);
            var call = typeof(AwcPlugin).GetMethod("AamNow", BindingFlags.Static | BindingFlags.NonPublic);
            for (int i = 1; i < l.Count; i++)
            {
                if (!IsHasAamRead(l[i])) continue;
                var f = l[i - 1].operand as FieldInfo;
                if (l[i - 1].opcode != OpCodes.Ldflda || f == null || f.Name != "_currentLoadoutCapabilities") continue;   // another form of the read: left as it is
                l[i - 1].opcode = OpCodes.Nop; l[i - 1].operand = null;   // keeps a jump target on it
                l[i].opcode = OpCodes.Call; l[i].operand = call;
            }
            return l;
        }

        /// <summary>HasAAM of the unit for the AI: as the game says, except that a unit of the player counts only the air-to-air missiles it may fire by itself.</summary>
        static bool AamNow(object unit)
        {
            bool has;
            try { has = R.B(R.P(R.F(unit, "_currentLoadoutCapabilities"), "HasAAM")); } catch { return true; }
            if (!has || _aamGuns == null || !_aamGuns.Value) return has;
            try
            {
                if (!R.B(R.P(unit, "IsPlayerObject"))) return has;
                double now = MissionTime();
                AamBox b;
                if (!AamCache.TryGetValue(unit, out b)) { b = new AamBox { Until = -1 }; AamCache.Add(unit, b); }
                if (now >= b.Until)
                {
                    bool was = b.Usable, seen = b.Seen;
                    b.Usable = AnyAamAllowed(unit); b.Until = now + 1; b.Seen = true;
                    if (seen && was != b.Usable) Trace("aircraft " + UName(unit) + ": air-to-air missiles " + (b.Usable ? "may be fired by the AI again" : "are all off: the AI goes for the guns"));
                }
                return b.Usable;
            }
            catch (Exception e) { TraceLimited("aamnow", 60f, "aircraft guns: " + e.Message); return has; }
        }

        /// <summary>The unit has air-to-air missiles (in stock) that its circles and the Standing Orders let the AI fire at an air target.</summary>
        static bool AnyAamAllowed(object unit)
        {
            var w = W(unit);
            foreach (var l in CachedLaunchers(unit, w))
            {
                if ((l.Domains & DAir) == 0 || !Effective(unit, w, l, DAir)) continue;
                for (int i = 0; i < l.Ammo.Count; i++)
                {
                    if ((l.AmmoDom[i] & DAir) == 0) continue;
                    var am = R.Call(unit, "getAmmunitionByName", l.Ammo[i]);
                    var ap = am == null ? null : R.F(am, "_ap");
                    if (ap == null || R.S(R.F(ap, "_type")) != "Missile") continue;
                    if (R.I(R.Call(unit, "getAmmunitionAmountByName", l.Ammo[i])) > 0) return true;
                }
            }
            return false;
        }

        // ---- a clicked air target in the game before the 0.9.3 beta ---------------------------------------------------------------------------------------------
        // A right click on an air contact with an aircraft selected only sets AI._objectToDestroy (AI.AutoAttackByClick). AI.ShouldAbandonAirChase then drops that
        // target (and CeaseFire clears the order) once it is beyond the weapons' reach and the aircraft is not closing in - an aircraft that waits for a missile shot does
        // not close in, so the order vanished by itself. The beta game asks AI.IsOrderedTarget first. Where that method does not exist the clicked target is remembered here
        // and ShouldAbandonAirChase answers "keep" for it.
        sealed class ClickedBox { public WeakReference Target; }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, ClickedBox> ClickedAir = new System.Runtime.CompilerServices.ConditionalWeakTable<object, ClickedBox>();

        void HookClickedAir(Harmony harmony, Assembly asm)
        {
            if (_keepClicked == null || _tObjectBase == null) return;   // patched always, the option is read when the game asks (no restart)
            var ai = asm.GetType("SeaPower.AI"); if (ai == null) return;
            const BindingFlags any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            if (ai.GetMethod("IsOrderedTarget", any) != null) return;   // the game keeps the ordered targets itself
            MethodInfo click = null, aclick = null, abandon = null;
            foreach (var m in ai.GetMethods(any))
            {
                if (m.Name == "AutoAttackByClick" && click == null) click = m;
                else if (m.Name == "AircraftAttackByClick" && aclick == null) aclick = m;
                else if (m.Name == "ShouldAbandonAirChase" && m.GetParameters().Length == 1) abandon = m;
            }
            if ((click == null && aclick == null) || abandon == null) { Warn("AI.AircraftAttackByClick / ShouldAbandonAirChase not found: the option \"keep a clicked air target\" does nothing"); return; }
            if (aclick != null) PatchOne(harmony, aclick, "AI.AircraftAttackByClick (clicked air target)", null, "ClickedAirPostfix2");
            if (click != null) PatchOne(harmony, click, "AI.AutoAttackByClick (clicked air target)", null, "ClickedAirPostfix");
            PatchOne(harmony, abandon, "AI.ShouldAbandonAirChase (clicked air target)", "AbandonAirChasePrefix");
            Trace("Keep a clicked air target: AI.ShouldAbandonAirChase patched (the game has no IsOrderedTarget)");
            // The release AI makes the missile engage task for a target only if the launcher's vision requirement is met (WeaponSystem.IsVisionRequirementMet); the game
            // waives it for a CAP patrol's committed target only. A target the player clicked is waived here too, and only while the AI selects the ammunition for it
            // (the launch itself still asks the game).
            var sel = ai.GetMethod("SelectLauncherAmmoForAutoEngage", any);
            var ws = asm.GetType("SeaPower.WeaponSystem"); var vis = ws == null ? null : ws.GetMethod("IsVisionRequirementMet", any);
            if (sel == null || vis == null) { Warn("AI.SelectLauncherAmmoForAutoEngage / WeaponSystem.IsVisionRequirementMet not found: a clicked air target still needs the launcher's vision"); return; }
            PatchOne(harmony, sel, "AI.SelectLauncherAmmoForAutoEngage (clicked air target vision)", "SelectAmmoPrefix", null, "SelectAmmoFinalizer");
            PatchOne(harmony, vis, "WeaponSystem.IsVisionRequirementMet (clicked air target vision)", null, "VisionPostfix");
        }

        static void ClickedAirPostfix(object __instance, object targetObject, bool showPlayerFeedback)
        {
            try
            {
                if (!showPlayerFeedback || targetObject == null || !_keepClicked.Value) return;
                var unit = R.F(__instance, "_baseObject");
                if (unit == null || !R.Is(unit, _tAircraft) || !R.B(R.P(unit, "IsPlayerObject")) || !R.B(R.Call(targetObject, "isAirTarget"))) return;
                RememberClicked(unit, targetObject);
            }
            catch (Exception e) { TraceLimited("clickedair", 60f, "clicked air target: " + e.Message); }
        }

        // The right click of the player on a contact with aircraft selected goes through AI.AircraftAttackByClick (not AutoAttackByClick): the target is queued for the
        // aircraft and, with a formation, handed to its members.
        static void ClickedAirPostfix2(object __instance, object targetObject, object areaTargets)
        {
            try
            {
                if (targetObject == null || !_keepClicked.Value || !R.B(R.Call(targetObject, "isAirTarget"))) return;
                var unit = R.F(__instance, "_baseObject");
                if (unit == null || !R.Is(unit, _tAircraft) || !R.B(R.P(unit, "IsPlayerObject"))) return;
                RememberClicked(unit, targetObject);
                var form = R.P(unit, "Formation");
                var stations = form == null ? null : R.P(form, "Stations") as System.Collections.IEnumerable;
                if (stations == null) return;
                foreach (var st in stations)
                {
                    var u = st == null ? null : R.P(st, "UnitObject");
                    if (u != null && !ReferenceEquals(u, unit) && R.Is(u, _tAircraft) && R.B(R.P(u, "IsPlayerObject"))) RememberClicked(u, targetObject);
                }
            }
            catch (Exception e) { TraceLimited("clickedair2", 60f, "clicked air target: " + e.Message); }
        }

        static void RememberClicked(object unit, object target)
        {
            ClickedBox b;
            if (!ClickedAir.TryGetValue(unit, out b)) { b = new ClickedBox(); ClickedAir.Add(unit, b); }
            b.Target = new WeakReference(target);
            Trace("aircraft " + UName(unit) + ": clicked air target " + UName(target) + " remembered");
        }

        [ThreadStatic] static object _selTarget;   // the clicked air target while the AI selects the missile for it
        static void SelectAmmoPrefix(object __instance, object targetObject)
        {
            _selTarget = null;
            try
            {
                if (!_keepClicked.Value || targetObject == null) return;
                var unit = R.F(__instance, "_baseObject");
                ClickedBox b;
                if (unit == null || !R.Is(unit, _tAircraft) || !R.B(R.P(unit, "IsPlayerObject")) || !ClickedAir.TryGetValue(unit, out b) || b.Target == null || !ReferenceEquals(b.Target.Target, targetObject)) return;
                _selTarget = targetObject;
            }
            catch { }
        }
        static void SelectAmmoFinalizer() { _selTarget = null; }
        static void VisionPostfix(object __0, ref bool __result)
        {
            if (__result || _selTarget == null || !ReferenceEquals(__0, _selTarget)) return;
            __result = true;
            try { if (TraceDue("clickedvision", 30f)) Trace("aircraft: the clicked air target " + UName(__0) + " needs no launcher vision to be engaged (the game waives it for a CAP patrol only)"); } catch { }
        }

        static bool AbandonAirChasePrefix(object __instance, object target, ref bool __result)
        {
            try
            {
                if (!_keepClicked.Value) return true;
                var unit = R.F(__instance, "_baseObject");
                ClickedBox b;
                if (unit == null || target == null || !ClickedAir.TryGetValue(unit, out b) || b.Target == null || !ReferenceEquals(b.Target.Target, target)) return true;
                if (TraceDue("clickedkeep" + R.I(R.P(unit, "UniqueID")), 30f)) Trace("aircraft " + UName(unit) + ": keeps the clicked target " + UName(target) + " (the game would drop it)");
                __result = false;
                return false;
            }
            catch { return true; }
        }
    }
}
