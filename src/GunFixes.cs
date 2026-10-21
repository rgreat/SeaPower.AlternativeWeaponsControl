using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace AlternativeWeaponsControl
{
    /// <summary>
    /// [Guns] options:
    ///  - MachineGunBurstFix: machine guns (Type=CIWS) show their tracers through the whole volley. Their tracers are particle
    ///    systems with a fixed duration (1.1 s, not looping): after it they stop emitting although the volley goes on.
    ///    Looping is switched on for every system of the effect that plays at the start of a volley.
    ///  - HitSoundFix: gun hits on ships always sound. Some hit effects (missile_hit_ship_small, _4) have no AudioSource at all:
    ///    they get one with an explosion / flak clip taken from another effect; sounds are audible from at least 50 units.
    ///  - MachineGunSoundLoopFix: the looped fire sound skips the quiet tail of its clip (a gap in the middle of a long burst).
    /// Unity's particle and audio modules are not referenced: read by reflection.
    /// </summary>
    public partial class AwcPlugin
    {
        [ThreadStatic] static object _hitProjectile;   // Projectile in ProcessCollision
        static Type _tPs, _tAudio;
        const float HitSoundMinRange = 50f;
        static readonly List<object> _donors = new List<object>();   // AudioSources of effect prefabs with an explosion / flak clip
        static bool _donorsDone;

        void HookGunFixes(Harmony harmony, Assembly asm)
        {
            const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _tPs = Type.GetType("UnityEngine.ParticleSystem, UnityEngine.ParticleSystemModule");
            _tAudio = Type.GetType("UnityEngine.AudioSource, UnityEngine.AudioModule");
            var ci = asm.GetType("SeaPower.CIWS");
            var sf = ci == null ? null : ci.GetMethod("startFire", inst);
            if (sf != null) harmony.Patch(sf, postfix: Hm("CiwsStartFire"));
            var cu = ci == null ? null : ci.GetMethod("update", inst);
            if (cu != null) harmony.Patch(cu, postfix: Hm("CiwsUpdate"));
            var pr = asm.GetType("SeaPower.Projectile");
            var pc = pr == null ? null : pr.GetMethod("ProcessCollision", inst);
            if (pc != null) harmony.Patch(pc, prefix: Hm("HitEnter"), finalizer: Hm("HitLeave"));
            var em = asm.GetType("SeaPower.EffectsManager");
            var se = em == null ? null : em.GetMethod("spawnEffect", inst);
            if (se != null) harmony.Patch(se, postfix: Hm("HitEffect"));
            var wbT = asm.GetType("SeaPower.WeaponBase");
            var cde = wbT == null ? null : wbT.GetMethod("CreateDesctructionEffects", inst | BindingFlags.DeclaredOnly);
            if (cde != null) harmony.Patch(cde, prefix: Hm("DetonationEnter"), postfix: Hm("DetonationFlash"), finalizer: Hm("DetonationLeave"));
            else Trace("WeaponBase.CreateDesctructionEffects not found: no torpedo / bomb / mortar flash");
            var mi = asm.GetType("SeaPower.Missile");
            var sfm = mi == null ? null : mi.GetMethod("ScheduleFlightEffects", inst | BindingFlags.DeclaredOnly);
            if (sfm != null) harmony.Patch(sfm, prefix: Hm("MissileLaunchPrefix"), postfix: Hm("MissileLaunchFlash")); else Trace("Missile.ScheduleFlightEffects not found: no missile launch flash");
            var gb = asm.GetType("SeaPower.GunBarrel");
            var lpm = gb == null ? null : gb.GetMethod("LaunchProjectile", inst | BindingFlags.DeclaredOnly);
            if (lpm != null) harmony.Patch(lpm, postfix: Hm("BarrelFlash")); else Trace("GunBarrel.LaunchProjectile not found: no muzzle flash");
            var gun = asm.GetType("SeaPower.WeaponSystemGun");
            var at = gun == null ? null : gun.GetMethod("AlignTurret", inst | BindingFlags.DeclaredOnly);
            if (at != null) harmony.Patch(at, prefix: Hm("AlignPrefix"), postfix: Hm("AlignPostfix")); else Trace("WeaponSystemGun.AlignTurret not found: no aiming beyond max range");
            var cf = ci == null ? null : ci.GetMethod("FireAtTarget", inst | BindingFlags.DeclaredOnly);
            if (cf != null) harmony.Patch(cf, prefix: Hm("CiwsAimPrefix")); else Trace("CIWS.FireAtTarget not found: CIWS / machine guns do not aim beyond max range");
            var ms = gun == null ? null : gun.GetMethod("GetMeasurementRangeSigma", inst | BindingFlags.DeclaredOnly);
            if (ms != null) harmony.Patch(ms, prefix: Hm("RangeSigmaPrefix"), finalizer: Hm("RangeSigmaFinalizer")); else Trace("WeaponSystemGun.GetMeasurementRangeSigma not found: no minimum range error option");
            if (sf == null || pc == null || se == null) Trace("gun fixes: not all hooks found (CIWS.startFire " + (sf != null) + ", ProcessCollision " + (pc != null) + ", spawnEffect " + (se != null) + ")");
        }

        // ------------------------------------------------------------------ minimum fire-control range error
        // GetMeasurementRangeSigma = max(Globals._gunFCBaseRangeErrorFloorUnity (~40 m), 1.5 % of range) / gain^2 ...: inside ~1.4 nm the
        // constant floor dominates, so close shooting is relatively worse. For the call the floor is replaced with the configured
        // value (metres); the percentage part (scaling with distance) is untouched.
        static FieldInfo _fFloor; static float _floorSaved; static bool _floorSwapped;
        static void RangeSigmaPrefix()
        {
            _floorSwapped = false;
            try
            {
                var a = Active; if (a == null || !Enabled || a._gunMinError == null) return;
                if (_fFloor == null) _fFloor = _tObjectBase.Assembly.GetType("SeaPower.Globals").GetField("_gunFCBaseRangeErrorFloorUnity", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (_fFloor == null) return;
                _floorSaved = R.Fl(_fFloor.GetValue(null));
                _fFloor.SetValue(null, Mathf.Max(a._gunMinError.Value, 0f) / (AimNmPerUnit * 1852f));
                _floorSwapped = true;
            }
            catch { }
        }
        static void RangeSigmaFinalizer()
        {
            if (!_floorSwapped) return;
            _floorSwapped = false;
            try { _fFloor.SetValue(null, _floorSaved); } catch { }
        }

        // ------------------------------------------------------------------ aiming beyond the maximum range
        // WeaponSystemGun.AlignTurret returns at once when the target is farther than the ammunition's range: the turret does not
        // move. For the player's guns a target up to 1.5 x range + 2 nm away is aimed at as a point of the same bearing at the
        // range (the game's own aiming code does the turning and elevating); the result stays "not aligned": no fire.
        // CIWS / machine guns: CiwsAimPrefix.
        [ThreadStatic] static bool _aimBeyond;
        const float AimNmPerUnit = 0.036285132f;

        static void AlignPrefix(object __instance, ref Vector3 targetPosition, bool isAirTarget)
        {
            _aimBeyond = false;
            try
            {
                var a = Active; if (a == null || !Enabled || a._gunAimBeyond == null || !a._gunAimBeyond.Value) return;
                var bo = R.F(__instance, "_baseObject");
                if (bo == null || !R.B(R.P(bo, "IsPlayerObject"))) return;
                var ap = R.F(R.F(__instance, "_ammoForEngage"), "_ap"); if (ap == null) return;
                float max = ((Vector2)R.F(ap, "_launchRangesInUnity")).y;
                var barrels = R.F(__instance, "_barrels") as System.Collections.IList; if (barrels == null || barrels.Count == 0) return;
                var go = R.F(barrels[0], "_gunObject") as GameObject; if (go == null) return;
                Vector3 from = go.transform.position, d = targetPosition - from;
                float dist = d.magnitude;
                if (isAirTarget) max = Mathf.Min(max, AirDirectFire(ap, targetPosition.y - from.y) * 1.15f);   // the game's own limit for air targets
                if (max <= 0f || dist <= max || dist > AimLimit(max)) return;
                targetPosition = from + d / dist * (max * 0.98f);
                _aimBeyond = true;
            }
            catch (Exception e) { Trace("aim beyond: " + e.Message); }
        }

        /// <summary>
        /// CIWS and machine guns (Type=CIWS): FireAtTarget aims only while the target is within the range (+ a small margin).
        /// Farther - up to the same limit as the guns - the mount is turned and elevated towards the target and nothing else is done.
        /// </summary>
        static bool CiwsAimPrefix(object __instance)
        {
            try
            {
                var a = Active; if (a == null || !Enabled || a._gunAimBeyond == null || !a._gunAimBeyond.Value) return true;
                var bo = R.F(__instance, "_baseObject");
                if (bo == null || !R.B(R.P(bo, "IsPlayerObject"))) return true;
                var tgt = R.F(__instance, "_currentClosestTarget"); if (tgt == null) return true;
                float maxSq = R.Fl(R.F(__instance, "_maxRangeSq")); if (maxSq <= 0f) return true;
                var mount = R.F(__instance, "_mountObject") as GameObject; if (mount == null) return true;
                var tr = R.P(tgt, "transform") as Transform; if (tr == null) return true;
                float dist = (tr.position - mount.transform.position).magnitude, max = Mathf.Sqrt(maxSq);
                float margin = R.Fl(R.Call(__instance, "CiwsPrepMargin"));
                if (dist <= max + margin || dist > AimLimit(max)) return true;   // the game's own code
                R.Call(__instance, "CalculateAimPosition", tgt);
                var aim = (Vector3)R.F(__instance, "_aimPosition");
                if (!R.B(R.Call(__instance, "targetIsInFiringArc", aim)) || !R.B(R.Call(__instance, "IsTargetInVerticalFiringArc", aim))) return false;
                R.Call(__instance, "alignToTarget", aim, false, 0, false);
                return false;
            }
            catch (Exception e) { Trace("ciws aim beyond: " + e.Message); return true; }
        }

        /// <summary>How far a target is still aimed at: 1.5 x the range, and always up to 5 nm.</summary>
        static float AimLimit(float max) { return Mathf.Max(max * 1.5f, 5f / AimNmPerUnit); }

        static float AirDirectFire(object ap, float dy)
        {
            try
            {
                var asm = _tObjectBase.Assembly;
                var gb = asm.GetType("SeaPower.GunBallistics");
                var gl = asm.GetType("SeaPower.Globals");
                float deg = R.Fl(gl.GetField("_gunAADirectFireMaxElevationDeg", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null));
                return R.Fl(gb.GetMethod("DirectFireRangeUnity", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { ap.GetType(), typeof(float), typeof(float) }, null).Invoke(null, new object[] { ap, dy, deg }));
            }
            catch { return float.MaxValue; }
        }

        static void AlignPostfix(ref bool __result)
        {
            if (!_aimBeyond) return;
            _aimBeyond = false;
            __result = false;
        }

        static bool FixOn(bool burst)
        {
            var a = Active; if (a == null || !Enabled) return false;
            var e = burst ? a._mgBurstFix : a._hitSoundFix;
            return e != null && e.Value;
        }

        static void CiwsStartFire(object __instance)
        {
            try
            {
                if (!FixOn(true) || _tPs == null) return;
                var root = R.F(__instance, "_tracers") as GameObject;
                if (root == null) return;
                foreach (var p in root.GetComponentsInChildren(_tPs, true))
                {
                    var m = R.P(p, "main");
                    if (R.B(R.P(p, "isPlaying")) && !R.B(R.P(m, "loop"))) R.SetP(m, "loop", true);
                }
            }
            catch (Exception e) { Trace("CIWS start: " + e.Message); }
        }

        // The fire sound is a clip (tripleA, 2.16 s) looped by the game while a burst goes on. Its tail (or head) is quiet: on every
        // wrap there is a gap in the middle of a long burst. The loudness of the clip is measured (50 ms windows) and playback
        // jumps from the end of the loud part back to its start.
        static readonly Dictionary<int, float[]> _loopRegion = new Dictionary<int, float[]>();   // clip id -> { start, end }; null = nothing to skip

        static float[] LoopRegion(object clip)
        {
            int id = ((UnityEngine.Object)clip).GetInstanceID();
            float[] r;
            if (_loopRegion.TryGetValue(id, out r)) return r;
            _loopRegion[id] = null;
            try
            {
                int n = R.I(R.P(clip, "samples")), ch = R.I(R.P(clip, "channels")), fr = R.I(R.P(clip, "frequency"));
                var buf = new float[n * ch];
                var gd = clip.GetType().GetMethod("GetData", new[] { typeof(float[]), typeof(int) });
                if (gd == null || !(bool)gd.Invoke(clip, new object[] { buf, 0 })) { Trace("sound loop: cannot read clip " + ((UnityEngine.Object)clip).name); return null; }
                int w = Math.Max(1, fr / 20) * ch, cnt = buf.Length / w;
                var rms = new float[cnt]; float max = 0f;
                for (int i = 0; i < cnt; i++)
                {
                    double sum = 0; for (int j = 0; j < w; j++) { float v = buf[i * w + j]; sum += v * v; }
                    rms[i] = (float)Math.Sqrt(sum / w); if (rms[i] > max) max = rms[i];
                }
                if (max <= 0f) return null;
                int a = 0, b = cnt - 1; float thr = 0.6f * max;   // the loud part: the fade-out before the silence is cut off too
                while (a < cnt && rms[a] < thr) a++;
                while (b > a && rms[b] < thr) b--;
                float len = (float)n / fr, start = a * 0.05f, end = Math.Min(len, (b + 1) * 0.05f);
                if (start > 0.1f || len - end > 0.1f) r = new[] { start, end };
                _loopRegion[id] = r;
            }
            catch (Exception e) { Trace("sound loop: " + e.Message); }
            return r;
        }

        static void CiwsSoundLoop(object ciws)
        {
            var a = Active;
            if (a == null || !Enabled || a._mgSoundLoop == null || !a._mgSoundLoop.Value) return;
            var snd = R.F(ciws, "_sound");
            if (snd == null || !R.B(R.P(snd, "isPlaying"))) return;
            var clip = R.P(snd, "clip");
            if (clip == null) return;
            var rg = LoopRegion(clip);
            if (rg == null) return;
            float t = R.Fl(R.P(snd, "time"));
            if (t >= rg[1]) R.SetP(snd, "time", rg[0] + (t - rg[1]));
            else if (t < rg[0]) R.SetP(snd, "time", rg[0]);
        }

        static void CiwsUpdate(object __instance)
        {
            try { CiwsSoundLoop(__instance); } catch (Exception e) { Trace("sound loop: " + e.Message); }
            CiwsFlash(__instance);
        }

        static void HitEnter(object __instance) { if (FixOn(false)) _hitProjectile = __instance; }
        static Exception HitLeave(Exception __exception) { _hitProjectile = null; return __exception; }

        static void HitEffect(GameObject __result, object ep)
        {
            if (__result == null) return;
            ExplosionFlash(__result, ep);
            if (_hitProjectile == null || _tAudio == null || !FixOn(false)) return;
            try
            {
                var srcs = __result.GetComponentsInChildren(_tAudio, true);
                if (srcs.Length == 0)
                {
                    if (!_donorsDone) FindDonors();
                    if (_donors.Count == 0) return;
                    var d = _donors[UnityEngine.Random.Range(0, _donors.Count)];
                    var n = __result.AddComponent(_tAudio);
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
            catch (Exception e) { Trace("hit sound: " + e.Message); }
        }

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
        }
    }
}
