using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using BepInEx.Configuration;
using Noesis;
using Mathf = UnityEngine.Mathf;

namespace AlternativeWeaponsControl
{
    /// <summary>
    /// The "Weapons" tab of Unit Status (Noesis UI built in code): the unit's weapons status (Free / Tight / Hold) and "On Tight:
    /// engage hostile aircraft"; its launchers by kind (SAM, ANTI-SHIP, LAND ATTACK, ASW, GUNS, CIWS), launchers of the same
    /// system in one group row (×N) with summary state and stock and the automatic-use chips AIR / MSL / SUR / SUB / LND
    /// (a group's chip sets all its launchers); a row opens to its ammunition (type, characteristics, reference, the fire menu
    /// of the bottom panel) and, for a group, its launchers one per line with their own chips. All open when everything fits,
    /// else all closed; what is open is kept per unit for the session.
    /// </summary>
    public partial class AwcPlugin
    {

        static readonly ConditionalWeakTable<object, Dictionary<string, bool>> OpenGroups = new ConditionalWeakTable<object, Dictionary<string, bool>>();

        static class WC
        {
            public static readonly Color Ground = Color.FromRgb(30, 33, 38), Card = Color.FromRgb(38, 42, 48), Line = Color.FromRgb(53, 59, 68),
                                         Line2 = Color.FromRgb(47, 52, 60), Text = Color.FromRgb(230, 232, 235), Dim = Color.FromRgb(150, 156, 162),
                                         Dim2 = Color.FromRgb(127, 134, 142), Sub = Color.FromRgb(194, 199, 204), Ammo = Color.FromRgb(27, 30, 35),
                                         Badge = Color.FromRgb(58, 65, 75), Ok = Color.FromRgb(79, 191, 138), Amber = Color.FromRgb(217, 164, 65),
                                         Red = Color.FromRgb(224, 103, 90), Grey = Color.FromRgb(138, 144, 153), Ring = Color.FromRgb(107, 116, 128),
                                         Engage = Color.FromRgb(79, 179, 217), Sel = Color.FromRgb(58, 111, 134), FireBg = Color.FromRgb(51, 64, 42),
                                         FireLine = Color.FromRgb(91, 106, 74), FireText = Color.FromRgb(223, 233, 207), FireHover = Color.FromRgb(70, 88, 56),
                                         FireActive = Color.FromRgb(112, 40, 36), FireActiveLine = Color.FromRgb(224, 103, 90), BtnBg = Color.FromRgb(43, 48, 56),
                                         BtnLine = Color.FromRgb(70, 76, 86), None = Color.FromRgb(63, 69, 78),
                                         BtnHover = Color.FromRgb(58, 66, 77),
                                         CeaseBg = Color.FromRgb(70, 34, 32), CeaseHover = Color.FromRgb(112, 46, 42), CeaseLine = Color.FromRgb(140, 64, 58), CeaseInk = Color.FromRgb(240, 176, 166),
                                         ReloadBg = Color.FromRgb(30, 50, 68), ReloadHover = Color.FromRgb(42, 76, 104), ReloadLine = Color.FromRgb(62, 108, 140), ReloadInk = Color.FromRgb(168, 212, 240), BandBg = Color.FromArgb(22, 79, 179, 217), BandLine = Color.FromArgb(70, 79, 179, 217);
            static readonly List<KeyValuePair<Color, SolidColorBrush>> _brushes = new List<KeyValuePair<Color, SolidColorBrush>>();
            // one brush per colour: the tab is refreshed every 0.3 s and made a native Noesis brush for every call (nothing changes a brush after it is set)
            public static SolidColorBrush B(Color c)
            {
                for (int i = 0; i < _brushes.Count; i++) if (_brushes[i].Key == c) return _brushes[i].Value;
                var b = new SolidColorBrush(c); _brushes.Add(new KeyValuePair<Color, SolidColorBrush>(c, b)); return b;
            }
            static RotateTransform _rot90;
            public static RotateTransform Rot90 { get { return _rot90 ?? (_rot90 = new RotateTransform(90f)); } }
            public static readonly SolidColorBrush Clear = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        }

        const float ColFire = 128f, ColChip = 26f;

        sealed class ChipUI
        {
            public Grid G; public Ellipse Outer, Dot; public Rectangle Bar; public TextBlock None; public int Domain; public float Size; public bool Painted; public Chip Last;
        }

        sealed class MemberRow { public Launcher L; public TextBlock State, Fc; public string FcSig; public StockUI Stock; public Ellipse Dot; public ChipUI[] Chips; public Grid G; public List<string> Ids; }
        sealed class AmmoCardUI { public string Id; public Grid G; public Border B; public Ellipse Dot; }

        /// <summary>The switches of one ammunition card: copies of the group's - they switch the whole group.</summary>
        sealed class AmmoChipsUI { public string Id; public int Dom; public ChipUI[] Chips; }

        /// <summary>The common controls in a section's title row: Fire / Stop / Reload and the target switches for the ammunition that ALL the section's launchers have.</summary>
        sealed class SecCtl { public List<Launcher> Ls; public List<string> Common; public List<GroupRow> Groups; public FireUI Fire; public List<int> LDom = new List<int>(); public int Dom; public ChipUI[] Chips = new ChipUI[5]; public GroupRow Tmp = new GroupRow(); }

        sealed class StockUI { public StackPanel P; public TextBlock Cur, Max, RailLab, RailCur, RailMax; public StackPanel Rail; }

        sealed class FireUI { public List<string> Ids; public Launcher Pin; public Border Box, Main, Arrow; public bool OverMain, OverArrow, Active; }

        sealed class IconBtn { public Border B; public bool Enabled = true, Over, Cease; public string Tip; }

        /// <summary>SAM readiness button of one launcher: a missile icon, tinted by the state, with the time left under it.</summary>
        sealed class WarmUI { public object Ws; public Border B; public StackPanel Icons; public TextBlock Timer; public bool Over; public int St = -1; public string TimerText = "", Tip, Sig; public List<bool> Rails; }

        /// <summary>The control slots of a row: fire (split), cease fire, reload - for a set of launchers.</summary>
        sealed class CtrlUI { public List<Launcher> Scope; public List<string> Ammo; public IconBtn Cease, Reload; }

        sealed class GroupRow
        {
            public string Key; public List<Launcher> Ls = new List<Launcher>(); public bool Open; public string StateTip; public string Suffix = "";
            public TextBlock Fc; public string FcSig;
            public bool StatesBuilt; public Ellipse[] SDot; public TextBlock[] SText; public StackPanel[] SPair; public string[] SLabel;
            public FrameworkElement SOne; public Ellipse SOneDot; public TextBlock SOneText; public FrameworkElement SMulti;
            public Path Chevron; public Border ChevronHit; public StockUI Stock; public Grid States; public ChipUI[] Chips; public StackPanel Detail;
            public List<MemberRow> Members = new List<MemberRow>(); public bool DetailBuilt, Expandable; public List<string> Ammo = new List<string>();
            public List<KeyValuePair<string, StockUI>> AmmoStocks = new List<KeyValuePair<string, StockUI>>();
            public List<AmmoChipsUI> AmmoChips = new List<AmmoChipsUI>();
            public List<string> FireIds; public string ActiveAmmo; public List<AmmoCardUI> Cards = new List<AmmoCardUI>();   // several ammunition types and several launchers: the chosen (active) ammunition, the launchers are listed under it
            public TextBlock Chan; public Border ChanBox; public string ChanTip; public List<object> Dirs = new List<object>(); public bool GuidedAmmo, Wires;
        }

        sealed class WeaponsView
        {
            public AwcPlugin P;
            public object Unit;
            public readonly List<TitleUI> Titles = new List<TitleUI>();
            public int DomMask;
            public float StockW = 80f;   // the AMMO column: fits the widest "max/max" of the tab (StockWidth)
            public readonly List<ColumnDefinition> StockCols = new List<ColumnDefinition>();   // the target domains the unit's launchers can engage: the AIR..LND columns shown
            public Grid Root;
            public ScrollViewer Scroll;
            public StackPanel Body;
            public readonly List<GroupRow> Groups = new List<GroupRow>();
            public Border[] Status = new Border[3];
            public TextBlock[] StatusText = new TextBlock[3];
            public Border TightBox; public Rectangle TightMark; public StackPanel TightRow;
            public Border FreeBox; public Rectangle FreeMark; public StackPanel FreeRow;
            public TextBlock ExpandText;
            public float NextRefresh;
            public bool Fitting;   // the first opening for the unit: groups are closed one by one until the tab fits
            public readonly Dictionary<string, object> Entries = new Dictionary<string, object>();
            public readonly List<FireUI> Fires = new List<FireUI>();
            public readonly List<CtrlUI> Ctrls = new List<CtrlUI>();
            public readonly List<SecCtl> SecCtls = new List<SecCtl>();
            public readonly List<WarmUI> Warms = new List<WarmUI>();
            // the fire-control directors window: opens while the mouse is over a group's T/W badge, stays while it is over it or the window
               // frames left to re-measure the scroll area after it is shown (its extent could stay stale)
            public Canvas Overlay; public Border DirCard; public GroupRow DirFor, DirHover; public float DirOpenAt = -1f, DirCloseAt = -1f;
            public readonly List<KeyValuePair<object, TextBlock[]>> DirLines = new List<KeyValuePair<object, TextBlock[]>>();
        }

        // ------------------------------------------------------------------ building

        static TextBlock T(string s, Color c, float size = 13f, FontWeight? w = null)
        {
            var t = new TextBlock { Text = s ?? "", Foreground = WC.B(c), FontSize = size, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap, Background = WC.Clear };
            if (w.HasValue) t.FontWeight = w.Value;
            t.MouseEnter += (o, e) => { try { AutoTip(t); } catch (Exception ex) { Trace("auto tooltip: " + ex.Message); } };
            return t;
        }

        sealed class AutoBox { public string S; public object O; }
        static readonly ConditionalWeakTable<TextBlock, AutoBox> AutoTips = new ConditionalWeakTable<TextBlock, AutoBox>();

        /// <summary>Tooltip content for a text: lines only where the text has them. The game's tooltip style wraps text at 300 px
        /// (an implicit TextBlock style: Wrap, MaxWidth 300) - local values on our own TextBlock override it.</summary>
        static object TipOf(string s)
        {
            return s == null ? null : new TextBlock { Text = s, TextWrapping = TextWrapping.NoWrap, MaxWidth = float.PositiveInfinity };
        }

        static void SetTip(DependencyObject e, string s) { ToolTipService.SetToolTip(e, TipOf(s)); }

        /// <summary>A text that does not fit its place (trimmed, cut by its column or a clipping parent, more lines than shown) gets its
        /// whole text as the tooltip when the mouse comes over it - unless it or an element around it has a tooltip of its own.</summary>
        static void AutoTip(TextBlock t)
        {
            var cur = t.ToolTip;
            AutoBox box;
            bool had = AutoTips.TryGetValue(t, out box);
            if (cur != null && !(had && ReferenceEquals(cur, box.O))) return;   // a tooltip of its own
            string want = !string.IsNullOrEmpty(t.Text) && !OuterTip(t) && Cut(t) ? t.Text : null;
            if (cur != null ? want == box.S : want == null) return;
            if (had) AutoTips.Remove(t);
            var o = TipOf(want);
            if (want != null) AutoTips.Add(t, new AutoBox { S = want, O = o });
            t.ToolTip = o;
        }

        static bool OuterTip(TextBlock t)
        {
            DependencyObject p = VisualTreeHelper.GetParent(t);
            for (int i = 0; i < 8 && p != null; i++, p = VisualTreeHelper.GetParent(p))
                if (p is FrameworkElement && ((FrameworkElement)p).ToolTip != null) return true;
            return false;
        }

        /// <summary>Is the text not shown whole?</summary>
        static bool Cut(TextBlock t)
        {
            float w = t.ActualWidth, h = t.ActualHeight;
            if (w <= 0f || h <= 0f) return false;
            bool wrap = t.TextWrapping != TextWrapping.NoWrap;
            // its natural size: a copy with the same text and font, measured unconstrained (width kept when it wraps)
            var m = new TextBlock { Text = t.Text, FontSize = t.FontSize, FontWeight = t.FontWeight, FontStyle = t.FontStyle, FontFamily = t.FontFamily, TextWrapping = t.TextWrapping };
            m.Measure(new Size(wrap ? w : float.PositiveInfinity, float.PositiveInfinity));
            float nw = m.DesiredSize.Width, nh = m.DesiredSize.Height;
            if (nw <= 0f) return false;
            if (!wrap && nw > w + 0.5f) return true;
            if (nh > h + 0.5f) return true;
            // a clipping parent cutting it
            DependencyObject p = VisualTreeHelper.GetParent(t);
            for (int i = 0; i < 10 && p != null; i++, p = VisualTreeHelper.GetParent(p))
            {
                var fe = p as FrameworkElement;
                if (fe == null || !fe.ClipToBounds) continue;
                var at = t.TranslatePoint(new Point(0f, 0f), fe);
                if (at.X + Math.Min(nw, w) > fe.ActualWidth + 0.5f || at.Y + h > fe.ActualHeight + 0.5f) return true;
            }
            return false;
        }

        static void Click(UIElement e, Action a, bool sound = true)
        {
            e.MouseLeftButtonUp += (s, ev) =>
            {
                try { if (sound && (object)Active != null) Active.ClickSound(); a(); } catch (Exception ex) { Trace("weapons tab click: " + ex); }
                ev.Handled = true;
            };
            var fe = e as FrameworkElement;
            if (fe != null) fe.Cursor = Cursors.Hand;
        }

        /// <summary>A row's columns: [chevron] name | state | stock | FIRE | 5 chips; the last six are the control block (a band behind).</summary>
        static Grid Columns(bool lead, WeaponsView v)
        {
            int mask = v.DomMask;
            var g = new Grid();
            if (lead) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22f) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3f, GridUnitType.Star) });   // launcher 60%
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2f, GridUnitType.Star) });   // state 40%
            var sc = new ColumnDefinition { Width = new GridLength(v.StockW) };
            v.StockCols.Add(sc);
            g.ColumnDefinitions.Add(sc);
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ColFire) });
            ChipColumns(g, mask);
            Band(g, lead ? 4 : 3);
            Sep(g, lead ? 2 : 1); Sep(g, lead ? 3 : 2);
            return g;
        }

        /// <summary>Member / ammunition rows (indented by the chevron's column): label | state, 60/40 as the group's | stock | FIRE | 5 chips.</summary>
        static Grid MemberColumns(WeaponsView v)
        {
            int mask = v.DomMask;
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3f, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2f, GridUnitType.Star) });
            var sc = new ColumnDefinition { Width = new GridLength(v.StockW) };
            v.StockCols.Add(sc);
            g.ColumnDefinitions.Add(sc);
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ColFire) });
            ChipColumns(g, mask);
            Band(g, 3);
            Sep(g, 1); Sep(g, 2);
            return g;
        }

        /// <summary>The AIR..LND columns: a domain none of the unit's launchers can engage gets no column (width 0, its chips collapsed).</summary>
        static void ChipColumns(Grid g, int mask)
        {
            for (int i = 0; i < 5; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength((mask & Domains[i]) != 0 ? ColChip : 0f) });
        }

        /// <summary>A faint line on the left of a column: WEAPONS | STATE | AMMO.</summary>
        static void Sep(Grid g, int col)
        {
            var b = new Border { BorderBrush = WC.B(Color.FromArgb(70, 140, 150, 165)), BorderThickness = new Thickness(1f, 0f, 0f, 0f), IsHitTestVisible = false };
            Grid.SetColumn(b, col);
            g.Children.Add(b);
        }

        static void ChipVisible(WeaponsView v, UIElement e, int i) { if ((v.DomMask & Domains[i]) == 0) e.Visibility = Visibility.Collapsed; }

        /// <summary>The control block (fire + automatic-use switches): a faint band behind those columns, with a line on its left.</summary>
        static void Band(Grid g, int firstCol)
        {
            var b = new Border { Background = WC.B(WC.BandBg), BorderBrush = WC.B(WC.BandLine), BorderThickness = new Thickness(1f, 0f, 0f, 0f), IsHitTestVisible = false };
            Grid.SetColumn(b, firstCol); Grid.SetColumnSpan(b, 6);
            g.Children.Add(b);
        }

        static void At(Grid g, UIElement e, int col) { Grid.SetColumn(e, col); g.Children.Add(e); }

        static ChipUI MakeChip(float size, int domain)
        {
            var c = new ChipUI { Domain = domain, Size = size, G = new Grid { Width = ColChip, Height = size > 12f ? 28f : 22f, Background = WC.Clear } };
            c.Outer = new Ellipse { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            c.Dot = new Ellipse { Width = 4f, Height = 4f, Fill = WC.B(WC.Amber), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            // mixed: the left half filled, the ring around
            c.Bar = new Rectangle { Width = size, Height = size, Fill = WC.B(WC.Ok), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                                    RadiusX = size / 2f, RadiusY = size / 2f, Clip = new RectangleGeometry(new Rect(0f, 0f, size / 2f, size)) };
            c.None = T("·", WC.None, 13f);
            c.None.HorizontalAlignment = HorizontalAlignment.Center;
            c.G.Children.Add(c.Outer); c.G.Children.Add(c.Dot); c.G.Children.Add(c.Bar); c.G.Children.Add(c.None);
            return c;
        }

        static void Paint(ChipUI c, Chip st)
        {
            if (c.Painted && c.Last == st) return;
            c.Painted = true; c.Last = st;
            bool big = c.Size > 12f;
            c.Outer.Visibility = st == AwcPlugin.Chip.None ? Visibility.Collapsed : Visibility.Visible;
            c.None.Visibility = st == AwcPlugin.Chip.None ? Visibility.Visible : Visibility.Collapsed;
            c.Dot.Visibility = st == AwcPlugin.Chip.Order && big ? Visibility.Visible : Visibility.Collapsed;
            c.Bar.Visibility = st == AwcPlugin.Chip.Mixed ? Visibility.Visible : Visibility.Collapsed;
            c.Outer.StrokeThickness = 2f;
            switch (st)
            {
                case AwcPlugin.Chip.On: c.Outer.Fill = WC.B(WC.Ok); c.Outer.Stroke = null; break;
                case AwcPlugin.Chip.Off: c.Outer.Fill = WC.Clear; c.Outer.Stroke = WC.B(WC.Ring); break;
                case AwcPlugin.Chip.Order: c.Outer.Fill = WC.Clear; c.Outer.Stroke = WC.B(WC.Amber); break;
                case AwcPlugin.Chip.Mixed: c.Outer.Fill = WC.Clear; c.Outer.Stroke = WC.B(WC.Ok); break;
            }
            c.G.Cursor = st == AwcPlugin.Chip.None ? null : Cursors.Hand;
        }

        WeaponsView BuildWeaponsView(object unit)
        {
            var v = new WeaponsView { P = this, Unit = unit };
            var w = W(unit);
            w.Cache = null;   // fresh: ammunition may have changed
            var ls = CachedLaunchers(unit, w);
            foreach (var l in ls) v.DomMask |= l.Domains;

            v.Root = new Grid { Background = WC.B(WC.Ground) };
            v.Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            v.Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            v.Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1f, GridUnitType.Star) });

            // header: weapons status, On Tight, expand all
            var head = new Grid { Background = WC.B(Color.FromRgb(38, 42, 48)) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1f, GridUnitType.Star) });   // the switches: in a row, wrapped only when they do not fit
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var hb = new Border { Child = head, BorderBrush = WC.B(WC.Line), BorderThickness = new Thickness(0f, 0f, 0f, 1f), Padding = new Thickness(10f, 6f, 10f, 6f) };
            var seg = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var lbl = T(Loc.T("tab.weapons"), WC.Dim, 11f); lbl.Margin = new Thickness(0f, 0f, 12f, 0f);
            seg.Children.Add(lbl);
            var segBox = new Border { BorderBrush = WC.B(WC.BtnLine), BorderThickness = new Thickness(1f), CornerRadius = new CornerRadius(3f) };
            var segRow = new StackPanel { Orientation = Orientation.Horizontal };
            segBox.Child = segRow;
            string[] sn = { Loc.T("status.free"), Loc.T("status.tight"), Loc.T("status.hold") };
            int[] sv = { 1, 0, 2 };   // ObjectBase.WeaponStatus: Tight, Free, Hold
            for (int i = 0; i < 3; i++)
            {
                int status = sv[i];
                var t = T(sn[i], WC.Dim, 12f);
                var b = new Border { Child = t, Padding = new Thickness(10f, 2f, 10f, 2f), Background = WC.Clear,
                                     BorderBrush = WC.B(WC.BtnLine), BorderThickness = new Thickness(i == 0 ? 0f : 1f, 0f, 0f, 0f) };
                Click(b, () => SetWeaponStatus(v, status));
                v.Status[i] = b; v.StatusText[i] = t;
                segRow.Children.Add(b);
            }
            seg.Children.Add(segBox);
            Grid.SetColumn(seg, 0); head.Children.Add(seg);
            v.TightRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16f, 0f, 0f, 0f), Background = WC.Clear, VerticalAlignment = VerticalAlignment.Center };
            v.TightBox = new Border { Width = 14f, Height = 14f, BorderBrush = WC.B(WC.Ring), BorderThickness = new Thickness(1f), CornerRadius = new CornerRadius(2f), Margin = new Thickness(0f, 0f, 6f, 0f) };
            v.TightMark = new Rectangle { Width = 6f, Height = 6f, Fill = WC.B(WC.Ok), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            v.TightBox.Child = v.TightMark;
            v.TightRow.Children.Add(v.TightBox);
            v.TightRow.Children.Add(T(Loc.T("tab.onTight"), WC.Sub, 12f));
            Click(v.TightRow, () => ToggleTightAir(v));
            // the second switch under it: On Free: engage approaching fast unidentified aircraft (the mod's own, saved with the unit); the tick is orange-red
            var orange = Color.FromRgb(240, 96, 40);
            v.FreeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16f, 0f, 0f, 0f), Background = WC.Clear, VerticalAlignment = VerticalAlignment.Center };
            v.FreeBox = new Border { Width = 14f, Height = 14f, BorderBrush = WC.B(WC.Ring), BorderThickness = new Thickness(1f), CornerRadius = new CornerRadius(2f), Margin = new Thickness(0f, 0f, 6f, 0f) };
            v.FreeMark = new Rectangle { Width = 6f, Height = 6f, Fill = WC.B(orange), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            v.FreeBox.Child = v.FreeMark;
            v.FreeRow.Children.Add(v.FreeBox);
            v.FreeRow.Children.Add(T(Loc.T("tab.onFree"), WC.Sub, 12f));
            Click(v.FreeRow, () => { W(v.Unit).FreeAirSet = FreeAirOn(v.Unit) ? 0 : 1; Refresh(v, false); });
            { var ai = AwcPlugin.Instance; SetTip(v.FreeRow, Loc.F("tab.onFreeTip", Num(ai != null && ai._freeAirKnots != null ? ai._freeAirKnots.Value : 500f), Num(ai != null && ai._freeAirNm != null ? ai._freeAirNm.Value : 50f))); }
            var sw = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0f), VerticalAlignment = VerticalAlignment.Center };
            sw.Children.Add(v.TightRow); sw.Children.Add(v.FreeRow);
            Grid.SetColumn(sw, 1); head.Children.Add(sw);
            // Expand / Collapse all: at the left of the WEAPONS column title, centred in its height
            v.ExpandText = T(Loc.T("tab.expandAll"), WC.Sub, 11f);
            var exp = new Border { Child = v.ExpandText, Padding = new Thickness(8f, 2f, 8f, 3f), Margin = new Thickness(2f, 0f, 0f, 0f), Background = WC.B(WC.BtnBg), BorderBrush = WC.B(WC.BtnLine),
                                   BorderThickness = new Thickness(1f), CornerRadius = new CornerRadius(3f), VerticalAlignment = VerticalAlignment.Center,
                                   HorizontalAlignment = HorizontalAlignment.Left };
            Click(exp, () => { bool any = v.Groups.Exists(gr => gr.Expandable && !gr.Open); foreach (var gr in v.Groups) SetOpen(v, gr, any); Refresh(v, true); });
            Grid.SetRow(hb, 0); v.Root.Children.Add(hb);

            // column titles
            var ch = Columns(true, v);
            ch.Margin = new Thickness(10f, 6f, 10f, 6f);
            var lh = T(Loc.T("tab.weapons"), WC.Dim, 10.5f); lh.HorizontalAlignment = HorizontalAlignment.Center; At(ch, lh, 1);
            Grid.SetColumnSpan(exp, 2); At(ch, exp, 0);
            var sth = T(Loc.T("tab.state"), WC.Dim, 10.5f); sth.HorizontalAlignment = HorizontalAlignment.Center; At(ch, sth, 2);
            var stk = T(Loc.T("tab.ammo"), WC.Dim, 10.5f); stk.HorizontalAlignment = HorizontalAlignment.Center;
            At(ch, stk, 3);
            var fh = T(Loc.T("tab.fire"), WC.Dim, 10.5f); fh.HorizontalAlignment = HorizontalAlignment.Center;
            SetTip(fh, Loc.T("tab.fireTip"));
            At(ch, fh, 4);
            for (int i = 0; i < 5; i++)
            {
                var t = T(Loc.T("dom." + i), WC.Dim, 10.5f); t.HorizontalAlignment = HorizontalAlignment.Center;
                SetTip(t, Loc.F("tab.autoAgainst", Loc.T("domLong." + i)));
                ChipVisible(v, t, i);
                At(ch, t, 5 + i);
            }
            Grid.SetRow(ch, 1); v.Root.Children.Add(ch);

            // sections
            v.Body = new StackPanel { Margin = new Thickness(10f, 0f, 10f, 0f) };   // no empty room at the end: the scroll bar shows only when something is hidden
            v.Scroll = new ScrollViewer { Content = v.Body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            Grid.SetRow(v.Scroll, 2); v.Root.Children.Add(v.Scroll);
            // a layer over the tab for the directors window (in the tab's own tree: same scale, clickable)
            v.Overlay = new Canvas();
            Grid.SetRowSpan(v.Overlay, 3);
            Panel.SetZIndex(v.Overlay, 100);
            v.Root.Children.Add(v.Overlay);

            Dictionary<string, bool> mem;
            v.Fitting = !OpenGroups.TryGetValue(unit, out mem);
            if (v.Fitting) mem = OpenGroups.GetOrCreateValue(unit);
            var bySection = new SortedDictionary<int, List<GroupRow>>();
            foreach (var l in ls)
            {
                List<GroupRow> list;
                if (!bySection.TryGetValue(l.Section, out list)) bySection[l.Section] = list = new List<GroupRow>();
                var key = l.Section + "|" + l.Name + "|" + string.Join(",", l.Ammo.ToArray());
                var gr = list.Find(x => x.Key == key);
                if (gr == null) { gr = new GroupRow { Key = key }; list.Add(gr); v.Groups.Add(gr); }
                gr.Ls.Add(l);
            }
            // groups of one launcher that share a name with another group of the section (the same mount with another ammunition load): the position tells them apart
            foreach (var kv0 in bySection)
                foreach (var gr in kv0.Value)
                {
                    if (gr.Ls.Count != 1) continue;
                    var same = kv0.Value.FindAll(x => x.Ls[0].Name == gr.Ls[0].Name);
                    if (same.Count < 2) continue;
                    var loc = gr.Ls[0].Loc;
                    gr.Suffix = !string.IsNullOrEmpty(loc) ? Cap(loc) : "#" + (same.IndexOf(gr) + 1);
                }
            var secOrder = new List<KeyValuePair<int, List<GroupRow>>>(bySection);
            { var so = SectionOrder(); secOrder.Sort((x, y) => { int a = so.IndexOf(x.Key), b = so.IndexOf(y.Key); return (a < 0 ? 1000 + x.Key : a).CompareTo(b < 0 ? 1000 + y.Key : b); }); }
            foreach (var kv in secOrder)
            {
                var card = new StackPanel();
                var cb = new Border { Child = card, Background = WC.B(WC.Card), BorderBrush = WC.B(WC.Line), BorderThickness = new Thickness(1f),
                                      CornerRadius = new CornerRadius(3f), Margin = new Thickness(0f, 0f, 0f, 6f) };
                var sh = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0f), Background = WC.Clear };
                sh.Margin = new Thickness(10f, 4f, 10f, 3f);
                sh.Children.Add(MakeTitle(v, kv.Key, kv.Value).Tb);
                // "2 turrets", "2 launchers, 2 tubes": counted by what they are
                var counts = new List<KeyValuePair<string, int>>();
                int count = 0;
                foreach (var gr in kv.Value)
                    foreach (var l in gr.Ls)
                    {
                        count++;
                        var nn = Noun(l);
                        int at = counts.FindIndex(x => x.Key == nn);
                        if (at < 0) counts.Add(new KeyValuePair<string, int>(nn, 1)); else counts[at] = new KeyValuePair<string, int>(nn, counts[at].Value + 1);
                    }
                var subText = new StringBuilder();
                if (count > 1) foreach (var c in counts) subText.Append(subText.Length > 0 ? ", " : "").Append(Loc.F("count.format", c.Value, Loc.T("noun." + c.Key + (c.Value == 1 ? "" : "s"))));
                var sub = T(subText.ToString(), WC.Dim2, 11f); sub.Margin = new Thickness(8f, 0f, 0f, 0f);
                sh.Children.Add(sub);
                var secLs = new List<Launcher>();
                foreach (var gr in kv.Value) secLs.AddRange(gr.Ls);
                // several groups (launchers that differ): the ammunition they all have gets controls in the title row
                List<string> common = null;
                if (secLs.Count > 1 && kv.Value.Count > 1)
                {
                    common = new List<string>(secLs[0].Ammo);
                    common.RemoveAll(id => !secLs.TrueForAll(l => l.Ammo.Contains(id)));
                    if (common.Count == 0) common = null;
                }
                card.Children.Add(common == null ? (UIElement)sh : SectionControls(v, sh, secLs, common, kv.Value));
                MakeDraggable(v, cb, sh, kv.Key);
                foreach (var gr in kv.Value)
                {
                    bool o;
                    gr.Open = mem.TryGetValue(gr.Key, out o) ? o : true;
                    card.Children.Add(BuildGroup(v, gr));
                }
                v.Body.Children.Add(cb);
            }
            if (v.Groups.Count == 0) v.Body.Children.Add(T(Loc.T("tab.noWeapons"), WC.Dim, 12f));
            else
            {
                // the last card: no gap below it (6 px between cards), nor below its last group's open rows
                var last = v.Body.Children[v.Body.Children.Count - 1] as Border;
                if (last != null) last.Margin = new Thickness(0f, 0f, 0f, 1f);
            }


            Refresh(v, true);
            StockWidth(v);
            return v;
        }

        /// <summary>The section's title row as a table row: the title in the name columns, Fire / Stop / Reload and the switches (only the target
        /// types of the common ammunition) in the control columns.</summary>
        Grid SectionControls(WeaponsView v, FrameworkElement sh, List<Launcher> ls, List<string> common, List<GroupRow> groups)
        {
            var row = Columns(true, v);
            Grid.SetColumnSpan(sh, 4);
            At(row, sh, 0);
            At(row, Controls(v, ls, common, true, true, null), 4);
            var sc = new SecCtl { Ls = ls, Common = common, Groups = groups, Fire = v.Fires[v.Fires.Count - 1] };
            var cu = v.Ctrls[v.Ctrls.Count - 1];
            if (cu.Reload != null) { cu.Reload.Tip = Loc.F("tip.reloadCurrent", cu.Reload.Tip); SetTip(cu.Reload.B, cu.Reload.Tip); }
            sc.Tmp.Ls = ls;
            foreach (var l in ls)
            {
                int ld = 0;
                foreach (var id in common) { int ix = l.Ammo.IndexOf(id); if (ix >= 0 && ix < l.AmmoDom.Length) ld |= l.AmmoDom[ix]; }
                sc.LDom.Add(ld); sc.Dom |= ld;
            }
            for (int i = 0; i < 5; i++)
            {
                var c = MakeChip(10f, Domains[i]);
                int d = Domains[i];
                Click(c.G, () => { if ((sc.Dom & d) != 0) ClickSection(v, sc, d); });
                sc.Chips[i] = c;
                ChipVisible(v, c.G, i);
                At(row, c.G, 5 + i);
            }
            v.SecCtls.Add(sc);
            return row;
        }

        static void SecPairs(object unit, SecCtl sc, int d, List<KeyValuePair<Launcher, int>> pairs, out Chip st)
        {
            st = AwcPlugin.Chip.None;
            for (int k = 0; k < sc.Ls.Count; k++)
            {
                if ((sc.LDom[k] & d) == 0) continue;
                var s = State(unit, sc.Ls[k], d);
                if (s == AwcPlugin.Chip.None) continue;
                if (pairs != null) pairs.Add(new KeyValuePair<Launcher, int>(sc.Ls[k], d));
                st = st == AwcPlugin.Chip.None || st == s ? s : AwcPlugin.Chip.Mixed;
            }
        }

        void ClickSection(WeaponsView v, SecCtl sc, int d)
        {
            var pairs = new List<KeyValuePair<Launcher, int>>(); Chip st;
            SecPairs(v.Unit, sc, d, pairs, out st);
            if (pairs.Count == 0) return;
            bool anyOn = false;
            foreach (var p in pairs) if (State(v.Unit, p.Key, d) == AwcPlugin.Chip.On) anyOn = true;
            Switch(v.Unit, pairs, !anyOn);
            Refresh(v, false);
        }

        // ------------------------------------------------------------------ the order of the sections (drag a section's title)
        static List<int> SectionOrder()
        {
            var res = new List<int>();
            var a = Active; if (a == null || a._secOrder == null) return res;
            foreach (var n in a._secOrder.Value.Split(','))
            {
                int i = Array.IndexOf(SectionNames, n.Trim());
                if (i >= 0 && !res.Contains(i)) res.Add(i);
            }
            return res;
        }

        static readonly ConditionalWeakTable<Border, object> CardSec = new ConditionalWeakTable<Border, object>();

        /// <summary>The section card follows the mouse by its title; on release it goes to the place it was dragged to; the order is kept for all units.</summary>
        /// <summary>Where the dragged card goes: the place whose top edge is the nearest to the card's top edge (not the centres: a tall card
        /// would have to be dragged far). The places' tops are those of the list without the card, the card put in at each index.</summary>
        static int DropIndex(float[] tops, float[] hs, int from, float dragTop)
        {
            int best = from; float bd = float.MaxValue, t = 0f; int j = 0;
            for (int i = 0; i <= tops.Length; i++)
            {
                // slot j: the card goes in front of the j-th of the other cards (t = their heights above)
                if (i == from) continue;
                float dist = Math.Abs(dragTop - t);
                if (dist < bd) { bd = dist; best = j; }
                if (i < tops.Length) t += hs[i];
                j++;
            }
            return best;
        }

        static void MakeDraggable(WeaponsView v, Border card, FrameworkElement handle, int sec)
        {
            CardSec.Add(card, sec);
            handle.Cursor = Cursors.Hand;
            SetTip(handle, Loc.T("tab.dragOrder"));
            bool drag = false; float startY = 0f, dy = 0f;
            // the layout at the start of the drag (tops / heights of the cards): the other cards slide aside while the card is dragged
            float[] tops = null, hs = null; int from = -1;
            Action<float> slide = d =>
            {
                var kids = v.Body.Children;
                if (tops == null || from < 0 || kids.Count != tops.Length) return;
                int to = DropIndex(tops, hs, from, tops[from] + d);
                for (int i = 0; i < kids.Count; i++)
                {
                    if (i == from) continue;
                    float shift = 0f;
                    if (from < to && i > from && i <= to) shift = -hs[from];
                    else if (from > to && i >= to && i < from) shift = hs[from];
                    var el = kids[i] as UIElement;
                    if (el != null) el.RenderTransform = Math.Abs(shift) < 0.01f ? null : new TranslateTransform(0f, shift);
                }
            };
            Action clearAll = () =>
            {
                var kids = v.Body.Children;
                for (int i = 0; i < kids.Count; i++) { var el = kids[i] as UIElement; if (el != null && !ReferenceEquals(el, card)) el.RenderTransform = null; }
                tops = null; hs = null; from = -1;
            };
            handle.MouseLeftButtonDown += (s, e) =>
            {
                try
                {
                    if (v.Body.Children.Count < 2) return;
                    drag = handle.CaptureMouse();
                    startY = e.GetPosition(v.Body).Y; dy = 0f;
                    var kids0 = v.Body.Children;
                    tops = new float[kids0.Count]; hs = new float[kids0.Count]; from = -1;
                    float top0 = 0f;
                    for (int i = 0; i < kids0.Count; i++)
                    {
                        if (ReferenceEquals(kids0[i], card)) from = i;
                        var fe = kids0[i] as FrameworkElement;
                        tops[i] = top0; hs[i] = fe == null ? 0f : fe.ActualHeight + fe.Margin.Top + fe.Margin.Bottom;
                        top0 += hs[i];
                    }
                    if (drag) { Panel.SetZIndex(card, 50); e.Handled = true; }
                }
                catch (Exception ex) { drag = false; Trace("section drag: " + ex.Message); }
            };
            handle.MouseMove += (s, e) =>
            {
                if (!drag) return;
                try { dy = e.GetPosition(v.Body).Y - startY; card.RenderTransform = new TranslateTransform(0f, dy); slide(dy); }
                catch (Exception ex) { Trace("section drag move: " + ex.Message); }
            };
            handle.MouseLeftButtonUp += (s, e) =>
            {
                if (!drag) return;
                drag = false;
                try
                {
                    handle.ReleaseMouseCapture();
                    card.RenderTransform = null;
                    clearAll();
                    Panel.SetZIndex(card, 0);
                    e.Handled = true;
                    if (Math.Abs(dy) < 6f) return;   // a click, not a drag
                    // the new place: the one whose top is the nearest to the dragged card's top
                    var kids = v.Body.Children;
                    int from = -1;
                    for (int i = 0; i < kids.Count; i++) if (ReferenceEquals(kids[i], card)) from = i;
                    if (from < 0) return;
                    float top = 0f;
                    var tops = new float[kids.Count]; var hs = new float[kids.Count];
                    for (int i = 0; i < kids.Count; i++)
                    {
                        var fe = kids[i] as FrameworkElement;
                        tops[i] = top; hs[i] = fe == null ? 0f : fe.ActualHeight + fe.Margin.Top + fe.Margin.Bottom;
                        top += hs[i];
                    }
                    int to = DropIndex(tops, hs, from, tops[from] + dy);
                    if (to == from) return;
                    kids.RemoveAt(from);
                    kids.Insert(to, card);
                    // margins: 6 px between cards, none below the last
                    for (int i = 0; i < kids.Count; i++) { var b = kids[i] as Border; if (b != null) b.Margin = new Thickness(0f, 0f, 0f, i == kids.Count - 1 ? 1f : 6f); }
                    var names = new List<string>();
                    foreach (var k in kids) { var b = k as Border; object o; if (b != null && CardSec.TryGetValue(b, out o)) names.Add(SectionNames[(int)o]); }
                    var a = Active; if (a != null && a._secOrder != null) a._secOrder.Value = string.Join(",", names.ToArray());
                }
                catch (Exception ex) { Trace("section drag end: " + ex.Message); }
            };
            handle.LostMouseCapture += (s, e) => { if (drag) { drag = false; card.RenderTransform = null; Panel.SetZIndex(card, 0); clearAll(); } };
        }

        /// <summary>The AMMO column's width: the widest "max/max" of the tab's groups, ammunition and launchers (and the title), measured
        /// in the stock's font; set on every row's column (rows built later take it from v.StockW).</summary>
        void StockWidth(WeaponsView v)
        {
            float w = Measure(Loc.T("tab.ammo"), 10.5f);
            Action<int> fit = max => { if (max > 0) { var s = Fmt(max); w = Math.Max(w, Measure(s + "/" + s, 12f)); } };
            foreach (var gr in v.Groups)
            {
                fit(StockMax(gr.Ls));
                foreach (var a in gr.Ammo) fit(StockMax(gr.Ls, a));
                if (gr.Ls.Count > 1) foreach (var l in gr.Ls) fit(Cap(l.Ws));
                // the "On launcher 2/2" line under the stock of a single launcher (font 10)
                if (gr.Ls.Count == 1 && !gr.Ls.TrueForAll(IsGun))
                {
                    try
                    {
                        var rails = Rails(gr.Ls[0].Ws);
                        if (rails != null && rails.Count > 0 && rails.Count < 10000)
                        {
                            var n = Fmt(rails.Count);
                            w = Math.Max(w, Measure(Loc.F("stock.rail", n, n), 10f));
                        }
                    }
                    catch { }
                }
            }
            v.StockW = (float)Math.Ceiling(w) + 16f;
            foreach (var c in v.StockCols) c.Width = new GridLength(v.StockW);
        }

        static float Measure(string s, float size)
        {
            try
            {
                var t = new TextBlock { Text = s, FontSize = size };
                t.Measure(new Size(float.PositiveInfinity, float.PositiveInfinity));
                if (t.DesiredSize.Width > 0f) return t.DesiredSize.Width;
            }
            catch { }
            return s.Length * size * 0.6f;
        }

        /// <summary>The legend of the tab: the tooltip of (i) on the "Weapons" tab button.</summary>
        public static FrameworkElement Legend()
        {
            var sp = new StackPanel { Orientation = Orientation.Vertical, Width = 420f };
            Action<string, Color, float> line = (t, c, size) =>
            {
                var tb = T(t, c, size); tb.TextWrapping = TextWrapping.Wrap; tb.Margin = new Thickness(0f, 0f, 0f, 4f);
                sp.Children.Add(tb);
            };
            Action<UIElement, string> item = (mark, t) =>
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0f, 0f, 0f, 3f) };
                var box = new Border { Child = mark, Width = 22f, VerticalAlignment = VerticalAlignment.Center };
                row.Children.Add(box);
                var tb = T(t, WC.Sub, 12.5f); tb.TextWrapping = TextWrapping.Wrap; tb.MaxWidth = 390f;
                row.Children.Add(tb);
                sp.Children.Add(row);
            };
            Func<Chip, UIElement> chip = st => { var c = MakeChip(11f, 0); Paint(c, st); c.G.Width = 16f; c.G.Height = 16f; c.G.Cursor = null; return c.G; };
            Func<Color, UIElement> dot = c => new Ellipse { Width = 7f, Height = 7f, Fill = WC.B(c), HorizontalAlignment = HorizontalAlignment.Left };

            line(Loc.T("legend.dom"), WC.Text, 13f);
            item(chip(Chip.On), Loc.T("legend.auto"));
            item(chip(Chip.Off), Loc.T("legend.off"));
            item(chip(Chip.Order), Loc.T("legend.order"));
            item(chip(Chip.Mixed), Loc.T("legend.mixed"));
            line(Loc.T("legend.manual"), WC.Dim, 12f);

            var gap = new Border { Height = 6f }; sp.Children.Add(gap);
            line(Loc.T("legend.state"), WC.Text, 13f);
            item(dot(WC.Ok), Loc.T("legend.ready"));
            item(dot(WC.Engage), Loc.T("legend.engaging"));
            item(dot(WC.Amber), Loc.T("legend.waiting"));
            item(dot(WC.Red), Loc.T("legend.damaged"));
            item(dot(WC.Grey), Loc.T("legend.empty"));
            line(Loc.T("legend.local"), WC.Dim, 12f);

            gap = new Border { Height = 6f }; sp.Children.Add(gap);
            line(Loc.T("legend.fire"), WC.Sub, 12.5f);
            line(Loc.T("legend.channels"), WC.Sub, 12.5f);
            line(Loc.T("legend.ammo"), WC.Sub, 12.5f);
            return sp;
        }

        UIElement BuildGroup(WeaponsView v, GroupRow gr)
        {
            foreach (var l in gr.Ls) foreach (var a in l.Ammo) if (!gr.Ammo.Contains(a)) gr.Ammo.Add(a);
            gr.Expandable = gr.Ammo.Count > 1 || gr.Ls.Count > 1;
            var box = new StackPanel();
            var row = Columns(true, v);
            row.MinHeight = 40f;
            row.Background = WC.Clear;
            gr.Chevron = new Path { Data = Geometry.Parse("M 0 0 L 9 6 L 0 12 Z"), Fill = WC.B(WC.Sub), Width = 9f, Height = 12f, Stretch = Stretch.None,
                                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new Point(0.5f, 0.5f),
                                    Margin = new Thickness(0f, 0f, 4f, 0f) };   // 2 px left of the column's centre
            gr.ChevronHit = new Border { Child = gr.Chevron, Background = WC.Clear, Width = 22f, Height = 28f, VerticalAlignment = VerticalAlignment.Center };
            if (!gr.Expandable) gr.ChevronHit.Visibility = Visibility.Hidden;
            bool warmInline = false;
            if (gr.Ls.Count == 1 && _samButton != null && _samButton.Value && CanWarm(gr.Ls[0].Ws))
            {
                // one launcher: the button is in the name line (below)
                warmInline = true;
            }
            At(row, gr.ChevronHit, 0);
            // name cell: the launcher and its ammunition (one type: its name, type and characteristics; several: their names)
            var cell = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0f, 3f, 6f, 3f) };
            cell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            cell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            cell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1f, GridUnitType.Star) });
            cell.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var l0 = gr.Ls[0];
            var top = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var sysLink = Link(Cap(l0.Name) + (gr.Suffix.Length > 0 ? " " + gr.Suffix : ""), WC.Text, 13f, FontWeights.Medium, null, () => OpenSystemReference(v, l0));
            ToolTipService.SetToolTip(sysLink, DescTip(R.S(R.P(SystemEntry(l0), "Description")), "Reference of " + Cap(l0.Name)));
            top.Children.Add(sysLink);
            if (gr.Ls.Count > 1)
            {
                var badge = new Border { Child = T("×" + gr.Ls.Count, Color.FromRgb(214, 218, 223), 11f, FontWeights.SemiBold), Background = WC.B(WC.Badge),
                                         CornerRadius = new CornerRadius(2f), Padding = new Thickness(4f, 0f, 4f, 0f), Margin = new Thickness(6f, 0f, 0f, 0f), VerticalAlignment = VerticalAlignment.Center };
                top.Children.Add(badge);
            }
            if (warmInline) top.Children.Add(WarmButton(v, gr.Ls[0].Ws, true));
            // fire-control channels: a framed badge at the end of the first line
            Directors(v, gr);
            if (gr.Dirs.Count > 0 || gr.Wires)
            {
                gr.Chan = T("", WC.Sub, 10.5f);
                gr.ChanBox = new Border { Child = gr.Chan, BorderBrush = WC.B(Color.FromRgb(74, 82, 96)), BorderThickness = new Thickness(1f), Background = WC.Clear,
                                          CornerRadius = new CornerRadius(2f), Padding = new Thickness(4f, 0f, 4f, 0f), Margin = new Thickness(8f, 0f, 0f, 0f), VerticalAlignment = VerticalAlignment.Center };
                top.Children.Add(gr.ChanBox);
                if (gr.Dirs.Count > 0)
                {
                    gr.ChanBox.Cursor = Cursors.Hand;
                    gr.ChanBox.MouseEnter += (o, e) => { v.DirHover = gr; v.DirOpenAt = UnityEngine.Time.unscaledTime + 0.25f; };
                }
            }
            // the director the group's mounts fire with (blue), or local control (orange)
            gr.Fc = T("", WC.Engage, 11f); gr.Fc.Margin = new Thickness(8f, 0f, 0f, 0f);
            top.Children.Add(gr.Fc);
            cell.Children.Add(top);
            UIElement second;
            if (gr.Ammo.Count == 1)
            {
                second = AmmoLine(v, gr.Ammo[0], 11.5f);
            }
            else
            {
                var names = new WrapPanel();
                for (int i = 0; i < gr.Ammo.Count; i++)
                {
                    var id = gr.Ammo[i];
                    var ap = AmmoParams(v, id);
                    var lk = Link(AmmoName(ap, id), WC.Dim, 11.5f, null, "Reference of the weapon", () => OpenWeaponReference(v, id));
                    lk.Margin = new Thickness(0f, 0f, 2f, 0f);
                    names.Children.Add(lk);
                    if (i < gr.Ammo.Count - 1) names.Children.Add(T(", ", WC.Dim, 11.5f));
                }
                second = names;
            }
            var sf = second as FrameworkElement;
            if (sf != null) sf.Margin = new Thickness(0f, 4f, 0f, 0f);
            Grid.SetRow(second, 1);
            cell.Children.Add(second);
            At(row, cell, 1);
            gr.States = new Grid { VerticalAlignment = VerticalAlignment.Center, Background = WC.Clear, Margin = new Thickness(8f, 2f, 6f, 2f), ClipToBounds = true };
            gr.States.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            gr.States.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1f, GridUnitType.Star) });
            At(row, gr.States, 2);
            gr.Stock = MakeStock(12f, WC.Text);
            At(row, gr.Stock.P, 3);
            gr.FireIds = new List<string>(gr.Ammo); OrderFireIds(v, gr);
            At(row, Controls(v, gr.Ls, gr.FireIds, true, true, gr.Ls.Count == 1 ? gr.Ls[0] : null), 4);   // several ammunition types: Fire takes the first one, Stop / Reload act on all; each ammunition's card has its own
            gr.Chips = new ChipUI[5];
            for (int i = 0; i < 5; i++)
            {
                var c = MakeChip(14f, Domains[i]);
                int d = Domains[i];
                Click(c.G, () => ClickGroup(v, gr, d));
                gr.Chips[i] = c;
                ChipVisible(v, c.G, i);
                At(row, c.G, 5 + i);
            }
            // the chevron, the state and the stock open / close it
            if (gr.Expandable)
                foreach (UIElement e in new UIElement[] { gr.ChevronHit, gr.States, gr.Stock.P }) Click(e, () => { SetOpen(v, gr, !gr.Open); Refresh(v, true); });
            var rb = new Border { Child = row, BorderBrush = WC.B(WC.Line2), BorderThickness = new Thickness(0f, 1f, 0f, 0f), Padding = new Thickness(4f, 0f, 0f, 0f) };
            box.Children.Add(rb);
            gr.Detail = new StackPanel { Margin = new Thickness(26f, 0f, 0f, 6f) };   // = the row's padding (4) + the chevron column (22): the columns line up
            box.Children.Add(gr.Detail);
            return box;
        }

        /// <summary>Text that opens something when clicked; underlined under the mouse.</summary>
        static TextBlock Link(string text, Color c, float size, FontWeight? w, string tip, Action a)
        {
            var t = T(text, c, size, w);
            t.Background = WC.Clear;
            t.MouseEnter += (s, e) => t.TextDecorations = TextDecorations.Underline;
            t.MouseLeave += (s, e) => t.TextDecorations = TextDecorations.None;
            if (tip != null) SetTip(t, tip);
            Click(t, a, false);
            return t;
        }

        object AmmoParams(WeaponsView v, string id)
        {
            try { return R.F(R.Call(v.Unit, "getAmmunitionByName", id), "_ap"); } catch { return null; }
        }

        /// <summary>The ammunition's name for the screen; the game's "MISSING TEXT" (no text for its name) is replaced by the ammunition's id.</summary>
        static string AmmoName(object ap, string id)
        {
            var n = ap == null ? null : R.S(R.P(ap, "_displayedName"));
            return string.IsNullOrEmpty(n) || n.StartsWith("MISSING TEXT", StringComparison.Ordinal) ? id : n;
        }

        /// <summary>One line about an ammunition type: its name (a link to its reference), type and characteristics.</summary>
        UIElement AmmoLine(WeaponsView v, string id, float size)
        {
            var ap = AmmoParams(v, id);
            var g = new Grid();
            for (int i = 0; i < 4; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 3 ? new GridLength(1f, GridUnitType.Star) : GridLength.Auto });
            var name = AmmoName(ap, id);
            var type = R.S(R.F(ap, "_displayedType")) ?? "";
            var mw = MuseumWeapon(id);
            var nl = Link(name, size > 12f ? WC.Text : WC.Sub, size, size > 12f ? FontWeights.SemiBold : FontWeights.Medium, null, () => OpenWeaponReference(v, id));
            At(g, nl, 0);
            if (type.Length > 0)
            {
                var tb = new Border { Child = T(type, WC.Sub, 10.5f), BorderBrush = WC.B(Color.FromRgb(74, 82, 96)), BorderThickness = new Thickness(1f), Background = WC.Clear,
                                      CornerRadius = new CornerRadius(2f), Padding = new Thickness(4f, 0f, 4f, 0f), Margin = new Thickness(6f, 0f, 0f, 0f), VerticalAlignment = VerticalAlignment.Center };
                SetTip(tb, Loc.T("tip.weaponType"));
                At(g, tb, 2);
            }
            var specs = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var parts = Specs(ap, mw);
            var all = new StringBuilder();
            for (int i = 0; i < parts.Count; i++)
            {
                if (i > 0) specs.Children.Add(T(" · ", WC.Dim, size));
                specs.Children.Add(T(parts[i].Key, WC.Dim, size));
                all.Append(i > 0 ? "\n" : "").Append(parts[i].Value);
            }
            // one tooltip for the whole line: all of it at once, also what the column cuts off
            var clip = new Border { Child = specs, ClipToBounds = true, Margin = new Thickness(8f, 0f, 0f, 0f), Background = WC.Clear };
            if (all.Length > 0) SetTip(clip, all.ToString());
            At(g, clip, 3);
            var desc = R.S(R.P(mw, "Description"));
            ToolTipService.SetToolTip(nl, DescTip(desc, Loc.T("tip.refWeapon")));
            return g;
        }

        /// <summary>
        /// The control slots: Fire (split: the button picks the target on the map as the weapon's button in the bottom panel,
        /// the arrow opens the salvo / launch options; for one launcher the order is tied to it), cease fire, reload.
        /// Empty slots keep their room so the rows line up.
        /// </summary>
        Grid Controls(WeaponsView v, List<Launcher> scope, List<string> ammo, bool fire, bool more, Launcher pin)
        {
            var g = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64f) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28f) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28f) });
            if (fire && ammo.Count > 0) At(g, FireButton(v, ammo, pin, scope), 0);
            if (!more) return g;
            var cu = new CtrlUI { Scope = scope, Ammo = ammo };
            cu.Cease = IconButton(true, Loc.T("btn.cease"), () => Cease(v, cu));
            At(g, cu.Cease.B, 1);
            bool mag = scope.Exists(l => { try { return R.B(R.Call(l.Ws, "hasAMagazine")); } catch { return false; } });
            if (mag)
            {
                cu.Reload = IconButton(false, Loc.T("btn.reload"), () => Reload(v, cu));
                At(g, cu.Reload.B, 2);
            }
            v.Ctrls.Add(cu);
            return g;
        }

        const string MissileIcon = "M 5 0 L 7.2 3.2 L 7.2 9 L 10 12 L 10 13 L 5 11.4 L 0 13 L 0 12 L 2.8 9 L 2.8 3.2 Z";

        /// <summary>
        /// SAM readiness button: one missile per rail (bright = loaded), grey = nothing loaded, amber = loading / partly loaded,
        /// green = all rails loaded, with the time left under it.
        /// </summary>
        FrameworkElement WarmButton(WeaponsView v, object ws, bool inline = false)
        {
            var wu = new WarmUI { Ws = ws };
            wu.Icons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            wu.B = new Border { Height = 20f, MinWidth = 22f, Child = wu.Icons, BorderThickness = new Thickness(1f), CornerRadius = new CornerRadius(3f),
                                Padding = new Thickness(3f, 0f, 3f, 0f), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand };
            wu.Timer = T("", WC.Ok, 9f); wu.Timer.HorizontalAlignment = HorizontalAlignment.Center; wu.Timer.Visibility = Visibility.Collapsed;
            wu.B.MouseEnter += (s, e) => { wu.Over = true; PaintWarm(wu); };
            wu.B.MouseLeave += (s, e) => { wu.Over = false; PaintWarm(wu); };
            Click(wu.B, () => { if ((object)Active != null) Active.ClickSound(); WarmToggle(ws); Refresh(v, false); }, false);
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            if (inline)
            {
                // in the launcher's name line (the rails' icons do not fit the narrow chevron column): the time left beside the button
                sp.Orientation = Orientation.Horizontal; sp.Margin = new Thickness(8f, 1f, 0f, 0f);
                wu.Timer.Margin = new Thickness(5f, 0f, 0f, 0f);
            }
            sp.Children.Add(wu.B); sp.Children.Add(wu.Timer);
            v.Warms.Add(wu);
            RefreshWarm(wu);
            return sp;
        }

        static void PaintWarm(WarmUI wu)
        {
            Color bg, hover, line, ink;
            switch (wu.St)
            {
                case 1: case 2: bg = Color.FromRgb(66, 52, 24); hover = Color.FromRgb(92, 72, 30); line = Color.FromRgb(150, 116, 48); ink = Color.FromRgb(240, 205, 130); break;
                case 3: bg = Color.FromRgb(30, 62, 48); hover = Color.FromRgb(42, 88, 66); line = Color.FromRgb(62, 140, 104); ink = Color.FromRgb(170, 235, 205); break;
                default: bg = WC.BtnBg; hover = WC.BtnHover; line = WC.BtnLine; ink = WC.Dim; break;
            }
            wu.B.Background = WC.B(wu.Over ? hover : bg);
            wu.B.BorderBrush = WC.B(line);
            var br = WC.B(ink);
            for (int i = 0; i < wu.Icons.Children.Count; i++)
            {
                var p = (Path)wu.Icons.Children[i];
                p.Fill = br;
                p.Opacity = wu.Rails != null && i < wu.Rails.Count && wu.Rails[i] ? 1f : (wu.St == 1 ? 0.55f : 0.3f);
            }
            wu.Timer.Foreground = WC.B(WC.Ok);
        }

        static string Mmss(float s) { int t = (int)Math.Ceiling(s); return (t / 60) + ":" + (t % 60).ToString("00"); }

        static void RefreshWarm(WarmUI wu)
        {
            var wi = WarmState(wu.Ws);
            var rails = wi.Rails;
            if (rails.Count == 0) rails.Add(false);
            string sig = wi.St + ":"; foreach (var r in rails) sig += r ? "1" : "0";
            if (rails.Count != wu.Icons.Children.Count)
            {
                wu.Icons.Children.Clear();
                bool one = rails.Count == 1;
                for (int i = 0; i < rails.Count; i++)
                    wu.Icons.Children.Add(new Path { Data = Geometry.Parse(MissileIcon), Stretch = Stretch.Uniform, Width = one ? 10f : 8f, Height = one ? 13f : 10.5f,
                                                     Margin = new Thickness(i == 0 ? 0f : 2f, 0f, 0f, 0f) });
            }
            string tx = wi.Left >= 0f ? Mmss(wi.Left) : "";
            if (sig != wu.Sig) { wu.Sig = sig; wu.St = wi.St; wu.Rails = rails; PaintWarm(wu); }
            if (tx != wu.TimerText)
            {
                wu.TimerText = tx; wu.Timer.Text = tx;
                wu.Timer.Visibility = tx.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            int loaded = 0; foreach (var r in rails) if (r) loaded++;
            string tip = wi.St == 0 ? Loc.T("sam.away")
                       : wi.St == 1 ? Loc.T("sam.loading")
                       : (wi.St == 2 ? Loc.F("sam.railsLoaded", loaded, rails.Count) : Loc.T("sam.ready"))
                         + (wi.Left >= 0f ? Loc.F("sam.putAwayIn", Mmss(wi.Left)) : "")
                         + Loc.T("sam.putAway");
            if (tip != wu.Tip) { wu.Tip = tip; Tip(wu.B, tip); }
        }

        /// <summary>A small square button with a drawn icon: cease fire (red tones: a square) or reload (blue tones: a circular arrow).</summary>
        IconBtn IconButton(bool cease, string tip, Action a)
        {
            var ib = new IconBtn { Tip = tip, Cease = cease };
            var ink = WC.B(cease ? WC.CeaseInk : WC.ReloadInk);
            var icon = new Grid { Width = 12f, Height = 12f, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            if (cease) icon.Children.Add(new Rectangle { Width = 8f, Height = 8f, Fill = ink, RadiusX = 1f, RadiusY = 1f, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            else
            {
                // an arc clockwise from 1 o'clock round to 11 o'clock (centre 6,6, radius 4.2) and its arrow head
                icon.Children.Add(new Path { Data = Geometry.Parse("M 8.1 2.36 A 4.2 4.2 0 1 1 3.9 2.36"), Stroke = ink, StrokeThickness = 1.6f, Stretch = Stretch.None,
                                             HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top });
                icon.Children.Add(new Path { Data = Geometry.Parse("M 6.6 0.8 L 4.4 4.2 L 2.6 0.6 Z"), Fill = ink, Stretch = Stretch.None,
                                             HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top });
            }
            ib.B = new Border { Width = 24f, Height = 22f, Child = icon, BorderThickness = new Thickness(1f), CornerRadius = new CornerRadius(3f),
                                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            PaintIcon(ib);
            SetTip(ib.B, tip);
            ib.B.MouseEnter += (s, e) => { ib.Over = true; PaintIcon(ib); };
            ib.B.MouseLeave += (s, e) => { ib.Over = false; PaintIcon(ib); };
            Click(ib.B, () => { if (!ib.Enabled) return; if ((object)Active != null) Active.ClickSound(); a(); }, false);
            return ib;
        }

        static void PaintIcon(IconBtn ib)
        {
            bool hot = ib.Enabled && ib.Over;
            ib.B.Opacity = ib.Enabled ? 1f : 0.35f;
            ib.B.Background = WC.B(ib.Cease ? (hot ? WC.CeaseHover : WC.CeaseBg) : (hot ? WC.ReloadHover : WC.ReloadBg));
            ib.B.BorderBrush = WC.B(ib.Cease ? WC.CeaseLine : WC.ReloadLine);
            ib.B.Cursor = ib.Enabled ? Cursors.Hand : null;
        }

        static void SetEnabled(IconBtn ib, bool on) { if (ib == null || ib.Enabled == on) return; ib.Enabled = on; PaintIcon(ib); }

        /// <summary>Cease fire: the launchers drop what they engage, and the unit's waiting orders for them go.</summary>
        void Cease(WeaponsView v, CtrlUI cu)
        {
            _stopWatchUntil = UnityEngine.Time.realtimeSinceStartup + 4f;
            if (_debug) { try { Trace("Stop clicked: " + string.Join("/", cu.Ammo.ToArray()) + ", " + cu.Scope.Count + " launcher(s), engaged before: " + CeaseState(cu)); } catch { } }
            foreach (var l in cu.Scope)
            {
                if (R.B(R.F(l.Ws, "_executingEngageTask")) || R.B(R.F(l.Ws, "_isAutoEngaging")) || R.F(l.Ws, "_takenEngageTask") != null)
                {
                    // one ammunition of a multi-ammunition system (aircraft stores): only when it is the one being fired
                    if (cu.Ammo.Count < l.Ammo.Count)
                    {
                        var cur = R.S(R.F(R.F(R.F(l.Ws, "_ammoForEngage"), "_ap"), "_ammunitionFileName"));
                        if (cur != null && !cu.Ammo.Contains(cur)) continue;
                    }
                    try { R.Call(l.Ws, "EndEngageTask", true); } catch (Exception e) { Trace("cease: " + e.Message); }
                    if (R.Is(l.Ws, _tCiws)) { try { R.Call(l.Ws, "StopEngage"); } catch { } }
                }
            }
            ForgetTargets(v.Unit, cu.Ammo);
            var tasks = R.F(v.Unit, "_currentEngageTasks") as IList;
            if (tasks != null)
                for (int i = tasks.Count - 1; i >= 0; i--)
                {
                    var t = tasks[i];
                    if (t == null) continue;
                    var pin = PinOf(t);
                    bool mine = pin != null ? cu.Scope.Exists(l => ReferenceEquals(l.Ws, pin)) : cu.Scope.Count > 1 || IsWholeAmmo(v, cu, R.S(R.F(t, "_ammoId")));
                    if (mine && cu.Ammo.Contains(R.S(R.F(t, "_ammoId")))) { try { R.Call(v.Unit, "RemoveEngageTask", t); } catch { tasks.RemoveAt(i); } }
                }
            if (_debug) { try { Trace("Stop done: still engaged: " + CeaseState(cu) + "; unit orders left: " + (tasks == null ? 0 : tasks.Count)); } catch { } }
            Refresh(v, false);
        }

        static float _stopWatchUntil;   // trace only: for a few seconds after a Stop click, every cease button's state is written

        static string CeaseState(CtrlUI cu)
        {
            var sb = new StringBuilder(); int n = 0;
            foreach (var l in cu.Scope)
            {
                bool ex = R.B(R.F(l.Ws, "_executingEngageTask")), au = R.B(R.F(l.Ws, "_isAutoEngaging")), tk = R.F(l.Ws, "_takenEngageTask") != null;
                if (!ex && !au && !tk) continue;
                n++; if (n <= 8) sb.Append(l.Name).Append(" #").Append(l.Index).Append(ex ? " exec" : "").Append(au ? " auto" : "").Append(tk ? " task" : "").Append("; ");
            }
            return n == 0 ? "none" : n + " (" + sb + ")";
        }

        /// <summary>Does this scope hold every launcher of the unit with the ammunition (so the unit's open orders for it are its own)?</summary>
        bool IsWholeAmmo(WeaponsView v, CtrlUI cu, string ammo)
        {
            foreach (var l in CachedLaunchers(v.Unit, W(v.Unit))) if (l.Ammo.Contains(ammo) && !cu.Scope.Exists(x => ReferenceEquals(x.Ws, l.Ws))) return false;
            return true;
        }

        void Reload(WeaponsView v, CtrlUI cu)
        {
            foreach (var l in cu.Scope)
            {
                if (CanWarm(l.Ws)) { Recharge(l.Ws); continue; }
                try { if (R.B(R.Call(l.Ws, "CanForceReload"))) R.Call(l.Ws, "ForceReload"); } catch (Exception e) { Trace("reload: " + e.Message); }
            }
            Refresh(v, false);
        }

        static bool Engaging(Launcher l) { return R.B(R.F(l.Ws, "_executingEngageTask")) || R.B(R.F(l.Ws, "_isAutoEngaging")); }

        void RefreshCtrls(WeaponsView v)
        {
            foreach (var wu in v.Warms) RefreshWarm(wu);
            var tasks = R.F(v.Unit, "_currentEngageTasks") as IList;
            foreach (var cu in v.Ctrls)
            {
                bool cease = cu.Scope.Exists(Engaging);
                if (!cease && tasks != null)
                    foreach (var t in tasks)
                    {
                        if (t == null || !cu.Ammo.Contains(R.S(R.F(t, "_ammoId")))) continue;
                        var pin = PinOf(t);
                        if (pin != null ? cu.Scope.Exists(l => ReferenceEquals(l.Ws, pin)) : (cu.Scope.Count > 1 || IsWholeAmmo(v, cu, R.S(R.F(t, "_ammoId"))))) { cease = true; break; }
                    }
                SetEnabled(cu.Cease, cease);
                if (_debug && UnityEngine.Time.realtimeSinceStartup < _stopWatchUntil)
                    TraceLimited("sw" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(cu), 0.5f,
                        "after Stop: " + string.Join("/", cu.Ammo.ToArray()) + " x" + cu.Scope.Count + " cease button " + (cease ? "ON" : "off") + "; " + CeaseState(cu) + "; unit orders " + (tasks == null ? 0 : tasks.Count));
                if (cu.Reload != null)
                {
                    bool can = cu.Scope.Exists(l => { try { return CanWarm(l.Ws) ? !IsEngaging(l.Ws) && !ReloadBusy(l.Ws) : R.B(R.Call(l.Ws, "CanForceReload")); } catch { return false; } });
                    SetEnabled(cu.Reload, can);
                    Tip(cu.Reload.B, can ? cu.Reload.Tip : Loc.F("btn.nothingToReload", cu.Reload.Tip));
                }
            }
        }

        FrameworkElement FireButton(WeaponsView v, List<string> ids, Launcher pin, List<Launcher> scope = null)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var main = new Border { Child = T(Loc.T("btn.fire"), WC.FireText, 12f, FontWeights.Medium), Padding = new Thickness(9f, 0f, 7f, 0f), Background = WC.Clear };
            SetTip(main, pin != null && v.Groups.Exists(x => x.Ls.Count > 1 && x.Ls.Contains(pin)) ? "Fire this launcher: pick the target on the map" : "Fire: pick the target on the map");
            var arrow = new Border { Child = T("▾", WC.FireText, 11f), Padding = new Thickness(6f, 0f, 6f, 0f), Background = WC.Clear,
                                     BorderBrush = WC.B(WC.FireLine), BorderThickness = new Thickness(1f, 0f, 0f, 0f) };
            SetTip(arrow, Loc.T("btn.salvoTip"));
            Grid.SetColumn(arrow, 1);
            g.Children.Add(main); g.Children.Add(arrow);
            var fire = new Border { Height = 22f, Background = WC.B(WC.FireBg), BorderBrush = WC.B(WC.FireLine), BorderThickness = new Thickness(1f), CornerRadius = new CornerRadius(3f),
                                    Child = g, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var fu = new FireUI { Ids = ids, Pin = pin, Box = fire, Main = main, Arrow = arrow };
            v.Fires.Add(fu);
            main.MouseEnter += (s, e) => { fu.OverMain = true; PaintFire(fu); };
            main.MouseLeave += (s, e) => { fu.OverMain = false; PaintFire(fu); };
            arrow.MouseEnter += (s, e) => { fu.OverArrow = true; PaintFire(fu); };
            arrow.MouseLeave += (s, e) => { fu.OverArrow = false; PaintFire(fu); };
            Click(main, () =>
            {
                if (_debug) { try { var all = R.F(R.F(v.Unit, "_obp"), "_weaponSystems") as IList; Trace("Fire clicked: " + PickAmmo(ids, pin, scope) + ", launcher " + (pin == null ? "none (any)" : "#" + (all == null ? -1 : all.IndexOf(pin.Ws)))); } catch { } }
                SetPending(v, pin, ids); FireNow(v, PickAmmo(ids, pin, scope)); CheckTargeting(v);
            });
            Click(arrow, () => { SetPending(v, pin, ids); OpenFireMenu(v, ids, fire); });
            return fire;
        }

        static void SetPending(WeaponsView v, Launcher pin, List<string> ids)
        {
            Pending = pin == null ? null : new PendingPin { Unit = v.Unit, Ws = pin.Ws, Ammos = new List<string>(ids), Since = MissionTime() };
        }

        /// <summary>The ammunition a launcher's Fire uses: the one it holds ready, else its first.</summary>
        static string PickAmmo(List<string> ids, Launcher pin, List<Launcher> scope = null)
        {
            if (pin == null)
            {
                // the chosen ammunition (first); when none of the launchers has any left, the first type that still has some
                if (scope != null && ids.Count > 1)
                    foreach (var a in ids)
                        foreach (var l in scope)
                            try { if (l.Ammo.Contains(a) && (R.I(R.Call(l.Ws, "getLoadedAmmoCount", a)) > 0 || R.I(R.Call(l.Ws, "getMagazineAmmoCount", a)) > 0)) return a; } catch { }
                return ids[0];
            }
            if (ids.Count == 1) return ids[0];
            var cur = R.S(R.F(R.F(R.F(pin.Ws, "_ammoForEngage"), "_ap"), "_ammunitionFileName"));
            if (cur != null && ids.Contains(cur)) return cur;
            foreach (var a in ids) { try { if (R.I(R.Call(pin.Ws, "getLoadedAmmoCount", a)) > 0) return a; } catch { } }
            return ids[0];
        }

        static void PaintFire(FireUI f)
        {
            f.Box.Background = WC.B(f.Active ? WC.FireActive : WC.FireBg);
            f.Box.BorderBrush = WC.B(f.Active ? WC.FireActiveLine : WC.FireLine);
            f.Main.Background = f.OverMain ? WC.B(f.Active ? WC.FireActiveLine : WC.FireHover) : WC.Clear;
            f.Arrow.Background = f.OverArrow ? WC.B(f.Active ? WC.FireActiveLine : WC.FireHover) : WC.Clear;
            f.Arrow.BorderBrush = WC.B(f.Active ? WC.FireActiveLine : WC.FireLine);
        }

        /// <summary>The fire button whose ammunition the game is picking a target for (AttackingState of this unit) is red until the state ends.</summary>
        static System.Reflection.FieldInfo _fMgvm;   // (looked up once: this runs every frame)
        static void CheckTargeting(WeaponsView v)
        {
            string id = null; bool ours = false;   // ours: the targeting was started by this mod's Fire (the zones are shown only for it)
            try
            {
                if (_fMgvm == null) { var globals = GameAssemblyStatic().GetType("SeaPower.Globals"); if (globals != null) _fMgvm = globals.GetField("_mainGameViewModel", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic); }
                var mgvm = _fMgvm == null ? null : _fMgvm.GetValue(null);
                var st = R.Rv(R.P(R.P(mgvm, "StateManager"), "CurrentState"));
                if (st != null && st.GetType().Name == "AttackingState" && ReferenceEquals(R.P(st, "Source"), v.Unit)) { id = R.S(R.P(st, "Ammunition")); ours = IsOurState(st); if (!ours && _debug) TraceLimited("notours", 5f, "zones: targeting of " + id + " is not ours (no mark from the mod's Fire)"); }
            }
            catch { }
            // the launcher tie of a Fire ends 3 s after the game's target picking for that unit is over (30 s if it never began)
            var pp = Pending;
            if (pp != null && ReferenceEquals(pp.Unit, v.Unit))
            {
                float now = UnityEngine.Time.unscaledTime;
                if (id != null) { pp.Seen = true; pp.EndAt = -1f; }
                else if (pp.EndAt < 0f) pp.EndAt = now + (pp.Seen ? 3f : 30f);
                else if (now > pp.EndAt) { Pending = null; pp = null; }
            }
            else pp = null;
            {
                object zpin = null;
                if (id != null && pp != null && ReferenceEquals(pp.Unit, v.Unit) && pp.Ws != null && pp.Ammos.Contains(id)) zpin = pp.Ws;
                var we = id == null ? null : WeaponEntryFor(v, id);
                ZonesUpdate(v.Unit, ours ? id : null, zpin, we == null ? null : R.P(we, "WeaponSystems") as IEnumerable);
            }
            foreach (var f in v.Fires)
            {
                bool a = id != null && f.Ids.Contains(id) && (f.Pin == null ? pp == null : pp != null && ReferenceEquals(pp.Ws, f.Pin.Ws));
                if (a == f.Active) continue;
                f.Active = a;
                PaintFire(f);
            }
        }

        void FireNow(WeaponsView v, string id)
        {
            var cmd = R.P(WeaponEntryFor(v, id), "Command");
            if (cmd != null) OurFireExecute(cmd);
        }

        void BuildDetail(WeaponsView v, GroupRow gr)
        {
            gr.DetailBuilt = true;
            bool pick = gr.Ammo.Count > 1 && gr.Ls.Count > 1;   // choose the active ammunition: the launchers are listed under it
            if (pick) { gr.ActiveAmmo = RememberedActive(v, gr) ?? DefaultActive(gr); OrderFireIds(v, gr); }
            if (gr.Ammo.Count > 1) foreach (var id in gr.Ammo) gr.Detail.Children.Add(AmmoCard(v, gr, id));
            if (gr.Ls.Count < 2) return;
            var names = new Dictionary<string, int>();
            foreach (var l in gr.Ls)
            {
                var m = new MemberRow { L = l, Chips = new ChipUI[5] };
                // label | state (all the room up to the stock: the game's states can be long) | stock | chips
                var g = MemberColumns(v);
                g.MinHeight = 24f;
                var label = Cap(string.IsNullOrEmpty(l.Loc) ? l.Name : l.Loc);
                int k; names[label] = names.TryGetValue(label, out k) ? k + 1 : 1;
                int same = 0; foreach (var x in gr.Ls) if (Cap(string.IsNullOrEmpty(x.Loc) ? x.Name : x.Loc) == label) same++;
                if (same > 1) label += " #" + names[label];
                var lab = new StackPanel { Orientation = Orientation.Horizontal };
                lab.Children.Add(T(label, WC.Sub, 12f));
                m.Fc = T("", WC.Engage, 11f); m.Fc.Margin = new Thickness(8f, 0f, 0f, 0f);
                lab.Children.Add(m.Fc);
                var nb = new Border { Child = lab, BorderBrush = WC.B(WC.Badge), BorderThickness = new Thickness(2f, 0f, 0f, 0f),
                                      Padding = new Thickness(6f, 0f, 0f, 0f), Margin = new Thickness(8f, 0f, 0f, 0f) };
                At(g, nb, 0);
                if (_samButton != null && _samButton.Value && CanWarm(l.Ws))
                {
                    var wb = WarmButton(v, l.Ws);
                    wb.HorizontalAlignment = HorizontalAlignment.Left; wb.VerticalAlignment = VerticalAlignment.Center;
                    wb.Margin = new Thickness(-20f, 0f, 0f, 0f);   // in the chevron's column, left of the member rows
                    At(g, wb, 0);
                }
                // state: wraps to a second line when long, centred
                var st = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8f, 2f, 6f, 2f), Background = WC.Clear };
                m.Dot = new Ellipse { Width = 6f, Height = 6f, Margin = new Thickness(0f, 0f, 5f, 0f), VerticalAlignment = VerticalAlignment.Center };
                m.State = T("", WC.Ok, 12f); m.State.TextTrimming = TextTrimming.CharacterEllipsis;   // one line: the row keeps its height
                st.Children.Add(Centred(m.Dot, m.State));
                FitWidth(st, m.State);
                At(g, st, 1);
                m.Stock = MakeStock(12f, WC.Sub);
                At(g, m.Stock.P, 2);
                m.G = g;
                m.Ids = pick ? new List<string>(l.Ammo.Contains(gr.ActiveAmmo) ? new[] { gr.ActiveAmmo } : l.Ammo.ToArray()) : l.Ammo;
                At(g, Controls(v, new List<Launcher> { l }, m.Ids, true, true, l), 3);
                if (pick && v.Ctrls.Count > 0) v.Ctrls[v.Ctrls.Count - 1].Ammo = new List<string>(l.Ammo);   // Stop ends the launcher's engagements with any of its ammunition
                for (int i = 0; i < 5; i++)
                {
                    var c = MakeChip(10f, Domains[i]);
                    int d = Domains[i];
                    var lm = l;
                    Click(c.G, () => ClickOne(v, lm, d));
                    m.Chips[i] = c;
                    ChipVisible(v, c.G, i);
                    At(g, c.G, 4 + i);
                }
                gr.Members.Add(m);
                if (!pick) gr.Detail.Children.Add(g);
            }
            if (pick) LayoutDetail(gr);
        }

        // the choice of the active ammunition is kept for the unit while the game runs (not in the saves)
        static readonly ConditionalWeakTable<object, Dictionary<string, string>> ActiveMemory = new ConditionalWeakTable<object, Dictionary<string, string>>();

        static string RememberedActive(WeaponsView v, GroupRow gr)
        {
            Dictionary<string, string> d; string a;
            return v.Unit != null && ActiveMemory.TryGetValue(v.Unit, out d) && d.TryGetValue(gr.Key, out a) && gr.Ammo.Contains(a) ? a : null;
        }

        /// <summary>The ammunition a group starts with: the one a launcher holds ready, else one loaded in a launcher, else the first.</summary>
        static string DefaultActive(GroupRow gr)
        {
            foreach (var l in gr.Ls)
            {
                try { var cur = R.S(R.F(R.F(R.F(l.Ws, "_ammoForEngage"), "_ap"), "_ammunitionFileName")); if (cur != null && gr.Ammo.Contains(cur)) return cur; } catch { }
            }
            foreach (var l in gr.Ls)
                foreach (var a in gr.Ammo) { try { if (l.Ammo.Contains(a) && R.I(R.Call(l.Ws, "getLoadedAmmoCount", a)) > 0) return a; } catch { } }
            return gr.Ammo[0];
        }

        /// <summary>The ammunition cards in order; the launchers that can use the active one are under its card, the others (dimmed) at the end.</summary>
        static void LayoutDetail(GroupRow gr)
        {
            gr.Detail.Children.Clear();
            foreach (var c in gr.Cards)
            {
                gr.Detail.Children.Add(c.G);
                if (c.Id != gr.ActiveAmmo) continue;
                foreach (var m in gr.Members) if (m.L.Ammo.Contains(c.Id)) { m.G.Opacity = 1f; gr.Detail.Children.Add(m.G); }
            }
            foreach (var m in gr.Members) if (!m.L.Ammo.Contains(gr.ActiveAmmo)) { m.G.Opacity = 0.5f; gr.Detail.Children.Add(m.G); }
        }

        /// <summary>The group's own Fire uses the chosen ammunition: it goes first (not chosen yet: the one the launchers hold ready, else the first).</summary>
        static void OrderFireIds(WeaponsView v, GroupRow gr)
        {
            if (gr.FireIds == null || gr.FireIds.Count < 2) return;
            var a = gr.ActiveAmmo ?? RememberedActive(v, gr) ?? DefaultActive(gr);
            if (a != null && gr.FireIds.Remove(a)) gr.FireIds.Insert(0, a);
        }

        void SetActive(WeaponsView v, GroupRow gr, string id)
        {
            if (gr.ActiveAmmo == id) return;
            gr.ActiveAmmo = id;
            OrderFireIds(v, gr);
            if (v.Unit != null) ActiveMemory.GetOrCreateValue(v.Unit)[gr.Key] = id;
            foreach (var m in gr.Members)
            {
                m.Ids.Clear();
                if (m.L.Ammo.Contains(id)) m.Ids.Add(id); else m.Ids.AddRange(m.L.Ammo);
            }
            foreach (var c in gr.Cards) PaintCard(c, c.Id == id);
            LayoutDetail(gr);
            Refresh(v, true);
        }

        static void PaintCard(AmmoCardUI c, bool on)
        {
            c.B.BorderBrush = WC.B(on ? WC.Engage : WC.Line);
            c.B.Background = WC.B(on ? Color.FromRgb(28, 38, 46) : WC.Ammo);
            c.Dot.Visibility = on ? Visibility.Visible : Visibility.Hidden;
        }

        /// <summary>One of several ammunition types of a group: its line in a frame, its stock, its fire button (in the control block).</summary>
        UIElement AmmoCard(WeaponsView v, GroupRow gr, string id)
        {
            var g = MemberColumns(v);
            g.MinHeight = 28f;
            var line = AmmoLine(v, id, 12f);
            bool pick = gr.Ammo.Count > 1 && gr.Ls.Count > 1;
            AmmoCardUI cardUi = null;
            UIElement inner = line;
            if (pick)
            {
                // the selector of the active ammunition before the name
                var rg = new Grid();
                rg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                rg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1f, GridUnitType.Star) });
                var ring = new Border { Width = 16f, Height = 16f, CornerRadius = new CornerRadius(8f), BorderBrush = WC.B(WC.Engage), BorderThickness = new Thickness(1.5f),
                                        Background = WC.Clear, Margin = new Thickness(0f, 0f, 8f, 0f), VerticalAlignment = VerticalAlignment.Center };
                var dot = new Ellipse { Width = 8f, Height = 8f, Fill = WC.B(WC.Engage), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                ring.Child = dot;
                SetTip(ring, Loc.T("ammo.activeTip"));
                rg.Children.Add(ring);
                Grid.SetColumn((UIElement)line, 1); rg.Children.Add((UIElement)line);
                inner = rg;
                cardUi = new AmmoCardUI { Id = id, Dot = dot };
                Click(ring, () => SetActive(v, gr, id));
            }
            var b = new Border { Child = inner, Background = WC.B(WC.Ammo), BorderBrush = WC.B(WC.Line), BorderThickness = new Thickness(1f), CornerRadius = new CornerRadius(3f),
                                 Padding = new Thickness(8f, 2f, 6f, 2f), Margin = new Thickness(0f, 2f, 8f, 2f), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumnSpan(b, 2);
            g.Children.Add(b);
            if (cardUi != null)
            {
                cardUi.B = b; cardUi.G = g;
                gr.Cards.Add(cardUi);
                Click(b, () => SetActive(v, gr, id));
                PaintCard(cardUi, id == gr.ActiveAmmo);
            }
            var stock = MakeStock(12f, WC.Sub);
            At(g, stock.P, 2);
            gr.AmmoStocks.Add(new KeyValuePair<string, StockUI>(id, stock));
            var scope = gr.Ls.FindAll(x => x.Ammo.Contains(id));
            At(g, Controls(v, scope, new List<string> { id }, true, true, scope.Count == 1 ? scope[0] : null), 3);   // one launcher: the order is tied to it
            // copies of the group's switches (they switch the whole group), for the domains this ammunition can engage
            var ac = new AmmoChipsUI { Id = id, Chips = new ChipUI[5] };
            foreach (var l in scope) { int ix = l.Ammo.IndexOf(id); if (ix >= 0 && ix < l.AmmoDom.Length) ac.Dom |= l.AmmoDom[ix]; }
            for (int i = 0; i < 5; i++)
            {
                var c = MakeChip(10f, Domains[i]);
                int d = Domains[i];
                Click(c.G, () => { if ((ac.Dom & d) != 0) ClickGroup(v, gr, d); });
                ac.Chips[i] = c;
                ChipVisible(v, c.G, i);
                At(g, c.G, 4 + i);
            }
            gr.AmmoChips.Add(ac);
            return g;
        }

        /// <summary>What a weapon system is called in counts: turret (gun), mount (CIWS, torpedo), launcher (the rest).</summary>
        static string Noun(Launcher l)
        {
            switch (R.S(R.F(l.Ws, "_weaponType")))
            {
                case "Gun": return "turret";
                case "CIWS": return "mount";
                case "Torpedo": return "mount";
                default: return "launcher";
            }
        }

        static string Cap(string s) { return string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1); }

        static object MuseumWeapon(string id)
        {
            try
            {
                var edm = GameAssemblyStatic().GetType("SeaPower.EncyclopedicDataManager");
                var inst = edm == null ? null : edm.GetProperty("Instance", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy).GetValue(null, null);
                var list = R.P(inst, "WeaponMuseumEntries") as IEnumerable;
                if (list != null) foreach (var e in list) if (R.S(R.P(e, "IniRef")) == id) return e;
            }
            catch { }
            return null;
        }

        static System.Reflection.Assembly GameAssemblyStatic() { return _tObjectBase == null ? null : _tObjectBase.Assembly; }

        /// <summary>The game's guidance name (parts joined by ", "): when the whole text is longer than 15 characters each part is shown as the short form of the language
        /// (lang file, keys guid.&lt;GuidanceType&gt;: SARH, ПАРГСН ...); an empty / missing entry keeps the game's name. The full name is in the tooltip.</summary>
        static string ShortGuidance(string g, object mw)
        {
            var names = new List<string>();
            var prim = R.P(mw, "GuidanceType"); if (prim != null) names.Add(prim.ToString());
            var sec = R.P(mw, "SecondaryPassiveRadarGuidanceType");
            if (sec != null && sec.ToString() != "None") names.Add("HomeOnJam");
            var texts = g.Split(new[] { ", " }, StringSplitOptions.None);
            if (texts.Length != names.Count) return g;
            if (g.Length <= 15) return g;   // the whole text counts: a second type (Home on Jam) makes it long
            for (int k = 0; k < texts.Length; k++) texts[k] = Loc.Or("guid." + names[k], texts[k]);
            return string.Join(", ", texts);
        }

        /// <summary>The characteristics shown in a line: (text, tooltip).</summary>
        static List<KeyValuePair<string, string>> Specs(object ap, object mw)
        {
            var parts = new List<KeyValuePair<string, string>>();
            var g = R.S(R.P(mw, "GuidanceTypeText"));
            if (!string.IsNullOrEmpty(g) && g != "None") parts.Add(new KeyValuePair<string, string>(ShortGuidance(g, mw), Loc.F("spec.guidance", g)));   // the full name is in the tooltip
            float min = R.Fl(R.P(R.P(mw, "MinLaunchRange"), "InNauticalMiles")), max = R.Fl(R.P(R.P(mw, "MaxLaunchRange"), "InNauticalMiles"));
            if (max > 0f && !float.IsNaN(max))
            {
                bool hasMin = min > 0.01f && !float.IsNaN(min);
                parts.Add(new KeyValuePair<string, string>((hasMin ? Num(min) + "–" : Loc.T("spec.upTo") + " ") + Num(max) + " " + Loc.T("spec.nm"),
                                                           Loc.F("spec.range", (hasMin ? Num(min) + "–" : "") + Num(max))));
            }
            var type = R.S(R.F(ap, "_type"));
            float kt = R.Fl(R.F(ap, "_maxVelocityInKnots"));
            if (kt > 0f && type != "Projectile") parts.Add(new KeyValuePair<string, string>(Num(kt) + " " + Loc.T("spec.kt"), Loc.F("spec.speed", Num(kt))));
            float wh = R.Fl(R.F(ap, "_warheadPower"));
            if (wh > 0f) parts.Add(new KeyValuePair<string, string>(Loc.T("spec.pwr") + " " + Num(wh), Loc.F("spec.warhead", Num(wh))));
            return parts;
        }

        static string Num(float f)
        {
            return f >= 100f ? f.ToString("0", CultureInfo.InvariantCulture) : f >= 10f ? f.ToString("0.#", CultureInfo.InvariantCulture) : f.ToString("0.##", CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------------ state

        /// <summary>Not a gun and nothing left in it (rails and magazine): the game sets _isEmpty only for guns, chaff and hardpoints.</summary>
        static bool NoAmmo(Launcher l)
        {
            return l != null && !IsGun(l) && Stock(new List<Launcher> { l }) == 0;
        }

        static int Cat(object ws, Launcher l = null)
        {
            if (R.B(R.Rv(R.P(ws, "Inoperable")))) return 3;
            if (R.B(R.F(ws, "_isOffline")) || R.B(R.F(ws, "_isEmpty"))) return 4;
            if (NoAmmo(l) && !R.B(R.F(ws, "_executingEngageTask"))) return 4;
            if (PinWait(ws) != null) return 5;
            if (R.B(R.F(ws, "_isReloading")) || R.Fl(R.F(ws, "_remainingReloadTime")) > 0f) return 2;
            if (SamBusy(ws)) return 2;
            if (SensorsOff(ws)) return 6;
            if (R.B(R.F(ws, "_executingEngageTask")) || R.B(R.F(ws, "_isAutoEngaging"))) return 1;
            return 0;
        }

        const int NCat = 7;
        static readonly string[] CatKeys = { "ready", "engaging", "reload", "damaged", "off", "waiting", "sensorsOff" };
        static string CatName(int c) { return Loc.T("cat." + CatKeys[c]); }
        static Color CatColor(int c) { return c == 0 ? WC.Ok : c == 1 ? WC.Engage : c == 2 ? WC.Amber : c == 3 ? WC.Red : c == 5 || c == 6 ? WC.Amber : WC.Grey; }

        /// <summary>All the launcher's directors switched off - the game's status "Sensors Offline" (WeaponSystem.GetStatus: it has
        /// directors, none IsOn).</summary>
        static bool SensorsOff(object ws)
        {
            var sens = R.F(R.F(ws, "_vwp"), "_associatedSensors") as IList;
            if (sens == null || sens.Count == 0) return false;
            foreach (var x in sens) if (x != null && R.B(R.Rv(R.P(x, "IsOn")))) return false;
            return true;
        }

        static int Loaded(object ws, List<string> ammo)
        {
            int n = 0;
            foreach (var a in ammo) { try { n += R.I(R.Call(ws, "getLoadedAmmoCount", a)); } catch { } }
            return n;
        }

        static int Stock(List<Launcher> ls, string only = null)
        {
            int n = 0;
            var mags = new List<object>();
            foreach (var l in ls)
            {
                var ammo = only == null ? l.Ammo : l.Ammo.Contains(only) ? new List<string> { only } : new List<string>();
                var mag = R.F(R.F(l.Ws, "_vwp"), "_associatedMagazine");
                // a gun with a magazine: the rounds in the mount (_sharedLoadedAmmoCount) are part of the magazine's count, not extra
                if (mag == null || !IsGun(l)) n += Loaded(l.Ws, ammo);
                if (mag == null || mags.Contains(mag)) continue;
                mags.Add(mag);
                foreach (var a in ammo) { try { n += R.I(R.Call(mag, "getAmmunitionCount", a)); } catch { } }
            }
            return n;
        }

        sealed class TitleUI { public TextBlock Tb; public int Section; public List<int> Always = new List<int>(), SamExtra = new List<int>(); }

        /// <summary>The title of a group card (a section): its name, and after it "/" the other purposes of the ammunition of its launchers
        /// ("ANTI-SHIP/ASW": URK-5 hits ships and submarines). What a SAM can also hit ("SAM/ANTI-SHIP") is added only with [UI] SectionTargetTypes.
        /// A torpedo has one purpose. Updated with the option in Refresh (TitleText).</summary>
        TitleUI MakeTitle(WeaponsView v, int section, List<GroupRow> groups)
        {
            var t = new TitleUI { Section = section };
            if (section <= 3)
                foreach (var gr in groups)
                {
                    var l = gr.Ls[0];
                    if (l.AmmoDom == null) continue;
                    foreach (var id in l.Ammo)   // (gr.Ammo is filled when the group row is built, later)
                    {
                        int ix = l.Ammo.IndexOf(id); if (ix < 0 || ix >= l.AmmoDom.Length) continue;
                        int d = l.AmmoDom[ix];
                        bool missile = R.S(R.F(AmmoParams(v, id), "_type")) == "Missile";
                        Action<List<int>, int> add = (lst, k) => { if (!lst.Contains(k)) lst.Add(k); };
                        if ((d & DAir) != 0)
                        {
                            add(t.Always, 0);
                            if (missile) { if ((d & DSur) != 0) add(t.SamExtra, 1); if ((d & DLnd) != 0) add(t.SamExtra, 2); if ((d & DSub) != 0) add(t.SamExtra, 3); }
                        }
                        else if (!missile) { if ((d & DSub) != 0) add(t.Always, 3); else if ((d & DSur) != 0) add(t.Always, 1); else if ((d & DLnd) != 0) add(t.Always, 2); }
                        else { if ((d & DSur) != 0) add(t.Always, 1); if ((d & DLnd) != 0) add(t.Always, 2); if ((d & DSub) != 0) add(t.Always, 3); }
                    }
                }
            t.Tb = T(TitleText(t), WC.Sub, 11.5f, FontWeights.SemiBold);
            v.Titles.Add(t);
            return t;
        }

        static string TitleText(TitleUI t)
        {
            var title = Loc.T("section." + t.Section);
            var a = Active;
            var kinds = new List<int>(t.Always);
            if (a != null && a._secTargets != null && a._secTargets.Value) foreach (var k in t.SamExtra) if (!kinds.Contains(k)) kinds.Add(k);
            kinds.Remove(t.Section);
            kinds.Sort();
            foreach (var k in kinds) title += "/" + Loc.T("section." + k);
            return title;
        }

        static StockUI MakeStock(float size, Color c)
        {
            var u = new StockUI { P = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                                                       Background = WC.Clear } };
            u.Cur = T("", c, size); u.Max = T("", WC.Dim, size);
            var line = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            line.Children.Add(u.Cur); line.Children.Add(u.Max);
            u.P.Children.Add(line);
            u.RailLab = T("", WC.Dim, 10f); u.RailCur = T("", c, 10f); u.RailMax = T("", WC.Dim, 10f);
            u.Rail = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0f, 3f, 0f, 0f), Visibility = Visibility.Collapsed };
            u.Rail.Children.Add(u.RailLab); u.Rail.Children.Add(u.RailCur); u.Rail.Children.Add(u.RailMax);
            u.P.Children.Add(u.Rail);
            u.Cur.Tag = c;
            return u;
        }

        /// <summary>"current/max", the current coloured by what is left (more than half green, more than a quarter amber, else red); a single plain number for gun mounts holding fewer than 10.</summary>
        static void SetStock(StockUI u, int cur, int max, bool plain)
        {
            if ((plain && max < 10) || max <= 0) { SetText(u.Cur, Fmt(cur), u.Cur.Tag is Color ? (Color)u.Cur.Tag : WC.Text); if (u.Max.Text != "") u.Max.Text = ""; return; }
            if (cur > max) max = cur;
            var mx = "/" + Fmt(max);
            if (u.Max.Text != mx) u.Max.Text = mx;
            float r = cur / (float)max;
            SetText(u.Cur, Fmt(cur), r > 0.5f ? WC.Ok : r > 0.25f ? WC.Amber : WC.Red);
        }

        static bool IsGun(Launcher l) { var t = R.S(R.F(l.Ws, "_weaponType")); return t == "Gun" || t == "CIWS"; }

        /// <summary>The stock at the start: the magazines' initial counts (each once), the launcher's own rounds where it has no magazine.</summary>
        /// <summary>What a launcher holds at most: its own capacity; the game's launchers with none (Mk 141: _maxAmmoCount 0, no reload) hold what their rails do.</summary>
        static int Cap(object ws)
        {
            int n = R.I(R.F(R.F(ws, "_vwp"), "_maxAmmoCount"));
            if (n > 0) return n;
            try { return Rails(ws).Count; } catch { return 0; }
        }

        static int StockMax(List<Launcher> ls, string only = null)
        {
            int n = 0;
            var mags = new List<object>();
            foreach (var l in ls)
            {
                var ammo = only == null ? l.Ammo : l.Ammo.Contains(only) ? new List<string> { only } : new List<string>();
                if (ammo.Count == 0) continue;
                var vwp = R.F(l.Ws, "_vwp");
                var mag = R.F(vwp, "_associatedMagazine");
                if (mag == null) { n += Cap(l.Ws); continue; }
                if (mags.Contains(mag)) continue;
                mags.Add(mag);
                var init = R.F(mag, "_initialAmmoCount") as IDictionary;
                if (init != null) foreach (DictionaryEntry kv in init) if (ammo.Contains(R.S(R.F(R.F(kv.Key, "_ap"), "_ammunitionFileName")))) n += R.I(kv.Value);
            }
            return n;
        }

        /// <summary>A target as the player knows it: "Track N" (and its name when identified).</summary>
        static string TrackLabel(object target)
        {
            if (target == null) return null;
            try
            {
                if (R.B(R.P(target, "IsPlayerObject")))
                {
                    // an own unit: its own track number (as the game's messages give it) and name
                    string own = null;
                    try { own = R.S(R.Call(target, "GetMapContactID")); } catch { }
                    var nm = R.S(R.P(target, "DisplayNameShort"));
                    return string.IsNullOrEmpty(own) ? nm : Loc.F("target.track", own, nm);
                }
                var globals = GameAssemblyStatic().GetType("SeaPower.Globals");
                var tf = globals.GetField("_playerTaskforce", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).GetValue(null);
                var veh = R.Call(target, "GetMapVehicle", tf);
                if (veh == null) return Loc.T("target.unknown");
                var id = R.S(R.P(veh, "Id"));
                return R.B(R.Rv(R.P(veh, "Identified"))) ? "Track " + id + " (" + R.S(R.Rv(R.P(target, "Name"))) + ")" : "Track " + id;
            }
            catch { return null; }
        }

        /// <summary>The game's status of a launcher, and its target while it engages one.</summary>
        static string StatusText(object ws, int c)
        {
            string st, tgt;
            StatusParts(ws, c, out st, out tgt);
            return string.IsNullOrEmpty(tgt) ? st : st + " → " + tgt;
        }

        /// <summary>The status ("Reloading: mm:ss" - the game's English text has no word before the time) and the target
        /// (engaging, aiming, reloading between shots of a salvo; null without one).</summary>
        static void StatusParts(object ws, int c, out string st, out string tgt, Launcher l = null)
        {
            st = null; tgt = null;
            if (c == 5)
            {
                // a task of this launcher waits: for the launcher, or for a fire-control channel
                var pw = PinWait(ws);
                st = pw.Wait == 1 ? Loc.T("st.queued") : Loc.T("st.waitFcr");
                tgt = TrackLabel(pw.Target);
                return;
            }
            try { st = R.S(R.Call(ws, "GetStatus")); } catch { }
            st = string.IsNullOrEmpty(st) ? CatName(c) : st;
            if (st.IndexOf("/10000)", StringComparison.Ordinal) > 0) st = System.Text.RegularExpressions.Regex.Replace(st, @"\((\d+)/10000\)", "($1)");   // the game's default capacity of a mount that has none of its own
            if (c == 2 && st.Length > 0 && !char.IsLetter(st[0])) st = Loc.F("st.reloading", st);
            if (c == 0 || c == 2) { var sx = SamStatus(ws); if (sx != null) st = sx; }
            // not a gun, nothing left (rails and magazine): the game keeps saying "Ready" / "Reloading" for such a launcher
            if (c == 4 && l != null && !R.B(R.F(ws, "_isOffline")) && !R.B(R.F(ws, "_isEmpty")) && NoAmmo(l)) st = Loc.T("st.empty");   // the game says "Ready" for a launcher whose rails are empty (it loads them at the next order)
            if (c != 3 && c != 4)
            {
                var t = R.F(ws, "_targetObject");
                bool busy = R.B(R.F(ws, "_executingEngageTask")) || R.B(R.F(ws, "_isAutoEngaging")) || R.F(ws, "_takenEngageTask") != null;
                if (t == null && R.Is(ws, _tCiws) && R.B(R.F(ws, "_isAutoEngaging"))) t = R.F(ws, "_currentClosestTarget");
                if (t != null && busy)
                {
                    tgt = TrackLabel(t);
                }
            }
        }

        /// <summary>The text with its timer ("mm:ss") taken out, and the timer in seconds (-1: none).</summary>
        static string NoTimer(string s, out float sec)
        {
            sec = -1f;
            if (string.IsNullOrEmpty(s)) return s;
            var m = System.Text.RegularExpressions.Regex.Match(s, @"(\d+):(\d\d)");
            if (!m.Success) return s;
            sec = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 60 + int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            return s.Substring(0, m.Index) + "#" + s.Substring(m.Index + m.Length);
        }

        /// <summary>"Track 1004 (Name)" → "1004".</summary>
        static string TrackNumber(string tl)
        {
            if (string.IsNullOrEmpty(tl) || !tl.StartsWith("Track ", StringComparison.Ordinal)) return tl;
            var r = tl.Substring(6);
            int sp = r.IndexOf(' ');
            return sp > 0 ? r.Substring(0, sp) : r;
        }

        /// <summary>The status with its target when that fits the text's place in the given lines; else the status alone.</summary>
        static string Shown(TextBlock t, string st, string tgt, int lines)
        {
            if (string.IsNullOrEmpty(tgt)) return st;
            var full = st + " → " + tgt;
            float w = t.MaxWidth > 0f && !float.IsInfinity(t.MaxWidth) ? t.MaxWidth : t.ActualWidth;   // centred texts: the room they may take
            if (w <= 0f) return full;
            // the whole target, else its number only ("Track 1004 (Name)" → "1004"), else none
            var shortT = TrackNumber(tgt);
            var cands = shortT != tgt ? new[] { full, st + " → " + shortT } : new[] { full };
            try
            {
                var one = new TextBlock { Text = "Xg", FontSize = t.FontSize, FontWeight = t.FontWeight, FontFamily = t.FontFamily };
                one.Measure(new Size(float.PositiveInfinity, float.PositiveInfinity));
                foreach (var c in cands)
                {
                    var m = new TextBlock { Text = c, FontSize = t.FontSize, FontWeight = t.FontWeight, FontFamily = t.FontFamily, TextWrapping = lines > 1 ? TextWrapping.Wrap : TextWrapping.NoWrap };
                    m.Measure(new Size(lines > 1 ? w : float.PositiveInfinity, float.PositiveInfinity));
                    bool fits = lines > 1 ? m.DesiredSize.Height <= one.DesiredSize.Height * lines + 0.5f : m.DesiredSize.Width <= w + 0.5f;
                    if (fits) return c;
                }
                return st;
            }
            catch { return full; }
        }

        // ------------------------------------------------------------------ the director a mount fires with
        // While a mount has a target: a gun - the director the game aims it with (WeaponSystemGun.GetFireControlSensor, with
        // GunChannelLimit only one holding the target); a CIWS - FireControl.ResolveBestDirector; a launcher - the director
        // holding its target. A gun / CIWS with directors but none working: "local control". Shown at the weapon's name:
        // the director in blue, local control in orange (amber).

        static Type _tFireControl;

        /// <summary>(name, local) of the director a busy mount fires with; null when it has no target or no director.</summary>
        static KeyValuePair<string, bool>? FcOf(object ws)
        {
            try
            {
                var tgt = R.F(ws, "_targetObject");
                bool busy = R.B(R.F(ws, "_executingEngageTask")) || R.B(R.F(ws, "_isAutoEngaging")) || R.F(ws, "_takenEngageTask") != null;
                if (tgt == null && R.Is(ws, _tCiws) && R.B(R.F(ws, "_isAutoEngaging"))) tgt = R.F(ws, "_currentClosestTarget");
                if (tgt == null || !busy) return null;
                var sens = R.F(R.F(ws, "_vwp"), "_associatedSensors") as IList;
                if (sens == null || sens.Count == 0) return null;
                var wt = R.S(R.F(ws, "_weaponType"));
                object dir = null;
                if (wt == "Gun" || wt == "CIWS")
                {
                    if (LocalControl(ws)) return new KeyValuePair<string, bool>(Loc.T("st.localControl"), true);
                    if (wt == "Gun") dir = R.Call(ws, "GetFireControlSensor");
                    else
                    {
                        if (_tFireControl == null) _tFireControl = GameAssemblyStatic().GetType("SeaPower.FireControl");
                        var m = _tFireControl == null ? null : _tFireControl.GetMethod("ResolveBestDirector", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                        if (m != null) dir = m.Invoke(null, new object[] { sens, true, false });
                    }
                }
                else foreach (var s in sens) if (s != null && Holds(s, tgt)) { dir = s; break; }
                if (dir == null) return null;
                return new KeyValuePair<string, bool>(Cap(R.S(R.Rv(R.P(dir, "DisplayName"))) ?? R.S(R.F(dir, "_systemName")) ?? "director"), false);
            }
            catch { return null; }
        }

        /// <summary>The mark at a weapon's name: directors in blue, local control in amber; rebuilt only when it changes.</summary>
        static void SetFc(TextBlock t, ref string sig, List<KeyValuePair<string, bool>> parts)
        {
            var sb = new StringBuilder();
            foreach (var p in parts) sb.Append(p.Value ? "!" : "").Append(p.Key).Append('|');
            var ns = sb.ToString();
            if (ns == sig) return;
            sig = ns;
            t.Inlines.Clear();
            for (int i = 0; i < parts.Count; i++)
            {
                if (i > 0) t.Inlines.Add(new Run(", ") { Foreground = WC.B(WC.Dim) });
                t.Inlines.Add(new Run(parts[i].Key) { Foreground = WC.B(parts[i].Value ? WC.Amber : WC.Engage) });
            }
        }

        /// <summary>A gun / CIWS with directors, none of them working (on, intact, giving contacts) - or, with GunChannelLimit,
        /// none holding the gun's target: it aims on local control (Manual, solution gain Globals._gunLocalControlGainRate,
        /// error × darkness).</summary>
        static bool LocalControl(object ws)
        {
            var wt = R.S(R.F(ws, "_weaponType"));
            if (wt != "Gun" && wt != "CIWS") return false;
            var sens = R.F(R.F(ws, "_vwp"), "_associatedSensors") as IList;
            if (sens == null) return false;
            bool any = false;
            foreach (var x in sens)
            {
                if (x == null) continue;
                any = true;
                if (Working(x) && (wt != "Gun" || !GunChannelsOn() || Holds(x, R.F(ws, "_targetObject")))) return false;
            }
            return any;
        }

        static string Fmt(int n) { return n.ToString("#,0", CultureInfo.InvariantCulture).Replace(",", " "); }

        void Refresh(WeaponsView v, bool structure)
        {
            var unit = v.Unit;
            var w = W(unit);
            SyncDoctrine(unit, w);
            foreach (var tt in v.Titles) { var txt = TitleText(tt); if (tt.Tb.Text != txt) tt.Tb.Text = txt; }
            // header
            var ws = R.S(R.F(unit, "_weaponStatus"));
            string[] sn = { "Free", "Tight", "Hold" };
            for (int i = 0; i < 3; i++)
            {
                bool on = ws == sn[i];
                v.Status[i].Background = on ? WC.B(WC.Sel) : WC.Clear;
                v.StatusText[i].Foreground = WC.B(on ? Color.FromRgb(255, 255, 255) : WC.Dim);
                v.StatusText[i].FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            }
            var tight = R.P(R.P(unit, "TacticalDoctrine"), "ShipsOnWeaponsTightEngageHostileAircraft");
            bool vessel = R.Is(unit, _tVessel) && tight != null;
            v.TightRow.Visibility = vessel ? Visibility.Visible : Visibility.Collapsed;
            v.FreeRow.Visibility = R.Is(unit, _tVessel) ? Visibility.Visible : Visibility.Collapsed;
            if (R.Is(unit, _tVessel))
            {
                bool fa = FreeAirOn(unit);
                v.FreeMark.Visibility = fa ? Visibility.Visible : Visibility.Hidden;
                v.FreeBox.BorderBrush = WC.B(fa ? Color.FromRgb(240, 96, 40) : WC.Ring);
            }
            if (vessel)
            {
                bool t = R.B(R.P(tight, "ResolvedValue"));
                v.TightMark.Visibility = t ? Visibility.Visible : Visibility.Hidden;
                v.TightBox.Background = WC.Clear;
                v.TightBox.BorderBrush = WC.B(t ? WC.Ok : WC.Ring);
            }
            v.ExpandText.Text = v.Groups.Exists(g => g.Expandable && !g.Open) ? Loc.T("tab.expandAll") : Loc.T("tab.collapseAll");
            RefreshCtrls(v);
            foreach (var sc in v.SecCtls)
            {
                // Fire uses the ammunition chosen as active in the groups (if it is a common one), else the first common
                string act = null;
                foreach (var g in sc.Groups) if (g.ActiveAmmo != null && sc.Common.Contains(g.ActiveAmmo)) { act = g.ActiveAmmo; break; }
                if (act != null && sc.Common[0] != act) { sc.Common.Remove(act); sc.Common.Insert(0, act); }
                Tip(sc.Fire.Main, Loc.T("btn.fire") + ": " + AmmoName(AmmoParams(v, sc.Common[0]), sc.Common[0]));
                for (int i = 0; i < 5; i++)
                {
                    Chip st; SecPairs(v.Unit, sc, Domains[i], null, out st);
                    Paint(sc.Chips[i], st);
                    Tip(sc.Chips[i].G, st == AwcPlugin.Chip.None ? null : Loc.F("tip.switchesSection", GroupTip(v.Unit, sc.Tmp, Domains[i], st)));
                }
            }

            foreach (var gr in v.Groups)
            {
                bool open = gr.Open && gr.Expandable;
                var rot = open ? WC.Rot90 : null; if (!ReferenceEquals(gr.Chevron.RenderTransform, rot)) gr.Chevron.RenderTransform = rot;
                if (open && !gr.DetailBuilt) BuildDetail(v, gr);
                gr.Detail.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                // open with a row per launcher: those rows show the states - the group's own would repeat them
                var stVis = open && gr.Ls.Count > 1 ? Visibility.Hidden : Visibility.Visible;
                if (gr.States.Visibility != stVis) gr.States.Visibility = stVis;
                RefreshStates(gr);
                if (gr.Chan != null) RefreshChannels(v, gr);
                {
                    var parts = new List<KeyValuePair<string, bool>>();
                    foreach (var l in gr.Ls) { var f = FcOf(l.Ws); if (f != null && !parts.Exists(x => x.Key == f.Value.Key)) parts.Add(f.Value); }
                    SetFc(gr.Fc, ref gr.FcSig, parts);
                }
                bool plain = gr.Ls.TrueForAll(IsGun);
                SetStock(gr.Stock, Stock(gr.Ls), StockMax(gr.Ls), plain);
                {
                    // one launcher: what is on its rails (the stock above is all that the launcher holds, the magazine behind it included)
                    var rails = gr.Ls.Count == 1 && !plain ? Rails(gr.Ls[0].Ws) : null;
                    int on = 0;
                    if (rails != null) foreach (var x in rails) if (x) on++;
                    // not when it only repeats the stock above (Mk 141: all it holds is on the rails)
                    if (rails != null && rails.Count > 0 && rails.Count < 10000 && !(on == Stock(gr.Ls) && rails.Count == StockMax(gr.Ls)))
                    {
                        // "On launcher " + current (coloured by what is left, as the stock above) + "/max"
                        var rt = Loc.T("stock.rail"); int p0 = rt.IndexOf("{0}"), p1 = rt.IndexOf("{1}");
                        string pre = p0 > 0 ? rt.Substring(0, p0) : "", mid = p0 >= 0 && p1 > p0 ? rt.Substring(p0 + 3, p1 - p0 - 3) : "/", post = p1 >= 0 ? rt.Substring(p1 + 3) : "";
                        float rr = on / (float)rails.Count;
                        SetText(gr.Stock.RailLab, pre, WC.Dim);
                        SetText(gr.Stock.RailCur, Fmt(on), rr > 0.5f ? WC.Ok : rr > 0.25f ? WC.Amber : WC.Red);
                        SetText(gr.Stock.RailMax, mid + Fmt(rails.Count) + post, WC.Dim);
                        if (gr.Stock.Rail.Visibility != Visibility.Visible) gr.Stock.Rail.Visibility = Visibility.Visible;
                    }
                    else if (gr.Stock.Rail.Visibility != Visibility.Collapsed) gr.Stock.Rail.Visibility = Visibility.Collapsed;
                }
                for (int i = 0; i < 5; i++)
                {
                    var d = Domains[i];
                    Chip st = AwcPlugin.Chip.None;
                    foreach (var l in gr.Ls)
                    {
                        var s = State(unit, l, d);
                        if (s == AwcPlugin.Chip.None) continue;
                        st = st == AwcPlugin.Chip.None || st == s ? s : AwcPlugin.Chip.Mixed;
                    }
                    Paint(gr.Chips[i], st);
                    Tip(gr.Chips[i].G, st == AwcPlugin.Chip.None ? null : GroupTip(unit, gr, d, st));
                    foreach (var ac in gr.AmmoChips)
                    {
                        var ast = (ac.Dom & d) != 0 ? st : AwcPlugin.Chip.None;
                        Paint(ac.Chips[i], ast);
                        Tip(ac.Chips[i].G, ast == AwcPlugin.Chip.None ? null : Loc.F("tip.switchesGroup", GroupTip(unit, gr, d, ast)));
                    }
                }
                foreach (var m in gr.Members)
                {
                    int c = Cat(m.L.Ws, m.L);
                    string mst, mtgt;
                    StatusParts(m.L.Ws, c, out mst, out mtgt, m.L);
                    var full = string.IsNullOrEmpty(mtgt) ? mst : mst + " → " + mtgt;
                    var s = Shown(m.State, mst, mtgt, 1);
                    SetText(m.State, s, CatColor(c));
                    Tip(m.State, s != full ? full : null);
                    SetFill(m.Dot, CatColor(c));
                    var mf = FcOf(m.L.Ws);
                    SetFc(m.Fc, ref m.FcSig, mf == null ? new List<KeyValuePair<string, bool>>() : new List<KeyValuePair<string, bool>> { mf.Value });
                    {
                        int mmax = Cap(m.L.Ws);
                        var one = new List<Launcher> { m.L };
                        // the game's default capacity of 10000 (a mount that has none of its own, e.g. an aircraft's guns): the magazine's
                        // a mount with a magazine shows the magazine's rest, not the rounds in the mount (a gun: always; another launcher: unless
                        // the magazine is shared with the group's other launchers - then only its own rounds can be told)
                        var mgz = R.F(R.F(m.L.Ws, "_vwp"), "_associatedMagazine");
                        bool own = mgz != null && (IsGun(m.L) || !gr.Members.Exists(o => o != m && R.F(R.F(o.L.Ws, "_vwp"), "_associatedMagazine") == mgz));
                        if (mgz != null && (mmax >= 10000 || own)) SetStock(m.Stock, Stock(one), StockMax(one), IsGun(m.L));
                        else SetStock(m.Stock, Loaded(m.L.Ws, m.L.Ammo), mmax, IsGun(m.L));
                    }
                    for (int i = 0; i < 5; i++)
                    {
                        var st = State(unit, m.L, Domains[i]);
                        Paint(m.Chips[i], st);
                        Tip(m.Chips[i].G, st == AwcPlugin.Chip.None ? null : ChipTip(unit, m.L, Domains[i]));
                    }
                }
            }
        }

        /// <summary>The group's radar directors that guide (or aim) its ammunition: they limit the targets and guided weapons at a time.</summary>
        void Directors(WeaponsView v, GroupRow gr)
        {
            var aps = new List<object>();
            foreach (var id in gr.Ammo)
            {
                var ap = AmmoParams(v, id);
                if (ap == null) continue;
                bool guided = R.B(R.F(ap, "_requiresGuidance"));
                if (guided) gr.GuidedAmmo = true;
                if (guided || R.S(R.F(ap, "_type")) == "Projectile") aps.Add(ap);
                if (R.B(R.P(R.F(ap, "_midCourseCorrection"), "IsWireGuided")) && R.I(R.F(v.Unit, "_maxWireCount")) < int.MaxValue) gr.Wires = true;
            }
            foreach (var l in gr.Ls)
            {
                var sens = R.F(R.F(l.Ws, "_vwp"), "_associatedSensors") as IList;
                if (sens == null) continue;
                foreach (var s in sens)
                {
                    if (s == null || gr.Dirs.Contains(s) || !R.Is(s, _tRadar)) continue;
                    foreach (var ap in aps) { bool ok = false; try { ok = R.B(R.Call(s, "CanGuideAmmo", ap)); } catch { } if (ok) { gr.Dirs.Add(s); break; } }
                }
            }
        }

        /// <summary>"T used/total · W used/total" over the group's directors (amber when one is full); wires for wire-guided torpedoes.</summary>
        static void RefreshChannels(WeaponsView v, GroupRow gr)
        {
            string text, tip;
            bool full;
            if (gr.Dirs.Count > 0)
            {
                int tw = 0, uw = 0, tt = 0, ut = 0;
                var sb = new StringBuilder(Loc.T("chan.head"));
                foreach (var s in gr.Dirs)
                {
                    int w = R.I(R.P(s, "EffectiveWeaponChannels")), w0 = Count(R.F(s, "_associatedWeapons"));
                    int t = R.I(R.F(s, "_targetChannels")), t0 = Count(R.F(s, "_associatedTargets"));
                    tw += w; uw += w0; tt += t; ut += t0;
                    bool on = R.B(R.Rv(R.P(s, "IsOn"))), bad = R.B(R.Rv(R.P(s, "Inoperable")));
                    sb.Append("\n").Append(Cap(R.S(R.Rv(R.P(s, "DisplayName"))) ?? R.S(R.F(s, "_systemName")) ?? "?"))
                      .Append(bad ? Loc.T("chan.damaged") : on ? "" : Loc.T("chan.off")).Append(Loc.F("chan.targets", t0, t));
                    if (gr.GuidedAmmo) sb.Append(Loc.F("chan.guidedWeapons", w0, w));
                }
                text = Loc.F("chan.tracked", ut, tt) + (gr.GuidedAmmo ? Loc.F("chan.guided", uw, tw) : "");
                full = ut >= tt || (gr.GuidedAmmo && uw >= tw);
                sb.Append(Loc.T("chan.foot"));
                tip = sb.ToString();
            }
            else
            {
                int used = 0;
                try { used = R.I(R.Call(v.Unit, "WiresInUse")); } catch { }
                int max = R.I(R.F(v.Unit, "_maxWireCount"));
                text = Loc.F("chan.wires", used, max);
                full = used >= max;
                tip = Loc.T("chan.wiresTip");
            }
            if (gr.Chan.Text != text) gr.Chan.Text = text;
            gr.Chan.Foreground = WC.B(full ? WC.Amber : WC.Sub);
            gr.ChanBox.BorderBrush = WC.B(full ? WC.Amber : Color.FromRgb(74, 82, 96));
            if (gr.Dirs.Count == 0) { if (tip != gr.ChanTip) { gr.ChanTip = tip; SetTip(gr.ChanBox, tip); } }
            else if (v.DirCard != null && v.DirFor == gr) RefreshDirLines(v);
        }

        // ------------------------------------------------------------------ the fire-control directors window
        // Over a group's T/W badge: a small window (in the tab's overlay layer) with each director - its name (a link to its
        // reference in the game's encyclopedia: SensorMuseumEntries by system name), state, channels in use, and its main
        // figures from the encyclopedia (type, fire-control grade and solution gain, ranges, band). It stays while the mouse is
        // over the badge or the window, so the links can be clicked.

        /// <summary>Each frame: open after a short hover on a badge, close a moment after the mouse left both badge and window.</summary>
        void DirWindowTick(WeaponsView v, float now)
        {
            if (v.Overlay == null) return;
            var h = v.DirHover;
            if (h != null && h.ChanBox != null && h.ChanBox.IsMouseOver && now >= v.DirOpenAt && v.DirFor != h) ShowDirs(v, h);
            if (v.DirCard == null) return;
            bool over = v.DirCard.IsMouseOver || (v.DirFor != null && v.DirFor.ChanBox != null && v.DirFor.ChanBox.IsMouseOver);
            if (over) { v.DirCloseAt = -1f; return; }
            if (v.DirCloseAt < 0f) { v.DirCloseAt = now + 0.35f; return; }
            if (now >= v.DirCloseAt) HideDirs(v);
        }

        static void HideDirs(WeaponsView v)
        {
            if (v.DirCard != null) v.Overlay.Children.Remove(v.DirCard);
            v.DirCard = null; v.DirFor = null; v.DirHover = null; v.DirCloseAt = -1f;
            v.DirLines.Clear();
        }

        void ShowDirs(WeaponsView v, GroupRow gr)
        {
            HideDirs(v);
            var sp = new StackPanel();
            sp.Children.Add(T(Loc.T("dir.title"), WC.Dim, 10.5f));
            foreach (var s in gr.Dirs)
            {
                var box = new StackPanel { Margin = new Thickness(0f, 6f, 0f, 0f) };
                var name = Cap(R.S(R.Rv(R.P(s, "DisplayName"))) ?? R.S(R.F(s, "_systemName")) ?? "?");
                var entry = SensorEntry(s);
                var head = new StackPanel { Orientation = Orientation.Horizontal };
                var dirSys = s;
                head.Children.Add(entry != null ? (UIElement)DescLink(name, R.S(R.P(entry, "Description")), Loc.T("tip.refDirector"), () => OpenEntry(entry))
                                                : T(name, WC.Text, 12.5f, FontWeights.SemiBold));
                var state = T("", WC.Ok, 11.5f); state.Margin = new Thickness(8f, 0f, 0f, 0f);
                head.Children.Add(state);
                box.Children.Add(head);
                var chan = T("", WC.Sub, 11.5f);
                box.Children.Add(chan);
                var facts = DirFacts(s, entry);
                if (facts.Length > 0)
                {
                    var ft = T(facts, WC.Dim, 11f); ft.TextWrapping = TextWrapping.Wrap; ft.MaxWidth = 380f;
                    box.Children.Add(ft);
                }
                v.DirLines.Add(new KeyValuePair<object, TextBlock[]>(s, new[] { state, chan }));
                sp.Children.Add(box);
            }
            var foot = T(Loc.T("dir.foot"), WC.Dim2, 10.5f);
            foot.Margin = new Thickness(0f, 8f, 0f, 0f);
            sp.Children.Add(foot);
            v.DirCard = new Border { Child = sp, Background = WC.B(Color.FromRgb(24, 27, 31)), BorderBrush = WC.B(WC.BtnLine), BorderThickness = new Thickness(1f),
                                     CornerRadius = new CornerRadius(3f), Padding = new Thickness(10f, 8f, 10f, 8f) };
            v.DirFor = gr; v.DirHover = null; v.DirCloseAt = -1f;
            RefreshDirLines(v);
            // under the badge (above it when there is no room), kept inside the tab
            try
            {
                v.DirCard.Measure(new Size(float.PositiveInfinity, float.PositiveInfinity));
                float w = v.DirCard.DesiredSize.Width, h = v.DirCard.DesiredSize.Height;
                var at = gr.ChanBox.TranslatePoint(new Point(0f, 0f), v.Overlay);
                float x = at.X, y = at.Y + gr.ChanBox.ActualHeight + 2f;
                if (x + w > v.Overlay.ActualWidth - 4f) x = Math.Max(4f, v.Overlay.ActualWidth - 4f - w);
                if (y + h > v.Overlay.ActualHeight - 4f) y = Math.Max(4f, at.Y - 2f - h);
                Canvas.SetLeft(v.DirCard, x); Canvas.SetTop(v.DirCard, y);
            }
            catch (Exception e) { Trace("directors window: " + e.Message); }
            v.Overlay.Children.Add(v.DirCard);
        }

        /// <summary>State and channels of each director in the window (live).</summary>
        static void RefreshDirLines(WeaponsView v)
        {
            bool guided = v.DirFor != null && v.DirFor.GuidedAmmo;
            foreach (var kv in v.DirLines)
            {
                var s = kv.Key;
                bool on = R.B(R.Rv(R.P(s, "IsOn"))), bad = R.B(R.Rv(R.P(s, "Inoperable")));
                SetText(kv.Value[0], bad ? Loc.T("dir.damaged") : on ? Loc.T("dir.on") : Loc.T("dir.off"), bad ? WC.Red : on ? WC.Ok : WC.Amber);
                int w = R.I(R.P(s, "EffectiveWeaponChannels")), w0 = Count(R.F(s, "_associatedWeapons"));
                int t = R.I(R.F(s, "_targetChannels")), t0 = Count(R.F(s, "_associatedTargets"));
                bool full = t0 >= t || (guided && w0 >= w);
                SetText(kv.Value[1], Loc.F("dir.targets", t0, t) + (guided ? Loc.F("dir.guided", w0, w) : ""), full ? WC.Amber : WC.Sub);
            }
        }

        /// <summary>The director's main figures: type, fire-control grade and solution gain, the encyclopedia's facts (ranges, band...).</summary>
        static string DirFacts(object s, object entry)
        {
            var parts = new List<string>();
            if (entry != null)
            {
                var ty = R.S(R.P(entry, "TypeText")); if (!string.IsNullOrEmpty(ty)) parts.Add(ty);
                var gr = R.S(R.P(entry, "FireControlGradeText")); if (!string.IsNullOrEmpty(gr)) parts.Add(Loc.F("spec.grade", gr));
                var facts = R.P(entry, "DetailFacts") as IEnumerable;
                if (facts != null)
                    foreach (var f in facts)
                    {
                        var lab = R.S(R.P(f, "Label")); var val = R.S(R.P(f, "Value"));
                        if (string.IsNullOrEmpty(val) || (lab != null && lab.IndexOf("hannel", StringComparison.OrdinalIgnoreCase) >= 0)) continue;   // channels: shown live above
                        parts.Add(string.IsNullOrEmpty(lab) ? val : lab.TrimEnd(':') + " " + val);
                    }
            }
            float gain = R.Fl(R.F(s, "_fcSolutionGainRate"));
            if (gain > 0f) parts.Add(Loc.F("spec.gain", gain.ToString("0.0#", CultureInfo.InvariantCulture)));
            return string.Join(" · ", parts.ToArray());
        }

        static object SensorEntry(object s)
        {
            try
            {
                var edm = GameAssemblyStatic().GetType("SeaPower.EncyclopedicDataManager");
                var inst = edm.GetProperty("Instance", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy).GetValue(null, null);
                var sys = R.S(R.F(s, "_systemName"));
                var list = R.P(inst, "SensorMuseumEntries") as IEnumerable;
                if (list != null && !string.IsNullOrEmpty(sys)) foreach (var e in list) if (R.S(R.P(e, "IniRef")) == sys) return e;
            }
            catch (Exception e) { Trace("director reference: " + e.Message); }
            return null;
        }

        /// <summary>Opens a museum entry as the Unit Reference opens it.</summary>
        static void OpenEntry(object entry)
        {
            try
            {
                var asm = GameAssemblyStatic();
                var mgv = asm.GetType("SeapowerUI.MainGameView").GetField("Instance").GetValue(null);
                var vm = Activator.CreateInstance(asm.GetType("SeapowerUI.ViewModels.ObjectInformationViewModel"), mgv, entry);
                var cmd = asm.GetType("SeapowerUI.WindowHostCommands").GetProperty("OpenWindowCommand").GetValue(null, null);
                R.Call(cmd, "Execute", vm, mgv);
            }
            catch (Exception e) { Trace("open reference: " + (e.InnerException ?? e).Message); }
        }


        /// <summary>The STATE cell of a group row, built once and updated in place (rebuilding it every refresh makes its tooltips flicker):
        /// one launcher - its state; two - a line each; more - counts by state, on two lines when there are several kinds.</summary>
        static void RefreshStates(GroupRow gr)
        {
            int n = gr.Ls.Count;
            if (!gr.StatesBuilt)
            {
                gr.StatesBuilt = true;
                // labels of the launchers for the tooltip: place (or name), numbered where they repeat
                gr.SLabel = new string[n];
                for (int i = 0; i < n; i++) gr.SLabel[i] = Cap(string.IsNullOrEmpty(gr.Ls[i].Loc) ? gr.Ls[i].Name : gr.Ls[i].Loc);
                for (int i = 0; i < n; i++)
                {
                    int dup = 0, nr = 0;
                    for (int j = 0; j < n; j++) if (gr.SLabel[j] == gr.SLabel[i] || gr.SLabel[j].StartsWith(gr.SLabel[i] + " #", StringComparison.Ordinal)) { dup++; if (j < i) nr++; }
                    if (dup > 1) gr.SLabel[i] += " #" + (nr + 1);
                }
                // one line (wraps to two): one launcher, or all of them with the same state
                gr.SOneDot = new Ellipse { Width = 7f, Height = 7f, Margin = new Thickness(0f, 0f, 5f, 0f), VerticalAlignment = VerticalAlignment.Center };
                gr.SOneText = T("", WC.Ok, 12f); gr.SOneText.TextWrapping = TextWrapping.Wrap;
                gr.SOne = Centred(gr.SOneDot, gr.SOneText);
                FitWidth(gr.States, gr.SOneText);
                Grid.SetColumnSpan(gr.SOne, 2);
                gr.States.Children.Add(gr.SOne);
                if (n == 2)
                {
                    gr.SDot = new Ellipse[n]; gr.SText = new TextBlock[n];
                    var sp = new StackPanel();
                    for (int i = 0; i < n; i++)
                    {
                        gr.SDot[i] = new Ellipse { Width = 7f, Height = 7f, Margin = new Thickness(0f, 0f, 5f, 0f), VerticalAlignment = VerticalAlignment.Center };
                        gr.SText[i] = T("", WC.Ok, 12f);
                        gr.SText[i].TextTrimming = TextTrimming.CharacterEllipsis;
                        FitWidth(gr.States, gr.SText[i]);
                        sp.Children.Add(Centred(gr.SDot[i], gr.SText[i]));
                    }
                    Grid.SetColumnSpan(sp, 2);
                    gr.States.Children.Add(sp);
                    gr.SMulti = sp;
                }
                else if (n > 2)
                {
                    // a pair (dot + "N state") per kind on each of the two lines; only the shown ones are visible
                    gr.SDot = new Ellipse[2 * NCat]; gr.SText = new TextBlock[2 * NCat]; gr.SPair = new StackPanel[2 * NCat];
                    var sp = new StackPanel();
                    for (int ln = 0; ln < 2; ln++)
                    {
                        var line = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
                        for (int c = 0; c < NCat; c++)
                        {
                            int i = ln * NCat + c;
                            gr.SDot[i] = new Ellipse { Width = 7f, Height = 7f, Fill = WC.B(CatColor(c)), Margin = new Thickness(0f, 0f, 5f, 0f), VerticalAlignment = VerticalAlignment.Center };
                            gr.SText[i] = T("", CatColor(c), 12f); gr.SText[i].Margin = new Thickness(0f, 0f, 8f, 0f);
                            gr.SPair[i] = new StackPanel { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed };
                            gr.SPair[i].Children.Add(gr.SDot[i]); gr.SPair[i].Children.Add(gr.SText[i]);
                            line.Children.Add(gr.SPair[i]);
                        }
                        sp.Children.Add(line);
                    }
                    Grid.SetColumnSpan(sp, 2);
                    gr.States.Children.Add(sp);
                    gr.SMulti = sp;
                }
            }
            // each launcher: category, state, target (whole and short); the same short text everywhere → one line for the group
            var cs = new int[n]; var sts = new string[n]; var tgts = new string[n];
            bool same = true;
            int lo = 0;   // the one shown when they are the same: the shortest timer
            float t0, tMin = float.MaxValue, tMax = float.MinValue;
            string n0 = null;
            for (int i = 0; i < n; i++)
            {
                cs[i] = Cat(gr.Ls[i].Ws, gr.Ls[i]);
                StatusParts(gr.Ls[i].Ws, cs[i], out sts[i], out tgts[i], gr.Ls[i]);
                float ti;
                var ni = NoTimer(sts[i], out ti);
                if (i == 0) { n0 = ni; t0 = ti; }
                else if (cs[i] != cs[0] || ni != n0 || TrackNumber(tgts[i]) != TrackNumber(tgts[0])) same = false;
                if (ti >= 0f) { if (ti < tMin) { tMin = ti; lo = i; } tMax = Math.Max(tMax, ti); }
            }
            // texts differing only in a timer ("Reloading: 00:12" / "00:14") are the same when the timers are less than 3 s apart
            if (same && tMax > float.MinValue && tMax - tMin >= 3f) same = false;
            var tipB = new StringBuilder();
            for (int i = 0; i < n; i++)
            {
                var whole = string.IsNullOrEmpty(tgts[i]) ? sts[i] : sts[i] + " → " + tgts[i];   // the tooltip: the whole target
                if (n == 1) tipB.Append(whole); else tipB.Append(i > 0 ? "\n" : "").Append(gr.SLabel[i]).Append(": ").Append(whole);
            }
            var tip0 = tipB.ToString();
            if (tip0 != gr.StateTip) { gr.StateTip = tip0; SetTip(gr.States, tip0); }
            var oneVis = same ? Visibility.Visible : Visibility.Collapsed;
            if (gr.SOne.Visibility != oneVis) gr.SOne.Visibility = oneVis;
            if (gr.SMulti != null)
            {
                var mVis = same ? Visibility.Collapsed : Visibility.Visible;
                if (gr.SMulti.Visibility != mVis) gr.SMulti.Visibility = mVis;
            }
            if (same)
            {
                SetText(gr.SOneText, Shown(gr.SOneText, sts[lo], TrackNumber(tgts[lo]), 2), CatColor(cs[lo]));
                SetFill(gr.SOneDot, CatColor(cs[0]));
                return;
            }
            if (n == 2)
            {
                for (int i = 0; i < 2; i++)
                {
                    SetText(gr.SText[i], Shown(gr.SText[i], sts[i], TrackNumber(tgts[i]), 1), CatColor(cs[i]));
                    SetFill(gr.SDot[i], CatColor(cs[i]));
                }
            }
            else
            {
                var cnt = new int[NCat];
                foreach (var c0 in cs) cnt[c0]++;
                int kinds = 0;
                for (int c = 0; c < NCat; c++) if (cnt[c] > 0) kinds++;
                int k = 0, first = (kinds + 1) / 2;
                for (int c = 0; c < NCat; c++)
                {
                    int ln = cnt[c] > 0 && k++ >= first ? 1 : 0;
                    for (int l2 = 0; l2 < 2; l2++)
                    {
                        int i = l2 * NCat + c;
                        bool show = cnt[c] > 0 && l2 == ln;
                        var vis = show ? Visibility.Visible : Visibility.Collapsed;
                        if (gr.SPair[i].Visibility != vis) gr.SPair[i].Visibility = vis;
                        if (show) SetText(gr.SText[i], cnt[c] + " " + CatName(c), CatColor(c));
                    }
                }
            }
        }

        /// <summary>A dot and its text side by side, at the left of the cell.</summary>
        static StackPanel Centred(Ellipse dot, TextBlock t)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
            sp.Children.Add(dot); sp.Children.Add(t);
            return sp;
        }

        /// <summary>A centred text may take the cell's width less its dot: it wraps / is trimmed there (MaxWidth follows the cell).</summary>
        static void FitWidth(FrameworkElement cell, TextBlock t)
        {
            cell.SizeChanged += (o, e) => { try { var w = Math.Max(20f, cell.ActualWidth - 14f); if (Math.Abs(t.MaxWidth - w) > 0.5f) t.MaxWidth = w; } catch { } };
        }

        /// <summary>Text and colour, set only when they change.</summary>
        static void SetText(TextBlock t, string s, Color c)
        {
            if (t.Text != s) t.Text = s;
            var b = t.Foreground as SolidColorBrush;
            if (b == null || b.Color != c) t.Foreground = WC.B(c);
        }

        static void SetFill(Shape e, Color c)
        {
            var b = e.Fill as SolidColorBrush;
            if (b == null || b.Color != c) e.Fill = WC.B(c);
        }

        static readonly ConditionalWeakTable<DependencyObject, string> TipCache = new ConditionalWeakTable<DependencyObject, string>();

        /// <summary>A tooltip set only when it changes (setting it again every refresh makes it flicker).</summary>
        static void Tip(DependencyObject e, string tip)
        {
            string last;
            if (TipCache.TryGetValue(e, out last))
            {
                if (last == tip) return;
                TipCache.Remove(e);
            }
            TipCache.Add(e, tip);
            SetTip(e, tip);
        }

        static void AddState(Panel sp, string text, Color c)
        {
            var dot = new Ellipse { Width = 7f, Height = 7f, Fill = WC.B(c), Margin = new Thickness(0f, 0f, 5f, 0f), VerticalAlignment = VerticalAlignment.Center };
            var t = T(text, c, 12f); t.Margin = new Thickness(0f, 0f, 8f, 0f);
            var pair = new StackPanel { Orientation = Orientation.Horizontal };
            pair.Children.Add(dot); pair.Children.Add(t);
            sp.Children.Add(pair);
        }

        static string GroupTip(object unit, GroupRow gr, int d, Chip st)
        {
            if (gr.Ls.Count == 1) return ChipTip(unit, gr.Ls[0], d);
            int di = Array.IndexOf(Domains, d);
            if (st != AwcPlugin.Chip.Mixed) return ChipTip(unit, gr.Ls[0], d, Loc.F("chip.allOf", gr.Ls.Count, Loc.T("noun." + Noun(gr.Ls[0]) + "s")));
            var sb = new StringBuilder(Loc.F("chip.mixed", Loc.T("dom." + di)));
            int k = 0;
            foreach (var l in gr.Ls)
            {
                var s = State(unit, l, d);
                if (s == AwcPlugin.Chip.None) continue;
                sb.Append(k++ > 0 ? ", " : "").Append(string.IsNullOrEmpty(l.Loc) ? l.Name : l.Loc).Append(": ")
                  .Append(s == AwcPlugin.Chip.On ? Loc.T("chip.mixedAuto") : s == AwcPlugin.Chip.Off ? Loc.T("chip.mixedOff") : Loc.T("chip.mixedOrder"));
            }
            sb.Append(Loc.T("chip.mixedEnd"));
            return sb.ToString();
        }

        // ------------------------------------------------------------------ actions

        void SetOpen(WeaponsView v, GroupRow gr, bool open)
        {
            gr.Open = open;
            OpenGroups.GetOrCreateValue(v.Unit)[gr.Key] = open;
            if (!ReferenceEquals(gr, null) && v.Fitting && open) v.Fitting = false;   // the player opened one: no more automatic closing
        }

        void ClickGroup(WeaponsView v, GroupRow gr, int d)
        {
            var pairs = new List<KeyValuePair<Launcher, int>>();
            bool anyOn = false;
            foreach (var l in gr.Ls)
            {
                var s = State(v.Unit, l, d);
                if (s == AwcPlugin.Chip.None) continue;
                pairs.Add(new KeyValuePair<Launcher, int>(l, d));
                if (s == AwcPlugin.Chip.On) anyOn = true;
            }
            if (pairs.Count == 0) return;
            Switch(v.Unit, pairs, !anyOn);
            Refresh(v, false);
        }

        void ClickOne(WeaponsView v, Launcher l, int d)
        {
            var s = State(v.Unit, l, d);
            if (s == AwcPlugin.Chip.None) return;
            Switch(v.Unit, new List<KeyValuePair<Launcher, int>> { new KeyValuePair<Launcher, int>(l, d) }, s != AwcPlugin.Chip.On);
            Refresh(v, false);
        }

        void SetWeaponStatus(WeaponsView v, int status)
        {
            if (_tWeaponStatus == null) return;
            if (status == 2) { try { R.Call(v.Unit, "CeaseFire", false); } catch (Exception e) { Trace("CeaseFire: " + e.Message); } }
            R.Call(v.Unit, "SetWeaponStatus", Enum.ToObject(_tWeaponStatus, status), true);
            Refresh(v, false);
        }

        void ToggleTightAir(WeaponsView v)
        {
            var it = R.P(R.P(v.Unit, "TacticalDoctrine"), "ShipsOnWeaponsTightEngageHostileAircraft");
            if (it == null) return;
            bool val = R.B(R.P(it, "ResolvedValue"));
            R.SetP(it, "IsSet", true);
            R.SetP(it, "Value", !val);
            try { R.Call(it, "Refresh"); } catch { }
            Refresh(v, false);
        }

        static object WeaponEntryFor(WeaponsView v, string id)
        {
            object e;
            if (v.Entries.TryGetValue(id, out e)) return e;
            var obp = R.F(v.Unit, "_obp");
            var g = (R.F(obp, "_launcherAmmunitionGroups") as IDictionary);
            object grp = g != null && g.Contains(id) ? g[id] : null;
            if (grp == null) { var gg = R.F(obp, "_gunAmmunitionGroups") as IDictionary; grp = gg != null && gg.Contains(id) ? gg[id] : null; }
            if (grp == null) return null;
            var t = GameAssemblyStatic().GetType("SeapowerUI.ViewModels.WeaponEntry");
            e = Activator.CreateInstance(t, v.Unit, grp, false);
            v.Entries[id] = e;
            return e;
        }

        void OpenWeaponReference(WeaponsView v, string id)
        {
            var e = WeaponEntryFor(v, id);
            var cmd = R.P(e, "OpenReferenceCommand");
            if (cmd != null) R.Call(cmd, "Execute", (object)null);
        }

        /// <summary>A reference link's tooltip: the encyclopedia's description (wrapped at 420 px), else the plain "Reference of ...".</summary>
        static object DescTip(string desc, string fallback)
        {
            if (string.IsNullOrEmpty(desc)) return TipOf(fallback);
            return new TextBlock { Text = desc.Replace("\\n", "\n").Trim(), TextWrapping = TextWrapping.Wrap, MaxWidth = 420f };
        }

        static TextBlock DescLink(string name, string desc, string fallback, Action a)
        {
            var t = Link(name, WC.Text, 12.5f, FontWeights.SemiBold, null, a);
            ToolTipService.SetToolTip(t, DescTip(desc, fallback));
            return t;
        }

        /// <summary>The museum entry of a launcher / gun (Gun / LauncherMuseumEntries by system name); null when there is none.</summary>
        static object SystemEntry(Launcher l)
        {
            try
            {
                var edm = GameAssemblyStatic().GetType("SeaPower.EncyclopedicDataManager");
                var inst = edm.GetProperty("Instance", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy).GetValue(null, null);
                var sys = R.S(R.F(l.Ws, "_systemName"));
                foreach (var listName in new[] { "GunMuseumEntries", "LauncherMuseumEntries" })
                {
                    var list = R.P(inst, listName) as IEnumerable;
                    if (list != null) foreach (var e in list) if (R.S(R.P(e, "IniRef")) == sys) return e;
                }
            }
            catch (Exception e) { Trace("system entry: " + e.Message); }
            return null;
        }

        /// <summary>The reference of a launcher / gun (its museum entry by system name, opened as the Unit Reference opens it); else its first ammunition's.</summary>
        void OpenSystemReference(WeaponsView v, Launcher l)
        {
            object entry = null;
            try
            {
                var asm = GameAssemblyStatic();
                var edm = asm.GetType("SeaPower.EncyclopedicDataManager");
                var inst = edm.GetProperty("Instance", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy).GetValue(null, null);
                var sys = R.S(R.F(l.Ws, "_systemName"));
                foreach (var listName in new[] { "GunMuseumEntries", "LauncherMuseumEntries" })
                {
                    var list = R.P(inst, listName) as IEnumerable;
                    if (list != null) foreach (var e in list) if (R.S(R.P(e, "IniRef")) == sys) { entry = e; break; }
                    if (entry != null) break;
                }
                if (entry != null)
                {
                    var mgv = asm.GetType("SeapowerUI.MainGameView").GetField("Instance").GetValue(null);
                    var vmT = asm.GetType("SeapowerUI.ViewModels.ObjectInformationViewModel");
                    var vm = Activator.CreateInstance(vmT, mgv, entry);
                    var cmd = asm.GetType("SeapowerUI.WindowHostCommands").GetProperty("OpenWindowCommand").GetValue(null, null);
                    R.Call(cmd, "Execute", vm, mgv);
                    return;
                }
            }
            catch (Exception e) { Trace("weapons tab: system reference: " + (e.InnerException ?? e).Message); }
            if (l.Ammo.Count > 0) OpenWeaponReference(v, l.Ammo[0]);
        }

        // ------------------------------------------------------------------ the click sound
        // The game's own buttons make no sound, so the mod has its own: a short click made in code (a decaying 1.9 kHz tone
        // over a few milliseconds of noise), played through a 2D AudioSource on the plugin's object (Unity's audio module by
        // reflection: the mod does not reference it). Volume: [UI] ClickVolume (0 = no sound).
        object _clickClip, _clickSource; bool _clickFailed;

        void ClickSound()
        {
            float vol = _clickVolume == null ? 0f : Mathf.Clamp01(_clickVolume.Value);
            if (vol <= 0f || _clickFailed) return;
            try
            {
                if (_clickSource == null)
                {
                    var clipT = Type.GetType("UnityEngine.AudioClip, UnityEngine.AudioModule");
                    var srcT = Type.GetType("UnityEngine.AudioSource, UnityEngine.AudioModule");
                    if (clipT == null || srcT == null) { _clickFailed = true; Trace("click sound: Unity audio module not found"); return; }
                    const int rate = 44100;
                    int n = rate * 45 / 1000;
                    var data = new float[n];
                    var rnd = new System.Random(7);
                    for (int k = 0; k < n; k++)
                    {
                        float t = k / (float)rate;
                        float tone = Mathf.Sin(2f * Mathf.PI * 1900f * t) * Mathf.Exp(-t / 0.006f);
                        float body = Mathf.Sin(2f * Mathf.PI * 620f * t) * Mathf.Exp(-t / 0.012f) * 0.5f;
                        float noise = ((float)rnd.NextDouble() * 2f - 1f) * Mathf.Exp(-t / 0.0015f) * 0.6f;
                        data[k] = Mathf.Clamp((tone + body + noise) * 0.55f, -1f, 1f);
                    }
                    var create = clipT.GetMethod("Create", new[] { typeof(string), typeof(int), typeof(int), typeof(int), typeof(bool) });
                    _clickClip = create.Invoke(null, new object[] { "AwcClick", n, 1, rate, false });
                    clipT.GetMethod("SetData", new[] { typeof(float[]), typeof(int) }).Invoke(_clickClip, new object[] { data, 0 });
                    var src = gameObject.AddComponent(srcT);
                    srcT.GetProperty("playOnAwake").SetValue(src, false, null);
                    srcT.GetProperty("spatialBlend").SetValue(src, 0f, null);
                    srcT.GetProperty("ignoreListenerPause").SetValue(src, true, null);
                    _clickSource = src;
                }
                _clickSource.GetType().GetMethod("PlayOneShot", new[] { _clickClip.GetType(), typeof(float) }).Invoke(_clickSource, new object[] { _clickClip, vol });
            }
            catch (Exception e) { _clickFailed = true; Trace("click sound: " + (e.InnerException ?? e).Message); }
        }

        void OpenFireMenu(WeaponsView v, List<string> ids, FrameworkElement at)
        {
            var menu = new ContextMenu();
            foreach (var id in ids)
            {
                var items = R.P(WeaponEntryFor(v, id), "Items") as IEnumerable;
                if (items == null) continue;
                if (ids.Count == 1) { AddMenu(menu.Items, items); continue; }
                // several ammunition types: one submenu each
                var ap = AmmoParams(v, id);
                var mi = new MenuItem { Header = AmmoName(ap, id) };
                AddMenu(mi.Items, items);
                if (mi.Items.Count > 0) menu.Items.Add(mi);
            }
            if (menu.Items.Count == 0) { Trace("weapons tab: no fire menu for " + string.Join(",", ids.ToArray())); return; }
            menu.PlacementTarget = at;
            menu.Placement = PlacementMode.Bottom;
            at.ContextMenu = menu;
            menu.IsOpen = true;
        }

        static void AddMenu(ItemCollection target, IEnumerable src)
        {
            foreach (var ci in src)
            {
                if (ci == null) continue;
                var label = R.P(ci, "Label");
                var mi = new MenuItem { Header = label as string ?? R.S(label) ?? "" };
                var en = R.P(ci, "IsEnabled");
                if (en != null) mi.IsEnabled = R.B(R.Rv(en));
                var chk = R.P(ci, "IsChecked");
                if (chk != null && R.B(R.Rv(chk))) { mi.IsCheckable = true; mi.IsChecked = true; }
                var sub = R.P(ci, "Items") as IEnumerable;
                bool hasSub = false;
                if (sub != null) foreach (var x in sub) { hasSub = true; break; }
                if (hasSub) AddMenu(mi.Items, sub);
                else
                {
                    var cmd = R.P(ci, "Command");
                    if (cmd != null) mi.Click += (s, ev) => { try { if (_debug) Trace("fire menu item: " + (mi.Header as string)); OurFireExecute(cmd); } catch (Exception ex) { Trace("fire menu: " + ex.Message); } };
                }
                target.Add(mi);
            }
        }

        /// <summary>Each refresh of a shown tab: the first time, all open if it fits, else all closed.</summary>
        void WeaponsTick(WeaponsView v, float now)
        {
            CheckTargeting(v);
            try { DirWindowTick(v, now); } catch (Exception e) { Trace("directors window: " + e.Message); }
            // the first opening for the unit: all open; while the tab does not fit, the open group taking the most room is
            // closed (one per frame, after the layout of the previous one)
            if (v.Fitting && v.Scroll.ViewportHeight > 1f && v.Scroll.ExtentHeight > 0f)
            {
                if (v.Scroll.ExtentHeight <= v.Scroll.ViewportHeight + 1f) v.Fitting = false;
                else
                {
                    GroupRow big = null;
                    foreach (var gr in v.Groups)
                        if (gr.Open && gr.Expandable && gr.Detail != null && gr.Detail.ActualHeight > 0f && (big == null || gr.Detail.ActualHeight > big.Detail.ActualHeight)) big = gr;
                    if (big == null) v.Fitting = false;
                    else { SetOpen(v, big, false); Refresh(v, true); v.NextRefresh = now + 0.3f; return; }
                }
            }
            if (now < v.NextRefresh) return;
            v.NextRefresh = now + 0.3f;
            Refresh(v, false);
        }
    }
}
