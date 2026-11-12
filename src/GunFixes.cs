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
            if (ms != null) harmony.Patch(ms, prefix: Hm("RangeSigmaPrefix"), postfix: Hm("RangeSigmaPostfix"), finalizer: Hm("RangeSigmaFinalizer")); else Trace("WeaponSystemGun.GetMeasurementRangeSigma not found: no minimum range error option");
            var gfm = asm.GetType("SeaPower.GunFireControlModel");
            var ss = gfm == null ? null : gfm.GetMethod("SurfaceSigmas", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (ss != null) harmony.Patch(ss, prefix: Hm("RangeSigmaPrefix"), postfix: Hm("FcErrorPostfix"), finalizer: Hm("RangeSigmaFinalizer"));   // (the real error of a salvo: its range part uses the floor too) else Trace("GunFireControlModel.SurfaceSigmas not found: no fire-control accuracy multiplier");
            var sens = asm.GetType("SeaPower.SensorSystem");
            var rfp = sens == null ? null : sens.GetMethod("ResolveFireControlProfile", inst | BindingFlags.DeclaredOnly);
            if (rfp != null) harmony.Patch(rfp, prefix: Hm("FcProfilePrefix"), postfix: Hm("FcProfilePostfix")); else Trace("SensorSystem.ResolveFireControlProfile not found: no director gain / minimum solution options");
            var su = asm.GetType("SeaPower.SensorUtils");
            var bs = su == null ? null : su.GetMethod("BuildSolution", BindingFlags.Static | BindingFlags.Public);
            if (bs != null) harmony.Patch(bs, prefix: Hm("SolSpeedPrefix")); else Trace("SensorUtils.BuildSolution not found: no SolutionBuildSpeed");
            ApplyLocalGain();
            var ini = asm.GetType("SeaPower.IniHandler");
            var rv = ini == null ? null : ini.GetMethod("readValue", inst, null, new[] { typeof(string), typeof(string), typeof(bool), typeof(bool) }, null);
            if (rv != null) harmony.Patch(rv, postfix: Hm("IniBoolPostfix")); else Trace("IniHandler.readValue(bool) not found: no EngageOnlyInsideArc override");
        }

        // ------------------------------------------------------------------ EngageOnlyInsideArc override
        // The weapon parameters read EngageOnlyInsideArc=True from the ini (battleships, big ships, 80+ weapons): the selector never offers
        // such a weapon a target outside its firing sector. With [Guns] EngageOutsideSector the value read for this key is False
        // (units loaded afterwards; the weapon then turns to the nearest edge of the sector, see AimBeyondMaxRange).
        static void IniBoolPostfix(string key, ref bool __result)
        {
            if (!__result || key != "EngageOnlyInsideArc") return;
            var a = Active; if (a != null && Enabled && a._engageOutside != null && a._engageOutside.Value) __result = false;
        }

        // ------------------------------------------------------------------ fire-control error multiplier
        // GunFireControlModel.SurfaceSigmas gives the four errors of the aim point of a salvo at a surface target (range, bearing, target's movement during the flight,
        // own ship's motion in a sea): director gain, light, crew, solution and the range floor are already in them. [Guns] FireControlAccuracy divides all four.
        static Type _tSensorRadar;

        static void FcErrorPostfix(ref float rangeSigma, ref float bearingSigma, ref float predictionSigma, ref float stabilizationSigma)
        {
            var a = Active; if (a == null || !Enabled) return;
            float m = a._fcAccuracy == null || a._fcAccuracy.Value <= 0.0001f ? 1f : a._fcAccuracy.Value;
            if (Math.Abs(m - 1f) < 1e-4f) return;
            rangeSigma /= m; bearingSigma /= m; predictionSigma /= m; stabilizationSigma /= m;
        }

        // The game decides whether a gun must wait for the fall of its last shell ("spotting fire") by comparing ITS estimate of the aim error with the shell scatter:
        // WeaponSystemGun.NeedsSpottingFire -> GetMeasurementRangeSigma. The estimate gets the same accuracy multipliers as the real error, else a more accurate
        // gun would still wait for splashes.
        static void RangeSigmaPostfix(object __instance, ref float __result)
        {
            var a = Active; if (a == null) return;
            float g = a._fcAccuracy == null || a._fcAccuracy.Value <= 0.0001f ? 1f : a._fcAccuracy.Value;
            float m = g;
            if (Math.Abs(m - 1f) > 1e-4f) __result /= m;
        }

        // ------------------------------------------------------------------ director gain / minimum solution, local control gain
        // A director's profile (SensorSystem._fcSolutionGainRate / _fcMinSolution, set once by ResolveFireControlProfile from the sensor's ini or inferred) drives
        // everything: range error ~ 1/gain^2, bearing error ~ 1/sqrt(gain), the speed the solution builds up (~ gain), the unstabilized-mount factor, the spotting-fire
        // decision. The scale of the values differs between rulesets (the game: optics 0.75, radars 1.5-2.5; WW2 pack: optics 0.11-0.37, radars 0.23-0.60).
        // Here the profile is recomputed from the sensor's own base values at every call (so a changed option works at once):
        //   1. WeakDirectorsLift p (percent): a gain below RefGain (0.75, the weakest director of the game) gets p% of the gap to RefGain added;
        //      the minimum solution grows by the same factor. Gains of the game's directors (>= 0.75) are not touched.
        //   2. the multipliers of the kind (optical / radar) on the gain and on the minimum solution.
        // Local control (no director) uses Globals._gunLocalControlGainRate (0.6): set to 0.6 * LocalControlGain.
        const float RefGain = 0.75f;
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, float[]> FcBase = new System.Runtime.CompilerServices.ConditionalWeakTable<object, float[]>();
        static FieldInfo _fGainGlobal; static float _localGainBase = float.NaN;

        static void FcProfilePrefix(object __instance, out bool __state)
        {
            __state = false;
            try { __state = R.B(R.F(__instance, "_fcProfileResolved")); } catch { }
        }

        static void FcProfilePostfix(object __instance, bool __state)
        {
            try
            {
                var a = Active;
                float[] b;
                if (!FcBase.TryGetValue(__instance, out b))
                {
                    if (a == null) return;   // nothing changed yet: the sensor's own values are the base
                    b = new[] { R.Fl(R.F(__instance, "_fcSolutionGainRate")), R.Fl(R.F(__instance, "_fcMinSolution")) };
                    FcBase.Add(__instance, b);
                }
                float gain = b[0], min = b[1];
                if (a != null && gain > 0f)
                {
                    if (_tSensorRadar == null) _tSensorRadar = _tObjectBase.Assembly.GetType("SeaPower.SensorSystemRadar");
                    bool radar = _tSensorRadar != null && _tSensorRadar.IsInstanceOfType(__instance);
                    float wm = a._weakBoost == null ? 0f : Mathf.Clamp01(a._weakBoost.Value / 100f);
                    float f = 1f;
                    if (gain < RefGain) f = (gain + wm * (RefGain - gain)) / gain;   // closes wm of the gap to the weakest director of the game
                    float mm = (radar ? a._minSolRadar : a._minSolOptical) == null ? 1f : (radar ? a._minSolRadar : a._minSolOptical).Value;
                    gain = Mathf.Max(gain * f, 0.02f);
                    if (min > 0f) min = Mathf.Clamp01(min * f * Mathf.Max(mm, 0f));
                }
                R.SetF(__instance, "_fcSolutionGainRate", gain);
                R.SetF(__instance, "_fcMinSolution", min);
            }
            catch (Exception e) { TraceLimited("fcprofile-err", 10f, "director profile: " + e.Message); }
        }

        /// <summary>Globals._gunLocalControlGainRate = the game's value * LocalControlGain (called at start and whenever an option changes).</summary>
        internal void ApplyLocalGain()
        {
            try
            {
                if (_fGainGlobal == null) _fGainGlobal = _tObjectBase.Assembly.GetType("SeaPower.Globals").GetField("_gunLocalControlGainRate", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (_fGainGlobal == null) return;
                if (float.IsNaN(_localGainBase)) _localGainBase = R.Fl(_fGainGlobal.GetValue(null));
                float m = Enabled && _gainLocal != null ? Mathf.Max(_gainLocal.Value, 0.01f) : 1f;
                _fGainGlobal.SetValue(null, _localGainBase * m);
            }
            catch (Exception e) { Trace("local control gain: " + e.Message); }
        }

        // ------------------------------------------------------------------ minimum fire-control range error
        // GetMeasurementRangeSigma = max(Globals._gunFCBaseRangeErrorFloorUnity (~40 m), 1.5 % of range) / gain^2 ...: inside ~1.4 nm the
        // constant floor dominates, so close shooting is relatively worse. For the call the floor is replaced with the configured
        // value (metres); the percentage part (scaling with distance) is untouched.
        // The floor is swapped around both users of the game's MeasurementRangeSigma for surface targets: WeaponSystemGun.GetMeasurementRangeSigma (the spotting-fire
        // decision) and GunFireControlModel.SurfaceSigmas (the real aiming error of a salvo and the tooltip); a patch only on the first one left the real error unchanged.
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
        // move (a target outside the firing sector needs no patch: RotateMountTowardsTarget turns the mount to the nearest edge of the sector). For the player's guns a target up to 1.5 x range + 2 nm away is aimed at as a point of the same bearing at the
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
        /// CIWS and machine guns (Type=CIWS): FireAtTarget aims only while the target is within the range (+ a small margin) AND inside the
        /// firing sector (horizontal and vertical arcs); otherwise it only stops the fire and does not move the mount. For the targets of the
        /// player's orders farther than that - up to the same limit as the guns - or outside the sector, the mount is turned and elevated
        /// towards the target as far as its arcs allow (the guns' own alignment code goes to the nearest edge of the sector) and nothing else is done.
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
                bool inSector = R.B(R.Call(__instance, "targetIsInFiringArc", tr.position)) && R.B(R.Call(__instance, "IsTargetInVerticalFiringArc", tr.position));
                if (dist <= max + margin && inSector) return true;   // the game's own code
                if (dist > AimLimit(max)) return true;
                R.Call(__instance, "CalculateAimPosition", tgt);
                var aim = (Vector3)R.F(__instance, "_aimPosition");
                if (R.B(R.F(__instance, "_isFiring"))) R.Call(__instance, "StopFire");
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

        static void AlignPostfix(object __instance, ref bool __result)
        {
            if (_aimBeyond) { _aimBeyond = false; __result = false; return; }
            if (!__result) { try { UnstickAim(__instance); } catch (Exception e) { TraceLimited("unstick-err", 10f, "unstick aim: " + e.Message); } }
        }

        // The game adds a random fire-control error (WeaponSystemGun._deviationVector) to the aim point BEFORE it asks the fire table for a solution,
        // and keeps it (_deviationCalculated) until the gun has fired. Near the maximum range the error can push the aim point past the table
        // (GunBallistics.TrySolveSurface: range > _maxRange): AlignTurret returns false before the mount moves, the gun never fires, so the error is
        // never redrawn - the turret idles until the target changes while the other turrets of the same type shoot. [Guns] RerollAimError: if the
        // target itself (without the error) has a solution, the error is drawn again (the game's own DeviationCheck runs on the next call).
        static MethodInfo _miSolveSurface, _miGetTable;
        static void UnstickAim(object g)
        {
            var a = Active; if (a == null || !Enabled || a._gunReroll == null || !a._gunReroll.Value) return;
            if (!R.B(R.F(g, "_deviationCalculated")) || R.B(R.F(g, "_solutionCalculated"))) return;
            if (Convert.ToDouble(R.F(g, "_projectileToTargetTime")) >= 0.0) return;   // the solution step did not fail
            var bo = R.F(g, "_baseObject"); if (bo == null || !R.B(R.P(bo, "IsPlayerObject"))) return;
            var tgt = R.F(g, "_targetObject"); if (tgt == null || R.B(R.Call(tgt, "isAirTarget"))) return;
            var tr = R.P(tgt, "transform") as Transform; var ap = R.F(R.F(g, "_ammoForEngage"), "_ap"); var barrels = R.F(g, "_barrels") as System.Collections.IList;
            if (tr == null || ap == null || barrels == null || barrels.Count == 0) return;
            var gb = _tObjectBase.Assembly.GetType("SeaPower.GunBallistics");
            if (_miGetTable == null) _miGetTable = gb.GetMethod("GetTable", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (_miSolveSurface == null) _miSolveSurface = gb.GetMethod("TrySolveSurface", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var table = _miGetTable.Invoke(null, new[] { ap }); if (table == null) return;
            var go = R.F(barrels[0], "_gunObject") as GameObject; if (go == null) return;
            Vector3 own = go.transform.position + go.transform.rotation * (Vector3)R.F(barrels[0], "_spawnPosition");
            Vector3 pos = tr.position; if (pos.y < 0f) pos.y = 0f;
            var vel = (Vector3)R.P(tgt, "UnityVelocityVector"); vel.y = 0f;
            var args = new object[] { table, own, pos, vel, 0f, Vector3.zero };
            if (!(bool)_miSolveSurface.Invoke(null, args)) return;   // the target itself cannot be hit: nothing to redraw
            R.SetF(g, "_deviationCalculated", false);
            TraceLimited("unstick" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(g), 5f,
                "aim error redrawn: gun " + R.S(R.F(g, "_systemName")) + " of " + R.S(R.Call(bo, "getUIDAndName")) + " for " + R.S(R.Call(tgt, "getUIDAndName")) + " (the old error left the fire table)");
        }
    }
}
