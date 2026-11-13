using System;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace AlternativeWeaponsControl
{
    public partial class AwcPlugin
    {
        // Trace line of every salvo of a gun at a surface target (all ships, not only the player's), to count the hit rate together with the hits of the damage
        // mod's trace: the errors the game used and the aim point error it drew. Debug only.
        //   salvo <ship> <gun> xN <ammo> -> <target> R=<km> v=<kn> tof=<s> sol=<%> gain=<g> | sigma m: range / bearing / predict / stab | shell scatter m: range / defl |
        //   drawn aim error m: range / bearing, shell miss m
        static int _salvoNo;

        /// <summary>Postfix of WeaponSystemGun.DeviateByCEP: the game calls it once per salvo (a burst shot) right before the barrels fire.</summary>
        static void SalvoLogPostfix(object __instance)
        {
            if (!_debug) return;
            try
            {
                var ws = __instance;
                if (R.F(ws, "_currentShootingMode").ToString() != "Naval") return;
                var tgt = R.F(ws, "_targetObject"); if (tgt == null) return;
                var bo = R.F(ws, "_baseObject");
                var mount = R.F(ws, "_mountObject") as GameObject; if (mount == null) return;
                var tr = R.P(tgt, "transform") as Transform; if (tr == null) return;
                Vector3 to = tr.position - mount.transform.position; float dist = to.magnitude;
                to.y = 0f; if (to.sqrMagnitude < 1e-10f) return; to.Normalize();
                Vector3 perp = Vector3.Cross(Vector3.up, to);
                var dev = (Vector3)R.F(ws, "_deviationVector");
                float spd = R.Fl(R.P(R.P(tgt, "UnityVelocityVector"), "magnitude"));
                double tt = Convert.ToDouble(R.F(ws, "_projectileToTargetTime") ?? 0.0, CultureInfo.InvariantCulture);
                float tof = (tt > 0 && tt < 10000) ? (float)tt : 0f;
                var st = R.Call(ws, "CurrentFcState");
                var ap = R.F(R.F(ws, "_ammoForEngage"), "_ap");
                var barrels = R.F(ws, "_barrels") as IList;
                const float U = 67.200066f;
                var sb = new StringBuilder();
                sb.Append("salvo ").Append(UName(bo)).Append(' ').Append(R.S(R.F(ws, "_systemName"))).Append(" x").Append(barrels == null ? 0 : barrels.Count)
                  .Append(' ').Append(ap == null ? "?" : R.S(R.F(ap, "_ammunitionFileName"))).Append(" -> ").Append(UName(tgt))
                  .Append(" R=").Append((dist * U / 1852f).ToString("0.00", CultureInfo.InvariantCulture)).Append("nm v=").Append((spd * U * 1.94384f).ToString("0", CultureInfo.InvariantCulture))
                  .Append("kn tof=").Append(tof.ToString("0.0", CultureInfo.InvariantCulture)).Append('s');
                if (st != null)
                {
                    sb.Append(" sol=").Append((R.Fl(R.F(st, "Solution")) * 100f).ToString("0", CultureInfo.InvariantCulture)).Append("% gain=").Append(R.Fl(R.F(st, "Gain")).ToString("0.00", CultureInfo.InvariantCulture));
                    if (_miSurface == null) _miSurface = GameAssemblyStatic().GetType("SeaPower.GunFireControlModel").GetMethod("SurfaceSigmas", BindingFlags.Static | BindingFlags.Public);
                    if (_miSurface != null)
                    {
                        var a = new object[] { st, dist, spd, tof, false, 0f, 0f, 0f, 0f };
                        _miSurface.Invoke(null, a);
                        sb.Append(" | sigma m: ").Append(M0((float)a[5] * U)).Append('/').Append(M0((float)a[6] * U)).Append('/').Append(M0((float)a[7] * U)).Append('/').Append(M0((float)a[8] * U));
                    }
                }
                sb.Append(" | drawn aim error m: ").Append(M0(Vector3.Dot(dev, to) * U)).Append('/').Append(M0(Vector3.Dot(dev, perp) * U))
                  .Append(", shell miss ").Append(M0(R.Fl(R.F(ws, "_lastPatternMissUnity")) * U));
                Trace(sb.ToString());
                _salvoNo++;
            }
            catch (Exception e) { TraceLimited("salvo-log", 10f, "salvo log: " + e.Message); }
        }

        static string M0(float v) { return v.ToString("0", CultureInfo.InvariantCulture); }
    }
}
