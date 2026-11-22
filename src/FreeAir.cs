using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace AlternativeWeaponsControl
{
    /// <summary>
    /// "On Free: engage approaching fast unidentified aircraft" (a per-ship switch in the Weapons tab, saved with the unit).
    /// The game's AI takes a unit for a target only when the player's plot has it as hostile (AI.IdentifiedAsHostile); an aircraft not yet
    /// identified is never engaged. For a ship on Free with the switch on (default: off for the player's ships, on for the enemy's AI), the targets list of the AI
    /// (AI.GetPossibleTargetsList) gets the aircraft that are: still unidentified on the plot (relation Unknown), faster than
    /// [Fire] UnknownAirMinKnots (500), closer than [Fire] UnknownAirMaxNm (50) and coming towards the ship (within 45 degrees of the bearing to it). Whose aircraft it is in fact
    /// is NOT looked at (no cheating: the switch does not know it) - a friendly or neutral one that fits is engaged like any other.
    /// </summary>
    public partial class AwcPlugin
    {
        internal ConfigEntry<float> _freeAirKnots, _freeAirNm;
        internal ConfigEntry<bool> _freeAirOwn, _freeAirEnemy;

        // Debug: what the AI of one of the player's units may engage (the game's possible targets with their priorities), traced when it changes
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, string[]> _tgtSig = new System.Runtime.CompilerServices.ConditionalWeakTable<object, string[]>();
        /// <summary>Debug: the unit's engage tasks as the aircraft AI of the game sees them: ammunition type, the player's (P) or the automatic (A) one, distance to the target and the reach of the ammunition
        /// (nm, rounded to 5), whether the task is beyond the shot range (the aircraft is then sent towards the target), and whether the game has a move-to-engage task for the unit.</summary>
        static string AirTaskDetail(object unit)
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                try { sb.Append(" move=").Append(R.B(R.Call(unit, "HasMoveToEngageTask"))); } catch { }
                var tasks = R.F(unit, "_currentEngageTasks") as IList;
                if (tasks == null) return sb.ToString();
                var up = (Vector3)R.V(R.V(unit, "transform"), "position");
                int n = 0;
                foreach (var t in tasks)
                {
                    if (t == null || ++n > 4) break;
                    string d = "?", r = "?", beyond = "?", type = "?";
                    try
                    {
                        var am = R.F(t, "_ammunition"); var ap = am == null ? null : R.F(am, "_ap");
                        if (ap != null) type = R.S(R.F(ap, "_type"));
                        var tg = R.F(t, "_targetObject");
                        if (tg != null)
                        {
                            var tp = (Vector3)R.V(R.V(tg, "transform"), "position");
                            d = (Math.Round((tp - up).magnitude * 67.200066f / 1000f / 1.852f / 5f) * 5).ToString();
                            if (am != null) { try { r = (Math.Round(Convert.ToSingle(R.Call(unit, "GetMaxRangeForAmmo", am, tg)) * 67.200066f / 1000f / 1.852f / 5f) * 5).ToString(); } catch { } }
                        }
                        try { beyond = R.B(R.Call(unit, "EngageTaskBeyondShotRange", t)).ToString(); } catch { }
                    }
                    catch { }
                    sb.Append(" {").Append(R.B(R.F(t, "_isAutoAttack")) ? "A " : "P ").Append(R.S(R.F(t, "_ammoId"))).Append(' ').Append(type).Append(" dist=").Append(d).Append(" reach=").Append(r).Append(" beyond=").Append(beyond).Append('}');
                }
            }
            catch { }
            return sb.ToString();
        }

        static void TraceTargets(object ai)
        {
            try
            {
                var unit = R.F(ai, "_baseObject");
                if (unit == null || !R.B(R.P(unit, "IsPlayerObject"))) return;
                var dict = R.F(ai, "_possibleTargetsWithPriorities") as IDictionary;
                var sb = new System.Text.StringBuilder(); var sg = new System.Text.StringBuilder();
                sb.Append(R.S(R.F(unit, "_weaponStatus"))).Append(" |"); sg.Append(sb.ToString());
                // an aircraft: what its AI has made of the player's order (the click on a target puts it into the list; the game pursues the committed one only)
                if (R.B(R.P(unit, "IsAirUnit")))
                {
                    string ex = "";
                    try
                    {
                        var dest = R.F(ai, "_objectToDestroy"); var lst = R.F(ai, "_objectsToDestroyList") as ICollection;
                        var st = R.P(R.P(unit, "StateMachine"), "CurrentState");
                        string ds = "-";
                        if (dest != null)
                        {
                            ds = R.S(R.Call(dest, "getUIDAndName"));
                            try { ds += R.B(R.Call(ai, "IsCommittedPursuit", dest)) ? " committed" : " not committed"; } catch { }
                            try
                            {
                                var dp = (Vector3)R.V(R.V(dest, "transform"), "position"); var up2 = (Vector3)R.V(R.V(unit, "transform"), "position");
                                ds += " dist=" + (Math.Round((dp - up2).magnitude * 0.036285132f / 5) * 5) + "nm maxrange=" + (Math.Round(Convert.ToSingle(R.Call(ai, "MaxRangeForObject", dest)) * 0.036285132f / 5) * 5) + "nm";
                            }
                            catch { }
                            try { ds += " ordered=" + R.B(R.Call(ai, "IsOrderedTarget", dest)); } catch { }
                        }
                        ex = " destroy=" + ds + " list=" + (lst == null ? 0 : lst.Count) +
                             " state=" + (st == null ? "?" : st.GetType().Name) + " HasAAM=" + R.B(R.P(R.F(unit, "_currentLoadoutCapabilities"), "HasAAM")) + " aamUsable=" + AamNow(unit) + " tasks=" + R.I(R.P(R.F(unit, "_currentEngageTasks"), "Count")) + AirTaskDetail(unit) + " |";
                    }
                    catch { }
                    sb.Append(ex); sg.Append(ex);
                }
                var up = (Vector3)R.V(R.V(unit, "transform"), "position");
                if (dict != null)
                    foreach (DictionaryEntry de in dict)
                    {
                        var o = de.Key; var oc = o as Component;
                        sg.Append(' ').Append(R.S(R.Call(o, "getUIDAndName"))).Append(" p").Append(de.Value);
                        sb.Append(' ').Append(R.S(R.Call(o, "getUIDAndName"))).Append(" p").Append(de.Value);
                        if (oc != null) sb.Append(' ').Append(((oc.transform.position - up).magnitude * 0.036285132f).ToString("0")).Append("nm");
                        sb.Append(';');
                    }
                var sig = sg.ToString();
                string[] last;
                if (!_tgtSig.TryGetValue(ai, out last)) { last = new string[1]; _tgtSig.Add(ai, last); }
                if (last[0] == sig) return;
                last[0] = sig;
                Trace("ai targets " + R.S(R.Call(unit, "getUIDAndName")) + ": " + sb);
            }
            catch { }
        }

        /// <summary>
        /// The game's AI (GetPossibleTargetsList) takes a unit for a target only if the contact is hostile on the plot (IdentifiedAsHostile) AND its true taskforce is
        /// hostile to the ship's (RelationshipTo): a neutral airliner the player marked hostile is passed by - the AI uses what the player cannot know. For the player's
        /// ships the contacts hostile on the plot are added when the unit's auto-engage rules let it engage them.
        /// </summary>
        /// <summary>AutoEngageRules.UnitMayAutoEngage: do the unit's rules let it engage the contact by itself in its weapon status?</summary>
        static bool IsFreeStatus(object unit) { var ws = R.F(unit, "_weaponStatus"); return ws != null && ws.ToString() == "Free"; }

        /// <summary>Release game (doctrine switches): Free - every contact, a surface one only with AutoAttackSurface; Tight - aircraft and helicopters with "On Tight: engage hostile aircraft"; Hold - none.</summary>
        static bool LegacyMayEngage(object unit, object target)
        {
            var ws = R.F(unit, "_weaponStatus");
            string st = ws == null ? null : ws.ToString();
            bool air = R.B(R.P(target, "IsAirUnit"));
            if (st == "Free") return air || ItemValue(unit, 0);
            if (st != "Tight") return false;
            var it = TightAirItem(unit);
            return it != null && R.B(R.P(it, "ResolvedValue")) && (R.Is(target, _tAircraft) || R.Is(target, _tHelicopter));
        }

        static bool MayAutoEngage(object unit, object target)
        {
            if (_legacy) return LegacyMayEngage(unit, target);
            // without the game's rules (not found) the game's own answer is used: no doctrine = Weapons Free only
            if (_miMayEngage == null) return IsFreeStatus(unit);
            try { return (bool)_miMayEngage.Invoke(null, new[] { unit, target }); } catch { return IsFreeStatus(unit); }
        }

        static void PlotHostilePostfix(object __instance)
        {
            try
            {
                var a = Active;
                if (a == null || !Enabled || a._plotHostile == null || !a._plotHostile.Value) return;   // (cheap checks first: this runs for every ship's AI)
                var unit = R.F(__instance, "_baseObject");
                if (unit == null || !R.Is(unit, _tVessel) || !R.B(R.P(unit, "IsPlayerObject"))) return;
                // The game's AI takes a contact only if the player's plot calls it hostile AND its true side is hostile; the contacts hostile on the plot are added here for
                // every weapon status, as far as the unit's auto-engage rules let it engage them (Hold: only what a rule with Always allows).
                var list = R.F(__instance, "_processedObjects") as IList;
                var dict = R.F(__instance, "_possibleTargetsWithPriorities") as IDictionary;
                var tf = R.V(unit, "_taskforce");
                if (list == null || dict == null || tf == null || list.Count == 0) return;
                List<object> add = null;
                foreach (var o in list)
                {
                    if (o == null || dict.Contains(o) || R.B(R.P(o, "IsDestroyed")) || !R.B(R.Call(o, "isUnit"))) continue;
                    if (!MayAutoEngage(unit, o)) continue;
                    var mv = R.Call(o, "GetMapVehicle", tf);
                    if (mv == null || !(R.Call(mv, "CurrentRelationship") ?? "").ToString().Equals("Hostile")) continue;
                    (add ?? (add = new List<object>())).Add(o);
                }
                if (add == null) return;
                foreach (var o in add) dict[o] = R.I(R.Call(unit, "GetEngagePriority", o));
                // the game sorts the list (priority, then distance): sorted again with the added ones, else the order of adding decides
                var up = ((Component)unit).transform.position;
                var pairs = new List<DictionaryEntry>();
                foreach (DictionaryEntry de in dict) pairs.Add(de);
                var sorted = new List<DictionaryEntry>(System.Linq.Enumerable.ThenBy(
                    System.Linq.Enumerable.OrderBy(pairs, de => (int)de.Value),
                    de => { var c = de.Key as Component; return c == null ? 0f : (c.transform.position - up).sqrMagnitude; }));
                var nd = (IDictionary)Activator.CreateInstance(dict.GetType());
                foreach (var de in sorted) nd[de.Key] = de.Value;
                R.SetF(__instance, "_possibleTargetsWithPriorities", nd);
                a.Verbose("plot hostile: " + add.Count + " contact(s) marked hostile added to the targets of " + R.S(R.Call(unit, "getUIDAndName")));
            }
            catch (Exception e) { Trace("plot hostile: " + e.Message); }
        }

        static void FreeAirPostfix(object __instance)
        {
            try
            {
                if (_debug) TraceTargets(__instance);
                var a = Active;
                if (a == null || !Enabled || a._freeAirKnots == null) return;   // (cheap checks first: this runs for every ship's AI)
                var unit = R.F(__instance, "_baseObject");
                if (unit == null || !R.Is(unit, _tVessel) || !FreeAirOn(unit)) return;
                var ws = R.F(unit, "_weaponStatus");
                if (ws == null || ws.ToString() != "Free") return;
                var list = R.F(__instance, "_processedObjects") as IList;
                var dict = R.F(__instance, "_possibleTargetsWithPriorities") as IDictionary;
                var tf = R.V(unit, "_taskforce");
                var uc = unit as Component;
                if (list == null || dict == null || tf == null || uc == null) return;
                float min = a._freeAirKnots.Value, maxNm = a._freeAirNm.Value;
                List<object> add = null;
                foreach (var o in list)
                {
                    var oc = o as Component;
                    if (oc == null || dict.Contains(o) || R.B(R.P(o, "IsDestroyed")) || !R.B(R.P(o, "IsAirUnit")) || !R.B(R.Call(o, "isUnit"))) continue;
                    var mv = R.Call(o, "GetMapVehicle", tf);
                    if (mv == null || !(R.Call(mv, "CurrentRelationship") ?? "").ToString().Equals("Unknown")) continue;
                    var vel = (Vector3)R.P(o, "UnityVelocityVector");
                    if (vel.magnitude / 0.0076554087f < min) continue;
                    var to = uc.transform.position - oc.transform.position;
                    if (to.sqrMagnitude < 1e-6f || to.magnitude * 0.036285132f > maxNm || Vector3.Dot(vel.normalized, to.normalized) < 0.7071f) continue;
                    (add ?? (add = new List<object>())).Add(o);
                }
                if (add == null) return;
                foreach (var o in add) dict[o] = R.I(R.Call(unit, "GetEngagePriority", o));
                a.Verbose("free air: " + add.Count + " unidentified fast aircraft added to the targets of " + R.S(R.Call(unit, "getUIDAndName")));
            }
            catch (Exception e) { Trace("free air: " + e.Message); }
        }
    }
}
