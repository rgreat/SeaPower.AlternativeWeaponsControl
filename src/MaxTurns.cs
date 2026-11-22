using System;
using System.Reflection;
using HarmonyLib;

namespace AlternativeWeaponsControl
{
    public partial class AwcPlugin
    {
        // "Max maneuverability" (the Weapons tab, next to Strict EMCON, fixed-wing aircraft only): the unit's own Standing Orders values
        // "Aircraft turns out of combat" and "Aircraft turns in combat" (TacticalDoctrine.AircraftTurnsOutOfCombat / AircraftTurnsInCombat) are overridden
        // with Max rate; pressing again restores what the unit had (its own value or the inheritance). The game reads the values at once (Aircraft.TurnAggressiveness,
        // at most 1 s of mission time old). The game saves the unit's overrides itself; the mod saves the switch and the previous state.
        // The values alone change little (a hard route turn happens anyway when the route needs it, at the load factor of the arc): while the switch is on the
        // limit of a transit turn (FixedWingFlightPhysics.TransitTurnLimit) is the most the aircraft has (BleedTurnHorizontalLimit).
        const int TurnMaxRate = 2;

        /// <summary>Only where the game has the aircraft turn doctrine and the physics the switch works through (the beta); elsewhere the button and the G box are hidden.</summary>
        static bool CanMaxTurns(object unit)
        {
            if (_legacy || _miBleedLimit == null || unit == null || _tAircraft == null || !R.Is(unit, _tAircraft)) return false;
            try { return TurnOptions(unit) != null; } catch { return false; }
        }

        static object[] TurnOptions(object unit)
        {
            var d = R.P(unit, "TacticalDoctrine");
            var a = R.P(d, "AircraftTurnsOutOfCombat"); var b = R.P(d, "AircraftTurnsInCombat");
            return a == null || b == null ? null : new[] { a, b };
        }

        static bool _maxTurnsAny;   // some unit has the switch on: the physics hook does nothing otherwise (it runs every tick)

        /// <summary>The load factor of the aircraft now (the game's GLoad of its motion controller: + pulls up, - pushes down).</summary>
        static bool CurrentG(object unit, out float g)
        {
            g = 0f;
            try { var mc = R.P(unit, "Motioncontroller"); var v = mc == null ? null : R.P(mc, "GLoad"); if (v == null) return false; g = Convert.ToSingle(v); return true; }
            catch { return false; }
        }

        static bool MaxTurnsActive(object unit) { UnitW w; return unit != null && UnitsW.TryGetValue(unit, out w) && w.MaxTurns; }

        void HookMaxTurns(Harmony harmony, Assembly asm)
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            var t = asm.GetType("SeaPower.FixedWingFlightPhysics");
            var lim = t == null ? null : t.GetMethod("TransitTurnLimit", all, null, new[] { typeof(float) }, null);
            if (lim == null || t.GetMethod("BleedTurnHorizontalLimit", all, null, new[] { typeof(float) }, null) == null || t.GetProperty("_aircraft") == null)
            { Warn("Max maneuverability: FixedWingFlightPhysics.TransitTurnLimit / BleedTurnHorizontalLimit not found: the button changes the Standing Orders values only"); return; }
            _miBleedLimit = t.GetMethod("BleedTurnHorizontalLimit", all, null, new[] { typeof(float) }, null);
            harmony.Patch(lim, postfix: Hm("MaxTurnLimitPostfix"));
            Trace("Max maneuverability: TransitTurnLimit patched");
            // In combat the physics does not use the Standing Orders limit: it turns with all the load factor, except when the state asks for sustained turns only
            // (surface attack) or energy limited turns (missile evasion). A unit with the switch on is freed from both.
            var ap = t.GetMethod("ApplyQuaternionControlInput", all);
            if (ap == null || t.GetField("SustainedTurnsOnly") == null || t.GetField("EnergyLimitedTurns") == null)
            { Warn("Max maneuverability: ApplyQuaternionControlInput / SustainedTurnsOnly not found: combat turns stay as the game has them"); return; }
            _fiSustained = t.GetField("SustainedTurnsOnly"); _fiEnergy = t.GetField("EnergyLimitedTurns");
            harmony.Patch(ap, prefix: Hm("MaxTurnCombatPrefix"));
            _miCorner = t.GetMethod("CornerSpeed", all); _miEnergyRate = t.GetMethod("EnergyTurnRateSpeed", all);
            if (_miCorner == null || _miEnergyRate == null) Warn("Max maneuverability: CornerSpeed / EnergyTurnRateSpeed not found: no speed floor out of combat");
            _miTransitLim = lim; _miAvailLf = t.GetMethod("AvailableLoadFactor", all);
            // The route turn limit is tan(bank) with the bank capped by the aircraft's MaxRollForHeading (50 deg for an A-4: 1.2 G horizontal, even if 7 G are available).
            var mr = t.GetProperty("MaxRollForHeading", all);
            var mrGet = mr == null ? null : mr.GetGetMethod(true);
            if (mrGet != null) { harmony.Patch(mrGet, postfix: Hm("MaxRollPostfix")); Trace("Max maneuverability: MaxRollForHeading patched"); }
            else Warn("Max maneuverability: MaxRollForHeading not found: the bank of route turns stays capped");
            harmony.Patch(ap, postfix: Hm("MaxTurnDiagPostfix"));
            Trace("Max maneuverability: combat turn limits patched");
        }

        static FieldInfo _fiSustained, _fiEnergy;
        static MethodInfo _miCorner, _miEnergyRate;

        /// <summary>How much of the extra maneuverability a unit with the switch on may use at its speed out of combat (0..1): none at or under the speed of the best turn
        /// (the corner / best sustained rate speed, there the game's own Max rate mode holds the speed), all of it 15% above. Without it an aircraft pulls hard turns
        /// until it can hardly keep level flight.</summary>
        static readonly float[] TurnsFloorK = { 0f, 0.8f, 1f, 1.25f };   // by level: the speed floor as a part of the corner speed (0: none)

        /// <summary>The speed floors of the four levels in knots for the unit as it flies now (the corner speed depends on altitude and mass); false if not known.</summary>
        static bool TurnsFloorsKt(object unit, out float[] kt)
        {
            kt = new float[4];
            try
            {
                var mc = R.P(unit, "Motioncontroller");
                if (mc == null || _miCorner == null || _miEnergyRate == null || !_miCorner.DeclaringType.IsInstanceOfType(mc)) return false;
                float b = Math.Max(Convert.ToSingle(_miCorner.Invoke(mc, null)), Convert.ToSingle(_miEnergyRate.Invoke(mc, null)));
                // the game's safe speed is 1.3 x stall x sqrt(load factor now): shown at 1 G, or it jumps while the aircraft pulls
                kt[0] = 1.3f * Convert.ToSingle(R.P(mc, "StallSpeed")) * 1.943844f;
                for (int i = 1; i < 4; i++) kt[i] = TurnsFloorK[i] * b * 1.943844f;
                if (kt[0] > kt[1]) kt[0] = kt[1];
                return true;
            }
            catch { return false; }
        }

        static int TurnsLevel(object unit) { UnitW w; return unit != null && UnitsW.TryGetValue(unit, out w) ? w.TurnsLevel : 2; }

        static void SetTurnsLevel(object unit, int level)
        {
            if (unit == null || level < 0 || level > 3) return;
            W(unit).TurnsLevel = level;
            Trace("max maneuverability speed floor: " + UName(unit) + " level " + level + " (" + TurnsFloorK[level].ToString("0.00") + " x corner speed)");
        }

        sealed class ShareBox { public int Frame = -1; public float V = 1f; }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, ShareBox> _shareBox = new System.Runtime.CompilerServices.ConditionalWeakTable<object, ShareBox>();

        /// <summary>The share is asked several times per physics step (the turn limit, the bank cap): worked out once per frame for an aircraft (the speed changes slowly).</summary>
        static float HardShare(object phys)
        {
            var b = _shareBox.GetOrCreateValue(phys);
            int fr = UnityEngine.Time.frameCount;
            if (b.Frame == fr) return b.V;
            b.V = HardShareCalc(phys); b.Frame = fr;
            return b.V;
        }

        static float HardShareCalc(object phys)
        {
            try
            {
                if (_miCorner == null || _miEnergyRate == null) return 1f;
                float k = TurnsFloorK[TurnsLevel(R.P(phys, "_aircraft"))];
                if (k <= 0f) return 1f;
                float v = Convert.ToSingle(R.P(phys, "Velocity"));
                float f = k * Math.Max(Convert.ToSingle(_miCorner.Invoke(phys, null)), Convert.ToSingle(_miEnergyRate.Invoke(phys, null)));
                if (f <= 1f) return 1f;
                return Math.Max(0f, Math.Min(1f, (v - f) / (0.15f * f)));
            }
            catch { return 1f; }
        }

        /// <summary>No bank cap for a route turn of a unit with the switch on: the turn is limited by the load factor, the speed and the low level (height) limit of the game only.</summary>
        static void MaxRollPostfix(object __instance, ref float __result)
        {
            if (!_maxTurnsAny || __result >= 89f) return;
            try { if (MaxTurnsActive(R.P(__instance, "_aircraft"))) __result += (89f - __result) * HardShare(__instance); } catch { }
        }
        static MethodInfo _miTransitLim, _miAvailLf;

        /// <summary>Diagnostics of a unit with the switch on: what the physics decides about its turn (every 2 s).</summary>
        static void MaxTurnDiagPostfix(object __instance)
        {
            if (!_maxTurnsAny || !_debug) return;
            try
            {
                var ac = R.P(__instance, "_aircraft");
                if (!MaxTurnsActive(ac)) return;
                if (!TraceDue("maxturnd" + UName(ac), 2f)) return;
                var one = new object[] { 1f };
                Trace("max maneuverability diag " + UName(ac)
                    + ": v=" + Convert.ToSingle(R.P(__instance, "Velocity")).ToString("0") + " m/s alt=" + Convert.ToSingle(R.P(__instance, "Altitude")).ToString("0") + " m"
                    + " transit=" + R.P(__instance, "IsTransit") + " inCombat=" + R.F(ac, "_inCombat")
                    + " availLF=" + Convert.ToSingle(_miAvailLf.Invoke(__instance, null)).ToString("0.0")
                    + " transitLim=" + Convert.ToSingle(_miTransitLim.Invoke(__instance, one)).ToString("0.0")
                    + " bleedLim=" + Convert.ToSingle(_miBleedLimit.Invoke(__instance, one)).ToString("0.0")
                    + " needG=" + Convert.ToSingle(R.F(__instance, "_routeTurnNeedG")).ToString("0.0")
                    + " routeBleed=" + R.F(__instance, "_routeTurnBleed") + " hard=" + R.F(__instance, "_hardTurning")
                    + " nCmd=" + Convert.ToSingle(R.F(__instance, "_nCmd")).ToString("0.0") + " bank=" + Convert.ToSingle(R.P(__instance, "BankAngle")).ToString("0") + " maxRoll=" + Convert.ToSingle(R.P(__instance, "MaxRollForHeading")).ToString("0")
                    + " share=" + HardShare(__instance).ToString("0.00") + " lowBank=" + Convert.ToSingle(R.P(__instance, "LowLevelBankLimitDeg")).ToString("0")
                    + " sustOnly=" + _fiSustained.GetValue(__instance) + " mode=" + R.P(ac, "TurnAggressiveness"));
            }
            catch (Exception e) { TraceLimited("maxturnd-err", 60f, "max maneuverability diag: " + e.Message); }
        }

        static void MaxTurnCombatPrefix(object __instance)
        {
            if (!_maxTurnsAny) return;
            try
            {
                if (!(bool)_fiSustained.GetValue(__instance) && !(bool)_fiEnergy.GetValue(__instance)) return;
                var ac = R.P(__instance, "_aircraft");
                if (!MaxTurnsActive(ac)) return;
                if (TraceDue("maxturnc" + UName(ac), 20f)) Trace("max maneuverability: " + UName(ac) + " combat turn limit lifted (sustained=" + _fiSustained.GetValue(__instance) + " energy=" + _fiEnergy.GetValue(__instance) + ")");
                _fiSustained.SetValue(__instance, false); _fiEnergy.SetValue(__instance, false);
            }
            catch (Exception e) { TraceLimited("maxturnc-err", 60f, "max maneuverability combat: " + e.Message); }
        }

        static MethodInfo _miBleedLimit;

        /// <summary>A unit with Max maneuverability turns in transit flight (route, waypoints) at all the load factor it has, not at the one its Standing Orders mode allows
        /// (the game's limit of a mode is lower, and a hard turn happens only when the route needs it, at exactly the load factor of the arc).</summary>
        static void MaxTurnLimitPostfix(object __instance, float verticalGs, ref float __result)
        {
            if (!_maxTurnsAny) return;
            try
            {
                if (!MaxTurnsActive(R.P(__instance, "_aircraft"))) return;
                var m = (float)_miBleedLimit.Invoke(__instance, new object[] { verticalGs });
                m = __result + (m - __result) * HardShare(__instance);
                if (m > __result + 0.01f)
                {
                    if (_debug) { var ac = R.P(__instance, "_aircraft"); if (TraceDue("maxturn" + UName(ac), 20f)) Trace("max maneuverability: " + UName(ac) + " turn limit " + __result.ToString("0.0") + " G -> " + m.ToString("0.0") + " G, mode " + R.P(ac, "TurnAggressiveness")); }
                    __result = m;
                }
            }
            catch (Exception e) { TraceLimited("maxturnE", 30f, "max maneuverability: " + e.Message); }
        }

        static void SetMaxTurns(object unit, bool on)
        {
            try
            {
                var o = TurnOptions(unit);
                if (o == null) { Warn("Max maneuverability: the unit's doctrine has no AircraftTurnsOutOfCombat / AircraftTurnsInCombat"); return; }
                var w = W(unit);
                if (on)
                {
                    if (!w.MaxTurns || w.TurnsPrev == null)
                    {
                        w.TurnsPrev = new int[4];
                        for (int i = 0; i < 2; i++) { w.TurnsPrev[i * 2] = R.B(R.P(o[i], "IsSet")) ? 1 : 0; w.TurnsPrev[i * 2 + 1] = Convert.ToInt32(R.P(o[i], "Value")); }
                    }
                    // IsSet first: while the option is not set, a changed Value is taken back at once (Refresh: an unset option follows its parent)
                    foreach (var x in o) { R.SetP(x, "IsSet", true); R.SetP(x, "Value", Enum.ToObject(R.P(x, "Value").GetType(), TurnMaxRate)); R.Call(x, "Refresh"); }
                    w.MaxTurns = true; _maxTurnsAny = true;
                }
                else
                {
                    var p = w.TurnsPrev;
                    for (int i = 0; i < 2; i++)
                    {
                        bool wasSet = p != null && p[i * 2] == 1;
                        if (wasSet) { R.SetP(o[i], "IsSet", true); R.SetP(o[i], "Value", Enum.ToObject(R.P(o[i], "Value").GetType(), p[i * 2 + 1])); R.Call(o[i], "Refresh"); }
                        else { R.SetP(o[i], "IsSet", false); R.Call(o[i], "Refresh"); }   // inherits again (Refresh takes the parent's value)
                    }
                    w.MaxTurns = false; w.TurnsPrev = null;
                }
                Trace("max maneuverability " + (on ? "on" : "off") + ": " + UName(unit));
            }
            catch (Exception e) { Warn("Max maneuverability: " + e.Message); }
        }
    }
}
