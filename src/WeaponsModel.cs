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
        // type -> name -> member: no key string is built per call (these run every frame)
        static readonly Dictionary<Type, Dictionary<string, FieldInfo>> Fields = new Dictionary<Type, Dictionary<string, FieldInfo>>();
        static readonly Dictionary<Type, Dictionary<string, PropertyInfo>> Props = new Dictionary<Type, Dictionary<string, PropertyInfo>>();
        static readonly Dictionary<Type, Dictionary<string, MethodInfo[]>> Methods = new Dictionary<Type, Dictionary<string, MethodInfo[]>>();
        static Dictionary<string, T> Of<T>(Dictionary<Type, Dictionary<string, T>> d, Type t)
        {
            Dictionary<string, T> m;
            if (!d.TryGetValue(t, out m)) d[t] = m = new Dictionary<string, T>();
            return m;
        }

        static FieldInfo Fi(Type t, string n)
        {
            var m = Of(Fields, t);
            FieldInfo f;
            if (m.TryGetValue(n, out f)) return f;
            for (var ty = t; ty != null && f == null; ty = ty.BaseType) f = ty.GetField(n, All);
            m[n] = f;
            return f;
        }

        static PropertyInfo Pi(Type t, string n)
        {
            var m = Of(Props, t);
            PropertyInfo p;
            if (m.TryGetValue(n, out p)) return p;
            for (var ty = t; ty != null && p == null; ty = ty.BaseType) p = ty.GetProperty(n, All);
            m[n] = p;
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
            var mm = Of(Methods, t);
            MethodInfo[] ms;
            if (!mm.TryGetValue(n, out ms))
            {
                var l = new List<MethodInfo>();
                for (var ty = t; ty != null; ty = ty.BaseType)
                    foreach (var m in ty.GetMethods(All)) if (m.Name == n && !m.ContainsGenericParameters) l.Add(m);
                ms = l.ToArray();
                mm[n] = ms;
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
    /// (game 0.9.0: TacticalDoctrine.AutoEngageRuleSet, the AutoEngageRules of weapon class x target category x weapon status);
    /// kept in the mission save (the unit's section); used by the AI hooks (WeaponsAi.cs).
    /// </summary>
    public partial class AwcPlugin
    {
        public const int DAir = 1, DMsl = 2, DSur = 4, DSub = 8, DLnd = 16, DAll = 31;
        public static readonly int[] Domains = { DAir, DMsl, DSur, DSub, DLnd };
        public static readonly string[] DomainNames = { "AIR", "MSL", "SUR", "SUB", "LND" };
        public static readonly string[] DomainLong = { "aircraft and helicopters", "missiles, torpedoes and other weapons", "surface ships", "submarines", "land targets" };

        // Standing Orders: the game's auto-engage rules (AutoEngageRules), read and extended by reflection
        static Type _tRules, _tAeCtx, _tAeDomain, _tAeClass, _tAeCat, _tAeRule, _tAePolicy;
        static MethodInfo _miPermits, _miCanEngage, _miClassifyAmmo, _miMountClass, _miDomainOf, _miMayEngage;
        const int CatLargeAir = 0, CatMediumAir = 1, CatSmallAir = 2, CatHeli = 3, CatAsm = 4, CatWeapon = 5, CatShip = 6, CatSurfSub = 7, CatSub = 8, CatLand = 9;
        static readonly int[][] DomainCats =
        {
            new[] { CatLargeAir, CatMediumAir, CatSmallAir, CatHeli },   // DAir
            new[] { CatAsm, CatWeapon },                                  // DMsl
            new[] { CatShip, CatSurfSub },                                // DSur
            new[] { CatSub },                                             // DSub
            new[] { CatLand },                                            // DLnd
        };

        static Type _tObjectBase, _tVessel, _tSubmarine, _tLandUnit, _tTorpedo, _tWeaponBase, _tCiws, _tWeaponStatus, _tLauncher, _tAircraft, _tHelicopter;

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
            _tLauncher = asm.GetType("SeaPower.WeaponSystemLauncher");
            _tAircraft = asm.GetType("SeaPower.Aircraft");
            _tHelicopter = asm.GetType("SeaPower.Helicopter");
            _tWeaponStatus = _tObjectBase == null ? null : _tObjectBase.GetNestedType("WeaponStatus");
            const BindingFlags st = BindingFlags.Static | BindingFlags.Public;
            _tRules = asm.GetType("SeaPower.AutoEngageRules");
            _tAeCtx = asm.GetType("SeaPower.AutoEngageConditionContext");
            _tAeDomain = asm.GetType("SeaPower.AutoEngageShooterDomain");
            _tAeClass = asm.GetType("SeaPower.AutoEngageWeaponClass");
            _tAeCat = asm.GetType("SeaPower.AutoEngageTargetCategory");
            _tAeRule = asm.GetType("SeaPower.AutoEngageRule");
            _tAePolicy = asm.GetType("SeaPower.AutoEngagePolicy");
            if (_tRules != null)
            {
                _miPermits = _tRules.GetMethod("Permits", st);
                _miCanEngage = _tRules.GetMethod("CanEngage", st);
                _miClassifyAmmo = _tRules.GetMethod("ClassifyAmmo", st);
                _miMountClass = _tRules.GetMethod("MountClass", st);
                _miDomainOf = _tRules.GetMethod("DomainOf", st);
                _miMayEngage = _tRules.GetMethod("UnitMayAutoEngage", st);
            }
            if (_miPermits == null || _miCanEngage == null || _miClassifyAmmo == null || _miMountClass == null || _miDomainOf == null || _tAeRule == null || _tAeCtx == null)
                Warn("The game's auto-engage rules (AutoEngageRules) were not found (the game is older than 0.9.0?): Standing Orders are not merged into the Weapons tab.");
        }

        /// <summary>The domain of a target object (one of DAir ... DLnd), 0 when none.</summary>
        static int TargetDomain(object t)
        {
            if (t == null) return 0;
            try
            {
                if (R.Is(t, _tWeaponBase)) return DMsl;   // missiles and torpedoes
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
            public int[] AmmoDom;        // per Ammo entry
            public int[] AmmoCls;        // per Ammo entry: the auto-engage weapon class (bit mask, 0 = none)
        }

        public static readonly string[] SectionNames = { "SAM", "ANTI-SHIP", "LAND ATTACK", "ASW", "GUNS", "CIWS", "OTHER", "STORES" };

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
            // TargetType=ASuW with CanNotAttackTypes=Vessel (land attack Tomahawk, RGM-109C): it cannot attack ships
            if ((d & DLnd) != 0 && (d & DSur) != 0)
            {
                var no = R.F(ap, "_canNotAttackTypes") as IEnumerable;
                if (no != null) foreach (var o in no) if (o != null && o.ToString() == "Vessel") { d &= ~DSur; break; }
            }
            if (R.B(R.F(ap, "_antiTorpedo"))) d |= DMsl;   // torpedoes as targets: MSL (weapons)
            return d;
        }

        /// <summary>The weapon class of the auto-engage rules for an ammunition on this mount (guns and CIWS by the mount, the rest by the ammunition), as a bit.</summary>
        static int AmmoClassBits(object unit, object ws, object ap)
        {
            try
            {
                if (_miClassifyAmmo == null) return 0;
                object c;
                if (R.S(R.F(ap, "_type")) == "Projectile") c = _miMountClass.Invoke(null, new[] { ws });
                else
                {
                    c = _miClassifyAmmo.Invoke(null, new[] { ap, unit });   // Nullable<WeaponClass>: boxed value or null
                }
                return c == null ? 0 : 1 << Convert.ToInt32(c);
            }
            catch { return 0; }
        }

        /// <summary>The unit's launchers (vessels and submarines; not chaff, noisemakers, hardpoints).</summary>
        public static List<Launcher> Launchers(object unit)
        {
            var res = new List<Launcher>();
            var obp = R.F(unit, "_obp");
            var list = R.F(obp, "_weaponSystems") as IList;
            if (list == null) return res;
            bool air = R.Is(unit, _tAircraft) || R.Is(unit, _tHelicopter);
            for (int i = 0; i < list.Count; i++)
            {
                var ws = list[i];
                if (ws == null) continue;
                var wt = R.S(R.F(ws, "_weaponType"));
                if (wt == null || wt == "UNKNOWN" || wt == "Chaff" || wt == "Noisemaker" || (wt == "Hardpoint" && !air)) continue;
                var l = new Launcher { Ws = ws, Index = i, Name = R.S(R.Rv(R.P(ws, "DisplayName"))) ?? R.S(R.F(ws, "_systemName")) ?? "?" };
                var names = R.F(ws, "_associatedAmmunitionNames") as IList;
                var doms = new List<int>();
                var cls = new List<int>();
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
                        int d = AmmoDomains(ap);
                        if (wt == "Hardpoint" && d == 0) continue;   // drop tanks, sonobuoys, ...
                        l.Ammo.Add(id);
                        doms.Add(d);
                        l.Domains |= d;
                        cls.Add(AmmoClassBits(unit, ws, ap));
                        if (first == null) first = ap;
                    }
                l.AmmoDom = doms.ToArray();
                l.AmmoCls = cls.ToArray();
                if (l.Domains == 0) continue;
                if (wt == "Hardpoint") l.Section = 7;   // all the stores of an aircraft: one group, a Fire per ammunition
                else if (wt == "CIWS") l.Section = 5;
                else if (wt == "Gun") l.Section = 4;
                else
                {
                    var type = R.S(R.F(first, "_type"));
                    var tt = R.S(R.F(first, "_targetType"));
                    // ASW: anti-submarine weapons only - a torpedo that cannot attack submarines is anti-ship
                    if (tt == "ASW" || type == "RBU" || type == "ASROC" || (type == "Torpedo" && (l.Domains & DSub) != 0)) l.Section = 3;
                    else if (type == "Torpedo" && (l.Domains & DSur) != 0) l.Section = 1;
                    else if (tt == "AAW") l.Section = 0;
                    else if (tt == "ASuW" && ((l.Domains & DSur) != 0 || (l.Domains & DLnd) == 0)) l.Section = 1;
                    else if ((l.Domains & DLnd) != 0) l.Section = 2;
                    else l.Section = 6;
                }
                l.Kind = SectionNames[l.Section];
                res.Add(l);
            }
            // where the mounts are: fore / aft of the middle, port / starboard (relative to the widest mount). Mounts that sit in one
            // point (the torpedo tubes of a submarine: all at the hull's origin) are told apart by their preferred firing arc
            try
            {
                var ut = ((Component)unit).transform;
                var pos = new Vector3[res.Count];
                float maxX = 0f;
                var wpos = new Vector3[res.Count];
                for (int i = 0; i < res.Count; i++)
                {
                    // the first container's own object (a submarine's tubes share one mount: the tube's place is the container's), else the mount
                    GameObject go = null;
                    try { var cs = R.F(res[i].Ws, "_containers") as System.Collections.IList; if (cs != null && cs.Count > 0) go = R.F(cs[0], "_gunObject") as GameObject; } catch { }
                    if (go == null) go = R.F(res[i].Ws, "_mountObject") as GameObject;
                    pos[i] = go == null ? new Vector3(float.NaN, 0f, 0f) : ut.InverseTransformPoint(go.transform.position);
                    wpos[i] = go == null ? Vector3.zero : go.transform.position;
                    if (float.IsNaN(pos[i].x)) continue;
                    maxX = Mathf.Max(maxX, Mathf.Abs(pos[i].x));
                }
                // mounts of one kind that stand within ~2 m of each other (0.03 world unit; 1 unit = 67 m) are in one point: a submarine's tubes
                var together = new bool[res.Count];
                for (int i = 0; i < res.Count; i++)
                {
                    int n = 0; Vector3 lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), hi = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                    for (int j = 0; j < res.Count; j++)
                        if (res[j].Name == res[i].Name && !float.IsNaN(pos[j].x)) { n++; lo = Vector3.Min(lo, wpos[j]); hi = Vector3.Max(hi, wpos[j]); }
                    together[i] = n > 1 && (hi - lo).magnitude < 0.03f;
                }
                // the label = the longitudinal part (fore / aft) + the lateral one (port / starboard)
                var lon = new string[res.Count]; var lat = new string[res.Count];
                for (int i = 0; i < res.Count; i++)
                {
                    if (!together[i] && !float.IsNaN(pos[i].x))
                    {
                        lon[i] = pos[i].z >= 0f ? Loc.T("loc.fore") : Loc.T("loc.aft");
                        if (maxX > 0f && Mathf.Abs(pos[i].x) > Mathf.Max(maxX * 0.25f, 0.02f)) lat[i] = pos[i].x < 0f ? Loc.T("loc.port") : Loc.T("loc.starboard");
                    }
                    else
                    {
                        float c;
                        if (ArcCentre(res[i].Ws, out c))
                        {
                            if (Mathf.Abs(c) >= 135f) lon[i] = Loc.T("loc.aft");
                            else if (Mathf.Abs(c) <= 45f) lon[i] = Loc.T("loc.fore");
                            else lat[i] = c < 0f ? Loc.T("loc.port") : Loc.T("loc.starboard");
                        }
                    }
                }
                // a part that is the same for all of a kind says nothing: "Fore port / Fore starboard" -> "Port / Starboard"
                for (int i = 0; i < res.Count; i++)
                {
                    bool lonVar = false, latVar = false;
                    for (int j = 0; j < res.Count; j++)
                    {
                        if (res[j].Name != res[i].Name) continue;
                        if (lon[j] != lon[i]) lonVar = true;
                        if (lat[j] != lat[i]) latVar = true;
                    }
                    string l1 = lon[i], l2 = lat[i];
                    if (latVar && !lonVar && l2 != null) l1 = null;            // only the side tells them apart
                    else if (lonVar && !latVar && l1 != null) l2 = null;       // only fore / aft tells them apart
                    string s = l1 == null ? l2 : l2 == null ? l1 : l1 + " " + l2;
                    if (s != null) res[i].Loc = s;
                }
            }
            catch { }
            return res;
        }

        /// <summary>The centre of the launcher's first preferred firing arc, degrees from the bow (-180..180; 0 = ahead, 180 = astern); false when it has none.</summary>
        static bool ArcCentre(object ws, out float centre)
        {
            centre = 0f;
            try
            {
                var arcs = R.F(R.F(ws, "_vwp"), "_preferredArcs") as System.Collections.IList;
                if (arcs == null) return false;
                foreach (var o in arcs)
                {
                    var v = (Vector2)o;
                    if (v == Vector2.zero) continue;
                    float c = v.x <= v.y ? (v.x + v.y) / 2f : (v.x + v.y + 360f) / 2f;   // an arc through the stern: 90..-90
                    while (c > 180f) c -= 360f;
                    while (c < -180f) c += 360f;
                    centre = c;
                    return true;
                }
            }
            catch { }
            return false;
        }

        // ------------------------------------------------------------------ per-unit switches

        public sealed class UnitW
        {
            public readonly Dictionary<int, int> Off = new Dictionary<int, int>();   // launcher index -> domains the player turned off
            public readonly Dictionary<long, bool> Snap = new Dictionary<long, bool>();   // launcher index * 64 + domain -> the rules allowed it when last seen
            public bool SnapInit;
            public volatile bool RulesDirty = true;                                      // the unit's rule set raised PropertyChanged (the game's editor, a parent change, AllowByRules)
            public object Hooked;                                                        // the rule set whose PropertyChanged is subscribed
            public int FreeAirSet = -1;                                             // On Free: engage approaching fast unidentified aircraft: -1 = the default of the side, 0 / 1 = the player's choice
            public List<Launcher> Cache;                                            // launchers (for the AI hooks)
            public readonly Dictionary<object, int> IndexOf = new Dictionary<object, int>();
        }

        static readonly ConditionalWeakTable<object, UnitW> UnitsW = new ConditionalWeakTable<object, UnitW>();

        /// <summary>On Free: engage approaching fast unidentified aircraft: the player's choice for the unit, else on for the enemy's AI ships, off for the others.</summary>
        public static bool FreeAirOn(object unit)
        {
            UnitW w;
            if (UnitsW.TryGetValue(unit, out w) && w.FreeAirSet >= 0) return w.FreeAirSet == 1;
            var tf = R.V(unit, "_taskforce");
            var side = R.P(tf, "Side");
            var a = Active;
            bool enemy = side != null && side.ToString() == "Enemy";
            return a == null ? enemy : enemy ? a._freeAirEnemy.Value : a._freeAirOwn.Value;
        }

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

        static bool RulesAvailable { get { return _miPermits != null && _tAeRule != null && _tAeCtx != null && _miDomainOf != null; } }

        static object RuleSet(object unit) { return R.P(R.P(unit, "TacticalDoctrine"), "AutoEngageRuleSet"); }

        /// <summary>Does the unit's rule set let the launcher engage the domain by itself (at Free: only a rule saying Never, or no rule, forbids)? Conditions of a rule count as allowed.</summary>
        static bool DocAllowed(object unit, Launcher l, int d)
        {
            if (!RulesAvailable || !R.B(R.P(unit, "IsPlayerObject"))) return true;
            try
            {
                var rules = R.P(RuleSet(unit), "ResolvedRules");
                if (rules == null) return true;
                var dom = _miDomainOf.Invoke(null, new[] { unit });
                int di = Array.IndexOf(Domains, d);
                var free = Enum.Parse(_tWeaponStatus, "Free");   // (WeaponStatus: Tight = 0, Free = 1, Hold = 2)
                bool engageable = false;
                for (int i = 0; i < l.Ammo.Count; i++)
                {
                    if ((l.AmmoDom[i] & d) == 0 || l.AmmoCls[i] == 0) continue;
                    foreach (var c in DomainCats[di])
                        for (int k = 0; k < 12; k++)
                        {
                            if ((l.AmmoCls[i] & (1 << k)) == 0) continue;
                            var cls = Enum.ToObject(_tAeClass, k); var cat = Enum.ToObject(_tAeCat, c);
                            if (!(bool)_miCanEngage.Invoke(null, new[] { cls, cat })) continue;
                            engageable = true;
                            var a = new object[] { rules, dom, cls, cat, free, Activator.CreateInstance(_tAeCtx), true, l.Ammo[i], null };
                            if ((bool)_miPermits.Invoke(null, a)) return true;
                        }
                }
                return !engageable;   // nothing the rules speak about: not blocked
            }
            catch (Exception e) { TraceLimited("docallowed", 60f, "auto-engage rules: " + e.Message); return true; }
        }

        public enum Chip { None, On, Off, Order, Mixed }

        public static Chip State(object unit, Launcher l, int d)
        {
            if ((l.Domains & d) == 0) return Chip.None;
            if (!DocAllowed(unit, l, d)) return Chip.Order;
            int off;
            return W(unit).Off.TryGetValue(l.Index, out off) && (off & d) != 0 ? Chip.Off : Chip.On;
        }

        static bool Effective(object unit, UnitW w, Launcher l, int d)
        {
            if ((l.Domains & d) == 0) return false;
            int off;
            if (w.Off.TryGetValue(l.Index, out off) && (off & d) != 0) return false;
            return DocAllowed(unit, l, d);
        }

        static void SetOff(UnitW w, int idx, int d, bool off)
        {
            int v;
            w.Off.TryGetValue(idx, out v);
            v = off ? v | d : v & ~d;
            if (v == 0) w.Off.Remove(idx); else w.Off[idx] = v;
        }

        /// <summary>Standing Orders changed outside the tab (the game's rules editor, a parent doctrine, a loaded save): the switches whose permission
        /// changed follow it again. The rule set tells itself when it changes (PropertyChanged: the editor edits the rules in place and raises
        /// ResolvedRules), so the AI hooks only look at a flag; full = true (the tab's refresh) compares the permissions anyway.</summary>
        public static void SyncDoctrine(object unit, UnitW w, bool full = true)
        {
            if (!RulesAvailable) return;
            var set = RuleSet(unit);
            if (set != null && !ReferenceEquals(set, w.Hooked))
            {
                var npc = set as System.ComponentModel.INotifyPropertyChanged;
                if (npc != null) npc.PropertyChanged += (o, e) => { if (e.PropertyName == "ResolvedRules" || string.IsNullOrEmpty(e.PropertyName)) w.RulesDirty = true; };
                w.Hooked = set; w.RulesDirty = true;
            }
            if (!full && w.SnapInit && !w.RulesDirty) return;
            w.RulesDirty = false;
            var ls = CachedLaunchers(unit, w);
            foreach (var l in ls)
                foreach (var d in Domains)
                {
                    if ((l.Domains & d) == 0) continue;
                    long k = (long)l.Index * 64 + d;
                    bool v = DocAllowed(unit, l, d), was;
                    if (w.SnapInit && w.Snap.TryGetValue(k, out was) && was != v && w.Off.Count > 0) SetOff(w, l.Index, d, false);
                    w.Snap[k] = v;
                }
            w.SnapInit = true;
        }

        /// <summary>The player allows automatic use for the launcher / domain pairs the rules forbid: one rule is put first in the unit's own rule set
        /// (its domain, the weapon classes of the launchers, the target categories of the domains; Weapons Tight = Free and Tight engage, Hold does not).</summary>
        static void AllowByRules(object unit, List<KeyValuePair<Launcher, int>> pairs)
        {
            if (!RulesAvailable || _tAePolicy == null) return;
            var set = RuleSet(unit);
            if (set == null) return;
            var rules = R.P(set, "ResolvedRules");
            var groups = new Dictionary<int, int>();       // weapon class mask -> category mask
            foreach (var p in pairs)
            {
                if (DocAllowed(unit, p.Key, p.Value)) continue;
                int cm = 0;
                for (int i = 0; i < p.Key.Ammo.Count; i++) if ((p.Key.AmmoDom[i] & p.Value) != 0) cm |= p.Key.AmmoCls[i];
                if (cm == 0) continue;
                int cats = 0; foreach (var c in DomainCats[Array.IndexOf(Domains, p.Value)]) cats |= 1 << c;
                int old; groups.TryGetValue(cm, out old); groups[cm] = old | cats;
            }
            if (groups.Count == 0) return;
            if (!R.B(R.P(set, "IsSet"))) R.SetP(set, "IsSet", true);   // an own copy of the inherited rules
            var own = R.P(set, "Rules") as IList;
            if (own == null) return;
            var dom = _miDomainOf.Invoke(null, new[] { unit });
            foreach (var kv in groups)
            {
                var rule = Activator.CreateInstance(_tAeRule);
                R.SetF(rule, "DomainMask", 1 << Convert.ToInt32(dom));
                R.SetF(rule, "WeaponMask", kv.Key);
                R.SetF(rule, "TargetMask", kv.Value);
                R.SetF(rule, "Policy", Enum.ToObject(_tAePolicy, 2));   // WeaponsTight
                own.Insert(0, rule);
            }
            try { R.Call(set, "NotifyRulesChanged"); } catch { }
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
                AllowByRules(unit, pairs);
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
                // off: only the player's switch (grey) - the Standing Orders stay as they are
                foreach (var p in pairs) SetOff(w, p.Key.Index, p.Value, true);
            }
            w.SnapInit = false; SyncDoctrine(unit, w);
        }

        /// <summary>Tooltip text of a chip.</summary>
        public static string ChipTip(object unit, Launcher l, int d, string whom = null)
        {
            int di = Array.IndexOf(Domains, d);
            var st = State(unit, l, d);
            if (whom == null) whom = Loc.T("chip.thisLauncher");
            if (st == Chip.On) return Loc.F("chip.on", Loc.T("dom." + di), Loc.T("domLong." + di), whom);
            if (st == Chip.Off) return Loc.F("chip.off", Loc.T("dom." + di), whom);
            var sb = new StringBuilder(Loc.F("chip.order", Loc.T("dom." + di)));
            sb.Append(Loc.T("chip.orderNo"));
            sb.Append(Loc.F("chip.orderEnd", whom));
            return sb.ToString();
        }

        // ------------------------------------------------------------------ the mission save

        const string SaveKey = "AlternativeWeaponsControl_WeaponAuto", FreeAirKey = "AlternativeWeaponsControl_FreeUnknownAir";

        static void UnitSavePostfix(object __instance, object ini, string sectionName)
        {
            if (ini == null) return;
            try
            {
                UnitW w;
                if (UnitsW.TryGetValue(__instance, out w) && w.FreeAirSet >= 0) R.Call(ini, "writeValue", sectionName, FreeAirKey, w.FreeAirSet.ToString());
                if (UnitsW.TryGetValue(__instance, out w) && w.Off.Count > 0)
                {
                    var sb = new StringBuilder();
                    foreach (var kv in w.Off) sb.Append(sb.Length > 0 ? "," : "").Append(kv.Key).Append(':').Append(kv.Value);
                    R.Call(ini, "writeValue", sectionName, SaveKey, sb.ToString());
                }
            }
            catch (Exception e) { Trace("weapons save: " + e.Message); }
            SavePins(__instance, ini, sectionName);
        }

        static void UnitLoadPostfix(object __instance, object ini, string sectionName)
        {
            try
            {
                if (ini == null) return;
                LoadPins(__instance, ini, sectionName);
                { var fa = R.Call(ini, "readValue", sectionName, FreeAirKey, "", false) as string; if (fa == "1" || fa == "0") W(__instance).FreeAirSet = fa == "1" ? 1 : 0; }
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
