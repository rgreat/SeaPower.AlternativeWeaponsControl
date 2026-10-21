using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace AlternativeWeaponsControl
{
    /// <summary>
    /// SAM readiness button (Weapons tab). A launcher with a warm-up (WeaponSystemLauncher: _vwp._hasWarmUp) is "charged" by hand:
    /// WarmUp() (IsHot + the reload time), then the game's load system puts the missiles onto the rails (triggerLoadSystem).
    /// The time limit starts when the rails are loaded; when it ends and the launcher was not used, the missiles go back to the
    /// magazine and the launcher cools down. A launch (rails empty) stops the count; it starts again when the rails are loaded again.
    /// Reload on such a launcher = unload the rails, charge again.
    /// </summary>
    public partial class AwcPlugin
    {
        sealed class Box { public double Until; public bool Pending, Unload, Recharge, Started; }
        static readonly ConditionalWeakTable<object, Box> _warmBox = new ConditionalWeakTable<object, Box>();

        void HookWarmUp(Harmony harmony, Assembly asm)
        {
            var t = asm.GetType("SeaPower.WeaponSystemLauncher");
            var m = t == null ? null : t.GetMethod("OnUpdate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
            if (m == null) { Trace("WeaponSystemLauncher.OnUpdate not found: no SAM ready time limit"); return; }
            harmony.Patch(m, postfix: Hm("WarmPostfix"));
            var ai = asm.GetType("SeaPower.AI");
            var am = ai == null ? null : ai.GetMethod("OnUpdate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (am != null) harmony.Patch(am, postfix: Hm("AutoChargePostfix")); else Trace("AI.OnUpdate not found: no SAM auto charge");
            var sn = t == null ? null : t.GetMethod("setNextAmmunitionLauncherPositions", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            var pu = t == null ? null : t.GetMethod("playUnloadAnimation", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
            if (sn != null) harmony.Patch(sn, postfix: Hm("AnimLoadPostfix")); else Trace("setNextAmmunitionLauncherPositions not found: no load animation fix");
            if (pu != null) harmony.Patch(pu, postfix: Hm("AnimUnloadPostfix")); else Trace("playUnloadAnimation not found: no unload animation fix");
        }

        static float WarmSeconds() { return _inst == null || _inst._samMinutes == null ? 900f : Math.Max(1f, _inst._samMinutes.Value) * 60f; }

        /// <summary>The rails: slots per container and what is loaded (slots in order, the loaded ones first in each container).</summary>
        internal static List<bool> Rails(object ws)
        {
            var res = new List<bool>();
            var cs = R.F(ws, "_containers") as IList;
            if (cs == null) return res;
            foreach (var c in cs)
            {
                var pos = R.F(c, "_attachmentPositions") as IList;
                var w = R.F(c, "_weapons") as IList;
                int have = w == null ? 0 : w.Count, cap = Math.Max(pos == null ? 0 : pos.Count, have);
                for (int i = 0; i < cap; i++) res.Add(i < have);
            }
            return res;
        }

        static bool Idle(object ws) { var s = R.F(ws, "_state"); return s == null || s.ToString() == "Idle"; }
        static bool IsEngaging(object ws) { return R.B(R.F(ws, "_executingEngageTask")) || R.B(R.F(ws, "_isAutoEngaging")); }

        static int Loaded(object ws) { int n = 0; foreach (var b in Rails(ws)) if (b) n++; return n; }

        /// <summary>Time limit and loading of a hand-charged launcher.</summary>
        static void WarmPostfix(object __instance)
        {
            try
            {
                Box b;
                bool has = _warmBox.TryGetValue(__instance, out b);
                if (has && b.Unload)
                {
                    // our own unload (the game's animated one): when it is done, charge again or cool down
                    if (IsEngaging(__instance)) { b.Unload = false; b.Recharge = false; b.Started = false; R.SetF(__instance, "_triggerShutDown", false); return; }
                    if (!b.Started)
                    {
                        b.Started = true;
                        // rails loaded without the game's load system have no "ammunition on the weapon": the unload animation needs it
                        if (R.F(__instance, "_ammunitionOnWeapon") == null)
                        {
                            var cs0 = R.F(__instance, "_containers") as IList;
                            if (cs0 != null) foreach (var c0 in cs0) { var la = R.F(c0, "_loadedAmmunition"); var w0 = R.F(c0, "_weapons") as IList; if (la != null && w0 != null && w0.Count > 0) { R.SetF(__instance, "_ammunitionOnWeapon", la); break; } }
                        }
                        R.SetF(__instance, "_triggerShutDown", true);
                        return;
                    }
                    if (!R.B(R.F(__instance, "_triggerShutDown")) && Idle(__instance))
                    {
                        b.Unload = false; b.Started = false;
                        if (b.Recharge) { b.Recharge = false; R.SetP(__instance, "IsHot", false); Charge(__instance); }   // as a click on a cold launcher: the game's warm-up, then the load
                        else { R.SetP(__instance, "IsHot", false); _warmBox.Remove(__instance); }
                    }
                    return;
                }
                if (_inst != null && _inst._samKeep != null && _inst._samKeep.Value && R.B(R.F(__instance, "_triggerShutDown")) && R.B(R.P(__instance, "IsHot")) && Idle(__instance))
                    R.SetF(__instance, "_triggerShutDown", false);   // the game's "idle: unload the rails" waits while the launcher is hot
                if (!has) return;
                var ws = __instance;
                if (!R.B(R.P(ws, "IsHot"))) { _warmBox.Remove(ws); return; }
                double now = MissionTime();
                int loaded = Loaded(ws);
                if (IsEngaging(ws)) { b.Pending = false; b.Until = loaded > 0 ? now + WarmSeconds() : 0.0; return; }
                if (R.B(R.F(ws, "_isReloading")) || !Idle(ws)) return;
                if (b.Pending)
                {
                    var rails = Rails(ws);
                    if (loaded < rails.Count && MagazineCount(ws) > 0)
                    {
                        if (R.F(ws, "_ammoForEngage") == null) R.SetF(ws, "_ammoForEngage", PickAmmo(ws));
                        var ae = R.F(ws, "_ammoForEngage");
                        if (ae != null) { R.Call(ws, "triggerLoadSystem"); if (!Idle(ws) || R.B(R.F(ws, "_isReloading"))) return; }
                    }
                    b.Pending = false;
                    b.Until = Loaded(ws) > 0 ? now + WarmSeconds() : 0.0;
                    return;
                }
                if (loaded == 0) { b.Until = 0.0; return; }      // launched: the count waits for the next load
                if (b.Until <= 0.0) { b.Until = now + WarmSeconds(); return; }
                if (now >= b.Until) StartUnload(ws, false);
            }
            catch { }
        }

        static object Magazine(object ws) { return R.F(R.F(ws, "_vwp"), "_associatedMagazine"); }

        static int MagazineCount(object ws)
        {
            try
            {
                var mag = Magazine(ws); if (mag == null) return 0;
                var list = R.F(mag, "_ammunitionList") as IList; if (list == null) return 0;
                int n = 0;
                foreach (var a in list) n += R.I(R.Call(mag, "getAmmunitionCount", R.S(R.F(R.F(a, "_ap"), "_ammunitionFileName"))));
                return n;
            }
            catch { return 0; }
        }

        /// <summary>An ammunition of the launcher's magazine that is left and fits the launcher (the first; the position filter is a preference).</summary>
        static object PickAmmo(object ws)
        {
            try
            {
                var mag = Magazine(ws);
                var list = R.F(mag, "_ammunitionList") as IList;
                if (list == null) return null;
                var names = new List<string> { R.S(R.F(ws, "_systemName")), R.S(R.F(R.F(ws, "_vwp"), "_systemName")) };
                object any = null;
                foreach (var a in list)
                {
                    var ap = R.F(a, "_ap");
                    if (ap == null) continue;
                    string file = R.S(R.F(ap, "_ammunitionFileName"));
                    int left = 0;
                    try { left = R.I(R.Call(mag, "getAmmunitionCount", file)); } catch { }
                    if (left <= 0) continue;
                    if (any == null) any = a;
                    var lp = R.F(ap, "_launcherPositions");
                    foreach (var n in names)
                        if (!string.IsNullOrEmpty(n) && lp != null && R.B(R.Call(lp, "ContainsKey", n))) return a;
                }
                return any;
            }
            catch { }
            return null;
        }

        /// <summary>The rails are unloaded into the magazine at once; the launcher cools down.</summary>
        /// <summary>The game's own unloading (the missiles go back with the animation); then charge again (recharge) or cool down.</summary>
        static void StartUnload(object ws, bool recharge)
        {
            Box b;
            if (!_warmBox.TryGetValue(ws, out b)) { b = new Box(); _warmBox.Add(ws, b); }
            b.Pending = false; b.Until = 0.0; b.Unload = true; b.Recharge = recharge; b.Started = false;
        }

        static void Discharge(object ws)
        {
            var cs = R.F(ws, "_containers") as IList;
            if (cs != null) foreach (var c in cs) { try { R.Call(c, "unload", true); R.Call(c, "ClearPartialPack"); } catch { } }
            R.SetF(ws, "_ammunitionOnWeapon", null); R.SetF(ws, "_spawnedAmmunition", null);
            try { var st = R.F(ws, "_state"); if (st != null) R.SetF(ws, "_state", Enum.Parse(st.GetType(), "Idle")); } catch { }
            R.SetP(ws, "IsHot", false);
        }

        /// <summary>The launcher can be charged (a SAM / missile launcher with a warm-up and a magazine).</summary>
        static bool CanWarm(object ws)
        {
            try
            {
                if (ws == null || !R.Is(ws, _tLauncher)) return false;
                var vwp = R.F(ws, "_vwp");
                return vwp != null && R.B(R.F(vwp, "_hasWarmUp")) && R.B(R.F(vwp, "_hasWarmUpAmmo")) && R.F(vwp, "_associatedMagazine") != null;
            }
            catch { return false; }
        }

        internal sealed class WarmInfo { public int St; public List<bool> Rails; public float Left = -1f; }

        /// <summary>0 grey (nothing loaded), 1 loading, 2 partly loaded, 3 ready (all rails). Left: seconds of the time limit, -1 none.</summary>
        static WarmInfo WarmState(object ws)
        {
            var wi = new WarmInfo { Rails = Rails(ws) };
            int loaded = 0; foreach (var b in wi.Rails) if (b) loaded++;
            bool hot = R.B(R.P(ws, "IsHot"));
            Box bx; bool has = _warmBox.TryGetValue(ws, out bx);
            if (has && bx.Unload) wi.St = 1;
            else if (loaded > 0 && loaded >= wi.Rails.Count) wi.St = 3;
            else if (loaded > 0) wi.St = 2;
            else if (hot && (has && bx.Pending || R.B(R.F(ws, "_isReloading")) || !Idle(ws))) wi.St = 1;
            if (has && bx.Until > 0.0 && loaded > 0) wi.Left = (float)Math.Max(0.0, bx.Until - MissionTime());
            return wi;
        }

        /// <summary>Click: charge, or unload and cool down (not during a fight).</summary>
        static void WarmToggle(object ws)
        {
            try
            {
                var wi = WarmState(ws);
                if (wi.St == 0) { Charge(ws); return; }
                if (IsEngaging(ws)) return;
                StartUnload(ws, false);
            }
            catch (Exception e) { Trace("warm: " + e.Message); }
        }

        static void Charge(object ws)
        {
            bool hotBefore = R.B(R.P(ws, "IsHot"));
            if (!hotBefore) R.Call(ws, "WarmUp");
            R.SetP(ws, "IsHot", true);
            Box b;
            if (!_warmBox.TryGetValue(ws, out b)) { b = new Box(); _warmBox.Add(ws, b); }
            b.Pending = true; b.Until = 0.0;
        }

        /// <summary>Reload: what is on the rails goes back, then the launcher is charged again.</summary>
        static void Recharge(object ws)
        {
            try
            {
                if (IsEngaging(ws)) return;
                StartUnload(ws, true);
            }
            catch (Exception e) { Trace("recharge: " + e.Message); }
        }

        // ------------------------------------------------------------------ load / unload animation
        // The animations (MoveMissileUp / Down) move the missiles between the ammunition's LoadedPosition and UnloadedPosition for the
        // launcher's system name ([Launchers] in the ammunition file). When the name is not there (SA-N-1A launcher, SA-N-1 entry),
        // the game uses zeros: the missiles do not move at all. The nearest entry is given to the animation instead.
        static void AnimLoadPostfix(object __instance) { FixAnim(__instance, true); }
        static void AnimUnloadPostfix(object __instance) { FixAnim(__instance, false); }

        static void FixAnim(object ws, bool load)
        {
            try
            {
                if (_inst == null || _inst._samAnim == null || !_inst._samAnim.Value) return;
                var anim = R.F(ws, load ? "_loadAmmunitionAnimation" : "_unloadAmmunitionAnimation");
                if (anim == null) return;
                var ammo = load ? (R.F(ws, "_spawnedAmmunition") ?? R.F(ws, "_ammunitionOnWeapon")) : (R.F(ws, "_ammunitionOnWeapon") ?? R.F(ws, "_spawnedAmmunition"));
                var lp = R.F(R.F(ammo, "_ap"), "_launcherPositions") as IDictionary;
                string sys = R.S(R.F(R.F(ws, "_vwp"), "_systemName"));
                if (lp == null || lp.Count == 0) return;
                if (lp.Contains(sys)) return;                       // the game has it
                object best = null; int bestLen = -1;
                foreach (DictionaryEntry de in lp)
                {
                    var k = de.Key as string; if (k == null) continue;
                    int n = 0;
                    if (sys.StartsWith(k, StringComparison.OrdinalIgnoreCase)) n = k.Length;
                    else if (k.StartsWith(sys, StringComparison.OrdinalIgnoreCase)) n = sys.Length;
                    if (n > bestLen) { bestLen = n; best = de.Value; }
                }
                if (best == null && lp.Count == 1) foreach (DictionaryEntry de in lp) best = de.Value;
                if (best == null) return;
                R.Call(anim, "setAmmunitionLoaderPositions", best);
            }
            catch (Exception e) { Trace("anim fix: " + e.Message); }
        }

        // ------------------------------------------------------------------ automatic charging
        static readonly ConditionalWeakTable<object, Box> _autoNext = new ConditionalWeakTable<object, Box>();
        const float NmPerUnit = 0.036285132f;

        /// <summary>
        /// A unit (any side) with SAM launchers: an unknown aircraft, already processed by the unit's OODA loop (the AI's
        /// _processedObjects), closer than the range and closing in, charges the launchers allowed against aircraft.
        /// </summary>
        static void AutoChargePostfix(object __instance)
        {
            try
            {
                var me = _inst;
                if (me == null || me._samAuto == null || !me._samAuto.Value || !Enabled) return;
                double now = MissionTime();
                Box nb;
                if (!_autoNext.TryGetValue(__instance, out nb)) { nb = new Box(); _autoNext.Add(__instance, nb); nb.Until = now + 1.0; }
                if (now < nb.Until) return;
                nb.Until = now + 3.0;
                var bo = R.F(__instance, "_baseObject");
                if (bo == null || R.B(R.P(bo, "IsDestroyed"))) return;
                var ws = R.F(bo, "_weaponStatus"); if (ws != null && ws.ToString() == "Hold") return;
                var procs = R.F(__instance, "_processedObjects") as IList; if (procs == null || procs.Count == 0) return;
                var launchers = new List<Launcher>();
                foreach (var l in CachedLaunchers(bo, W(bo)))
                    if ((l.Domains & DAir) != 0 && CanWarm(l.Ws) && State(bo, l, DAir) != Chip.Off && WarmState(l.Ws).St == 0 && !R.B(R.P(l.Ws, "IsHot"))) launchers.Add(l);
                if (launchers.Count == 0) return;
                if (!UnknownAirClosing(__instance, bo, procs, me._samAutoRange.Value)) return;
                foreach (var l in launchers) Charge(l.Ws);
            }
            catch (Exception e) { Trace("auto charge: " + e.Message); }
        }

        static bool UnknownAirClosing(object ai, object bo, IList procs, float rangeNm)
        {
            var tf = R.V(bo, "_taskforce");
            var table = R.V(R.V(tf, "PlottingTable"), "LocalVehicles");
            var myPos = (UnityEngine.Vector3)R.V(R.V(bo, "transform"), "position");
            foreach (var o in procs)
            {
                if (o == null || R.B(R.V(o, "IsDestroyed")) || !R.B(R.V(o, "IsAirUnit"))) continue;
                object veh = null;
                try { var args = new object[] { o, null }; if (table != null && (bool)table.GetType().GetMethod("TryGetValue").Invoke(table, args)) veh = args[1]; } catch { }
                if (veh == null) continue;                               // not on the plot: nothing known about it
                var rel = R.Call(veh, "CurrentRelationship");
                string rs = rel == null ? "" : rel.ToString();
                if (rs == "Friendly" || rs == "Neutral") continue;        // identified as not a threat
                var pos = (UnityEngine.Vector3)R.V(R.V(o, "transform"), "position");
                var d = pos - myPos; d.y = 0f;
                float nm = d.magnitude * NmPerUnit;
                var vel = R.V(o, "_velocityVecInUnity");
                bool closing = vel is UnityEngine.Vector3 && UnityEngine.Vector3.Dot((UnityEngine.Vector3)vel, d) < 0f;
                if (nm <= rangeNm && closing) return true;
            }
            return false;
        }
    }
}
