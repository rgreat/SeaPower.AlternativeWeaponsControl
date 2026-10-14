using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace AlternativeWeaponsControl
{
    /// <summary>
    /// The Weapons tab, the AI side: a launcher the player turned off for a target domain is not used AUTOMATICALLY against it.
    ///  - automatic = inside AI.OnUpdate / AI.OnFixedUpdate (auto attack, air defence, guns in range, cued missiles, submarine
    ///    volleys: they all pick their launchers through WeaponSelector.GetWeaponSystemsForAmmunition, so an ammunition type
    ///    with no allowed launcher is not chosen), or inside ObjectBase.HandleEngageTasks for an engage task the AI made
    ///    (_isAutoAttack); the player's own orders are never filtered;
    ///  - CIWS pick their targets themselves (WeaponSystemCIWS.AquireTarget): targets of a domain turned off are not "in the
    ///    firing arc" for that mount while it looks (WeaponSystem.targetIsInFiringArc), and a current one is dropped.
    /// The Standing Orders part is the game's own (the merged switches change the unit's orders), not filtered here.
    /// </summary>
    public partial class AwcPlugin
    {
        [ThreadStatic] static int _aiDepth;
        [ThreadStatic] static object _hetUnit;
        [ThreadStatic] static object _ciws;
        [ThreadStatic] static HashSet<Vector3> _ciwsSkip;

        void HookWeapons(Harmony harmony, Assembly asm)
        {
            const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            InitWeaponTypes(asm);
            int n = 0;
            Action<MethodBase, string, string, string> patch = (m, pre, post, fin) =>
            {
                if (m == null) return;
                harmony.Patch(m, prefix: pre == null ? null : Hm(pre), postfix: post == null ? null : Hm(post), finalizer: fin == null ? null : Hm(fin));
                n++;
            };
            var ai = asm.GetType("SeaPower.AI");
            patch(ai == null ? null : ai.GetMethod("OnUpdate", inst, null, Type.EmptyTypes, null), "AiEnter", null, "AiLeave");
            patch(ai == null ? null : ai.GetMethod("OnFixedUpdate", inst, null, Type.EmptyTypes, null), "AiEnter", null, "AiLeave");
            patch(_tObjectBase == null ? null : _tObjectBase.GetMethod("HandleEngageTasks", inst, null, Type.EmptyTypes, null), "HetEnter", null, "HetLeave");
            var sel = asm.GetType("SeaPower.WeaponSelector");
            MethodInfo gw = null;
            if (sel != null) foreach (var m in sel.GetMethods(BindingFlags.Static | BindingFlags.Public)) if (m.Name == "GetWeaponSystemsForAmmunition" && m.GetParameters().Length == 4) gw = m;
            patch(gw, null, "SelectorPostfix", null);
            patch(_tCiws == null ? null : _tCiws.GetMethod("AquireTarget", inst | BindingFlags.DeclaredOnly), "CiwsEnter", null, "CiwsLeave");
            var ws = asm.GetType("SeaPower.WeaponSystem");
            patch(ws == null ? null : ws.GetMethod("targetIsInFiringArc", inst, null, new[] { typeof(Vector3) }, null), null, "ArcPostfix", null);
            var ib = _tObjectBase == null ? null : _tObjectBase.Assembly.GetType("SeaPower.IniHandler");
            if (ib != null)
            {
                patch(_tObjectBase.GetMethod("SaveStateToFile", inst | BindingFlags.DeclaredOnly), null, "UnitSavePostfix", null);
                patch(_tObjectBase.GetMethod("LoadStateFromFile", inst | BindingFlags.DeclaredOnly), null, "UnitLoadPostfix", null);
            }
            var ast = asm.GetType("SeapowerUI.ViewModels.AttackingState");
            var aam = ast == null ? null : ast.GetMethod("AddAttackTask", inst);
            if (aam != null) harmony.Patch(aam, postfix: Hm("AddAttackPostfix")); else Trace("AttackingState.AddAttackTask not found: no Fire for one launcher");
            var at = asm.GetType("SeaPower.AttackTask");
            var ex = at == null ? null : at.GetMethod("OnExecute", inst | BindingFlags.DeclaredOnly);
            if (ex != null) harmony.Patch(ex, postfix: Hm("AttackExecPostfix")); else Trace("AttackTask.OnExecute not found: no Fire for one launcher");
            var eet = ws == null ? null : ws.GetMethod("EndEngageTask", inst, null, new[] { typeof(bool) }, null);
            if (eet != null) harmony.Patch(eet, prefix: Hm("EndEngagePrefix")); else Trace("WeaponSystem.EndEngageTask not found: a launcher's Fire may break off other mounts");
            _tRadar = asm.GetType("SeaPower.SensorSystemRadar");
            var gun = asm.GetType("SeaPower.WeaponSystemGun");
            var gfc = gun == null ? null : gun.GetMethod("GetFireControlSensor", inst, null, Type.EmptyTypes, null);
            var apg = gun == null ? null : gun.GetMethod("activatePreLaunchGuidance", inst | BindingFlags.DeclaredOnly);
            if (apg != null) harmony.Patch(apg, prefix: Hm("GunGuidePrefix"), postfix: Hm("GunGuidePostfix")); else Trace("WeaponSystemGun.activatePreLaunchGuidance not found: GunOneDirector does nothing");
            if (gfc != null) harmony.Patch(gfc, postfix: Hm("GunFcPostfix")); else Trace("WeaponSystemGun.GetFireControlSensor not found: GunChannelLimit does nothing");
            if (n < 8) Logger.LogWarning("Weapons tab: " + (8 - n) + " AI / save hook(s) not found (game updated?); the launcher switches may not be applied or saved.");
            Trace("patched weapons AI / save hooks: " + n);
        }

        static void AiEnter() { _aiDepth++; }
        static void AiLeave() { if (_aiDepth > 0) _aiDepth--; }
        // ------------------------------------------------------------------ a target for one launcher
        // The Weapons tab's Fire of one launcher: until the game's target picking for that unit and ammunition ends (and 3 s
        // after), the player's new engage tasks for it are tied to that launcher (Pins); while the game hands them out
        // (HandleEngageTasks) only that launcher is offered, and others that took the same task (guns, linked launchers) drop it.
        public sealed class PendingPin { public object Unit, Ws; public List<string> Ammos; public double Since; public float EndAt = -1f; public bool Seen; }
        public static PendingPin Pending;
        sealed class PinBox { public object Ws; }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, PinBox> Pins = new System.Runtime.CompilerServices.ConditionalWeakTable<object, PinBox>();

        public static double MissionTime()
        {
            try { var gt = _tObjectBase.Assembly.GetType("SeaPower.GameTime"); return (double)gt.GetProperty("missionElapsedTime").GetValue(null, null); } catch { return 0.0; }
        }

        static object PinOf(object task) { PinBox b; return task != null && Pins.TryGetValue(task, out b) ? b.Ws : null; }

        // The player's order (AttackTask in the unit's order list) is made at the click - also while the game is paused - and
        // turns into an engage task only when it runs: the launcher is tied to the order (AttackingState.AddAttackTask postfix:
        // the newest order of the unit, while a launcher's Fire is pending) and passed on to the engage task it makes
        // (AttackTask.OnExecute postfix: the unit's newest task for that ammunition and target).
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, PinBox> OrderPins = new System.Runtime.CompilerServices.ConditionalWeakTable<object, PinBox>();

        static void AddAttackPostfix(object attacker)
        {
            if (!Enabled) return;
            var pp = Pending;
            if (pp == null || !ReferenceEquals(pp.Unit, attacker)) return;
            try
            {
                var nodes = R.Call(R.F(attacker, "_userRoot"), "GetChildNodes") as IEnumerable;
                if (nodes == null) return;
                foreach (var node in nodes)
                {
                    var task = R.V(node, "task");
                    PinBox had;
                    if (task != null && task.GetType().Name == "AttackTask" && pp.Ammos.Contains(R.S(R.P(R.V(task, "ammunitionForEngage"), "value"))) && !OrderPins.TryGetValue(task, out had))
                        OrderPins.Add(task, new PinBox { Ws = pp.Ws });
                    break;   // the order just added is the first
                }
            }
            catch (Exception e) { Trace("weapons pin order: " + e.Message); }
        }

        static void AttackExecPostfix(object __instance)
        {
            if (!Enabled) return;
            PinBox b;
            if (__instance == null || !OrderPins.TryGetValue(__instance, out b)) return;
            try
            {
                var unit = R.F(__instance, "_baseObject");
                var ammo = R.S(R.P(R.V(__instance, "ammunitionForEngage"), "value"));
                var target = R.P(R.V(__instance, "targetObject"), "value");
                var tasks = R.F(unit, "_currentEngageTasks") as IList;
                if (tasks == null) return;
                for (int i = tasks.Count - 1; i >= 0; i--)
                {
                    var t = tasks[i];
                    if (t == null || PinOf(t) != null || R.S(R.F(t, "_ammoId")) != ammo || (target != null && !ReferenceEquals(R.F(t, "_targetObject"), target))) continue;
                    Pins.Add(t, new PinBox { Ws = b.Ws });
                    PinRecs.Add(new PinRec { Task = t, Unit = unit, Ws = b.Ws, Ammo = ammo, Target = R.F(t, "_targetObject"),
                                             Loaded0 = Loaded(b.Ws, new List<string> { ammo }) });
                    break;
                }
            }
            catch (Exception e) { Trace("weapons pin task: " + e.Message); }
        }

        public static bool IsPinnedTo(object task, object ws) { return ReferenceEquals(PinOf(task), ws); }

        // what a mount was doing before the distribution: the game hands a new task to EVERY gun with the ammunition (and CIWS,
        // linked launchers), overwriting their engagement; for a task tied to another launcher it is put back afterwards
        static readonly string[] EngageFields = { "_takenEngageTask", "_executingEngageTask", "_engageTaskPriority", "_ammoForEngage", "_isTargetObjectRequired",
                                                  "_alignmentUID", "_engageStartTime", "_engageDelay", "_targetObject", "_currentTargetPoint", "_targetPosition",
                                                  "_isAutoEngaging", "_ignoreIfUndetected", "_autoEngageTask", "_offsetVector" };
        [ThreadStatic] static Dictionary<object, object[]> _before;

        static void HetEnter(object __instance)
        {
            if (!Enabled) return;
            _hetUnit = __instance;
            _hetPin = null;
            if (_before != null) _before.Clear();
            try
            {
                var tasks = R.F(__instance, "_currentEngageTasks") as IList;
                if (tasks == null) return;
                bool pinned = false;
                foreach (var t in tasks) if (t != null && PinOf(t) != null) { pinned = true; break; }
                if (!pinned) return;
                var all = R.F(R.F(__instance, "_obp"), "_weaponSystems") as IList;
                if (all == null) return;
                if (_before == null) _before = new Dictionary<object, object[]>();
                foreach (var ws in all)
                {
                    if (ws == null) continue;
                    var vals = new object[EngageFields.Length];
                    for (int i = 0; i < vals.Length; i++) vals[i] = R.F(ws, EngageFields[i]);
                    _before[ws] = vals;
                }
            }
            catch (Exception e) { Trace("weapons pin: " + e.Message); }
        }
        // after the distribution: the game also hands an engage task to every gun / CIWS mount with the same ammunition (and to
        // linked launchers) without asking the selector - an automatic task on a mount turned off for its target is dropped again
        static void HetLeave(object __instance)
        {
            _hetUnit = null;
            _hetPin = null;
            if (!Enabled) return;
            try
            {
                // other mounts that the game gave a task tied to another launcher (all guns with the ammunition, linked launchers)
                var obp = R.F(__instance, "_obp");
                var all = R.F(obp, "_weaponSystems") as IList;
                if (all != null)
                    foreach (var ws in all)
                    {
                        if (ws == null || !R.B(R.F(ws, "_executingEngageTask"))) continue;
                        var taken = R.F(ws, "_takenEngageTask");
                        var pin = PinOf(taken);
                        if (pin == null || ReferenceEquals(pin, ws)) continue;
                        object[] was;
                        if (_before != null && _before.TryGetValue(ws, out was) && !ReferenceEquals(was[0], taken) && R.B(was[1]))
                        {
                            // it was engaging something else: back to that
                            for (int i = 0; i < EngageFields.Length; i++) if (R.F(ws, EngageFields[i]) != null || was[i] != null) R.SetF(ws, EngageFields[i], was[i]);
                        }
                        else R.Call(ws, "EndEngageTask", true);
                    }
            }
            catch (Exception e) { Trace("weapons pin tasks: " + e.Message); }
            if (_before != null) _before.Clear();
            UpdatePins(__instance);
            try
            {
                UnitW w;
                if (__instance == null || !UnitsW.TryGetValue(__instance, out w) || w.Off.Count == 0) return;
                CachedLaunchers(__instance, w);
                foreach (var kv in w.IndexOf)
                {
                    int off;
                    if (!w.Off.TryGetValue(kv.Value, out off)) continue;
                    var ws = kv.Key;
                    if (!R.B(R.F(ws, "_executingEngageTask"))) continue;
                    var task = R.F(ws, "_takenEngageTask");
                    if (task == null || !R.B(R.F(task, "_isAutoAttack"))) continue;
                    var tgt = R.F(task, "_targetObject");
                    if (tgt == null || (off & TargetDomain(tgt)) == 0) continue;
                    R.Call(ws, "EndEngageTask", true);
                }
            }
            catch (Exception e) { Trace("weapons tasks: " + e.Message); }
        }

        // ------------------------------------------------------------------ waiting for a launcher / a fire-control channel
        // A player's task tied to one launcher waits while that launcher is busy (queued: the game keeps the task) or while
        // none of its directors has a channel for the target (the launcher holds the task and waits itself) - until it
        // fires; no time limit (Cease fire drops it). Guns never wait: the game aims them without a free channel.
        // The game, finding no free mount for a player's order, breaks off other mounts' engagements for it
        // (HandleEngageTasks: guns, and launchers when the target is a missile): not for a task tied to one launcher.
        public sealed class PinRec { public object Task, Unit, Ws, Target; public string Ammo; public int Loaded0; public int Wait; }
        public static readonly List<PinRec> PinRecs = new List<PinRec>();
        [ThreadStatic] static object _hetPin;
        static Type _tRadar;

        static bool EndEngagePrefix(object __instance)
        {
            if (!Enabled) return true;
            if (_hetPin == null || _hetUnit == null || ReferenceEquals(__instance, _hetPin)) return true;
            if (!ReferenceEquals(R.F(__instance, "_baseObject"), _hetUnit)) return true;
            return false;
        }

        /// <summary>The waiting task of a launcher: 1 queued (the launcher is busy), 2 waiting for a fire-control channel; null when none.</summary>
        public static PinRec PinWait(object ws)
        {
            foreach (var r in PinRecs) if (ReferenceEquals(r.Ws, ws) && r.Wait != 0) return r;
            return null;
        }

        static readonly string[] FiringStates = { "LaunchingSalvo", "Launch", "Firing" };

        static void UpdatePins(object unit)
        {
            if (PinRecs.Count == 0) return;
            try
            {
                var tasks = R.F(unit, "_currentEngageTasks") as IList;
                for (int i = PinRecs.Count - 1; i >= 0; i--)
                {
                    var r = PinRecs[i];
                    if (!ReferenceEquals(r.Unit, unit)) continue;
                    bool inList = tasks != null && tasks.Contains(r.Task);
                    bool taken = R.B(R.F(r.Ws, "_executingEngageTask")) && ReferenceEquals(R.F(r.Ws, "_takenEngageTask"), r.Task);
                    if (!inList && !taken) { PinRecs.RemoveAt(i); continue; }
                    if (taken && (Loaded(r.Ws, new List<string> { r.Ammo }) < r.Loaded0 || Array.IndexOf(FiringStates, R.S(R.F(r.Ws, "_engageState"))) >= 0))
                    { PinRecs.RemoveAt(i); continue; }   // it fires: no longer waiting
                    r.Wait = !taken ? 1 : NoChannel(unit, r.Ws, R.F(r.Task, "_ammunition"), r.Target) ? 2 : 0;
                }
            }
            catch (Exception e) { Trace("weapons pin wait: " + e.Message); }
        }

        // ------------------------------------------------------------------ guns and fire-control channels (GunChannelLimit)
        // The game aims a gun with its best working director (FireControl.ResolveBestDirector) whether or not that director
        // took the gun's target (WeaponSystemGun.activatePreLaunchGuidance -> TryToAddTarget, once per engagement). With the
        // option: only a director holding the target counts; else the gun tries to get a channel (every call, so it picks one
        // up when it frees) and, failing, aims on local control (null: Globals._gunLocalControlGainRate, darkness factor).
        static bool GunChannelsOn() { var a = Active; return a != null && a._gunChannels != null && a._gunChannels.Value; }

        public static bool Working(object s)
        {
            return s != null && !R.B(R.Rv(R.P(s, "Inoperable"))) && R.B(R.Rv(R.P(s, "IsOn"))) && R.B(R.P(s, "CanProduceContacts"));
        }

        public static bool Holds(object s, object target) { return target != null && R.B(R.Call(s, "IsAimedAtTarget", target)); }

        // GunOneDirector: WeaponSystemGun.activatePreLaunchGuidance takes the target on EVERY director of the gun with a free
        // channel; afterwards only one keeps it - one that held it before, else the best of the new ones (radar first, then
        // the higher _fcSolutionGainRate, as FireControl.ResolveBestDirector) - the others get their channel and state back.
        sealed class DirWas { public object S; public bool Held, Guiding, Rest; public object Tgt, Mode; }
        [ThreadStatic] static List<DirWas> _dirWas;

        static void GunGuidePrefix(object __instance, object targetObject)
        {
            if (_dirWas != null) _dirWas.Clear();
            var a = Active;
            if (a == null || a._gunOneDirector == null || !a._gunOneDirector.Value || targetObject == null) return;
            try
            {
                var sens = R.F(R.F(__instance, "_vwp"), "_associatedSensors") as IList;
                if (sens == null || sens.Count < 2) return;
                if (_dirWas == null) _dirWas = new List<DirWas>();
                foreach (var s in sens)
                {
                    if (s == null) continue;
                    var at = R.F(s, "_associatedTargets") as IList;
                    _dirWas.Add(new DirWas { S = s, Held = at != null && at.Contains(targetObject), Guiding = R.B(R.F(s, "_isGuiding")),
                                             Rest = R.B(R.F(s, "_restPositionReached")), Tgt = R.F(s, "_targetObject"), Mode = R.F(s, "_currentShootingMode") });
                }
            }
            catch (Exception e) { Trace("weapons gun director: " + e.Message); }
        }

        static void GunGuidePostfix(object targetObject)
        {
            if (_dirWas == null || _dirWas.Count == 0) return;
            try
            {
                DirWas keep = null;
                foreach (var d in _dirWas) if (d.Held) { keep = d; break; }
                var fresh = new List<DirWas>();
                foreach (var d in _dirWas)
                {
                    var at = R.F(d.S, "_associatedTargets") as IList;
                    if (!d.Held && at != null && at.Contains(targetObject)) fresh.Add(d);
                }
                if (keep == null)
                    foreach (var d in fresh)
                    {
                        if (keep == null) { keep = d; continue; }
                        bool r = R.Is(d.S, _tRadar), kr = R.Is(keep.S, _tRadar);
                        if (r != kr ? r : R.Fl(R.F(d.S, "_fcSolutionGainRate")) > R.Fl(R.F(keep.S, "_fcSolutionGainRate"))) keep = d;
                    }
                foreach (var d in fresh)
                {
                    if (d == keep) continue;
                    var at = R.F(d.S, "_associatedTargets") as IList;
                    at.Remove(targetObject);
                    R.SetF(d.S, "_isGuiding", d.Guiding);
                    R.SetF(d.S, "_restPositionReached", d.Rest);
                    R.SetF(d.S, "_targetObject", d.Tgt);
                    R.SetF(d.S, "_currentShootingMode", d.Mode);
                }
            }
            catch (Exception e) { Trace("weapons gun director: " + e.Message); }
            _dirWas.Clear();
        }

        static void GunFcPostfix(object __instance, ref object __result)
        {
            if (__result == null || !GunChannelsOn()) return;
            try
            {
                var tgt = R.F(__instance, "_targetObject");
                if (tgt == null || !(R.B(R.F(__instance, "_executingEngageTask")) || R.B(R.F(__instance, "_isAutoEngaging")))) return;
                if (Holds(__result, tgt)) return;
                var sens = R.F(R.F(__instance, "_vwp"), "_associatedSensors") as IList;
                if (sens == null) return;
                foreach (var s in sens) if (Working(s) && Holds(s, tgt)) { __result = s; return; }
                foreach (var s in sens)
                    if (Working(s) && R.B(R.Call(s, "TryToAddTarget", tgt)))
                    {
                        R.SetF(s, "_isGuiding", true);
                        R.SetF(s, "_restPositionReached", false);
                        R.SetF(s, "_targetObject", tgt);
                        __result = s;
                        return;
                    }
                __result = null;
            }
            catch (Exception e) { Trace("weapons gun FC: " + e.Message); }
        }

        /// <summary>The ammunition needs a director for this target and none of the launcher's directors has a channel for it.</summary>
        public static bool NoChannel(object unit, object ws, object ammo, object target)
        {
            if (target == null || ammo == null) return false;
            var wt = R.S(R.F(ws, "_weaponType"));
            if (wt == "Gun" || wt == "CIWS") return false;   // guns fire with a busy director, or on local control
            var ap = R.F(ammo, "_ap");
            if (!R.B(R.Call(unit, "DoesEngageRequiresSensor", ap, target))) return false;
            var sens = R.F(R.F(ws, "_vwp"), "_associatedSensors") as IList;
            if (sens == null || sens.Count == 0) return false;
            foreach (var s in sens)
            {
                if (s == null || R.B(R.Rv(R.P(s, "Inoperable"))) || !R.B(R.Call(s, "CanGuideAmmo", ap))) continue;
                if (!R.Is(s, _tRadar)) return false;   // sonar, optics: no channels
                if (!R.B(R.Rv(R.P(s, "IsOn"))) && !R.B(R.F(R.F(ws, "_vwp"), "_autoEnableSensor"))) continue;
                if (ReferenceEquals(R.F(s, "_preLaunchTargetObject"), target)) return false;
                if ((R.B(R.Call(s, "HasFreeTargetChannels")) || R.B(R.Call(s, "IsAimedAtTarget", target))) && R.B(R.Call(s, "HasFreeWeaponChannels"))) return false;
            }
            return true;
        }

        /// <summary>Engage tasks of the unit for this ammunition and target: all made by the AI (and at least one)?</summary>
        static bool TasksAuto(object unit, string ammoId, object target)
        {
            var tasks = R.F(unit, "_currentEngageTasks") as IList;
            if (tasks == null) return false;
            bool found = false;
            foreach (var t in tasks)
            {
                if (t == null || R.S(R.F(t, "_ammoId")) != ammoId || !ReferenceEquals(R.F(t, "_targetObject"), target)) continue;
                if (!R.B(R.F(t, "_isAutoAttack"))) return false;
                found = true;
            }
            return found;
        }

        static void SelectorPostfix(object obj, string ammoId, object targetObject, object __result)
        {
            if (!Enabled) return;
            var list = __result as IList;
            if (obj != null && ReferenceEquals(_hetUnit, obj)) _hetPin = null;
            if (list == null || list.Count == 0 || obj == null) return;
            if (ReferenceEquals(_hetUnit, obj))
            {
                // a player's task tied to one launcher: only that one
                try
                {
                    var tasks = R.F(obj, "_currentEngageTasks") as IList;
                    object pin = null;
                    if (tasks != null)
                        foreach (var t in tasks)
                        {
                            if (t == null || R.S(R.F(t, "_ammoId")) != ammoId || !ReferenceEquals(R.F(t, "_targetObject"), targetObject)) continue;
                            pin = PinOf(t);
                            if (pin != null) break;
                        }
                    _hetPin = pin;
                    if (pin != null)
                    {
                        for (int i = list.Count - 1; i >= 0; i--) if (!ReferenceEquals(list[i], pin)) list.RemoveAt(i);
                        return;
                    }
                }
                catch (Exception e) { Trace("weapons pin selector: " + e.Message); }
            }
            if (targetObject == null) return;
            UnitW w;
            if (!UnitsW.TryGetValue(obj, out w) || w.Off.Count == 0) return;
            try
            {
                bool auto = _aiDepth > 0 || (ReferenceEquals(_hetUnit, obj) && TasksAuto(obj, ammoId, targetObject));
                if (!auto) return;
                SyncDoctrine(obj, w);
                if (w.Off.Count == 0) return;
                CachedLaunchers(obj, w);
                int d = TargetDomain(targetObject);
                if (d == 0) return;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    int idx, off;
                    if (list[i] != null && w.IndexOf.TryGetValue(list[i], out idx) && w.Off.TryGetValue(idx, out off) && (off & d) != 0) list.RemoveAt(i);
                }
            }
            catch (Exception e) { Trace("weapons selector: " + e.Message); }
        }

        static void CiwsEnter(object __instance)
        {
            if (!Enabled) return;
            _ciws = null;
            try
            {
                var unit = R.F(__instance, "_baseObject");
                UnitW w;
                if (unit == null || !UnitsW.TryGetValue(unit, out w) || w.Off.Count == 0) return;
                SyncDoctrine(unit, w);
                CachedLaunchers(unit, w);
                int idx, off;
                if (!w.IndexOf.TryGetValue(__instance, out idx) || !w.Off.TryGetValue(idx, out off) || off == 0) return;
                var cur = R.F(__instance, "_currentClosestTarget");
                if (cur != null && (off & TargetDomain(cur)) != 0) R.SetF(__instance, "_currentClosestTarget", null);
                var skip = _ciwsSkip ?? (_ciwsSkip = new HashSet<Vector3>());
                skip.Clear();
                Action<object> add = o =>
                {
                    var c = o as Component;
                    if (c != null && (off & TargetDomain(o)) != 0) skip.Add(c.transform.position);
                };
                var threats = R.F(unit, "_threats") as IEnumerable;
                if (threats != null) foreach (var t in threats) add(t);
                var pt = R.P(R.F(unit, "_taskforce"), "PlottingTable");
                var vs = R.P(pt, "Vehicles") as IEnumerable;
                if (vs != null) foreach (var v in vs) add(R.P(v, "BaseObject"));
                var lv = R.P(pt, "LocalVehicles") as IDictionary;
                if (lv != null) foreach (var k in lv.Keys) add(k);
                if (skip.Count > 0) _ciws = __instance;
            }
            catch (Exception e) { Trace("weapons CIWS: " + e.Message); }
        }

        static void CiwsLeave() { _ciws = null; }

        static void ArcPostfix(object __instance, Vector3 interceptPosition, ref bool __result)
        {
            if (__result && _ciws != null && ReferenceEquals(__instance, _ciws) && _ciwsSkip.Contains(interceptPosition)) __result = false;
        }
    }
}
