using System;
using System.Runtime.CompilerServices;
using Noesis;

namespace AlternativeWeaponsControl
{
    /// <summary>
    /// The "Weapons" tab button of Unit Status: "Weapons" + (i) with the legend (tooltip) + the gear (the settings window), as
    /// Alternative Damage Model decorates its "Damage Control" button. The tab buttons are RadioButtons bound to
    /// UnitStatusTabViewModel (Header, Content); ours is the one whose DataContext is the mod's tab. Its text block (made by the
    /// button's ContentPresenter) gets the icons as inlines; kept when the game rewrites the text.
    /// </summary>
    public partial class AwcPlugin
    {
        sealed class HeaderState { public string Shown, Text; public UIElement Anchor; }
        static readonly ConditionalWeakTable<TextBlock, HeaderState> Headers = new ConditionalWeakTable<TextBlock, HeaderState>();

        void HeaderStep(FrameworkElement usv, ModTab mt, ViewState vs, float now)
        {
            if (vs.Header != null && VisualTreeHelper.GetParent(vs.Header) != null) { DecorateHeader(vs.Header, usv); return; }
            if (now < vs.NextScan) return;
            vs.NextScan = now + 0.5f;
            vs.Header = null;
            var stack = new System.Collections.Generic.Stack<DependencyObject>();
            stack.Push(usv);
            int budget = 8000;
            while (stack.Count > 0 && --budget > 0)
            {
                var e = stack.Pop();
                var rb = e as RadioButton;
                if (rb != null)
                {
                    object dc = null; try { dc = rb.DataContext; } catch { }
                    if (ReferenceEquals(dc, mt.Tab))
                    {
                        vs.Header = FirstText(rb);
                        break;
                    }
                    continue;
                }
                int n = VisualTreeHelper.GetChildrenCount(e);
                for (int k = n - 1; k >= 0; k--) { var c = VisualTreeHelper.GetChild(e, k); if (c != null) stack.Push(c); }
            }
            if (vs.Header != null) DecorateHeader(vs.Header, usv);
        }

        static TextBlock FirstText(DependencyObject root)
        {
            var stack = new System.Collections.Generic.Stack<DependencyObject>();
            stack.Push(root);
            int budget = 50;
            while (stack.Count > 0 && --budget > 0)
            {
                var e = stack.Pop();
                var t = e as TextBlock;
                if (t != null) return t;
                int n = VisualTreeHelper.GetChildrenCount(e);
                for (int k = n - 1; k >= 0; k--) { var c = VisualTreeHelper.GetChild(e, k); if (c != null) stack.Push(c); }
            }
            return null;
        }

        static void DecorateHeader(TextBlock tb, UIElement anchor)
        {
            if (tb == null) return;
            var hs = Headers.GetOrCreateValue(tb);
            if (anchor != null) hs.Anchor = anchor;
            if (hs.Shown != null && tb.Text == hs.Shown) return;
            string text = tb.Text ?? "";
            if (hs.Text != null && text.StartsWith(hs.Text + "  ", StringComparison.Ordinal)) { hs.Shown = text; return; }   // still ours
            var il = tb.Inlines;
            il.Clear();
            il.Add(new Run(text + "  "));
            // (i) and the gear side by side in ONE inline container: a horizontal panel, both icons 14 px, 6 px apart; 3 px down (an
            // inline element stands on the baseline); the negative top margin keeps the line from growing
            var info = HeaderIcon(InfoIcon());
            try
            {
                ToolTipService.SetToolTip(info, LegendTip());
                ToolTipService.SetPlacement(info, PlacementMode.Top);
                ToolTipService.SetShowDuration(info, int.MaxValue);
            }
            catch (Exception e) { Trace("legend: " + e.Message); }
            var gear = HeaderIcon(GearIcon());
            gear.Margin = new Thickness(6f, 0f, 0f, 0f);
            try { gear.Cursor = Cursors.Hand; ToolTipService.SetToolTip(gear, Loc.T("tab.gearTip")); } catch { }
            gear.PreviewMouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                Trace("settings gear clicked");
                SettingsWindow.Open(gear, hs.Anchor);
            };
            var pair = new StackPanel { Orientation = Orientation.Horizontal, RenderTransform = new TranslateTransform(0f, 3f), Margin = new Thickness(0f, -6f, 0f, 0f) };
            pair.Children.Add(info);
            pair.Children.Add(gear);
            il.Add(new InlineUIContainer(pair));
            hs.Shown = tb.Text;
            hs.Text = text;
        }

        static FrameworkElement LegendTip()
        {
            return new Border { Child = Legend(), Padding = new Thickness(4f) };
        }

        static Border HeaderIcon(FrameworkElement icon)
        {
            var b = new Border { Child = icon, Width = 14f, Height = 14f, VerticalAlignment = VerticalAlignment.Center,
                                 Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)), Opacity = 0.9f };
            b.MouseEnter += (s, e) => b.Opacity = 1f;
            b.MouseLeave += (s, e) => b.Opacity = 0.9f;
            return b;
        }

        static FrameworkElement GearIcon()
        {
            return new Path
            {
                Data = new StreamGeometry("F0 M10.3,2 L13.7,2 L14.2,4.6 C14.8,4.8 15.4,5.1 15.9,5.5 L18.4,4.6 L20.1,7.5 L18.1,9.2 C18.2,9.8 18.2,10.4 18.1,11 " +
                                          "L20.1,12.7 L18.4,15.6 L15.9,14.7 C15.4,15.1 14.8,15.4 14.2,15.6 L13.7,18.2 L10.3,18.2 L9.8,15.6 C9.2,15.4 8.6,15.1 8.1,14.7 " +
                                          "L5.6,15.6 L3.9,12.7 L5.9,11 C5.8,10.4 5.8,9.8 5.9,9.2 L3.9,7.5 L5.6,4.6 L8.1,5.5 C8.6,5.1 9.2,4.8 9.8,4.6 Z " +
                                          "M15.4,10.1 A3.4,3.4 0 1 1 8.6,10.1 A3.4,3.4 0 1 1 15.4,10.1 Z"),
                Fill = new SolidColorBrush(Color.FromRgb(170, 176, 184)), Stretch = Stretch.Uniform, Width = 14f, Height = 14f
            };
        }

        static FrameworkElement InfoIcon()
        {
            var cv = new Canvas { Width = 14f, Height = 14f };
            cv.Children.Add(new Path { Data = new StreamGeometry("F0 M7,0 A7,7 0 1 1 7,14 A7,7 0 1 1 7,0 Z M6,6 L8,6 L8,11.5 L6,11.5 Z M7,2.5 A1.2,1.2 0 1 1 7,4.9 A1.2,1.2 0 1 1 7,2.5 Z"),
                                       Fill = new SolidColorBrush(Color.FromRgb(86, 160, 232)) });
            return cv;
        }
    }
}
