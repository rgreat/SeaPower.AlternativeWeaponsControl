using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace AlternativeWeaponsControl
{
    public partial class AwcPlugin
    {
        // BigGunsLocalSolution: a naval gun with associated sensors takes its firing solution from the best WORKING one every check (WeaponSystemGun.OnUpdate); with all of them
        // destroyed or off the solution is 0 for ever (a gun with no sensors at all builds it itself with SensorUtils.BuildSolution). Real turrets of a big calibre have local
        // control and their own rangefinders: such a gun (shell heavier than LsMinKg, about 50 mm and more) builds its solution itself, as a gun without sensors does; its aiming
        // error still uses the local control gain (worse than a director). Smaller guns stay as in the game.
        const float LsMinKg = 2f;

        sealed class LsBox { public float Sol, H, V; public bool First; public double LastCheck = -1; public object Tgt; }
        static readonly ConditionalWeakTable<object, LsBox> LsState = new ConditionalWeakTable<object, LsBox>();
        static MethodInfo _lsBuild;

        static bool LsEligible(object ws, out object tgt)
        {
            tgt = R.F(ws, "_targetObject");
            if (tgt == null) return false;
            var a = Active; if (a == null || !Enabled || a._bigLocal == null || !a._bigLocal.Value) return false;
            if (R.F(ws, "_currentShootingMode").ToString() != "Naval") return false;
            if (R.B(R.Call(tgt, "isAirTarget"))) return false;
            var vwp = R.F(ws, "_vwp");
            var ap = R.F(R.F(ws, "_ammoForEngage"), "_ap"); if (ap == null || R.Fl(R.F(ap, "_massInKg")) < LsMinKg) return false;
            return R.Call(ws, "GetFireControlSensor") == null;   // no working director
        }

        /// <summary>Own solution of a big gun without a working director: grows every check like the game's unsensored gun; written to _solution.</summary>
        static void LsSync(object ws, bool step)
        {
            try
            {
                object tgt;
                if (!LsEligible(ws, out tgt)) { LsBox old; if (LsState.TryGetValue(ws, out old)) LsState.Remove(ws); return; }
                LsBox b; if (!LsState.TryGetValue(ws, out b)) { b = new LsBox(); LsState.Add(ws, b); }
                if (!ReferenceEquals(b.Tgt, tgt)) { b.Tgt = tgt; b.Sol = 0f; b.First = false; b.LastCheck = Convert.ToDouble(R.F(ws, "_checkSolutionTime")); }
                double ct = Convert.ToDouble(R.F(ws, "_checkSolutionTime"));
                if (step && ct != b.LastCheck)
                {
                    b.LastCheck = ct;
                    if (_lsBuild == null) _lsBuild = GameAssemblyStatic().GetType("SeaPower.SensorUtils").GetMethod("BuildSolution", BindingFlags.Static | BindingFlags.Public);
                    var crew = R.F(R.F(ws, "_baseObject"), "_crew"); float mult = crew == null ? 1f : R.Fl(R.F(crew, "_fcSolutionRateMultiplier"));
                    var ac = Active; float k = ac == null || ac._localGrowth == null ? 0.75f : ac._localGrowth.Value / 100f;
                    float best = 0f; var sl = R.F(R.F(ws, "_vwp"), "_associatedSensors") as IList;
                    if (sl != null)
                        for (int i = 0; i < sl.Count; i++)
                        {
                            var sn = sl[i]; if (sn == null) continue;
                            R.Call(sn, "ResolveFireControlProfile");
                            float gn = R.Fl(R.F(sn, "_fcSolutionGainRate")); if (gn <= 0f) continue;
                            gn *= Mathf.Lerp(0.15f, 1f, R.Fl(R.Call(sn, "GetFireControlLightFactor")));
                            if (gn > best) best = gn;
                        }
                    if (best <= 0f) best = MissionDirectorAverage();   // a gun with no director at all: the average director of the mission
                    if (best > 0f) mult *= best * k;   // the best director's speed (working or not) x the option; nothing to compare with: the crew speed
                    var args = new object[] { b.Sol, b.H, b.V, b.First, tgt, R.F(ws, "_mountObject"), mult };
                    _lsBuild.Invoke(null, args);
                    b.Sol = (float)args[0]; b.H = (float)args[1]; b.V = (float)args[2]; b.First = (bool)args[3];
                }
                R.SetF(ws, "_solution", b.Sol);
            }
            catch { }
        }

        // splash spotting: the game adds Random(0.1..0.15) x SpotQuality(miss) to the solutions of WORKING sensors when a shell of the salvo lands and the fall of shot can be
        // seen (round-tracking sensor or CanObserveFallOfShot); a gun with dead sensors gets nothing. Prefix: the head of _pendingSplashes; postfix: it was consumed -> same bonus.
        static object _lsGun; static int _lsPend; static float _lsMiss; static double _lsDue; static MethodInfo _lsSpot;

        static float _lsAvg, _lsAvgAt = -1000f; static Type _tObjMgr, _tSingletonGeneric;

        /// <summary>Average gain of the fire-control sensors of the gun directors of all units now in the mission (best sensor of every gun that has any; after the mod's lift), cached for a minute. 0 = nothing to average.</summary>
        static float MissionDirectorAverage()
        {
            float now = Time.unscaledTime;
            if (now - _lsAvgAt < 60f && now >= _lsAvgAt) return _lsAvg;
            _lsAvgAt = now; _lsAvg = 0f;
            try
            {
                var asm = GameAssemblyStatic();
                if (_tObjMgr == null) { _tObjMgr = asm.GetType("SeaPower.ObjectsManager"); _tSingletonGeneric = asm.GetType("SeaPower.Singleton`1"); }
                var mgr = _tSingletonGeneric.MakeGenericType(_tObjMgr).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public).GetValue(null, null);
                var all = R.F(mgr, "_listOfAllObjects") as IList; if (all == null) return 0f;
                float sum = 0f; int n = 0;
                for (int i = 0; i < all.Count; i++)
                {
                    var wss = R.F(R.F(all[i], "_obp"), "_weaponSystems") as IList; if (wss == null) continue;
                    for (int j = 0; j < wss.Count; j++)
                    {
                        var w = wss[j]; if (w == null || w.GetType().Name != "WeaponSystemGun") continue;
                        var sl = R.F(R.F(w, "_vwp"), "_associatedSensors") as IList; if (sl == null) continue;
                        float best = 0f;
                        for (int q = 0; q < sl.Count; q++)
                        {
                            var sn = sl[q]; if (sn == null) continue;
                            R.Call(sn, "ResolveFireControlProfile");
                            float gn = R.Fl(R.F(sn, "_fcSolutionGainRate")); if (gn > best) best = gn;
                        }
                        if (best > 0f) { sum += best; n++; }
                    }
                }
                if (n > 0) _lsAvg = sum / n;
            }
            catch { }
            return _lsAvg;
        }

        static void LsPrefix(object __instance)
        {
            _lsGun = null;
            try
            {
                var l = R.F(__instance, "_pendingSplashes") as IList; if (l == null || l.Count == 0) return;
                var v = (Vector2)l[0]; _lsGun = __instance; _lsPend = l.Count; _lsDue = v.x; _lsMiss = v.y;
            }
            catch { _lsGun = null; }
        }

        static void LsPostfix(object __instance)
        {
            if (R.F(__instance, "_targetObject") == null) return;
            LsSync(__instance, true);
            try
            {
                if (!ReferenceEquals(_lsGun, __instance)) return;
                var l = R.F(__instance, "_pendingSplashes") as IList; if (l == null || l.Count >= _lsPend) return;
                LsBox b; if (!LsState.TryGetValue(__instance, out b)) return;
                bool tracked = R.Call(__instance, "GetRoundTrackingSensor") != null;
                if (!tracked && !R.B(R.Call(__instance, "CanObserveFallOfShot"))) return;
                if (_lsSpot == null) _lsSpot = GameAssemblyStatic().GetType("SeaPower.GunFireControlModel").GetMethod("SpotQuality", BindingFlags.Static | BindingFlags.Public);
                float q = (float)_lsSpot.Invoke(null, new object[] { _lsMiss * 67.200066f, tracked });
                b.Sol = Mathf.Min(1f, b.Sol + UnityEngine.Random.Range(0.1f, 0.15f) * q);
                R.SetF(__instance, "_solution", b.Sol);
            }
            catch { }
        }
    }
}
