using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace AlternativeWeaponsControl
{
    /// <summary>
    /// Fire shows the weapons' firing arcs and range on the map and in 3D while the target is being picked. The game draws them
    /// itself when Alt is held over a weapon in the status line (WeaponArcHoverManager: Hovered + WeaponArc3DRenderer.Show);
    /// here the same HoverInfo is handed to the manager without Alt, and the manager's per-frame Reconcile keeps it while targeting lasts.
    /// </summary>
    public partial class AwcPlugin
    {
        static object _zoneInfo, _zoneKeyPin; static string _zoneKeyId; static readonly object _zoneOwner = new object();
        static Type _tHoverMgr, _tHoverInfo;
        static PropertyInfo _pHovered;

        void HookZones(Harmony harmony, Assembly asm)
        {
            _tHoverMgr = asm.GetType("SeaPower.WeaponArcHoverManager");
            _tHoverInfo = asm.GetType("SeaPower.WeaponArcHoverManager+HoverInfo");
            var m = _tHoverMgr == null ? null : _tHoverMgr.GetMethod("Reconcile", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (m == null || _tHoverInfo == null) { Trace("WeaponArcHoverManager.Reconcile not found: Fire does not show zones"); return; }
            _pHovered = _tHoverMgr.GetProperty("Hovered", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            harmony.Patch(m, prefix: Hm("ZoneReconcile"));
            // The game's red max-range circle of the weapon being targeted (AttackingState.MaxRangeInLat bound to a MapEllipse): the model's value is 0
            // from the moment it is set (the circle never gets a radius), the real one is kept aside and given back to the game's own
            // bearing-preview code only for the duration of its call. (Patching the map controls' callbacks crashed the game at start.)
            var tAtk = asm.GetType("SeapowerUI.ViewModels.AttackingState");
            var pMax = tAtk == null ? null : tAtk.GetProperty("MaxRangeInLat", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _fMaxRange = tAtk == null ? null : tAtk.GetField("_maxRangeInLat", BindingFlags.Instance | BindingFlags.NonPublic);
            var mPrev = tAtk == null ? null : tAtk.GetMethod("UpdateBearingPreview", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (pMax != null && pMax.GetSetMethod(true) != null && _fMaxRange != null && mPrev != null)
            {
                harmony.Patch(pMax.GetSetMethod(true), prefix: Hm("MaxRangeSetPrefix"));
                harmony.Patch(mPrev, prefix: Hm("BearingPrefix"), postfix: Hm("BearingPostfix"));
            }
            else Trace("AttackingState.MaxRangeInLat / UpdateBearingPreview not found: the game's range circle stays");
        }

        static FieldInfo _fMaxRange;
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, object> RealMax = new System.Runtime.CompilerServices.ConditionalWeakTable<object, object>();
        static bool HideRing { get { var a = Active; return a != null && a._zoneFire != null && a._zoneFire.Value; } }

        // The circle is hidden only for a targeting state started by this mod's Fire button (the game's own Fire keeps its circle): the state
        // is marked when it first gets its range, inside the button's command (OurFireDepth > 0); the mark stays for the state's life.
        internal static int OurFireDepth;

        static void MaxRangeSetPrefix(object __instance, ref double value)
        {
            if (!HideRing) return;
            object old;
            bool mine = RealMax.TryGetValue(__instance, out old);
            if (!mine && OurFireDepth <= 0) return;
            if (mine) RealMax.Remove(__instance);
            RealMax.Add(__instance, value);
            value = 0.0;
        }

        internal static bool IsOurState(object st) { object o; return st != null && RealMax.TryGetValue(st, out o); }

        internal static void OurFireExecute(object cmd)
        {
            OurFireDepth++;
            try { R.Call(cmd, "Execute", (object)null); } finally { OurFireDepth--; }
        }

        static void BearingPrefix(object __instance)
        {
            object real;
            if (RealMax.TryGetValue(__instance, out real)) _fMaxRange.SetValue(__instance, (double)real);
        }

        static void BearingPostfix(object __instance)
        {
            object real;
            if (RealMax.TryGetValue(__instance, out real) && HideRing) _fMaxRange.SetValue(__instance, 0.0);
        }

        // The wedges are orange by the map theme's resource Map_FiringArc (MapDayTheme / MapNightTheme, merged into the map by ThemeDictionarySwapper
        // when the map loads and when the day / night theme changes). After each such swap the resource is replaced by a red brush in the dictionary
        // just merged: DynamicResource users (the wedges, created later or already there) take it. Nothing is scanned.
        // The wedges are orange by the map theme's resource Map_FiringArc (MapDayTheme / MapNightTheme, merged into the MapBase itself by the game's
        // ThemeDictionarySwapper, so it is not visible from the status views). When the zones start, the resource in the map's merged dictionaries is
        // replaced by a red brush (a no-op when it already is). The map is looked up in the UI tree once and remembered; no scan afterwards.
        // (A Harmony patch of ThemeDictionarySwapper.ApplyTheme crashed the game at start.)
        static WeakReference _mapRef; static Noesis.Brush _arcBrush;

        static void ArcColorOnce()
        {
            try
            {
                var map = _mapRef == null ? null : _mapRef.Target as Noesis.FrameworkElement;
                if (map == null)
                {
                    Noesis.DependencyObject root = null;
                    lock (StatusViews) { foreach (var w in StatusViews) { var v = w.Target as Noesis.FrameworkElement; if (v != null && v.IsVisible) { root = Noesis.VisualTreeHelper.GetRoot(v); break; } } }
                    if (root == null) return;
                    var stack = new Stack<Noesis.DependencyObject>();
                    stack.Push(root);
                    int budget = 60000;
                    while (stack.Count > 0 && --budget > 0 && map == null)
                    {
                        var e = stack.Pop();
                        var fe = e as Noesis.FrameworkElement;
                        if (fe != null && fe.GetType().Name == "MapBase") { map = fe; break; }
                        int n = Noesis.VisualTreeHelper.GetChildrenCount(e);
                        for (int k = n - 1; k >= 0; k--) { var c = Noesis.VisualTreeHelper.GetChild(e, k); if (c != null) stack.Push(c); }
                    }
                    if (map == null) return;
                    _mapRef = new WeakReference(map);
                }
                if (_arcBrush == null) _arcBrush = new Noesis.SolidColorBrush(Noesis.Color.FromArgb(0xDD, 235, 45, 45));
                foreach (var d in map.Resources.MergedDictionaries)
                {
                    if (d == null) continue;
                    var cur = d["Map_FiringArc"] as Noesis.Brush;
                    if (cur != null && !ReferenceEquals(cur, _arcBrush)) d["Map_FiringArc"] = _arcBrush;
                }
            }
            catch (Exception e) { Trace("arc colour: " + e.Message); }
        }

        static bool ZoneReconcile()
        {
            if (_zoneInfo == null) return true;
            try
            {
                var rp = _pHovered.GetValue(null, null);
                if (!ReferenceEquals(R.P(rp, "Value"), _zoneInfo))
                {
                    R.SetP(rp, "Value", _zoneInfo);
                    _tHoverMgr.Assembly.GetType("SeaPower.WeaponArc3DRenderer").GetMethod("Show", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(null, new[] { _zoneInfo });
                }
                return false;
            }
            catch (Exception e) { Trace("zones: " + e.Message); _zoneInfo = null; return true; }
        }

        /// <summary>Shows (id != null) or ends the zones of the Fire being targeted; pin = the one launcher, or null = all of the ammunition.</summary>
        static void ZonesUpdate(object unit, string id, object pin, IEnumerable systemsOfAmmo)
        {
            var a = Active;
            try
            {
                if (a == null || !Enabled || a._zoneFire == null || !a._zoneFire.Value || _tHoverInfo == null || _pHovered == null) id = null;
                if (id == null)
                {
                    if (_zoneInfo != null) { _zoneInfo = null; _zoneKeyId = null; _zoneKeyPin = null; _tHoverMgr.GetMethod("ClearCandidate").Invoke(null, new object[] { _zoneOwner }); }
                    return;
                }
                if (_zoneInfo != null && _zoneKeyId == id && ReferenceEquals(_zoneKeyPin, pin)) return;
                var wsT = unit.GetType().Assembly.GetType("SeaPower.WeaponSystem");
                var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(wsT));
                if (pin != null) list.Add(pin); else foreach (var s in systemsOfAmmo) if (s != null && wsT.IsInstanceOfType(s)) list.Add(s);
                if (list.Count == 0) return;
                float range = 0f;
                var am = R.Call(unit, "getAmmunitionByName", id);
                if (am != null) { var lr = R.F(R.F(am, "_ap"), "_launchRangesInUnity"); if (lr is UnityEngine.Vector2) range = ((UnityEngine.Vector2)lr).y; }
                var info = Activator.CreateInstance(_tHoverInfo);
                _tHoverInfo.GetField("Unit").SetValue(info, unit);
                _tHoverInfo.GetField("Systems").SetValue(info, list);
                _tHoverInfo.GetField("MapRangeInUnity").SetValue(info, range);
                ArcColorOnce();
                _zoneInfo = info; _zoneKeyId = id; _zoneKeyPin = pin;
                _tHoverMgr.GetMethod("SetCandidate").Invoke(null, new object[] { _zoneOwner, unit, list, range });   // starts the manager's tick
            }
            catch (Exception e) { Trace("zones update: " + e.Message); _zoneInfo = null; }
        }
    }
}
