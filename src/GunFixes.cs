using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace AlternativeWeaponsControl
{
    /// <summary>
    /// [Guns] options: AimBeyondMaxRange (turrets turn towards targets beyond the maximum range) and MaxAccuracyMeters (the floor of the
    /// fire-control range error). The effect fixes (machine gun tracers / sound, gun hit sound) moved to Alternative Special Effects.
    /// </summary>
    public partial class AwcPlugin
    {

        void HookGunFixes(Harmony harmony, Assembly asm)
        {
            const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var gun = asm.GetType("SeaPower.WeaponSystemGun");
            var at = gun == null ? null : gun.GetMethod("AlignTurret", inst | BindingFlags.DeclaredOnly);
            if (at != null) harmony.Patch(at, prefix: Hm("AlignPrefix"), postfix: Hm("AlignPostfix")); else Trace("WeaponSystemGun.AlignTurret not found: no aiming beyond max range");
            var wci = asm.GetType("SeaPower.WeaponSystemCIWS");   // the aiming logic is here (SeaPower.CIWS is the effects attachment: tracers, sound)
            var cf = wci == null ? null : wci.GetMethod("FireAtTarget", inst | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
            if (cf != null) harmony.Patch(cf, prefix: Hm("CiwsAimPrefix")); else Trace("CIWS.FireAtTarget not found: CIWS / machine guns do not aim beyond max range");
            var ms = gun == null ? null : gun.GetMethod("GetMeasurementRangeSigma", inst | BindingFlags.DeclaredOnly);
            if (ms != null) harmony.Patch(ms, prefix: Hm("RangeSigmaPrefix"), finalizer: Hm("RangeSigmaFinalizer")); else Trace("WeaponSystemGun.GetMeasurementRangeSigma not found: no minimum range error option");
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
    }
}
