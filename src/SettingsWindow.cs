using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BepInEx.Configuration;
using Noesis;

namespace AlternativeWeaponsControl
{
    /// <summary>
    /// The mod's settings window, opened by the gear next to (i) on the "Weapons" tab button; built like Alternative Damage
    /// Model's (same look: title bar to drag, ✕, groups of rows, a help pane with the setting under the mouse, Cancel / Apply / OK),
    /// without profiles.
    ///  - Works on a copy: nothing changes until Apply or OK; then the .cfg is saved.
    ///  - A yellow dot marks a value changed and not applied yet (as in ADM: differs from the saved one); ↺ puts the default in;
    ///    Cancel drops what was not applied (the saved values come back).
    /// Noesis elements are built in code; buttons, text and check boxes get the game's own styles.
    /// While a text box has the keyboard, the game's hot keys are off (InputHandler.typingActive, as the game's own text boxes do).
    /// </summary>
    static class SettingsWindow
    {
        public static Action<string> Trace = s => { };
        public static Action<string> Warn = s => { };

        enum Kind { Number, Bool }

        sealed class Field
        {
            public string Group, Key, Label, Unit, Game;
            public Kind Kind;
            public float Step, Min, Max;
        }

        static readonly List<Field> Fields = new List<Field>();

        static void Add(string group, string key, string label, string unit, Kind kind, float step, float min, float max, string game = null)
        {
            Fields.Add(new Field { Group = group, Key = key, Label = label, Unit = unit, Kind = kind, Step = step, Min = min, Max = max, Game = game });
        }

        static SettingsWindow()
        {
            Add("Weapons tab", "UI/ClickVolume", "Click sound volume", "0..1 · 0 = no sound", Kind.Number, 0.1f, 0f, 1f);
            Add("Fire control", "FireControl/GunOneDirector", "Guns take one director", "", Kind.Bool, 0, 0, 0, "game: off");
            Add("Fire control", "FireControl/GunChannelLimit", "Guns without a free channel: local control", "", Kind.Bool, 0, 0, 0, "game: off");
            Add("Diagnostics", "General/Debug", "Debug trace", "", Kind.Bool, 0, 0, 0);
        }

        // ------------------------------------------------------------ colours (the game's dark panels)
        static readonly Color CBg = Color.FromRgb(30, 33, 38), CCard = Color.FromRgb(45, 50, 57),
                              CLine = Color.FromRgb(70, 76, 86), CText = Color.FromRgb(230, 232, 235), CDim = Color.FromRgb(150, 156, 162),
                              CWarn = Color.FromRgb(236, 212, 110), CTitle = Color.FromRgb(43, 48, 56), CField = Color.FromRgb(21, 24, 28),
                              CHl = Color.FromRgb(52, 58, 66);

        static SolidColorBrush B(Color c) { return new SolidColorBrush(c); }

        // ------------------------------------------------------------ state
        static Popup _popup;
        static UIElement _top;             // the screen the window is centred on (mouse positions for dragging)
        static Grid _root;
        static TextBlock _help, _status;
        static StackPanel _body;
        static Panel _host;                // the top panel of the screen the window was put into (null: shown in a Popup)
        static TranslateTransform _move;
        static readonly Dictionary<string, string> _work = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        static AwcPlugin P { get { return AwcPlugin.Instance; } }

        static ConfigEntryBase Entry(string key)
        {
            if (P == null) return null;
            foreach (var e in P.Entries) if (string.Equals(e.Definition.Section + "/" + e.Definition.Key, key, StringComparison.OrdinalIgnoreCase)) return e;
            return null;
        }

        public static bool IsOpen { get { return (_host != null && _root != null && SafeParent(_root) != null) || (_popup != null && _popup.IsOpen); } }

        // ------------------------------------------------------------ open / close

        /// <summary>Opens the window on the screen of 'anchor' (the gear clicked); 'other' when the gear's own screen has no panel for it.</summary>
        public static void Open(UIElement anchor, UIElement other)
        {
            if (anchor == null) { anchor = other; other = null; }
            if (P == null || anchor == null) { Warn("Settings window: not ready."); return; }
            try
            {
                if (IsOpen)
                {
                    bool shown = false;
                    try { shown = _root != null && _root.IsVisible; } catch { }
                    if (shown) return;
                }
                Close();
                DependencyObject top = anchor; Panel panel = null;
                foreach (var start in new[] { anchor, other })
                {
                    if (start == null || panel != null) continue;
                    for (DependencyObject p = start; p != null; p = SafeParent(p))
                    {
                        top = p;
                        var pn = p as Panel;
                        bool itemsHost = false;
                        try { itemsHost = pn != null && pn.IsItemsHost; } catch { }
                        if (pn != null && !itemsHost && (pn is Grid || pn is Canvas)) panel = pn;
                    }
                }
                _top = top as UIElement ?? anchor;
                Load();
                BuildWindow();
                _move = new TranslateTransform(0f, 0f);
                _place = 0;
                _root.LayoutUpdated += OnLayout;
                _root.RenderTransform = _move;
                if (panel != null)
                {
                    var grid = panel as Grid;
                    if (grid != null)
                    {
                        _root.HorizontalAlignment = HorizontalAlignment.Center;
                        _root.VerticalAlignment = VerticalAlignment.Center;
                        Grid.SetRow(_root, 0); Grid.SetColumn(_root, 0);
                        Grid.SetRowSpan(_root, Math.Max(1, grid.RowDefinitions.Count));
                        Grid.SetColumnSpan(_root, Math.Max(1, grid.ColumnDefinitions.Count));
                    }
                    else
                    {
                        float w = panel.ActualWidth > 0 ? panel.ActualWidth : 1280f, h = panel.ActualHeight > 0 ? panel.ActualHeight : 720f;
                        Canvas.SetLeft(_root, Math.Max(0f, (w - _root.Width) / 2f));
                        Canvas.SetTop(_root, Math.Max(0f, (h - _root.Height) / 2f));
                    }
                    Panel.SetZIndex(_root, 30000);
                    panel.Children.Add(_root);
                    _host = panel;
                }
                else
                {
                    _popup = new Popup { Child = _root, StaysOpen = true, AllowsTransparency = true, Placement = PlacementMode.Center, PlacementTarget = _top };
                    _popup.IsOpen = true;
                }
                Show();
            }
            catch (Exception e) { Warn("Settings window could not be opened: " + e.Message); Trace("SettingsWindow.Open: " + e); }
        }

        static DependencyObject SafeParent(DependencyObject e) { try { return VisualTreeHelper.GetParent(e); } catch { return null; } }

        static void Close()
        {
            Typing(false);
            try { var pp = _root == null ? null : SafeParent(_root) as Panel; if (pp != null) pp.Children.Remove(_root); } catch { }
            try { if (_popup != null) { _popup.IsOpen = false; _popup.Child = null; } } catch { }
            _host = null; _popup = null;
        }

        /// <summary>The .cfg was changed outside the window: the window shows it, unless there are unsaved edits.</summary>
        public static void Refresh()
        {
            if (!IsOpen || P == null || Dirty()) return;
            Load();
            Show();
        }

        static void Load()
        {
            _work.Clear();
            foreach (var e in P.Entries) _work[e.Definition.Section + "/" + e.Definition.Key] = e.GetSerializedValue();
        }

        static string Saved(Field f) { var e = Entry(f.Key); return e == null ? "" : e.GetSerializedValue(); }
        static string Default(Field f) { var e = Entry(f.Key); return e == null ? "" : TomlTypeConverter.ConvertToString(e.DefaultValue, e.SettingType); }
        static string Get(Field f) { string v; return _work.TryGetValue(f.Key, out v) ? v : ""; }

        static bool Same(string a, string b)
        {
            bool p, q;
            if (bool.TryParse((a ?? "").Trim(), out p) && bool.TryParse((b ?? "").Trim(), out q)) return p == q;
            float x, y;
            if (TryNum(a, out x) && TryNum(b, out y)) return Math.Abs(x - y) < 1e-5f;
            return string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
        }

        static bool Dirty() { foreach (var f in Fields) if (!Same(Get(f), Saved(f))) return true; return false; }

        // ------------------------------------------------------------ the game's keyboard while typing

        static PropertyInfo _ihInstance; static FieldInfo _ihTyping; static bool _ihTried;

        static void Typing(bool on)
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

        // ------------------------------------------------------------ building

        static TextBlock Text(string s, Color c, float size = 13f)
        {
            return new TextBlock { Text = s, Foreground = B(c), FontSize = size, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap };
        }

        static Button Btn(string s, Action click, float minW = 0f)
        {
            var b = new Button { Content = s, Padding = new Thickness(10f, 2f, 10f, 2f), Margin = new Thickness(0f, 0f, 6f, 0f), MinHeight = 26f, MinWidth = minW };
            b.Click += (o, e) => { try { click(); } catch (Exception ex) { Trace("button " + s + ": " + ex); } };
            return b;
        }

        // size in UI units (the game's UI scale applies): everything fits at once; kept while the game runs, at most the screen less 40 px
        const float WinW = 820f, WinH = 580f;
        static float _winW = WinW, _winH = WinH;
        static int _place;

        static void BuildWindow()
        {
            _root = new Grid { Width = _winW, Height = _winH };
            var win = new Border { Background = B(CBg), BorderBrush = B(CLine), BorderThickness = new Thickness(1f), CornerRadius = new CornerRadius(4f) };
            _root.Children.Add(win);
            var layout = new Grid();
            for (int r = 0; r < 4; r++) layout.RowDefinitions.Add(new RowDefinition { Height = r == 1 ? new GridLength(1f, GridUnitType.Star) : GridLength.Auto });
            win.Child = layout;

            // title bar: drag to move, ✕ to close
            var title = new Grid { Background = B(CTitle), Height = 32f };
            title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1f, GridUnitType.Star) });
            title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var tt = Text("Alternative Weapons Control - Settings", CText, 14f); tt.Margin = new Thickness(10f, 0f, 0f, 0f);
            title.Children.Add(tt);
            var x = new Button { Content = Cross(10f), Width = 28f, Height = 24f, Padding = new Thickness(0f), Margin = new Thickness(0f, 0f, 4f, 0f),
                                 HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            x.Click += (o, e) => Close();
            Grid.SetColumn(x, 1);
            title.Children.Add(x);
            Draggable(title);
            layout.Children.Add(title);

            _body = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0f, 6f, 0f, 6f) };
            var scroll = new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            var area = new Border { Child = scroll, Background = B(CCard), BorderBrush = B(CLine), BorderThickness = new Thickness(1f), Margin = new Thickness(12f, 12f, 12f, 0f) };
            Grid.SetRow(area, 1); layout.Children.Add(area);

            _help = new TextBlock { Foreground = B(Color.FromRgb(194, 199, 204)), FontSize = 12f, TextWrapping = TextWrapping.Wrap, Text = "Point at a setting to see what it does." };
            var hb = new Border { Child = _help, Background = B(Color.FromRgb(34, 38, 44)), BorderBrush = B(CLine), BorderThickness = new Thickness(1f),
                                  Padding = new Thickness(10f, 8f, 10f, 8f), Margin = new Thickness(12f, 8f, 12f, 0f), Height = 108f, CornerRadius = new CornerRadius(3f) };
            Grid.SetRow(hb, 2); layout.Children.Add(hb);

            var foot = new Grid { Margin = new Thickness(12f, 10f, 12f, 12f) };
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1f, GridUnitType.Star) });
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _status = Text("", CDim, 11f); _status.HorizontalAlignment = HorizontalAlignment.Right; _status.Margin = new Thickness(0f, 0f, 10f, 0f);
            Grid.SetColumn(_status, 1); foot.Children.Add(_status);
            var right = new StackPanel { Orientation = Orientation.Horizontal };
            right.Children.Add(Btn("Cancel", () => { Load(); Close(); }, 70f));   // the saved values back: edits not applied are dropped
            right.Children.Add(Btn("Apply", () => Commit(false), 70f));
            var ok = Btn("OK", () => Commit(true), 70f); ok.Margin = new Thickness(0f);
            right.Children.Add(ok);
            Grid.SetColumn(right, 2); foot.Children.Add(right);
            Grid.SetRow(foot, 3); layout.Children.Add(foot);

            AddResizeHandles();
        }

        /// <summary>
        /// Resizing by the window's edges (5 px strips) and corners (16 px; the bottom right one shows the grip lines), as in
        /// Alternative Damage Model: 560..1600 x 420..1400, the settings area takes the change. Dragging the left or top side keeps
        /// the opposite side in place. The size is kept while the game runs.
        /// </summary>
        static void AddResizeHandles()
        {
            for (int sy = -1; sy <= 1; sy++)
                for (int sx = -1; sx <= 1; sx++)
                    if (sx != 0 || sy != 0) _root.Children.Add(ResizeHandle(sx, sy));
        }

        /// <param name="sx">-1 left, 1 right, 0 neither</param>
        /// <param name="sy">-1 top, 1 bottom, 0 neither</param>
        static FrameworkElement ResizeHandle(int sx, int sy)
        {
            const float Edge = 5f, Corner = 16f;
            bool corner = sx != 0 && sy != 0;
            var h = new Border { Background = B(Color.FromArgb(0, 0, 0, 0)),
                                 HorizontalAlignment = sx < 0 ? HorizontalAlignment.Left : sx > 0 ? HorizontalAlignment.Right : HorizontalAlignment.Stretch,
                                 VerticalAlignment = sy < 0 ? VerticalAlignment.Top : sy > 0 ? VerticalAlignment.Bottom : VerticalAlignment.Stretch };
            if (sx != 0) h.Width = corner ? Corner : Edge;
            if (sy != 0) h.Height = corner ? Corner : Edge;
            if (sx == 0) h.Margin = new Thickness(Corner, 0f, Corner, 0f);        // the corners belong to the corner handles
            else if (sy == 0) h.Margin = new Thickness(0f, Corner, 0f, Corner);
            if (sx > 0 && sy > 0)
            {
                h.Margin = new Thickness(0f, 0f, 1f, 1f);
                h.Child = new Path { Data = new StreamGeometry("M15,4 L4,15 M15,8 L8,15 M15,12 L12,15"), Stroke = B(CDim), StrokeThickness = 1f };
                try { ToolTipService.SetToolTip(h, "Drag to resize"); } catch { }
            }
            try { h.Cursor = corner ? (sx == sy ? Cursors.SizeNWSE : Cursors.SizeNESW) : sx != 0 ? Cursors.SizeWE : Cursors.SizeNS; } catch { }
            bool down = false; Point start = new Point(); float w0 = 0f, h0 = 0f;
            h.MouseLeftButtonDown += (o, e) =>
            {
                down = true; start = e.GetPosition(_top); w0 = _winW; h0 = _winH;
                h.CaptureMouse(); e.Handled = true;
            };
            h.MouseMove += (o, e) =>
            {
                if (!down || _root == null) return;
                var p = e.GetPosition(_top);
                float w = sx == 0 ? _winW : Math.Max(560f, Math.Min(1600f, w0 + sx * (p.X - start.X)));
                float hh = sy == 0 ? _winH : Math.Max(420f, Math.Min(1400f, h0 + sy * (p.Y - start.Y)));
                float dw = w - _winW, dh = hh - _winH;
                if (Math.Abs(dw) < 0.5f && Math.Abs(dh) < 0.5f) return;
                _winW = w; _winH = hh;
                _root.Width = w; _root.Height = hh;
                if (_move == null) return;
                // a window centred in a Grid grows both ways; one placed by its top left corner grows right / down:
                // move it so that only the dragged side moves
                bool centred = _host is Grid;
                if (sx != 0) _move.X += centred ? sx * dw / 2f : (sx < 0 ? -dw : 0f);
                if (sy != 0) _move.Y += centred ? sy * dh / 2f : (sy < 0 ? -dh : 0f);
            };
            h.MouseLeftButtonUp += (o, e) => { if (!down) return; down = false; h.ReleaseMouseCapture(); e.Handled = true; };
            return h;
        }

        static Path Cross(float size)
        {
            return new Path { Data = new StreamGeometry("M0,0 L10,10 M10,0 L0,10"), Stroke = B(CText), StrokeThickness = 1.5f, Stretch = Stretch.Uniform,
                              Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }

        static void Draggable(FrameworkElement handle)
        {
            bool down = false; Point start = new Point(); float h0 = 0f, v0 = 0f;
            handle.MouseLeftButtonDown += (o, e) =>
            {
                if (_move == null) return;
                down = true; start = e.GetPosition(_top); h0 = _move.X; v0 = _move.Y;
                handle.CaptureMouse(); e.Handled = true;
            };
            handle.MouseMove += (o, e) =>
            {
                if (!down || _move == null) return;
                var p = e.GetPosition(_top);
                _move.X = h0 + (p.X - start.X);
                _move.Y = v0 + (p.Y - start.Y);
            };
            handle.MouseLeftButtonUp += (o, e) => { if (!down) return; down = false; handle.ReleaseMouseCapture(); e.Handled = true; };
        }

        // The game scales its UI: the size (UI units) is cut to the screen less 40 px and the window is centred on the screen.
        static void OnLayout(object sender, Noesis.EventArgs e)
        {
            if (_root == null || _place >= 2) return;
            try
            {
                if (_root.ActualWidth <= 0f) return;
                var a = _root.PointToScreen(new Point(0f, 0f)); var b = _root.PointToScreen(new Point(100f, 0f));
                float k = (float)Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y)) / 100f;
                if (k <= 0.01f || float.IsNaN(k)) { _place = 2; return; }
                float sw = UnityEngine.Screen.width, sh = UnityEngine.Screen.height;
                if (_place == 0)
                {
                    _root.Width = Math.Min(_winW, (sw - 40f) / k);
                    _root.Height = Math.Min(_winH, (sh - 40f) / k);
                    _place = 1;
                    return;
                }
                float w = _root.Width * k, h = _root.Height * k;
                _move.X += ((sw - w) / 2f - a.X) / k;
                _move.Y += ((sh - h) / 2f - a.Y) / k;
                _place = 2;
                _root.LayoutUpdated -= OnLayout;
            }
            catch (Exception ex) { _place = 2; Trace("settings window placing: " + ex.Message); }
        }

        // ------------------------------------------------------------ rows

        static void Show()
        {
            if (_body == null) return;
            _body.Children.Clear();
            string group = null;
            foreach (var f in Fields)
            {
                if (f.Group != group) { group = f.Group; _body.Children.Add(GroupTitle(group)); }
                _body.Children.Add(Row(f));
            }
            UpdateStatus();
        }

        static void UpdateStatus()
        {
            if (_status == null) return;
            bool any = false;
            foreach (var f in Fields) if (!Same(Get(f), Saved(f))) { any = true; break; }
            _status.Text = any ? "● changed, not applied yet   ↺ = back to the default" : "";
        }

        static TextBlock GroupTitle(string s)
        {
            return new TextBlock { Text = s.ToUpperInvariant(), Foreground = B(CDim), FontSize = 11f, Margin = new Thickness(12f, 10f, 12f, 2f) };
        }

        static FrameworkElement Row(Field f)
        {
            var g = new Grid { Background = B(Color.FromArgb(0, 0, 0, 0)) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330f) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1f, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110f) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30f) });
            var outer = new Border { Child = g, Padding = new Thickness(12f, 4f, 6f, 4f), Background = B(Color.FromArgb(0, 0, 0, 0)) };

            var lab = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var dot = new Ellipse { Width = 6f, Height = 6f, Fill = B(CWarn), Margin = new Thickness(0f, 0f, 6f, 0f), VerticalAlignment = VerticalAlignment.Center };
            lab.Children.Add(dot);
            lab.Children.Add(Text(f.Label, CText));
            g.Children.Add(lab);

            Action mark = () => { dot.Visibility = Same(Get(f), Saved(f)) ? Visibility.Hidden : Visibility.Visible; UpdateStatus(); };
            Action refresh;
            var editor = f.Kind == Kind.Bool ? BoolEditor(f, mark, out refresh) : NumberEditor(f, mark, out refresh);
            Grid.SetColumn(editor, 1);
            g.Children.Add(editor);

            string d = Default(f).Replace("True", "on").Replace("False", "off").Replace("true", "on").Replace("false", "off");
            var game = new TextBlock { Text = "default: " + d + (f.Game != null ? "\n" + f.Game : ""), Foreground = B(CDim), FontSize = 11f, TextAlignment = TextAlignment.Right,
                                       VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(game, 2);
            g.Children.Add(game);

            var rst = new Button { Content = "↺", Width = 24f, Height = 22f, Padding = new Thickness(0f), HorizontalAlignment = HorizontalAlignment.Right };
            try { ToolTipService.SetToolTip(rst, "Back to the default"); } catch { }
            rst.Click += (o, e) => { _work[f.Key] = Default(f); refresh(); mark(); };
            Grid.SetColumn(rst, 3);
            g.Children.Add(rst);

            mark();
            outer.MouseEnter += (o, e) =>
            {
                outer.Background = B(CHl);
                var en = Entry(f.Key);
                _help.Inlines.Clear();
                var r = new Run(f.Label); r.FontWeight = FontWeight.Bold; r.Foreground = B(CText);
                _help.Inlines.Add(r);
                _help.Inlines.Add(new Run(" - " + (en != null && en.Description != null ? en.Description.Description : "")));
            };
            outer.MouseLeave += (o, e) => outer.Background = B(Color.FromArgb(0, 0, 0, 0));
            return outer;
        }

        static TextBox Edit(string text, float width)
        {
            var t = new TextBox { Text = text ?? "", Width = width, Height = 24f, TextAlignment = TextAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
                                  Background = B(CField), Foreground = B(CText), BorderBrush = B(CLine), Padding = new Thickness(2f, 0f, 2f, 0f) };
            t.GotFocus += (s, e) => { Typing(true); try { t.SelectAll(); } catch { } };
            t.LostFocus += (s, e) => Typing(false);
            return t;
        }

        static bool TryNum(string s, out float v)
        {
            return float.TryParse((s ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && !float.IsNaN(v) && !float.IsInfinity(v);
        }

        static float Num(string s) { float v; return TryNum(s, out v) ? v : 0f; }

        static FrameworkElement NumberEditor(Field f, Action mark, out Action refresh)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var box = Edit(Get(f), 66f);
            Action<float> put = v =>
            {
                v = Math.Max(f.Min, Math.Min(f.Max, v));
                string s = v.ToString("0.##", CultureInfo.InvariantCulture);
                box.Text = s;
                _work[f.Key] = s;
                mark();
            };
            var minus = new Button { Content = "−", Width = 24f, Height = 24f, Padding = new Thickness(0f) };
            var plus = new Button { Content = "+", Width = 24f, Height = 24f, Padding = new Thickness(0f) };
            minus.Click += (o, e) => put((float)Math.Round((Num(box.Text) - f.Step) / f.Step) * f.Step);
            plus.Click += (o, e) => put((float)Math.Round((Num(box.Text) + f.Step) / f.Step) * f.Step);
            Action typed = () => { float v; if (TryNum(box.Text, out v)) put(v); else box.Text = Get(f); };
            box.LostFocus += (o, e) => typed();
            box.KeyDown += (o, e) => { if (e.Key == Key.Return) { typed(); e.Handled = true; } };
            sp.Children.Add(minus); sp.Children.Add(box); sp.Children.Add(plus);
            if (!string.IsNullOrEmpty(f.Unit)) { var u = Text(f.Unit, CDim, 12f); u.Margin = new Thickness(8f, 0f, 0f, 0f); sp.Children.Add(u); }
            refresh = () => box.Text = Get(f);
            return sp;
        }

        static FrameworkElement BoolEditor(Field f, Action mark, out Action refresh)
        {
            var cb = new CheckBox { IsChecked = Get(f).Trim().Equals("true", StringComparison.OrdinalIgnoreCase), VerticalAlignment = VerticalAlignment.Center };
            cb.Click += (o, e) => { _work[f.Key] = cb.IsChecked == true ? "true" : "false"; mark(); };
            refresh = () => cb.IsChecked = Get(f).Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
            return cb;
        }

        static void Commit(bool close)
        {
            Typing(false);
            foreach (var f in Fields)
            {
                var e = Entry(f.Key);
                if (e == null || Same(Get(f), e.GetSerializedValue())) continue;
                try { e.SetSerializedValue(Get(f)); } catch (Exception ex) { Trace(f.Key + ": " + ex.Message); }
            }
            try { P.Cfg.Save(); P.RememberCfgStamp(); } catch (Exception ex) { Warn("Saving the settings failed: " + ex.Message); }
            if (close) { Close(); return; }
            Load();
            Show();
        }
    }
}
