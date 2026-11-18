using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace AlternativeWeaponsControl
{
    /// <summary>
    /// The beta game: the chip's tooltip names the Standing Orders rules that apply to the launcher against the chip's domain (the first rule
    /// that matches each weapon class x target category pair; a rule with conditions lets the search go on, as in AutoEngageRules.PermitsFrom).
    /// The texts are the game's own (AutoEngageLabels, its language files), the rule numbers are the ones of the Standing Orders list.
    /// The note is cached per launcher and domain; the cache is dropped when the rules change (a signature of the rule list, read at most twice a second).
    /// </summary>
    public partial class AwcPlugin
    {
        const int RuleLines = 4;

        sealed class RuleNote { public int Sig; public string Text; }
        sealed class UnitRuleSig { public int Frame = -1000, Sig; public Dictionary<int, RuleNote> Notes = new Dictionary<int, RuleNote>(); }
        static readonly ConditionalWeakTable<object, UnitRuleSig> RuleSigs = new ConditionalWeakTable<object, UnitRuleSig>();

        static MethodInfo _miMatches;

        /// <summary>The lines about the rules of the launcher's chip (starts with a new line); empty when there are no rules in the game / for this unit.</summary>
        static string RulesNote(object unit, Launcher l, int d)
        {
            if (_legacy || !RulesAvailable || _tAeRule == null || !R.B(R.P(unit, "IsPlayerObject")))
            {
                TraceLimited("rn-off", 60f, "rule note off: legacy=" + _legacy + " rulesAvailable=" + RulesAvailable + " ruleType=" + (_tAeRule != null) + " player=" + R.B(R.P(unit, "IsPlayerObject")));
                return "";
            }
            try
            {
                var rules = R.P(RuleSet(unit), "ResolvedRules") as IList;
                if (rules == null) { TraceLimited("rn-null", 60f, "rule note: the unit has no rule list (rule set " + (RuleSet(unit) != null) + ")"); return ""; }
                var us = RuleSigs.GetOrCreateValue(unit);
                int frame = UnityEngine.Time.frameCount;
                if (frame - us.Frame >= 30 || frame < us.Frame) { us.Frame = frame; us.Sig = unchecked(RulesSignature(rules) * 31 + UncoveredIndex()); }
                int key = l.Index * 8 + Array.IndexOf(Domains, d);
                RuleNote n;
                if (us.Notes.TryGetValue(key, out n) && n.Sig == us.Sig) return n.Text;
                n = new RuleNote { Sig = us.Sig, Text = BuildRulesNote(unit, rules, l, d) };
                us.Notes[key] = n;
                return n.Text;
            }
            catch (Exception e) { Trace("rule note failed: " + e); return ""; }
        }

        static int RulesSignature(IList rules)
        {
            unchecked
            {
                int h = RuntimeHelpers.GetHashCode(rules) * 31 + rules.Count;
                foreach (var r in rules)
                {
                    if (r == null) { h *= 31; continue; }
                    h = h * 31 + Convert.ToInt32(R.F(r, "DomainMask")); h = h * 31 + Convert.ToInt32(R.F(r, "WeaponMask")); h = h * 31 + Convert.ToInt32(R.F(r, "TargetMask"));
                    h = h * 31 + Convert.ToInt32(R.F(r, "Policy")); h = h * 31 + R.Fl(R.F(r, "MaxRangeNm")).GetHashCode();
                    h = h * 31 + (R.B(R.F(r, "OnlyIfTargetingUs")) ? 1 : 0) + (R.B(R.F(r, "OnlyIfClosing")) ? 2 : 0) + (R.B(R.F(r, "OnlyInWeaponRange")) ? 4 : 0);
                    h = h * 31 + AmmoSetHash(R.F(r, "IncludeAmmo")); h = h * 31 + AmmoSetHash(R.F(r, "ExcludeAmmo"));
                }
                return h;
            }
        }

        static int AmmoSetHash(object set)
        {
            int h = 0;
            var e = set as IEnumerable;
            if (e != null) unchecked { foreach (var s in e) h += (s == null ? 0 : s.GetHashCode()) + 1; }
            return h;
        }

        /// <summary>Adds the indices of the rules that decide the launcher against the domain (the first match of every ammunition x class x category; a rule with conditions lets the search go on); true when some pair matches no rule.</summary>
        static bool CollectRules(object unit, IList rules, Launcher l, int d, SortedSet<int> used)
        {
            if (_miMatches == null) _miMatches = _tAeRule.GetMethod("Matches", BindingFlags.Instance | BindingFlags.Public);
            if (_miMatches == null) return false;
            var dom = _miDomainOf.Invoke(null, new[] { unit });
            int di = Array.IndexOf(Domains, d);
            bool uncovered = false;
            for (int i = 0; i < l.Ammo.Count; i++)
            {
                if ((l.AmmoDom[i] & d) == 0 || l.AmmoCls[i] == 0) continue;
                foreach (var c in DomainCats[di])
                    for (int k = 0; k < 12; k++)
                    {
                        if ((l.AmmoCls[i] & (1 << k)) == 0) continue;
                        var cls = Enum.ToObject(_tAeClass, k); var cat = Enum.ToObject(_tAeCat, c);
                        if (!(bool)_miCanEngage.Invoke(null, new[] { cls, cat })) continue;
                        bool hit = false, end = false;
                        for (int ri = 0; ri < rules.Count && !end; ri++)
                        {
                            var rule = rules[ri];
                            if (rule == null || !(bool)_miMatches.Invoke(rule, new object[] { dom, cls, cat, l.Ammo[i], null })) continue;
                            hit = true; used.Add(ri);
                            end = !R.B(R.P(rule, "HasConditions"));
                        }
                        if (!hit) uncovered = true;
                    }
            }
            return uncovered;
        }

        static string BuildRulesNote(object unit, IList rules, Launcher l, int d)
        {
            var used = new SortedSet<int>();
            bool uncovered = CollectRules(unit, rules, l, d, used);
            var text = FormatRules(rules, used, uncovered);
            Trace("rule note " + UName(unit) + " / " + l.Name + " / domain " + d + ": " + rules.Count + " rule(s) in the unit's list, deciding: [" + string.Join(",", new List<int>(used).ConvertAll(x => (x + 1).ToString()).ToArray()) + "], uncovered=" + uncovered + ", " + (text.Length == 0 ? "NO TEXT" : text.Replace("\n", " | ")));
            return text;
        }

        /// <summary>"\nStanding Orders rules applied:" + the numbered rules (+ the line about the weapons no rule covers); empty when there is nothing to say.</summary>
        static string FormatRules(IList rules, SortedSet<int> used, bool uncovered)
        {
            var sb = new StringBuilder();
            if (used.Count > 0)
            {
                sb.Append('\n').Append(Loc.T("chip.rules"));
                int n = 0;
                foreach (var ri in used)
                {
                    if (n++ >= RuleLines) { sb.Append('\n').Append(Loc.F("chip.rulesMore", used.Count - RuleLines)); break; }
                    sb.Append('\n').Append(RuleText(rules[ri], ri + 1));
                }
            }
            if (uncovered) sb.Append('\n').Append(UncoveredLine());
            return sb.ToString();
        }

        /// <summary>A note about the unit's rules, cached per unit and key (-1 the On Tight box, -2 Hold, -3 Free) and dropped when the rule list or the option changes.</summary>
        static string CachedRulesText(object unit, int key, Func<IList, string> build)
        {
            if (_legacy || !RulesAvailable || _tAeRule == null) return "";
            try
            {
                var rules = R.P(RuleSet(unit), "ResolvedRules") as IList;
                if (rules == null) return "";
                var us = RuleSigs.GetOrCreateValue(unit);
                int frame = UnityEngine.Time.frameCount;
                if (frame - us.Frame >= 30 || frame < us.Frame) { us.Frame = frame; us.Sig = unchecked(RulesSignature(rules) * 31 + UncoveredIndex()); }
                RuleNote n;
                if (us.Notes.TryGetValue(key, out n) && n.Sig == us.Sig) return n.Text;
                n = new RuleNote { Sig = us.Sig, Text = build(rules) };
                us.Notes[key] = n;
                return n.Text;
            }
            catch (Exception e) { Trace("rule note failed: " + e); return ""; }
        }

        /// <summary>The same for the "On Tight: engage hostile aircraft" mark: the rules that decide the unit's air launchers against aircraft (the mark's own rule and the ones it overrides).</summary>
        static string TightRulesNote(object unit)
        {
            return CachedRulesText(unit, -1, rules =>
            {
                var used = new SortedSet<int>(); bool unc = false;
                foreach (var l in CachedLaunchers(unit, W(unit))) if ((l.Domains & DAir) != 0 && CollectRules(unit, rules, l, DAir, used)) unc = true;
                return FormatRules(rules, used, unc);
            });
        }

        /// <summary>" (#3, #5)": the numbers of the rules that decide the launcher against the domain and have the given policy (Never: why it is off by the orders; Always: what forces it); empty when none.</summary>
        static string RuleRefs(object unit, Launcher l, int d, bool always)
        {
            int key = -1000 - (l.Index * 8 + Array.IndexOf(Domains, d)) * 2 - (always ? 1 : 0);
            return CachedRulesText(unit, key, rules =>
            {
                var all = new SortedSet<int>();
                CollectRules(unit, rules, l, d, all);
                var nums = new List<string>();
                foreach (var ri in all) if (R.S(R.F(rules[ri], "Policy")) == (always ? "Always" : "Never")) nums.Add("#" + (ri + 1));
                return nums.Count == 0 ? "" : " (" + string.Join(", ", nums.ToArray()) + ")";
            });
        }

        /// <summary>The Free / Hold button hints: the numbered rules behind the override - for Hold the rules saying Always that keep the fire going, for Free the rules saying Never
        /// (over the launchers and domains where the button is overridden, as StatusOverride found them).</summary>
        static string StatusRulesNote(object unit, bool hold)
        {
            return CachedRulesText(unit, hold ? -2 : -3, rules =>
            {
                var used = new SortedSet<int>();
                foreach (var l in CachedLaunchers(unit, W(unit)))
                    for (int i = 0; i < Domains.Length; i++)
                    {
                        int d = Domains[i];
                        if ((l.Domains & d) == 0 || !(hold ? DocForced(unit, l, d) : !DocAllowed(unit, l, d))) continue;
                        var all = new SortedSet<int>();
                        CollectRules(unit, rules, l, d, all);
                        foreach (var ri in all) if (R.S(R.F(rules[ri], "Policy")) == (hold ? "Always" : "Never")) used.Add(ri);
                    }
                return FormatRules(rules, used, false);
            });
        }

        /// <summary>"#3 Weapons Free: domains | weapons -> targets [conditions]" in the game's words.</summary>
        static readonly string[] PolicyKeys = { "WeaponsFree", "WeaponsTight", "Always" };

        /// <summary>"No rule covers ...: the mod's option decides: Weapons Free" (Fire/UncoveredWeapons).</summary>
        static string UncoveredLine() { return Loc.F("chip.rulesNone", GameText("AutoEngagePolicy", PolicyKeys[UncoveredIndex()])); }

        static string RuleText(object rule, int number)
        {
            var sb = new StringBuilder();
            sb.Append('#').Append(number).Append(' ').Append(GameText("AutoEngagePolicy", R.S(R.F(rule, "Policy")))).Append(": ");
            sb.Append(MaskText(R.F(rule, "DomainMask"), _tAeDomain, "Domain"));
            sb.Append(" | ");
            int wm = Convert.ToInt32(R.F(rule, "WeaponMask"));
            var inc = AmmoNames(R.F(rule, "IncludeAmmo")); var exc = AmmoNames(R.F(rule, "ExcludeAmmo"));
            var parts = new List<string>();
            if (wm != 0 || inc.Count == 0) parts.Add(MaskText(wm, _tAeClass, "WeaponClass"));
            foreach (var a in inc) parts.Add("+" + a);
            foreach (var a in exc) parts.Add("-" + a);
            sb.Append(string.Join(" ", parts.ToArray()));
            sb.Append(" -> ").Append(MaskText(R.F(rule, "TargetMask"), _tAeCat, "Target"));
            var cond = new List<string>();
            float mr = R.Fl(R.F(rule, "MaxRangeNm"));
            if (mr > 0f) cond.Add(LabelRange(mr));
            if (R.B(R.F(rule, "OnlyIfTargetingUs"))) cond.Add(GameText("TacticalDoctrine", "AutoEngageCondTargetingUs"));
            if (R.B(R.F(rule, "OnlyIfClosing"))) cond.Add(GameText("TacticalDoctrine", "AutoEngageCondClosing"));
            if (R.B(R.F(rule, "OnlyInWeaponRange"))) cond.Add(GameText("TacticalDoctrine", "AutoEngageCondInWeaponRange"));
            if (cond.Count > 0) sb.Append("  [").Append(string.Join(", ", cond.ToArray())).Append(']');
            return sb.ToString();
        }

        static List<string> AmmoNames(object set)
        {
            var l = new List<string>();
            var e = set as IEnumerable;
            if (e != null) foreach (var s in e) if (s != null) l.Add(s.ToString());
            l.Sort(StringComparer.OrdinalIgnoreCase);
            return l;
        }

        static string MaskText(object mask, Type en, string label)
        {
            int m = Convert.ToInt32(mask);
            if (en == null) return m.ToString();
            var vals = Enum.GetValues(en);
            int all = (1 << vals.Length) - 1;
            if ((m & all) == all) return GameText("TacticalDoctrine", "AutoEngageAll");
            var l = new List<string>();
            foreach (var v in vals) if ((m & (1 << Convert.ToInt32(v))) != 0) l.Add(AeLabel(label, v));
            return l.Count == 0 ? "-" : string.Join(", ", l.ToArray());
        }

        static Type _tAeLabels;
        static readonly Dictionary<string, MethodInfo> AeLabelMethods = new Dictionary<string, MethodInfo>();

        /// <summary>AutoEngageLabels.Domain / WeaponClass / Target of the game (its language); the enum's name when the game has no such method.</summary>
        static string AeLabel(string method, object value)
        {
            try
            {
                if (_tAeLabels == null)
                {
                    const string full = "SeapowerUI.ViewModels.AutoEngageLabels";
                    _tAeLabels = GameAssembly() == null ? null : GameAssembly().GetType(full);
                    if (_tAeLabels == null) foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) { try { _tAeLabels = a.GetType(full); } catch { } if (_tAeLabels != null) break; }
                }
                if (_tAeLabels != null)
                {
                    MethodInfo mi;
                    if (!AeLabelMethods.TryGetValue(method, out mi)) AeLabelMethods[method] = mi = _tAeLabels.GetMethod(method, BindingFlags.Static | BindingFlags.Public, null, new[] { value.GetType() }, null);
                    var s = mi == null ? null : mi.Invoke(null, new[] { value }) as string;
                    if (!string.IsNullOrEmpty(s)) return s;
                }
            }
            catch { }
            return value.ToString();
        }

        static string LabelRange(float nm)
        {
            try
            {
                if (_tAeLabels != null)
                {
                    var mi = _tAeLabels.GetMethod("RangeWindow", BindingFlags.Static | BindingFlags.Public);
                    var s = mi == null ? null : mi.Invoke(null, new object[] { 0f, nm }) as string;
                    if (!string.IsNullOrEmpty(s)) return s;
                }
            }
            catch { }
            return "<= " + nm.ToString("0.#") + " NM";
        }

        static Type _tLangHandler, _tLangSingleton;
        static MethodInfo _miGetText;

        /// <summary>The game's text (section, key) in the game's language; the key itself when it is not there.</summary>
        static string GameText(string section, string key)
        {
            try
            {
                var asm = GameAssembly();
                if (asm == null) return key;
                if (_tLangHandler == null) { _tLangHandler = asm.GetType("SeaPower.LanguageResourceHandler"); _tLangSingleton = asm.GetType("SeaPower.Singleton`1"); }
                if (_tLangHandler == null || _tLangSingleton == null) return key;
                var inst = _tLangSingleton.MakeGenericType(_tLangHandler).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public).GetValue(null, null);
                if (inst == null) return key;
                if (_miGetText == null) _miGetText = _tLangHandler.GetMethod("getText", new[] { typeof(string), typeof(string) });
                var s = _miGetText == null ? null : _miGetText.Invoke(inst, new object[] { section, key }) as string;
                return string.IsNullOrEmpty(s) ? key : s;
            }
            catch { return key; }
        }
    }
}
