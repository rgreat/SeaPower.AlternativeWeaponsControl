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
        static void TraceTargets(object ai)
        {
            try
            {
                var unit = R.F(ai, "_baseObject");
                if (unit == null || !R.B(R.P(unit, "IsPlayerObject"))) return;
                var dict = R.F(ai, "_possibleTargetsWithPriorities") as IDictionary;
                var sb = new System.Text.StringBuilder(); var sg = new System.Text.StringBuilder();
                sb.Append(R.S(R.F(unit, "_weaponStatus"))).Append(" |"); sg.Append(sb.ToString());
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
        /// ships on Free the contacts hostile on the plot are added (the rest of the game's tests for a unit are kept: a surface contact needs the AutoAttackSurface doctrine).
        /// </summary>
        static void PlotHostilePostfix(object __instance)
        {
            try
            {
                var a = Active;
                if (a == null || !Enabled || a._plotHostile == null || !a._plotHostile.Value) return;   // (cheap checks first: this runs for every ship's AI)
                var unit = R.F(__instance, "_baseObject");
                if (unit == null || !R.Is(unit, _tVessel) || !R.B(R.P(unit, "IsPlayerObject"))) return;
                var ws = R.F(unit, "_weaponStatus");
                if (ws == null || ws.ToString() != "Free") return;
                var list = R.F(__instance, "_processedObjects") as IList;
                var dict = R.F(__instance, "_possibleTargetsWithPriorities") as IDictionary;
                var tf = R.V(unit, "_taskforce");
                if (list == null || dict == null || tf == null || list.Count == 0) return;
                bool surfaceOk = true;
                try
                {
                    var doc = R.V(unit, "TacticalDoctrine"); var aas = doc == null ? null : R.V(doc, "AutoAttackSurface"); var rv = aas == null ? null : R.V(aas, "ResolvedValue");
                    surfaceOk = rv == null || R.B(rv);
                }
                catch { }
                List<object> add = null;
                foreach (var o in list)
                {
                    if (o == null || dict.Contains(o) || R.B(R.P(o, "IsDestroyed")) || !R.B(R.Call(o, "isUnit"))) continue;
                    if (!R.B(R.P(o, "IsAirUnit")) && !surfaceOk) continue;
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
