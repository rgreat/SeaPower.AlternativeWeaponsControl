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
            public static SolidColorBrush B(Color c) { return new SolidColorBrush(c); }
            public static readonly SolidColorBrush Clear = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        }

        const float ColFire = 128f, ColChip = 26f;

        sealed class ChipUI
        {
            public Grid G; public Ellipse Outer, Dot; public Rectangle Bar; public TextBlock None; public int Domain; public float Size; public bool Painted; public Chip Last;
        }

        sealed class MemberRow { public Launcher L; public TextBlock State, Fc; public string FcSig; public StockUI Stock; public Ellipse Dot; public ChipUI[] Chips; }

        sealed class StockUI { public StackPanel P; public TextBlock Cur, Max; }

        sealed class FireUI { public List<string> Ids; public Launcher Pin; public Border Box, Main, Arrow; public bool OverMain, OverArrow, Active; }

        sealed class IconBtn { public Border B; public bool Enabled = true, Over, Cease; public string Tip; }

        /// <summary>The control slots of a row: fire (split), cease fire, reload - for a set of launchers.</summary>
        sealed class CtrlUI { public List<Launcher> Scope; public List<string> Ammo; public IconBtn Cease, Reload; }

        sealed class GroupRow
        {
            public string Key; public List<Launcher> Ls = new List<Launcher>(); public bool Open; public string StateTip;
            public TextBlock Fc; public string FcSig;
            public bool StatesBuilt; public Ellipse[] SDot; public TextBlock[] SText; public StackPanel[] SPair; public string[] SLabel;
            public FrameworkElement SOne; public Ellipse SOneDot; public TextBlock SOneText; public FrameworkElement SMulti;
            public Path Chevron; public Border ChevronHit; public StockUI Stock; public Grid States; public ChipUI[] Chips; public StackPanel Detail;
            public List<MemberRow> Members = new List<MemberRow>(); public bool DetailBuilt, Expandable; public List<string> Ammo = new List<string>();
            public List<KeyValuePair<string, StockUI>> AmmoStocks = new List<KeyValuePair<string, StockUI>>();
            public TextBlock Chan; public Border ChanBox; public string ChanTip; public List<object> Dirs = new List<object>(); public bool GuidedAmmo, Wires;
        }

        sealed class WeaponsView
        {
            public AwcPlugin P;
            public object Unit;
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
            public TextBlock ExpandText;
            public float NextRefresh;
            public bool Fitting;   // the first opening for the unit: groups are closed one by one until the tab fits
            public readonly Dictionary<string, object> Entries = new Dictionary<string, object>();
            public readonly List<FireUI> Fires = new List<FireUI>();
            public readonly List<CtrlUI> Ctrls = new List<CtrlUI>();
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
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1f, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var hb = new Border { Child = head, BorderBrush = WC.B(WC.Line), BorderThickness = new Thickness(0f, 0f, 0f, 1f), Padding = new Thickness(10f, 6f, 10f, 6f) };
            var seg = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var lbl = T("WEAPONS", WC.Dim, 11f); lbl.Margin = new Thickness(0f, 0f, 12f, 0f);
            seg.Children.Add(lbl);
            var segBox = new Border { BorderBrush = WC.B(WC.BtnLine), BorderThickness = new Thickness(1f), CornerRadius = new CornerRadius(3f) };
            var segRow = new StackPanel { Orientation = Orientation.Horizontal };
            segBox.Child = segRow;
            string[] sn = { "Free", "Tight", "Hold" };
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
            v.TightRow.Children.Add(T("On Tight: engage hostile aircraft", WC.Sub, 12f));
            Click(v.TightRow, () => ToggleTightAir(v));
            Grid.SetColumn(v.TightRow, 1); head.Children.Add(v.TightRow);
            // Expand / Collapse all: at the left of the WEAPONS column title, centred in its height
            v.ExpandText = T("Expand all", WC.Sub, 11f);
            var exp = new Border { Child = v.ExpandText, Padding = new Thickness(8f, 2f, 8f, 3f), Margin = new Thickness(2f, 0f, 0f, 0f), Background = WC.B(WC.BtnBg), BorderBrush = WC.B(WC.BtnLine),
                                   BorderThickness = new Thickness(1f), CornerRadius = new CornerRadius(3f), VerticalAlignment = VerticalAlignment.Center,
                                   HorizontalAlignment = HorizontalAlignment.Left };
            Click(exp, () => { bool any = v.Groups.Exists(gr => gr.Expandable && !gr.Open); foreach (var gr in v.Groups) SetOpen(v, gr, any); Refresh(v, true); });
            Grid.SetRow(hb, 0); v.Root.Children.Add(hb);

            // column titles
            var ch = Columns(true, v);
            ch.Margin = new Thickness(10f, 6f, 10f, 6f);
            var lh = T("WEAPONS", WC.Dim, 10.5f); lh.HorizontalAlignment = HorizontalAlignment.Center; At(ch, lh, 1);
            Grid.SetColumnSpan(exp, 2); At(ch, exp, 0);
            var sth = T("STATE", WC.Dim, 10.5f); sth.HorizontalAlignment = HorizontalAlignment.Center; At(ch, sth, 2);
            var stk = T("AMMO", WC.Dim, 10.5f); stk.HorizontalAlignment = HorizontalAlignment.Center;
            At(ch, stk, 3);
            var fh = T("FIRE", WC.Dim, 10.5f); fh.HorizontalAlignment = HorizontalAlignment.Center;
            SetTip(fh, "Click: pick the target on the map. Arrow: salvo and launch options.");
            At(ch, fh, 4);
            for (int i = 0; i < 5; i++)
            {
                var t = T(DomainNames[i], WC.Dim, 10.5f); t.HorizontalAlignment = HorizontalAlignment.Center;
                SetTip(t, "Automatic use against " + DomainLong[i]);
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
            foreach (var kv in bySection)
            {
                var card = new StackPanel();
                var cb = new Border { Child = card, Background = WC.B(WC.Card), BorderBrush = WC.B(WC.Line), BorderThickness = new Thickness(1f),
                                      CornerRadius = new CornerRadius(3f), Margin = new Thickness(0f, 0f, 0f, 6f) };
                var sh = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10f, 4f, 10f, 3f) };
                sh.Children.Add(T(SectionNames[kv.Key], WC.Sub, 11.5f, FontWeights.SemiBold));
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
                if (count > 1) foreach (var c in counts) subText.Append(subText.Length > 0 ? ", " : "").Append(c.Value).Append(' ').Append(c.Key).Append(c.Value == 1 ? "" : "s");
                var sub = T(subText.ToString(), WC.Dim2, 11f); sub.Margin = new Thickness(8f, 0f, 0f, 0f);
                sh.Children.Add(sub);
                card.Children.Add(sh);
                foreach (var gr in kv.Value)
                {
                    bool o;
                    gr.Open = mem.TryGetValue(gr.Key, out o) ? o : true;
                    card.Children.Add(BuildGroup(v, gr));
                }
                v.Body.Children.Add(cb);
            }
            if (v.Groups.Count == 0) v.Body.Children.Add(T("No weapons.", WC.Dim, 12f));
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

        /// <summary>The AMMO column's width: the widest "max/max" of the tab's groups, ammunition and launchers (and the title), measured
        /// in the stock's font; set on every row's column (rows built later take it from v.StockW).</summary>
        void StockWidth(WeaponsView v)
        {
            float w = Measure("AMMO", 10.5f);
            Action<int> fit = max => { if (max > 0) { var s = Fmt(max); w = Math.Max(w, Measure(s + "/" + s, 12f)); } };
            foreach (var gr in v.Groups)
            {
                fit(StockMax(gr.Ls));
                foreach (var a in gr.Ammo) fit(StockMax(gr.Ls, a));
                if (gr.Ls.Count > 1) foreach (var l in gr.Ls) fit(R.I(R.F(R.F(l.Ws, "_vwp"), "_maxAmmoCount")));
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

            line("AIR · MSL · SUR · SUB · LND - may the AI use the launcher by itself against aircraft, missiles, surface ships, submarines, land targets:", WC.Text, 13f);
            item(chip(Chip.On), "automatic");
            item(chip(Chip.Off), "off (click to switch; the group's switch sets all its launchers)");
            item(chip(Chip.Order), "off by the unit's Standing Orders (click: allowed for this launcher)");
            item(chip(Chip.Mixed), "mixed in the group");
            line("Manual fire is never restricted. The switches are kept in the mission save.", WC.Dim, 12f);

            var gap = new Border { Height = 6f }; sp.Children.Add(gap);
            line("State:", WC.Text, 13f);
            item(dot(WC.Ok), "ready");
            item(dot(WC.Engage), "engaging - and its target (Track N)");
            item(dot(WC.Amber), "reloading; sensors offline (none of its directors is on); waiting - queued behind the launcher's own target, or for a fire-control channel");
            item(dot(WC.Red), "damaged");
            item(dot(WC.Grey), "off / empty");
            line("At a weapon's name while it fires: the director aiming it (blue), or \"Local control\" (orange) - no working director, less accurate.", WC.Dim, 12f);

            gap = new Border { Height = 6f }; sp.Children.Add(gap);
            line("Fire: pick the target on the map (on a launcher's row - that launcher fires); ▾ salvo and launch options; ■ cease fire; ↻ reload.", WC.Sub, 12.5f);
            line("T 2/6 · W 5/12: fire-control channels of the group's directors - targets tracked / guided weapons in flight at a time; amber when none is free. Point at it: the directors, with their reference.", WC.Sub, 12.5f);
            line("AMMO: left / at the start (loaded and in the magazines).", WC.Sub, 12.5f);
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
            At(row, gr.ChevronHit, 0);
            // name cell: the launcher and its ammunition (one type: its name, type and characteristics; several: their names)
            var cell = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0f, 3f, 6f, 3f) };
            cell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            cell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            cell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1f, GridUnitType.Star) });
            cell.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var l0 = gr.Ls[0];
            var top = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            top.Children.Add(Link(Cap(l0.Name), WC.Text, 13f, FontWeights.Medium, "Reference of " + Cap(l0.Name), () => OpenSystemReference(v, l0)));
            if (gr.Ls.Count > 1)
            {
                var badge = new Border { Child = T("×" + gr.Ls.Count, Color.FromRgb(214, 218, 223), 11f, FontWeights.SemiBold), Background = WC.B(WC.Badge),
                                         CornerRadius = new CornerRadius(2f), Padding = new Thickness(4f, 0f, 4f, 0f), Margin = new Thickness(6f, 0f, 0f, 0f), VerticalAlignment = VerticalAlignment.Center };
                top.Children.Add(badge);
            }
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
                    var lk = Link(R.S(R.P(ap, "_displayedName")) ?? id, WC.Dim, 11.5f, null, "Reference of the weapon", () => OpenWeaponReference(v, id));
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
            At(row, Controls(v, gr.Ls, gr.Ammo, gr.Ammo.Count == 1, true, gr.Ls.Count == 1 ? gr.Ls[0] : null), 4);
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

        /// <summary>One line about an ammunition type: its name (a link to its reference), type and characteristics.</summary>
        UIElement AmmoLine(WeaponsView v, string id, float size)
        {
            var ap = AmmoParams(v, id);
            var g = new Grid();
            for (int i = 0; i < 4; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 3 ? new GridLength(1f, GridUnitType.Star) : GridLength.Auto });
            var name = R.S(R.P(ap, "_displayedName")) ?? id;
            var type = R.S(R.F(ap, "_displayedType")) ?? "";
            var mw = MuseumWeapon(id);
            var nl = Link(name, size > 12f ? WC.Text : WC.Sub, size, size > 12f ? FontWeights.SemiBold : FontWeights.Medium, null, () => OpenWeaponReference(v, id));
            At(g, nl, 0);
            if (type.Length > 0)
            {
                var tb = new Border { Child = T(type, WC.Sub, 10.5f), BorderBrush = WC.B(Color.FromRgb(74, 82, 96)), BorderThickness = new Thickness(1f), Background = WC.Clear,
                                      CornerRadius = new CornerRadius(2f), Padding = new Thickness(4f, 0f, 4f, 0f), Margin = new Thickness(6f, 0f, 0f, 0f), VerticalAlignment = VerticalAlignment.Center };
                SetTip(tb, "Weapon type");
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
            ToolTipService.SetToolTip(nl, string.IsNullOrEmpty(desc) ? TipOf("Reference of the weapon") : new TextBlock { Text = desc.Replace("\\n", "\n"), TextWrapping = TextWrapping.Wrap, MaxWidth = 420f });
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
            if (fire && ammo.Count > 0) At(g, FireButton(v, ammo, pin), 0);
            if (!more) return g;
            var cu = new CtrlUI { Scope = scope, Ammo = ammo };
            cu.Cease = IconButton(true, "Cease fire", () => Cease(v, cu));
            At(g, cu.Cease.B, 1);
            bool mag = scope.Exists(l => { try { return R.B(R.Call(l.Ws, "hasAMagazine")); } catch { return false; } });
            if (mag)
            {
                cu.Reload = IconButton(false, "Reload from the magazine", () => Reload(v, cu));
                At(g, cu.Reload.B, 2);
            }
            v.Ctrls.Add(cu);
            return g;
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
            foreach (var l in cu.Scope)
            {
                if (R.B(R.F(l.Ws, "_executingEngageTask")) || R.B(R.F(l.Ws, "_isAutoEngaging")))
                {
                    try { R.Call(l.Ws, "EndEngageTask", true); } catch (Exception e) { Trace("cease: " + e.Message); }
                    if (R.Is(l.Ws, _tCiws)) { try { R.Call(l.Ws, "StopEngage"); } catch { } }
                }
            }
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
            Refresh(v, false);
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
                try { if (R.B(R.Call(l.Ws, "CanForceReload"))) R.Call(l.Ws, "ForceReload"); } catch (Exception e) { Trace("reload: " + e.Message); }
            }
            Refresh(v, false);
        }

        static bool Engaging(Launcher l) { return R.B(R.F(l.Ws, "_executingEngageTask")) || R.B(R.F(l.Ws, "_isAutoEngaging")); }

        void RefreshCtrls(WeaponsView v)
        {
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
                if (cu.Reload != null)
                {
                    bool can = cu.Scope.Exists(l => { try { return R.B(R.Call(l.Ws, "CanForceReload")); } catch { return false; } });
                    SetEnabled(cu.Reload, can);
                    Tip(cu.Reload.B, can ? cu.Reload.Tip : cu.Reload.Tip + " (nothing to reload now)");
                }
            }
        }

        FrameworkElement FireButton(WeaponsView v, List<string> ids, Launcher pin)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var main = new Border { Child = T("Fire", WC.FireText, 12f, FontWeights.Medium), Padding = new Thickness(9f, 0f, 7f, 0f), Background = WC.Clear };
            SetTip(main, pin != null && v.Groups.Exists(x => x.Ls.Count > 1 && x.Ls.Contains(pin)) ? "Fire this launcher: pick the target on the map" : "Fire: pick the target on the map");
            var arrow = new Border { Child = T("▾", WC.FireText, 11f), Padding = new Thickness(6f, 0f, 6f, 0f), Background = WC.Clear,
                                     BorderBrush = WC.B(WC.FireLine), BorderThickness = new Thickness(1f, 0f, 0f, 0f) };
            SetTip(arrow, "Salvo and launch options");
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
            Click(main, () => { SetPending(v, pin, ids); FireNow(v, PickAmmo(ids, pin)); CheckTargeting(v); });
            Click(arrow, () => { SetPending(v, pin, ids); OpenFireMenu(v, ids, fire); });
            return fire;
        }

        static void SetPending(WeaponsView v, Launcher pin, List<string> ids)
        {
            Pending = pin == null ? null : new PendingPin { Unit = v.Unit, Ws = pin.Ws, Ammos = new List<string>(ids), Since = MissionTime() };
        }

        /// <summary>The ammunition a launcher's Fire uses: the one it holds ready, else its first.</summary>
        static string PickAmmo(List<string> ids, Launcher pin)
        {
            if (ids.Count == 1 || pin == null) return ids[0];
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
        static void CheckTargeting(WeaponsView v)
        {
            string id = null;
            try
            {
                var globals = GameAssemblyStatic().GetType("SeaPower.Globals");
                var mgvm = globals == null ? null : globals.GetField("_mainGameViewModel", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).GetValue(null);
                var st = R.Rv(R.P(R.P(mgvm, "StateManager"), "CurrentState"));
                if (st != null && st.GetType().Name == "AttackingState" && ReferenceEquals(R.P(st, "Source"), v.Unit)) id = R.S(R.P(st, "Ammunition"));
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
            if (cmd != null) R.Call(cmd, "Execute", (object)null);
        }

        void BuildDetail(WeaponsView v, GroupRow gr)
        {
            gr.DetailBuilt = true;
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
                // state: wraps to a second line when long, centred
                var st = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8f, 2f, 6f, 2f), Background = WC.Clear };
                m.Dot = new Ellipse { Width = 6f, Height = 6f, Margin = new Thickness(0f, 0f, 5f, 0f), VerticalAlignment = VerticalAlignment.Center };
                m.State = T("", WC.Ok, 12f); m.State.TextTrimming = TextTrimming.CharacterEllipsis;   // one line: the row keeps its height
                st.Children.Add(Centred(m.Dot, m.State));
                FitWidth(st, m.State);
                At(g, st, 1);
                m.Stock = MakeStock(12f, WC.Sub);
                At(g, m.Stock.P, 2);
                At(g, Controls(v, new List<Launcher> { l }, l.Ammo, true, true, l), 3);
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
                gr.Detail.Children.Add(g);
            }
        }

        /// <summary>One of several ammunition types of a group: its line in a frame, its stock, its fire button (in the control block).</summary>
        UIElement AmmoCard(WeaponsView v, GroupRow gr, string id)
        {
            var g = MemberColumns(v);
            g.MinHeight = 28f;
            var line = AmmoLine(v, id, 12f);
            var b = new Border { Child = line, Background = WC.B(WC.Ammo), BorderBrush = WC.B(WC.Line), BorderThickness = new Thickness(1f), CornerRadius = new CornerRadius(3f),
                                 Padding = new Thickness(8f, 2f, 6f, 2f), Margin = new Thickness(0f, 2f, 8f, 2f), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumnSpan(b, 2);
            g.Children.Add(b);
            var stock = MakeStock(12f, WC.Sub);
            At(g, stock.P, 2);
            gr.AmmoStocks.Add(new KeyValuePair<string, StockUI>(id, stock));
            var scope = gr.Ls.FindAll(x => x.Ammo.Contains(id));
            At(g, Controls(v, scope, new List<string> { id }, true, false, null), 3);
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

        /// <summary>The characteristics shown in a line: (text, tooltip).</summary>
        static List<KeyValuePair<string, string>> Specs(object ap, object mw)
        {
            var parts = new List<KeyValuePair<string, string>>();
            var g = R.S(R.P(mw, "GuidanceTypeText"));
            if (!string.IsNullOrEmpty(g) && g != "None") parts.Add(new KeyValuePair<string, string>(g, "Guidance: " + g));
            float min = R.Fl(R.P(R.P(mw, "MinLaunchRange"), "InNauticalMiles")), max = R.Fl(R.P(R.P(mw, "MaxLaunchRange"), "InNauticalMiles"));
            if (max > 0f && !float.IsNaN(max))
            {
                bool hasMin = min > 0.01f && !float.IsNaN(min);
                parts.Add(new KeyValuePair<string, string>((hasMin ? Num(min) + "–" : "up to ") + Num(max) + " nm",
                                                           "Range, nm: " + (hasMin ? Num(min) + "–" : "") + Num(max)));
            }
            var type = R.S(R.F(ap, "_type"));
            float kt = R.Fl(R.F(ap, "_maxVelocityInKnots"));
            if (kt > 0f && type != "Projectile") parts.Add(new KeyValuePair<string, string>(Num(kt) + " kt", "Max speed, kt: " + Num(kt)));
            float wh = R.Fl(R.F(ap, "_warheadPower"));
            if (wh > 0f) parts.Add(new KeyValuePair<string, string>("pwr " + Num(wh), "Warhead power: " + Num(wh)));
            return parts;
        }

        static string Num(float f)
        {
            return f >= 100f ? f.ToString("0", CultureInfo.InvariantCulture) : f >= 10f ? f.ToString("0.#", CultureInfo.InvariantCulture) : f.ToString("0.##", CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------------ state

        static int Cat(object ws)
        {
            if (R.B(R.Rv(R.P(ws, "Inoperable")))) return 3;
            if (R.B(R.F(ws, "_isOffline")) || R.B(R.F(ws, "_isEmpty"))) return 4;
            if (PinWait(ws) != null) return 5;
            if (R.B(R.F(ws, "_isReloading")) || R.Fl(R.F(ws, "_remainingReloadTime")) > 0f) return 2;
            if (SensorsOff(ws)) return 6;
            if (R.B(R.F(ws, "_executingEngageTask")) || R.B(R.F(ws, "_isAutoEngaging"))) return 1;
            return 0;
        }

        const int NCat = 7;
        static readonly string[] CatNames = { "ready", "engaging", "reload", "damaged", "off", "waiting", "sensors off" };
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
                n += Loaded(l.Ws, ammo);
                var mag = R.F(R.F(l.Ws, "_vwp"), "_associatedMagazine");
                if (mag == null || mags.Contains(mag)) continue;
                mags.Add(mag);
                foreach (var a in ammo) { try { n += R.I(R.Call(mag, "getAmmunitionCount", a)); } catch { } }
            }
            return n;
        }

        static StockUI MakeStock(float size, Color c)
        {
            var u = new StockUI { P = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                                                       Background = WC.Clear } };
            u.Cur = T("", c, size); u.Max = T("", WC.Dim, size);
            u.P.Children.Add(u.Cur); u.P.Children.Add(u.Max);
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
                if (mag == null) { n += R.I(R.F(vwp, "_maxAmmoCount")); continue; }
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
                    return string.IsNullOrEmpty(own) ? nm : "Track " + own + " (" + nm + ")";
                }
                var globals = GameAssemblyStatic().GetType("SeaPower.Globals");
                var tf = globals.GetField("_playerTaskforce", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).GetValue(null);
                var veh = R.Call(target, "GetMapVehicle", tf);
                if (veh == null) return "unknown";
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
        static void StatusParts(object ws, int c, out string st, out string tgt)
        {
            st = null; tgt = null;
            if (c == 5)
            {
                // a task of this launcher waits: for the launcher, or for a fire-control channel
                var pw = PinWait(ws);
                st = pw.Wait == 1 ? "Queued" : "Waiting for FCR channel";
                tgt = TrackLabel(pw.Target);
                return;
            }
            try { st = R.S(R.Call(ws, "GetStatus")); } catch { }
            st = string.IsNullOrEmpty(st) ? CatNames[c] : st;
            if (c == 2 && st.Length > 0 && !char.IsLetter(st[0])) st = "Reloading: " + st;
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
                    if (LocalControl(ws)) return new KeyValuePair<string, bool>("Local control", true);
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
            if (vessel)
            {
                bool t = R.B(R.P(tight, "ResolvedValue"));
                v.TightMark.Visibility = t ? Visibility.Visible : Visibility.Hidden;
                v.TightBox.Background = WC.Clear;
                v.TightBox.BorderBrush = WC.B(t ? WC.Ok : WC.Ring);
            }
            v.ExpandText.Text = v.Groups.Exists(g => g.Expandable && !g.Open) ? "Expand all" : "Collapse all";
            RefreshCtrls(v);

            foreach (var gr in v.Groups)
            {
                bool open = gr.Open && gr.Expandable;
                gr.Chevron.RenderTransform = open ? new RotateTransform(90f) : null;
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
                foreach (var kv in gr.AmmoStocks) SetStock(kv.Value, Stock(gr.Ls, kv.Key), StockMax(gr.Ls, kv.Key), plain);
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
                }
                foreach (var m in gr.Members)
                {
                    int c = Cat(m.L.Ws);
                    string mst, mtgt;
                    StatusParts(m.L.Ws, c, out mst, out mtgt);
                    var full = string.IsNullOrEmpty(mtgt) ? mst : mst + " → " + mtgt;
                    var s = Shown(m.State, mst, mtgt, 1);
                    SetText(m.State, s, CatColor(c));
                    Tip(m.State, s != full ? full : null);
                    SetFill(m.Dot, CatColor(c));
                    var mf = FcOf(m.L.Ws);
                    SetFc(m.Fc, ref m.FcSig, mf == null ? new List<KeyValuePair<string, bool>>() : new List<KeyValuePair<string, bool>> { mf.Value });
                    SetStock(m.Stock, Loaded(m.L.Ws, m.L.Ammo), R.I(R.F(R.F(m.L.Ws, "_vwp"), "_maxAmmoCount")), IsGun(m.L));
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
                var sb = new StringBuilder("Fire-control channels of the directors:");
                foreach (var s in gr.Dirs)
                {
                    int w = R.I(R.P(s, "EffectiveWeaponChannels")), w0 = Count(R.F(s, "_associatedWeapons"));
                    int t = R.I(R.F(s, "_targetChannels")), t0 = Count(R.F(s, "_associatedTargets"));
                    tw += w; uw += w0; tt += t; ut += t0;
                    bool on = R.B(R.Rv(R.P(s, "IsOn"))), bad = R.B(R.Rv(R.P(s, "Inoperable")));
                    sb.Append("\n").Append(Cap(R.S(R.Rv(R.P(s, "DisplayName"))) ?? R.S(R.F(s, "_systemName")) ?? "?"))
                      .Append(bad ? " (damaged)" : on ? "" : " (off)").Append(": targets ").Append(t0).Append('/').Append(t);
                    if (gr.GuidedAmmo) sb.Append(", guided weapons ").Append(w0).Append('/').Append(w);
                }
                text = "T " + ut + "/" + tt + (gr.GuidedAmmo ? " · W " + uw + "/" + tw : "");
                full = ut >= tt || (gr.GuidedAmmo && uw >= tw);
                sb.Append("\nT: targets tracked at a time; W: guided weapons in flight at a time.");
                tip = sb.ToString();
            }
            else
            {
                int used = 0;
                try { used = R.I(R.Call(v.Unit, "WiresInUse")); } catch { }
                int max = R.I(R.F(v.Unit, "_maxWireCount"));
                text = "Wires " + used + "/" + max;
                full = used >= max;
                tip = "Wire-guided torpedoes on the wire: in use / at a time.";
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
            sp.Children.Add(T("FIRE-CONTROL DIRECTORS", WC.Dim, 10.5f));
            foreach (var s in gr.Dirs)
            {
                var box = new StackPanel { Margin = new Thickness(0f, 6f, 0f, 0f) };
                var name = Cap(R.S(R.Rv(R.P(s, "DisplayName"))) ?? R.S(R.F(s, "_systemName")) ?? "?");
                var entry = SensorEntry(s);
                var head = new StackPanel { Orientation = Orientation.Horizontal };
                var dirSys = s;
                head.Children.Add(entry != null ? (UIElement)Link(name, WC.Text, 12.5f, FontWeights.SemiBold, "Reference of the director", () => OpenEntry(entry))
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
            var foot = T("T: targets tracked at a time · W: guided weapons in flight at a time", WC.Dim2, 10.5f);
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
                SetText(kv.Value[0], bad ? "damaged" : on ? "on" : "off", bad ? WC.Red : on ? WC.Ok : WC.Amber);
                int w = R.I(R.P(s, "EffectiveWeaponChannels")), w0 = Count(R.F(s, "_associatedWeapons"));
                int t = R.I(R.F(s, "_targetChannels")), t0 = Count(R.F(s, "_associatedTargets"));
                bool full = t0 >= t || (guided && w0 >= w);
                SetText(kv.Value[1], "Targets " + t0 + "/" + t + (guided ? " · guided weapons " + w0 + "/" + w : ""), full ? WC.Amber : WC.Sub);
            }
        }

        /// <summary>The director's main figures: type, fire-control grade and solution gain, the encyclopedia's facts (ranges, band...).</summary>
        static string DirFacts(object s, object entry)
        {
            var parts = new List<string>();
            if (entry != null)
            {
                var ty = R.S(R.P(entry, "TypeText")); if (!string.IsNullOrEmpty(ty)) parts.Add(ty);
                var gr = R.S(R.P(entry, "FireControlGradeText")); if (!string.IsNullOrEmpty(gr)) parts.Add("grade " + gr);
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
            if (gain > 0f) parts.Add("solution gain " + gain.ToString("0.0#", CultureInfo.InvariantCulture));
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
                cs[i] = Cat(gr.Ls[i].Ws);
                StatusParts(gr.Ls[i].Ws, cs[i], out sts[i], out tgts[i]);
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
                        if (show) SetText(gr.SText[i], cnt[c] + " " + CatNames[c], CatColor(c));
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
            if (st != AwcPlugin.Chip.Mixed) return ChipTip(unit, gr.Ls[0], d).Replace("this launcher", "all " + gr.Ls.Count + " " + Noun(gr.Ls[0]) + "s");
            var sb = new StringBuilder(DomainNames[di] + ": mixed - ");
            int k = 0;
            foreach (var l in gr.Ls)
            {
                var s = State(unit, l, d);
                if (s == AwcPlugin.Chip.None) continue;
                sb.Append(k++ > 0 ? ", " : "").Append(string.IsNullOrEmpty(l.Loc) ? l.Name : l.Loc).Append(": ")
                  .Append(s == AwcPlugin.Chip.On ? "automatic" : s == AwcPlugin.Chip.Off ? "off" : "off by Standing Orders");
            }
            sb.Append(".\nClick: the same for all.");
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

        object WeaponEntryFor(WeaponsView v, string id)
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
                var mi = new MenuItem { Header = R.S(R.P(ap, "_displayedName")) ?? id };
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
                    if (cmd != null) mi.Click += (s, ev) => { try { R.Call(cmd, "Execute", (object)null); } catch (Exception ex) { Trace("fire menu: " + ex.Message); } };
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
