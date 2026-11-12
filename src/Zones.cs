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
        static object _zoneInfo, _zoneKeyPin, _zoneUnit; static string _zoneKeyId; static readonly object _zoneOwner = new object();
        static Type _tHoverMgr, _tHoverInfo;
        static PropertyInfo _pHovered;

        void HookZones(Harmony harmony, Assembly asm)
        {
            _zonesHarmony = harmony;
            try
            {
                var t3 = asm.GetType("SeaPower.WeaponArc3DRenderer");
                var m3 = t3 == null ? null : t3.GetMethod("Show", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                _fArcObjs = t3 == null ? null : t3.GetField("_arcObjects", BindingFlags.Static | BindingFlags.NonPublic);
                if (m3 != null && _fArcObjs != null) harmony.Patch(m3, postfix: Hm("Arc3DShowPostfix"));
            }
            catch (Exception e) { Trace("arc3d patch: " + e.Message); }
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
            // the weapon panel at the bottom of the screen (and the game's other Fire menus) start the targeting with AttackingState.TryStart
            var ts = tAtk == null ? null : tAtk.GetMethod("TryStart", BindingFlags.Static | BindingFlags.Public);
            if (ts != null) harmony.Patch(ts, prefix: Hm("TryStartPrefix"), finalizer: Hm("TryStartFinalizer"));
            else Trace("AttackingState.TryStart not found: the bottom panel does not act as Fire of the mod");
        }

        // ---- [UI] BottomPanelFire: a click on a weapon of the bottom panel works like the mod's Fire (red zones, no range circle) ------------------------------------
        static void TryStartPrefix(bool isWaypointEdit, ref bool __state)
        {
            __state = false;
            var a = Active;
            if (a == null || !Enabled || a._bottomFire == null || !a._bottomFire.Value || isWaypointEdit || OurFireDepth > 0) return;
            OurFireDepth++; __state = true;
            Pending = null;   // a launcher tie of an earlier Fire does not apply to this click
        }
        static void TryStartFinalizer(bool __state) { if (__state && OurFireDepth > 0) OurFireDepth--; }

        static bool _bottomOn; static float _nextBottom;
        static readonly Dictionary<object, Dictionary<string, object>> _bottomEntries = new Dictionary<object, Dictionary<string, object>>(new RefCmp());
        /// <summary>The zones of a targeting started from the bottom panel do not depend on the Weapons tab being open: shown / ended from here.</summary>
        static void BottomZonesStep(float now)
        {
            if (now < _nextBottom) return;
            _nextBottom = now + 0.1f;
            var a = Active;
            if (a == null || !Enabled || a._bottomFire == null || !a._bottomFire.Value || a._zoneFire == null || !a._zoneFire.Value) { if (_bottomOn) { _bottomOn = false; ZonesUpdate(_zoneUnit, null, null, null); } return; }
            object st = null;
            try
            {
                if (_fMgvm == null) { var globals = _tHoverMgr.Assembly.GetType("SeaPower.Globals"); if (globals != null) _fMgvm = globals.GetField("_mainGameViewModel", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic); }
                var mgvm = _fMgvm == null ? null : _fMgvm.GetValue(null);
                var cur = R.Rv(R.P(R.P(mgvm, "StateManager"), "CurrentState"));
                if (cur != null && cur.GetType().Name == "AttackingState" && IsOurState(cur)) st = cur;
            }
            catch { }
            var unit = st == null ? null : R.P(st, "Source");
            var pp = Pending;
            if (st != null && unit != null && !(pp != null && ReferenceEquals(pp.Unit, unit)) && R.B(R.P(unit, "IsPlayerObject")))
            {
                var id = R.S(R.P(st, "Ammunition"));
                // the weapons of the ammunition as the tab's Fire takes them: WeaponEntry.WeaponSystems of the game's ammunition group (guns included)
                IEnumerable list = null;
                try
                {
                    var obp = R.F(unit, "_obp");
                    var g = R.F(obp, "_launcherAmmunitionGroups") as IDictionary;
                    object grp = g != null && g.Contains(id) ? g[id] : null;
                    if (grp == null) { var gg = R.F(obp, "_gunAmmunitionGroups") as IDictionary; grp = gg != null && gg.Contains(id) ? gg[id] : null; }
                    if (grp != null)
                    {
                        object key = unit, e;
                        Dictionary<string, object> per;
                        if (!_bottomEntries.TryGetValue(unit, out per)) { per = new Dictionary<string, object>(); _bottomEntries[unit] = per; }
                        if (!per.TryGetValue(id, out e)) { e = Activator.CreateInstance(_tHoverMgr.Assembly.GetType("SeapowerUI.ViewModels.WeaponEntry"), unit, grp, false); per[id] = e; }
                        list = R.P(e, "WeaponSystems") as IEnumerable;
                    }
                }
                catch (Exception ex) { TraceLimited("bz", 5f, "bottom zones: " + ex.Message); }
                if (list == null) return;
                _bottomOn = true;
                ZonesUpdate(unit, id, null, list);
            }
            else if (_bottomOn) { _bottomOn = false; ZonesUpdate(_zoneUnit, null, null, null); }
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
            ZoneRangeFollow(__instance, value);
            value = 0.0;
        }

        // The wedges show the range the game itself works out for the weapon against what the cursor is over (AttackingState sets MaxRangeInLat on every hover:
        // kinematic / sensor / terrain range against that contact, e.g. a SAM against a ship), not the ammunition's nominal range. The game's circle stays hidden.
        static float _zoneRange, _zoneNominal; static IList _zoneList;
        static void ZoneRangeFollow(object st, double lat)
        {
            if (_zoneInfo == null || _zoneList == null || _tHoverInfo == null) return;
            try
            {
                if (!ReferenceEquals(R.P(st, "Source"), _zoneUnit) || R.S(R.P(st, "Ammunition")) != _zoneKeyId) return;
                float r = (float)(lat / 0.0006047522103382444);
                if (r <= 0f || r > 1e7f) r = _zoneNominal;
                if (r <= 0f || Math.Abs(r - _zoneRange) < _zoneRange * 0.005f) return;
                _zoneRange = r;
                var info = Activator.CreateInstance(_tHoverInfo);
                _tHoverInfo.GetField("Unit").SetValue(info, _zoneUnit);
                _tHoverInfo.GetField("Systems").SetValue(info, _zoneList);
                _tHoverInfo.GetField("MapRangeInUnity").SetValue(info, r);
                _zoneInfo = info;   // ZoneReconcile hands it to the manager and the renderer on the next frame
            }
            catch (Exception e) { TraceLimited("zr", 5f, "zone range: " + e.Message); }
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

        // ---- the colour of the wedges on the 2D map (Sea Power 0.8.7) -------------------------------------------------------------------------------------------------
        // 0.8.7 draws the wedge through the map's geometry store: WeaponArcSegmentViewModel.Render opens a Pie slot with ctx.Palette.Stroke(MapColor.FiringArc, thickness).
        // The colour is the palette's (MapPalette, public Override(MapColor, day, night) raises Changed and the map redraws). While the zones of our Fire are shown
        // FiringArc is overridden with red on the palette(s) of the map(s) and put back when they end. A weak red fill is added by a postfix of Render (a second Pie
        // slot with the same geometry and a Fill), made at the first Fire. Older versions (0.8.6: MapPie with a theme brush) have no such class: nothing is done.
        static readonly List<WeakReference> _maps = new List<WeakReference>();
        sealed class PaletteSave { public object Palette; public object Day, Night; }
        static readonly List<PaletteSave> _paletteSaves = new List<PaletteSave>();
        static Harmony _zonesHarmony; static bool _fillPatched, _fillFailed;

        static void ArcColor(bool red)
        {
            try
            {
                if (!red) { RestorePalettes(); return; }
                if (_paletteSaves.Count > 0) return;
                var asm = _tHoverMgr.Assembly;
                var tMapBase = asm.GetType("MapControl.MapBase"); var tColor = asm.GetType("SeaPower.LiveMap.MapColor");
                if (tMapBase == null || tColor == null) { TraceLimited("arc0", 5f, "arc colour: MapBase / MapColor not found (an older game version): the wedge colour is not changed"); return; }
                var getPalette = tMapBase.GetMethod("GetPalette", BindingFlags.Static | BindingFlags.Public);
                var colour = Enum.Parse(tColor, "FiringArc");
                _maps.RemoveAll(w => !w.IsAlive);
                if (_maps.Count == 0)
                {
                    Noesis.DependencyObject root = null;
                    lock (StatusViews) { foreach (var w in StatusViews) { var v = w.Target as Noesis.FrameworkElement; if (v != null && v.IsVisible) { root = Noesis.VisualTreeHelper.GetRoot(v); break; } } }
                    if (root == null) { Trace("arc colour: no visible status view to find the map from"); return; }
                    var st = new Stack<Noesis.DependencyObject>(); st.Push(root);
                    int budget = 80000;
                    while (st.Count > 0 && --budget > 0)
                    {
                        var e = st.Pop();
                        var fe = e as Noesis.FrameworkElement;
                        if (fe != null && fe.GetType().Name == "MapBase") _maps.Add(new WeakReference(fe));
                        int n = Noesis.VisualTreeHelper.GetChildrenCount(e);
                        for (int k = n - 1; k >= 0; k--) { var c = Noesis.VisualTreeHelper.GetChild(e, k); if (c != null) st.Push(c); }
                    }
                    Trace("arc colour: maps found " + _maps.Count + " (once; the maps are remembered)");
                }
                var red4 = Noesis.Color.FromArgb(0xE6, 235, 45, 45);
                foreach (var w in _maps)
                {
                    var map = w.Target as Noesis.DependencyObject; if (map == null) continue;
                    var pal = getPalette.Invoke(null, new object[] { map });
                    if (pal == null) continue;
                    bool known = false; foreach (var sv in _paletteSaves) if (ReferenceEquals(sv.Palette, pal)) known = true;
                    if (known) continue;
                    var pair = pal.GetType().GetMethod("Pair").Invoke(pal, new object[] { colour });   // (Color Day, Color Night)
                    var pt = pair.GetType();
                    var save = new PaletteSave { Palette = pal, Day = pt.GetField("Item1").GetValue(pair), Night = pt.GetField("Item2").GetValue(pair) };
                    pal.GetType().GetMethod("Override").Invoke(pal, new object[] { colour, red4, red4 });
                    _paletteSaves.Add(save);
                }
                Trace("arc colour: FiringArc overridden with red on " + _paletteSaves.Count + " palette(s)");
                PatchFill();
            }
            catch (Exception e) { Trace("arc colour: " + e.Message); }
        }

        // 3D wedges: after WeaponArc3DRenderer.Show the vertex colours of the meshes are set to the same weak red as the map fill (only while our zones are shown).
        static FieldInfo _fArcObjs;
        static void Arc3DShowPostfix()
        {
            if (_zoneInfo == null) return;
            try
            {
                var list = _fArcObjs.GetValue(null) as System.Collections.IEnumerable; if (list == null) return;
                var col = new UnityEngine.Color32(235, 45, 45, 0x30);
                foreach (var o in list)
                {
                    var go = o as UnityEngine.GameObject; if (go == null) continue;
                    var mf = go.GetComponent<UnityEngine.MeshFilter>(); var mesh = mf == null ? null : mf.sharedMesh; if (mesh == null) continue;
                    var cs = new UnityEngine.Color32[mesh.vertexCount]; for (int i = 0; i < cs.Length; i++) cs[i] = col;
                    mesh.colors32 = cs;
                }
            }
            catch (Exception e) { TraceLimited("a3d", 5f, "arc3d: " + e.Message); }
        }

        static void RestorePalettes()
        {
            if (_paletteSaves.Count == 0) return;
            try
            {
                var tColor = _tHoverMgr.Assembly.GetType("SeaPower.LiveMap.MapColor");
                var colour = Enum.Parse(tColor, "FiringArc");
                foreach (var sv in _paletteSaves) sv.Palette.GetType().GetMethod("Override").Invoke(sv.Palette, new object[] { colour, sv.Day, sv.Night });
                Trace("arc colour: restored on " + _paletteSaves.Count + " palette(s)");
            }
            catch (Exception e) { Trace("arc colour restore: " + e.Message); }
            _paletteSaves.Clear();
        }

        // ---- the weak red fill: a postfix of WeaponArcSegmentViewModel.Render adds a second Pie slot (same geometry) with a Fill, only while our zones are shown ----------
        static void PatchFill()
        {
            if (_fillPatched || _fillFailed || _zonesHarmony == null) return;
            try
            {
                var t = _tHoverMgr.Assembly.GetType("SeapowerUI.WeaponArcSegmentViewModel");
                var m = t == null ? null : t.GetMethod("Render", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (m == null) { _fillFailed = true; Trace("arc fill: WeaponArcSegmentViewModel.Render not found (an older game version): no fill"); return; }
                _fArcShown = t.GetField("_shown", BindingFlags.Instance | BindingFlags.NonPublic); _fArcGeom = t.GetField("_geometry", BindingFlags.Instance | BindingFlags.NonPublic);
                _fArcLayer = t.GetField("_gizmoLayer", BindingFlags.Instance | BindingFlags.NonPublic);
                if (_fArcShown == null || _fArcGeom == null || _fArcLayer == null) { _fillFailed = true; Trace("arc fill: fields of the wedge not found: no fill"); return; }
                _zonesHarmony.Patch(m, postfix: Hm("ArcFillPostfix"));
                _fillPatched = true; Trace("arc fill: patched WeaponArcSegmentViewModel.Render");
            }
            catch (Exception e) { _fillFailed = true; Trace("arc fill patch: " + e.Message); }
        }

        static FieldInfo _fArcShown, _fArcGeom, _fArcLayer; static MethodInfo _mPie, _mSlotLayer, _mSlotFill, _mSlotVisible, _mFillFrom; static MethodInfo[] _mSlotGeoms; static bool _fillInit;

        static void ArcFillPostfix(object __instance, object ctx)
        {
            if (_zoneInfo == null) return;   // only while the zones of our Fire are shown
            try
            {
                if (!(bool)_fArcShown.GetValue(__instance)) return;
                var geom = _fArcGeom.GetValue(__instance); if (geom == null) return;   // Nullable<PieGeometry>: boxed value or null
                if (!_fillInit)
                {
                    var asm = _tHoverMgr.Assembly;
                    var tCtx = ctx.GetType(); var tSlot = asm.GetType("MapControl.MapSlot"); var tFill = asm.GetType("MapControl.MapFill");
                    _mPie = tCtx.GetMethod("Pie", new[] { typeof(byte) });
                    _mSlotLayer = tSlot.GetMethod("GizmoLayer"); _mSlotFill = tSlot.GetMethod("Fill"); _mSlotVisible = tSlot.GetMethod("Visible");
                    var gl = new List<MethodInfo>(); foreach (var mi in tSlot.GetMethods()) if (mi.Name == "Geometry") gl.Add(mi); _mSlotGeoms = gl.ToArray();
                    foreach (var mi in tFill.GetMethods(BindingFlags.Static | BindingFlags.Public)) if (mi.Name == "From" && mi.GetParameters().Length == 2) _mFillFrom = mi;
                    _fillInit = true;
                }
                var weak = Noesis.Color.FromArgb(0x30, 235, 45, 45);
                var fill = _mFillFrom.Invoke(null, new object[] { weak, weak });
                object slot = _mPie.Invoke(ctx, new object[] { (byte)128 });
                slot = _mSlotLayer.Invoke(slot, new object[] { _fArcLayer.GetValue(__instance) });
                slot = _mSlotFill.Invoke(slot, new object[] { fill });
                MethodInfo geomM = null; foreach (var mi in _mSlotGeoms) if (mi.GetParameters()[0].ParameterType.GetElementType() == geom.GetType()) geomM = mi;
                if (geomM == null) { TraceLimited("fill0", 5f, "arc fill: no Geometry overload for " + geom.GetType().Name); return; }
                slot = geomM.Invoke(slot, new object[] { geom });
                _mSlotVisible.Invoke(slot, new object[] { true });
            }
            catch (Exception e) { TraceLimited("fill1", 5f, "arc fill: " + e.Message); }
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
                if (id != null && _debug) TraceLimited("zonesTry", 5f, "zones: Fire targeting " + id + " pin=" + (pin != null) + " enabled=" + Enabled + " cfg=" + (a != null && a._zoneFire != null && a._zoneFire.Value) + " hover=" + (_tHoverInfo != null && _pHovered != null));
                if (id == null)
                {
                    if (_zoneInfo != null && ReferenceEquals(unit, _zoneUnit)) { ArcColor(false); _zoneInfo = null; _zoneList = null; _zoneKeyId = null; _zoneKeyPin = null; _tHoverMgr.GetMethod("ClearCandidate").Invoke(null, new object[] { _zoneOwner }); }
                    return;
                }
                if (_zoneInfo != null && _zoneKeyId == id && ReferenceEquals(_zoneKeyPin, pin) && ReferenceEquals(_zoneUnit, unit)) return;
                var wsT = unit.GetType().Assembly.GetType("SeaPower.WeaponSystem");
                var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(wsT));
                if (pin != null) list.Add(pin); else foreach (var s in systemsOfAmmo) if (s != null && wsT.IsInstanceOfType(s)) list.Add(s);
                if (list.Count == 0) { Trace("zones: no launchers for " + id); return; }
                float range = 0f;
                var am = R.Call(unit, "getAmmunitionByName", id);
                if (am != null) { var lr = R.F(R.F(am, "_ap"), "_launchRangesInUnity"); if (lr is UnityEngine.Vector2) range = ((UnityEngine.Vector2)lr).y; }
                var info = Activator.CreateInstance(_tHoverInfo);
                _tHoverInfo.GetField("Unit").SetValue(info, unit);
                _tHoverInfo.GetField("Systems").SetValue(info, list);
                _tHoverInfo.GetField("MapRangeInUnity").SetValue(info, range);
                Trace("zones: start " + id + " launchers=" + list.Count + " range=" + range);
                ArcColor(true);
                _zoneInfo = info; _zoneKeyId = id; _zoneKeyPin = pin; _zoneUnit = unit; _zoneList = list; _zoneRange = range; _zoneNominal = range;
                _tHoverMgr.GetMethod("SetCandidate").Invoke(null, new object[] { _zoneOwner, unit, list, range });   // starts the manager's tick
            }
            catch (Exception e) { Trace("zones update: " + e.Message); _zoneInfo = null; }
        }
    }
}
