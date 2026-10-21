using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace AlternativeWeaponsControl
{
    /// <summary>
    /// Binoculars at the maximum magnification (the last optical profile, e.g. 20x120 STAB) start with the game's post-process filter
    /// switched on (night / thermal look). With [Optics] BinocularsNormalView the filter starts off, i.e. the normal image; the game's
    /// own filter key (the toggle key) switches it on again. [Optics] NormalViewDayOnly: only while the sun is up, at night the game's default stays.
    /// </summary>
    public partial class AwcPlugin
    {
        void HookOptics(Harmony harmony, Assembly asm)
        {
            var cm = asm.GetType("SeaPower.CameraManager");
            var m = cm == null ? null : cm.GetMethod("ApplyOpticalPostProcessProfile", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (m == null) { Trace("CameraManager.ApplyOpticalPostProcessProfile not found: no normal binocular view option"); return; }
            harmony.Patch(m, postfix: Hm("OpticsPostfix"));
        }

        static void OpticsPostfix(object __instance, object profile)
        {
            try
            {
                var a = Active;
                if (a == null || !Enabled || a._optNormal == null || !a._optNormal.Value || profile == null) { Trace("optics: skipped (option " + (a != null && a._optNormal != null && a._optNormal.Value) + ", profile " + (profile != null) + ")"); return; }
                var id = R.S(R.P(profile, "Id"));
                var pp = R.F(__instance, "_opticalPostProcess"); if (pp == null) { Trace("optics " + id + ": no post-process component"); return; }
                var mode = R.Rv(R.P(__instance, "CurrentOpticalViewMode"));
                var list = R.F(__instance, "_availableOpticalViewProfiles") as System.Collections.IList;
                bool last = list != null && list.Count > 0 && ReferenceEquals(list[list.Count - 1], profile);
                var mats = R.F(pp, "fullscreenMaterials") as Array;
                var names = R.P(profile, "PostProcessFilters") as string[];
                Trace("optics " + id + ": mode " + mode + ", profiles " + (list == null ? -1 : list.Count) + ", last " + last + ", materials " + (mats == null ? -1 : mats.Length) +
                      ", filters " + (names == null ? "-" : string.Join("|", names)) + ", default " + R.S(R.P(profile, "DefaultFilter")));
                if (mode == null || mode.ToString() != "Binoculars") return;
                if (!last) return;   // the maximum magnification only
                if (a._optDayOnly != null && a._optDayOnly.Value && !SunIsUp()) { Trace("optics: night, the game's default stays"); return; }
                if (mats == null || mats.Length == 0) return;
                R.SetF(pp, "effectEnabled", false);
                R.SetF(pp, "toggleKey", R.F(pp, "originalToggleKey"));   // the game's key switches the filter on again
                Trace("optics " + id + ": the filter is off, normal view");
            }
            catch (Exception e) { Trace("optics: " + e); }
        }

        static bool SunIsUp()
        {
            try
            {
                var a = _tObjectBase.Assembly;
                var ds = a.GetType("SeaPower.DynamicSky");
                var sg = a.GetType("SeaPower.Singleton`1").MakeGenericType(ds);
                var inst = sg.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null, null);
                if (inst == null) return true;
                return R.Fl(R.Call(inst, "getSunlightIntensity")) >= 0.5f;
            }
            catch { return true; }
        }
    }
}
