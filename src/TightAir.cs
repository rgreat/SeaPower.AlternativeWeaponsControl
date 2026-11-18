using System;
using System.Collections;
using System.Collections.Generic;

namespace AlternativeWeaponsControl
{
    /// <summary>
    /// "On Tight: engage hostile aircraft" of the beta game. The release game has a doctrine item for it (TightAirItem); the beta has the auto-engage rules, where the same thing is a rule:
    /// the unit's own copy of the Standing Orders (the game's per-unit override: IsSet) gets a WeaponsTight rule on top for the unit's weapon classes against the four air categories.
    /// The box shows what the game's rules say (does every air-capable launcher engage at Weapons Tight), not whether the rule is there. The rule overrides the rules below it
    /// (first match wins), so the box is amber while the rule is in place.
    /// </summary>
    public partial class AwcPlugin
    {
        const int PolicyFree = 1, PolicyTight = 2;

        /// <summary>The box is shown: the release game - the doctrine item exists; the beta - the player's ship that has a weapon against aircraft.</summary>
        public static bool TightAirShown(object unit)
        {
            if (_legacy) return TightAirItem(unit) != null;
            if (!RulesAvailable || _tAePolicy == null || !R.Is(unit, _tVessel) || !R.B(R.P(unit, "IsPlayerObject"))) return false;
            foreach (var l in CachedLaunchers(unit, W(unit))) if ((l.Domains & DAir) != 0) return true;
            return false;
        }

        static int AirCatsMask() { int m = 0; foreach (var c in DomainCats[0]) m |= 1 << c; return m; }

        /// <summary>The rule of the box: WeaponsTight, the ship's domain, exactly the air categories, no conditions or filters.</summary>
        static bool IsTightMark(object rule, int dom, int air)
        {
            try
            {
                return Convert.ToInt32(R.F(rule, "Policy")) == PolicyTight && Convert.ToInt32(R.F(rule, "DomainMask")) == 1 << dom
                    && Convert.ToInt32(R.F(rule, "TargetMask")) == air && Convert.ToInt32(R.F(rule, "WeaponMask")) != 0
                    && !R.B(R.P(rule, "HasConditions")) && !R.B(R.P(rule, "NeedsRoundDetail")) && !R.B(R.P(rule, "HasSaturation")) && !R.B(R.P(rule, "HasShotRange"));
            }
            catch { return false; }
        }

        /// <summary>The rules (not the release doctrine) engage at Weapons Tight: 2 - every launcher that can engage aircraft, 1 - some, 0 - none, -1 - there is no such launcher.</summary>
        public static int TightAirState(object unit)
        {
            int n = 0, yes = 0;
            foreach (var l in CachedLaunchers(unit, W(unit)))
            {
                if ((l.Domains & DAir) == 0) continue;
                n++;
                if (DocAllowed(unit, l, DAir, "Tight")) yes++;
            }
            return n == 0 ? -1 : yes == 0 ? 0 : yes == n ? 2 : 1;
        }

        /// <summary>Is the box's own rule in the unit's Standing Orders (it overrides the rules below it)?</summary>
        public static bool TightAirOverrides(object unit)
        {
            if (!RulesAvailable) return false;
            try
            {
                var set = RuleSet(unit);
                if (set == null || !R.B(R.P(set, "IsSet"))) return false;
                var own = R.P(set, "Rules") as IList;
                if (own == null) return false;
                int dom = Convert.ToInt32(_miDomainOf.Invoke(null, new[] { unit })), air = AirCatsMask();
                foreach (var r in own) if (IsTightMark(r, dom, air)) return true;
            }
            catch { }
            return false;
        }

        /// <summary>Click on the box (beta). Not fully on: the rule is put on top of the unit's own rules for the weapon classes that do not engage aircraft on Tight.
        /// Fully on: the rule is taken out; if the rules below still engage on Tight (the player's own), a rule making it Free only is put on top for those cells.</summary>
        public static void ToggleTightRules(object unit)
        {
            try
            {
                if (!TightAirShown(unit)) return;
                var set = RuleSet(unit);
                if (set == null) return;
                var w = W(unit);
                SyncDoctrine(unit, w);
                bool on = TightAirState(unit) != 2;
                if (!R.B(R.P(set, "IsSet"))) R.SetP(set, "IsSet", true);   // the unit's own copy of the inherited rules
                var own = R.P(set, "Rules") as IList;
                if (own == null) return;
                int dom = Convert.ToInt32(_miDomainOf.Invoke(null, new[] { unit })), air = AirCatsMask();
                int removed = 0;
                for (int i = own.Count - 1; i >= 0; i--) if (IsTightMark(own[i], dom, air)) { own.RemoveAt(i); removed++; }
                int cm = 0;   // weapon classes of the launchers against aircraft, and the ammunition of each (rules may name an ammunition: DocAllowed asks with the names)
                var ammo = new Dictionary<int, List<string>>();
                foreach (var l in CachedLaunchers(unit, w))
                    for (int i = 0; i < l.Ammo.Count; i++)
                    {
                        if ((l.AmmoDom[i] & DAir) == 0) continue;
                        cm |= l.AmmoCls[i];
                        for (int k = 0; k < 12; k++)
                        {
                            if ((l.AmmoCls[i] & (1 << k)) == 0) continue;
                            List<string> la; if (!ammo.TryGetValue(k, out la)) ammo[k] = la = new List<string>();
                            if (!la.Contains(l.Ammo[i])) la.Add(l.Ammo[i]);
                        }
                    }
                var domObj = Enum.ToObject(_tAeDomain, dom);
                var rules = R.P(set, "ResolvedRules");
                var tight = Enum.Parse(_tWeaponStatus, "Tight");
                var groups = new Dictionary<int, int>();   // target categories -> weapon classes
                for (int k = 0; k < 12; k++)
                {
                    if ((cm & (1 << k)) == 0) continue;
                    int cats = 0;
                    foreach (var c in DomainCats[0])
                    {
                        var cls = Enum.ToObject(_tAeClass, k); var cat = Enum.ToObject(_tAeCat, c);
                        if (!(bool)_miCanEngage.Invoke(null, new[] { cls, cat })) continue;
                        bool t = false;   // does any ammunition of the class engage this category on Tight
                        foreach (var an in ammo[k])
                            if ((bool)_miPermits.Invoke(null, new object[] { rules, domObj, cls, cat, tight, Activator.CreateInstance(_tAeCtx), true, an, null })) { t = true; break; }
                        if (on ? !t : t) cats |= 1 << c;   // on: where Tight does not engage; off: where it does (a Free rule on top: Tight and Hold keep quiet, also over an Always)
                    }
                    if (cats == 0) continue;
                    if (on) cats = air;   // the rule is recognisable by its exact target mask
                    int old; groups.TryGetValue(cats, out old); groups[cats] = old | (1 << k);
                }
                foreach (var kv in groups)
                {
                    var rule = Activator.CreateInstance(_tAeRule);
                    R.SetF(rule, "DomainMask", 1 << dom);
                    R.SetF(rule, "WeaponMask", kv.Value);
                    R.SetF(rule, "TargetMask", kv.Key);
                    R.SetF(rule, "Policy", Enum.ToObject(_tAePolicy, on ? PolicyTight : PolicyFree));   // Free: Tight and Hold keep quiet
                    own.Insert(0, rule);
                }
                try { R.Call(set, "NotifyRulesChanged"); } catch { }
                w.SnapInit = false; SyncDoctrine(unit, w);
                Trace("on tight: " + (on ? "engage hostile aircraft" : "no aircraft on Tight") + " (" + groups.Count + " rule(s) put, " + removed + " taken out)");
            }
            catch (Exception e) { Trace("on tight rules: " + e.Message); }
        }
    }
}
