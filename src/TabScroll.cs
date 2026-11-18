// ======================================================================================================================
// rgreat settings kit - THE SAME FILE IN EVERY rgreat MOD (Alternative Game Interface, Alternative Weapons Control,
// Alternative Damage Model, Alternative Special Effects). The canonical copy is src/TabScroll.cs of the SeaPower.AlternativeBase
// repository; change it there and copy it to the mods with tools/sync.sh, never edit a copy inside a mod.
//
// TabScroll: the strip of tabs of a settings window as ONE line. The tabs (a horizontal Panel the mod fills and recolours itself) sit in a
// ScrollViewer without scroll bars; a button with an arrow at each end scrolls it, the wheel over the strip too. The buttons are visible only
// while the tabs do not fit. The chosen tab is kept in view (Reveal): when it is chosen, when the window gets smaller, when the buttons
// appear or go (they take part of the strip).
// ======================================================================================================================
using System;
using Noesis;

namespace Rgreat.Kit
{
    internal sealed class TabScroll
    {
        /// <summary>The element to put into the window in place of the panel of tabs: [ ◀ | scrolled tabs | ▶ ].</summary>
        public readonly Grid Root;

        readonly ScrollViewer _sv;
        readonly Panel _tabs;
        readonly Border _left, _right;
        FrameworkElement _target;
        bool _pending;
        int _try;
        float _vp, _ext;

        /// <param name="tabs">The panel of the tab buttons (horizontal StackPanel). Its children may be rebuilt at any time; call Reveal after that.</param>
        /// <param name="panel">Background of the arrow buttons (the colour of an unchosen tab).</param>
        /// <param name="line">Border colour of the arrow buttons.</param>
        /// <param name="text">Colour of the arrows.</param>
        public TabScroll(Panel tabs, Brush panel, Brush line, Brush text)
        {
            _tabs = tabs;
            _sv = new ScrollViewer { Content = tabs, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
            _left = Arrow("◀", -1, panel, line, text); _right = Arrow("▶", 1, panel, line, text);
            _right.Margin = new Thickness(0f);
            Root = new Grid();
            Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1f, GridUnitType.Star) });
            Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(_left, 0); Grid.SetColumn(_sv, 1); Grid.SetColumn(_right, 2);
            Root.Children.Add(_left); Root.Children.Add(_sv); Root.Children.Add(_right);
            _sv.ScrollChanged += (o, e) => { Arrows(); Step(); };
            _sv.SizeChanged += (o, e) => { Arrows(); Ask(); };
            _sv.LayoutUpdated += (o, e) =>
            {
                // the visible part of the strip or the tabs' total width changed (the arrows appeared / went, the window or a tab changed its size): the chosen tab is brought into view again
                if (Math.Abs((float)_sv.ViewportWidth - _vp) > 0.5f || Math.Abs((float)_sv.ExtentWidth - _ext) > 0.5f) { _vp = (float)_sv.ViewportWidth; _ext = (float)_sv.ExtentWidth; Ask(); }
                else Step();
            };
            _sv.MouseWheel += (o, e) =>
            {
                if (_sv.ScrollableWidth < 1f) return;
                Scroll(-e.Delta * 0.5f); e.Handled = true;
            };
        }

        Border Arrow(string glyph, int dir, Brush panel, Brush line, Brush text)
        {
            var b = new Border
            {
                Child = new TextBlock { Text = glyph, Foreground = text, FontSize = 13f }, Padding = new Thickness(8f, 6f, 8f, 6f), Margin = new Thickness(0f, 0f, 2f, 0f),
                Background = panel, BorderBrush = line, BorderThickness = new Thickness(1f, 1f, 1f, 0f), CornerRadius = new CornerRadius(3f, 3f, 0f, 0f), Visibility = Visibility.Collapsed
            };
            try { b.Cursor = Cursors.Hand; } catch { }
            b.MouseLeftButtonUp += (o, e) => { Scroll(dir * 160f); e.Handled = true; };
            return b;
        }

        void Scroll(float delta) { _sv.ScrollToHorizontalOffset(Math.Max(0f, Math.Min((float)_sv.ScrollableWidth, (float)_sv.HorizontalOffset + delta))); }

        /// <summary>The arrows are visible only while the tabs do not fit; the one at the end of the way is dimmed.</summary>
        void Arrows()
        {
            bool over = _sv.ScrollableWidth > 0.5f;
            _left.Visibility = over ? Visibility.Visible : Visibility.Collapsed;
            _right.Visibility = over ? Visibility.Visible : Visibility.Collapsed;
            _left.Opacity = _sv.HorizontalOffset > 0.5f ? 1f : 0.35f;
            _right.Opacity = _sv.HorizontalOffset < _sv.ScrollableWidth - 0.5f ? 1f : 0.35f;
        }

        /// <summary>The tab to keep in view (the chosen one). Call when it is chosen and after the buttons of the strip were rebuilt.</summary>
        public void Reveal(FrameworkElement tab)
        {
            _target = tab; _pending = true; _try = 0;
            Step();
        }

        void Ask() { if (_target != null) { _pending = true; _try = 0; } }

        /// <summary>Scrolls only as far as needed to see the whole tab. Done when the widths are valid (from the layout events) and repeated until the strip really stands there
        /// (a scroll made in the middle of a layout pass is lost).</summary>
        void Step()
        {
            if (!_pending) return;
            try
            {
                var cur = _target;
                if (cur == null) { _pending = false; return; }
                if (_sv.ViewportWidth < 1f || cur.ActualWidth < 1f) return;                       // not laid out yet
                if (_sv.ScrollableWidth < 0.5f) { _pending = false; return; }                     // all tabs fit
                float left = 0f; bool found = false;
                for (int i = 0; i < _tabs.Children.Count; i++)
                {
                    var c = _tabs.Children[i] as FrameworkElement;
                    if (c == null) continue;
                    if (ReferenceEquals(c, cur)) { found = true; break; }
                    if (c.Visibility == Visibility.Visible) left += (float)c.ActualWidth + (float)c.Margin.Left + (float)c.Margin.Right;
                }
                if (!found) { _pending = false; return; }
                float right = left + (float)cur.ActualWidth, off = (float)_sv.HorizontalOffset, vp = (float)_sv.ViewportWidth;
                float to = left < off ? left : right > off + vp ? right - vp : off;
                to = Math.Max(0f, Math.Min((float)_sv.ScrollableWidth, to));
                if (Math.Abs(to - off) <= 0.5f) { _pending = false; return; }                     // the tab is in view
                if (++_try > 40) { _pending = false; return; }
                _sv.ScrollToHorizontalOffset(to);
            }
            catch { _pending = false; }
        }
    }
}
