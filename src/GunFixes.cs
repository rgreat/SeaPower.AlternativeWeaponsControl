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
            PatchOne(harmony, gun == null ? null : gun.GetMethod("AlignTurret", inst | BindingFlags.DeclaredOnly), "WeaponSystemGun.AlignTurret", "AlignPrefix", "AlignPostfix");
            PatchOne(harmony, gun == null ? null : gun.GetMethod("DeviateByCEP", inst | BindingFlags.DeclaredOnly), "WeaponSystemGun.DeviateByCEP", "ScatterPrefix", "SalvoLogPostfix");   // trace of every salvo; BarrelScatter
            PatchOne(harmony, gun == null ? null : gun.GetMethod("OnUpdate", inst | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null), "WeaponSystemGun.OnUpdate", "LsPrefix", "LsPostfix");   // BigGunsLocalSolution
            var gbar = asm.GetType("SeaPower.GunBarrel");
            PatchOne(harmony, gbar == null ? null : gbar.GetMethod("LaunchProjectile", inst | BindingFlags.DeclaredOnly), "GunBarrel.LaunchProjectile", "BarrelPrefix");
            var wci = asm.GetType("SeaPower.WeaponSystemCIWS");   // the aiming logic is here (SeaPower.CIWS is the effects attachment: tracers, sound)
            PatchOne(harmony, wci == null ? null : wci.GetMethod("FireAtTarget", inst | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null), "WeaponSystemCIWS.FireAtTarget", "CiwsAimPrefix");
            PatchOne(harmony, gun == null ? null : gun.GetMethod("GetMeasurementRangeSigma", inst | BindingFlags.DeclaredOnly), "WeaponSystemGun.GetMeasurementRangeSigma", "RangeSigmaPrefix", "RangeSigmaPostfix", "RangeSigmaFinalizer");
            var gfm = asm.GetType("SeaPower.GunFireControlModel");
            // the real error of a salvo: its range part uses the floor too
            PatchOne(harmony, gfm == null ? null : gfm.GetMethod("SurfaceSigmas", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic), "GunFireControlModel.SurfaceSigmas", "RangeSigmaPrefix", "FcErrorPostfix", "RangeSigmaFinalizer");
            var sens = asm.GetType("SeaPower.SensorSystem");
            PatchOne(harmony, sens == null ? null : sens.GetMethod("ResolveFireControlProfile", inst | BindingFlags.DeclaredOnly), "SensorSystem.ResolveFireControlProfile", "FcProfilePrefix", "FcProfilePostfix");
            // Strict EMCON: a gun-only radar station works as an optical director (light, splashes)
            PatchOne(harmony, sens == null ? null : sens.GetMethod("GetFireControlLightFactor", inst | BindingFlags.DeclaredOnly), "SensorSystem.GetFireControlLightFactor", null, "TrimLightPostfix");
            PatchOne(harmony, gun == null ? null : gun.GetMethod("CanObserveFallOfShot", inst | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null), "WeaponSystemGun.CanObserveFallOfShot", null, "TrimSplashPostfix");
            PatchOne(harmony, gun == null ? null : gun.GetMethod("GetFireControlSensor", inst | BindingFlags.DeclaredOnly), "WeaponSystemGun.GetFireControlSensor", null, "TrimRangePostfix");
            var su = asm.GetType("SeaPower.SensorUtils");
            PatchOne(harmony, su == null ? null : su.GetMethod("BuildSolution", BindingFlags.Static | BindingFlags.Public), "SensorUtils.BuildSolution", "SolSpeedPrefix");
            PatchOne(harmony, gfm == null ? null : gfm.GetMethod("SolutionStep", BindingFlags.Static | BindingFlags.Public), "GunFireControlModel.SolutionStep", "SolLossPrefix", "SolLossPostfix");
            _gameAsm = asm;
            ApplyLocalGain();
            var ini = asm.GetType("SeaPower.IniHandler");
            PatchOne(harmony, ini == null ? null : ini.GetMethod("readValue", inst, null, new[] { typeof(string), typeof(string), typeof(bool), typeof(bool) }, null), "IniHandler.readValue(string, string, bool, bool)", null, "IniBoolPostfix");
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
            try
            {
                __state = R.B(R.F(__instance, "_fcProfileResolved"));
                // a non-radar sensor with no authored FCSolutionGainRate (-1 before the game infers it): the game gives it gain 1.0, better than every director of
                // a WW2 pack (0.1-0.4) and than local control (0.6). That is the bridge's naked eye: it takes over as the "best director" when the radars/rangefinders
                // of the gun's group are destroyed. It is not a director: it gets the local control gain.
                if (!__state && R.Fl(R.F(__instance, "_fcSolutionGainRate")) < 0f)
                {
                    if (_tSensorRadar == null && _tObjectBase != null) _tSensorRadar = _tObjectBase.Assembly.GetType("SeaPower.SensorSystemRadar");
                    if (_tSensorRadar == null || !_tSensorRadar.IsInstanceOfType(__instance)) { object o; if (!FcUnauth.TryGetValue(__instance, out o)) FcUnauth.Add(__instance, true); }
                }
            }
            catch { }
        }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, object> FcUnauth = new System.Runtime.CompilerServices.ConditionalWeakTable<object, object>();

        // The game calls ResolveFireControlProfile for every director of every gun on every check (it returns at once when the profile is resolved):
        // the values are rewritten only when the profile was just resolved or an option changed (_fcEpoch), not on every call.
        internal static int _fcEpoch;

        static void FcProfilePostfix(object __instance, bool __state)
        {
            try
            {
                float[] b;
                if (__state && FcBase.TryGetValue(__instance, out b) && (int)b[2] == _fcEpoch) return;
                var a = Active;
                if (!FcBase.TryGetValue(__instance, out b))
                {
                    if (a == null) return;   // nothing changed yet: the sensor's own values are the base
                    b = new[] { R.Fl(R.F(__instance, "_fcSolutionGainRate")), R.Fl(R.F(__instance, "_fcMinSolution")), 0f, R.B(R.F(__instance, "_fcTracksRounds")) ? 1f : 0f };
                    FcBase.Add(__instance, b);
                }
                b[2] = _fcEpoch;
                float gain = b[0], min = b[1];
                bool trim = a != null && Trimmed(__instance);   // Strict EMCON on a gun-only radar station: an optical director that does not radiate
                if (a != null && gain > 0f)
                {
                    if (_tSensorRadar == null) _tSensorRadar = _tObjectBase.Assembly.GetType("SeaPower.SensorSystemRadar");
                    bool radar = _tSensorRadar != null && _tSensorRadar.IsInstanceOfType(__instance) && !trim;
                    float wm = a._weakBoost == null ? 0f : Mathf.Clamp01(a._weakBoost.Value / 100f);
                    float f = 1f;
                    if (gain < RefGain) f = (gain + wm * (RefGain - gain)) / gain;   // closes wm of the gap to the weakest director of the game
                    float mm = (radar ? a._minSolRadar : a._minSolOptical) == null ? 1f : (radar ? a._minSolRadar : a._minSolOptical).Value;
                    gain = Mathf.Max(gain * f, 0.02f);
                    object un; if (FcUnauth.TryGetValue(__instance, out un) && _fGainGlobal != null) gain = Mathf.Max(R.Fl(_fGainGlobal.GetValue(null)), 0.02f);   // not a director: local control
                    if (min > 0f) min = Mathf.Clamp01(min * f * Mathf.Max(mm, 0f));
                    if (trim && _fGainGlobal != null)
                    {
                        // the optical gain: a point between local control (the crew's own eyes) and the radar, the share of the way from the first to the second
                        float local = Mathf.Max(R.Fl(_fGainGlobal.GetValue(null)), 0.02f);
                        float share = a._emconOptical == null ? 0.5f : Mathf.Clamp01(a._emconOptical.Value / 100f);
                        gain = Mathf.Max(Mathf.Lerp(local, gain, share), 0.02f);
                    }
                }
                R.SetF(__instance, "_fcSolutionGainRate", gain);
                R.SetF(__instance, "_fcMinSolution", min);
                R.SetF(__instance, "_fcTracksRounds", trim ? false : b[3] > 0.5f);   // optics do not follow shells in flight
                Radiate(__instance, !trim);
            }
            catch (Exception e) { TraceLimited("fcprofile-err", 10f, "director profile: " + e.Message); }
        }

        // ------------------------------------------------------------------ Strict EMCON: a gun-only radar station as an optical director
        // The game's ESM hears a radar by RadarParameters._peakPower (RadarCalculator.getSignalStrength: a station with 0 is not in the list of emitters), and a
        // director only needs IsOn and a free channel to take a gun's target, so a station whose power is 0 still works as a director, silently.
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, float[]> PeakBase = new System.Runtime.CompilerServices.ConditionalWeakTable<object, float[]>();
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, object> OptStations = new System.Runtime.CompilerServices.ConditionalWeakTable<object, object>();   // value: bool (boxed)

        static void Radiate(object sensor, bool on)
        {
            var rp = R.F(R.F(sensor, "_radar"), "_rp"); if (rp == null) return;
            float[] pk;
            if (!PeakBase.TryGetValue(sensor, out pk)) { if (on) return; pk = new[] { R.Fl(R.F(rp, "_peakPower")) }; PeakBase.Add(sensor, pk); }
            R.SetF(rp, "_peakPower", on ? pk[0] : 0f);
        }

        /// <summary>A radar station that serves guns and nothing else (every weapon that lists it is a gun): optics can stand in for it. Decided once per station.</summary>
        internal static bool OpticalStation(object sensor)
        {
            if (sensor == null || _tRadar == null || !_tRadar.IsInstanceOfType(sensor)) return false;
            object o; if (OptStations.TryGetValue(sensor, out o)) return (bool)o;
            bool yes = false;
            try
            {
                var wss = R.F(R.F(R.F(sensor, "_baseObject"), "_obp"), "_weaponSystems") as System.Collections.IList;
                int guns = 0, others = 0;
                if (wss != null)
                    foreach (var w in wss)
                    {
                        var sl = R.F(R.F(w, "_vwp"), "_associatedSensors") as System.Collections.IList;
                        if (sl == null || !sl.Contains(sensor)) continue;
                        if (R.S(R.F(w, "_weaponType")) == "Gun") guns++; else others++;
                    }
                yes = guns > 0 && others == 0;
            }
            catch (Exception e) { Trace("optical station: " + e.Message); }
            OptStations.Add(sensor, yes);
            return yes;
        }

        /// <summary>The station is under Strict EMCON of its unit and works as an optical director.</summary>
        internal static bool Trimmed(object sensor)
        {
            try
            {
                if (!OpticalStation(sensor)) return false;
                var unit = R.F(sensor, "_baseObject"); UnitW w;
                return unit != null && UnitsW.TryGetValue(unit, out w) && w.StrictEmcon;
            }
            catch { return false; }
        }

        static MethodInfo _miSun; static Type _skyType;
        internal static float Sunlight()
        {
            try
            {
                if (_miSun == null)
                {
                    var asm = GameAssemblyStatic();
                    var sky = asm.GetType("SeaPower.DynamicSky"); if (sky == null) return 1f;
                    _skyType = _tSingletonGeneric ?? asm.GetType("SeaPower.Singleton`1");
                    _miSun = sky.GetMethod("getSunlightIntensity", BindingFlags.Instance | BindingFlags.Public);
                    _skyInst = _skyType.MakeGenericType(sky).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public);
                }
                var inst = _skyInst.GetValue(null, null);
                return inst == null ? 1f : Mathf.Clamp01(Convert.ToSingle(_miSun.Invoke(inst, null)));
            }
            catch { return 1f; }
        }
        static PropertyInfo _skyInst;

        // the light factor of the optical director: the game's formula for optics (sunlight; no night vision here)
        static void TrimLightPostfix(object __instance, ref float __result)
        {
            if (!_trimAny || !Trimmed(__instance)) return;
            __result = Sunlight();
        }

        // a splash is seen in the light, or when a working radar is associated: a station that works as optics is not one
        static FieldInfo _fSplashMin;
        static void TrimSplashPostfix(object __instance, ref bool __result)
        {
            if (!__result || !_trimAny) return;
            try
            {
                if (_fSplashMin == null) _fSplashMin = GameAssemblyStatic().GetType("SeaPower.Globals").GetField("_gunSplashSpotMinLight", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (_fSplashMin != null && Sunlight() >= R.Fl(_fSplashMin.GetValue(null))) return;   // light enough
                var sl = R.F(R.F(__instance, "_vwp"), "_associatedSensors") as System.Collections.IList; if (sl == null) return;
                bool real = false;
                foreach (var x in sl)
                    if (x != null && _tRadar.IsInstanceOfType(x) && !R.B(R.Rv(R.P(x, "Inoperable"))) && R.B(R.Rv(R.P(x, "IsOn"))) && !Trimmed(x)) { real = true; break; }
                __result = real;
            }
            catch { }
        }
        // The range of an optical director: as a Kind=Visual sensor of the game sees (SensorSystemVisual.CheckObjectDetection / GetRange: SensorUtils.CalculateVisualRange
        // with night vision 0 (size, light, weather), and the visual horizon from the station's height). A gun's own fire control has no director range in the game,
        // so a trimmed station is given up for a target beyond it: the next director, or local control.
        static MethodInfo _miVisRange, _miVisHorizon, _miResolveBest; static PropertyInfo _piSensorCache; static Type _tSensorCache;
        static bool InVisualRange(object station, object target)
        {
            try
            {
                if (_miVisRange == null)
                {
                    var asm = GameAssemblyStatic();
                    _miVisRange = asm.GetType("SeaPower.SensorUtils").GetMethod("CalculateVisualRange", BindingFlags.Static | BindingFlags.Public, null,
                        new[] { _tObjectBase, _tObjectBase, typeof(float), typeof(float).MakeByRefType(), typeof(bool), typeof(bool), typeof(bool), asm.GetType("SeaPower.SensorEcho") }, null);
                    _tSensorCache = asm.GetType("SeaPower.UnitSensorCache");
                    _miVisHorizon = _tSensorCache.GetMethod("VisualHorizon");
                    _piSensorCache = asm.GetType("SeaPower.Singleton`1").MakeGenericType(_tSensorCache).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy);
                }
                var own = R.F(station, "_baseObject");
                var args = new object[] { own, target, 0f, 0f, false, false, false, null };
                float range = Convert.ToSingle(_miVisRange.Invoke(null, args));
                var sp = R.P(station, "SensorPosition");
                float h = Convert.ToSingle(_miVisHorizon.Invoke(_piSensorCache.GetValue(null, null), new object[] { own, target, R.Fl(R.F(sp, "y")) }));
                float d = ((Vector3)R.P(R.P(own, "transform"), "position") - (Vector3)R.P(R.P(target, "transform"), "position")).magnitude;
                return range >= 0f && d <= Mathf.Min(range, h);
            }
            catch (Exception e) { TraceLimited("trim-range", 10f, "optical range: " + e.Message); return true; }
        }

        // a gun's director: a trimmed station only for a target within its visual range; beyond it the next director, or local control
        static void TrimRangePostfix(object __instance, ref object __result)
        {
            if (__result == null || !_trimAny || !Trimmed(__result)) return;
            var tgt = R.F(__instance, "_targetObject");
            if (tgt == null || InVisualRange(__result, tgt)) return;
            try
            {
                var all = R.F(R.F(__instance, "_vwp"), "_associatedSensors") as System.Collections.IList;
                var rest = (System.Collections.IList)Activator.CreateInstance(all.GetType());
                foreach (var x in all) if (x != __result) rest.Add(x);
                if (_miResolveBest == null) _miResolveBest = GameAssemblyStatic().GetType("SeaPower.FireControl").GetMethod("ResolveBestDirector", BindingFlags.Static | BindingFlags.Public);
                var next = _miResolveBest.Invoke(null, new object[] { rest, true, false });
                __result = next != null && (!Trimmed(next) || InVisualRange(next, tgt)) ? next : null;
            }
            catch (Exception e) { TraceLimited("trim-range2", 10f, "optical range: " + e.Message); }
        }

        /// <summary>Some unit is under Strict EMCON (the hooks above do nothing otherwise).</summary>
        internal static bool _trimAny;

        static System.Reflection.Assembly _gameAsm;

        /// <summary>SensorUtils.BuildSolution: the growth speed of the firing solution (director: gain x crew x light; a gun without sensors: crew) times Guns/SolutionBuildSpeed. Only the speed: the accuracy of the director is untouched.</summary>
        static void SolSpeedPrefix(ref float multiplier)
        {
            var a = Active; if (a == null || !Enabled || a._solSpeed == null) return;
            multiplier *= Mathf.Max(a._solSpeed.Value, 0.01f);
        }

        /// <summary>GunFireControlModel.SolutionStep: the game takes a fixed 0.1/max(multiplier,1) off the solution for a changed course and again for a changed speed. Instead (Guns/ManeuverSolutionLoss > 0) the same
        /// count of times that percent of the grown solution, divided by max(multiplier,1) as the game does. The prefix hides the changes from the game, the postfix applies them.</summary>
        static void SolLossPrefix(bool firstCheckDone, ref bool headingChanged, ref bool speedChanged, out int __state)
        {
            __state = 0;
            var a = Active; if (a == null || !Enabled || a._solLoss == null || a._solLoss.Value <= 0f || !firstCheckDone) return;
            if (headingChanged) __state++;
            if (speedChanged) __state++;
            headingChanged = false; speedChanged = false;
        }

        static void SolLossPostfix(ref float __result, int __state, float multiplier)
        {
            if (__state <= 0) return;
            var a = Active; if (a == null || a._solLoss == null) return;
            float q = Mathf.Clamp01(a._solLoss.Value / 100f / Mathf.Max(multiplier, 1f));
            __result = Mathf.Clamp01(__result * Mathf.Pow(1f - q, __state));
        }

        /// <summary>Globals._gunLocalControlGainRate = the game's value * LocalControlGain (called at start and whenever an option changes).</summary>
        internal void ApplyLocalGain()
        {
            try
            {
                var ga = _tObjectBase != null ? _tObjectBase.Assembly : _gameAsm;
                if (ga == null) return;
                if (_fGainGlobal == null) _fGainGlobal = ga.GetType("SeaPower.Globals").GetField("_gunLocalControlGainRate", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (_fGainGlobal == null) return;
                if (float.IsNaN(_localGainBase)) _localGainBase = R.Fl(_fGainGlobal.GetValue(null));
                float m = Enabled && _gainLocal != null ? Mathf.Max(_gainLocal.Value, 0.01f) : 1f;
                _fGainGlobal.SetValue(null, _localGainBase * m);
            }
            catch (Exception e) { Warn("local control gain (Globals._gunLocalControlGainRate): " + e); }
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
            LsSync(__instance, false);
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
