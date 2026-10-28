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
    /// the game's load system puts the missiles onto the rails (triggerLoadSystem), then WarmUp() (IsHot + the reload time) - the warm-up
    /// runs on the rails, as in the game's own order of a fire order to a cold launcher (and as the real launcher does it).
    /// The time limit starts when the warm-up is over; when it ends and the launcher was not used, the missiles go back to the
    /// magazine and the launcher cools down. A launch (rails empty) stops the count; it starts again when the rails are loaded again.
    /// Reload on such a launcher = unload the rails, charge again.
    /// </summary>
    public partial class AwcPlugin
    {
        // One launcher under our control. Until: end of the time limit of a ready launcher. Pending: the rails are to be loaded. Warm: after the load the warm-up
        // (the reload count) is to start - on the loaded rails. Unload/Started: our own unloading. Recharge: charge again after it. Btn: the button is ON (a demand).
        sealed class Box { public double Until, NeedAt; public bool Pending, Unload, Recharge, Started, Warm, Btn, Need, Real; }
        static readonly ConditionalWeakTable<object, Box> _warmBox = new ConditionalWeakTable<object, Box>();

        void HookWarmUp(Harmony harmony, Assembly asm)
        {
            var t = asm.GetType("SeaPower.WeaponSystemLauncher");
            var m = t == null ? null : t.GetMethod("OnUpdate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
            if (m == null) { Trace("WeaponSystemLauncher.OnUpdate not found: no SAM ready time limit"); return; }
            harmony.Patch(m, postfix: Hm("WarmPostfix"));
            var lm = t == null ? null : t.GetMethod("launch", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (lm != null) harmony.Patch(lm, postfix: Hm("LaunchPostfix")); else Trace("WeaponSystemLauncher.launch not found: the reload after the last missile is not skipped");
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

        /// <summary>The state the game's own status does not tell: "Rails empty" (cold, missiles in the magazine), "Loading", "Unloading"; null: the game's text.</summary>
        internal static string SamStatus(object ws)
        {
            try
            {
                if (!CanWarm(ws) || R.B(R.F(ws, "_isReloading")) || IsEngaging(ws)) return null;
                Box b; bool has = _warmBox.TryGetValue(ws, out b);
                if (has && b.Unload) return Loc.T("st.unloading");
                double left = PauseLeft(ws);
                if (left > 0.5) { int sec = (int)Math.Ceiling(left); return Loc.F("st.unloadPause", (sec / 60).ToString("00") + ":" + (sec % 60).ToString("00")); }   // the pause after an unloading
                if (has && b.Pending) return Loc.T("st.loading");
                if (Idle(ws) && Loaded(ws) == 0 && MagazineCount(ws) > 0) return Loc.T("st.railsEmpty");
            }
            catch { }
            return null;
        }

        // The pause after an unloading is its own block: it starts when a real unloading ends (the Missile button, the Reload button, the time limit)
        // and only holds the next load back. Unloading and loading do not depend on each other, they only wait for each other to end.
        sealed class PauseBox { public double End; }
        static readonly ConditionalWeakTable<object, PauseBox> _pause = new ConditionalWeakTable<object, PauseBox>();
        static double PauseEnd(object ws) { PauseBox p; return _pause.TryGetValue(ws, out p) ? p.End : 0.0; }
        static double PauseLeft(object ws) { return PauseEnd(ws) - MissionTime(); }
        static void StartPause(object ws) { _pause.Remove(ws); _pause.Add(ws, new PauseBox { End = MissionTime() + LoadPause(ws) }); }

        /// <summary>The launcher is unloading, in the pause after it, or loading: for the weapons list this is a reload (the same category and colour).</summary>
        internal static bool SamBusy(object ws)
        {
            try
            {
                if (!CanWarm(ws) || IsEngaging(ws)) return false;
                Box b; if (_warmBox.TryGetValue(ws, out b) && (b.Unload || b.Pending)) return true;
                return PauseLeft(ws) > 0.5;
            }
            catch { return false; }
        }

        static int Loaded(object ws) { int n = 0; foreach (var b in Rails(ws)) if (b) n++; return n; }

        // ------------------------------------------------------------------ the model
        // Cold: nothing on the rails. Loading / warming: the missiles go onto the rails, then the warm-up (the game's reload count) runs ON them.
        // Ready: loaded, warm, idle. "Hot" means "warm missiles on the rails": a hot launcher with empty rails is cold. The demand (hot missiles are needed)
        // is one thing: the button is ON, or an open order with a live target, or an unknown aircraft closing in. One function (Reconcile) moves a launcher
        // between the states; it is called by the launcher's update (about 4 times a second, for the launchers under control only) and after a launch.

        /// <summary>The demand for hot missiles (the need is asked once a second).</summary>
        static bool Demand(object ws, Box b, double now, bool fresh)
        {
            if (b == null) return NeedHot(ws);
            if (b.Btn) return true;
            if (fresh || now >= b.NeedAt)
            {
                b.Need = NeedHot(ws);
                b.NeedAt = now + 1.0;
            }
            return b.Need;
        }

        static void ResetReload(object ws) { R.SetF(ws, "_isReloading", false); R.SetF(ws, "_remainingReloadTime", 0f); R.SetF(ws, "_reloadStartTime", -1000.0); }

        /// <summary>Cold: no reload count, not hot (the box goes with it).</summary>
        static void GoCold(object ws)
        {
            // the reload clock is reset too: the game's load flow (InitiateLoading) sets _isReloading again and counts from the LAST StartReload, so the count
            // of the launch would run once more during the load, and the warm-up on the rails after it (two counts in a row)
            ResetReload(ws); R.SetP(ws, "IsHot", false);
            _warmBox.Remove(ws);
        }

        /// <summary>After a launch the game counts a reload when the rails are empty. A count on empty rails is not a warm-up: it is dropped at once; with a
        /// demand the rails are loaded now and the warm-up runs on them, without one the launcher is cold.</summary>
        static void LaunchPostfix(object __instance)
        {
            try
            {
                var ws = __instance;
                if (_debug && Loaded(ws) == 0) TraceOrders(ws, "launch: rails empty");
                if (!R.B(R.F(ws, "_isReloading")) || !CanWarm(ws) || !Idle(ws) || Loaded(ws) != 0) return;
                Box b; _warmBox.TryGetValue(ws, out b);
                if (b != null && b.Btn) { b.Btn = false; if (_debug) Trace("sam button: ready ON dropped, the last missile is gone " + SamName(ws)); }   // the button holds the missiles on the rails, not the reloading
                bool need = Demand(ws, b, MissionTime(), true);
                GoCold(ws);
                if (need && MagazineCount(ws) > 0) Charge(ws, "launch: demand");
            }
            catch (Exception e) { Trace("launch postfix: " + e.Message); }
        }

        /// <summary>Reconcile, from the launcher's update.</summary>
        static void WarmPostfix(object __instance)
        {
            try
            {
                var ws = __instance;
                Box b;
                bool has = _warmBox.TryGetValue(ws, out b);
                if (!has && !CanWarm(ws)) return;   // tubes, guns, launchers without a warm-up: nothing here (this runs for every launcher every frame)
                if (_debug) WarmTrace(ws, has ? b : null);
                if (has && b.Unload) { UnloadStep(ws, b); return; }
                if (_inst != null && _inst._samKeep != null && _inst._samKeep.Value && R.B(R.F(ws, "_triggerShutDown")) && R.B(R.P(ws, "IsHot")) && Idle(ws))
                    R.SetF(ws, "_triggerShutDown", false);   // the game's "idle: unload the rails" waits while the launcher is hot
                int tick = UnityEngine.Time.frameCount + RuntimeHelpers.GetHashCode(ws);
                if (has ? !b.Pending && tick % 15 != 0 : tick % 30 != 0) return;   // about 4 (2 for a launcher not yet taken) times a second
                bool hot = R.B(R.P(ws, "IsHot"));
                if (!has)
                {
                    if (!hot) return;                    // cold and not asked: the game's business
                    b = new Box(); _warmBox.Add(ws, b);   // the game made it hot (an order, a known hostile, the mission start): it is under our rules now
                }
                else if (!hot && !b.Pending) { _warmBox.Remove(ws); return; }
                double now = MissionTime();
                int loaded = Loaded(ws);
                bool reloading = R.B(R.F(ws, "_isReloading"));
                if (b.Btn && loaded == 0 && !b.Pending && !b.Unload && !IsEngaging(ws))
                {
                    b.Btn = false;   // empty rails and no load asked for: the button's demand is over (what loads them again is a real demand only)
                    if (_debug) Trace("sam button: ready ON dropped, rails empty " + SamName(ws));
                }
                if (IsEngaging(ws))
                {
                    if (loaded > 0) b.Until = now + WarmSeconds();   // in use: the limit counts from the last use
                    if (!b.Pending)
                    {
                        // the game's order flow of a cold launcher counts first (on empty rails) and loads the rails after the count. The agreed order: the load
                        // animation opens the reload, the count runs on / after it. The load is started now; the count goes on (the game does the same on a repeated salvo).
                        if (reloading && loaded == 0 && Idle(ws) && R.F(ws, "_ammoForEngage") != null && MagazineCount(ws) > 0) R.Call(ws, "triggerLoadSystem");
                        return;                                  // the rest is the game's own flow of the order
                    }
                }
                if (reloading)
                {
                    // a count on empty rails is not a warm-up (see LaunchPostfix); the demand is asked fresh: the game starts such a count for its own prep / order
                    if (loaded == 0 && Idle(ws) && !IsEngaging(ws) && !b.Pending)
                    {
                        bool wanted = Demand(ws, b, now, true);
                        if (_debug && !wanted) Trace("sam cancel: a count on empty rails without a demand (" + R.S(R.F(ws, "_systemName")) + ")");
                        GoCold(ws);
                        if (wanted && MagazineCount(ws) > 0) Charge(ws, "count on empty rails: demand");
                    }
                    else if (b.Pending && now >= PauseEnd(ws) && loaded == 0 && Idle(ws) && MagazineCount(ws) > 0)
                    {
                        // the rails are to be loaded but a count is still running (the old one after our unloading, or the game's own warm-up on the empty rails):
                        // the load animation must open the reload, so it starts now and the count goes on
                        if (R.F(ws, "_ammoForEngage") == null) R.SetF(ws, "_ammoForEngage", PickAmmo(ws));
                        if (R.F(ws, "_ammoForEngage") != null) R.Call(ws, "triggerLoadSystem");
                    }
                    return;
                }
                bool need = Demand(ws, b, now, false);
                if (!Idle(ws)) return;
                if (b.Pending)
                {
                    if (now < PauseEnd(ws)) return;   // the pause after an unloading
                    var rails = Rails(ws);
                    if (loaded < rails.Count && MagazineCount(ws) > 0)
                    {
                        if (R.F(ws, "_ammoForEngage") == null) R.SetF(ws, "_ammoForEngage", PickAmmo(ws));
                        var ae = R.F(ws, "_ammoForEngage");
                        if (ae != null) { R.Call(ws, "triggerLoadSystem"); if (!Idle(ws) || R.B(R.F(ws, "_isReloading"))) return; }
                    }
                    // the rails are loaded: the warm-up runs on them, the time limit starts when it is over
                    if (b.Warm && Loaded(ws) > 0 && !R.B(R.P(ws, "IsHot"))) { b.Warm = false; R.Call(ws, "WarmUp"); return; }
                    b.Warm = false; b.Pending = false;
                    if (Loaded(ws) > 0) b.Until = now + WarmSeconds(); else _warmBox.Remove(ws);
                    return;
                }
                if (loaded == 0)
                {
                    // hot with empty rails is cold; with a demand the rails are loaded and warmed
                    if (hot) R.SetP(ws, "IsHot", false);
                    if (need && MagazineCount(ws) > 0) { if (_debug) Trace("sam charge " + SamName(ws) + " src=hot with empty rails: demand"); b.Pending = true; b.Warm = true; b.Until = 0.0; }
                    else _warmBox.Remove(ws);
                    return;
                }
                if (b.Until <= 0.0) { b.Until = now + WarmSeconds(); return; }
                if (now >= b.Until)
                {
                    // the limit is over: the missiles go back; with a demand (not the button: it ends here) they are loaded and warmed again
                    b.Btn = false;
                    StartUnload(ws, Demand(ws, b, now, true));
                }
            }
            catch { }
        }

        /// <summary>Our own unloading (the game's animated one); when it is done: charge again when asked or needed, else cold.</summary>
        static void UnloadStep(object ws, Box b)
        {
            if (IsEngaging(ws)) { b.Unload = false; b.Recharge = false; b.Started = false; R.SetF(ws, "_triggerShutDown", false); return; }
            if (!b.Started)
            {
                // the game stops its load flow while the shut-down flag is up: a load animation in progress would freeze for good; wait until the launcher is free
                if (!Idle(ws)) return;
                b.Started = true;
                b.Real = Loaded(ws) > 0 || R.F(ws, "_ammunitionOnWeapon") != null;   // an unloading of empty rails is no unloading: no pause after it
                // rails loaded without the game's load system have no "ammunition on the weapon": the unload animation needs it
                if (R.F(ws, "_ammunitionOnWeapon") == null)
                {
                    var cs0 = R.F(ws, "_containers") as IList;
                    if (cs0 != null) foreach (var c0 in cs0) { var la = R.F(c0, "_loadedAmmunition"); var w0 = R.F(c0, "_weapons") as IList; if (la != null && w0 != null && w0.Count > 0) { R.SetF(ws, "_ammunitionOnWeapon", la); break; } }
                }
                R.SetF(ws, "_triggerShutDown", true);
                return;
            }
            if (!R.B(R.F(ws, "_triggerShutDown")) && Idle(ws))
            {
                bool again = b.Recharge || NeedHot(ws);
                if (b.Real) StartPause(ws);   // the missiles stay down for a while before they go up again
                b.Unload = false; b.Started = false; b.Recharge = false; b.Real = false;
                R.SetP(ws, "IsHot", false);
                if (again && MagazineCount(ws) > 0) Charge(ws, "after unload");
                else _warmBox.Remove(ws);
            }
        }

        /// <summary>The pause between the end of an unloading and the next load: half of the launcher's reload cycle.</summary>
        static double LoadPause(object ws)
        {
            try { return Math.Max(0.0, R.Fl(R.F(R.F(ws, "_vwp"), "_magazineReloadTime")) * 0.5); } catch { return 0.0; }
        }

        /// <summary>
        /// Hot missiles are needed: the unit has open engage orders for the launcher's ammunition (a live target), or an unknown / hostile
        /// aircraft is coming in within [Fire] SamAutoChargeRange (the same test as the automatic charging).
        /// </summary>
        /// <summary>Debug: the unit's open engage orders for this launcher's ammunition (what makes NeedHot true).</summary>
        static void TraceOrders(object ws, string why)
        {
            try
            {
                var bo = R.F(ws, "_baseObject");
                var tasks = R.F(bo, "_currentEngageTasks") as IList;
                var sb = new System.Text.StringBuilder();
                if (tasks != null)
                    foreach (var t in tasks)
                    {
                        if (t == null) continue;
                        var tg = R.F(t, "_targetObject");
                        sb.Append(" [uid " + R.I(R.F(t, "_uid")) + " " + R.S(R.F(t, "_ammoId")) + " shots=" + R.I(R.F(t, "_shotsToFire")) + " target=" + (tg == null ? "-" : R.S(R.Call(tg, "getUIDAndName"))) + (tg != null && R.B(R.P(tg, "IsDestroyed")) ? " destroyed" : "") + "]");
                    }
                var taken = R.F(ws, "_takenEngageTask");
                Trace("sam orders (" + why + ") " + R.S(R.F(ws, "_systemName")) + ": list=" + (tasks == null ? 0 : tasks.Count) + sb + " taken=" + (taken == null ? "none" : "uid " + R.I(R.F(taken, "_uid")) + " shots=" + R.I(R.F(taken, "_shotsToFire"))) + " needHot=" + NeedHot(ws));
            }
            catch (Exception e) { Trace("sam orders: " + e.Message); }
        }

        /// <summary>The live target of an open engage order for this launcher's ammunition (null: none).</summary>
        static object EngageTarget(object ws)
        {
            try
            {
                var bo = R.F(ws, "_baseObject");
                var mag = Magazine(ws);
                var list = mag == null ? null : R.F(mag, "_ammunitionList") as IList;
                var names = new List<string>();
                if (list != null) foreach (var a in list) { var n = R.S(R.F(R.F(a, "_ap"), "_ammunitionFileName")); if (n != null) names.Add(n); }
                var tasks = bo == null ? null : R.F(bo, "_currentEngageTasks") as IList;
                if (tasks != null)
                    foreach (var t in tasks)
                    {
                        if (t == null || !names.Contains(R.S(R.F(t, "_ammoId")))) continue;
                        var tg = R.F(t, "_targetObject");
                        if (tg != null && !R.B(R.P(tg, "IsDestroyed"))) return tg;
                    }
            }
            catch { }
            return null;
        }

        static bool NeedHot(object ws)
        {
            try
            {
                var bo = R.F(ws, "_baseObject");
                if (bo == null || R.B(R.P(bo, "IsDestroyed"))) return false;
                var held = R.F(bo, "_weaponStatus"); if (held != null && held.ToString() == "Hold") return false;
                // the game's own request to ready the launcher (AI.PrepareLaunchersForClosingThreats -> RequestPrep: a target is about to come into range)
                var pAmmo = R.F(ws, "_prepAmmo"); var pTgt = R.F(ws, "_prepTarget");
                if (pAmmo != null && pTgt != null && !R.B(R.P(pTgt, "IsDestroyed"))) return true;
                // the game's AI (AI.cs ~705-726) warms up ALL launchers on every cycle while its list of possible targets holds an air unit or a missile
                // aimed at a non-airborne target (no range test: the list is the contacts known as hostile). That is its own demand for hot missiles.
                var ai0 = R.F(bo, "_ai");
                var pt = ai0 == null ? null : R.F(ai0, "_possibleTargetsWithPriorities") as IDictionary;
                if (pt != null)
                    foreach (DictionaryEntry de in pt)
                    {
                        var k = de.Key;
                        if (k == null || R.B(R.P(k, "IsDestroyed"))) continue;
                        if (R.B(R.V(k, "IsAirUnit"))) return true;
                        if (IsMissileAtSurface(k)) return true;
                    }
                var mag = Magazine(ws);
                var list = mag == null ? null : R.F(mag, "_ammunitionList") as IList;
                var names = new List<string>();
                if (list != null) foreach (var a in list) { var n = R.S(R.F(R.F(a, "_ap"), "_ammunitionFileName")); if (n != null) names.Add(n); }
                var tasks = R.F(bo, "_currentEngageTasks") as IList;
                if (tasks != null)
                    foreach (var t in tasks)
                    {
                        if (t == null || !names.Contains(R.S(R.F(t, "_ammoId")))) continue;
                        var tg = R.F(t, "_targetObject");
                        if (tg != null && !R.B(R.P(tg, "IsDestroyed"))) return true;
                    }
                var me = _inst;
                var ai = R.F(bo, "_ai");
                var procs = ai == null ? null : R.F(ai, "_processedObjects") as IList;
                if (me != null && me._samAutoRange != null && procs != null && procs.Count > 0 && UnknownAirClosing(ai, bo, procs, me._samAutoRange.Value)) return true;
            }
            catch { }
            return false;
        }

        /// <summary>A missile whose target is not airborne (the game's AI warms the launchers up for it too).</summary>
        static bool IsMissileAtSurface(object o)
        {
            for (var ty = o.GetType(); ty != null; ty = ty.BaseType)
                if (ty.Name == "Missile") return !R.B(R.F(o, "_isTargetAirborne"));
            return false;
        }

        // Debug: every change of a SAM launcher's loading state is traced (to find out why the rails load before the reload ends)
        static readonly ConditionalWeakTable<object, string[]> _warmSig = new ConditionalWeakTable<object, string[]>();
        /// <summary>"unit system #n" for the trace lines.</summary>
        static string SamName(object ws)
        {
            try
            {
                int ix = -1; var all = R.F(R.F(R.F(ws, "_baseObject"), "_obp"), "_weaponSystems") as IList; if (all != null) ix = all.IndexOf(ws);
                return R.S(R.Call(R.F(ws, "_baseObject"), "getUIDAndName")) + " " + R.S(R.F(ws, "_systemName")) + " #" + ix;
            }
            catch { return "?"; }
        }

        static void WarmTrace(object ws, Box b)
        {
            try
            {
                var st = R.F(ws, "_state");
                string sig = (st == null ? "?" : st.ToString()) + " reloading=" + R.B(R.F(ws, "_isReloading")) + " left=" + (R.Fl(R.F(ws, "_remainingReloadTime")) > 0f ? "yes" : "no")
                           + " hot=" + R.B(R.P(ws, "IsHot")) + " weapons=" + Loaded(ws) + "/" + Rails(ws).Count + " onWeapon=" + (R.F(ws, "_ammunitionOnWeapon") != null)
                           + " engaging=" + IsEngaging(ws) + " shutDown=" + R.B(R.F(ws, "_triggerShutDown"))
                           + (b == null ? " box=none" : " box(pending=" + b.Pending + " unload=" + b.Unload + " recharge=" + b.Recharge + " btn=" + b.Btn + ")");
                string[] last;
                if (!_warmSig.TryGetValue(ws, out last)) { last = new string[1]; _warmSig.Add(ws, last); }
                if (last[0] == sig) return;
                last[0] = sig;
                int ix = -1; try { var all = R.F(R.F(R.F(ws, "_baseObject"), "_obp"), "_weaponSystems") as IList; if (all != null) ix = all.IndexOf(ws); } catch { }
                Trace("sam load " + R.S(R.Call(R.F(ws, "_baseObject"), "getUIDAndName")) + " " + R.S(R.F(ws, "_systemName")) + " #" + ix + ": " + sig + " t=" + MissionTime().ToString("0.0"));
                if (IsEngaging(ws) || Loaded(ws) == 0) TraceOrders(ws, "state change");
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
        sealed class Flag { public bool V; }
        static readonly ConditionalWeakTable<object, Flag> _canWarm = new ConditionalWeakTable<object, Flag>();   // what a launcher is does not change: asked once

        static bool CanWarm(object ws)
        {
            if (ws == null) return false;
            Flag f;
            if (_canWarm.TryGetValue(ws, out f)) return f.V;
            bool r = false, known = false;
            try
            {
                if (R.Is(ws, _tLauncher))
                {
                    var vwp = R.F(ws, "_vwp");
                    if (vwp != null) { known = true; r = R.B(R.F(vwp, "_hasWarmUp")) && R.B(R.F(vwp, "_hasWarmUpAmmo")) && R.F(vwp, "_associatedMagazine") != null; }
                }
                else known = true;
            }
            catch { }
            if (known) _canWarm.Add(ws, new Flag { V = r });
            return r;
        }

        internal sealed class WarmInfo { public int St; public List<bool> Rails; public float Left = -1f; }

        /// <summary>0 grey (nothing loaded), 1 loading, 2 partly loaded, 3 ready (all rails). Left: seconds of the time limit, -1 none.</summary>
        static WarmInfo WarmState(object ws)
        {
            var wi = new WarmInfo { Rails = Rails(ws) };
            int loaded = 0; foreach (var b in wi.Rails) if (b) loaded++;
            bool hot = R.B(R.P(ws, "IsHot"));
            Box bx; bool has = _warmBox.TryGetValue(ws, out bx);
            // the rails count the missiles the game has SPAWNED: the load animation spawns them at its start, and a reload can still be counting -
            // not ready until the load state machine is idle and no reload is counting
            bool busy = (hot || has && bx.Pending) && loaded > 0 && (has && bx.Pending || !Idle(ws) || R.B(R.F(ws, "_isReloading")));
            if (has && bx.Unload) wi.St = 1;
            else if (busy && loaded >= wi.Rails.Count) { wi.St = 1; for (int i = 0; i < wi.Rails.Count; i++) wi.Rails[i] = false; }
            else if (loaded > 0 && loaded >= wi.Rails.Count) wi.St = 3;
            else if (loaded > 0) wi.St = 2;
            else if (has && bx.Pending || hot && (R.B(R.F(ws, "_isReloading")) || !Idle(ws))) wi.St = 1;
            if (has && bx.Until > 0.0 && loaded > 0) wi.Left = (float)Math.Max(0.0, bx.Until - MissionTime());
            return wi;
        }

        /// <summary>Click: ON charges and is a demand (until the time limit ends); OFF is a single unload (a demand that is still there charges again).</summary>
        static void WarmToggle(object ws)
        {
            try
            {
                var wi = WarmState(ws);
                if (wi.St == 0) { if (_debug) Trace("sam button: ready ON " + SamName(ws)); Charge(ws, "button"); Box b; if (_warmBox.TryGetValue(ws, out b)) b.Btn = true; return; }
                if (IsEngaging(ws)) { if (_debug) Trace("sam button: ready OFF ignored, engaging " + SamName(ws)); return; }
                if (_debug) Trace("sam button: ready OFF " + SamName(ws));
                Box bx; if (_warmBox.TryGetValue(ws, out bx)) bx.Btn = false;
                StartUnload(ws, false);
            }
            catch (Exception e) { Trace("warm: " + e.Message); }
        }

        /// <summary>The missiles go onto the rails; the warm-up (the reload count) runs on the rails.</summary>
        static void Charge(object ws, string src = null)
        {
            if (_debug) Trace("sam charge " + SamName(ws) + " src=" + (src ?? "?"));
            bool hotBefore = R.B(R.P(ws, "IsHot"));
            Box b;
            if (!_warmBox.TryGetValue(ws, out b)) { b = new Box(); _warmBox.Add(ws, b); }
            b.Pending = true; b.Until = 0.0; b.Warm = !hotBefore;
        }

        /// <summary>The launcher is unloading, loading (the pause before it, the animation) or counting the warm-up: the Reload button is off for it.</summary>
        internal static bool ReloadBusy(object ws)
        {
            try
            {
                Box b; if (_warmBox.TryGetValue(ws, out b) && (b.Unload || b.Pending)) return true;
                return R.B(R.F(ws, "_isReloading")) || !Idle(ws) || R.B(R.F(ws, "_triggerShutDown"));
            }
            catch { return false; }
        }

        /// <summary>Reload button: what is on the rails goes back, then the launcher is charged again (the button is ON).</summary>
        static void Recharge(object ws)
        {
            try
            {
                if (IsEngaging(ws)) { if (_debug) Trace("sam button: Reload ignored, engaging " + SamName(ws)); return; }
                if (ReloadBusy(ws)) { if (_debug) Trace("sam button: Reload ignored, busy " + SamName(ws)); return; }   // a reload (or a separate unload / load) is already going: the button is grey, a press must not restart anything
                bool onRails = Loaded(ws) > 0 || R.F(ws, "_ammunitionOnWeapon") != null;
                if (_debug) Trace("sam button: Reload " + SamName(ws) + (onRails ? " (unload, then load)" : " (rails empty: load)"));
                if (onRails) StartUnload(ws, true); else if (MagazineCount(ws) > 0) Charge(ws, "Reload button");   // empty rails: nothing to unload, the load goes at once
                Box b; if (_warmBox.TryGetValue(ws, out b)) b.Btn = true;
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
                if (_debug) Trace("auto charge: " + R.S(R.Call(bo, "getUIDAndName")) + " launchers=" + launchers.Count);
                foreach (var l in launchers) Charge(l.Ws, "auto-charge");
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
                try { var td = table as System.Collections.IDictionary; if (td != null && td.Contains(o)) veh = td[o]; } catch { }
                if (veh == null) continue;                               // not on the plot: nothing known about it
                var rel = R.Call(veh, "CurrentRelationship");
                string rs = rel == null ? "" : rel.ToString();
                if (rs == "Friendly" || rs == "Neutral" || rs == "Hostile") continue;   // identified: not a threat, or a hostile one - as in the game, its engage order does the warm-up; this is for the unknown ones
                var pos = (UnityEngine.Vector3)R.V(R.V(o, "transform"), "position");
                var d = pos - myPos; d.y = 0f;                          // from the ship to the aircraft
                float nm = d.magnitude * NmPerUnit;
                if (nm > rangeNm) continue;
                // Charging is NOT engaging (its own setting: SamAutoChargeRange, nothing of the "On Free" settings): the range to the aircraft shrinks, i.e. its
                // velocity RELATIVE to the ship points towards the ship (a slower aircraft on a parallel course does not close)
                var vel = R.V(o, "_velocityVecInUnity");
                if (!(vel is UnityEngine.Vector3)) continue;
                var rv = (UnityEngine.Vector3)vel;
                var mv = R.V(bo, "_velocityVecInUnity");
                if (mv is UnityEngine.Vector3) rv -= (UnityEngine.Vector3)mv;
                rv.y = 0f;
                if (UnityEngine.Vector3.Dot(rv, d) >= 0f) continue;
                if (_debug) { try { TraceLimited("demand" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(bo) + "/" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o), 10f, "sam demand: aircraft " + R.S(R.Call(o, "getUIDAndName")) + " " + rs + " " + nm.ToString("0.0") + " nm, closing at " + (rv.magnitude / 0.0076554087f).ToString("0") + " kn"); } catch { } }   // once per 10 s per ship and aircraft
                return true;
            }
            return false;
        }
    }
}
