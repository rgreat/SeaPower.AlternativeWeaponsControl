using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using Noesis;

namespace AlternativeWeaponsControl
{
    public partial class AwcPlugin
    {
        // Tooltip of a gun in the Weapons tab: what it aims with (radar / optical director / local control), the director's numbers
        // after the mod's multipliers, and the aiming error the game draws for a salvo (SurfaceSigmas, with the mod's postfix applied).
        const float UnityToM = 67.200066f;
        static MethodInfo _miSurface, _miPattern, _miTable;

        static string N1(float f) { return f.ToString(f >= 100f ? "0" : f >= 10f ? "0.#" : "0.##", CultureInfo.InvariantCulture); }

        /// <summary>Text for the tooltip of a gun / its group header; null for a mount that is not a gun.</summary>
        static string GunInfo(object ws)
        {
            try
            {
                if (ws == null || R.S(R.F(ws, "_weaponType")) != "Gun") return null;
                var sb = new StringBuilder();
                var fc = R.Call(ws, "GetFireControlSensor");
                var sens = R.F(R.F(ws, "_vwp"), "_associatedSensors") as IList;
                bool hasDirs = sens != null && sens.Count > 0;
                if (fc != null)
                {
                    bool radar = _tSensorRadar != null && _tSensorRadar.IsInstanceOfType(fc);
                    var nm = Cap(R.S(R.Rv(R.P(fc, "DisplayName"))) ?? R.S(R.F(fc, "_systemName")) ?? "director");
                    sb.Append(Loc.F(Trimmed(fc) ? "gt.trimmed" : radar ? "gt.radar" : "gt.optical", nm)).Append('\n');
                    float gain = R.Fl(R.F(fc, "_fcSolutionGainRate")), min = R.Fl(R.F(fc, "_fcMinSolution"));
                    sb.Append(Loc.F("gt.dir", N1(gain), Math.Round(min * 100f).ToString(CultureInfo.InvariantCulture))).Append(R.B(R.F(fc, "_fcTracksRounds")) ? Loc.T("gt.tracks") : "").Append('\n');
                }
                else
                {
                    sb.Append(Loc.T(hasDirs ? "gt.localNoDir" : "gt.local")).Append('\n');
                    sb.Append(Loc.F("gt.localGain", N1(R.Fl(GetLocalGain())))).Append('\n');
                }
                float sol = R.Fl(R.F(ws, "_solution"));
                sb.Append(Loc.F("gt.solution", Math.Round(sol * 100f).ToString(CultureInfo.InvariantCulture)));
                if (R.F(ws, "_targetObject") != null)
                {
                    bool wait = false; try { wait = R.B(R.Call(ws, "NeedsSpottingFire")); } catch { }
                    if (wait) sb.Append(" · ").Append(Loc.T("gt.waits"));
                }
                sb.Append('\n');
                // the aiming error: at the target's range, else at reference ranges
                object st = R.Call(ws, "CurrentFcState");
                var tgt = R.F(ws, "_targetObject");
                float dist = 0f, spd = 0f, tof = 0f;
                if (tgt != null)
                {
                    var tp = R.P(R.P(tgt, "transform"), "position"); var mp = R.P(R.F(ws, "_mountObject"), "transform");
                    dist = (float)Math.Sqrt(Dist2(tp, R.P(mp, "position")));
                    spd = R.Fl(R.P(R.P(tgt, "UnityVelocityVector"), "magnitude"));
                    double t = Convert.ToDouble(R.F(ws, "_projectileToTargetTime") ?? 0.0, CultureInfo.InvariantCulture);
                    tof = (t > 0 && t < 10000) ? (float)t : 0f;
                }
                if (st != null)
                {
                    if (tgt != null && dist > 0f) sb.Append(ErrLine(ws, st, dist, spd, tof, true));
                    else foreach (var d in new[] { 4000f, 8000f, 12000f }) sb.Append(ErrLine(ws, st, d / UnityToM, 0f, 0f, false));
                }
                sb.Append(Loc.T("gt.note"));
                return sb.ToString();
            }
            catch (Exception e) { Trace("gun tooltip: " + e.Message); return null; }
        }

        static object GetLocalGain()
        {
            try { return GameAssemblyStatic().GetType("SeaPower.Globals").GetField("_gunLocalControlGainRate", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null); }
            catch { return 0f; }
        }

        static float Dist2(object a, object b)
        {
            float ax = R.Fl(R.F(a, "x")), ay = R.Fl(R.F(a, "y")), az = R.Fl(R.F(a, "z"));
            float bx = R.Fl(R.F(b, "x")), by = R.Fl(R.F(b, "y")), bz = R.Fl(R.F(b, "z"));
            return (ax - bx) * (ax - bx) + (ay - by) * (ay - by) + (az - bz) * (az - bz);
        }

        /// <summary>One line: the aim point error (range / bearing / target's movement / own motion) at a distance, in metres.</summary>
        static string ErrLine(object ws, object st, float distUnity, float spd, float tof, bool real)
        {
            try
            {
                if (_miSurface == null) _miSurface = GameAssemblyStatic().GetType("SeaPower.GunFireControlModel").GetMethod("SurfaceSigmas", BindingFlags.Static | BindingFlags.Public);
                if (_miSurface == null) return "";
                var a = new object[] { st, distUnity, spd, tof, false, 0f, 0f, 0f, 0f };
                _miSurface.Invoke(null, a);
                float rs = (float)a[5] * UnityToM, bs = (float)a[6] * UnityToM, ps = (float)a[7] * UnityToM, ss = (float)a[8] * UnityToM;
                var sb = new StringBuilder();
                sb.Append((real ? Loc.F("gt.errAt", N1(distUnity * UnityToM / 1000f)) : Loc.F("gt.errRef", N1(distUnity * UnityToM / 1000f))).TrimEnd() + " ");
                sb.Append(Loc.F("gt.err", N1(rs), N1(bs)));
                if (real && ps > 0.5f) sb.Append(Loc.F("gt.errPred", N1(ps)));
                if (ss > 0.5f) sb.Append(Loc.F("gt.errStab", N1(ss)));
                sb.Append('\n');
                if (real)
                {
                    var ap = R.F(R.F(ws, "_ammoForEngage"), "_ap");
                    if (ap != null)
                    {
                        var gt = GameAssemblyStatic();
                        if (_miTable == null) _miTable = gt.GetType("SeaPower.GunBallistics").GetMethod("GetTable", BindingFlags.Static | BindingFlags.Public);
                        if (_miPattern == null) _miPattern = gt.GetType("SeaPower.GunFireControlModel").GetMethod("PatternSigmas", BindingFlags.Static | BindingFlags.Public);
                        var table = _miTable.Invoke(null, new[] { ap });
                        var p = new object[] { table, ap, distUnity, R.Fl(R.F(ws, "_cepModifier")), 0f, 0f };
                        _miPattern.Invoke(null, p);
                        sb.Append(Loc.F("gt.scatter", N1((float)p[4] * UnityToM), N1((float)p[5] * UnityToM))).Append('\n');
                    }
                }
                return sb.ToString();
            }
            catch (Exception e) { Trace("gun tooltip error: " + e.Message); return ""; }
        }

        // The tooltip under the mouse: its text is rebuilt several times a second (the numbers change with the situation) and it stays
        // as long as the mouse is over the element (the default show time is a few seconds).
        static TextBlock _liveTip; static Func<object> _liveWs; static FrameworkElement _liveOwner; static float _liveNext;

        /// <summary>Tooltip of a hover target, built when the mouse comes and refreshed while it is shown; ws returns the gun.</summary>
        static void GunTip(FrameworkElement e, Func<object> ws)
        {
            e.MouseEnter += (o, a) =>
            {
                try
                {
                    var s = GunInfo(ws());
                    if (s == null) { ToolTipService.SetToolTip(e, null); _liveTip = null; return; }
                    var tb = new TextBlock { Text = s.TrimEnd('\n'), TextWrapping = TextWrapping.NoWrap, MaxWidth = float.PositiveInfinity };
                    ToolTipService.SetToolTip(e, tb);
                    ToolTipService.SetShowDuration(e, int.MaxValue);
                    _liveTip = tb; _liveWs = ws; _liveOwner = e; _liveNext = 0f;
                }
                catch (Exception ex) { Trace("gun tooltip: " + ex.Message); }
            };
            e.MouseLeave += (o, a) => { if (ReferenceEquals(_liveOwner, e)) { _liveTip = null; _liveOwner = null; } };
        }

        /// <summary>Called from Update: refreshes the text of the shown gun tooltip.</summary>
        static void GunTipStep(float now)
        {
            var tb = _liveTip;
            if (tb == null || now < _liveNext) return;
            _liveNext = now + 0.25f;
            try
            {
                if (_liveOwner != null && !_liveOwner.IsMouseOver) { _liveTip = null; _liveOwner = null; return; }
                var s = GunInfo(_liveWs());
                if (s != null) { s = s.TrimEnd('\n'); if (tb.Text != s) tb.Text = s; }
            }
            catch (Exception ex) { Trace("gun tooltip refresh: " + ex.Message); _liveTip = null; }
        }
    }
}
