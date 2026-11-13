using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace AlternativeWeaponsControl
{
    /// <summary>
    /// Alternative Weapons Control - Sea Power mod: a "Weapons" tab in the Unit Status window of ships and submarines.
    ///  - The unit's launchers by kind (SAM, anti-ship, guns, CIWS...), grouped by system, with state, target, ammunition and
    ///    fire-control channels; Fire (target on the map, salvo menu), cease fire and reload for a group, an ammunition or one launcher.
    ///  - Per launcher and target domain (air, missiles, surface, submarines, land) whether the AI may use it automatically,
    ///    merged with the unit's Standing Orders; kept in the mission save (WeaponsModel.cs, WeaponsAi.cs).
    ///  - Fire control: a launcher's own target waits for a free channel; guns take one director (GunOneDirector) and, optionally,
    ///    aim on local control without a free channel (GunChannelLimit).
    ///  - The tab button gets (i) with the legend and a gear for the settings window.
    /// Follows its tick in the game's Mods menu (Anchor Chain starts every mod folder): while it is off nothing is changed.
    /// No compile-time dependency on the game's assemblies: types and members are looked up by name.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public partial class AwcPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.rgreat.seapower.alternativeweaponscontrol";
        public const string PluginName = "Alternative Weapons Control";
        public const string PluginVersion = "1.5.0";

        public static readonly string BuildId = ReadBuildId();

        static AwcPlugin _inst;
        public static AwcPlugin Instance { get { return _inst; } }
        static volatile bool Enabled;                   // ticked in the game's Mods menu (always true for a BepInEx/plugins copy)
        static AwcPlugin Active { get { return Enabled ? _inst : null; } }
        static bool _debug;
        static string _traceFile;
        bool _patchTried;

        // ------------------------------------------------------------ settings
        public ConfigFile Cfg;
        internal ConfigEntry<string> _language; internal ConfigEntry<bool> _debugEntry, _gunChannels, _gunOneDirector, _noRbuSurfaceChase, _noAirChase;
        internal ConfigEntry<string> _secOrder;
        internal ConfigEntry<float> _clickVolume, _samMinutes, _samAutoRange, _gunMinError, _accuracyMult, _fcAccuracy, _weakBoost, _gainLocal, _minSolOptical, _minSolRadar, _solSpeed, _solLoss, _localGrowth;
        internal ConfigEntry<bool> _ammoFix, _plotHostile, _tabAir, _tabLand, _samButton, _zoneFire, _samAuto, _samKeep, _samAnim, _gunAimBeyond, _engageOutside, _gunMemory, _gunReroll, _barrelScatter, _bigLocal, _clickAllGuns, _gunLocalFire, _bottomFire, _secTargets;
        public static string UserDir { get; private set; }

        /// <summary>The settings the window shows (in this order).</summary>
        internal readonly List<ConfigEntryBase> Entries = new List<ConfigEntryBase>();

        static string ReadBuildId()
        {
            try
            {
                foreach (AssemblyMetadataAttribute a in typeof(AwcPlugin).Assembly.GetCustomAttributes(typeof(AssemblyMetadataAttribute), false))
                    if (a.Key == "Build") return a.Value;
            }
            catch { }
            return "unknown";
        }

        static void Trace(string msg)
        {
            if (!_debug) return;
            try { File.AppendAllText(_traceFile, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + msg + Environment.NewLine); }
            catch { }
        }

        // A trace line that repeats (the same key) is written once per `sec` seconds of real time: the selector and the demand test run many times a second.
        static readonly Dictionary<string, float> _traceAt = new Dictionary<string, float>();
        static void TraceLimited(string key, float sec, string msg)
        {
            if (!_debug) return;
            float now = UnityEngine.Time.realtimeSinceStartup, last;
            if (_traceAt.TryGetValue(key, out last) && now - last < sec) return;
            if (_traceAt.Count > 2000) _traceAt.Clear();
            _traceAt[key] = now;
            Trace(msg);
        }

        void Verbose(string msg) { Trace(msg); }   // details go to the trace only
        static void Say(string msg) { if (_inst != null) _inst.Logger.LogInfo(msg); Trace(msg); }   // startup diagnostics: BepInEx log + trace
        static void Warn(string msg) { if (_inst != null) _inst.Logger.LogWarning(msg); Trace("WARN " + msg); }

        static HarmonyMethod Hm(string name)
        {
            var m = typeof(AwcPlugin).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (m == null) throw new MissingMethodException("AwcPlugin." + name + " (patch method of the mod) not found");
            return new HarmonyMethod(m);
        }

        /// <summary>One patch: a missing game member or a failing Harmony call is reported by name (type.member) and does not stop the other patches.</summary>
        static void PatchOne(Harmony harmony, MethodBase target, string what, string prefix = null, string postfix = null, string finalizer = null)
        {
            if (target == null) { Warn(what + " not found in the game: the feature that needs it is off"); return; }
            try { harmony.Patch(target, prefix: prefix == null ? null : Hm(prefix), postfix: postfix == null ? null : Hm(postfix), finalizer: finalizer == null ? null : Hm(finalizer)); }
            catch (Exception e) { Warn("patching " + what + " failed: " + e.Message); }
        }

        internal static Assembly GameAssembly()
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                if (a.GetName().Name == "Seapower-Scripts") return a;
            return null;
        }

        static int Count(object c) { var col = c as ICollection; return col == null ? 0 : col.Count; }

        /// <summary>
        /// The settings file: Sea Power_Data/StreamingAssets/user/AlternativeWeaponsControl/AlternativeWeaponsControl.cfg,
        /// as Alternative Damage Model keeps its own (BepInEx/config when that folder cannot be used).
        /// </summary>
        void OpenSettingsFile()
        {
            try
            {
                UserDir = Path.Combine(Path.Combine(UnityEngine.Application.streamingAssetsPath, "user"), "AlternativeWeaponsControl");
                Directory.CreateDirectory(UserDir);
                Cfg = new ConfigFile(Path.Combine(UserDir, "AlternativeWeaponsControl.cfg"), true);
            }
            catch (Exception e)
            {
                Logger.LogWarning("Settings in StreamingAssets/user not available (" + e.Message + "); BepInEx/config is used.");
                Cfg = Config;
                UserDir = Paths.ConfigPath;
            }
        }

        void Awake()
        {
            _inst = this;
            OpenSettingsFile();
            _clickVolume = Cfg.Bind("UI", "ClickVolume", 0.5f,
                "Volume of the click of the tab's buttons and switches, 0..1 (0 = no sound; the game's own buttons make none).");
            _gunOneDirector = Cfg.Bind("FireControl", "GunOneDirector", true,
                "A gun opening fire takes a target channel on ONE of its directors (the one already tracking its target, or the best free " +
                "one), not on all of them as in the game - so the other directors stay free for other guns and for missiles sharing them " +
                "(e.g. Mk92 + STIR on the Adelaide, Top Bow + Four Eyes on the Sverdlov).");
            _gunChannels = Cfg.Bind("FireControl", "GunChannelLimit", true,
                "A gun whose directors have no free target channel for its target aims on local control (the game's penalty when no " +
                "director works: lower solution gain, larger error in the dark) until a channel frees. Off = as in the game: a gun aims " +
                "with its best working director however many targets that director already tracks.");
            _samButton = Cfg.Bind("UI", "SamReadyButton", true,
                "A readiness button on the rows of launchers that warm up before firing (SAM): click = load the missiles now, click again = put them away.");
            _samMinutes = Cfg.Bind("UI", "SamReadyMinutes", 15f,
                "Minutes the launcher stays ready after the button, if it is not used: then the missiles are put away.");
            _samAuto = Cfg.Bind("Ships", "SamAutoCharge", true,
                "Units (the enemy's too) charge their SAM launchers (missiles onto the rails) when an unknown aircraft comes closer than the " +
                "range below. Only contacts the unit's crew has already processed (the game's OODA loop) count. Off = the game's own warm-up, " +
                "which starts when a hostile target is known.");
            _samKeep = Cfg.Bind("Ships", "SamKeepLoaded", true,
                "A charged (hot) launcher keeps the missiles on its rails after a launch: the game unloads them into the magazine " +
                "when the launcher has been idle for a while (a second missile 'disappears' after the first shot). Off = as in the game.");
            _samAnim = Cfg.Bind("Ships", "SamAnimFix", true,
                "Missiles move on load and unload (up from / down under the deck) even when the ammunition file has no position data " +
                "under the launcher's exact system name (e.g. SA-N-1A with an SA-N-1 ammunition): the nearest matching entry is used.");
            _samAutoRange = Cfg.Bind("Ships", "SamAutoChargeRange", 30f, "Distance (nm) of an approaching unidentified aircraft (its range to the ship is shrinking) that charges the SAM launchers.");
            _noRbuSurfaceChase = Cfg.Bind("Ships", "NoRbuSurfaceChase", true,
                "A ship attacking a surface target with ASW mortars (RBU) does not run after it to get into the mortars' range - " +
                "it keeps its course and fires once the target comes into range. All ships, the enemy's too.");
            _noAirChase = Cfg.Bind("Ships", "NoAirChase", true,
                "A ship does not run after aircraft and missiles to get them into the range of its weapons - it keeps its course " +
                "and fires once they come into range. All ships, the enemy's too.");
            _tabLand = Cfg.Bind("UI", "WeaponsTabGroundUnits", true, "The Weapons tab for ground vehicles and land installations (the units with guns / launchers of their own).");
            _tabAir = Cfg.Bind("UI", "WeaponsTabAircraft", true, "The Weapons tab for aircraft and helicopters (their weapons on the hardpoints: one group, a Fire for each ammunition type, switches by target type).");
            _secOrder = Cfg.Bind("UI", "SectionOrder", "SAM,GUNS,ANTI-SHIP,ASW", "The order of the groups of the Weapons tab (drag a group's title to change it): names separated by commas; the sections not named follow in the usual order. Empty = the usual order (SAM, ANTI-SHIP, LAND ATTACK, ASW, GUNS, CIWS, OTHER, STORES).");
            _freeAirKnots = Cfg.Bind("Fire", "UnknownAirMinKnots", 500f,
                "Speed (knots) above which an unidentified aircraft coming towards the ship (within the distance of UnknownAirMaxNm) is engaged by the Weapons tab's \"On Free: engage unidentified aircraft\". " +
                "Airliners of the game cruise at about 460-480 knots and reach 560 at most.");
            _freeAirNm = Cfg.Bind("Fire", "UnknownAirMaxNm", 20f, "Distance (nm) within which an unidentified fast aircraft coming towards the ship is engaged by the Weapons tab's \"On Free: engage unidentified aircraft\".");
            _plotHostile = Cfg.Bind("Fire", "EngageByPlotRelation", true,
                "The game's AI takes a contact for a target only if it is hostile on the plot AND its true side is hostile too. The true side is what the player cannot know, so a contact the player marked hostile (e.g. a neutral airliner) is never engaged by the player's ships on Free. On: the player's ships go by the plot only - the contacts marked hostile are engaged, the true side is ignored (no cheating). Off = as in the game.");
            _freeAirOwn = Cfg.Bind("Fire", "UnknownAirDefaultOwn", false, "The starting state of the Weapons tab's \"On Free: engage unidentified aircraft\" for your ships and your allies' ships (a switch set on a unit is kept with it).");
            _freeAirEnemy = Cfg.Bind("Fire", "UnknownAirDefaultEnemy", true, "The same for the enemy's AI ships (their AI engages such aircraft by itself).");
            _zoneFire = Cfg.Bind("UI", "FireShowsZones", true,
                "While the target is being picked after Fire, the firing arcs and range of the weapons are shown on the map and in 3D " +
                "(as the game does with Alt held over a weapon in the unit's status line).");
            _secTargets = Cfg.Bind("UI", "SectionTargetTypes", false,
                "A SAM group's title on the Weapons tab also lists the other target types of the SAM ammunition: SAM/ANTI-SHIP (a SAM that can also hit ships). The other target types of non-SAM missiles (ANTI-SHIP/ASW: a missile that hits ships and submarines) are always listed.");
            _bottomFire = Cfg.Bind("UI", "BottomPanelFire", false,
                "A click on a weapon of the panel at the bottom of the screen works like the Fire button of the Weapons tab: the weapons' firing arcs and range are shown in red on the map and in 3D and the game's red range circle is hidden. Needs 'Fire shows weapon arcs and range'.");
            _gunAimBeyond = Cfg.Bind("Guns", "AimBeyondMaxRange", true,
                "The player's guns, CIWS and machine guns turn and elevate towards a target beyond their maximum range (up to 1.5 x the range, at least 5 nm) " +
                "or outside their firing sector (as far as the sector allows) without firing. Off = as in the game: the turret stays still until the target is in range and in the sector.");
            _engageOutside = Cfg.Bind("Guns", "EngageOutsideSector", true,
                "Many big weapons (battleship and cruiser guns, some CIWS) have EngageOnlyInsideArc=True in the game: they are never given a target outside their firing sector. " +
                "On = this restriction is ignored; the weapon takes such a target and turns to the nearest edge of its sector (and does not fire until the target is inside). Applied to units loaded after the change (a mission start). Works together with the option above.");
            _gunMemory = Cfg.Bind("Guns", "RememberTargets", true,
                "A new order to fire makes the guns switch to the new target as before, but the earlier targets are not forgotten: a gun that cannot reach the new target " +
                "(out of range or outside its firing sector) goes on with the newest earlier target it can reach, and returns to the newest one when it can. Cease fire clears the list. Off = as in the game: every new order replaces the old one.");
            _gunReroll = Cfg.Bind("Guns", "RerollAimError", true,
                "The game adds a random fire-control error to the aim point of every gun and keeps it until the gun has fired. Near the maximum range that error can push the aim point beyond the gun's firing table: the gun then has no solution, " +
                "does not even turn, and never fires because the error is only redrawn after a shot - a turret stays idle for minutes while its neighbours shoot. On = such a gun draws a new error at once (only if the target itself can be hit). Off = as in the game. The player's guns only.");
            _barrelScatter = Cfg.Bind("Guns", "BarrelScatter", true,
                "The game draws the scatter of the shells once per salvo and gives it to every barrel of the mount, so the shells of a twin or triple turret fly exactly parallel and land in a line, hit or miss together. " +
                "On = each barrel after the first draws its own shell scatter (the same scatter as the game's, around the same aim point). Off = as in the game. Surface targets, naval guns of all ships.");
            _bigLocal = Cfg.Bind("Guns", "BigGunsLocalSolution", true,
                "A gun whose director is destroyed or off keeps its firing solution at 0 for ever in the game (it only follows the best working sensor), so it shoots with the full local-control error. Big turrets have local control and their own rangefinders. " +
                "On = a naval gun with a shell heavier than 2 kg (about 50 mm and more) and no working director builds its firing solution itself, as a gun without sensors does (its aiming gain stays that of local control: worse than a director). Smaller guns stay as in the game. Off = as in the game.");
            _clickAllGuns = Cfg.Bind("Guns", "ClickEngagesAllGuns", true,
                "A right click on a hostile contact with your own ship selected makes the game order ONE gun ammunition - the one with the longest range that has a usable gun at that moment " +
                "(a gun whose director is destroyed or that is reloading does not count, so its calibre is skipped). On = every gun ammunition of the ship that can reach the target (range only, the firing sector does not matter) gets the order. " +
                "Off = as in the game. The player's ships only.");
            _gunLocalFire = Cfg.Bind("Guns", "LocalControlKeepsFiring", true,
                "A gun without a working director (destroyed, switched off, or none) aims on local control. The game then holds every next shot until the previous shell has landed (spotting fire: " +
                "one shell per time of flight, 30-60 s at long range) while the gun's fire-control solution is below 95 %; with a destroyed director the solution stays at 0, so such a gun fires very rarely. " +
                "On = no such wait: a gun on local control fires at its normal rate (its accuracy stays poor, as in the game). Off = as in the game. The player's guns only.");
            _gunMinError = Cfg.Bind("Guns", "MaxAccuracyMeters", 5f,
                "Best (maximum) accuracy of guns against surface targets, in metres: the floor of the fire-control range error. The game uses about 40 m, so at close range " +
                "(up to ~1.4 nm) guns are relatively less accurate than at medium range. The scaling with distance (1.5 % of range) stays. " +
                "Set about 40 for the game's behaviour.");
            _accuracyMult = Cfg.Bind("Guns", "AccuracyMultiplier", 1f,
                "How accurate the shells themselves are: the scatter of the shells around the aim point is divided by this number, so 2 = half the scatter (twice as accurate), 0.5 = twice the scatter, 1 = as in the game. Values of the author's pack and your own values in the Ammunition table below replace the result as they are. Takes effect from the next mission start.");
            _fcAccuracy = Cfg.Bind("Guns", "FireControlAccuracy", 1f,
                "How accurately guns aim at surface targets, all guns: the aiming error of every salvo is divided by this number (2 = half the error, 1 = as in the game). It comes on top of everything the game already counts (the director, light, crew, how well the aim is locked). The scatter of the shells themselves is a different thing: see the shell accuracy above. Takes effect at once.");
            _weakBoost = Cfg.Bind("Guns", "WeakDirectorsLift", 50f,
                "How much of the gap between a very weak director and the weakest director of the game is closed, in percent. The weakest director of the game has an accuracy of 0.75 (the game calls it gain); some rule sets (e.g. WW2 packs) have directors of 0.1-0.6, and even a good one of them aims far worse than the game's own. A director below 0.75 gets its accuracy raised by this share of the difference to 0.75 (50 = halfway: 0.35 becomes 0.55); its minimum solution grows by the same factor. A better director has smaller aiming errors, builds the firing solution (the Solution % of the gun) faster and makes the guns wait for splashes less. Directors of 0.75 and more are not touched. 0 = as the directors are, 100 = all weak directors as good as the game's weakest. Takes effect at once.");
            _gainLocal = Cfg.Bind("Guns", "LocalControlGain", 0.75f,
                "Accuracy of guns without a working director (the gun crew aims by themselves; the game calls it gain): the higher, the smaller the aiming errors. The game's own value is 0.6, better than most directors of rule sets with weak directors; this is a multiplier of it. 1 = the game's value. Takes effect at once.");
            _solSpeed = Cfg.Bind("Guns", "SolutionBuildSpeed", 1f,
                "How fast guns build their firing solution (the Solution % of the gun: how well the gun has locked on the target's course and speed; a higher solution means smaller aiming errors from the target's movement and fewer waits for splashes). This is a multiplier of the speed only - the accuracy of the director does not change. It applies to directors and to guns without a director. 1 = as the game; 0.3 = three times slower. A turn or a speed change of the target still costs the same part of the solution, so with a low speed a maneuvering target keeps the solution low. Takes effect at once.");
            _solLoss = Cfg.Bind("Guns", "ManeuverSolutionLoss", 10f,
                "How much of the firing solution a gun loses at each check (every 2 s) when the target's course changed by more than 4 degrees and/or its speed by more than 4 knots: this percent of the CURRENT solution, once for the course and once for the speed. " +
                "The game takes a fixed 0.1 (10 points) each, however high or low the solution is, so a gun with a low solution cannot grow it against a manoeuvring target at all. 0 = the game's fixed 0.1. Applies to directors and to guns without a director.");
            _localGrowth = Cfg.Bind("Guns", "LocalSolutionGrowth", 75f,
                "How fast a big gun with no working director (see Big guns build a solution without a director) builds its own firing solution, as a percent of the growth speed of the BEST director it has (the strongest of its associated sensors, working or not: gain x crew x light). " +
                "The game's gun without any sensor builds it at the crew speed alone, which is faster than a weak director (gain 0.3-0.5) and so makes a lost director an advantage. 75 = three quarters of the director's speed. A gun with no associated sensor at all uses the AVERAGE director of all units in the mission (the same percent of it), so it is not faster than a director either.");
            _minSolOptical = Cfg.Bind("Guns", "OpticalDirectorMinSolution", 1f,
                "Minimum firing solution with an optical director: the solution (how well the gun has locked on the target's course and speed, shown as Solution % of the gun, 0..1) never falls below this level and starts from it, so the guns hit sooner after they open fire and wait for splashes less. This is a multiplier of the director's own minimum (the sensor ini key FCMinSolution); 1 = as the director is.");
            _minSolRadar = Cfg.Bind("Guns", "RadarDirectorMinSolution", 1f,
                "Minimum firing solution with a radar director: the solution (how well the gun has locked on the target's course and speed, shown as Solution % of the gun, 0..1) never falls below this level and starts from it, so the guns hit sooner after they open fire and wait for splashes less. This is a multiplier of the director's own minimum (the sensor ini key FCMinSolution); 1 = as the director is.");
            _ammoFix = Cfg.Bind("Guns", "AmmunitionFix", false,
                "Accuracy (circular error probable) and warhead power of gun ammunition are taken from the author's pack (ammunition_fix.txt in the mod folder) and from the values you set in the table below; any other gun ammunition of the game can be added to the table, or a row removed. Off = the game's own ammunition values. Applied to ammunition loaded after Apply (a mission start).");
            _debugEntry = Cfg.Bind("General", "Debug", false,
                "Verbose logging and BepInEx/AlternativeWeaponsControl.trace.txt.");
            _language = Cfg.Bind("General", "Language", "",
                "Language of the mod's texts: a code of a game language (en, ru ...); empty = the game's own language. The texts are in the files lang/<code>.ini in the mod folder - edit them to translate.");
            Entries.Add(_gunMemory); Entries.Add(_gunReroll); Entries.Add(_barrelScatter); Entries.Add(_bigLocal); Entries.Add(_clickAllGuns); Entries.Add(_gunLocalFire); Entries.Add(_bottomFire); Entries.Add(_secTargets); Entries.Add(_clickVolume); Entries.Add(_samButton); Entries.Add(_zoneFire); Entries.Add(_plotHostile); Entries.Add(_freeAirKnots); Entries.Add(_freeAirNm); Entries.Add(_freeAirOwn); Entries.Add(_freeAirEnemy); Entries.Add(_tabLand); Entries.Add(_tabAir); Entries.Add(_samAuto); Entries.Add(_samKeep); Entries.Add(_samAnim); Entries.Add(_samAutoRange); Entries.Add(_samMinutes); Entries.Add(_gunOneDirector); Entries.Add(_gunChannels); Entries.Add(_noRbuSurfaceChase); Entries.Add(_noAirChase); Entries.Add(_gunAimBeyond); Entries.Add(_engageOutside); Entries.Add(_gunMinError); Entries.Add(_accuracyMult); Entries.Add(_fcAccuracy); Entries.Add(_weakBoost); Entries.Add(_gainLocal); Entries.Add(_minSolOptical); Entries.Add(_minSolRadar); Entries.Add(_solSpeed); Entries.Add(_solLoss); Entries.Add(_localGrowth); Entries.Add(_ammoFix); Entries.Add(_debugEntry); Entries.Add(_language);
            Loc.Trace = Trace; Loc.Forced = () => _language == null ? "" : _language.Value;
            foreach (var en in Entries) Loc.Register("help." + en.Definition.Section + "/" + en.Definition.Key, en.Description == null ? "" : en.Description.Description);

            _debug = _debugEntry.Value;
            _traceFile = Path.Combine(Paths.BepInExRootPath, "AlternativeWeaponsControl.trace.txt");
            if (_debug) { try { File.WriteAllText(_traceFile, ""); } catch { } }
            Trace(PluginName + " " + PluginVersion + " build " + BuildId);
            SettingsWindow.Trace = Trace;
            SettingsWindow.Warn = Warn;
            Cfg.SettingChanged += (s, e) => { _debug = _debugEntry.Value; RememberCfgStamp(); ApplyLocalGain(); };
            RememberCfgStamp();

            RefreshEnabled();
            TryPatch();
            Logger.LogInfo(PluginName + " " + PluginVersion + " (build " + BuildId + ") loaded" +
                           (ModState.Controlled ? "; follows its tick in the game's Mods menu." : "."));
        }

        void Update()
        {
            if (!_patchTried) TryPatch();
            try { SettingsWindow.Tick(); } catch (Exception e) { Trace("settings tick: " + e.Message); }
            WatchConfigFile();
            try { FixTargetsStep(); } catch (Exception e) { Trace("target fix: " + e.Message); }
            try { BottomZonesStep(UnityEngine.Time.unscaledTime); } catch (Exception e) { Trace("bottom zones: " + e.Message); }
            try { GunTargetsStep(UnityEngine.Time.unscaledTime); } catch (Exception e) { Trace("gun targets: " + e.Message); }
            try { GunTipStep(UnityEngine.Time.unscaledTime); } catch { }
            if (!Enabled) return;
            try { StatusViewsStep(UnityEngine.Time.unscaledTime); } catch (Exception e) { Trace("status views: " + e); }
        }

        // ------------------------------------------------------------ the .cfg changed outside the game

        DateTime _cfgStamp; float _nextCfgCheck, _cfgChangedAt = -1f;

        internal void RememberCfgStamp()
        {
            try { _cfgStamp = File.GetLastWriteTimeUtc(Cfg.ConfigFilePath); } catch { }
            _cfgChangedAt = -1f;
        }

        void WatchConfigFile()
        {
            float now = UnityEngine.Time.unscaledTime;
            if (now < _nextCfgCheck) return;
            _nextCfgCheck = now + 1f;
            try
            {
                if (!File.Exists(Cfg.ConfigFilePath)) return;
                var t = File.GetLastWriteTimeUtc(Cfg.ConfigFilePath);
                if (t == _cfgStamp) { if (_cfgChangedAt >= 0f && now - _cfgChangedAt >= 0.5f) { _cfgChangedAt = -1f; Cfg.Reload(); _debug = _debugEntry.Value; SettingsWindow.Refresh(); Say("Settings re-read from " + Cfg.ConfigFilePath + "."); } return; }
                _cfgStamp = t; _cfgChangedAt = now;
            }
            catch (Exception e) { Trace("WatchConfigFile: " + e.Message); }
        }

        // ------------------------------------------------------------ the Mods menu tick

        static void RefreshEnabled()
        {
            bool? state = ModState.Read();
            if (state == null && ModState.Controlled) Warn("Could not read the game's mod list; " + PluginName + " stays on.");
            bool now = state != false;
            if (now == Enabled) return;
            Enabled = now;
            try { if (_inst != null) _inst.ApplyLocalGain(); } catch { }
            if (ModState.Controlled)
                Say(now ? "Enabled in the game's Mods menu (" + ModState.ModId + ")." : "Disabled in the game's Mods menu (" + ModState.ModId + "); idle.");
        }

        static WeakReference _loadIter = new WeakReference(null);

        static void LoadScreenPrefix(object __instance)
        {
            if (__instance == null || ReferenceEquals(_loadIter.Target, __instance)) return;
            _loadIter = new WeakReference(__instance);
            try { RefreshEnabled(); } catch (Exception e) { Trace("LoadScreenPrefix: " + e.Message); }
        }

        static void ModsSavedPostfix()
        {
            try { RefreshEnabled(); } catch (Exception e) { Trace("ModsSavedPostfix: " + e.Message); }
        }

        // ------------------------------------------------------------ patching

        void TryPatch()
        {
            var asm = GameAssembly();
            if (asm == null) return;   // not loaded yet: next frame
            _patchTried = true;
            var harmony = new Harmony(PluginGuid);
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            try { HookStatusTabs(harmony, asm); } catch (Exception e) { Warn("Unit Status hooks failed: " + e.Message); Trace(e.ToString()); }
            try { HookZones(harmony, asm); } catch (Exception e) { Warn("Fire zones failed: " + e.Message); }
            try { HookWarmUp(harmony, asm); } catch (Exception e) { Warn("SAM ready button failed: " + e.Message); }
            try { HookAmmoFix(harmony, asm); } catch (Exception e) { Warn("Ammunition fix failed: " + e.Message); }
            try { HookGunTargets(harmony, asm); } catch (Exception e) { Warn("Gun targets failed: " + e.Message); }
            try { HookGunFixes(harmony, asm); } catch (Exception e) { Warn("Gun fixes failed: " + e.Message); }
            try { HookWeapons(harmony, asm); } catch (Exception e) { Warn("Weapons hooks failed: " + e.Message); Trace(e.ToString()); }
            try
            {
                var fm = asm.GetType("SeaPower.FileManager");
                var save = fm == null ? null : fm.GetMethod("SaveDirectories", all);
                if (save != null) harmony.Patch(save, postfix: Hm("ModsSavedPostfix"));
                var ls = asm.GetType("SeaPower.LoadScreen");
                if (ls != null)
                    foreach (var nt in ls.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                        if (nt.Name.StartsWith("<ExecuteLoadScreen>", StringComparison.Ordinal))
                        {
                            var mn = nt.GetMethod("MoveNext", all);
                            if (mn != null) harmony.Patch(mn, prefix: Hm("LoadScreenPrefix"));
                            break;
                        }
            }
            catch (Exception e) { Warn("Mods menu hooks failed: " + e.Message); }
            try
            {
                // the shared settings entry of the author's mods in the main and the pause menu (Rgreat.Hub, SettingsHub.cs is the same file in every mod)
                Rgreat.Hub.SettingsHub.Register("alternative-weapons-control", () => Loc.T("menu.button"), PluginVersion, 20,
                    anchor => SettingsWindow.Open(anchor as Noesis.UIElement, null), () => Enabled);
                Rgreat.Hub.SettingsHub.Install(harmony, asm, Trace, () => Loc.Language);
            }
            catch (Exception e) { Warn("Settings menu entry failed: " + e.Message); }
        }

        // ------------------------------------------------------------ the Weapons tab in Unit Status
        // It lives in UnitStatusViewModel.Tabs like the game's ones: added in its constructor (second), available or not after
        // each unit switch, content made when the tab is shown (EnsureContent) and dropped when the unit changes (DisposeOwnedContent).

        const int WeaponsTabKind = 6;   // past the game's UnitStatusTab values (Engagement ... Supplies = 5); free in 0.9.0

        Type _ustType, _ustKind;
        FieldInfo _ustUnit;
        MethodInfo _ustSelectFirst;

        sealed class ModTab
        {
            public object Tab;           // UnitStatusTabViewModel
            public object View;          // the view's root
            public WeaponsView Wv;
        }
        static readonly ConditionalWeakTable<object, ModTab> StTabs = new ConditionalWeakTable<object, ModTab>();

        void HookStatusTabs(Harmony harmony, Assembly asm)
        {
            const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var usvm = asm.GetType("SeapowerUI.ViewModels.UnitStatusViewModel");
            _ustType = asm.GetType("SeapowerUI.ViewModels.UnitStatusTabViewModel");
            _ustKind = asm.GetType("SeapowerUI.ViewModels.UnitStatusTab");
            InitWeaponTypes(asm);
            if (usvm == null || _ustType == null || _ustKind == null)
            {
                Warn("Unit Status types not found (game version?); the Weapons tab is not available.");
                return;
            }
            _ustUnit = usvm.GetField("_unit", inst);
            _ustSelectFirst = usvm.GetMethod("SelectFirstAvailableTab", inst);
            var ctor = usvm.GetConstructors(inst);
            var sw = usvm.GetMethod("SwitchUnit", inst);
            var ens = usvm.GetMethod("EnsureContent", inst);
            var disp = usvm.GetMethod("DisposeOwnedContent", inst);
            if (ctor.Length == 0 || sw == null || ens == null || disp == null || _ustUnit == null)
            {
                Warn("UnitStatusViewModel members not found (game updated?); the Weapons tab is not available.");
                return;
            }
            foreach (var c in ctor) harmony.Patch(c, postfix: Hm("StatusCreatedPostfix"));
            harmony.Patch(sw, postfix: Hm("StatusSwitchPostfix"));
            harmony.Patch(ens, postfix: Hm("StatusEnsurePostfix"));
            harmony.Patch(disp, prefix: Hm("StatusDisposePrefix"));
            // the window itself: its tab button gets (i) and the gear, our host shows the tab's content
            var tView = asm.GetType("SeapowerUI.UnitStatusView");
            var vctor = tView == null ? null : tView.GetConstructor(inst, null, Type.EmptyTypes, null);
            if (vctor != null) harmony.Patch(vctor, postfix: Hm("StatusViewCreated"));
            else Warn("SeapowerUI.UnitStatusView not found (game updated?); the Weapons tab cannot be shown.");
            Trace("patched UnitStatusViewModel / UnitStatusView");
        }

        // added always (also while the mod is off in the Mods menu): available only while it is on
        static void StatusCreatedPostfix(object __instance)
        {
            var a = _inst;
            if ((object)a == null) return;
            try { a.AddModTab(__instance); } catch (Exception e) { Trace("Unit Status tab: " + e); }
        }

        static void StatusSwitchPostfix(object __instance)
        {
            var a = _inst;
            if ((object)a == null) return;
            try { a.UpdateModTab(__instance); } catch (Exception e) { Trace("Unit Status tab: " + e); }
        }

        static void StatusEnsurePostfix(object __instance, object tab)
        {
            var a = Active;
            if ((object)a == null || tab == null) return;
            try { a.EnsureModContent(__instance, tab); } catch (Exception e) { Trace("Unit Status tab: " + e); }
        }

        static void StatusDisposePrefix(object __instance)
        {
            var a = _inst;
            if ((object)a == null) return;
            try { a.DropModContent(__instance); } catch (Exception e) { Trace("Unit Status tab: " + e); }
        }

        void AddModTab(object status)
        {
            ModTab mt;
            if (StTabs.TryGetValue(status, out mt)) return;
            var tabs = R.P(status, "Tabs") as IList;
            if (tabs == null) return;
            mt = new ModTab { Tab = Activator.CreateInstance(_ustType, status, Enum.ToObject(_ustKind, WeaponsTabKind), "Weapons") };
            tabs.Insert(Math.Min(1, tabs.Count), mt.Tab);
            StTabs.Add(status, mt);
            UpdateModTab(status);
        }

        object StatusUnit(object status) { return _ustUnit == null ? null : _ustUnit.GetValue(status); }

        bool WeaponsAvailableFor(object unit)
        {
            if (!Enabled || unit == null) return false;
            bool land = R.Is(unit, _tLandUnit);
            bool air = R.Is(unit, _tAircraft) || R.Is(unit, _tHelicopter);
            if (!(R.Is(unit, _tVessel) || R.Is(unit, _tSubmarine) || (land && _tabLand != null && _tabLand.Value) || (air && _tabAir != null && _tabAir.Value))) return false;
            var w = W(unit);
            return CachedLaunchers(unit, w).Count > 0;
        }

        void UpdateModTab(object status)
        {
            ModTab mt;
            if (!StTabs.TryGetValue(status, out mt)) return;
            bool avail = WeaponsAvailableFor(StatusUnit(status));
            R.SetP(mt.Tab, "IsAvailable", avail);
            if (!avail && R.B(R.P(mt.Tab, "IsSelected")))
            {
                R.SetP(mt.Tab, "IsSelected", false);
                if (_ustSelectFirst != null) { try { _ustSelectFirst.Invoke(status, null); } catch (Exception e) { Trace("SelectFirstAvailableTab: " + e.Message); } }
            }
        }

        void EnsureModContent(object status, object tab)
        {
            ModTab mt;
            if (!StTabs.TryGetValue(status, out mt) || !ReferenceEquals(mt.Tab, tab)) return;
            if (!R.B(R.P(tab, "IsAvailable")) || R.B(R.P(tab, "HasContentError")) || R.P(tab, "Content") != null) return;
            var unit = StatusUnit(status);
            if (!WeaponsAvailableFor(unit)) return;
            try
            {
                var v = BuildWeaponsView(unit);
                mt.Wv = v;
                mt.View = v.Root;
                R.SetP(tab, "Content", v.Root);
                Verbose("Weapons tab: " + v.Groups.Count + " launcher group(s) for " + R.S(R.Call(unit, "getUIDAndName")));
            }
            catch (Exception e)
            {
                var ex = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
                Trace("Weapons tab: " + ex);
                R.SetP(tab, "Content", null);
                R.SetP(tab, "ContentError", Loc.T("tab.contentError"));
            }
        }

        void DropModContent(object status)
        {
            ModTab mt;
            if (!StTabs.TryGetValue(status, out mt) || mt.View == null) return;
            R.SetP(mt.Tab, "Content", null);
            mt.View = null; mt.Wv = null;
        }

        // ------------------------------------------------------------ showing it
        // The Unit Status view shows each game tab's content in a place of its own (a Grid per tab, Visibility bound to
        // "<Name>Tab.IsSelected"), not a generic "selected tab" presenter, so a new tab's content is not shown by the game: the
        // mod puts a host of its own next to them (same panel, grid cell, margins) and shows it while the tab is selected.

        static readonly List<WeakReference> StatusViews = new List<WeakReference>();   // SeapowerUI.UnitStatusView
        static readonly object HostTag = new object();
        static readonly ConditionalWeakTable<Noesis.Border, object> Hosts = new ConditionalWeakTable<Noesis.Border, object>();   // the mod's host Borders

        sealed class ViewState
        {
            public Noesis.Border Host; public Noesis.Panel Panel; public Noesis.FrameworkElement Beside;
            public bool Failed; public int Tries;
            public Noesis.TextBlock Header; public float NextScan;
            public bool ClickProbe;
        }
        static readonly ConditionalWeakTable<object, ViewState> ViewStates = new ConditionalWeakTable<object, ViewState>();

        static void StatusViewCreated(object __instance)
        {
            if (__instance == null) return;
            lock (StatusViews) { StatusViews.RemoveAll(w => !w.IsAlive); StatusViews.Add(new WeakReference(__instance)); }
        }

        void StatusViewsStep(float now)
        {
            List<object> views = new List<object>();
            lock (StatusViews) { foreach (var w in StatusViews) { var v = w.Target; if (v != null) views.Add(v); } }
            foreach (var o in views)
            {
                var usv = o as Noesis.FrameworkElement;
                if (usv == null || !usv.IsVisible) continue;
                var status = usv.DataContext;
                ModTab mt;
                if (status == null || !StTabs.TryGetValue(status, out mt)) continue;
                var vs = ViewStates.GetOrCreateValue(o);
                if (_debug && !vs.ClickProbe) { vs.ClickProbe = true; try { AddClickProbe(usv, vs); } catch (Exception e) { Trace("click probe: " + e.Message); } }
                try { HeaderStep(usv, mt, vs, now); } catch (Exception e) { Trace("tab header: " + e.Message); }
                if (!vs.Failed) { try { HostStep(usv, status, mt, vs, now); } catch (Exception e) { Trace("tab host: " + e); } }
            }
        }

        void HostStep(Noesis.FrameworkElement usv, object status, ModTab mt, ViewState vs, float now)
        {
            bool selected = R.B(R.P(mt.Tab, "IsSelected")) && R.B(R.P(mt.Tab, "IsAvailable"));
            var view = mt.View as Noesis.UIElement;
            var host = vs.Host;
            if (host == null || Noesis.VisualTreeHelper.GetParent(host) == null)
            {
                if (!selected || view == null) return;
                var vp0 = Noesis.VisualTreeHelper.GetParent(view);
                if (vp0 != null)
                {
                    // still in the host of a closed Unit Status window (reopened: a new view, the same tab content): take it out
                    var ob = vp0 as Noesis.Border;
                    object mark;
                    if (ob != null && Hosts.TryGetValue(ob, out mark)) ob.Child = null;
                    else return;   // the game shows it
                }
                host = MakeHost(vs, usv, status);
                if (host == null) { if (++vs.Tries > 120) vs.Failed = true; return; }
                vs.Host = host;
            }
            if (selected && view != null)
            {
                if (!ReferenceEquals(host.Child, view))
                {
                    var vp = Noesis.VisualTreeHelper.GetParent(view) as Noesis.Border;
                    if (vp != null && !ReferenceEquals(vp, host)) vp.Child = null;
                    host.Child = view;
                }
                if (host.Visibility != Noesis.Visibility.Visible) host.Visibility = Noesis.Visibility.Visible;
                if (mt.Wv != null) { try { WeaponsTick(mt.Wv, now); } catch (Exception e) { Trace("Weapons tab refresh: " + e); } }
            }
            else
            {
                if (host.Visibility != Noesis.Visibility.Collapsed) host.Visibility = Noesis.Visibility.Collapsed;
                if (view == null && host.Child != null) host.Child = null;
            }
        }

        /// <summary>Debug: a click inside the Weapons tab is traced with the element the UI hit-tests on top at that point (and its
        /// parents) - to find what covers buttons that do not react. Attached to the root of the view's UI tree (preview: before
        /// anything handles the click).</summary>
        static void AddClickProbe(Noesis.FrameworkElement usv, ViewState vs)
        {
            var root = Noesis.VisualTreeHelper.GetRoot(usv) as Noesis.UIElement;
            if (root == null) return;
            root.PreviewMouseLeftButtonDown += (o, e) =>
            {
                try
                {
                    var host = vs.Host;
                    if (host == null || host.Visibility != Noesis.Visibility.Visible) return;
                    var ph = e.GetPosition(host);
                    if (ph.X < 0 || ph.Y < 0 || ph.X > host.ActualWidth || ph.Y > host.ActualHeight) return;
                    var hit = Noesis.VisualTreeHelper.HitTest(root as Noesis.Visual, e.GetPosition(root));
                    var sb = new StringBuilder("click in Weapons tab at " + ph.X.ToString("0") + "," + ph.Y.ToString("0") + " of " + host.ActualWidth.ToString("0") + "x" + host.ActualHeight.ToString("0") + ": ");
                    Noesis.DependencyObject d = hit.VisualHit;
                    bool ours = false;
                    for (int i = 0; i < 14 && d != null; i++, d = Noesis.VisualTreeHelper.GetParent(d))
                    {
                        if (ReferenceEquals(d, host)) { ours = true; sb.Append(" < [our host]"); break; }
                        var fe = d as Noesis.FrameworkElement;
                        sb.Append(i == 0 ? "" : " < ").Append(d.GetType().Name);
                        if (fe != null && !string.IsNullOrEmpty(fe.Name)) sb.Append('#').Append(fe.Name);
                    }
                    if (!ours) sb.Append("  (NOT inside the tab: something covers it)");
                    var src = e.Source; sb.Append("; source ").Append(src == null ? "null" : src.GetType().Name);
                    Trace(sb.ToString());
                }
                catch (Exception ex) { Trace("click probe: " + ex.Message); }
            };
            Trace("click probe on " + root.GetType().Name);
        }

        /// <summary>A Border next to where the game shows its tabs' contents (same panel, cell and margins); the place is found once per view.</summary>
        Noesis.Border MakeHost(ViewState vs, Noesis.DependencyObject usv, object status)
        {
            if (vs.Panel != null && vs.Beside != null && Noesis.VisualTreeHelper.GetParent(vs.Beside) != null) return AddHost(vs.Panel, vs.Beside);
            // UnitStatusView: one Grid per tab, Visibility bound to "<Name>Tab.IsSelected", all in the same cell of one Grid
            Noesis.FrameworkElement place = FindFirst(usv, e =>
            {
                if (!(e is Noesis.FrameworkElement)) return false;
                Noesis.BindingExpression x = null;
                try { x = Noesis.BindingOperations.GetBindingExpression(e, Noesis.UIElement.VisibilityProperty); } catch { }
                var path = x == null || x.ParentBinding == null || x.ParentBinding.Path == null ? null : x.ParentBinding.Path.Path;
                return path != null && path.EndsWith("Tab.IsSelected", StringComparison.Ordinal);
            }) as Noesis.FrameworkElement;
            if (place == null) { Trace("Weapons tab: no place of the game's tab contents found in UnitStatusView"); return null; }
            Noesis.FrameworkElement refEl = place;
            var parent = Noesis.VisualTreeHelper.GetParent(refEl);
            while (parent != null && !(parent is Noesis.Panel) && !ReferenceEquals(parent, usv))
            {
                refEl = parent as Noesis.FrameworkElement;
                if (refEl == null) break;
                parent = Noesis.VisualTreeHelper.GetParent(refEl);
            }
            var panel = parent as Noesis.Panel;
            if (panel == null || refEl == null) { Trace("Weapons tab: no panel above the game's tab contents"); return null; }
            vs.Panel = panel; vs.Beside = refEl;
            Trace("Weapons tab: host goes to " + panel.GetType().Name + " next to " + refEl.GetType().Name);
            return AddHost(panel, refEl);
        }

        static Noesis.Border AddHost(Noesis.Panel panel, Noesis.FrameworkElement refEl)
        {
            var host = new Noesis.Border { Visibility = Noesis.Visibility.Collapsed, Margin = refEl.Margin,
                                           HorizontalAlignment = Noesis.HorizontalAlignment.Stretch, VerticalAlignment = Noesis.VerticalAlignment.Stretch };
            Noesis.Grid.SetRow(host, Noesis.Grid.GetRow(refEl));
            Noesis.Grid.SetColumn(host, Noesis.Grid.GetColumn(refEl));
            Noesis.Grid.SetRowSpan(host, Noesis.Grid.GetRowSpan(refEl));
            Noesis.Grid.SetColumnSpan(host, Noesis.Grid.GetColumnSpan(refEl));
            Hosts.Add(host, HostTag);
            int at = panel.Children.IndexOf(refEl as Noesis.UIElement);
            panel.Children.Insert(at < 0 ? panel.Children.Count : at + 1, host);
            return host;
        }

        static Noesis.DependencyObject FindFirst(Noesis.DependencyObject root, Func<Noesis.DependencyObject, bool> match)
        {
            var stack = new Stack<Noesis.DependencyObject>();
            stack.Push(root);
            int budget = 30000;
            while (stack.Count > 0 && --budget > 0)
            {
                var e = stack.Pop();
                if (!ReferenceEquals(e, root) && match(e)) return e;
                int n = Noesis.VisualTreeHelper.GetChildrenCount(e);
                for (int k = n - 1; k >= 0; k--) { var c = Noesis.VisualTreeHelper.GetChild(e, k); if (c != null) stack.Push(c); }
            }
            return null;
        }
    }
}
