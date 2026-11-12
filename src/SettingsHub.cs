// ======================================================================================================================
// rgreat settings hub, protocol v1 - THE SAME FILE IN EVERY rgreat MOD (Alternative Game Interface, Alternative Weapons
// Control, Alternative Damage Model, Alternative Special Effects). The canonical copy is src/SettingsHub.cs of
// https://github.com/rgreat/SeaPower.AlternativeSpecialEffects ; change it there and copy it to the other mods unchanged
// (only the 'using' lines and the namespace below may differ if a mod needs it; the logic and the protocol must not).
//
// What it does: one entry for the settings of ALL the rgreat mods that are installed and ticked, in the main menu (under Mod Manager)
// and in the pause menu (under Options). The mods do not depend on each other and may be installed in any combination:
//  - A mod registers itself in a registry that lives in AppDomain data ("rgreat.settings.v1"). The registry holds only BCL types
//    (List<object[]>, Func<>, Action<>), so copies of this code in different dlls share it.
//    Entry: [0] string id, [1] Func<string> title (localized, short), [2] string version, [3] int order, [4] Action<object> open(anchor UIElement),
//    [5] Func<bool> active (the mod is ticked in the Mods menu). New protocol versions append fields; readers ignore what they do not know.
//  - The mod whose hook runs first when a menu is built puts the button in; the others find it there (main menu: a Button named
//    "rgreatSettings"; pause menu: the list object noted in AppDomain data "rgreat.settings.v1.pause") and add nothing.
//  - One active mod: the button carries its name and opens its settings window. Two or more: the button is "Alternative Mods"
//    and opens a small list, one button per mod, in the order of the entries (then by id).
//  - The hub logs to LogOutput.log (source 'rgreat settings hub'): every click, every failure.
//  - Menus are built each time they are shown, so the label always reflects the mods that are active now. No polling anywhere.
//  - A mod that registers later than the first one (early start under Keel) refreshes the text of the buttons already in the menus (RefreshLabels, in Register and in Install).
// ======================================================================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Input;
using HarmonyLib;
using Noesis;

namespace Rgreat.Hub
{
    internal static class SettingsHub
    {
        const string RegistryKey = "rgreat.settings.v1", PauseKey = "rgreat.settings.v1.pause", ButtonName = "rgreatSettings";

        static Action<string> _trace = s => { };
        static BepInEx.Logging.ManualLogSource _log;
        /// <summary>The hub writes to the BepInEx log (LogOutput.log) always: a click that does nothing must leave a trace even when the mod's Debug is off.</summary>
        static void Log(string msg) { try { if (_log == null) _log = BepInEx.Logging.Logger.CreateLogSource("rgreat settings hub"); _log.LogInfo(msg); } catch { } }
        static Func<string> _language = () => "en";
        static Assembly _game;

        // ------------------------------------------------------------ registry

        static List<object[]> Registry()
        {
            lock (typeof(object))
            {
                var r = AppDomain.CurrentDomain.GetData(RegistryKey) as List<object[]>;
                if (r == null) { r = new List<object[]>(); AppDomain.CurrentDomain.SetData(RegistryKey, r); }
                return r;
            }
        }

        /// <summary>A mod announces itself (again with the same id = replaces its entry). 'order' places it in the list: 10 Game Interface, 20 Weapons Control, 30 Damage Model, 40 Special Effects.</summary>
        public static void Register(string id, Func<string> title, string version, int order, Action<object> open, Func<bool> active)
        {
            var r = Registry();
            lock (r)
            {
                r.RemoveAll(e => e.Length > 0 && string.Equals(e[0] as string, id, StringComparison.Ordinal));
                r.Add(new object[] { id, title, version, order, open, active });
            }
            RefreshLabels();
        }

        /// <summary>The buttons already in the menus get the text for the mods registered now (a mod that starts later than the first one).</summary>
        static void RefreshLabels()
        {
            try
            {
                if (_game == null) return;
                string label = Label();
                if (label == null) return;
                var view = GameView("SeapowerUI.MainMenuView") as FrameworkElement;
                var scroll = view == null ? null : view.FindName("scrollViewer") as ScrollViewer;
                var list = scroll == null ? null : scroll.Content as StackPanel;
                if (list != null)
                    foreach (var c in list.Children)
                    {
                        var b = c as Button;
                        if (b != null && b.Name == ButtonName) b.Content = label.ToUpperInvariant();
                    }
                if (_setContent != null)
                    foreach (var w in _pauseButtons) { var b = w.Target; if (b != null) _setContent.Invoke(b, new object[] { label }); }
            }
            catch (Exception e) { _trace("SettingsHub refresh: " + e.Message); }
        }

        static List<object[]> Active()
        {
            var r = Registry(); var list = new List<object[]>();
            lock (r)
                foreach (var e in r)
                {
                    try
                    {
                        if (e.Length < 6) continue;
                        var act = e[5] as Func<bool>;
                        if (act == null || act()) list.Add(e);
                    }
                    catch { }
                }
            list.Sort((a, b) =>
            {
                int c = Convert.ToInt32(a[3]).CompareTo(Convert.ToInt32(b[3]));
                return c != 0 ? c : string.CompareOrdinal(a[0] as string, b[0] as string);
            });
            return list;
        }

        static string Title(object[] e) { try { var f = e[1] as Func<string>; return f == null ? (e[0] as string) : f(); } catch { return e[0] as string; } }

        static string Hub()
        {
            switch (_language())
            {
                case "ru": return "Альтернативные моды";
                case "cn": return "Alternative 模组";
                case "ja": return "Alternative MOD";
                case "ko": return "Alternative 모드";
                default: return "Alternative Mods";
            }
        }

        /// <summary>The text of the menu button now: the mod's own title when it is alone, else the common one.</summary>
        static string Label()
        {
            var a = Active();
            return a.Count == 0 ? null : a.Count == 1 ? Title(a[0]) : Hub();
        }

        // ------------------------------------------------------------ installing the menu hooks

        static Type _tPauseButton, _tCommand, _tLang;
        static ConstructorInfo _ctorPauseButton, _ctorCommand;
        static MethodInfo _setContent;
        static FieldInfo _fSection, _fKey;
        static readonly List<WeakReference> _pauseButtons = new List<WeakReference>();

        /// <summary>Once per mod, with the mod's Harmony instance. 'language' gives the current language code (en, ru, cn, de, es, fr, ja, ko, vn).</summary>
        public static void Install(Harmony harmony, Assembly game, Action<string> trace, Func<string> language)
        {
            _game = game; if (trace != null) _trace = trace; if (language != null) _language = language;
            RefreshLabels();   // each mod has its own copy of this class: Register above ran before _game was known, so the buttons of the mods started earlier get their text here
            // Patching the menu classes at plugin start makes the runtime run their static constructors, which register Noesis properties before Noesis
            // is ready: the game (the Release build) crashes natively. So the patches are made after the game's load screen has finished.
            const BindingFlags any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            // Keel starts the mods after the start-up load screen is over: the coroutine will not finish again
            if (LoadScreenIsDone(game))
            {
                _harmony = harmony; _installed = true;
                Log("load screen is already over: menu entries are installed now");
                try
                {
                    DoInstall(harmony);
                    var menu = GameView("SeapowerUI.MainMenuView");
                    if (menu != null) MainMenuCtorPostfix(menu);
                }
                catch (Exception e) { Log("installing the menu entries failed: " + e); }
                return;
            }
            try
            {
                var ls = game.GetType("SeaPower.LoadScreen");
                if (ls != null)
                    foreach (var nt in ls.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                        if (nt.Name.StartsWith("<ExecuteLoadScreen>", StringComparison.Ordinal))
                        {
                            var mn = nt.GetMethod("MoveNext", any);
                            if (mn != null) { harmony.Patch(mn, postfix: Hm("LoadScreenPostfix")); _harmony = harmony; Log("menu entries wait for the end of the load screen"); return; }
                        }
            }
            catch (Exception e) { Log("load-screen hook failed: " + e.Message); }
            Log("load screen not found: menu entries are installed now");
            DoInstall(harmony);
        }

        static bool LoadScreenIsDone(Assembly game)
        {
            try
            {
                var ls = game.GetType("SeaPower.LoadScreen");
                if (ls == null) return false;
                // Singleton<LoadScreen>.InstanceExists(false) -> Instance -> LoadText
                var si = ls.BaseType;
                var exists = si == null ? null : si.GetMethod("InstanceExists", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                var instProp = si == null ? null : si.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                if (exists == null || instProp == null) return false;
                if (!(bool)exists.Invoke(null, new object[] { false })) return false;
                var inst = instProp.GetValue(null, null);
                var p = ls.GetProperty("LoadText", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                return inst != null && p != null && "Done".Equals(p.GetValue(inst, null) as string);
            }
            catch { return false; }
        }

        static Harmony _harmony;
        static bool _installed;

        static void LoadScreenPostfix(bool __result)
        {
            if (__result || _installed) return;    // the iterator has not finished yet
            _installed = true;
            try
            {
                DoInstall(_harmony);
                // the main menu may have been built already: it gets its button now
                var menu = GameView("SeapowerUI.MainMenuView");
                if (menu != null) MainMenuCtorPostfix(menu);
            }
            catch (Exception e) { Log("installing the menu entries failed: " + e); }
        }

        static void DoInstall(Harmony harmony)
        {
            var game = _game;
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var main = game.GetType("SeapowerUI.MainMenuView");
            var ctor = main == null ? null : main.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (ctor != null) harmony.Patch(ctor, postfix: Hm("MainMenuCtorPostfix"));
            else _trace("SettingsHub: MainMenuView constructor not found: no settings entry in the main menu.");

            _tPauseButton = game.GetType("SeapowerUI.ViewModels.PauseMenuButton");
            _tCommand = game.GetType("SeaPower.DelegateCommand");
            _tLang = game.GetType("SeaPower.LanguageResourceHandler");
            var vm = game.GetType("SeapowerUI.ViewModels.PauseMenuViewModel");
            if (_tPauseButton == null || _tCommand == null || vm == null || _tLang == null) { _trace("SettingsHub: pause menu types not found: no settings entry in the pause menu."); return; }
            _ctorPauseButton = _tPauseButton.GetConstructor(new[] { typeof(string), typeof(string), typeof(ICommand), typeof(bool), typeof(bool) });
            _ctorCommand = _tCommand.GetConstructor(new[] { typeof(Action<object>) });
            var content = _tPauseButton.GetProperty("ButtonContent", all);
            _setContent = content == null ? null : content.GetSetMethod(true);
            _fSection = _tPauseButton.GetField("contentSection", all); _fKey = _tPauseButton.GetField("contentKey", all);
            var init = vm.GetMethod("Initialise", all, null, new[] { typeof(List<>).MakeGenericType(_tPauseButton) }, null);
            if (_ctorPauseButton != null && _ctorCommand != null && _setContent != null && _fSection != null && _fKey != null && init != null)
                harmony.Patch(init, prefix: Hm("PauseInitialisePrefix"));
            else _trace("SettingsHub: pause menu members not found: no settings entry in the pause menu.");
        }

        static HarmonyMethod Hm(string name) { return new HarmonyMethod(typeof(SettingsHub).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)); }

        // ------------------------------------------------------------ main menu

        static void MainMenuCtorPostfix(object __instance)
        {
            try
            {
                string label = Label();
                var view = __instance as FrameworkElement;
                var scroll = view == null ? null : view.FindName("scrollViewer") as ScrollViewer;
                var list = scroll == null ? null : scroll.Content as StackPanel;
                if (list == null) { _trace("SettingsHub: main menu: the button list was not found"); return; }
                var kids = list.Children;
                for (int i = 0; i < kids.Count; i++) { var b0 = kids[i] as Button; if (b0 != null && b0.Name == ButtonName) return; }   // another mod put it in
                if (label == null) return;
                int n = kids.Count;
                // ... Options, Mod Manager, (spacer), Credits, (spacer), Exit: under Mod Manager
                if (n < 5 || !(kids[n - 4] is TextBlock) || !(kids[n - 1] is Button)) { _trace("SettingsHub: main menu: unexpected button list (" + n + ")"); return; }
                var b = new Button { Name = ButtonName };
                var style = view.TryFindResource("MenuButton") as Style;
                if (style != null) b.Style = style;
                b.FontSize = 25f;
                b.Content = label.ToUpperInvariant();
                b.Click += (s, e) => Open(GameView("SeapowerUI.MainMenuView") ?? view);
                kids.Insert(n - 4, b);
            }
            catch (Exception e) { _trace("SettingsHub main menu: " + e); }
        }

        // ------------------------------------------------------------ pause menu

        static void PauseInitialisePrefix(IList pauseMenuButtons)
        {
            if (pauseMenuButtons == null) return;
            try
            {
                var noted = AppDomain.CurrentDomain.GetData(PauseKey) as WeakReference;
                if (noted != null && ReferenceEquals(noted.Target, pauseMenuButtons)) return;   // another mod put it in this list
                int at = -1;
                for (int i = 0; i < pauseMenuButtons.Count; i++)
                {
                    var b = pauseMenuButtons[i];
                    if (b != null && "PauseMenu".Equals(_fSection.GetValue(b)) && "Options".Equals(_fKey.GetValue(b))) { at = i; break; }
                }
                string label = Label();
                if (at < 0 || label == null) return;   // a menu without Options (confirmation pages), or no active mod
                AppDomain.CurrentDomain.SetData(PauseKey, new WeakReference(pauseMenuButtons));
                Action<object> open = o => Open(GameView("SeapowerUI.PauseMenuView"));
                var cmd = _ctorCommand.Invoke(new object[] { open });
                // an existing key keeps the game's constructor from logging a missing text; the text is the mod's
                var button = _ctorPauseButton.Invoke(new object[] { "PauseMenu", "Options", cmd, true, true });
                _setContent.Invoke(button, new object[] { label });
                pauseMenuButtons.Insert(at + 1, button);
                _pauseButtons.Add(new WeakReference(button));
                if (_pauseButtons.Count > 8) _pauseButtons.RemoveAt(0);
                SubscribeLanguage();
            }
            catch (Exception e) { _trace("SettingsHub pause menu: " + e); }
        }

        /// <summary>The game's buttons set their text on LanguageChanged; this handler is subscribed again after each button is made, so it runs after the game's own and keeps the hub's text.</summary>
        static void SubscribeLanguage()
        {
            var si = _tLang.BaseType;   // Singleton<LanguageResourceHandler>
            var instProp = si == null ? null : si.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            var inst = instProp == null ? null : instProp.GetValue(null, null);
            var ev = _tLang.GetEvent("LanguageChanged");
            if (inst == null || ev == null) return;
            System.EventHandler h = LanguageChanged;
            ev.RemoveEventHandler(inst, h);
            ev.AddEventHandler(inst, h);
        }

        static void LanguageChanged(object sender, System.EventArgs e)
        {
            try
            {
                string label = Label();
                if (label == null) return;
                foreach (var w in _pauseButtons) { var b = w.Target; if (b != null) _setContent.Invoke(b, new object[] { label }); }
            }
            catch (Exception ex) { _trace("SettingsHub language: " + ex.Message); }
        }

        // ------------------------------------------------------------ the click

        static UIElement GameView(string typeName)
        {
            var t = _game == null ? null : _game.GetType(typeName);
            var f = t == null ? null : t.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
            return f == null ? null : f.GetValue(null) as UIElement;
        }

        /// <summary>One active mod: its window at once. Several: the list.</summary>
        static void Open(UIElement anchor)
        {
            if (anchor == null) { Log("click: the menu screen was not found"); _trace("SettingsHub: the menu screen was not found."); return; }
            var a = Active();
            Log("click: " + a.Count + " active mod(s): " + string.Join(", ", a.ConvertAll(e => (string)e[0]).ToArray()));
            if (a.Count == 0) return;
            if (a.Count == 1) { Run(a[0], anchor); return; }
            ShowList(anchor, a);
        }

        static void Run(object[] e, UIElement anchor)
        {
            try { var open = e[4] as Action<object>; if (open != null) open(anchor); }
            catch (Exception ex) { Log("opening " + e[0] + " failed: " + ex); _trace("SettingsHub: opening " + e[0] + " failed: " + ex); }
        }

        // ------------------------------------------------------------ the list of mods
        // Hosted like the mods' settings windows: added to the top Grid / Canvas of the menu screen with a high z-index (a Popup is not used:
        // it was not shown in the menus). A dim layer behind the frame closes the list on a click outside it.

        static Panel _host;
        static FrameworkElement _overlay;

        static DependencyObject Parent(DependencyObject e) { try { return VisualTreeHelper.GetParent(e); } catch { return null; } }

        static void ShowList(UIElement anchor, List<object[]> entries)
        {
            try
            {
                CloseList();
                Panel panel = null;
                for (DependencyObject p = anchor; p != null; p = Parent(p))
                {
                    var pn = p as Panel;
                    bool itemsHost = false;
                    try { itemsHost = pn != null && pn.IsItemsHost; } catch { }
                    if (pn != null && !itemsHost && (pn is Grid || pn is Canvas)) panel = pn;
                }
                if (panel == null) { Log("the list of mods: no panel to host it in; opening the first mod's settings"); Run(entries[0], anchor); return; }

                var column = new StackPanel { Margin = new Thickness(16f) };
                column.Children.Add(new TextBlock { Text = Hub(), FontSize = 18f, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.FromRgb(242, 245, 250)), Margin = new Thickness(0f, 0f, 0f, 12f) });
                foreach (var e in entries)
                {
                    var entry = e;
                    var b = new Button { Content = Title(e), FontSize = 16f, MinWidth = 280f, Margin = new Thickness(0f, 0f, 0f, 8f), Padding = new Thickness(18f, 8f, 18f, 8f) };
                    b.Click += (s, a) => { CloseList(); Log("opening the settings of " + entry[0]); Run(entry, anchor); };
                    column.Children.Add(b);
                }
                var frame = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(27, 42, 64)), BorderBrush = new SolidColorBrush(Color.FromRgb(95, 200, 228)),
                    BorderThickness = new Thickness(2.5f), CornerRadius = new CornerRadius(12f), Child = column,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                };
                frame.MouseLeftButtonDown += (s, a) => { a.Handled = true; };
                var overlay = new Grid { Background = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)) };
                overlay.Children.Add(frame);
                overlay.MouseLeftButtonDown += (s, a) => { a.Handled = true; CloseList(); };

                var grid = panel as Grid;
                if (grid != null)
                {
                    Grid.SetRow(overlay, 0); Grid.SetColumn(overlay, 0);
                    Grid.SetRowSpan(overlay, Math.Max(1, grid.RowDefinitions.Count)); Grid.SetColumnSpan(overlay, Math.Max(1, grid.ColumnDefinitions.Count));
                }
                else
                {
                    overlay.Width = panel.ActualWidth > 0 ? panel.ActualWidth : 1280f; overlay.Height = panel.ActualHeight > 0 ? panel.ActualHeight : 720f;
                    Canvas.SetLeft(overlay, 0f); Canvas.SetTop(overlay, 0f);
                }
                Panel.SetZIndex(overlay, 30000);
                panel.Children.Add(overlay);
                _host = panel; _overlay = overlay;
                Log("the list of mods shown (" + entries.Count + " mods) in " + panel.GetType().Name + " " + panel.ActualWidth + " x " + panel.ActualHeight);
            }
            catch (Exception e) { Log("the list of mods failed: " + e); _trace("SettingsHub list: " + e); }
        }

        static void CloseList()
        {
            try { if (_host != null && _overlay != null) _host.Children.Remove(_overlay); } catch { }
            _host = null; _overlay = null;
        }
    }
}
