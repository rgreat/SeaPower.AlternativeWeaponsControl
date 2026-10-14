using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;

namespace AlternativeWeaponsControl
{
    /// <summary>Reflection helpers for game objects (fields and properties of any visibility, methods by name; cached).</summary>
    static class R
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        static readonly Dictionary<string, FieldInfo> Fields = new Dictionary<string, FieldInfo>();
        static readonly Dictionary<string, PropertyInfo> Props = new Dictionary<string, PropertyInfo>();
        static readonly Dictionary<string, MethodInfo[]> Methods = new Dictionary<string, MethodInfo[]>();

        static FieldInfo Fi(Type t, string n)
        {
            var k = t.FullName + "#" + n;
            FieldInfo f;
            if (Fields.TryGetValue(k, out f)) return f;
            for (var ty = t; ty != null && f == null; ty = ty.BaseType) f = ty.GetField(n, All);
            Fields[k] = f;
            return f;
        }

        static PropertyInfo Pi(Type t, string n)
        {
            var k = t.FullName + "#" + n;
            PropertyInfo p;
            if (Props.TryGetValue(k, out p)) return p;
            for (var ty = t; ty != null && p == null; ty = ty.BaseType) p = ty.GetProperty(n, All);
            Props[k] = p;
            return p;
        }

        public static object F(object o, string n)
        {
            if (o == null) return null;
            try { var f = Fi(o.GetType(), n); return f == null ? null : f.GetValue(o); } catch { return null; }
        }

        public static void SetF(object o, string n, object v)
        {
            if (o == null) return;
            try { var f = Fi(o.GetType(), n); if (f != null) f.SetValue(o, v); } catch { }
        }

        public static object P(object o, string n)
        {
            if (o == null) return null;
            try { var p = Pi(o.GetType(), n); return p == null ? null : p.GetValue(o, null); } catch { return null; }
        }

        public static void SetP(object o, string n, object v)
        {
            if (o == null) return;
            try { var p = Pi(o.GetType(), n); if (p != null) p.SetValue(o, v, null); } catch { }
        }

        /// <summary>A field or a property.</summary>
        public static object V(object o, string n)
        {
            if (o == null) return null;
            var f = Fi(o.GetType(), n);
            if (f != null) { try { return f.GetValue(o); } catch { return null; } }
            return P(o, n);
        }

        /// <summary>Value of a ReactiveProperty-like object (its Value property), or the object itself.</summary>
        public static object Rv(object o) { var v = P(o, "Value"); return v ?? o; }

        /// <summary>Calls a method by name: the overload whose parameters take the arguments; missing trailing ones get their defaults.</summary>
        public static object Call(object o, string n, params object[] args)
        {
            if (o == null) return null;
            var t = o.GetType();
            var k = t.FullName + "#" + n;
            MethodInfo[] ms;
            if (!Methods.TryGetValue(k, out ms))
            {
                var l = new List<MethodInfo>();
                for (var ty = t; ty != null; ty = ty.BaseType)
                    foreach (var m in ty.GetMethods(All)) if (m.Name == n && !m.ContainsGenericParameters) l.Add(m);
                ms = l.ToArray();
                Methods[k] = ms;
            }
            foreach (var m in ms)
            {
                var ps = m.GetParameters();
                if (ps.Length < args.Length) continue;
                bool ok = true;
                for (int i = 0; i < ps.Length && ok; i++)
                {
                    if (i < args.Length)
                    {
                        var a = args[i];
                        if (a == null) ok = !ps[i].ParameterType.IsValueType;
                        else ok = ps[i].ParameterType.IsInstanceOfType(a);
                    }
                    else ok = ps[i].IsOptional;
                }
                if (!ok) continue;
                var full = new object[ps.Length];
                for (int i = 0; i < ps.Length; i++) full[i] = i < args.Length ? args[i] : ps[i].DefaultValue;
                return m.Invoke(o, full);
            }
            throw new MissingMethodException(t.Name, n);
        }

        public static bool B(object v) { return v is bool && (bool)v; }
        public static int I(object v) { try { return v == null ? 0 : Convert.ToInt32(v, CultureInfo.InvariantCulture); } catch { return 0; } }
        public static float Fl(object v) { try { return v == null ? 0f : Convert.ToSingle(v, CultureInfo.InvariantCulture); } catch { return 0f; } }
        public static string S(object v) { return v == null ? null : v as string ?? v.ToString(); }
        public static bool Is(object o, Type t) { return o != null && t != null && t.IsInstanceOfType(o); }
    }

    /// <summary>
    /// The Weapons tab model: the launchers of a unit (its weapon systems except decoys), grouped by kind; the target domains each can
    /// engage; the player's "no automatic use" switches per launcher and domain, merged with the unit's Standing Orders
    /// (TacticalDoctrine items AutoAttackSurface, ShipsAutoUseSAMsAgainstSurfaceTargets, ShipsAutoUseAntiShipMissilesAgainstSurfaceTargets);
    /// kept in the mission save (the unit's section); used by the AI hooks (WeaponsAi.cs).
    /// </summary>
    public partial class AwcPlugin
    {
        public const int DAir = 1, DMsl = 2, DSur = 4, DSub = 8, DLnd = 16, DAll = 31;
        public static readonly int[] Domains = { DAir, DMsl, DSur, DSub, DLnd };
        public static readonly string[] DomainNames = { "AIR", "MSL", "SUR", "SUB", "LND" };
        public static readonly string[] DomainLong = { "aircraft and helicopters", "missiles and other weapons in flight", "surface ships", "submarines", "land targets" };

        // Standing Orders items mapped onto launcher domains
        const string ItAuto = "AutoAttackSurface", ItSam = "ShipsAutoUseSAMsAgainstSurfaceTargets", ItAsm = "ShipsAutoUseAntiShipMissilesAgainstSurfaceTargets";
        static readonly string[] Items = { ItAuto, ItSam, ItAsm };
        static readonly string[] ItemLabels = { "Auto attack surface", "Ships auto-use SAMs against surface targets", "Ships auto-use anti-ship missiles against surface targets" };

        static Type _tObjectBase, _tVessel, _tSubmarine, _tLandUnit, _tTorpedo, _tWeaponBase, _tCiws, _tWeaponStatus;

        static void InitWeaponTypes(Assembly asm)
        {
            if (_tObjectBase != null || asm == null) return;
            _tObjectBase = asm.GetType("SeaPower.ObjectBase");
            _tVessel = asm.GetType("SeaPower.Vessel");
            _tSubmarine = asm.GetType("SeaPower.Submarine");
            _tLandUnit = asm.GetType("SeaPower.LandUnit");
            _tTorpedo = asm.GetType("SeaPower.Torpedo");
            _tWeaponBase = asm.GetType("SeaPower.WeaponBase");
            _tCiws = asm.GetType("SeaPower.WeaponSystemCIWS");
            _tWeaponStatus = _tObjectBase == null ? null : _tObjectBase.GetNestedType("WeaponStatus");
        }

        /// <summary>The domain of a target object (one of DAir ... DLnd), 0 when none.</summary>
        static int TargetDomain(object t)
        {
            if (t == null) return 0;
            try
            {
                if (R.Is(t, _tWeaponBase)) return R.Is(t, _tTorpedo) ? DSub : DMsl;
                if (R.B(R.P(t, "IsAirUnit"))) return DAir;
                if (R.Is(t, _tLandUnit)) return DLnd;
                if (R.Is(t, _tSubmarine)) return R.B(R.Call(t, "isSubmerged")) ? DSub : DSur;
                if (R.Is(t, _tVessel)) return DSur;
                return DSub;   // biologics
            }
            catch { return 0; }
        }

        // ------------------------------------------------------------------ launchers

        public sealed class Launcher
        {
            public object Ws;            // WeaponSystem
            public int Index;            // in _obp._weaponSystems
            public string Name, Kind;    // display name; section
            public int Section;          // order of the section
            public string Loc = "";      // "fore port" ...
            public List<string> Ammo = new List<string>();
            public int Domains;          // domains its ammo can engage
            public bool HasSam, HasAsm;  // Standing Orders items that cover its ammo
            public int[] AmmoDom;        // per Ammo entry
        }

        public static readonly string[] SectionNames = { "SAM", "ANTI-SHIP", "LAND ATTACK", "ASW", "GUNS", "CIWS", "OTHER" };

        static int AmmoDomains(object ap)
        {
            if (ap == null) return 0;
            int d = 0;
            Action<string> t = s =>
            {
                if (s == "AAW") d |= DAir | DMsl;
                else if (s == "ASuW") d |= DSur;
                else if (s == "ASW") d |= DSub;
            };
            t(R.S(R.F(ap, "_targetType")));
            if (R.B(R.F(ap, "_hasSecondaryTargetType"))) t(R.S(R.F(ap, "_secondaryTargetType")));
            var la = R.F(ap, "_landAttack") as ICollection;
            if (la != null && la.Count > 0) d |= DLnd;
            if (R.B(R.F(ap, "_antiTorpedo"))) d |= DSub;
            return d;
        }

        static bool IsSam(object ap)
        {
            return R.S(R.F(ap, "_type")) == "Missile" && R.S(R.F(ap, "_targetType")) == "AAW" && R.B(R.F(ap, "_hasSecondaryTargetType")) && R.S(R.F(ap, "_secondaryTargetType")) == "ASuW";
        }

        static bool IsAsm(object ap) { return R.S(R.F(ap, "_type")) == "Missile" && R.S(R.F(ap, "_targetType")) == "ASuW"; }

        /// <summary>The unit's launchers (vessels and submarines; not chaff, noisemakers, hardpoints).</summary>
        public static List<Launcher> Launchers(object unit)
        {
            var res = new List<Launcher>();
            var obp = R.F(unit, "_obp");
            var list = R.F(obp, "_weaponSystems") as IList;
            if (list == null) return res;
            for (int i = 0; i < list.Count; i++)
            {
                var ws = list[i];
                if (ws == null) continue;
                var wt = R.S(R.F(ws, "_weaponType"));
                if (wt == null || wt == "UNKNOWN" || wt == "Chaff" || wt == "Noisemaker" || wt == "Hardpoint") continue;
                var l = new Launcher { Ws = ws, Index = i, Name = R.S(R.Rv(R.P(ws, "DisplayName"))) ?? R.S(R.F(ws, "_systemName")) ?? "?" };
                var names = R.F(ws, "_associatedAmmunitionNames") as IList;
                var doms = new List<int>();
                object first = null;
                if (names != null)
                    foreach (var o in names)
                    {
                        var id = o as string;
                        if (string.IsNullOrEmpty(id) || l.Ammo.Contains(id)) continue;
                        object ammo = null;
                        try { ammo = R.Call(unit, "getAmmunitionByName", id); } catch { }
                        var ap = R.F(ammo, "_ap");
                        if (ap == null) continue;
                        l.Ammo.Add(id);
                        int d = AmmoDomains(ap);
                        doms.Add(d);
                        l.Domains |= d;
                        if (IsSam(ap)) l.HasSam = true;
                        if (IsAsm(ap)) l.HasAsm = true;
                        if (first == null) first = ap;
                    }
                l.AmmoDom = doms.ToArray();
                if (l.Domains == 0) continue;
                if (wt == "CIWS") l.Section = 5;
                else if (wt == "Gun") l.Section = 4;
                else
                {
                    var type = R.S(R.F(first, "_type"));
                    var tt = R.S(R.F(first, "_targetType"));
                    if (tt == "ASW" || type == "Torpedo" || type == "RBU" || type == "ASROC") l.Section = 3;
                    else if (tt == "AAW") l.Section = 0;
                    else if (tt == "ASuW") l.Section = 1;
                    else if ((l.Domains & DLnd) != 0) l.Section = 2;
                    else l.Section = 6;
                }
                l.Kind = SectionNames[l.Section];
                res.Add(l);
            }
            // where the mounts are: fore / aft of the middle, port / starboard (relative to the widest mount)
            try
            {
                var ut = ((Component)unit).transform;
                var pos = new Vector3[res.Count];
                float maxX = 0f;
                for (int i = 0; i < res.Count; i++)
                {
                    var go = R.F(res[i].Ws, "_mountObject") as GameObject;
                    pos[i] = go == null ? new Vector3(float.NaN, 0f, 0f) : ut.InverseTransformPoint(go.transform.position);
                    if (!float.IsNaN(pos[i].x)) maxX = Mathf.Max(maxX, Mathf.Abs(pos[i].x));
                }
                for (int i = 0; i < res.Count; i++)
                {
                    if (float.IsNaN(pos[i].x)) continue;
                    var s = pos[i].z >= 0f ? "fore" : "aft";
                    if (maxX > 0f && Mathf.Abs(pos[i].x) > maxX * 0.25f) s += pos[i].x < 0f ? " port" : " starboard";
                    res[i].Loc = s;
                }
            }
            catch { }
            return res;
        }

        // ------------------------------------------------------------------ per-unit switches

        public sealed class UnitW
        {
            public readonly Dictionary<int, int> Off = new Dictionary<int, int>();   // launcher index -> domains the player turned off
            public readonly bool[] Snap = new bool[Items.Length];                    // Standing Orders values last seen
            public bool SnapInit;
            public List<Launcher> Cache;                                            // launchers (for the AI hooks)
            public readonly Dictionary<object, int> IndexOf = new Dictionary<object, int>();
        }

        static readonly ConditionalWeakTable<object, UnitW> UnitsW = new ConditionalWeakTable<object, UnitW>();

        public static UnitW W(object unit) { return UnitsW.GetOrCreateValue(unit); }

        static List<Launcher> CachedLaunchers(object unit, UnitW w)
        {
            if (w.Cache == null)
            {
                w.Cache = Launchers(unit);
                w.IndexOf.Clear();
                foreach (var l in w.Cache) w.IndexOf[l.Ws] = l.Index;
            }
            return w.Cache;
        }

        static object DoctrineItem(object unit, int item)
        {
            var doc = R.P(unit, "TacticalDoctrine");
            return R.P(doc, Items[item]);
        }

        /// <summary>Does the item apply to this unit at all (the AI checks it for player vessels; AutoAttackSurface for any non-land unit).</summary>
        static bool ItemApplies(object unit, int item)
        {
            if (!R.B(R.P(unit, "IsPlayerObject"))) return false;
            if (item == 0) return !R.Is(unit, _tLandUnit);
            return R.Is(unit, _tVessel);
        }

        static bool ItemValue(object unit, int item)
        {
            var it = DoctrineItem(unit, item);
            return it == null || R.B(R.P(it, "ResolvedValue"));
        }

        /// <summary>Does the item cover this launcher / domain (would its "No" block it)?</summary>
        static bool Covers(int item, Launcher l, int d)
        {
            if ((l.Domains & d) == 0) return false;
            if (item == 0) return (d & (DSur | DSub | DLnd)) != 0;
            if (item == 1) return l.HasSam && (d & (DSur | DSub | DLnd)) != 0;
            return l.HasAsm && d == DSur;
        }

        /// <summary>Is automatic use of the launcher against the domain allowed by the unit's Standing Orders? blockers: the items saying No.</summary>
        static bool DocAllowed(object unit, Launcher l, int d, List<int> blockers)
        {
            bool ok = true;
            if ((d & (DSur | DSub | DLnd)) != 0 && ItemApplies(unit, 0) && !ItemValue(unit, 0)) { ok = false; if (blockers != null) blockers.Add(0); }
            // per ammunition: the domain stays allowed while one of its ammunition types able to engage it is not blocked
            bool sam = l.HasSam && ItemApplies(unit, 1) && !ItemValue(unit, 1) && (d & (DSur | DSub | DLnd)) != 0;
            bool asm = l.HasAsm && ItemApplies(unit, 2) && !ItemValue(unit, 2) && d == DSur;
            if (sam || asm)
            {
                bool any = false;
                var obp = unit;
                for (int i = 0; i < l.Ammo.Count; i++)
                {
                    if ((l.AmmoDom[i] & d) == 0) continue;
                    object ap = null;
                    try { ap = R.F(R.Call(obp, "getAmmunitionByName", l.Ammo[i]), "_ap"); } catch { }
                    if (ap == null) continue;
                    if ((sam && IsSam(ap)) || (asm && IsAsm(ap))) continue;
                    any = true;
                    break;
                }
                if (!any)
                {
                    ok = false;
                    if (blockers != null) { if (sam) blockers.Add(1); if (asm) blockers.Add(2); }
                }
            }
            return ok;
        }

        public enum Chip { None, On, Off, Order, Mixed }

        public static Chip State(object unit, Launcher l, int d)
        {
            if ((l.Domains & d) == 0) return Chip.None;
            if (!DocAllowed(unit, l, d, null)) return Chip.Order;
            int off;
            return W(unit).Off.TryGetValue(l.Index, out off) && (off & d) != 0 ? Chip.Off : Chip.On;
        }

        static bool Effective(object unit, UnitW w, Launcher l, int d)
        {
            if ((l.Domains & d) == 0) return false;
            int off;
            if (w.Off.TryGetValue(l.Index, out off) && (off & d) != 0) return false;
            return DocAllowed(unit, l, d, null);
        }

        static void SetOff(UnitW w, int idx, int d, bool off)
        {
            int v;
            w.Off.TryGetValue(idx, out v);
            v = off ? v | d : v & ~d;
            if (v == 0) w.Off.Remove(idx); else w.Off[idx] = v;
        }

        /// <summary>Standing Orders changed outside the tab (the window, or by inheritance): the switches it covers follow it again.</summary>
        public static void SyncDoctrine(object unit, UnitW w)
        {
            for (int i = 0; i < Items.Length; i++)
            {
                bool v = ItemValue(unit, i);
                if (w.SnapInit && v != w.Snap[i] && w.Off.Count > 0)
                {
                    var ls = CachedLaunchers(unit, w);
                    foreach (var l in ls) foreach (var d in Domains) if (Covers(i, l, d)) SetOff(w, l.Index, d, false);
                }
                w.Snap[i] = v;
            }
            w.SnapInit = true;
        }

        static void SetItem(object unit, int item, bool value)
        {
            var it = DoctrineItem(unit, item);
            if (it == null) return;
            R.SetP(it, "IsSet", true);
            R.SetP(it, "Value", value);
            try { R.Call(it, "Refresh"); } catch { }
        }

        /// <summary>The player turns automatic use on (true) or off for these launcher / domain pairs (one chip, or a group's).</summary>
        public static void Switch(object unit, List<KeyValuePair<Launcher, int>> pairs, bool on)
        {
            var w = W(unit);
            var ls = CachedLaunchers(unit, w);
            SyncDoctrine(unit, w);
            if (on)
            {
                var before = new Dictionary<long, bool>();
                foreach (var l in ls) foreach (var d in Domains) if ((l.Domains & d) != 0) before[(long)l.Index * 64 + d] = Effective(unit, w, l, d);
                var blockers = new List<int>();
                foreach (var p in pairs) DocAllowed(unit, p.Key, p.Value, blockers);
                foreach (var b in blockers) if (!ItemValue(unit, b)) SetItem(unit, b, true);
                var mine = new HashSet<long>();
                foreach (var p in pairs) { mine.Add((long)p.Key.Index * 64 + p.Value); SetOff(w, p.Key.Index, p.Value, false); }
                // what the changed orders turned on besides these pairs stays off
                foreach (var l in ls)
                    foreach (var d in Domains)
                    {
                        long k = (long)l.Index * 64 + d;
                        bool was;
                        if ((l.Domains & d) == 0 || mine.Contains(k) || !before.TryGetValue(k, out was) || was) continue;
                        if (Effective(unit, w, l, d)) SetOff(w, l.Index, d, true);
                    }
            }
            else
            {
                foreach (var p in pairs) SetOff(w, p.Key.Index, p.Value, true);
                // an order whose every pair is now off says No itself, and its pairs follow it again
                for (int i = 0; i < Items.Length; i++)
                {
                    if (!ItemApplies(unit, i) || !ItemValue(unit, i)) continue;
                    bool any = false, allOff = true;
                    foreach (var l in ls)
                        foreach (var d in Domains)
                        {
                            if (!Covers(i, l, d)) continue;
                            any = true;
                            if (Effective(unit, w, l, d)) { allOff = false; break; }
                        }
                    if (!any || !allOff) continue;
                    SetItem(unit, i, false);
                    foreach (var l in ls) foreach (var d in Domains) if (Covers(i, l, d)) SetOff(w, l.Index, d, false);
                }
            }
            for (int i = 0; i < Items.Length; i++) w.Snap[i] = ItemValue(unit, i);
            w.SnapInit = true;
        }

        /// <summary>Tooltip text of a chip.</summary>
        public static string ChipTip(object unit, Launcher l, int d)
        {
            int di = Array.IndexOf(Domains, d);
            var st = State(unit, l, d);
            if (st == Chip.On) return DomainNames[di] + ": automatic against " + DomainLong[di] + ". Click: off for this launcher.";
            if (st == Chip.Off) return DomainNames[di] + ": off for this launcher. Click: automatic again.";
            var b = new List<int>();
            DocAllowed(unit, l, d, b);
            var sb = new StringBuilder(DomainNames[di] + ": off by Standing Orders - ");
            for (int i = 0; i < b.Count; i++) sb.Append(i > 0 ? ", " : "").Append(ItemLabels[b[i]]).Append(": No");
            sb.Append(". Click: allow for this launcher (the unit's order becomes Yes, the other launchers it covers turn off).");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ the mission save

        const string SaveKey = "AlternativeWeaponsControl_WeaponAuto";

        static void UnitSavePostfix(object __instance, object ini, string sectionName)
        {
            try
            {
                UnitW w;
                if (ini == null || !UnitsW.TryGetValue(__instance, out w) || w.Off.Count == 0) return;
                var sb = new StringBuilder();
                foreach (var kv in w.Off) sb.Append(sb.Length > 0 ? "," : "").Append(kv.Key).Append(':').Append(kv.Value);
                R.Call(ini, "writeValue", sectionName, SaveKey, sb.ToString());
            }
            catch (Exception e) { Trace("weapons save: " + e.Message); }
        }

        static void UnitLoadPostfix(object __instance, object ini, string sectionName)
        {
            try
            {
                if (ini == null) return;
                var s = R.Call(ini, "readValue", sectionName, SaveKey, "", false) as string;
                if (string.IsNullOrEmpty(s)) return;
                var w = W(__instance);
                w.Off.Clear();
                foreach (var part in s.Split(','))
                {
                    var kv = part.Split(':');
                    int i, m;
                    if (kv.Length == 2 && int.TryParse(kv[0], out i) && int.TryParse(kv[1], out m) && m != 0) w.Off[i] = m & DAll;
                }
                w.SnapInit = false;
                if ((object)Active != null) Active.Verbose("weapons: " + w.Off.Count + " launcher switch(es) loaded for " + R.S(R.Call(__instance, "getUIDAndName")));
            }
            catch (Exception e) { Trace("weapons load: " + e.Message); }
        }
    }
}
