using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace AlternativeWeaponsControl
{
    /// <summary>
    /// Localization. All the mod's texts are in the table below (key, English text); the translations are plain text files
    /// "lang/&lt;code&gt;.ini" in the mod folder, one for each language of the game (the list is the game's own: config.ini [Languages]):
    ///     key = text        (";" starts a comment line; \n in a text is a line break; {0}, {1} are filled in by the mod)
    /// The language is the game's (Options → Language), or [General] Language in the .cfg when it is set. A text missing from the
    /// file is English. The mod creates the files that are missing (the English texts to translate); in an existing file it
    /// appends the keys added later (after "; --- new ---"); it never changes the lines the player has edited.
    /// The settings window's help texts (the .cfg descriptions) are in the files as "help.Section/Key".
    /// </summary>
    static class Loc
    {
        public static Action<string> Trace = s => { };

        static readonly List<string> Keys = new List<string>();
        static readonly Dictionary<string, string> En = new Dictionary<string, string>(StringComparer.Ordinal);
        static Dictionary<string, string> _tr = new Dictionary<string, string>(StringComparer.Ordinal);
        static string _lang = "en", _forced = "";
        static int _checkedAt = -1;
        static bool _filesDone;

        /// <summary>Language from the .cfg ("" = the game's).</summary>
        public static Func<string> Forced = () => "";

        static void D(string key, string en) { Keys.Add(key); En[key] = en; }

        static Loc()
        {
            // ---- the "Weapons" tab
            D("tab.weapons", "WEAPONS");
            D("tab.state", "STATE");
            D("tab.ammo", "AMMO");
            D("tab.fire", "FIRE");
            D("status.free", "Free"); D("status.tight", "Tight"); D("status.hold", "Hold");
            D("tab.onTight", "On Tight: engage hostile aircraft");
            D("tab.onFree", "On Free: engage unidentified aircraft");
            D("tab.onFreeTip", "Ships on Free also engage aircraft that:\n- are not identified yet;\n- are faster than {0} kn;\n- are closer than {1} nm;\n- come towards the ship (within 45° of the bearing to it).\nKeep in mind: war crimes have no statute of limitations.");
            D("tab.expandAll", "Expand all"); D("tab.collapseAll", "Collapse all");
            D("tab.noWeapons", "No weapons.");
            D("tab.dragOrder", "Drag to change the order of the sections");
            D("tab.fireTip", "Click: pick the target on the map. Arrow: salvo and launch options.");
            D("tab.autoAgainst", "Automatic use against {0}");
            D("tab.contentError", "The weapons could not be shown for the selected unit.");
            D("tab.gearTip", "Alternative Weapons Control settings");
            D("dom.0", "AIR"); D("dom.1", "MSL"); D("dom.2", "SUR"); D("dom.3", "SUB"); D("dom.4", "LND");
            D("domLong.0", "aircraft and helicopters"); D("domLong.1", "missiles, torpedoes and other weapons");
            D("domLong.2", "surface ships"); D("domLong.3", "submarines"); D("domLong.4", "land targets");
            D("section.0", "SAM"); D("section.1", "ANTI-SHIP"); D("section.2", "LAND ATTACK"); D("section.3", "ASW");
            D("section.4", "GUNS"); D("section.5", "CIWS"); D("section.6", "OTHER"); D("section.7", "STORES");
            D("noun.turret", "turret"); D("noun.turrets", "turrets"); D("noun.mount", "mount"); D("noun.mounts", "mounts");
            D("noun.launcher", "launcher"); D("noun.launchers", "launchers");
            D("count.format", "{0} {1}");
            D("loc.fore", "fore"); D("loc.aft", "aft"); D("loc.port", "port"); D("loc.starboard", "starboard");
            // ---- status
            D("cat.ready", "ready"); D("cat.engaging", "engaging"); D("cat.reload", "reload"); D("cat.damaged", "damaged");
            D("cat.off", "off"); D("cat.waiting", "waiting"); D("cat.sensorsOff", "sensors off");
            D("st.queued", "Queued"); D("st.waitFcr", "Waiting for FCR channel"); D("st.reloading", "Reloading: {0}");
            D("st.localControl", "Local control"); D("st.railsEmpty", "Rails empty"); D("st.loading", "Loading rails"); D("st.unloading", "Unloading rails");
            D("target.track", "Track {0} ({1})"); D("target.trackOnly", "Track {0}"); D("target.unknown", "unknown");
            // ---- buttons and tips
            D("btn.fire", "Fire");
            D("btn.salvoTip", "Salvo and launch options");
            D("btn.cease", "Cease fire");
            D("btn.reload", "Reload from the magazine");
            D("btn.nothingToReload", "{0} (nothing to reload now)");
            D("tip.weaponType", "Weapon type");
            D("stock.rail", "On launcher {0}/{1}");
            D("guid.IRHoming", "IR");
            D("guid.SemiActiveRadarHoming", "SARH");
            D("guid.ActiveRadarHoming", "ARH");
            D("guid.AntiRadiationHoming", "ARM");
            D("guid.LaserHoming", "LGH");
            D("guid.TVHoming", "TV");
            D("guid.ActiveSonarHoming", "ASH");
            D("guid.PassiveSonarHoming", "PSH");
            D("guid.WakeHoming", "WH");
            D("guid.ActivePassiveSonarHoming", "APSH");
            D("guid.HomeOnJam", "HOJ");
            D("ammo.activeTip", "Active ammunition: the Fire buttons of the launchers below fire it. Click to choose.");
            D("tip.refWeapon", "Reference of the weapon");
            D("tip.refDirector", "Reference of the director");
            D("tip.switchesGroup", "{0}\n(switches the whole group)");
            D("tip.switchesSection", "{0}\n(this target type on all the section's launchers - also for their other ammunition of it)");
            D("tip.reloadCurrent", "{0}\n(the game reloads the ammunition the launcher holds ready)");
            // ---- SAM readiness button
            D("sam.away", "Missiles away. Click: load the missiles onto the rails");
            D("sam.loading", "Loading the missiles onto the rails... Click: cancel");
            D("sam.railsLoaded", "Rails loaded: {0} of {1}. ");
            D("sam.ready", "Ready. ");
            D("sam.putAwayIn", "The missiles are put away {0} after the last use. ");
            D("sam.putAway", "Click: put the missiles away");
            // ---- characteristics
            D("spec.upTo", "up to "); D("spec.nm", " nm"); D("spec.kt", " kt"); D("spec.pwr", "pwr ");
            D("spec.guidance", "Guidance: {0}"); D("spec.range", "Range, nm: {0}");
            D("spec.speed", "Max speed, kt: {0}"); D("spec.warhead", "Warhead power: {0}");
            D("spec.grade", "grade {0}"); D("spec.gain", "solution gain {0}");
            // ---- fire-control channels, directors
            D("chan.tracked", "T {0}/{1}"); D("chan.guided", " · W {0}/{1}"); D("chan.wires", "Wires {0}/{1}");
            D("chan.wiresTip", "Wire-guided torpedoes on the wire: in use / at a time.");
            D("chan.head", "Fire-control channels of the directors:");
            D("chan.damaged", " (damaged)"); D("chan.off", " (off)");
            D("chan.targets", ": targets {0}/{1}"); D("chan.guidedWeapons", ", guided weapons {0}/{1}");
            D("chan.foot", "\nT: targets tracked at a time; W: guided weapons in flight at a time.");
            D("dir.title", "FIRE-CONTROL DIRECTORS");
            D("dir.foot", "T: targets tracked at a time · W: guided weapons in flight at a time");
            D("dir.damaged", "damaged"); D("dir.on", "on"); D("dir.off", "off");
            D("dir.targets", "Targets {0}/{1}"); D("dir.guided", " · guided weapons {0}/{1}");
            // ---- chip tips
            D("chip.thisLauncher", "this launcher");
            D("chip.allOf", "all {0} {1}");
            D("chip.on", "{0}: automatic against {1}.\nClick: off for {2}.");
            D("chip.off", "{0}: off for {1}.\nClick: automatic again.");
            D("chip.order", "{0}: off by Standing Orders - ");
            D("chip.orderNo", "{0}: No");
            D("chip.orderEnd", ".\nClick: allow for {0} (the unit's order becomes Yes, the other launchers it covers turn off).");
            D("chip.mixed", "{0}: mixed - ");
            D("chip.mixedAuto", "automatic"); D("chip.mixedOff", "off"); D("chip.mixedOrder", "off by Standing Orders");
            D("chip.mixedEnd", ".\nClick: the same for all.");
            // ---- legend
            D("legend.dom", "AIR · MSL · SUR · SUB · LND - may the AI use the launcher by itself against aircraft, missiles and torpedoes, surface ships, submarines, land targets:");
            D("legend.auto", "automatic");
            D("legend.off", "off (click to switch; the group's switch sets all its launchers)");
            D("legend.order", "off by the unit's Standing Orders (click: allowed for this launcher)");
            D("legend.mixed", "mixed in the group");
            D("legend.manual", "Manual fire is never restricted. The switches are kept in the mission save.");
            D("legend.state", "State:");
            D("legend.ready", "ready");
            D("legend.engaging", "engaging - and its target (Track N)");
            D("legend.waiting", "reloading; sensors offline (none of its directors is on); waiting - queued behind the launcher's own target, or for a fire-control channel");
            D("legend.damaged", "damaged");
            D("legend.empty", "off / empty");
            D("legend.local", "At a weapon's name while it fires: the director aiming it (blue), or \"Local control\" (orange) - no working director, less accurate.");
            D("legend.fire", "Fire: pick the target on the map (on a launcher's row - that launcher fires); ▾ salvo and launch options; ■ cease fire; ↻ reload.");
            D("legend.channels", "T 2/6 · W 5/12: fire-control channels of the group's directors - targets tracked / guided weapons in flight at a time; amber when none is free. Point at it: the directors, with their reference.");
            D("legend.ammo", "AMMO: left / at the start (loaded and in the magazines).");
            // ---- settings window
            D("set.title", "Alternative Weapons Control - Settings");
            D("set.hint", "Point at a setting to see what it does.");
            D("set.cancel", "Cancel"); D("set.apply", "Apply"); D("set.ok", "OK");
            D("set.changed", "● changed, not applied yet   ↺ = back to the default");
            D("set.default", "default: {0}"); D("set.on", "on"); D("set.off", "off");
            D("set.gameOff", "game: off");
            D("set.gameLang", "the game's language");
            D("set.resetTip", "Back to the default");
            D("set.resizeTip", "Drag to resize");
            D("tabs.panel", "Weapons panel"); D("tabs.fire", "Fire Control"); D("tabs.ai", "AI"); D("tabs.guns", "Guns");
            D("tabs.visual", "Visual effects"); D("tabs.debug", "Debug"); D("tabs.language", "Language");
            D("grp.panel", "Panel"); D("grp.samButton", "SAM readiness button"); D("grp.sam", "SAM");
            D("grp.fireControl", "Fire control"); D("grp.chasing", "Chasing"); D("grp.accuracy", "Accuracy");
            D("grp.tracers", "Tracers and sound"); D("grp.binoculars", "Binoculars"); D("grp.flashes", "Flashes");
            D("grp.freeAir", "On Free: unidentified aircraft"); D("grp.targeting", "Target selection"); D("grp.fires", "Fires"); D("grp.diagnostics", "Diagnostics"); D("grp.language", "Language");
            D("lbl.UI/ClickVolume", "Click sound volume");
            D("unit.UI/ClickVolume", "0..1 · 0 = no sound");
            D("lbl.UI/SamReadyButton", "SAM readiness button");
            D("lbl.UI/SamReadyMinutes", "SAM readiness time, minutes");
            D("unit.UI/SamReadyMinutes", "1..60");
            D("lbl.UI/WeaponsTabGroundUnits", "Weapons tab for ground units");
            D("lbl.UI/WeaponsTabAircraft", "Weapons tab for aircraft and helicopters");
            D("lbl.UI/FireShowsZones", "Fire shows weapon arcs and range");
            D("lbl.FireControl/GunOneDirector", "Guns take one director");
            D("lbl.FireControl/GunChannelLimit", "Guns without a free channel: local control");
            D("lbl.Ships/SamAutoCharge", "Charge SAMs when unknown aircraft approach");
            D("lbl.Ships/SamKeepLoaded", "Keep SAM missiles on the rails while hot");
            D("lbl.Ships/SamAnimFix", "Missile load / unload animation fix");
            D("lbl.Ships/SamAutoChargeRange", "Unknown aircraft distance to charge");
            D("lbl.Fire/EngageByPlotRelation", "Ignore the true side of contacts"); D("lbl.Fire/UnknownAirMinKnots", "Engage unidentified aircraft faster than"); D("unit.Fire/UnknownAirMinKnots", "kn");
            D("lbl.Fire/UnknownAirMaxNm", "Engage unidentified aircraft closer than"); D("lbl.Fire/UnknownAirDefaultOwn", "On Free by default: your and allied ships");
            D("lbl.Fire/UnknownAirDefaultEnemy", "On Free by default: enemy AI ships");
            D("lbl.Ships/NoRbuSurfaceChase", "No chasing surface targets with ASW mortars");
            D("lbl.Ships/NoAirChase", "No chasing air targets");
            D("lbl.Guns/AimBeyondMaxRange", "Aim guns at targets beyond max range");
            D("lbl.Guns/MaxAccuracyMeters", "Gun maximum accuracy (best fire control error)");
            D("unit.Guns/MaxAccuracyMeters", "m");
            D("lbl.Guns/MachineGunBurstFix", "Machine gun tracers in the whole burst");
            D("lbl.Guns/MachineGunSoundLoopFix", "Seamless machine gun sound");
            D("lbl.Guns/HitSoundFix", "Always play gun hit sound");
            D("lbl.Optics/BinocularsNormalView", "Normal view in binoculars at max zoom");
            D("lbl.Optics/NormalViewDayOnly", "Normal view only by day");
            D("lbl.Effects/FireFlicker", "Fire light flicker");
            D("lbl.Effects/FireLightPower", "Fire light brightness");
            D("lbl.Effects/MuzzleFlashLight", "Light flash when guns fire");
            D("lbl.Effects/MissileLaunchFlashLight", "Light of missile boosters at launch");
            D("lbl.Effects/HitFlashLight", "Light flash when shells hit");
            D("lbl.Effects/FlashPower", "Flash brightness");
            D("lbl.General/Debug", "Debug trace");
            D("lbl.General/Language", "Language of the texts (empty = the game's)");
            D("unit.General/Language", "code, e.g. en, ru");
            D("unit.x", "x"); D("unit.nm", "nm");
        }

        // ------------------------------------------------------------ access

        /// <summary>The text of a key in the current language (English when it is not translated).</summary>
        public static string T(string key)
        {
            Check();
            string v;
            if (_tr.TryGetValue(key, out v) && v.Length > 0) return v;
            return En.TryGetValue(key, out v) ? v : key;
        }

        /// <summary>The text with {0}, {1} ... filled in.</summary>
        public static string F(string key, params object[] a)
        {
            var s = T(key);
            try { return string.Format(CultureInfo.InvariantCulture, s, a); }
            catch { try { return string.Format(CultureInfo.InvariantCulture, En.ContainsKey(key) ? En[key] : key, a); } catch { return s; } }
        }

        /// <summary>The translation of a key if there is one, else the given (English) text; for texts that are not in the table (.cfg descriptions).</summary>
        public static string Or(string key, string fallback)
        {
            Check();
            string v;
            return _tr.TryGetValue(key, out v) && v.Length > 0 ? v : fallback;
        }

        /// <summary>Texts known only at run time (the .cfg descriptions): put in the table so that the files get them.</summary>
        public static void Register(string key, string en)
        {
            if (string.IsNullOrEmpty(key) || En.ContainsKey(key)) return;
            Keys.Add(key); En[key] = en ?? "";
            _filesDone = false;
        }

        public static string Language { get { Check(); return _lang; } }

        static bool _checking;

        static void Check()
        {
            if (_checking) return;
            int now = Environment.TickCount;
            if (_checkedAt >= 0 && now - _checkedAt < 1500 && _filesDone) return;
            _checking = true;
            try
            {
                _checkedAt = now;
                string forced = "";
                try { forced = (Forced() ?? "").Trim(); } catch { }
                string lang = forced.Length > 0 ? forced : GameLanguage();
                if (lang != _lang || _tr.Count == 0 && !_filesDone || forced != _forced)
                {
                    _lang = lang; _forced = forced;
                    _tr = Load(lang);
                }
                if (!_filesDone) { _filesDone = true; try { MakeFiles(); } catch (Exception e) { Trace("lang files: " + e.Message); } _tr = Load(_lang); }
            }
            catch (Exception e) { Trace("Loc: " + e.Message); }
            finally { _checking = false; }
        }

        // ------------------------------------------------------------ the game's language

        static Type _tHandler, _tSingleton;

        static string GameLanguage()
        {
            try
            {
                var asm = AwcPlugin.GameAssembly();
                if (asm == null) return _lang;
                if (_tHandler == null) { _tHandler = asm.GetType("SeaPower.LanguageResourceHandler"); _tSingleton = asm.GetType("SeaPower.Singleton`1"); }
                var inst = _tSingleton.MakeGenericType(_tHandler).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public).GetValue(null, null);
                if (inst == null) return _lang;
                var l = _tHandler.GetProperty("Language").GetValue(inst, null) as string;
                return string.IsNullOrEmpty(l) ? "en" : l;
            }
            catch { return _lang; }
        }

        /// <summary>The languages of the game: its config.ini [Languages] (code → name); English alone when it cannot be read.</summary>
        public static List<KeyValuePair<string, string>> GameLanguages()
        {
            var res = new List<KeyValuePair<string, string>>();
            try
            {
                var asm = AwcPlugin.GameAssembly();
                var utils = asm == null ? null : asm.GetType("SeaPower.Utils");
                MethodInfo open = null;
                if (utils != null)
                    foreach (var m in utils.GetMethods(BindingFlags.Static | BindingFlags.Public))
                    {
                        var p = m.GetParameters();
                        if (m.Name == "openIniFile" && p.Length == 4 && p[0].ParameterType == typeof(string) && p[1].ParameterType == typeof(string)) { open = m; break; }
                    }
                if (open != null)
                {
                    var ini = open.Invoke(null, new object[] { "", "/config.ini", false, false });
                    var d = ini == null ? null : ini.GetType().GetMethod("GetSectionKeyValues", new[] { typeof(string) }).Invoke(ini, new object[] { "Languages" }) as Dictionary<string, string>;
                    if (d != null)
                        foreach (var kv in d)
                            if (!string.IsNullOrEmpty(kv.Key) && !string.IsNullOrEmpty(kv.Value)) res.Add(new KeyValuePair<string, string>(kv.Key, kv.Value));
                }
            }
            catch (Exception e) { Trace("game languages: " + e.Message); }
            if (res.Count == 0) res.Add(new KeyValuePair<string, string>("en", "English"));
            return res;
        }

        // ------------------------------------------------------------ files

        static string _dir;

        public static string Dir
        {
            get
            {
                if (_dir == null)
                {
                    string p = "";
                    try { p = Path.GetDirectoryName(typeof(Loc).Assembly.Location) ?? ""; } catch { }
                    _dir = Path.Combine(p, "lang");
                }
                return _dir;
            }
        }

        static string Esc(string s) { return (s ?? "").Replace("\\", "\\\\").Replace("\r", "").Replace("\n", "\\n"); }

        static string Unesc(string s)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length) { char n = s[++i]; sb.Append(n == 'n' ? '\n' : n == 't' ? '\t' : n); }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        static Dictionary<string, string> Load(string lang)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(lang) || lang.IndexOfAny(new[] { '/', '\\', ':' }) >= 0) return d;
            var path = Path.Combine(Dir, lang + ".ini");
            try
            {
                if (!File.Exists(path)) return d;
                foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    var line = raw.TrimEnd('\r');
                    if (line.Length == 0 || line[0] == ';' || line[0] == '#' || line[0] == '[') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    d[line.Substring(0, eq).Trim()] = Unesc(line.Substring(eq + 1).Trim());
                }
            }
            catch (Exception e) { Trace("lang " + lang + ": " + e.Message); }
            return d;
        }

        static void MakeFiles()
        {
            Directory.CreateDirectory(Dir);
            foreach (var l in GameLanguages()) Sync(l.Key, l.Value);
            Sync("en", "English");
            if (_lang != "en" && !File.Exists(Path.Combine(Dir, _lang + ".ini"))) Sync(_lang, _lang);
        }

        /// <summary>Creates the language file, or appends the keys it lacks.</summary>
        static void Sync(string code, string name)
        {
            if (code.IndexOfAny(new[] { '/', '\\', ':' }) >= 0) return;
            var path = Path.Combine(Dir, code + ".ini");
            bool english = code == "en";
            if (!File.Exists(path))
            {
                var sb = new StringBuilder();
                sb.AppendLine("; Alternative Weapons Control - texts, " + name + " (" + code + ")");
                sb.AppendLine("; key = text. Edit the text after '='. A line with no translation can stay English or be deleted.");
                sb.AppendLine("; \\n = line break; {0}, {1} ... are filled in by the mod - keep them.");
                sb.AppendLine("; The file is used when the game's language is '" + code + "' (or [General] Language in the .cfg).");
                sb.AppendLine();
                foreach (var k in Keys) sb.AppendLine(k + " = " + Esc(En[k]));
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                return;
            }
            var have = Load(code);
            var add = new StringBuilder();
            foreach (var k in Keys)
                if (!have.ContainsKey(k) && !HasKeyLine(path, k)) add.AppendLine(k + " = " + Esc(En[k]));
            if (add.Length > 0)
                File.AppendAllText(path, Environment.NewLine + "; --- new ---" + Environment.NewLine + add, new UTF8Encoding(false));
        }

        static HashSet<string> _lineCache; static string _lineCachePath;

        static bool HasKeyLine(string path, string key)
        {
            if (_lineCachePath != path)
            {
                _lineCachePath = path; _lineCache = new HashSet<string>(StringComparer.Ordinal);
                foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    var t = raw.TrimStart(';', '#', ' ');
                    int eq = t.IndexOf('=');
                    if (eq > 0) _lineCache.Add(t.Substring(0, eq).Trim());
                }
            }
            return _lineCache.Contains(key);
        }
    }
}
