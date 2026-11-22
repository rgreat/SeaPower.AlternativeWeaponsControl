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
        static Dictionary<string, float[]> _ammoUser;         // the player's values { accuracy, power, listed, removed }; null = not read yet
        static List<string> _ammoUserOrder = new List<string>();   // the order of the sections of the player's file (the added ammunition keeps it)

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
                        cur = new[] { float.NaN, float.NaN, float.NaN, float.NaN }; res[n] = cur; if (order != null) order.Add(n);
                        continue;
                    }
                    int eq = l.IndexOf('=');
                    if (cur == null || eq < 0) continue;
                    string k = l.Substring(0, eq).Trim(), v = l.Substring(eq + 1).Trim();
                    float f;
                    if (!float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) continue;
                    if (k == "CircularErrorProbable") cur[0] = f; else if (k == "Power") cur[1] = f;
                    else if (k == "Listed") cur[2] = f != 0f ? 1f : float.NaN; else if (k == "Removed") cur[3] = f != 0f ? 1f : float.NaN;
                }
            }
            catch (Exception e) { Trace("ammo file " + path + ": " + e.Message); }
            return res;
        }

        static Dictionary<string, float[]> LoadAmmoFix()
        {
            _ammoNames = new List<string>();
            var res = ReadAmmoFile(Path.Combine(ModFolder(), AmmoFixFile), _ammoNames);
            _ammoUserOrder = new List<string>(); _ammoUser = ReadAmmoFile(UserAmmoPath(), _ammoUserOrder);
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
            if (AmmoRemoved(name)) return float.NaN;   // taken off the list: the game's values
            float u = AmmoUser(name, which); return !float.IsNaN(u) ? u : AmmoPack(name, which);
        }

        /// <summary>A pack ammunition the player took off the list: neither the pack's nor any value applies.</summary>
        internal static bool AmmoRemoved(string name) { EnsureAmmoLoaded(); float[] v; return _ammoUser.TryGetValue(name, out v) && !float.IsNaN(v[3]); }

        internal static bool AmmoIsPack(string name) { EnsureAmmoLoaded(); return _ammoFixTable.ContainsKey(name); }

        /// <summary>The list of the table: the pack's ammunition (except the removed) and the ammunition the player added, in the file order.</summary>
        internal static List<string> AmmoListNames()
        {
            EnsureAmmoLoaded();
            var r = new List<string>();
            foreach (var n in _ammoNames) if (!AmmoRemoved(n)) r.Add(n);
            foreach (var n in _ammoUserOrder)
            {
                float[] v;
                if (_ammoUser.TryGetValue(n, out v) && !float.IsNaN(v[2]) && !r.Exists(x => string.Equals(x, n, StringComparison.OrdinalIgnoreCase))) r.Add(n);
            }
            return r;
        }

        // ------------------------------------------------------------ every gun ammunition of the game (for the Add button)
        static List<string> _ammoDirs, _ammoAll;

        /// <summary>The ammunition folders of the game: its own files and every layer (mods).</summary>
        static List<string> AmmoDirs()
        {
            if (_ammoDirs != null) return _ammoDirs;
            _ammoDirs = new List<string>();
            try { var o = Path.Combine(Path.Combine(UnityEngine.Application.streamingAssetsPath, "original"), "ammunition"); if (Directory.Exists(o)) _ammoDirs.Add(o); } catch { }
            try
            {
                var asm = _tObjectBase.Assembly;
                var fmT = asm.GetType("SeaPower.FileManager");
                var inst = fmT == null ? null : fmT.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy).GetValue(null, null);
                var dirs = inst == null ? null : fmT.GetProperty("Directories").GetValue(inst, null) as System.Collections.IEnumerable;
                if (dirs != null)
                    foreach (var d in dirs)
                    {
                        var di = d.GetType().GetProperty("DirectoryInfo").GetValue(d, null) as DirectoryInfo;
                        if (di == null) continue;
                        var a = Path.Combine(di.FullName, "ammunition");
                        if (Directory.Exists(a) && !_ammoDirs.Contains(a)) _ammoDirs.Add(a);
                    }
            }
            catch (Exception e) { Trace("ammo dirs: " + e.Message); }
            return _ammoDirs;
        }

        /// <summary>Names of all projectile ammunition (shells and bullets of guns): the files of the game's ammunition folders with Type=Projectile in [General].</summary>
        internal static List<string> AllGunAmmo()
        {
            if (_ammoAll != null) return _ammoAll;
            var set = new SortedDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var dir in AmmoDirs())
            {
                try
                {
                    foreach (var f in Directory.GetFiles(dir, "*.ini"))
                    {
                        string name = Path.GetFileNameWithoutExtension(f);
                        if (set.ContainsKey(name)) continue;
                        string sec = "";
                        foreach (var raw in File.ReadLines(f))
                        {
                            string l = raw; int c = l.IndexOf("//"); if (c >= 0) l = l.Substring(0, c);
                            l = l.Trim(); if (l.Length == 0 || l[0] == '#' || l[0] == ';') continue;
                            if (l[0] == '[') { int e = l.IndexOf(']'); sec = e > 0 ? l.Substring(1, e - 1).Trim() : ""; if (!sec.Equals("General", StringComparison.OrdinalIgnoreCase)) break; continue; }
                            int eq = l.IndexOf('='); if (eq <= 0) continue;
                            if (l.Substring(0, eq).Trim().Equals("Type", StringComparison.OrdinalIgnoreCase)) { if (l.Substring(eq + 1).Trim().Equals("Projectile", StringComparison.OrdinalIgnoreCase)) set[name] = true; break; }
                        }
                    }
                }
                catch (Exception e) { Trace("ammo list " + dir + ": " + e.Message); }
            }
            _ammoAll = new List<string>(set.Keys);
            Trace("ammo list: " + _ammoAll.Count + " projectile ammunition in " + AmmoDirs().Count + " folders");
            return _ammoAll;
        }

        internal sealed class AmmoGame { public float Cep = float.NaN, Power = float.NaN; public string Title = "?"; }
        static readonly Dictionary<string, AmmoGame> _ammoGame = new Dictionary<string, AmmoGame>(StringComparer.OrdinalIgnoreCase);
        static MethodInfo _mOpenIni, _mReadStr, _mOpenLoc;

        /// <summary>Reads CircularErrorProbable ([Physics], default 50) and Power ([WarheadData], default 0) from StreamingAssets/original/ammunition/&lt;name&gt;.ini on disk, without the game's layers (other mods).</summary>
        static bool ReadGameIni(string name, out float cep, out float power)
        {
            cep = 50f; power = 0f;
            try
            {
                string path = null;   // the untouched files of the game first, then the layers (mods) for ammunition that the game itself does not have
                foreach (var d in AmmoDirs()) { var p = Path.Combine(d, name + ".ini"); if (File.Exists(p)) { path = p; break; } }
                if (path == null) return false;
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
        internal static void AmmoSaveUser(Dictionary<string, float[]> user, List<string> order)
        {
            EnsureAmmoLoaded();
            var names = new List<string>(_ammoNames);
            Action<string> addName = n => { if (!names.Exists(x => string.Equals(x, n, StringComparison.OrdinalIgnoreCase))) names.Add(n); };
            foreach (var n in order) addName(n);
            foreach (var n in _ammoUserOrder) addName(n);
            foreach (var n in user.Keys) addName(n);
            var changed = new List<string>();
            foreach (var n in names)
            {
                float[] o, w; _ammoUser.TryGetValue(n, out o); user.TryGetValue(n, out w);
                for (int i = 0; i < 4; i++)
                {
                    float a = o == null ? float.NaN : o[i], b = w == null ? float.NaN : w[i];
                    if (!(float.IsNaN(a) && float.IsNaN(b)) && !(Math.Abs(a - b) < 1e-5f)) { changed.Add(n); break; }
                }
            }
            _ammoUser = user; _ammoUserOrder = new List<string>(order);
            try
            {
                var lines = new List<string> { "; The player's ammunition values (Guns tab of the settings window). Same format as ammunition_fix.txt; a key is here only when it differs from the pack / the game. Listed=1: ammunition added to the table; Removed=1: pack ammunition taken off the table (the game's values apply)." };
                foreach (var n in names)
                {
                    float[] v; if (!user.TryGetValue(n, out v)) continue;
                    if (float.IsNaN(v[0]) && float.IsNaN(v[1]) && float.IsNaN(v[2]) && float.IsNaN(v[3])) continue;
                    lines.Add(""); lines.Add("[" + n + "]");
                    if (!float.IsNaN(v[2])) lines.Add("Listed=1");
                    if (!float.IsNaN(v[3])) lines.Add("Removed=1");
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
                var a = Active; if (a == null || !Enabled) return;
                bool fix = a._ammoFix != null && a._ammoFix.Value;
                float am = a._accuracyMult == null ? 1f : a._accuracyMult.Value;
                if (!fix && Math.Abs(am - 1f) < 1e-4f) return;
                var name = R.F(__instance, "_ammunitionFileName") as string; if (name == null) return;
                float[] v = { float.NaN, float.NaN };
                if (fix) { EnsureAmmoLoaded(); v[0] = AmmoEffective(name, 0); v[1] = AmmoEffective(name, 1); }
                // the global accuracy multiplier first: it scales the game's own error radius; the pack's / the player's values below replace it as they are
                if (am > 0.0001f && Math.Abs(am - 1f) >= 1e-4f)
                {
                    var tp = R.F(__instance, "_type");
                    if (tp != null && tp.ToString() == "Projectile") R.SetF(__instance, "_circularErrorProbable", R.Fl(R.F(__instance, "_circularErrorProbable")) / am);
                }
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
