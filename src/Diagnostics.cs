using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace AlternativeWeaponsControl
{
    /// <summary>
    /// Debug probes (only with [General] Debug) for two open questions, written to the trace file:
    ///  - machine guns showing their burst only every other shot: each shot of a gun whose effect is a "*burst*" emitter -
    ///    the effect instance the pool gave (new or reused), its particle system state right after the spawn;
    ///  - silent hit explosions of gun rounds: each effect a projectile spawns on a hit - the prefab and every AudioSource
    ///    in it (clip, enabled, play on awake, playing, volume, distance).
    /// Unity's particle and audio modules are not referenced: read by reflection.
    /// </summary>
    public partial class AwcPlugin
    {
        [ThreadStatic] static object _probeBarrel;      // GunBarrel in LaunchProjectile
        [ThreadStatic] static object _probeProjectile;  // Projectile in ProcessCollision
        static readonly HashSet<int> _probeSeen = new HashSet<int>();
        static Type _tPs, _tAudio;
        static float _probeNext;
        static int _probeLines;

        void HookDiagnostics(Harmony harmony, Assembly asm)
        {
            const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _tPs = Type.GetType("UnityEngine.ParticleSystem, UnityEngine.ParticleSystemModule");
            _tAudio = Type.GetType("UnityEngine.AudioSource, UnityEngine.AudioModule");
            var gb = asm.GetType("SeaPower.GunBarrel");
            var lp = gb == null ? null : gb.GetMethod("LaunchProjectile", inst);
            if (lp != null) harmony.Patch(lp, prefix: Hm("ProbeBarrelEnter"), finalizer: Hm("ProbeBarrelLeave"));
            Type pp = null;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) { pp = a.GetType("PathologicalGames.PrefabPool"); if (pp != null) break; }
            var si = pp == null ? null : pp.GetMethod("SpawnInstance", inst);
            if (si != null) harmony.Patch(si, postfix: Hm("ProbePoolSpawn"));
            var pr = asm.GetType("SeaPower.Projectile");
            var pc = pr == null ? null : pr.GetMethod("ProcessCollision", inst);
            if (pc != null) harmony.Patch(pc, prefix: Hm("ProbeHitEnter"), finalizer: Hm("ProbeHitLeave"));
            var ci = asm.GetType("SeaPower.CIWS");
            var sf = ci == null ? null : ci.GetMethod("startFire", inst);
            if (sf != null) harmony.Patch(sf, postfix: Hm("ProbeStartFire"));
            var xf = ci == null ? null : ci.GetMethod("stopFire", inst);
            if (xf != null) harmony.Patch(xf, prefix: Hm("ProbeStopFire"));
            var cu = ci == null ? null : ci.GetMethod("update", inst);
            if (cu != null) harmony.Patch(cu, postfix: Hm("ProbeCiwsUpdate"));
            Trace("diagnostics: CIWS update " + (cu != null));
            Trace("diagnostics: CIWS startFire " + (sf != null) + ", stopFire " + (xf != null));
            var fi = pr == null ? null : pr.GetMethod("fire", inst);
            if (fi != null) harmony.Patch(fi, postfix: Hm("ProbeFire"));
            var fu = pr == null ? null : pr.GetMethod("OnFixedUpdate", inst);
            if (fu != null) harmony.Patch(fu, postfix: Hm("ProbeTick"));
            var em = asm.GetType("SeaPower.EffectsManager");
            var se = em == null ? null : em.GetMethod("spawnEffect", inst);
            if (se != null) harmony.Patch(se, postfix: Hm("ProbeEffect"));
            Trace("diagnostics: barrel " + (lp != null) + ", pool " + (si != null) + ", hit " + (pc != null) + ", effect " + (se != null)
                  + "; audio config " + AudioConfig());
        }

        static string AudioConfig()
        {
            try
            {
                var s = Type.GetType("UnityEngine.AudioSettings, UnityEngine.AudioModule");
                var c = s == null ? null : s.GetMethod("GetConfiguration", BindingFlags.Static | BindingFlags.Public).Invoke(null, null);
                return c == null ? "?" : "real voices " + R.F(c, "numRealVoices") + ", virtual " + R.F(c, "numVirtualVoices");
            }
            catch (Exception e) { return e.Message; }
        }

        static bool ProbeOn() { return (_debug && _probeLines < 4000) || FixOn(true) || FixOn(false); }
        static bool FixOn(bool burst)
        {
            var a = Active; if (a == null || !Enabled) return false;
            var e = burst ? a._mgBurstFix : a._hitSoundFix;
            return e != null && e.Value;
        }

        static void ProbeBarrelEnter(object __instance) { if (ProbeOn()) _probeBarrel = __instance; }
        static Exception ProbeBarrelLeave(Exception __exception) { _probeBarrel = null; return __exception; }
        static void ProbeHitEnter(object __instance) { if (ProbeOn()) _probeProjectile = __instance; }
        static Exception ProbeHitLeave(Exception __exception) { _probeProjectile = null; return __exception; }

        /// <summary>A pooled instance spawned during a gun barrel's shot: the muzzle / burst effect.</summary>
        static void ProbePoolSpawn(Transform __result)
        {
            var b = _probeBarrel;
            if (b == null || __result == null) return;
            try
            {
                var ws = R.F(b, "_vwsg");
                var name = __result.gameObject.name;
                if (name.IndexOf("burst", StringComparison.OrdinalIgnoreCase) < 0) return;   // machine-gun burst emitters only
                int id = __result.gameObject.GetInstanceID();
                bool reused = !_probeSeen.Add(id);
                if (FixOn(true) && _tPs != null)
                    foreach (var p in __result.gameObject.GetComponentsInChildren(_tPs, true))
                    {
                        R.Call(p, "Clear", true);
                        R.Call(p, "Play", true);
                    }
                if (!_debug) return;
                var sb = new StringBuilder("MG shot ");
                sb.Append(R.S(R.F(R.F(ws, "_vwp"), "_systemName"))).Append(" t=").Append(Time.time.ToString("0.000"))
                  .Append(" frame ").Append(Time.frameCount).Append(": ").Append(name).Append(" #").Append(id).Append(reused ? " reused" : " new");
                Ps(sb, __result.gameObject);
                Line(sb.ToString());
            }
            catch (Exception e) { Line("MG probe: " + e.Message); }
        }

        static void Ps(StringBuilder sb, GameObject go)
        {
            if (_tPs == null) return;
            var all = go.GetComponentsInChildren(_tPs, true);
            int playing = 0, alive = 0, parts = 0;
            foreach (var p in all)
            {
                if (R.B(R.P(p, "isPlaying"))) playing++;
                try { if (R.B(R.Call(p, "IsAlive"))) alive++; } catch { }
                parts += R.I(R.P(p, "particleCount"));
            }
            foreach (var p in all)
            {
                try
                {
                    var m = R.P(p, "main");
                    sb.Append(" {").Append(((Component)p).gameObject.name).Append(": duration ").Append(R.Fl(R.P(m, "duration")).ToString("0.00"))
                      .Append(" loop ").Append(R.B(R.P(m, "loop"))).Append(" lifetime ").Append(R.Fl(R.P(R.P(m, "startLifetime"), "constantMax")).ToString("0.00"))
                      .Append(" time ").Append(R.Fl(R.P(p, "time")).ToString("0.00")).Append(" simSpace ").Append(R.P(m, "simulationSpace")).Append("}");
                }
                catch { }
            }
            if (_tAudio != null)
                foreach (var a in go.GetComponentsInChildren(_tAudio, true))
                {
                    var clip = R.P(a, "clip");
                    sb.Append(" {audio ").Append(clip == null ? "none" : ((UnityEngine.Object)clip).name).Append(" length ")
                      .Append(clip == null ? "-" : R.Fl(R.P(clip, "length")).ToString("0.00")).Append(" playing ").Append(R.B(R.P(a, "isPlaying"))).Append("}");
                }
            sb.Append("; particle systems ").Append(all.Length).Append(", playing ").Append(playing).Append(", alive ").Append(alive)
              .Append(", particles ").Append(parts).Append(", active ").Append(go.activeInHierarchy);
        }

        /// <summary>An effect spawned while a projectile processes a hit: its audio.</summary>
        static void ProbeEffect(object ep, GameObject __result)
        {
            DumpEffectAudio();
            var pr = _probeProjectile;
            if (pr == null) return;
            try
            {
                if (FixOn(false) && __result != null && _tAudio != null) HitSound(__result);
                if (!_debug) return;
                var eff = R.F(ep, "_effect");
                var sb = new StringBuilder("hit effect of ");
                sb.Append(R.S(R.F(R.F(pr, "_pp"), "_ammunitionFileName"))).Append(": ").Append(R.S(R.F(eff, "_name")) ?? "?")
                  .Append(" (class ").Append(R.S(R.F(eff, "_effectClassName"))).Append(", ignored ").Append(R.B(R.F(eff, "_isIgnored"))).Append(")");
                if (_tAudio != null)
                {
                    int pl = 0, all = 0;
                    foreach (var o in UnityEngine.Object.FindObjectsOfType(_tAudio)) { all++; if (R.B(R.P(o, "isPlaying"))) pl++; }
                    sb.Append("; audio sources in scene ").Append(all).Append(", playing ").Append(pl);
                }
                if (__result == null) { Line(sb.Append(" - not spawned").ToString()); return; }
                sb.Append(" -> ").Append(__result.name);
                var cam = Camera.main;
                if (cam != null) sb.Append(", camera ").Append(Vector3.Distance(cam.transform.position, __result.transform.position).ToString("0.###")).Append(" u");
                if (_tAudio == null) { Line(sb.Append("; audio module not found").ToString()); return; }
                var srcs = __result.GetComponentsInChildren(_tAudio, true);
                if (srcs.Length == 0) sb.Append("; NO AudioSource");
                foreach (var a in srcs)
                {
                    var clip = R.P(a, "clip") as UnityEngine.Object;
                    var c = a as Component;
                    sb.Append("; audio '").Append(c == null ? "?" : c.gameObject.name).Append("' clip ").Append(clip == null ? "NONE" : clip.name)
                      .Append(", enabled ").Append(R.B(R.P(a, "enabled"))).Append(", active ").Append(c != null && c.gameObject.activeInHierarchy)
                      .Append(", playOnAwake ").Append(R.B(R.P(a, "playOnAwake"))).Append(", playing ").Append(R.B(R.P(a, "isPlaying")))
                      .Append(", vol ").Append(R.Fl(R.P(a, "volume")).ToString("0.##")).Append(", maxDist ").Append(R.Fl(R.P(a, "maxDistance")).ToString("0.###"))
                      .Append(", mixer ").Append(R.P(a, "outputAudioMixerGroup") is UnityEngine.Object mg && mg != null ? mg.name : "none");
                }
                Line(sb.ToString());
            }
            catch (Exception e) { Line("hit probe: " + e.Message); }
        }

        // ------------------------------------------------------------------ CIWS / machine guns (Type=CIWS)
        // The visible tracers of these weapons are one particle system ("Tracers" child of the weapon effect) with
        // rateOverTime = FireRate / 60 / 5 per second (every 5th round): 270 rpm = 0.9 tracers per second. It is Play()ed at
        // the start of a volley and Stop()ped at its end, so a volley shorter than ~1 s may emit no tracer at all.
        // MachineGunBurstFix: one tracer is emitted at the start of every volley.
        static readonly HashSet<object> _ciwsListed = new HashSet<object>();
        static readonly Dictionary<object, float> _ciwsStart = new Dictionary<object, float>();

        static void ProbeStartFire(object __instance)
        {
            try
            {
                var ps = R.F(__instance, "_tracersStreamPS");
                if (ps == null) return;
                if (_debug && _probeLines < 4000)
                {
                    var m = R.P(ps, "main"); var em = R.P(ps, "emission");
                    _ciwsStart[__instance] = Time.time;
                    var root0 = R.F(__instance, "_tracers") as GameObject;
                    if (root0 != null && _tPs != null && _ciwsListed.Add(__instance))
                        foreach (var cp in root0.GetComponentsInChildren(_tPs, true))
                        {
                            var cm = R.P(cp, "main"); var ce = R.P(cp, "emission");
                            Line("  CIWS effect part '" + ((Component)cp).gameObject.name + "': playing " + R.B(R.P(cp, "isPlaying")) + " duration " + R.Fl(R.P(cm, "duration")).ToString("0.00")
                                + " loop " + R.B(R.P(cm, "loop")) + " rate " + R.Fl(R.P(R.P(ce, "rateOverTime"), "constantMax")).ToString("0.00") + " lifetime " + R.Fl(R.P(R.P(cm, "startLifetime"), "constantMax")).ToString("0.0")
                                + " color " + R.P(R.P(cm, "startColor"), "mode") + " " + R.P(R.P(cm, "startColor"), "colorMax") + " bursts " + R.I(R.P(ce, "burstCount")));
                        }
                    Line("CIWS start " + R.S(R.F(R.F(__instance, "_vwp"), "_systemName")) + " t=" + Time.time.ToString("0.00")
                        + ": tracer rate " + R.Fl(R.P(R.P(em, "rateOverTime"), "constantMax")).ToString("0.00") + "/s, lifetime "
                        + R.Fl(R.P(R.P(m, "startLifetime"), "constantMax")).ToString("0.0") + " s, speed " + R.Fl(R.P(R.P(m, "startSpeed"), "constantMax")).ToString("0.0")
                        + ", duration " + R.Fl(R.P(m, "duration")).ToString("0.00") + " loop " + R.B(R.P(m, "loop")) + ", playing " + R.B(R.P(ps, "isPlaying")) + ", emitting " + R.B(R.P(ps, "isEmitting")) + ", particles " + R.I(R.P(ps, "particleCount")));
                }
                if (FixOn(true))
                {
                    // the tracer system has a fixed duration (1.1 s, not looping): after that it stops emitting although the volley goes on
                    // every other playing, non-looping system of the effect (the white tracers are most likely a sibling system)
                    var root = R.F(__instance, "_tracers") as GameObject;
                    if (root != null && _tPs != null)
                        foreach (var cp in root.GetComponentsInChildren(_tPs, true))
                        {
                            var cm = R.P(cp, "main");
                            if (R.B(R.P(cp, "isPlaying")) && !R.B(R.P(cm, "loop"))) R.SetP(cm, "loop", true);
                        }
                    var mm = R.P(ps, "main");
                    if (!R.B(R.P(mm, "loop"))) R.SetP(mm, "loop", true);
                }
            }
            catch (Exception e) { Line("CIWS start probe: " + e.Message); }
        }

        static readonly Dictionary<object, float> _ciwsNext = new Dictionary<object, float>();

        /// <summary>During a volley: the tracer particle count every 0.2 s.</summary>
        static void ProbeCiwsUpdate(object __instance)
        {
            if (!_debug || _ciwsStart.Count == 0 || !_ciwsStart.ContainsKey(__instance) || _probeLines >= 4000) return;
            float nx;
            if (_ciwsNext.TryGetValue(__instance, out nx) && Time.time < nx) return;
            _ciwsNext[__instance] = Time.time + 0.2f;
            try
            {
                var ps = R.F(__instance, "_tracersStreamPS");
                if (ps == null) return;
                Line("  CIWS +" + (Time.time - _ciwsStart[__instance]).ToString("0.00") + " s: tracer particles " + R.I(R.P(ps, "particleCount"))
                    + ", emitting " + R.B(R.P(ps, "isEmitting")) + ", ps time " + R.Fl(R.P(ps, "time")).ToString("0.00") + CiwsSound(__instance));
            }
            catch (Exception e) { Line("CIWS update probe: " + e.Message); }
        }

        static string CiwsSound(object ciws)
        {
            try
            {
                var snd = R.F(ciws, "_sound");
                if (snd == null) return "; no sound";
                var clip = R.P(snd, "clip");
                return "; sound " + (clip == null ? "none" : ((UnityEngine.Object)clip).name + " len " + R.Fl(R.P(clip, "length")).ToString("0.00"))
                    + " loop " + R.B(R.P(snd, "loop")) + " playing " + R.B(R.P(snd, "isPlaying")) + " virtual " + R.B(R.P(snd, "isVirtual"))
                    + " t " + R.Fl(R.P(snd, "time")).ToString("0.00") + " vol " + R.Fl(R.P(snd, "volume")).ToString("0.00") + " prio " + R.I(R.P(snd, "priority"))
                    + " mute " + R.B(R.P(snd, "mute"));
            }
            catch (Exception e) { return "; sound probe: " + e.Message; }
        }

        static void ProbeStopFire(object __instance)
        {
            if (!_debug || _probeLines >= 4000) return;
            try
            {
                float t0;
                if (!_ciwsStart.TryGetValue(__instance, out t0)) return;
                _ciwsStart.Remove(__instance);
                var ps = R.F(__instance, "_tracersStreamPS");
                Line("CIWS stop " + R.S(R.F(R.F(__instance, "_vwp"), "_systemName")) + " t=" + Time.time.ToString("0.00") + ": volley " + (Time.time - t0).ToString("0.00")
                    + " s, tracer particles alive " + (ps == null ? -1 : R.I(R.P(ps, "particleCount"))));
            }
            catch (Exception e) { Line("CIWS stop probe: " + e.Message); }
        }

        // ------------------------------------------------------------------ tracers (trail effect of a projectile)
        static readonly Dictionary<object, float> _probeDue = new Dictionary<object, float>();
        static readonly Dictionary<object, int> _probeShot = new Dictionary<object, int>();
        static int _shotNo;

        static bool TrailMatch(object pr)
        {
            var f = Active == null || Active._debugFilter == null ? "" : Active._debugFilter.Value;
            if (string.IsNullOrEmpty(f)) return true;
            var pp = R.F(pr, "_pp"); var vwp = R.F(pr, "_vwp");
            return (R.S(R.F(pp, "_ammunitionFileName")) ?? "").IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0
                || (R.S(R.F(vwp, "_systemName")) ?? "").IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static void ProbeFire(object __instance)
        {
            try
            {
                bool fix = FixOn(true);
                if (!fix && !(_debug && _probeLines < 4000)) return;
                var trail = R.F(__instance, "_trailEffect") as GameObject;
                if (fix && trail != null && _tPs != null)
                {
                    // the trail of a pooled projectile must be emitting from the first frame
                    foreach (var p in trail.GetComponentsInChildren(_tPs, true))
                        if (!R.B(R.P(p, "isPlaying"))) R.Call(p, "Play", true);
                    foreach (var t in trail.GetComponentsInChildren<TrailRenderer>(true)) { t.Clear(); t.emitting = true; }
                }
                if (!_debug || _probeLines >= 4000 || !TrailMatch(__instance)) return;
                _probeShot[__instance] = ++_shotNo;
                _probeDue[__instance] = Time.time + 0.15f;
                Line("TRACER #" + _shotNo + " fired " + Describe(__instance, trail, "at fire"));
            }
            catch (Exception e) { Line("fire probe: " + e.Message); }
        }

        static void ProbeTick(object __instance)
        {
            if (!_debug || _probeDue.Count == 0) return;
            float due;
            if (!_probeDue.TryGetValue(__instance, out due) || Time.time < due) return;
            _probeDue.Remove(__instance);
            try { Line("TRACER #" + _probeShot[__instance] + " +0.15s " + Describe(__instance, R.F(__instance, "_trailEffect") as GameObject, "later")); }
            catch (Exception e) { Line("tick probe: " + e.Message); }
        }

        static string Describe(object pr, GameObject trail, string when)
        {
            var sb = new StringBuilder();
            sb.Append(R.S(R.F(R.F(pr, "_pp"), "_ammunitionFileName"))).Append(" t=").Append(Time.time.ToString("0.000"));
            var pi = R.F(pr, "_projectileInstance");
            var obj = R.F(pi, "_object") as GameObject;
            if (obj != null) sb.Append("; projectile active ").Append(obj.activeInHierarchy).Append(" #").Append(obj.GetInstanceID());
            if (trail == null) return sb.Append("; NO TRAIL EFFECT").ToString();
            int id = trail.GetInstanceID();
            sb.Append("; trail ").Append(trail.name).Append(" #").Append(id).Append(_probeSeen.Contains(id) ? " reused" : " new")
              .Append(", active ").Append(trail.activeInHierarchy).Append(", parent ").Append(trail.transform.parent == null ? "none" : trail.transform.parent.name);
            _probeSeen.Add(id);
            Ps(sb, trail);
            if (_tPs != null)
                foreach (var p in trail.GetComponentsInChildren(_tPs, true))
                    sb.Append(" [ps emitting ").Append(R.B(R.P(p, "isEmitting"))).Append(" paused ").Append(R.B(R.P(p, "isPaused"))).Append(" time ").Append(R.Fl(R.P(p, "time")).ToString("0.00")).Append("]");
            foreach (var t in trail.GetComponentsInChildren<TrailRenderer>(true))
                sb.Append(" [trail renderer enabled ").Append(t.enabled).Append(" emitting ").Append(t.emitting).Append(" points ").Append(t.positionCount).Append(" time ").Append(t.time.ToString("0.00")).Append("]");
            var ad = trail.GetComponent("ParticleAutoDestroy");
            if (ad != null) sb.Append(" [autodestroy timer ").Append(R.B(R.F(ad, "_useTimer"))).Append(" ").Append(R.Fl(R.F(ad, "_timer")).ToString("0.0")).Append(" afterDestr ").Append(R.B(R.F(ad, "_useTimerAfterDestruction"))).Append("]");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ hit sound
        // Trace: effects missile_hit_ship_small / _4 have no AudioSource at all, the flak_burst ones do (maxDistance 8.2 units
        // = ~550 m, the camera is often further). With the option such an effect gets a sound of a flak burst, and every hit
        // sound is audible from at least HitSoundMinRange units.
        const float HitSoundMinRange = 50f;
        static readonly List<object> _donors = new List<object>();   // AudioSources of prefabs that have a clip
        static bool _donorsDone;

        static void FindDonors()
        {
            _donorsDone = true;
            var em = UnityEngine.Object.FindObjectOfType(GameAssembly().GetType("SeaPower.EffectsManager"));
            var dict = em == null ? null : R.F(em, "_effectsDictionary") as System.Collections.IDictionary;
            if (dict == null) return;
            foreach (System.Collections.DictionaryEntry de in dict)
            {
                var go = de.Value as GameObject;
                if (go == null) continue;
                foreach (var a in go.GetComponentsInChildren(_tAudio, true))
                    {
                        var cl = R.P(a, "clip") as UnityEngine.Object;
                        if (cl != null && (cl.name.StartsWith("explosion") || cl.name.StartsWith("flak"))) { _donors.Add(a); break; }
                    }
            }
            Trace("hit sound donors: " + _donors.Count);
        }

        static void HitSound(GameObject fx)
        {
            var srcs = fx.GetComponentsInChildren(_tAudio, true);
            if (srcs.Length == 0)
            {
                if (!_donorsDone) FindDonors();
                if (_donors.Count == 0) return;
                var d = _donors[UnityEngine.Random.Range(0, _donors.Count)];
                var n = fx.AddComponent(_tAudio);
                R.SetP(n, "clip", R.P(d, "clip"));
                R.SetP(n, "playOnAwake", false);
                R.SetP(n, "loop", false);
                R.SetP(n, "volume", R.P(d, "volume"));
                R.SetP(n, "spatialBlend", R.P(d, "spatialBlend"));
                R.SetP(n, "rolloffMode", R.P(d, "rolloffMode"));
                R.SetP(n, "minDistance", R.P(d, "minDistance"));
                R.SetP(n, "outputAudioMixerGroup", R.P(d, "outputAudioMixerGroup"));
                srcs = new Component[] { n };
            }
            foreach (var a in srcs)
            {
                if (R.P(a, "clip") == null) continue;
                if (R.Fl(R.P(a, "maxDistance")) < HitSoundMinRange) R.SetP(a, "maxDistance", HitSoundMinRange);
                if (R.B(R.P(a, "enabled")) && !R.B(R.P(a, "isPlaying"))) R.Call(a, "Play");
            }
        }

        // ------------------------------------------------------------------ audio of all effect prefabs (Debug)
        static bool _dumped;

        /// <summary>Once per session (Debug): every effect class of effects.ini and the audio of each of its prefabs.</summary>
        static void DumpEffectAudio()
        {
            if (_dumped || !_debug || _tAudio == null) return;
            _dumped = true;
            try
            {
                var em = UnityEngine.Object.FindObjectOfType(GameAssembly().GetType("SeaPower.EffectsManager"));
                var classes = em == null ? null : R.F(em, "_effectClassDictionary") as System.Collections.IDictionary;
                var effects = em == null ? null : R.F(em, "_effectsDictionary") as System.Collections.IDictionary;
                if (classes == null || effects == null) { Trace("effect audio dump: dictionaries not found"); return; }
                Trace("effect audio dump: " + classes.Count + " classes, " + effects.Count + " effects");
                foreach (System.Collections.DictionaryEntry c in classes)
                {
                    var names = c.Value as System.Collections.IEnumerable;
                    var sb = new StringBuilder("  class ").Append(c.Key).Append(':');
                    foreach (var n in names)
                    {
                        sb.Append("\n    ").Append(n).Append(" - ");
                        var go = effects.Contains(n) ? effects[n] as GameObject : null;
                        if (go == null) { sb.Append("prefab not found"); continue; }
                        var srcs = go.GetComponentsInChildren(_tAudio, true);
                        if (srcs.Length == 0) { sb.Append("NO AudioSource"); continue; }
                        foreach (var a in srcs)
                        {
                            var clip = R.P(a, "clip") as UnityEngine.Object;
                            sb.Append("audio '").Append(((Component)a).gameObject.name).Append("' clip ").Append(clip == null ? "NONE" : clip.name)
                              .Append(" vol ").Append(R.Fl(R.P(a, "volume")).ToString("0.##")).Append(" range ").Append(R.Fl(R.P(a, "minDistance")).ToString("0.#")).Append("..").Append(R.Fl(R.P(a, "maxDistance")).ToString("0.#"))
                              .Append(" units (~").Append((R.Fl(R.P(a, "maxDistance")) * 67.2f).ToString("0")).Append(" m) rolloff ").Append(R.P(a, "rolloffMode")).Append(" 3D ").Append(R.Fl(R.P(a, "spatialBlend")).ToString("0.#"))
                              .Append(" awake ").Append(R.B(R.P(a, "playOnAwake"))).Append("; ");
                        }
                    }
                    Trace(sb.ToString());
                }
            }
            catch (Exception e) { Trace("effect audio dump: " + e.Message); }
        }

        static void Line(string s) { _probeLines++; Trace(s); }
    }
}
