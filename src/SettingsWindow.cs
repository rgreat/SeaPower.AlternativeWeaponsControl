using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BepInEx.Configuration;
using Noesis;
using Rgreat.Kit;

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
    static partial class SettingsWindow
    {
        public static Action<string> Trace = s => { };
        public static Action<string> Warn = s => { };

        enum Kind { Number, Bool, Lang, Pick, Action }

        sealed class Field
        {
            public string Tab, Group, Key, Label, Unit, Game, Parent;
            public Kind Kind;
            public float Step, Min, Max;
            public Action Act;         // Kind.Action: what the button does
            public string[] Options;   // Pick: the values of the setting (their texts are "pick.<value>")
        }

        static readonly List<Field> Fields = new List<Field>();
        static readonly List<string> Tabs = new List<string>();
        static string _curTab = "tabs.panel";
        static string _curSub = "sub.general";   // the inner tab of "Accuracy"

        /// <summary>The inner tab of a field (only the Fire Control tab has them): the director settings on their own, the rest together.</summary>
        static string SubOf(Field f) { return f.Tab != "tabs.gunsAccuracy" ? null : f.Group == "grp.directors" ? "sub.directors" : "sub.general"; }

        static UIElement SubBar()
        {
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0f, 0f, 0f, 6f) };
            foreach (var sub in new[] { "sub.general", "sub.directors" })
            {
                string s = sub;
                bool sel = s == _curSub;
                var b = new Border { Child = Text(s == "sub.directors" ? Loc.T("grp.directors") : Loc.T(s), sel ? CText : CDim, 12f), Padding = new Thickness(12f, 4f, 12f, 4f), Margin = new Thickness(0f, 0f, 4f, 0f),
                                     Cursor = Cursors.Hand, Background = B(sel ? CBg : CCard), BorderBrush = B(sel ? CText : CLine), BorderThickness = new Thickness(1f), CornerRadius = new CornerRadius(3f) };
                b.MouseLeftButtonUp += (o, e) => { _curSub = s; Show(); };
                bar.Children.Add(b);
            }
            return bar;
        }

        /// <summary>A button in the list of options (no value, nothing to save).</summary>
        static void AddAction(string tab, string group, string key, string label, Action act)
        {
            if (!Tabs.Contains(tab)) Tabs.Add(tab);
            Fields.Add(new Field { Tab = tab, Group = group, Key = key, Label = label, Unit = "", Kind = Kind.Action, Act = act });
        }

        static void Add(string tab, string group, string key, string label, string unit, Kind kind, float step, float min, float max, string game = null, string parent = null)
        {
            if (!Tabs.Contains(tab)) Tabs.Add(tab);
            Fields.Add(new Field { Tab = tab, Group = group, Key = key, Label = label, Unit = unit, Kind = kind, Step = step, Min = min, Max = max, Game = game, Parent = parent });
        }

        static SettingsWindow()
        {
            const string T1 = "tabs.panel", T2 = "tabs.fire", TA = "tabs.ai", T3 = "tabs.guns", TG = "tabs.gunsAccuracy", T5 = "tabs.debug";
            Add(T1, "grp.panel", "UI/WeaponsTabGroundUnits", "Weapons tab for ground units", "", Kind.Bool, 0, 0, 0, "game: off");
            Add(T1, "grp.panel", "UI/WeaponsTabAircraft", "Weapons tab for aircraft and helicopters", "", Kind.Bool, 0, 0, 0, "game: off");
            Add(T1, "grp.panel", "UI/FireShowsZones", "Fire shows weapon arcs and range", "", Kind.Bool, 0, 0, 0, "game: off");
            Add(T1, "grp.panel", "UI/BottomPanelFire", "Bottom panel weapons act as Fire", "", Kind.Bool, 0, 0, 0, "game: off", "UI/FireShowsZones");
            Add(T1, "grp.panel", "UI/SectionTargetTypes", "SAM group title lists other target types", "", Kind.Bool, 0, 0, 0, "game: off");
            Add(T1, "grp.panel", "UI/ClickVolume", "Click sound volume", "0..1 · 0 = no sound", Kind.Number, 0.1f, 0f, 1f);
            Add(T1, "grp.samButton", "UI/SamReadyButton", "SAM readiness button", "", Kind.Bool, 0, 0, 0);
            Add(T1, "grp.samButton", "UI/SamReadyMinutes", "SAM readiness limit", "1..60", Kind.Number, 1f, 1f, 60f, null, "UI/SamReadyButton");
            Fields.Add(new Field { Tab = T2, Group = "grp.so", Key = "Fire/UncoveredWeapons", Label = "Weapons that no Standing Orders rule covers", Unit = "", Kind = Kind.Pick, Options = AwcPlugin.UncoveredNames, Game = "game: off" });
            Add(T2, "grp.freeAir", "Fire/UnknownAirMinKnots", "Engage unidentified aircraft faster than", "kn", Kind.Number, 10f, 100f, 2000f);
            Add(T2, "grp.freeAir", "Fire/UnknownAirMaxNm", "Engage unidentified aircraft closer than", "nm", Kind.Number, 5f, 5f, 200f);
            Add(T2, "grp.freeAir", "Fire/UnknownAirDefaultOwn", "On Free by default: your and allied ships", "", Kind.Bool, 0, 0, 0);
            Add(T2, "grp.freeAir", "Fire/UnknownAirDefaultEnemy", "On Free by default: enemy AI ships", "", Kind.Bool, 0, 0, 0);
            Add(T2, "grp.sam", "Ships/SamAutoCharge", "Charge SAMs when unknown aircraft approach", "", Kind.Bool, 0, 0, 0, "game: off");
            Add(T2, "grp.sam", "Ships/SamAutoChargeRange", "Unknown aircraft distance to charge", "nm", Kind.Number, 5f, 5f, 200f, null, "Ships/SamAutoCharge");
            Add(T2, "grp.sam", "Ships/SamKeepLoaded", "Keep SAM missiles on the rails while hot", "", Kind.Bool, 0, 0, 0, "game: off");
            Add(T2, "grp.sam", "Fire/SamTightNezOnly", "On Tight: fire only in no-escape zone", "", Kind.Bool, 0, 0, 0, "game: off");
            Add(T2, "grp.sam", "Ships/SamAnimFix", "Missile load / unload animation fix", "", Kind.Bool, 0, 0, 0, "game: off");
            Add(T3, "grp.gunTargets", "Guns/RememberTargets", "Remember earlier targets", "", Kind.Bool, 0, 0, 0, "game: off");
            Add(T3, "grp.gunTargets", "Guns/ClickEngagesAllGuns", "Click on a target: all guns", "", Kind.Bool, 0, 0, 0, "game: one ammunition");
            Add(T3, "grp.gunTargets", "Guns/PointFire", "Fire at a point of the map", "", Kind.Bool, 0, 0, 0, "game: off");
            Add(T3, "grp.gunTargets", "Guns/EngageOutsideSector", "Engage targets outside the firing sector", "", Kind.Bool, 0, 0, 0, "game: off");
            Add(T3, "grp.gunTargets", "Guns/LocalControlKeepsFiring", "Local control: no spotting wait", "", Kind.Bool, 0, 0, 0, "game: off");
            Add(T3, "grp.fireControl", "FireControl/GunOneDirector", "Guns take one director", "", Kind.Bool, 0, 0, 0, "game: off");
            Add(T3, "grp.fireControl", "FireControl/GunChannelLimit", "Guns without a free channel: local control", "", Kind.Bool, 0, 0, 0, "game: off");
            Add(TG, "grp.aiming", "Guns/AimBeyondMaxRange", "Aim guns at targets beyond max range", "", Kind.Bool, 0, 0, 0, "game: off");
            Add(TG, "grp.aiming", "Guns/RerollAimError", "Unstick guns at long range", "", Kind.Bool, 0, 0, 0, "game: off");
            Add(TG, "grp.aiming", "Guns/BarrelScatter", "Scatter for every barrel of a turret", "", Kind.Bool, 0, 0, 0, "game: off");
            Add(TG, "grp.aiming", "Guns/BigGunsLocalSolution", "Big guns build a solution without a director", "", Kind.Bool, 0, 0, 0, "game: off");
            Add(TG, "grp.global", "Guns/MaxAccuracyMeters", "Maximum accuracy", "m", Kind.Number, 1f, 0f, 100f, "~40");
            Add(TG, "grp.global", "Guns/AccuracyMultiplier", "Shell accuracy", "x", Kind.Number, 0.1f, 0.1f, 20f, "1");
            Add(TG, "grp.global", "Guns/FireControlAccuracy", "Aiming accuracy", "x", Kind.Number, 0.1f, 0.1f, 20f, "1");
            Add(TG, "grp.ammoFix", "Guns/AmmunitionFix", "Custom accuracy and power", "", Kind.Bool, 0, 0, 0, "game: off");
            Add(TG, "grp.directors", "Guns/WeakDirectorsLift", "Weak directors boost", "%", Kind.Number, 5f, 0f, 100f, "game: 0");
            Add(TG, "grp.directors", "Guns/SolutionBuildSpeed", "Firing solution build speed", "x", Kind.Number, 0.05f, 0.05f, 5f, "game: 1");
            Add(TG, "grp.directors", "Guns/LocalSolutionGrowth", "Solution build speed without a director", "%", Kind.Number, 5f, 10f, 200f, "game: n/a");
            Add(TG, "grp.directors", "Guns/ManeuverSolutionLoss", "Solution lost when the target manoeuvres", "%", Kind.Number, 1f, 0f, 50f, "game: 0");
            Add(TG, "grp.directors", "Guns/LocalControlGain", "Solution quality without a director", "x", Kind.Number, 0.05f, 0.1f, 10f, "game: 1");
            Add(TG, "grp.directors", "Guns/StrictEmconOpticalShare", "Gun radar director on Strict EMCON: share of radar accuracy", "%", Kind.Number, 5f, 0f, 100f, "game: off");
            Add(TG, "grp.directors", "Guns/OpticalDirectorMinSolution", "Solution quality with optical directors", "x", Kind.Number, 0.1f, 0f, 5f, "game: 1");
            Add(TG, "grp.directors", "Guns/RadarDirectorMinSolution", "Solution quality with radar directors", "x", Kind.Number, 0.1f, 0f, 5f, "game: 1");
            Add(TA, "grp.targeting", "Fire/EngageByPlotRelation", "Disable AI cheating", "", Kind.Bool, 0, 0, 0, "game: off");
            Add(TA, "grp.chasing", "Ships/NoRbuSurfaceChase", "No chasing surface targets with ASW mortars", "", Kind.Bool, 0, 0, 0, "game: off");
            Add(TA, "grp.chasing", "Ships/NoAirChase", "No chasing air targets", "", Kind.Bool, 0, 0, 0, "game: off");
            Add(T5, "grp.diagnostics", "General/Debug", "Debug trace", "", Kind.Bool, 0, 0, 0);
            AddAction(T5, "grp.diagnostics", "Debug/ScreenshotTabs", "Screenshot all tabs now", StartShots);
        }

        // ------------------------------------------------------------ colours (the game's dark panels)
        static readonly Color CBg = WinStyle.Bg, CCard = WinStyle.Card, CLine = WinStyle.Line, CText = WinStyle.Text, CDim = WinStyle.Dim,
                              CWarn = WinStyle.Warn, CTitle = WinStyle.Title, CField = WinStyle.Field, CHl = WinStyle.Hl;

        static SolidColorBrush B(Color c) { return WinStyle.B(c); }

        // ------------------------------------------------------------ state
        // the window itself (Rgreat.Kit): where it is, its size, dragging, resizing, the game's keyboard
        static readonly WinFrame _f = MakeFrame();

        static WinFrame MakeFrame()
        {
            var f = new WinFrame(820f, 805f);   // screen pixels: converted to UI units at the first opening
            f.Id = SelfId;
            f.Trace = s => Trace(s);
            f.ResizeTip = () => Loc.T("set.resizeTip");
            f.LanguageLabel = () => Loc.T("lang.caption");
            f.Languages = () => { var l = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("", Loc.T("lang.default")) }; l.AddRange(Loc.GameLanguages()); return l; };
            f.LanguageChanged = () => { Loc.Refresh(); _redraw = 2; };   // the window is rebuilt a couple of frames later (Tick)
            return f;
        }
        static TextBlock _help, _status;
        static StackPanel _body, _tabBar;
        static ScrollViewer _scroll;   // the scroll area of the options
        static TabScroll _tabScroll;   // the one-line strip of tabs (Rgreat.Kit)
        static readonly Dictionary<string, string> _work = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        static AwcPlugin P { get { return AwcPlugin.Instance; } }

        static ConfigEntryBase Entry(string key)
        {
            if (P == null) return null;
            foreach (var e in P.Entries) if (string.Equals(e.Definition.Section + "/" + e.Definition.Key, key, StringComparison.OrdinalIgnoreCase)) return e;
            return null;
        }

        public static bool IsOpen { get { return _f.Attached; } }

        // ------------------------------------------------------------ the switch between the mods' windows (Rgreat.Hub.SettingsHub)
        const string SelfId = "alternative-weapons-control";
        static bool _kept;   // taken off the screen by the switch to another mod's window, the edits kept (Open puts them back)
        public static bool IsDirty() { return (_kept || IsOpen) && Dirty(); }
        public static bool Kept() { return _kept; }
        public static void Hide() { Close(); _kept = true; }
        public static void CommitKept() { Commit(true); _kept = false; }
        public static void DiscardKept() { Load(); Close(); _kept = false; }
        /// <summary>Cancel / ✕: the edits are dropped, here and in the windows of the other mods that are kept.</summary>
        static void Cancel() { Rgreat.Hub.SettingsHub.DiscardOthers(SelfId); Load(); Close(); }

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
                    try { shown = _f.Root != null && _f.Root.IsVisible; } catch { }
                    if (shown) return;
                }
                Close();
                var panel = _f.Locate(anchor, other);
                EnsureAmmoFields();
                if (_kept) _kept = false; else Load();   // put back after a switch to another mod's window: the edits are kept
                BuildWindow();
                _f.Mount(panel);
                Show();
            }
            catch (Exception e) { Warn("Settings window could not be opened: " + e.Message); Trace("SettingsWindow.Open: " + e); }
        }


        static void Close()
        {
            Typing(false);
            _f.Unmount();
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
            AmmoReload();
            foreach (var f in Fields) if (IsAmmo(f.Key)) _work[f.Key] = AmmoSavedText(f.Key);
        }

        static string Saved(Field f) { if (IsAmmo(f.Key)) return AmmoSavedText(f.Key); var e = Entry(f.Key); return e == null ? "" : e.GetSerializedValue(); }
        static string Default(Field f) { if (IsAmmo(f.Key)) return AmmoDefaultText(f.Key); var e = Entry(f.Key); return e == null ? "" : TomlTypeConverter.ConvertToString(e.DefaultValue, e.SettingType); }
        static string Get(Field f) { string v; return _work.TryGetValue(f.Key, out v) ? v : ""; }

        static bool Same(string a, string b)
        {
            bool p, q;
            if (bool.TryParse((a ?? "").Trim(), out p) && bool.TryParse((b ?? "").Trim(), out q)) return p == q;
            float x, y;
            if (TryNum(a, out x) && TryNum(b, out y)) return Math.Abs(x - y) < 1e-5f;
            return string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
        }

        static bool Dirty() { if (AmmoListDirty()) return true; foreach (var f in Fields) if (!Same(Get(f), Saved(f))) return true; return false; }


        // ------------------------------------------------------------ building

        static TextBlock Text(string s, Color c, float size = 13f) { return _f.Text(s, c, size); }
        static Button Btn(string s, Action click, float minW = 0f) { return _f.Btn(s, click, minW); }
        static TextBox Edit(string text, float width) { return _f.Edit(text, width); }
        static void Typing(bool on) { _f.Typing(on); }
        static Path Cross(float size) { return WinStyle.Cross(size); }
        static bool TryNum(string s, out float v) { return WinStyle.TryNum(s, out v); }
        static float Num(string s) { return WinStyle.Num(s); }
        static TextBlock GroupTitle(string s) { return WinStyle.GroupTitle(s); }
        static DependencyObject SafeParent(DependencyObject e) { return WinStyle.SafeParent(e); }



        static void BuildWindow(bool keepRoot = false)
        {
            if (keepRoot && _f.Root != null) _f.Root.Children.Clear();
            else _f.NewRoot();
            var win = _f.Window();
            var parts = new List<UIElement>();
            parts.Add(_f.TitleBar(Loc.T("set.title"), Cancel));
            var modRow = _f.ModRow();   // the installed mods: a click switches to that mod's window
            if (modRow != null) parts.Add(modRow);

            _tabBar = new StackPanel { Orientation = Orientation.Horizontal };
            _tabScroll = new TabScroll(_tabBar, B(CTitle), B(CLine), B(CText));   // one line, scrolled by the arrows at its ends / the wheel; the chosen tab is kept in view
            parts.Add(_f.ToolRow(_tabScroll.Root, Loc.T("set.search"), () => Show(), () => TabChanged(_curTab), RevertTab, Loc.T("set.revertTab")));   // the strip of tabs, the search, the revert of the tab

            _body = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0f, 6f, 0f, 6f) };
            var scroll = _scroll = new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            var area = _f.Card(scroll);
            parts.Add(area);

            parts.Add(_f.HelpPane(Loc.T("set.hint")));
            _help = _f.Help;

            parts.Add(_f.Footer(null, out _status, Loc.T("set.cancel"), Cancel,   // the saved values back: edits not applied are dropped (here and in the other mods' windows)
                                Loc.T("set.apply"), () => Commit(false), Loc.T("set.ok"), () => Commit(true)));
            _f.Assemble(win, parts, area);
        }

        // ------------------------------------------------------------ rows

        // the "changed" markers of the rows on the page: after Apply they are refreshed in place (no rebuild: the scroll position and the page stay)
        static readonly List<Action> _marks = new List<Action>();

        static void Show()
        {
            if (_body == null) return;
            _marks.Clear();
            _body.Children.Clear();
            bool searching = !string.IsNullOrEmpty(_f.Query);
            if (searching && !TabHasHits(_curTab)) foreach (var t0 in Tabs) if (TabHasHits(t0)) { _curTab = t0; break; }   // the search: a tab with nothing found is left
            if (_tabBar != null)
            {
                _tabBar.Children.Clear();
                Border chosen = null;
                foreach (var t in Tabs)
                {
                    if (searching && !TabHasHits(t)) continue;   // the tabs with nothing found are hidden
                    string tab = t;
                    bool sel = tab == _curTab;
                    var b = _f.TabButton(Loc.T(tab), sel, TabChanged(tab), () => { _curTab = tab; Show(); });
                    _tabBar.Children.Add(b);
                    if (sel) chosen = b;
                }
                if (_tabScroll != null && chosen != null) _tabScroll.Reveal(chosen);
            }
            if (!string.IsNullOrEmpty(_f.Query)) { ShowFound(); UpdateStatus(); return; }
            string group = null;
            if (_curTab == "tabs.gunsAccuracy") _body.Children.Add(SubBar());
            foreach (var f in Fields)
            {
                if (f.Tab != _curTab || f.Group == "grp.ammoList" || !ParentOn(f)) continue;
                if (_curTab == "tabs.gunsAccuracy" && SubOf(f) != _curSub) continue;
                if (f.Group != group)
                {
                    group = f.Group;
                    if (group == "grp.directors") PresetBar();
                    else { string gr = group, tabNow = _curTab; _body.Children.Add(_f.GroupHeader(Loc.T(gr), () => GroupChanged(tabNow, gr), () => RevertWhere(x => x.Tab == _curTab && x.Group == gr, false), Loc.T("set.revertGroup"))); }
                }
                _body.Children.Add(Row(f));
            }
            if (_curTab == "tabs.gunsAccuracy" && _curSub == "sub.general" && Flag("Guns/AmmunitionFix")) AmmoTable();
            UpdateStatus();
        }

        static bool FieldHit(Field f)
        {
            return f.Group != "grp.ammoList" && ParentOn(f) && _f.Match(Loc.T("lbl." + f.Key));
        }

        static bool TabHasHits(string tab) { foreach (var f in Fields) if (f.Tab == tab && FieldHit(f)) return true; return false; }

        /// <summary>The search: the options of the tab that fit, all its pages one under the other.</summary>
        static void ShowFound()
        {
            string group = null; int n = 0;
            foreach (var f in Fields)
            {
                if (f.Tab != _curTab || !FieldHit(f)) continue;
                FrameworkElement row;
                try { row = Row(f); } catch (Exception e) { Trace("settings row '" + f.Key + "' failed: " + e); continue; }   // the title of a group goes in only with a row under it
                if (f.Group != group) { group = f.Group; _body.Children.Add(GroupTitle(Loc.T(group))); }
                _body.Children.Add(row); n++;
            }
            if (n == 0) { var t = Text(Loc.T("set.nothing"), CDim, 13f); t.Margin = new Thickness(12f, 14f, 12f, 0f); _body.Children.Add(t); }
        }

        static bool GroupChanged(string tab, string group)
        {
            foreach (var f in Fields) if (f.Tab == tab && f.Group == group && f.Kind != Kind.Action && !Same(Get(f), Saved(f))) return true;
            return false;
        }

        /// <summary>The saved values back into the options picked (not applied edits dropped; nothing closes). ammo: the list of ammunition too.</summary>
        static void RevertWhere(Func<Field, bool> pick, bool ammo)
        {
            foreach (var f in Fields) if (f.Kind != Kind.Action && pick(f)) _work[f.Key] = Saved(f);
            if (ammo) { AmmoReload(); foreach (var f in Fields) if (IsAmmo(f.Key)) _work[f.Key] = AmmoSavedText(f.Key); }
            _f.KeepScroll(_scroll, Show);
        }

        static void RevertTab() { string t = _curTab; RevertWhere(x => x.Tab == t, t == "tabs.gunsAccuracy"); }

        static void UpdateTabDots()
        {
            if (_tabBar == null) return;
            int i = 0;
            foreach (var t in Tabs)
            {
                if (!string.IsNullOrEmpty(_f.Query) && !TabHasHits(t)) continue;   // the tabs with nothing found are not in the bar (search)
                if (i >= _tabBar.Children.Count) break;
                var b = _tabBar.Children[i++] as Border; var sp = b == null ? null : b.Child as StackPanel;
                if (sp == null) continue;
                bool has = sp.Children.Count > 1, want = TabChanged(t);
                if (has == want) continue;
                if (want) sp.Children.Insert(0, new Ellipse { Width = 6f, Height = 6f, Fill = B(CWarn), Margin = new Thickness(0f, 0f, 6f, 0f), VerticalAlignment = VerticalAlignment.Center });
                else sp.Children.RemoveAt(0);
            }
        }

        static bool TabChanged(string tab)
        {
            if (tab == "tabs.gunsAccuracy" && AmmoListDirty()) return true;
            foreach (var f in Fields) if (f.Tab == tab && !Same(Get(f), Saved(f))) return true;
            return false;
        }

        static void UpdateStatus()
        {
            _f.UpdateModDots();
            if (_status == null) return;
            bool any = AmmoListDirty();
            foreach (var f in Fields) if (!Same(Get(f), Saved(f))) { any = true; break; }
            _status.Text = Loc.T(any ? "set.changed" : "set.clean");
            _f.UpdateTools();
        }


        /// <summary>A row with a button (Kind.Action): the label, the button, the help on hover.</summary>
        static FrameworkElement ActionRow(Field f)
        {
            var g = new Grid { Background = B(Color.FromArgb(0, 0, 0, 0)) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1f, GridUnitType.Star), MinWidth = 330f });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1f, GridUnitType.Star), MinWidth = 200f });
            var outer = new Border { Child = g, Padding = new Thickness(12f, 4f, 6f, 4f), Background = B(Color.FromArgb(0, 0, 0, 0)) };
            var lab = Text(Loc.T("lbl." + f.Key), CText); lab.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(lab);
            var btn = Btn(Loc.T("btn.run"), f.Act, 70f); btn.HorizontalAlignment = HorizontalAlignment.Left; btn.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(btn, 1); g.Children.Add(btn);
            outer.MouseEnter += (o, e) =>
            {
                outer.Background = B(CHl);
                _f.ShowHelp(Loc.T("lbl." + f.Key), Loc.Or("help." + f.Key, ""));
            };
            outer.MouseLeave += (o, e) => outer.Background = B(Color.FromArgb(0, 0, 0, 0));
            return outer;
        }

        // ---- screenshots of every tab (the settings kit, SettingsShotKit.cs): the window fitted to each tab, a PNG in BepInEx/AlternativeWeaponsControl_screens
        /// <summary>The screenshots of the tabs of the window on the screen (the Game Interface\'s Debug runs it for every mod).</summary>
        internal static System.Collections.IEnumerator ShotsRun() { return SettingsShots.Run(new ShotAdapter(), System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "AlternativeWeaponsControl_screens"), s => Trace(s), s => Warn(s)); }

        static void StartShots()
        {
            if (P == null || SettingsShots.Busy) return;
            P.StartCoroutine(SettingsShots.Run(new ShotAdapter(), System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "AlternativeWeaponsControl_screens"), s => Trace(s), s => Warn(s)));
        }

        sealed class ShotAdapter : IShotWindow
        {
            string _tab0, _sub0;
            public bool Begin() { if (_f.Root == null || _body == null || !_f.Root.IsVisible) return false; _tab0 = _curTab; _sub0 = _curSub; return true; }
            public int TabCount { get { return Tabs.Count; } }
            public bool TabUsable(int tab) { return true; }
            public int Pages(int tab) { return Tabs[tab] == "tabs.gunsAccuracy" ? 2 : 1; }   // the inner tabs of "Accuracy"
            public void Show(int tab, int page) { _curTab = Tabs[tab]; if (_curTab == "tabs.gunsAccuracy") _curSub = page == 1 ? "sub.directors" : "sub.general"; SettingsWindow.Show(); }
            public string Name(int tab, int page) { return Loc.T(Tabs[tab]) + (Tabs[tab] == "tabs.gunsAccuracy" ? "_p" + (page + 1) : ""); }
            public bool Rect(out float x, out float y, out float w, out float h) { return ShotFit.Rect(_f.Root, out x, out y, out w, out h); }
            public void FitMax() { _f.ClearHelp(); ShotFit.Max(_f.Root, _f.Move); }
            public void FitWidth() { ShotFit.Width(_f.Root, _body, _scroll, _f.Move, _f.ShotMinW); }
            public void FitHeight() { ShotFit.Height(_f.Root, _scroll, _f.Move, 420f); }
            public void End() { _curTab = _tab0; _curSub = _sub0; ShotFit.Restore(_f.Root, _f.Move, _f.W, _f.H); SettingsWindow.Show(); }
        }

        static FrameworkElement Row(Field f)
        {
            if (f.Kind == Kind.Action) return ActionRow(f);
            var g = new Grid { Background = B(Color.FromArgb(0, 0, 0, 0)) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1f, GridUnitType.Star), MinWidth = 330f });   // the names take what the window gains
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1f, GridUnitType.Star), MinWidth = 200f });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110f) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30f) });
            var outer = new Border { Child = g, Padding = new Thickness(12f, 4f, 6f, 4f), Background = B(Color.FromArgb(0, 0, 0, 0)) };

            var lab = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var dot = new Ellipse { Width = 6f, Height = 6f, Fill = B(CWarn), Margin = new Thickness(0f, 0f, 6f, 0f), VerticalAlignment = VerticalAlignment.Center };
            lab.Children.Add(dot);
            lab.Children.Add(Text(Loc.T("lbl." + f.Key), CText));
            g.Children.Add(lab);

            Action mark = () => { dot.Visibility = Same(Get(f), Saved(f)) ? Visibility.Hidden : Visibility.Visible; UpdateStatus(); UpdateTabDots(); };
            _marks.Add(mark);
            Action refresh;
            var editor = f.Kind == Kind.Bool ? BoolEditor(f, mark, out refresh) : f.Kind == Kind.Lang ? LangEditor(f, mark, out refresh) : f.Kind == Kind.Pick ? PickEditor(f, mark, out refresh) : NumberEditor(f, mark, out refresh);
            Grid.SetColumn(editor, 1);
            g.Children.Add(editor);

            string d = Default(f).Replace("True", Loc.T("set.on")).Replace("False", Loc.T("set.off")).Replace("true", Loc.T("set.on")).Replace("false", Loc.T("set.off"));
            if (f.Kind == Kind.Lang) d = Loc.T("set.gameLang");
            if (f.Kind == Kind.Pick) d = PickText(Default(f));
            string gv = f.Game;
            var game = new TextBlock { Text = Loc.F("set.default", d) + (f.Game == null ? "" : "\n" + (f.Game == "game: off" ? Loc.T("set.gameOff") : Loc.F("set.gameVal", gv))), Foreground = B(CDim), FontSize = 11f, TextAlignment = TextAlignment.Right,
                                       VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(game, 2);
            g.Children.Add(game);

            var rst = _f.ResetButton(Loc.T("set.resetTip"), () => { _work[f.Key] = Default(f); refresh(); mark(); DepsChanged(f.Key); });
            Grid.SetColumn(rst, 3);
            g.Children.Add(rst);

            mark();
            _f.Hover(outer, () => Loc.T("lbl." + f.Key), () => { var en = Entry(f.Key); return Loc.Or("help." + f.Key, en != null && en.Description != null ? en.Description.Description : ""); });
            return outer;
        }




        static FrameworkElement NumberEditor(Field f, Action mark, out Action refresh)
        {
            return _f.Number(() => Get(f), null, v => _work[f.Key] = v.ToString("0.##", CultureInfo.InvariantCulture), f.Step, f.Min, f.Max,
                             string.IsNullOrEmpty(f.Unit) ? null : Loc.Or("unit." + f.Key, f.Unit), mark, out refresh);
        }

        static FrameworkElement BoolEditor(Field f, Action mark, out Action refresh)
        {
            return _f.Check(() => Get(f).Trim().Equals("true", StringComparison.OrdinalIgnoreCase), on => { _work[f.Key] = on ? "true" : "false"; mark(); DepsChanged(f.Key); }, out refresh);
        }

        /// <summary>The text of a Pick value: the weapon status names of the game (Free / Tight / Hold).</summary>
        static string PickText(string o) { return Loc.T("status." + (o ?? "").ToLowerInvariant()); }

        static FrameworkElement PickEditor(Field f, Action mark, out Action refresh)
        {
            var labels = new List<string>();
            foreach (var o in f.Options) labels.Add(PickText(o));
            return CodeCombo(f, new List<string>(f.Options), labels, mark, out refresh);
        }

        /// <summary>A drop-down over a list of values (codes) with their texts.</summary>
        static FrameworkElement CodeCombo(Field f, List<string> codes, List<string> labels, Action mark, out Action refresh)
        {
            return _f.Combo(labels, () => { int i = codes.FindIndex(o => string.Equals(o, (Get(f) ?? "").Trim(), StringComparison.OrdinalIgnoreCase)); return i < 0 ? 0 : i; },
                            i => { if (i >= codes.Count || codes[i] == (Get(f) ?? "").Trim()) return; _work[f.Key] = codes[i]; mark(); }, 200f, 24f, out refresh);
        }

        static FrameworkElement LangEditor(Field f, Action mark, out Action refresh)
        {
            var codes = new List<string> { "" };
            foreach (var l in Loc.GameLanguages()) if (!codes.Contains(l.Key)) codes.Add(l.Key);
            var names = new Dictionary<string, string>();
            foreach (var l in Loc.GameLanguages()) names[l.Key] = l.Value;
            var labels = new List<string>();
            foreach (var c in codes) labels.Add(string.IsNullOrEmpty(c) ? Loc.T("set.gameLang") : (names.ContainsKey(c) ? names[c] + " (" + c + ")" : c));
            return CodeCombo(f, codes, labels, mark, out refresh);
        }

        // ------------------------------------------------------------------ presets of the fire-control accuracy settings (Guns tab, Accuracy)
        // A preset only fills the settings below with values (nothing is stored but those settings); the bar shows which preset the current values equal.
        // base = the game as it is; advanced = weak directors lifted, local control a little worse (right for the game and for rule sets with weak
        // directors); ww2 = the mod's default now, for the WW2 pack (directors of gain 0.1-0.6): stronger lift, radars better than optics, local control below the directors.
        static readonly string[] PresetKeys = { "Guns/WeakDirectorsLift", "Guns/LocalControlGain", "Guns/OpticalDirectorMinSolution", "Guns/RadarDirectorMinSolution", "Guns/SolutionBuildSpeed" };
        static readonly string[] PresetNames = { "base", "advanced", "ww2" };
        static readonly float[][] PresetValues =
        {
            new[] { 0f,   1f,    1f, 1f,    1f },
            new[] { 50f,  0.75f, 1f, 1f,    1f },
            new[] { 10f,  0.5f,  1f, 1f,    0.5f },
        };

        static string CurrentPreset()
        {
            for (int p = 0; p < PresetNames.Length; p++)
            {
                bool all = true;
                for (int i = 0; i < PresetKeys.Length && all; i++)
                {
                    string v; float f;
                    all = _work.TryGetValue(PresetKeys[i], out v) && TryNum(v, out f) && Math.Abs(f - PresetValues[p][i]) < 0.0005f;
                }
                if (all) return PresetNames[p];
            }
            return null;
        }

        static void PresetBar()
        {
            string cur = CurrentPreset();
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12f, 2f, 12f, 4f) };
            var t = Text(Loc.T("preset.title") + ":  ", CDim); t.Margin = new Thickness(0f, 0f, 4f, 0f);
            bar.Children.Add(t);
            foreach (var n in PresetNames)
            {
                string name = n;
                bar.Children.Add(Btn(Loc.T("preset." + name) + (cur == name ? "  ✓" : ""), () =>
                {
                    int p = Array.IndexOf(PresetNames, name);
                    for (int i = 0; i < PresetKeys.Length; i++) _work[PresetKeys[i]] = PresetValues[p][i].ToString("0.##", CultureInfo.InvariantCulture);
                    _reshow = 2;
                }, 90f));
            }
            if (cur == null) { var c = Text(Loc.T("preset.custom"), CDim, 12f); c.Margin = new Thickness(6f, 0f, 0f, 0f); bar.Children.Add(c); }
            _body.Children.Add(bar);
            _body.Children.Add(new TextBlock { Text = Loc.T("preset.note"), Foreground = B(CDim), FontSize = 11f, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12f, 0f, 12f, 6f) });
        }

        static void Commit(bool close)
        {
            Typing(false);
            Rgreat.Hub.SettingsHub.CommitOthers(SelfId);   // the edits kept in the other mods' windows are applied too
            var langEntry = Entry("General/Language");
            string langBefore = langEntry == null ? "" : langEntry.GetSerializedValue();
            foreach (var f in Fields)
            {
                var e = Entry(f.Key);
                if (e == null || Same(Get(f), e.GetSerializedValue())) continue;
                try { e.SetSerializedValue(Get(f)); } catch (Exception ex) { Trace(f.Key + ": " + ex.Message); }
            }
            try { AmmoCommit(); } catch (Exception ex) { Trace("ammo commit: " + ex); }
            try { P.Cfg.Save(); } catch (Exception ex) { Warn("Saving the settings failed: " + ex.Message); }
            if (close) { Close(); return; }
            Load();
            if (langEntry != null && !Same(langBefore, langEntry.GetSerializedValue()))
            {
                Show();
                _redraw = 2;   // another language: the whole window is rebuilt a couple of frames later (not inside the click of a button that is being replaced)
                return;
            }
            // nothing else changes how the window looks: only the "changed" markers go off, the page and its scroll position stay
            foreach (var m in _marks.ToArray()) { try { m(); } catch { } }
            UpdateStatus(); UpdateTabDots();
        }

        static int _redraw, _reshow;

        static bool Flag(string key) { string v; return _work.TryGetValue(key, out v) && v.Trim().Equals("true", StringComparison.OrdinalIgnoreCase); }

        /// <summary>A setting whose switch is off is not shown (the switch is another setting, Field.Parent).</summary>
        static bool ParentOn(Field f) { return f.Parent == null || Flag(f.Parent); }

        /// <summary>A switch was clicked: the settings that depend on it appear or disappear a couple of frames later (not inside the click of the box being replaced).</summary>
        static void DepsChanged(string key)
        {
            if (key == "Guns/AmmunitionFix") { _reshow = 2; return; }
            foreach (var f in Fields) if (f.Parent == key) { _reshow = 2; return; }
        }

        /// <summary>Called every frame: the delayed rebuild of the window after Apply (the texts follow the new language).</summary>
        public static void Tick()
        {
            if (_reshow > 0 && --_reshow == 0 && IsOpen && _f.Root != null) { try { _f.KeepScroll(_scroll, Show); } catch (Exception ex) { Trace("reshow: " + ex); } }
            if (_redraw <= 0) return;
            if (--_redraw > 0) return;
            if (_f.Root == null || !IsOpen) return;
            try { BuildWindow(true); Show(); } catch (Exception ex) { Trace("rebuild: " + ex); }
        }
    }
}
