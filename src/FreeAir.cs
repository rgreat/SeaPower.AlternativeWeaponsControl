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

        static void FreeAirPostfix(object __instance)
        {
            try
            {
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
                var add = new List<object>();
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
                    add.Add(o);
                }
                if (add.Count == 0) return;
                foreach (var o in add) dict[o] = R.I(R.Call(unit, "GetEngagePriority", o));
                a.Verbose("free air: " + add.Count + " unidentified fast aircraft added to the targets of " + R.S(R.Call(unit, "getUIDAndName")));
            }
            catch (Exception e) { Trace("free air: " + e.Message); }
        }
    }
}
