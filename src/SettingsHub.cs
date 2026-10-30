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
//  - Menus are built each time they are shown, so the label always reflects the mods that are active now. No polling anywhere.
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
            if (anchor == null) { _trace("SettingsHub: the menu screen was not found."); return; }
            var a = Active();
            if (a.Count == 0) return;
            if (a.Count == 1) { Run(a[0], anchor); return; }
            ShowList(anchor, a);
        }

        static void Run(object[] e, UIElement anchor)
        {
            try { var open = e[4] as Action<object>; if (open != null) open(anchor); }
            catch (Exception ex) { _trace("SettingsHub: opening " + e[0] + " failed: " + ex); }
        }

        // ------------------------------------------------------------ the list of mods

        static Popup _popup;

        static void ShowList(UIElement anchor, List<object[]> entries)
        {
            try
            {
                CloseList();
                var panel = new StackPanel { Margin = new Thickness(16f) };
                panel.Children.Add(new TextBlock { Text = Hub(), FontSize = 18f, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.FromRgb(242, 245, 250)), Margin = new Thickness(0f, 0f, 0f, 12f) });
                foreach (var e in entries)
                {
                    var entry = e;
                    var b = new Button { Content = Title(e), FontSize = 16f, MinWidth = 280f, Margin = new Thickness(0f, 0f, 0f, 8f), Padding = new Thickness(18f, 8f, 18f, 8f) };
                    b.Click += (s, a) => { CloseList(); Run(entry, anchor); };
                    panel.Children.Add(b);
                }
                var frame = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(27, 42, 64)), BorderBrush = new SolidColorBrush(Color.FromRgb(95, 200, 228)),
                    BorderThickness = new Thickness(2.5f), CornerRadius = new CornerRadius(12f), Child = panel
                };
                _popup = new Popup { Child = frame, StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.Center, PlacementTarget = anchor, IsOpen = true };
            }
            catch (Exception e) { _trace("SettingsHub list: " + e); }
        }

        static void CloseList()
        {
            try { if (_popup != null) { _popup.IsOpen = false; _popup.Child = null; } } catch { }
            _popup = null;
        }
    }
}
