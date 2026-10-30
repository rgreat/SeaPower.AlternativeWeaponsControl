using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using HarmonyLib;

namespace AlternativeWeaponsControl
{
    /// <summary>
    /// [Guns] AmmunitionFix: accuracy (CircularErrorProbable, metres) and warhead power of gun ammunition. Three layers: the game's own ini,
    /// the author's pack (ammunition_fix.txt in the mod folder: [ammunition file name] then Key=value as in the ini), and the player's
    /// values (AlternativeWeaponsControl.ammunition.txt next to the .cfg, same format, written by the Ammunition tab of the settings
    /// window; a key is stored only when it differs from the pack / the game). Not inis of the game: the mod reads the files itself and
    /// puts the values into the ammunition parameters right after the game has read the ammunition's own ini (postfix of the
    /// AmmunitionParameters constructor, which also makes the shared masters that Create() clones). The values the game derives from
    /// Power in the same constructor (dud power, ammunition points) follow it unless the ini set them itself. No polling.
    /// </summary>
    public partial class AwcPlugin
    {
        const string AmmoFixFile = "ammunition_fix.txt";
        const float UnitPerMetre = 0.0148809375f;
        static Dictionary<string, float[]> _ammoFixTable;   // ammunition -> { CircularErrorProbable, Power }, NaN = not set

        void HookAmmoFix(Harmony harmony, Assembly asm)
        {
            var t = asm.GetType("SeaPower.AmmunitionParameters");
            var ws = asm.GetType("SeaPower.WeaponSystem");
            var ctor = t == null || ws == null ? null : t.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(string), typeof(int), ws }, null);
            if (ctor == null) { Trace("AmmunitionParameters constructor not found: no ammunition fix"); return; }
            harmony.Patch(ctor, postfix: Hm("AmmoFixPostfix"));
        }

        // ------------------------------------------------------------ the pack and the player's values
        static List<string> _ammoNames;                       // pack order
        static Dictionary<string, float[]> _ammoUser;         // the player's values; null = not read yet

        static string ModFolder() { try { return Path.GetDirectoryName(typeof(AwcPlugin).Assembly.Location) ?? ""; } catch { return ""; } }
        static string UserAmmoPath()
        {
            var a = Active; string dir = "";
            try { dir = Path.GetDirectoryName(a.Cfg.ConfigFilePath) ?? ""; } catch { }
            return Path.Combine(dir, "AlternativeWeaponsControl.ammunition.txt");
        }

        static Dictionary<string, float[]> ReadAmmoFile(string path, List<string> order)
        {
            var res = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(path)) return res;
                float[] cur = null;
                foreach (var raw in File.ReadAllLines(path))
                {
                    var l = raw.Trim();
                    if (l.Length == 0 || l[0] == ';' || l[0] == '#') continue;
                    if (l[0] == '[' && l[l.Length - 1] == ']')
                    {
                        string n = l.Substring(1, l.Length - 2).Trim();
                        cur = new[] { float.NaN, float.NaN }; res[n] = cur; if (order != null) order.Add(n);
                        continue;
                    }
                    int eq = l.IndexOf('=');
                    if (cur == null || eq < 0) continue;
                    string k = l.Substring(0, eq).Trim(), v = l.Substring(eq + 1).Trim();
                    float f;
                    if (!float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) continue;
                    if (k == "CircularErrorProbable") cur[0] = f; else if (k == "Power") cur[1] = f;
                }
            }
            catch (Exception e) { Trace("ammo file " + path + ": " + e.Message); }
            return res;
        }

        static Dictionary<string, float[]> LoadAmmoFix()
        {
            _ammoNames = new List<string>();
            var res = ReadAmmoFile(Path.Combine(ModFolder(), AmmoFixFile), _ammoNames);
            _ammoUser = ReadAmmoFile(UserAmmoPath(), null);
            Trace("ammo fix: " + res.Count + " ammunition in the pack, " + _ammoUser.Count + " with the player's values");
            return res;
        }

        static void EnsureAmmoLoaded() { if (_ammoFixTable == null) _ammoFixTable = LoadAmmoFix(); }

        /// <summary>The ammunition of the pack in the file order.</summary>
        internal static List<string> AmmoNames() { EnsureAmmoLoaded(); return _ammoNames; }

        /// <summary>The pack's value (NaN = none): which 0 = CircularErrorProbable, 1 = Power.</summary>
        internal static float AmmoPack(string name, int which) { EnsureAmmoLoaded(); float[] v; return _ammoFixTable.TryGetValue(name, out v) ? v[which] : float.NaN; }

        /// <summary>The player's value (NaN = none).</summary>
        internal static float AmmoUser(string name, int which) { EnsureAmmoLoaded(); float[] v; return _ammoUser.TryGetValue(name, out v) ? v[which] : float.NaN; }

        /// <summary>The value that is applied: the player's, else the pack's (NaN = the game's).</summary>
        internal static float AmmoEffective(string name, int which)
        {
            float u = AmmoUser(name, which); return !float.IsNaN(u) ? u : AmmoPack(name, which);
        }

        internal sealed class AmmoGame { public float Cep = float.NaN, Power = float.NaN; public string Title = "?"; }
        static readonly Dictionary<string, AmmoGame> _ammoGame = new Dictionary<string, AmmoGame>(StringComparer.OrdinalIgnoreCase);
        static MethodInfo _mOpenIni, _mReadFloat, _mReadStr, _mOpenLoc;

        /// <summary>The game's own values of an ammunition (its ini as the game reads it) and its name in the game's language. Cached; read when the tab is shown.</summary>
        static string _baseDir; static bool _baseDirDone; static MethodInfo _mOpenIniFull;

        /// <summary>The ini of the ammunition from the base layer of the game (LayerKind.Base), so that other mods that replace the file do not show up as "game" values.</summary>
        static object OpenBaseAmmoIni(Assembly asm, string name)
        {
            try
            {
                if (!_baseDirDone)
                {
                    _baseDirDone = true;
                    var fmT = asm.GetType("SeaPower.FileManager");
                    var inst = fmT == null ? null : fmT.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy).GetValue(null, null);
                    var dirs = inst == null ? null : fmT.GetProperty("Directories").GetValue(inst, null) as System.Collections.IEnumerable;
                    if (dirs != null)
                        foreach (var d in dirs)
                        {
                            var dt = d.GetType();
                            if (!string.Equals(dt.GetProperty("Kind").GetValue(d, null).ToString(), "Base", StringComparison.Ordinal)) continue;
                            if (!(bool)dt.GetProperty("IsRootLayout").GetValue(d, null)) continue;
                            var di = dt.GetProperty("DirectoryInfo").GetValue(d, null) as System.IO.DirectoryInfo;
                            if (di != null) { _baseDir = di.FullName; break; }
                        }
                    Trace("ammo tab: base layer " + (_baseDir ?? "not found"));
                }
                if (_baseDir == null) return null;
                string path = System.IO.Path.Combine(System.IO.Path.Combine(_baseDir, "ammunition"), name + ".ini");
                if (!System.IO.File.Exists(path)) return null;
                if (_mOpenIniFull == null) _mOpenIniFull = asm.GetType("SeaPower.Utils").GetMethod("openIniFile", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(string), typeof(bool), typeof(bool), typeof(bool) }, null);
                return _mOpenIniFull.Invoke(null, new object[] { path, false, false, true });
            }
            catch (Exception e) { Trace("ammo tab: base ini " + name + ": " + e.Message); return null; }
        }

        /// <summary>Reads CircularErrorProbable ([Physics], default 50) and Power ([WarheadData], default 0) from StreamingAssets/original/ammunition/&lt;name&gt;.ini on disk, without the game's layers (other mods).</summary>
        static bool ReadGameIni(string name, out float cep, out float power)
        {
            cep = 50f; power = 0f;
            try
            {
                string sa = UnityEngine.Application.streamingAssetsPath;
                string path = Path.Combine(Path.Combine(Path.Combine(sa, "original"), "ammunition"), name + ".ini");   // the untouched files of the game
                if (!File.Exists(path)) return false;
                string sec = "";
                foreach (var raw in File.ReadAllLines(path))
                {
                    string l = raw; int c = l.IndexOf("//"); if (c >= 0) l = l.Substring(0, c);
                    c = l.IndexOf(';'); if (c >= 0) l = l.Substring(0, c);
                    l = l.Trim(); if (l.Length == 0 || l[0] == '#') continue;
                    if (l[0] == '[') { int e = l.IndexOf(']'); sec = e > 0 ? l.Substring(1, e - 1).Trim() : ""; continue; }
                    int eq = l.IndexOf('='); if (eq <= 0) continue;
                    string k = l.Substring(0, eq).Trim(), v = l.Substring(eq + 1).Trim();
                    float f;
                    if (!float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f)) continue;
                    if (sec.Equals("Physics", StringComparison.OrdinalIgnoreCase) && k.Equals("CircularErrorProbable", StringComparison.OrdinalIgnoreCase)) cep = f;
                    else if (sec.Equals("WarheadData", StringComparison.OrdinalIgnoreCase) && k.Equals("Power", StringComparison.OrdinalIgnoreCase)) power = f;
                }
                return true;
            }
            catch (Exception e) { Trace("ammo tab: ini " + name + ": " + e.Message); return false; }
        }

        internal static AmmoGame AmmoGameValues(string name)
        {
            AmmoGame g;
            if (_ammoGame.TryGetValue(name, out g)) return g;
            g = new AmmoGame(); _ammoGame[name] = g;
            try
            {
                const BindingFlags st = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                var asm = _tObjectBase.Assembly;
                if (_mOpenIni == null) _mOpenIni = asm.GetType("SeaPower.Utils").GetMethod("openIniFile", st, null, new[] { typeof(string), typeof(string), typeof(bool), typeof(bool) }, null);
                float dc, dp;
                if (ReadGameIni(name, out dc, out dp)) { g.Cep = dc; g.Power = dp; }   // straight from the game's own file on disk (the layers of the game are replaced by mods)
                else Trace("ammo tab: " + name + ": no file in StreamingAssets/original/ammunition");
                if (_mOpenLoc == null) _mOpenLoc = asm.GetType("SeaPower.LocalizationLoader").GetMethod("OpenVirtualLocalizationFile", st, null, new[] { typeof(string), typeof(string), typeof(string), typeof(bool) }, null);
                foreach (var lang in new[] { Loc.Language, "en" })
                {
                    var loc = _mOpenLoc.Invoke(null, new object[] { "language_" + lang, "ammunition_names", "ammunition_names", true });
                    if (loc == null) continue;
                    if (_mReadStr == null) _mReadStr = loc.GetType().GetMethod("readValue", new[] { typeof(string), typeof(string), typeof(string), typeof(bool) });
                    var t = _mReadStr.Invoke(loc, new object[] { "AmmunitionNames", name, "", false }) as string;
                    if (string.IsNullOrEmpty(t)) continue;
                    int c = t.IndexOf(','); g.Title = (c > 0 ? t.Substring(0, c) : t).Trim();   // the line is "name,,type,description"
                    break;
                }
            }
            catch (Exception e) { Trace("ammo tab: " + name + ": " + e.Message); }
            return g;
        }

        /// <summary>Applies the player's new values: writes the file (only keys that differ from the pack / the game) and drops the shared ammunition parameters of the changed ammunition, so the next load makes them again.</summary>
        internal static void AmmoSaveUser(Dictionary<string, float[]> user)
        {
            EnsureAmmoLoaded();
            var changed = new List<string>();
            foreach (var n in _ammoNames)
            {
                float[] o, w; _ammoUser.TryGetValue(n, out o); user.TryGetValue(n, out w);
                for (int i = 0; i < 2; i++)
                {
                    float a = o == null ? float.NaN : o[i], b = w == null ? float.NaN : w[i];
                    if (!(float.IsNaN(a) && float.IsNaN(b)) && !(Math.Abs(a - b) < 1e-5f)) { changed.Add(n); break; }
                }
            }
            _ammoUser = user;
            try
            {
                var lines = new List<string> { "; The player's ammunition values (Ammunition tab of the settings window). Same format as ammunition_fix.txt; a key is here only when it differs from the pack / the game." };
                foreach (var n in _ammoNames)
                {
                    float[] v; if (!user.TryGetValue(n, out v)) continue;
                    if (float.IsNaN(v[0]) && float.IsNaN(v[1])) continue;
                    lines.Add(""); lines.Add("[" + n + "]");
                    if (!float.IsNaN(v[0])) lines.Add("CircularErrorProbable=" + v[0].ToString("0.####", CultureInfo.InvariantCulture));
                    if (!float.IsNaN(v[1])) lines.Add("Power=" + v[1].ToString("0.####", CultureInfo.InvariantCulture));
                }
                File.WriteAllLines(UserAmmoPath(), lines.ToArray());
            }
            catch (Exception e) { Trace("ammo user file: " + e.Message); }
            if (changed.Count > 0) DropSharedAmmo(changed);
        }

        static void DropSharedAmmo(List<string> names)
        {
            try
            {
                var t = _tObjectBase.Assembly.GetType("SeaPower.AmmunitionParameters");
                var d = t.GetField("_sharedByType", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).GetValue(null) as System.Collections.IDictionary;
                if (d == null) return;
                var keys = new List<object>(); foreach (var k in d.Keys) keys.Add(k);
                int n = 0;
                foreach (var k in keys)
                {
                    var name = k.GetType().GetField("Item1").GetValue(k) as string;
                    if (name != null && names.Contains(name)) { d.Remove(k); n++; }
                }
                Trace("ammo fix: " + n + " shared ammunition parameters dropped, they are made again with the new values");
            }
            catch (Exception e) { Trace("ammo drop: " + e.Message); }
        }

        static void AmmoFixPostfix(object __instance)
        {
            try
            {
                var a = Active; if (a == null || !Enabled || a._ammoFix == null || !a._ammoFix.Value) return;
                EnsureAmmoLoaded();
                var name = R.F(__instance, "_ammunitionFileName") as string; if (name == null) return;
                float[] v = { AmmoEffective(name, 0), AmmoEffective(name, 1) };
                if (float.IsNaN(v[0]) && float.IsNaN(v[1])) return;
                var type = R.F(__instance, "_type");
                bool missile = type != null && type.ToString() == "Missile";
                if (!float.IsNaN(v[0]) && type != null && type.ToString() == "Projectile") R.SetF(__instance, "_circularErrorProbable", v[0] * UnitPerMetre);   // [Physics] is read for projectiles only
                if (!float.IsNaN(v[1]))
                {
                    float oldP = R.Fl(R.F(__instance, "_warheadPower")), oldDud = R.Fl(R.F(__instance, "_warheadDudPower")), oldPts = R.Fl(R.F(__instance, "_ammoPoints"));
                    float mult = missile ? 0.5f : 0.2f;
                    R.SetF(__instance, "_warheadPower", v[1]);
                    if (Math.Abs(oldDud - oldP * mult) < 1e-4f) R.SetF(__instance, "_warheadDudPower", v[1] * mult);   // the game's default, not the ini's own DudPower
                    if (Math.Abs(oldPts - oldP) < 1e-4f) R.SetF(__instance, "_ammoPoints", v[1]);                       // the game's default AmmoPoints = Power
                }
                TraceLimited("ammofix" + name, 3600f, "ammo fix: " + name + " cep=" + v[0] + " power=" + v[1]);
            }
            catch (Exception e) { TraceLimited("ammofixerr", 5f, "ammo fix: " + e.Message); }
        }
    }
}
