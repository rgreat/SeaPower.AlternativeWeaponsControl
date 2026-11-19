// ======================================================================================================================
// rgreat settings kit - THE SAME FILE IN EVERY rgreat MOD (Alternative Game Interface, Alternative Weapons Control,
// Alternative Damage Model, Alternative Special Effects). The canonical copy is src/SettingsKit.cs of the SeaPower.AlternativeBase
// repository; change it there and copy it to the mods with tools/sync.sh, never edit a copy inside a mod.
//
// WinStyle: the colours of the settings windows (the game's dark panels) and the small helpers every window uses.
// WinFrame: the window itself - the part that is the same in all mods and has nothing to do with what the settings are:
//   - finding the panel of the screen and putting the window into it (or into a Popup), taking it out again;
//   - the size in screen pixels (kept in UI units), cut to the screen, centred on it once laid out;
//   - dragging by the title bar, resizing by the edges and corners;
//   - the title bar, the help pane, the footer, the rows of the window put together;
//   - the game's hot keys off while a text box has the keyboard (InputHandler.typingActive);
//   - the editors of a row: number (- / box / +), check box, drop-down, the reset arrow, the help of the row under the mouse.
// What the mods keep: the list of options, where a value is read from / written to, the tabs and their pages, texts (Loc).
// ======================================================================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Noesis;

namespace Rgreat.Kit
{
    internal static class WinStyle
    {
        public static readonly Color Bg = Color.FromRgb(30, 33, 38), Panel = Color.FromRgb(38, 42, 48), Card = Color.FromRgb(45, 50, 57),
                                     Line = Color.FromRgb(70, 76, 86), Text = Color.FromRgb(230, 232, 235), Dim = Color.FromRgb(150, 156, 162),
                                     Warn = Color.FromRgb(236, 212, 110), Title = Color.FromRgb(43, 48, 56), Field = Color.FromRgb(21, 24, 28),
                                     Err = Color.FromRgb(232, 96, 80), Hl = Color.FromRgb(52, 58, 66),
                                     HelpText = Color.FromRgb(194, 199, 204), HelpBg = Color.FromRgb(34, 38, 44);

        public static SolidColorBrush B(Color c) { return new SolidColorBrush(c); }
        public static readonly SolidColorBrush Clear = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));

        public static bool TryNum(string s, out float v)
        {
            return float.TryParse((s ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && !float.IsNaN(v) && !float.IsInfinity(v);
        }

        public static float Num(string s) { float v; return TryNum(s, out v) ? v : 0f; }

        /// <summary>A drawn ✕ for a button: the font's glyph sat low in the button, cut at the bottom and off centre.</summary>
        public static Path Cross(float size)
        {
            return new Path { Data = new StreamGeometry("M0,0 L10,10 M10,0 L0,10"), Stroke = B(Text), StrokeThickness = 1.5f, Stretch = Stretch.Uniform,
                              Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }

        /// <summary>The title of a group of rows (capitals, small, grey).</summary>
        public static TextBlock GroupTitle(string s, float top = 10f, float bottom = 2f)
        {
            return new TextBlock { Text = (s ?? "").ToUpperInvariant(), Foreground = B(Dim), FontSize = 11f, Margin = new Thickness(12f, top, 12f, bottom) };
        }

        public static DependencyObject SafeParent(DependencyObject e) { try { return VisualTreeHelper.GetParent(e); } catch { return null; } }
    }

    internal sealed class WinFrame
    {
        // ---------------------------------------------------------------- what the mod sets
        public Action<string> Trace = s => { };
        /// <summary>Applied to the texts given to Text / Btn (a mod whose English text is the key of its translation: Loc.T). Default: as is.</summary>
        public Func<string, string> Tr = s => s;
        /// <summary>The tool tip of the grip in the bottom right corner.</summary>
        public Func<string> ResizeTip = () => "";
        /// <summary>A text box got / lost the keyboard. Default: the game's hot keys off / on.</summary>
        public Action<bool, TextBox> Keyboard;
        public float MinW = 560f, MinH = 420f, MaxW = 1600f, MaxH = 1400f;
        /// <summary>The id of the mod in the hub's registry (ModRow, the switch between the mods' windows).</summary>
        public string Id;

        // ---------------------------------------------------------------- state
        public Grid Root;
        public TranslateTransform Move;
        public Panel Host;            // the panel of the screen the window was put into (null: in a Popup, or not shown)
        public Popup Popup;
        public UIElement Top;         // the screen the window is centred on (mouse positions for dragging)
        public float W, H;            // the size in UI units; set from the size in screen pixels at the first opening, kept while the game runs
        public TextBlock Help;        // the help pane's text (HelpPane)
        public ScrollViewer HelpScroll;
        public readonly float PixW, PixH;
        int _place;
        bool _sizedOnce;
        float[] _geom;                // the rectangle (screen pixels) of the window of another mod that this one replaces
        List<KeyValuePair<object[], Ellipse>> _modDots;

        /// <param name="pixW">The size in screen pixels (what fits at once); UI units follow when the game's UI scale is known.</param>
        public WinFrame(float pixW, float pixH) { PixW = W = pixW; PixH = H = pixH; }

        // ================================================================ open / close

        /// <summary>
        /// Finds where the window goes: the topmost Grid / Canvas (not an items host) on the way up from 'anchor' (when there is none, from 'other').
        /// Sets Top (the root of that screen). Null: no place, the window goes into a Popup.
        /// </summary>
        public Panel Locate(UIElement anchor, UIElement other)
        {
            DependencyObject top = anchor; Panel panel = null;
            foreach (var start in new[] { anchor, other })
            {
                if (start == null || panel != null) continue;
                for (DependencyObject p = start; p != null; p = WinStyle.SafeParent(p))
                {
                    top = p;
                    var pn = p as Panel;
                    bool itemsHost = false;
                    try { itemsHost = pn != null && pn.IsItemsHost; } catch { }
                    if (pn != null && !itemsHost && (pn is Grid || pn is Canvas)) panel = pn;
                }
            }
            Top = top as UIElement ?? anchor;
            return panel;
        }

        /// <summary>A new empty root of the window (the size kept so far).</summary>
        public Grid NewRoot() { Root = new Grid { Width = W, Height = H }; return Root; }

        /// <summary>Puts the built Root into the panel (centred, above everything) or, with no panel, into a Popup in the middle of Top.</summary>
        public void Mount(Panel panel)
        {
            Move = new TranslateTransform(0f, 0f);
            _place = 0;
            _geom = Rgreat.Hub.SettingsHub.TakeGeometry();   // replacing the window of another mod: same place and size
            Root.LayoutUpdated -= OnLayout;
            Root.LayoutUpdated += OnLayout;   // size in screen pixels and centring on the screen: once laid out
            Root.RenderTransform = Move;
            if (panel != null)
            {
                var grid = panel as Grid;
                if (grid != null)
                {
                    Root.HorizontalAlignment = HorizontalAlignment.Center;
                    Root.VerticalAlignment = VerticalAlignment.Center;
                    Grid.SetRow(Root, 0); Grid.SetColumn(Root, 0);
                    Grid.SetRowSpan(Root, Math.Max(1, grid.RowDefinitions.Count));
                    Grid.SetColumnSpan(Root, Math.Max(1, grid.ColumnDefinitions.Count));
                }
                else
                {
                    float w = panel.ActualWidth > 0 ? panel.ActualWidth : 1280f, h = panel.ActualHeight > 0 ? panel.ActualHeight : 720f;
                    Canvas.SetLeft(Root, Math.Max(0f, (w - Root.Width) / 2f));
                    Canvas.SetTop(Root, Math.Max(0f, (h - Root.Height) / 2f));
                }
                Panel.SetZIndex(Root, 30000);
                panel.Children.Add(Root);
                Host = panel;
            }
            else
            {
                Popup = new Popup { Child = Root, StaysOpen = true, AllowsTransparency = true, Placement = PlacementMode.Center, PlacementTarget = Top };
                Popup.IsOpen = true;
            }
            CopyStart();
        }

        /// <summary>Takes the window out of the screen (Root stays, built).</summary>
        public void Unmount()
        {
            try { if (Host != null && Root != null) Host.Children.Remove(Root); } catch { }
            try { var pp = Root == null ? null : WinStyle.SafeParent(Root) as Panel; if (pp != null) pp.Children.Remove(Root); } catch { }   // wherever it is now
            try { if (Popup != null) { Popup.IsOpen = false; Popup.Child = null; } } catch { }
            Host = null; Popup = null;
            CopyStop();
        }

        /// <summary>The window is on a screen (in its panel, or its Popup is open).</summary>
        public bool Attached
        {
            get { return (Host != null && Root != null && WinStyle.SafeParent(Root) != null) || (Popup != null && Popup.IsOpen); }
        }

        // ================================================================ the game's keyboard while typing

        static PropertyInfo _ihInstance; static FieldInfo _ihTyping; static bool _ihTried;

        /// <summary>The game's hot keys off (on = a text box has the keyboard) / on again.</summary>
        public void Typing(bool on)
        {
            try
            {
                if (!_ihTried)
                {
                    _ihTried = true;
                    foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (a.GetName().Name != "Seapower-Scripts") continue;
                        var ih = a.GetType("SeaPower.InputHandler"); var sg = a.GetType("SeaPower.Singleton`1");
                        if (ih != null && sg != null)
                        {
                            _ihInstance = sg.MakeGenericType(ih).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public);
                            _ihTyping = ih.GetField("typingActive", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        }
                    }
                }
                if (_ihInstance == null || _ihTyping == null) return;
                var inst = _ihInstance.GetValue(null, null);
                if (inst != null) _ihTyping.SetValue(inst, on);
            }
            catch (Exception e) { Trace("typing: " + e.Message); }
        }

        // ================================================================ small elements

        public TextBlock Text(string s, Color c, float size = 13f)
        {
            return new TextBlock { Text = Tr(s), Foreground = WinStyle.B(c), FontSize = size, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap };
        }

        public Button Btn(string s, Action click, float minW = 0f)
        {
            string shown = Tr(s);
            var b = new Button { Content = shown, Padding = new Thickness(10f, 2f, 10f, 2f), Margin = new Thickness(0f, 0f, 6f, 0f), MinHeight = 26f, MinWidth = minW };
            b.Click += (o, e) => { try { click(); } catch (Exception ex) { Trace("button " + shown + ": " + ex); } };
            return b;
        }

        /// <summary>A text box in the look of the window; the game's hot keys are off while it has the keyboard. width 0: as wide as it is given.</summary>
        public TextBox Edit(string text, float width)
        {
            var t = new TextBox { Text = text ?? "", Height = 24f, TextAlignment = TextAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
                                  Background = WinStyle.B(WinStyle.Field), Foreground = WinStyle.B(WinStyle.Text), BorderBrush = WinStyle.B(WinStyle.Line),
                                  Padding = new Thickness(2f, 0f, 2f, 0f) };
            if (width > 0f) t.Width = width;
            t.GotFocus += (s, e) => { Keys(true, t); try { t.SelectAll(); } catch { } };
            t.LostFocus += (s, e) => Keys(false, t);
            return t;
        }

        void Keys(bool on, TextBox t) { _typingBox = on ? t : null; if (Keyboard != null) Keyboard(on, t); else Typing(on); }

        // ================================================================ Ctrl+C: the text under the mouse

        string _copy, _helpPlain; TextBox _typingBox;   // _typingBox: the box that has the keyboard (it stays focused after a click elsewhere)
        UnityEngine.GameObject _copyObj;

        /// <summary>Per-frame check of the window (a small component of the game's scene lives while the window is on a screen).</summary>
        sealed class CopyPoll : UnityEngine.MonoBehaviour
        {
            public Action Tick;
            void Update() { if (Tick != null) Tick(); }
        }

        void CopyStart()
        {
            CopyStop();
            try { _copyObj = new UnityEngine.GameObject("rgreat.settings.copy"); _copyObj.AddComponent<CopyPoll>().Tick = CopyStep; }
            catch { _copyObj = null; }
        }

        void CopyStop()
        {
            try { if (_copyObj != null) UnityEngine.Object.Destroy(_copyObj); } catch { }
            _copyObj = null; _copy = null;
        }

        void CopyStep()
        {
            if (!UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.C)) return;
            if (!UnityEngine.Input.GetKey(UnityEngine.KeyCode.LeftControl) && !UnityEngine.Input.GetKey(UnityEngine.KeyCode.RightControl)) return;
            bool selected = false;
            try { selected = _typingBox != null && _typingBox.SelectionLength > 0; } catch { }
            if (Trace != null) Trace("settings: Ctrl+C, text under the mouse " + (string.IsNullOrEmpty(_copy) ? "none" : _copy.Length + " chars") + (selected ? ", the box with the keyboard has a selection (its own copy)" : ""));
            if (string.IsNullOrEmpty(_copy) || selected) return;   // the box that has the keyboard has text selected: that is what Ctrl+C copies
            try
            {
                UnityEngine.GUIUtility.systemCopyBuffer = _copy;
                if (Help != null) Help.Inlines.Add(new Run("  ✓") { Foreground = WinStyle.B(WinStyle.Warn) });   // copied
            }
            catch { }
        }

        // ================================================================ the parts of the window

        /// <summary>The dark rounded window, added to Root; the parts go into its Child (Assemble).</summary>
        public Border Window()
        {
            var win = new Border { Background = WinStyle.B(WinStyle.Bg), BorderBrush = WinStyle.B(WinStyle.Line), BorderThickness = new Thickness(1f), CornerRadius = new CornerRadius(4f) };
            Root.Children.Add(win);
            return win;
        }

        /// <summary>Title bar: drag to move, ✕ to close. With several mods installed the title is the common one (as on the menu button).</summary>
        public Grid TitleBar(string text, Action close)
        {
            try { text = Rgreat.Hub.SettingsHub.WindowTitle(text); } catch { }
            var title = new Grid { Background = WinStyle.B(WinStyle.Title), Height = 32f };
            title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1f, GridUnitType.Star) });
            title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var tt = Text(text, WinStyle.Text, 14f); tt.Margin = new Thickness(10f, 0f, 0f, 0f);
            title.Children.Add(tt);
            var x = new Button { Content = WinStyle.Cross(10f), Width = 28f, Height = 24f, Padding = new Thickness(0f), Margin = new Thickness(0f, 0f, 4f, 0f),
                                 HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            x.Click += (o, e) => close();
            Grid.SetColumn(x, 1);
            title.Children.Add(x);
            Draggable(title);
            return title;
        }

        /// <summary>The settings area: a card with a scroll viewer (no sideways scrolling); Help is the pane below it.</summary>
        public Border Card(ScrollViewer scroll, float top = 0f)
        {
            return new Border { Child = scroll, Background = WinStyle.B(WinStyle.Card), BorderBrush = WinStyle.B(WinStyle.Line), BorderThickness = new Thickness(1f), Margin = new Thickness(12f, top, 12f, 0f) };
        }

        /// <summary>The pane with the help of the setting under the mouse: fixed height (the window does not jump as the text changes); a long text scrolls
        /// (the wheel over the pane; the text stays when the mouse leaves the row, so it can be read to the end). Sets Help.</summary>
        public Border HelpPane(string text)
        {
            _helpDefault = text;
            Help = new TextBlock { Foreground = WinStyle.B(WinStyle.HelpText), FontSize = 12f, TextWrapping = TextWrapping.Wrap, Text = text };
            HelpScroll = new ScrollViewer { Content = Help, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            var pane = new Border { Child = HelpScroll, Background = WinStyle.B(WinStyle.HelpBg), BorderBrush = WinStyle.B(WinStyle.Line), BorderThickness = new Thickness(1f),
                                    Padding = new Thickness(10f, 8f, 4f, 8f), Margin = new Thickness(12f, 8f, 12f, 0f), Height = 108f, CornerRadius = new CornerRadius(3f) };
            pane.MouseEnter += (o, e) => { _copy = _helpPlain; };   // Ctrl+C over the pane copies what is written in it
            pane.MouseLeave += (o, e) => { _copy = null; };
            return pane;
        }

        string _helpDefault;
        /// <summary>The help pane shows the first option of the page (for the screenshots); the default text when there is none.</summary>
        public void ClearHelp()
        {
            if (Help == null) return;
            _hovers.RemoveAll(kv => !InWindow(kv.Key));
            if (_hovers.Count > 0) { try { _hovers[0].Value(); return; } catch { } }   // the help of the first option shown
            _helpPlain = null; Help.Inlines.Clear(); Help.Text = _helpDefault ?? "";
            try { if (HelpScroll != null) HelpScroll.ScrollToVerticalOffset(0f); } catch { }
        }

        readonly List<KeyValuePair<Border, Action>> _hovers = new List<KeyValuePair<Border, Action>>();
        bool InWindow(DependencyObject e) { for (var p = e; p != null; p = WinStyle.SafeParent(p)) if (ReferenceEquals(p, Root)) return true; return false; }

        /// <summary>The narrowest width (UI units) a screenshot fit may give: the row of the mods with its language box in one line, and the footer (search, status, buttons).</summary>
        public float ShotMinW { get { return Math.Max(Math.Max(MinW, _rowW), 800f); } }

        /// <summary>Puts a name (bold) and the text (and an extra line in yellow) into the help pane, scrolled to the top.</summary>
        public void ShowHelp(string label, string text, string extra = null)
        {
            if (Help == null) return;
            _helpPlain = label + " - " + text + (string.IsNullOrEmpty(extra) ? "" : "  " + extra);
            Help.Inlines.Clear();
            var r = new Run(label); r.FontWeight = FontWeight.Bold; r.Foreground = WinStyle.B(WinStyle.Text);
            Help.Inlines.Add(r);
            Help.Inlines.Add(new Run(" - " + text));
            if (!string.IsNullOrEmpty(extra)) { var x = new Run("  " + extra); x.Foreground = WinStyle.B(WinStyle.Warn); Help.Inlines.Add(x); }
            try { if (HelpScroll != null) HelpScroll.ScrollToVerticalOffset(0f); } catch { }
        }

        /// <summary>The footer: an optional element at the left, the status text, Cancel / Apply / OK at the right.</summary>
        public Grid Footer(UIElement left, out TextBlock status, string cancel, Action onCancel, string apply, Action onApply, string ok, Action onOk)
        {
            var foot = new Grid { Margin = new Thickness(12f, 10f, 12f, 12f) };
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1f, GridUnitType.Star) });
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var lefts = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };   // the search and the revert of the tab (ToolRow) come first, then the mod's own button
            if (_tools != null) lefts.Children.Add(_tools);
            foot.Children.Add(lefts);
            status = Text("", WinStyle.Dim, 11f); status.HorizontalAlignment = HorizontalAlignment.Right; status.Margin = new Thickness(0f, 0f, 10f, 0f);
            Grid.SetColumn(status, 1); foot.Children.Add(status);
            var right = new StackPanel { Orientation = Orientation.Horizontal };
            if (left != null) right.Children.Add(left);                       // the mod's own extra button, next to Cancel (hidden when it has nothing to do)
            if (_revertTab != null) right.Children.Add(_revertTab);           // the revert of the tab: shown while the tab has edits
            right.Children.Add(Btn(cancel, onCancel, 70f));
            right.Children.Add(Btn(apply, onApply, 70f));
            var okb = Btn(ok, onOk, 70f); okb.Margin = new Thickness(0f);
            right.Children.Add(okb);
            Grid.SetColumn(right, 2); foot.Children.Add(right);
            return foot;
        }

        /// <summary>The parts one under the other: all as tall as they need, 'star' (the settings area) takes the rest of the window's height. Adds the resize handles.</summary>
        public void Assemble(Border win, IList<UIElement> parts, UIElement star)
        {
            var layout = new Grid();
            for (int r = 0; r < parts.Count; r++)
            {
                layout.RowDefinitions.Add(new RowDefinition { Height = ReferenceEquals(parts[r], star) ? new GridLength(1f, GridUnitType.Star) : GridLength.Auto });
                Grid.SetRow(parts[r], r);
                layout.Children.Add(parts[r]);
            }
            win.Child = layout;
            AddResizeHandles();
        }

        // ================================================================ drag, resize, placing

        public void Draggable(FrameworkElement handle)
        {
            bool down = false; Point start = new Point(); float h0 = 0f, v0 = 0f;
            handle.MouseLeftButtonDown += (o, e) =>
            {
                if (Move == null) return;
                down = true; start = e.GetPosition(Top); h0 = Move.X; v0 = Move.Y;
                handle.CaptureMouse(); e.Handled = true;
            };
            handle.MouseMove += (o, e) =>
            {
                if (!down || Move == null) return;
                var p = e.GetPosition(Top);
                Move.X = h0 + (p.X - start.X);
                Move.Y = v0 + (p.Y - start.Y);
            };
            handle.MouseLeftButtonUp += (o, e) => { if (!down) return; down = false; handle.ReleaseMouseCapture(); e.Handled = true; };
        }

        /// <summary>
        /// Resizing by the window's edges (5 px strips) and corners (16 px; the bottom right one shows the grip lines): Min..Max, the settings
        /// area takes the change. Dragging the left or top side keeps the opposite side in place. The size is kept while the game runs.
        /// </summary>
        public void AddResizeHandles()
        {
            for (int sy = -1; sy <= 1; sy++)
                for (int sx = -1; sx <= 1; sx++)
                    if (sx != 0 || sy != 0) Root.Children.Add(ResizeHandle(sx, sy));
        }

        /// <param name="sx">-1 left, 1 right, 0 neither</param>
        /// <param name="sy">-1 top, 1 bottom, 0 neither</param>
        FrameworkElement ResizeHandle(int sx, int sy)
        {
            const float Edge = 5f, Corner = 16f;
            bool corner = sx != 0 && sy != 0;
            var h = new Border { Background = WinStyle.Clear,
                                 HorizontalAlignment = sx < 0 ? HorizontalAlignment.Left : sx > 0 ? HorizontalAlignment.Right : HorizontalAlignment.Stretch,
                                 VerticalAlignment = sy < 0 ? VerticalAlignment.Top : sy > 0 ? VerticalAlignment.Bottom : VerticalAlignment.Stretch };
            if (sx != 0) h.Width = corner ? Corner : Edge;
            if (sy != 0) h.Height = corner ? Corner : Edge;
            if (sx == 0) h.Margin = new Thickness(Corner, 0f, Corner, 0f);        // the corners belong to the corner handles
            else if (sy == 0) h.Margin = new Thickness(0f, Corner, 0f, Corner);
            if (sx > 0 && sy > 0)
            {
                h.Margin = new Thickness(0f, 0f, 1f, 1f);
                h.Child = new Path { Data = new StreamGeometry("M15,4 L4,15 M15,8 L8,15 M15,12 L12,15"), Stroke = WinStyle.B(WinStyle.Dim), StrokeThickness = 1f };
                try { ToolTipService.SetToolTip(h, ResizeTip()); } catch { }
            }
            try { h.Cursor = corner ? (sx == sy ? Cursors.SizeNWSE : Cursors.SizeNESW) : sx != 0 ? Cursors.SizeWE : Cursors.SizeNS; } catch { }
            bool down = false; Point start = new Point(); float w0 = 0f, h0 = 0f;
            h.MouseLeftButtonDown += (o, e) =>
            {
                down = true; start = e.GetPosition(Top); w0 = W; h0 = H;
                h.CaptureMouse(); e.Handled = true;
            };
            h.MouseMove += (o, e) =>
            {
                if (!down || Root == null) return;
                e.Handled = true;   // while resizing the mouse events go no further
                var p = e.GetPosition(Top);
                float w = sx == 0 ? W : Math.Max(Math.Max(MinW, _rowW), Math.Min(MaxW, w0 + sx * (p.X - start.X)));
                float hh = sy == 0 ? H : Math.Max(MinH, Math.Min(MaxH, h0 + sy * (p.Y - start.Y)));
                float dw = w - W, dh = hh - H;
                if (Math.Abs(dw) < 0.5f && Math.Abs(dh) < 0.5f) return;
                W = w; H = hh;
                Root.Width = w; Root.Height = hh;
                if (Move == null) return;
                // a window centred in a Grid grows both ways; one placed by its top left corner grows right / down:
                // move it so that only the dragged side moves
                bool centred = Host is Grid;
                if (sx != 0) Move.X += centred ? sx * dw / 2f : (sx < 0 ? -dw : 0f);
                if (sy != 0) Move.Y += centred ? sy * dh / 2f : (sy < 0 ? -dh : 0f);
            };
            h.MouseLeftButtonUp += (o, e) => { if (!down) return; down = false; h.ReleaseMouseCapture(); e.Handled = true; };
            h.MouseWheel += (o, e) => { if (down) e.Handled = true; };
            h.MouseRightButtonDown += (o, e) => { if (down) e.Handled = true; };
            return h;
        }

        // The game scales its UI: the size is set in screen pixels at the first opening (converted to UI units, then kept), cut to the
        // screen less 40 px, and the window is centred on the screen, not in the panel it is put into.
        void OnLayout(object sender, Noesis.EventArgs e)
        {
            if (Root == null || _place >= 2) return;
            try
            {
                if (Root.ActualWidth <= 0f) return;
                var a = Root.PointToScreen(new Point(0f, 0f)); var b = Root.PointToScreen(new Point(100f, 0f));
                float k = (float)Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y)) / 100f;   // screen pixels per UI unit
                if (k <= 0.01f || float.IsNaN(k)) { _place = 2; return; }
                float sw = UnityEngine.Screen.width, sh = UnityEngine.Screen.height;
                if (_place == 0)
                {
                    if (_geom != null) { W = _geom[2] / k; H = _geom[3] / k; _sizedOnce = true; }
                    else if (!_sizedOnce) { W = PixW / k; H = PixH / k; _sizedOnce = true; }
                    if (_rowW > W) W = _rowW;   // the row of the mods holds up to 4 names and the language in one line
                    Root.Width = Math.Min(W, (sw - 40f) / k);
                    Root.Height = Math.Min(H, (sh - 40f) / k);
                    _place = 1; _k = k;
                    Trace("settings window: " + k.ToString("0.##") + " px per unit, " + Root.Width.ToString("0") + " x " + Root.Height.ToString("0") + " units");
                    return;   // laid out again with the new size
                }
                if (Trace != null) Trace("settings window: second layout pass, placing");
                float w = Root.Width * k, h = Root.Height * k;
                if (_geom != null) { Move.X += (_geom[0] - a.X) / k; Move.Y += (_geom[1] - a.Y) / k; }
                else { Move.X += ((sw - w) / 2f - a.X) / k; Move.Y += ((sh - h) / 2f - a.Y) / k; }
                _place = 2;
                Root.LayoutUpdated -= OnLayout;
            }
            catch (Exception ex) { _place = 2; Trace("settings window placing: " + ex.Message); }
        }


        // ================================================================ the row of the mods (switch between the windows of the mods)

        /// <summary>
        /// The names of the installed rgreat mods in a row, the current one lit; a click opens that mod's window here (SettingsHub.SwitchTo). A yellow dot
        /// marks a mod with edits not applied. Null when there is only one mod. UpdateModDots refreshes the dots (call it where the window's changed markers are updated).
        /// </summary>
        public FrameworkElement ModRow()
        {
            _modDots = null; _hasMods = _hasRow = false; _rowW = 0f;
            if (Id == null) return null;
            Rgreat.Hub.SettingsHub.MarkLast(Id);   // the window of this mod is on the screen: the menu and the gears open it next time
            List<object[]> mods;
            try { mods = Rgreat.Hub.SettingsHub.ActiveMods(); } catch { return null; }
            if (mods.Count < 2 && Languages == null) return null;
            _modDots = new List<KeyValuePair<object[], Ellipse>>();
            var bar = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };   // wraps when the window is narrow
            float need = 0f; int shown = 0;
            foreach (var m in mods.Count < 2 ? new List<object[]>() : mods)
            {
                var entry = m; string id = Rgreat.Hub.SettingsHub.IdOf(m); bool self = id == Id;
                var lab = new StackPanel { Orientation = Orientation.Horizontal };
                var dot = new Ellipse { Width = 6f, Height = 6f, Fill = WinStyle.B(WinStyle.Warn), Margin = new Thickness(0f, 0f, 6f, 0f), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
                lab.Children.Add(dot);
                var icon = ModIcon(id, self ? WinStyle.Text : WinStyle.Dim);
                if (icon != null) lab.Children.Add(icon);
                lab.Children.Add(new TextBlock { Text = Rgreat.Hub.SettingsHub.TitleOf(m), Foreground = WinStyle.B(self ? WinStyle.Text : WinStyle.Dim), FontSize = 13f,
                                                 VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap });
                var b = new Border { Child = lab, Padding = new Thickness(10f, 5f, 10f, 5f), Margin = new Thickness(0f, 0f, 4f, 0f), CornerRadius = new CornerRadius(3f),
                                     Background = WinStyle.B(self ? WinStyle.Hl : WinStyle.Panel), BorderBrush = WinStyle.B(self ? WinStyle.Dim : WinStyle.Line), BorderThickness = new Thickness(1f) };
                if (!self)
                {
                    try { b.Cursor = Cursors.Hand; } catch { }
                    b.MouseLeftButtonUp += (o, e) => { e.Handled = true; SwitchTo(id); };
                }
                bar.Children.Add(b);
                _modDots.Add(new KeyValuePair<object[], Ellipse>(entry, dot));
                if (++shown <= 4) { try { b.Measure(new Size(float.PositiveInfinity, float.PositiveInfinity)); need += b.DesiredSize.Width + 4f; } catch { need += 190f; } }
            }
            UpdateModDots();
            _hasMods = mods.Count >= 2; _hasRow = true;
            var row = new Grid { Margin = new Thickness(12f, 8f, 12f, 0f) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1f, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(bar);
            float langW = 0f;
            if (Languages != null)
            {
                var langs = Languages();
                var labels = new List<string>(); var codes = new List<string>();
                foreach (var l in langs) { codes.Add(l.Key); labels.Add(l.Value); }
                var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12f, 0f, 0f, 0f) };
                var cap = new TextBlock { Text = LanguageLabel == null ? "Language" : LanguageLabel(), Foreground = WinStyle.B(WinStyle.Dim), FontSize = 12f, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0f, 0f, 6f, 0f) };
                sp.Children.Add(cap);
                Action refresh;
                Func<int> cur = () => { int k = codes.IndexOf(Rgreat.Hub.SettingsHub.SharedLanguage()); return k < 0 ? 0 : k; };
                var cb = Combo(labels, cur,
                               // the box reports its first selection after the layout too: only a real change of the language rebuilds the window (else it is rebuilt without end)
                               k => { if (k == cur()) return; Rgreat.Hub.SettingsHub.SetLanguage(codes[k]); if (LanguageChanged != null) LanguageChanged(); }, 130f, 24f, out refresh);
                cb.HorizontalAlignment = HorizontalAlignment.Right;
                sp.Children.Add(cb);
                Grid.SetColumn(sp, 1);
                row.Children.Add(sp);
                try { cap.Measure(new Size(float.PositiveInfinity, float.PositiveInfinity)); langW = 12f + cap.DesiredSize.Width + 6f + 130f; } catch { langW = 230f; }
            }
            _rowW = need + langW + 24f + 2f;   // the names of up to 4 mods and the language in one line (+ the margins of the row and the border of the window)
            // a rebuild after the language changed: the names may be longer now - the window grows to hold the row (it is never made narrower: the user may have sized it)
            if (Root != null && _place >= 1 && _k > 0.01f)
            {
                float cap = (UnityEngine.Screen.width - 40f) / _k, want = Math.Min(_rowW, cap);
                if (!float.IsNaN(Root.Width) && Root.Width < want) { W = Math.Max(W, want); Root.Width = want; }
            }
            return row;
        }

        /// <summary>The languages of the drop-down at the right of the mods' row: (code, name), the first is ("", "Default"). Null: no drop-down.</summary>
        public Func<List<KeyValuePair<string, string>>> Languages;
        /// <summary>The caption of the drop-down.</summary>
        public Func<string> LanguageLabel;
        /// <summary>The language was changed (the mods already follow): the window rebuilds itself with the new texts.</summary>
        public Action LanguageChanged;
        float _k;             // screen pixels per UI unit (measured at the first layout)
        float _rowW;          // the width (UI units) the row of the mods needs for up to 4 mods and the language in one line
        bool _hasRow;

        bool _hasMods;
        StackPanel _tools;            // the search box and the revert button of the tab: the footer puts them at its left (ToolRow, Footer)

        /// <summary>A small line icon (drawn, 24 x 24 units) for a mod's name: Game Interface - windows, Weapons Control - crosshair, Damage Model - ship, Special Effects - spark.</summary>
        static FrameworkElement ModIcon(string id, Color c)
        {
            string d = null;
            switch (id)
            {
                case "alternative-game-interface": d = "M3,5 L21,5 L21,19 L3,19 Z M3,9 L21,9 M9,9 L9,19"; break;
                case "alternative-weapons-control": d = "M12,5 A7,7 0 1 1 11.99,5 M12,1 L12,8 M12,16 L12,23 M1,12 L8,12 M16,12 L23,12"; break;
                case "alternative-damage-model": d = "M2,14 L22,14 L18,20 L6,20 Z M8,14 L8,9 L15,9 L15,14 M11,9 L11,4"; break;
                case "alternative-special-effects": d = "M12,2 L14.5,9.5 L22,12 L14.5,14.5 L12,22 L9.5,14.5 L2,12 L9.5,9.5 Z"; break;
            }
            if (d == null) return null;
            try
            {
                return new Path { Data = new StreamGeometry(d), Stroke = WinStyle.B(c), StrokeThickness = 1.6f, Stretch = Stretch.Uniform, Width = 14f, Height = 14f,
                                  Margin = new Thickness(0f, 0f, 6f, 0f), VerticalAlignment = VerticalAlignment.Center };
            }
            catch { return null; }
        }

        /// <summary>The yellow dots on the mods' names: edits not applied.</summary>
        public void UpdateModDots()
        {
            if (_modDots == null) return;
            foreach (var kv in _modDots) { try { kv.Value.Visibility = Rgreat.Hub.SettingsHub.IsDirty(kv.Key) ? Visibility.Visible : Visibility.Collapsed; } catch { } }
        }

        void SwitchTo(string targetId)
        {
            try
            {
                float x, y, w, h;
                float[] rect = ShotFit.Rect(Root, out x, out y, out w, out h) ? new[] { x, y, w, h } : null;
                UIElement anchor = Host != null ? (UIElement)Host : Top;
                Rgreat.Hub.SettingsHub.CarryQuery(Query);   // the search text stays
                Rgreat.Hub.SettingsHub.SwitchTo(Id, targetId, anchor, rect);
            }
            catch (Exception ex) { Trace("switch to " + targetId + ": " + ex.Message); }
        }

        // ================================================================ tabs, search, revert

        /// <summary>The yellow dot of "edits not applied".</summary>
        public static Ellipse Dot(float right = 6f)
        {
            return new Ellipse { Width = 6f, Height = 6f, Fill = WinStyle.B(WinStyle.Warn), Margin = new Thickness(0f, 0f, right, 0f), VerticalAlignment = VerticalAlignment.Center };
        }

        /// <summary>A tab of the strip: [dot] name, lit when chosen. The dot is the first child of the Child StackPanel (it can be added / removed in place).</summary>
        public Border TabButton(string text, bool selected, bool changed, Action click)
        {
            var lab = new StackPanel { Orientation = Orientation.Horizontal };
            if (changed) lab.Children.Add(Dot());
            lab.Children.Add(Text(text, selected ? WinStyle.Text : WinStyle.Dim, 13f));
            var b = new Border { Child = lab, Padding = new Thickness(14f, 6f, 14f, 6f), Margin = new Thickness(0f, 0f, 3f, 0f),
                                 Background = WinStyle.B(selected ? WinStyle.Card : WinStyle.Title), BorderBrush = WinStyle.B(WinStyle.Line),
                                 BorderThickness = new Thickness(1f, 1f, 1f, 0f), CornerRadius = new CornerRadius(3f, 3f, 0f, 0f) };
            try { b.Cursor = Cursors.Hand; } catch { }
            b.MouseLeftButtonUp += (o, e) => { e.Handled = true; click(); };
            return b;
        }

        /// <summary>A page of a tab (the second row): [dot] name in a frame, lit when chosen.</summary>
        public Border PageButton(string text, bool selected, bool changed, Action click)
        {
            var lab = new StackPanel { Orientation = Orientation.Horizontal };
            if (changed) lab.Children.Add(Dot(5f));
            lab.Children.Add(Text(text, selected ? WinStyle.Text : WinStyle.Dim, 12f));
            var b = new Border { Child = lab, Padding = new Thickness(12f, 4f, 12f, 4f), Margin = new Thickness(0f, 0f, 4f, 0f),
                                 Background = WinStyle.B(selected ? WinStyle.Bg : WinStyle.Card), BorderBrush = WinStyle.B(selected ? WinStyle.Text : WinStyle.Line),
                                 BorderThickness = new Thickness(1f), CornerRadius = new CornerRadius(3f) };
            try { b.Cursor = Cursors.Hand; } catch { }
            b.MouseLeftButtonUp += (o, e) => { e.Handled = true; click(); };
            return b;
        }

        /// <summary>What is typed into the search box (trimmed); empty = no search.</summary>
        public string Query = "";
        Button _revertTab; Func<bool> _tabChanged;
        TextBox _searchBox;

        /// <summary>Does a row with these texts fit the search (every word of the query is in some of them)? Always true with no query.</summary>
        public bool Match(params string[] texts)
        {
            if (string.IsNullOrEmpty(Query)) return true;
            foreach (var w in Query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                bool hit = false;
                foreach (var t in texts) if (!string.IsNullOrEmpty(t) && t.IndexOf(w, StringComparison.CurrentCultureIgnoreCase) >= 0) { hit = true; break; }
                if (!hit) return false;
            }
            return true;
        }

        /// <summary>
        /// The row of the strip of tabs; the tools go to the footer (build the window's footer AFTER this): the search box (changed: the page is built again; Query is the text) and the button that takes
        /// the tab's edits back (not applied ones - the same as Cancel without closing). Call UpdateTools when the edits change.
        /// </summary>
        public Grid ToolRow(FrameworkElement strip, string hint, Action changed, Func<bool> tabChanged, Action revertTab, string revertTip)
        {
            // the strip of tabs (close under the row of the mods when there is one); the tools (search, revert of the tab) go to the footer, leftmost
            var row = new Grid { Margin = new Thickness(12f, _hasRow ? 6f : 10f, 12f, 0f) };
            strip.Margin = new Thickness(0f);
            row.Children.Add(strip);
            var tools = _tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0f, 0f, 10f, 0f) };

            // the text of the search: kept over a rebuild of the open window, brought from the window of the mod we switched from, else empty
            bool rebuilding = Host != null || (Popup != null && Popup.IsOpen);
            string start = Rgreat.Hub.SettingsHub.TakeQuery();
            if (rebuilding && string.IsNullOrEmpty(start)) start = Query ?? "";
            Query = start;
            var box = _searchBox = Edit(start, 170f);
            box.TextAlignment = TextAlignment.Left; box.Padding = new Thickness(6f, 0f, 22f, 0f);
            var clear = new Border { Width = 20f, Background = WinStyle.B(WinStyle.Field), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Stretch, Margin = new Thickness(0f, 1f, 1f, 1f),
                                     Child = WinStyle.Cross(8f), Visibility = start.Length > 0 ? Visibility.Visible : Visibility.Collapsed };
            try { clear.Cursor = Cursors.Hand; } catch { }
            clear.MouseLeftButtonUp += (o, e) => { e.Handled = true; box.Text = ""; };
            var hintText = new TextBlock { Text = Tr(hint), Foreground = WinStyle.B(WinStyle.Dim), FontSize = 12f, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8f, 0f, 0f, 0f) };
            if (start.Length > 0) hintText.Visibility = Visibility.Collapsed;
            var inner = new Grid();
            inner.Children.Add(box); inner.Children.Add(hintText); inner.Children.Add(clear);
            var holder = new Border { Child = inner, BorderBrush = WinStyle.B(WinStyle.Dim), BorderThickness = new Thickness(1f),
                                      Background = WinStyle.B(WinStyle.Field), Margin = new Thickness(0f, 0f, 6f, 0f) };   // framed: the box alone is hard to see on the dark window
            box.TextChanged += (o, e) =>
            {
                string q = (box.Text ?? "").Trim();
                hintText.Visibility = string.IsNullOrEmpty(box.Text) ? Visibility.Visible : Visibility.Collapsed;
                clear.Visibility = string.IsNullOrEmpty(box.Text) ? Visibility.Collapsed : Visibility.Visible;
                if (q == Query) return;
                Query = q;
                try { changed(); } catch (Exception ex) { Trace("search: " + ex.Message); }
            };
            tools.Children.Add(holder);

            _tabChanged = tabChanged;
            _revertTab = new Button { Content = "↺", Width = 30f, MinHeight = 26f, Padding = new Thickness(0f), Margin = new Thickness(0f, 0f, 6f, 0f), Visibility = Visibility.Collapsed };
            try { ToolTipService.SetToolTip(_revertTab, revertTip); } catch { }
            _revertTab.Click += (o, e) => { try { revertTab(); } catch (Exception ex) { Trace("revert tab: " + ex.Message); } };
            return row;
        }

        /// <summary>The revert button of the tab is shown while the tab has edits.</summary>
        public void UpdateTools()
        {
            try { if (_revertTab != null && _tabChanged != null) _revertTab.Visibility = _tabChanged() ? Visibility.Visible : Visibility.Collapsed; } catch { }
            foreach (var kv in _heads) { try { kv.Key.Visibility = kv.Value() ? Visibility.Visible : Visibility.Collapsed; } catch { } }
        }

        readonly List<KeyValuePair<Button, Func<bool>>> _heads = new List<KeyValuePair<Button, Func<bool>>>();

        /// <summary>The title of a group with the button at its right that takes the group's edits back (shown while 'changed' says so; UpdateTools refreshes it).</summary>
        public FrameworkElement GroupHeader(string title, Func<bool> changed, Action revert, string tip)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1f, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(WinStyle.GroupTitle(Tr(title)));
            var b = new Button { Content = "↺", Width = 24f, Height = 20f, Padding = new Thickness(0f), Margin = new Thickness(0f, 8f, 12f, 0f), VerticalAlignment = VerticalAlignment.Bottom,
                                 Visibility = Visibility.Collapsed };
            try { ToolTipService.SetToolTip(b, tip); } catch { }
            b.Click += (o, e) => { try { revert(); } catch (Exception ex) { Trace("revert group: " + ex.Message); } };
            Grid.SetColumn(b, 1);
            g.Children.Add(b);
            _heads.RemoveAll(kv => WinStyle.SafeParent(kv.Key) == null);   // of the page built before
            _heads.Add(new KeyValuePair<Button, Func<bool>>(b, changed));
            try { if (changed()) b.Visibility = Visibility.Visible; } catch { }
            return g;
        }

        /// <summary>A page is built again (a switch was clicked, the search changed): the scroll position stays where it was, no jump to the top.</summary>
        public void KeepScroll(ScrollViewer sv, Action rebuild)
        {
            float y = 0f;
            try { y = sv == null ? 0f : (float)sv.VerticalOffset; } catch { }
            rebuild();
            if (sv == null || y <= 0f) return;
            try { sv.UpdateLayout(); sv.ScrollToVerticalOffset(y); } catch { }
        }

        // ================================================================ rows

        /// <summary>The row lights up under the mouse and the help pane shows its name in bold and the text (and an extra line in yellow).</summary>
        public void Hover(Border row, Func<string> label, Func<string> text, Func<string> extra = null)
        {
            _hovers.Add(new KeyValuePair<Border, Action>(row, () => ShowHelp(label(), text(), extra == null ? null : extra())));
            if (_hovers.Count > 600) _hovers.RemoveAll(kv => !InWindow(kv.Key));   // rows of the pages built before
            row.MouseEnter += (o, e) =>
            {
                row.Background = WinStyle.B(WinStyle.Hl);
                string ex = extra == null ? null : extra();
                ShowHelp(label(), text(), ex);
                _copy = _helpPlain;   // Ctrl+C copies it while the mouse is over the row
            };
            row.MouseLeave += (o, e) => { row.Background = WinStyle.Clear; _copy = null; };
        }

        /// <summary>The ↺ button of a row (the default back).</summary>
        public Button ResetButton(string tip, Action click)
        {
            var rst = new Button { Content = "↺", Width = 24f, Height = 22f, Padding = new Thickness(0f), HorizontalAlignment = HorizontalAlignment.Right };
            try { ToolTipService.SetToolTip(rst, tip); } catch { }
            rst.Click += (o, e) => click();
            return rst;
        }

        /// <summary>
        /// A number: − / box / + (and the unit). shown: the text of the stored value; store: puts a value (already within min..max) in;
        /// stored: the stored number (null: the number in the box). commits: how typing is taken (null: on leaving the box and on Enter).
        /// </summary>
        public FrameworkElement Number(Func<string> shown, Func<float> stored, Action<float> store, float step, float min, float max, string unit,
                                       Action mark, out Action refresh, Action<TextBox, Action, Action> commits = null)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var box = Edit(shown(), 66f);
            Func<float> cur = stored ?? (() => WinStyle.Num(box.Text));
            Action<float> put = v =>
            {
                v = Math.Max(min, Math.Min(max, v));
                store(v);
                box.Text = shown();
                if (mark != null) mark();
            };
            var minus = new Button { Content = "−", Width = 24f, Height = 24f, Padding = new Thickness(0f) };
            var plus = new Button { Content = "+", Width = 24f, Height = 24f, Padding = new Thickness(0f) };
            minus.Click += (o, e) => put((float)Math.Round((cur() - step) / step) * step);
            plus.Click += (o, e) => put((float)Math.Round((cur() + step) / step) * step);
            // typed in: a number is taken (within the limits); anything else puts the value back as it was
            Action typed = () =>
            {
                float v;
                if (WinStyle.TryNum(box.Text, out v)) { if (stored == null || Math.Abs(v - cur()) > 1e-6f) put(v); else box.Text = shown(); }
                else box.Text = shown();
            };
            if (commits != null) commits(box, typed, () => box.Text = shown());
            else
            {
                box.LostFocus += (o, e) => typed();
                box.KeyDown += (o, e) => { if (e.Key == Key.Return) { typed(); e.Handled = true; } };
            }
            sp.Children.Add(minus); sp.Children.Add(box); sp.Children.Add(plus);
            if (!string.IsNullOrEmpty(unit)) { var u = Text(unit, WinStyle.Dim, 12f); u.Margin = new Thickness(8f, 0f, 0f, 0f); sp.Children.Add(u); }
            refresh = () => box.Text = shown();
            return sp;
        }

        /// <summary>A check box. on: the value; click: the user changed it.</summary>
        public CheckBox Check(Func<bool> on, Action<bool> click, out Action refresh)
        {
            var cb = new CheckBox { IsChecked = on(), VerticalAlignment = VerticalAlignment.Center };
            cb.Click += (o, e) => click(cb.IsChecked == true);
            refresh = () => cb.IsChecked = on();
            return cb;
        }

        /// <summary>A drop-down. index: the chosen item; pick: the user chose another one (not called by refresh).</summary>
        public ComboBox Combo(IList<string> labels, Func<int> index, Action<int> pick, float width, float minHeight, out Action refresh)
        {
            var cb = new ComboBox { Width = width, MinHeight = minHeight, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
                                    VerticalContentAlignment = VerticalAlignment.Center };
            foreach (var s in labels) cb.Items.Add(s);
            bool busy = false;
            cb.SelectedIndex = index();
            cb.SelectionChanged += (o, e) =>
            {
                if (busy || cb.SelectedIndex < 0) return;
                pick(cb.SelectedIndex);
            };
            refresh = () => { busy = true; try { cb.SelectedIndex = index(); } finally { busy = false; } };
            return cb;
        }
    }
}
