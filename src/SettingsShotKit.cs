// ======================================================================================================================
// rgreat settings kit - THE SAME FILE IN EVERY rgreat MOD. The canonical copy is src/SettingsShotKit.cs of the
// SeaPower.AlternativeBase repository; change it there and copy it with tools/sync.sh, never edit a copy inside a mod.
//
// Screenshots of the settings window (a debug tool): the window goes through every tab (and every page of a tab that has pages), is fitted to
// the tab - the smallest window in which all the options of the tab are seen - a screenshot of the window alone is taken at the end of a frame and
// written as a PNG (01_Windows.png, 02_Desktop_mode.png ...) to a folder of the mod, and the window is put back as it was. Nothing is applied or changed.
// The mod implements IShotWindow for its window (a few lines over what it already has) and starts the coroutine: StartCoroutine(SettingsShots.Run(...)).
// ======================================================================================================================
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using Noesis;

namespace Rgreat.Kit
{
    /// <summary>What the screenshot tool needs from a settings window.</summary>
    internal interface IShotWindow
    {
        /// <summary>The window is open; remembers what must be put back (the chosen tab, "show all"...). False = nothing to do.</summary>
        bool Begin();
        int TabCount { get; }
        /// <summary>False for a tab the window does not offer now (a tab of a mod part that is off).</summary>
        bool TabUsable(int tab);
        /// <summary>The number of pages of the tab (1 = no pages).</summary>
        int Pages(int tab);
        void Show(int tab, int page);
        string Name(int tab, int page);
        /// <summary>The window's rectangle in screen pixels (top left origin).</summary>
        bool Rect(out float x, out float y, out float w, out float h);
        void FitMax();
        void FitWidth();
        void FitHeight();
        /// <summary>The window as it was: its own size, in the middle of the screen, the tab that was chosen.</summary>
        void End();
    }

    /// <summary>Fitting a settings window to its tab: the helpers for IShotWindow.FitMax / FitWidth / FitHeight / End. The window is a Grid with a fixed Width / Height
    /// (UI units) and a TranslateTransform that moves it; the options are in a StackPanel inside a ScrollViewer.</summary>
    internal static class ShotFit
    {
        /// <summary>Screen pixels per UI unit of the element.</summary>
        public static float K(FrameworkElement root)
        {
            var a = root.PointToScreen(new Point(0f, 0f)); var b = root.PointToScreen(new Point(100f, 0f));
            float k = (float)Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y)) / 100f;
            return k > 0.01f && !float.IsNaN(k) ? k : 1f;
        }

        /// <summary>The window's top left corner 20 screen pixels from the screen's.</summary>
        public static void Corner(FrameworkElement root, TranslateTransform move, float k)
        {
            var a = root.PointToScreen(new Point(0f, 0f));
            if (move != null) { move.X += (20f - a.X) / k; move.Y += (20f - a.Y) / k; }
        }

        /// <summary>As large as the screen allows: nothing scrolls, nothing wraps for lack of room.</summary>
        public static void Max(Grid root, TranslateTransform move)
        {
            if (root == null) return;
            float k = K(root);
            root.Width = (UnityEngine.Screen.width - 40f) / k; root.Height = (UnityEngine.Screen.height - 40f) / k;
            Corner(root, move, k);
        }

        /// <summary>The width the widest row needs (and the chrome around the options area), minW .. 1600 units.</summary>
        public static void Width(Grid root, Panel body, ScrollViewer area, TranslateTransform move, float minW)
        {
            if (root == null || body == null || area == null) return;
            float k = K(root), maxW = (UnityEngine.Screen.width - 40f) / k;
            // a long text that wraps (a note, a help line) would make the natural width as wide as its one line: it is measured at most 260 units wide (short labels keep their width), the rows decide
            var wrapped = new System.Collections.Generic.List<TextBlock>(); var olds = new System.Collections.Generic.List<float>();
            Wrapping(body, wrapped);
            foreach (var tb in wrapped) { olds.Add(tb.MaxWidth); tb.MaxWidth = 260f; }
            body.Measure(new Size(float.PositiveInfinity, float.PositiveInfinity));
            float nat = (float)body.DesiredSize.Width;
            for (int i = 0; i < wrapped.Count; i++) wrapped[i].MaxWidth = olds[i];
            body.InvalidateMeasure();
            float chrome = (float)(root.Width - area.ViewportWidth);
            root.Width = Math.Max(minW, Math.Min(maxW, Math.Min(1600f, nat + chrome + 24f)));
            Corner(root, move, k);
        }

        static void Wrapping(DependencyObject d, System.Collections.Generic.List<TextBlock> list)
        {
            var tb = d as TextBlock;
            if (tb != null && tb.TextWrapping != TextWrapping.NoWrap) list.Add(tb);
            int n = 0;
            try { n = VisualTreeHelper.GetChildrenCount(d); } catch { }
            for (int i = 0; i < n; i++) { DependencyObject c = null; try { c = VisualTreeHelper.GetChild(d, i); } catch { } if (c != null) Wrapping(c, list); }
        }

        /// <summary>The height the options need, minH .. 1400 units.</summary>
        public static void Height(Grid root, ScrollViewer area, TranslateTransform move, float minH)
        {
            if (root == null || area == null) return;
            float k = K(root), maxH = (UnityEngine.Screen.height - 40f) / k;
            float h = (float)(root.Height - area.ViewportHeight + area.ExtentHeight) + 2f;
            root.Height = Math.Max(minH, Math.Min(maxH, Math.Min(1400f, h)));
            Corner(root, move, k);
        }

        /// <summary>The window's own size, in the middle of the screen.</summary>
        public static void Restore(Grid root, TranslateTransform move, float winW, float winH)
        {
            if (root == null) return;
            float k = K(root), sw = UnityEngine.Screen.width, sh = UnityEngine.Screen.height;
            root.Width = Math.Min(winW, (sw - 40f) / k); root.Height = Math.Min(winH, (sh - 40f) / k);
            var a = root.PointToScreen(new Point(0f, 0f));
            if (move != null) { move.X += ((sw - root.Width * k) / 2f - a.X) / k; move.Y += ((sh - root.Height * k) / 2f - a.Y) / k; }
        }

        /// <summary>The window's rectangle in screen pixels.</summary>
        public static bool Rect(FrameworkElement root, out float x, out float y, out float w, out float h)
        {
            x = y = w = h = 0f;
            if (root == null || root.ActualWidth <= 0f) return false;
            var a = root.PointToScreen(new Point(0f, 0f)); var b = root.PointToScreen(new Point((float)root.ActualWidth, (float)root.ActualHeight));
            x = Math.Min(a.X, b.X); y = Math.Min(a.Y, b.Y); w = Math.Abs(b.X - a.X); h = Math.Abs(b.Y - a.Y);
            return w > 4f && h > 4f;
        }
    }

    internal static class SettingsShots
    {
        static bool _busy;
        public static bool Busy { get { return _busy; } }

        /// <param name="dir">The folder for the PNG files (emptied of old PNG files first).</param>
        /// <param name="trace">The mod's trace (everything goes there).</param>
        /// <param name="warn">The BepInEx log (a failure that stops the tool).</param>
        public static IEnumerator Run(IShotWindow win, string dir, Action<string> trace, Action<string> warn)
        {
            if (_busy) yield break;
            _busy = true;
            int saved = 0;
            Type capT = Type.GetType("UnityEngine.ScreenCapture, UnityEngine.ScreenCaptureModule");
            Type pngT = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
            MethodInfo cap = capT == null ? null : capT.GetMethod("CaptureScreenshotAsTexture", Type.EmptyTypes);
            MethodInfo png = pngT == null ? null : pngT.GetMethod("EncodeToPNG", new[] { typeof(UnityEngine.Texture2D) });
            if (cap == null || png == null) { warn("Screenshots of the tabs: ScreenCapture / ImageConversion not available"); _busy = false; yield break; }
            bool began = false;
            try { Directory.CreateDirectory(dir); foreach (var old in Directory.GetFiles(dir, "*.png")) File.Delete(old); } catch (Exception e) { warn("Screenshots of the tabs: " + e.Message); }
            try { began = win.Begin(); } catch (Exception e) { trace("ShotBegin: " + e.Message); }
            if (!began) { warn("Screenshots of the tabs: the settings window is not open"); _busy = false; yield break; }
            int tabs = win.TabCount, index = 0;
            for (int t = 0; t < tabs; t++)
            {
                bool usable = true;
                try { usable = win.TabUsable(t); } catch { }
                if (!usable) continue;
                int pages = 1;
                try { pages = Math.Max(1, win.Pages(t)); } catch (Exception e) { trace("ShotPages tab " + t + ": " + e.Message); }
                for (int p = 0; p < pages; p++)
                {
                    try { win.Show(t, p); } catch (Exception e) { trace("ShotShow tab " + t + " page " + p + ": " + e.Message); continue; }
                    try { win.FitMax(); } catch (Exception e) { trace("ShotFitMax: " + e.Message); }
                    yield return null; yield return null; yield return null;   // the rows are laid out in the largest window
                    try { win.FitWidth(); } catch (Exception e) { trace("ShotFitWidth: " + e.Message); }
                    yield return null; yield return null; yield return null;
                    try { win.FitHeight(); } catch (Exception e) { trace("ShotFitHeight: " + e.Message); }
                    yield return null; yield return null; yield return null;   // the window of the fitted size is laid out
                    yield return new UnityEngine.WaitForEndOfFrame();
                    bool done = false;
                    for (int attempt = 0; attempt < 8 && !done; attempt++)
                    {
                        if (attempt > 0) { yield return null; yield return new UnityEngine.WaitForEndOfFrame(); }   // the capture sometimes returns nothing: again on a later frame
                        UnityEngine.Texture2D full = null, crop = null;
                        try
                        {
                            float x, y, w, h;
                            if (!win.Rect(out x, out y, out w, out h)) { trace("settings screenshot: tab " + t + " page " + p + ": no window rect"); break; }
                            full = cap.Invoke(null, null) as UnityEngine.Texture2D;
                            if (full == null) { if (attempt == 7) trace("settings screenshot: tab " + t + " page " + p + ": no capture"); continue; }
                            int ix = UnityEngine.Mathf.Clamp((int)x, 0, full.width - 1), iw = UnityEngine.Mathf.Clamp((int)Math.Ceiling(w), 1, full.width - ix);
                            int top = UnityEngine.Mathf.Clamp((int)y, 0, full.height - 1), ih = UnityEngine.Mathf.Clamp((int)Math.Ceiling(h), 1, full.height - top);
                            int iy = full.height - top - ih;            // the texture's rows start at the bottom
                            crop = new UnityEngine.Texture2D(iw, ih, UnityEngine.TextureFormat.RGBA32, false);
                            crop.SetPixels(full.GetPixels(ix, iy, iw, ih)); crop.Apply();
                            var bytes = png.Invoke(null, new object[] { crop }) as byte[];
                            index++;
                            string name = index.ToString("00") + "_" + win.Name(t, p) + ".png";
                            foreach (char bad in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(bad, '_');
                            File.WriteAllBytes(System.IO.Path.Combine(dir, name.Replace(' ', '_')), bytes);
                            saved++; done = true;
                            trace("settings screenshot: " + name + " " + iw + "x" + ih);
                        }
                        catch (Exception e) { trace("settings screenshot: " + e.Message); }
                        finally { if (full != null) UnityEngine.Object.Destroy(full); if (crop != null) UnityEngine.Object.Destroy(crop); }
                    }
                }
            }
            try { win.End(); } catch (Exception e) { trace("ShotEnd: " + e.Message); }
            warn("Screenshots of the settings tabs: " + saved + " files in " + dir);
            _busy = false;
        }
    }
}
