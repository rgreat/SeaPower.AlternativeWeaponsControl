using System;
using System.Reflection;
using UnityEngine;

namespace AlternativeWeaponsControl
{
    public partial class AwcPlugin
    {
        // BarrelScatter: WeaponSystemGun.DeviateByCEP draws the shell scatter ONCE per salvo and adds it to _projectileAimGeoPosition; every barrel of the mount then
        // copies that aim point (GunBarrel.LaunchProjectile), so the shells of a twin / triple turret fly exactly parallel. The prefix remembers the aim point before the
        // game's offset; the prefix of fireBarrel gives every barrel after the first its own offset (same sampler, same sigmas) around that point.
        sealed class BsBox { public object Base; public int Frame; public int N; public System.Collections.Generic.List<Vector3> Offs = new System.Collections.Generic.List<Vector3>(); }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, BsBox> BsState = new System.Runtime.CompilerServices.ConditionalWeakTable<object, BsBox>();
        static MethodInfo _bsMiTable, _bsMiPattern, _bsMiCircular;

        // fireBarrel only sets the barrel's state to Fire; GunBarrel.update creates the projectile later (LaunchProjectile reads the gun's aim point THEN), so the offset is
        // given there: the prefix of GunBarrel.LaunchProjectile.
        static void ScatterPrefix(object __instance)
        {
            try
            {
                var a = Active; if (a == null || !Enabled || a._barrelScatter == null || !a._barrelScatter.Value) return;
                BsBox b; if (!BsState.TryGetValue(__instance, out b)) { b = new BsBox(); BsState.Add(__instance, b); }
                b.Base = R.F(__instance, "_projectileAimGeoPosition"); b.Frame = Time.frameCount; b.N = 0; b.Offs.Clear();
            }
            catch { }
        }

        static void BarrelPrefix(object __instance)   // __instance = the GunBarrel
        {
            try
            {
                var ws = R.F(__instance, "_vwsg"); if (ws == null) return;
                BsBox bx; if (!BsState.TryGetValue(ws, out bx) || bx.Base == null || Time.frameCount - bx.Frame > 3) return;
                bx.N++;
                var tgt = R.F(ws, "_targetObject"); var mount = R.F(ws, "_mountObject") as GameObject; if (tgt == null || mount == null) return;
                if (R.F(ws, "_currentShootingMode").ToString() != "Naval") return;
                var tr = R.P(tgt, "transform") as Transform; if (tr == null) return;
                var ammo = R.F(ws, "_ammoForEngage"); var ap = R.F(ammo, "_ap"); if (ap == null) return;
                Vector3 los = tr.position - mount.transform.position; float mag = los.magnitude;
                float cep = R.Fl(R.F(ws, "_cepModifier"));
                if (_bsMiTable == null)
                {
                    var gb = GameAssemblyStatic().GetType("SeaPower.GunBallistics");
                    _bsMiTable = gb.GetMethod("GetTable", BindingFlags.Static | BindingFlags.Public);
                    _bsMiPattern = gb.GetMethod("SamplePatternOffset", BindingFlags.Static | BindingFlags.Public);
                    _bsMiCircular = gb.GetMethod("SampleCircularOffset", BindingFlags.Static | BindingFlags.Public);
                }
                if (_bsMiTable == null || _bsMiPattern == null || _bsMiCircular == null) return;
                var table = _bsMiTable.Invoke(null, new object[] { ap });
                Vector3 off;
                if (table != null && mag > R.Fl(R.F(table, "_minRange")) && mag <= R.Fl(R.F(table, "_maxRange")))
                    off = (Vector3)_bsMiPattern.Invoke(null, new object[] { table, mag, los, cep });
                else
                {
                    var lr = R.F(ap, "_launchRangesInUnity"); float maxU = Mathf.Max(lr is Vector2 ? ((Vector2)lr).y : 1f, 1f);
                    off = (Vector3)_bsMiCircular.Invoke(null, new object[] { cep * R.Fl(R.F(ap, "_circularErrorProbable")) * mag / maxU / 1.1774f, los });
                }
                var pos = R.Call(bx.Base, "OffsetByGridVector", off);
                if (pos != null) R.SetF(ws, "_projectileAimGeoPosition", pos);
                if (_debug)
                {
                    bx.Offs.Add(off);
                    var bl = R.F(ws, "_barrels") as System.Collections.IList;
                    if (bl != null && bx.Offs.Count == bl.Count && bl.Count > 1)
                    {
                        var ci = System.Globalization.CultureInfo.InvariantCulture; var sb2 = new System.Text.StringBuilder();
                        float apart = 0f;
                        for (int k = 0; k < bx.Offs.Count; k++) { if (k > 0) sb2.Append('/'); sb2.Append((bx.Offs[k].magnitude * 67.200066f).ToString("0", ci)); for (int q = 0; q < k; q++) apart = Mathf.Max(apart, (bx.Offs[k] - bx.Offs[q]).magnitude); }
                        Trace("barrel scatter: " + R.S(R.F(ws, "_systemName")) + " x" + bl.Count + " own offsets m " + sb2 + ", farthest pair " + (apart * 67.200066f).ToString("0", ci) + " m");
                    }
                }
            }
            catch { }
        }
    }
}
