// ======================================================================================================================
// rgreat settings hub, protocol v1 - THE SAME FILE IN EVERY rgreat MOD (Alternative Game Interface, Alternative Weapons
// Control, Alternative Damage Model, Alternative Special Effects). The canonical copy is src/SettingsHub.cs of
// https://github.com/rgreat/SeaPower.AlternativeBase (src/SettingsHub.cs) ; change it there and copy it to the mods with tools/sync.sh, unchanged
// (only the 'using' lines and the namespace below may differ if a mod needs it; the logic and the protocol must not).
//
// What it does: one entry for the settings of ALL the rgreat mods that are installed and ticked, in the main menu (under Mod Manager)
// and in the pause menu (under Options). The mods do not depend on each other and may be installed in any combination:
//  - A mod registers itself in a registry that lives in AppDomain data ("rgreat.settings.v1"). The registry holds only BCL types
//    (List<object[]>, Func<>, Action<>), so copies of this code in different dlls share it.
//    Entry: [0] string id, [1] Func<string> title (localized, short), [2] string version, [3] int order, [4] Action<object> open(anchor UIElement),
//    [5] Func<bool> active (the mod is ticked in the Mods menu). New protocol versions append fields; readers ignore what they do not know.
//    Appended (the switch between the mods' windows): [6] Func<bool> dirty (edits not applied), [7] Action commit (apply the kept edits and drop the state),
//    [8] Action discard (drop the kept edits), [9] Action hide (take the window off the screen, KEEPING the edits), [10] Func<bool> kept (hidden with edits kept),
//    [11] Func<IEnumerator> shots (Debug: the coroutine of the screenshots of the tabs of the window that is on the screen; the Game Interface's Debug runs it for every mod).
//  - The windows of the mods are separate, but each has a row with the mods' names at the top: a click hides this window (the edits stay) and opens the
//    other mod's window at the same place and size (the rectangle goes through AppDomain data "rgreat.settings.v1.geom", float[x, y, w, h] in screen pixels).
//    Apply / OK in any window applies the kept edits of the others too, Cancel / ✕ drops them. A yellow dot on a mod's name marks edits not applied yet.
//  - The mod whose hook runs first when a menu is built puts the button in; the others find it there (main menu: a Button named
//    "rgreatSettings"; pause menu: the list object noted in AppDomain data "rgreat.settings.v1.pause") and add nothing.
//  - One active mod: the button carries its name and opens its settings window. Two or more: the button is "Alternative Mods" and opens the window
//    of the mod used last (else the first in the order of the entries, then by id); the row of the mods at the top of the window switches between them.
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

        /// <summary>As above, with what the switch between the mods' windows needs (see the header).</summary>
        public static void Register(string id, Func<string> title, string version, int order, Action<object> open, Func<bool> active,
                                    Func<bool> dirty, Action commit, Action discard, Action hide, Func<bool> kept, Func<IEnumerator> shots = null)
        {
            var r = Registry();
            lock (r)
            {
                r.RemoveAll(e => e.Length > 0 && string.Equals(e[0] as string, id, StringComparison.Ordinal));
                r.Add(new object[] { id, title, version, order, open, active, dirty, commit, discard, hide, kept, shots });
            }
            RefreshLabels();
        }

        /// <summary>[11] (appended later) the coroutine that takes the screenshots of the tabs of the mod's window that is on the screen now (Debug); null when the mod has none.</summary>
        public static Func<IEnumerator> ShotsOf(object[] e) { return e.Length > 11 ? e[11] as Func<IEnumerator> : null; }

        // ------------------------------------------------------------ the switch between the mods' windows

        const string GeomKey = "rgreat.settings.v1.geom";
        const string LangKey = "rgreat.settings.v1.lang", LangSinksKey = "rgreat.settings.v1.langsinks";

        // ------------------------------------------------------------ the language shared by the mods
        // One setting for all the rgreat mods: a language code ("" = the default, the game's own) in AppDomain data; every mod joins with a sink
        // that stores the code in its own config and makes its texts follow. The first mod that joins brings its stored value; the others take the shared one.

        const string CarryKey = "rgreat.settings.v1.querycarry";
        /// <summary>The text of the search box goes with the user to the window of another mod: stored when a window is switched, taken (once) by the next window's search box.</summary>
        public static void CarryQuery(string q) { AppDomain.CurrentDomain.SetData(CarryKey, q ?? ""); }
        public static string TakeQuery() { var q = (AppDomain.CurrentDomain.GetData(CarryKey) as string) ?? ""; AppDomain.CurrentDomain.SetData(CarryKey, ""); return q; }

        /// <summary>The shared language code ("" = the game's own).</summary>
        public static string SharedLanguage() { return (AppDomain.CurrentDomain.GetData(LangKey) as string) ?? ""; }

        /// <summary>Once per mod at start: 'own' is the language stored in the mod's config, 'apply' stores a code there and refreshes the texts.</summary>
        public static void JoinLanguage(string own, Action<string> apply)
        {
            own = own ?? "";
            lock (typeof(object))
            {
                var sinks = AppDomain.CurrentDomain.GetData(LangSinksKey) as List<Action<string>>;
                if (sinks == null) { sinks = new List<Action<string>>(); AppDomain.CurrentDomain.SetData(LangSinksKey, sinks); }
                sinks.Add(apply);
                var shared = AppDomain.CurrentDomain.GetData(LangKey) as string;
                if (shared == null) { AppDomain.CurrentDomain.SetData(LangKey, own); return; }
                if (shared != own) { try { apply(shared); } catch (Exception e) { Log("language: " + e.Message); } }
            }
        }

        /// <summary>The user chose a language: every mod stores it and follows.</summary>
        public static void SetLanguage(string code)
        {
            code = code ?? "";
            AppDomain.CurrentDomain.SetData(LangKey, code);
            var sinks = AppDomain.CurrentDomain.GetData(LangSinksKey) as List<Action<string>>;
            if (sinks == null) return;
            foreach (var s in sinks.ToArray()) { try { s(code); } catch (Exception e) { Log("language: " + e.Message); } }
        }

        /// <summary>The mods that are active now, in order.</summary>
        public static List<object[]> ActiveMods() { return Active(); }
        public static string IdOf(object[] e) { return e[0] as string; }
        public static string TitleOf(object[] e) { return Title(e); }
        /// <summary>The title of a settings window: the mod's own when it is alone, the common one (as on the menu button) when several mods are installed.</summary>
        public static string WindowTitle(string own) { return Active().Count > 1 ? Hub() : own; }

        static bool Call(object[] e, int i) { try { var f = e.Length > i ? e[i] as Func<bool> : null; return f != null && f(); } catch { return false; } }
        static void Do(object[] e, int i) { try { var a = e.Length > i ? e[i] as Action : null; if (a != null) a(); } catch (Exception ex) { Log("settings switch: " + e[0] + ": " + ex.Message); } }

        public static bool IsDirty(object[] e) { return Call(e, 6); }

        /// <summary>The rectangle of the window being replaced (screen pixels), taken by the next window that is put on the screen; null when there is none.</summary>
        public static float[] TakeGeometry()
        {
            var g = AppDomain.CurrentDomain.GetData(GeomKey) as float[];
            if (g != null) AppDomain.CurrentDomain.SetData(GeomKey, null);
            return g != null && g.Length >= 4 ? g : null;
        }

        /// <summary>The window of 'selfId' goes (its edits stay), the window of 'targetId' comes at the same place and size.</summary>
        public static void SwitchTo(string selfId, string targetId, UIElement anchor, float[] rect)
        {
            var r = Registry(); object[] self = null, target = null;
            lock (r) foreach (var e in r) { var id = e[0] as string; if (id == selfId) self = e; else if (id == targetId) target = e; }
            if (target == null) { Log("switch: " + targetId + " is not registered"); return; }
            Log("switch: " + selfId + " -> " + targetId);
            AppDomain.CurrentDomain.SetData(LastKey, targetId);
            try
            {
                if (rect != null) AppDomain.CurrentDomain.SetData(GeomKey, rect);
                if (self != null) Do(self, 9);
                var open = target[4] as Action<object>;
                if (open != null) open(anchor);
            }
            catch (Exception ex) { Log("switch to " + targetId + " failed: " + ex); }
            finally { AppDomain.CurrentDomain.SetData(GeomKey, null); }
        }

        /// <summary>Apply / OK in the window of 'selfId': the edits kept in the hidden windows of the other mods are applied too.</summary>
        public static void CommitOthers(string selfId)
        {
            foreach (var e in Active()) if ((e[0] as string) != selfId && Call(e, 10) && Call(e, 6)) Do(e, 7);
        }

        /// <summary>Cancel / ✕ in the window of 'selfId': the edits kept in the other mods are dropped.</summary>
        public static void DiscardOthers(string selfId)
        {
            foreach (var e in Active()) if ((e[0] as string) != selfId && Call(e, 10)) Do(e, 8);
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

        const string LastKey = "rgreat.settings.v1.last";

        /// <summary>The window of the mod used last (else the first one); the others are reached by the row of the mods at the top of the window.</summary>
        static void Open(UIElement anchor)
        {
            if (anchor == null) { Log("click: the menu screen was not found"); _trace("SettingsHub: the menu screen was not found."); return; }
            var a = Active();
            Log("click: " + a.Count + " active mod(s): " + string.Join(", ", a.ConvertAll(e => (string)e[0]).ToArray()));
            if (a.Count == 0) return;
            DiscardOthers(null);   // a new start from the menu: edits left in a hidden window (the game closed the visible one without Cancel) are dropped
            string last = AppDomain.CurrentDomain.GetData(LastKey) as string;
            Run(a.Find(e => (string)e[0] == last) ?? a[0], anchor);
        }

        /// <summary>A window of the mod 'id' is on the screen: the menu and the gears open this mod's window next time.</summary>
        public static void MarkLast(string id)
        {
            if (!string.IsNullOrEmpty(id)) AppDomain.CurrentDomain.SetData(LastKey, id);
        }

        /// <summary>The gear of a window of 'ownId' (World View): opens the window of the mod used last when that is another active mod; false when it is 'ownId' itself, none, or not active (the caller opens its own).</summary>
        public static bool OpenLastOther(UIElement anchor, string ownId)
        {
            try
            {
                string last = AppDomain.CurrentDomain.GetData(LastKey) as string;
                if (string.IsNullOrEmpty(last) || last == ownId) return false;
                var e = Active().Find(x => (string)x[0] == last);
                if (e == null) return false;
                Run(e, anchor); return true;
            }
            catch (Exception ex) { Log("open the last mod failed: " + ex.Message); return false; }
        }

        static void Run(object[] e, UIElement anchor)
        {
            try { AppDomain.CurrentDomain.SetData(LastKey, e[0] as string); var open = e[4] as Action<object>; if (open != null) open(anchor); }
            catch (Exception ex) { Log("opening " + e[0] + " failed: " + ex); _trace("SettingsHub: opening " + e[0] + " failed: " + ex); }
        }
    }
}
