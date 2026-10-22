using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace AlternativeWeaponsControl
{
    /// <summary>A short point light: a gun's muzzle flash or a hit flash. Fades out and removes itself.</summary>
    public sealed class AwcFlash : MonoBehaviour
    {
        public static int Alive;
        public float Life = 0.1f, Peak = 3f, Hold = 0f, Range = 5f; float _t; Light _l;
        void Awake() { _l = GetComponent<Light>(); Alive++; }
        void OnDestroy() { Alive--; }
        void Update()
        {
            _t += Time.deltaTime;
            if (_t >= Life) { Destroy(gameObject); return; }
            if (_l != null) { float k = AwcPlugin.FlashK(); _l.range = Range * k; float u = _t / Life; _l.intensity = Peak * k * (u <= Hold ? 1f : 1f - (u - Hold) / (1f - Hold)); }
        }
    }

    /// <summary>Flicker of a fire's light: the game's own point light of a compartment fire effect, modulated by noise; deeper and faster for a stronger fire.</summary>
    public sealed class AwcFireFlicker : MonoBehaviour
    {
        public float Strength = 1f;   // 0.5 small, 1 moderate, 1.6 severe
        Light _l; float _i, _r, _seed;
        void Awake() { _seed = UnityEngine.Random.value * 100f; Grab(); }
        void Grab()
        {
            _l = GetComponentInChildren<Light>(true);
            if (_l == null) return;
            _i = _l.intensity; _r = _l.range;
        }
        void OnEnable() { if (_l == null) Grab(); }
        void Update()
        {
            if (_l == null) return;
            if (!AwcPlugin.FlickerOn()) { _l.intensity = _i; _l.range = _r; return; }
            float pw = AwcPlugin.FirePower();
            float t = Time.time * (3f + 3f * Strength) + _seed;
            float n = Mathf.PerlinNoise(t, _seed) * 0.6f + Mathf.PerlinNoise(t * 2.7f, _seed + 7f) * 0.4f;   // 0..1
            float depth = Mathf.Clamp01(0.18f * Strength + 0.07f);
            float f = 1f - depth + 2f * depth * n;
            _l.intensity = Mathf.Max(_i, 0.5f) * pw * f * (0.8f + 0.2f * Strength);
            _l.range = Mathf.Max(_r, 3f) * (1f + 0.5f * (pw - 1f)) * (0.92f + 0.08f * f);
        }
    }

    public partial class AwcPlugin
    {
        internal static float FlashK() { var a = Active; return a != null && a._flashPower != null ? Mathf.Max(a._flashPower.Value, 0.1f) : 1f; }
        internal static float FirePower() { var a = Active; return a != null && a._firePower != null ? Mathf.Max(a._firePower.Value, 0.1f) : 1f; }
        internal static bool FlickerOn() { var a = Active; return a != null && a._fireFlicker != null && a._fireFlicker.Value; }

        void HookFireFlicker(Harmony harmony, Assembly asm)
        {
            var c = asm.GetType("SeaPower.Compartments");
            var m = c == null ? null : c.GetMethod("CreateFireEffect", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (m == null) { Trace("Compartments.CreateFireEffect not found: no fire flicker"); return; }
            harmony.Patch(m, postfix: Hm("FireEffectCreated"));
        }

        static void FireEffectCreated(object __instance, object fireEffect, object effect)
        {
            try
            {
                var go = R.F(fireEffect, "_fireEffectLight") as GameObject; if (go == null) return;
                var obp = R.F(__instance, "_obp");
                float k = 1f;
                if (ReferenceEquals(effect, R.F(obp, "_smallFireEffect"))) k = 0.5f;
                else if (ReferenceEquals(effect, R.F(obp, "_severeFireEffect"))) k = 1.6f;
                var f = go.GetComponent<AwcFireFlicker>() ?? go.AddComponent<AwcFireFlicker>();
                f.Strength = k;
            }
            catch (Exception e) { Trace("fire flicker: " + e.Message); }
        }

        const int FlashMax = 10;
        const float Metre = 67.2f;   // metres in one Unity unit
        static float _ciwsNext;

        static bool FlashOn(int kind)
        {
            var a = Active; if (a == null || !Enabled) return false;
            var e = kind == 1 ? a._flashMuzzle : kind == 2 ? a._flashMissile : a._flashHit;
            return e != null && e.Value;
        }

        /// <summary>A point light of the given size at a world position; skipped when the camera is far away or too many are alive.</summary>
        static void Flash(Vector3 pos, float range, float life, Color col, float peak, Transform parent = null, float hold = 0f)
        {
            try
            {
                if (AwcFlash.Alive >= FlashMax + (parent != null ? 8 : 0)) return;
                var cam = Camera.main;
                if (cam != null && (cam.transform.position - pos).sqrMagnitude > 275f * 275f) return;   // ~ 10 nm (1 unit = 67 m)
                float k = FlashK();
                var go = new GameObject("AwcFlash");
                go.transform.position = pos;
                if (parent != null) go.transform.SetParent(parent, true);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point; l.color = col; l.range = range * k; l.intensity = peak * k; l.shadows = LightShadows.None;
                var f = go.AddComponent<AwcFlash>(); f.Life = life; f.Peak = peak; f.Range = range; f.Hold = hold;
            }
            catch (Exception e) { Trace("flash: " + e.Message); }
        }

        static readonly Color MuzzleCol = new Color(1f, 0.72f, 0.38f), HitCol = new Color(1f, 0.6f, 0.25f);

        // gun barrel: the game's GunBarrel.LaunchProjectile spawns the muzzle effect at _gunObject + _effectPosition
        static void BarrelFlash(object __instance, object ammo)
        {
            if (!FlashOn(1)) return;
            try
            {
                var go = R.F(__instance, "_gunObject") as GameObject; if (go == null) return;
                var lp = R.F(__instance, "_effectPosition");
                Vector3 pos = lp is Vector3 ? go.transform.TransformPoint((Vector3)lp) : go.transform.position;
                // by the shell: brightness 25..100 % and radius 50..100 % from the lightest (0.13 kg, 0.5 in) to the heaviest shell (862 kg, 406 mm),
                // on a gentle log scale ln(mass + 10 kg)
                float mass = ammo == null ? 0f : R.Fl(R.F(R.F(ammo, "_ap"), "_massInKg"));
                float t = mass > 0f ? Mathf.Clamp01(Mathf.Log((mass + 10f) / 10.13f) / Mathf.Log(872f / 10.13f)) : 0.3f;
                Flash(pos, (50f + 250f * t) / Metre, 0.09f * (1f + 2f * t), MuzzleCol, 5f * (0.25f + 0.75f * t));   // radius 50 .. 300 m
            }
            catch (Exception e) { Trace("muzzle flash: " + e.Message); }
        }

        // CIWS / machine guns fire continuously: a small flash about 8 times a second while firing
        static void CiwsFlash(object __instance)
        {
            if (!FlashOn(1)) return;
            try
            {
                if (!R.B(R.F(__instance, "_isFiring"))) return;
                float now = Time.time; if (now < _ciwsNext) return;
                var root = R.F(__instance, "_tracers") as GameObject; if (root == null) return;
                _ciwsNext = now + 0.12f * UnityEngine.Random.Range(0.7f, 1.3f);
                Flash(root.transform.position, 50f / Metre, 0.07f, MuzzleCol, 2f);   // radius 50 m
            }
            catch (Exception e) { Trace("ciws flash: " + e.Message); }
        }

        // missile launch: a light that follows the missile for as long as its booster burns (MissileSimulator.GetAccelerationTimes primary time)
        static void MissileLaunchPrefix(object __instance, ref bool __state) { try { __state = R.F(__instance, "_flightEffects") == null; } catch { __state = false; } }

        static MethodInfo _mGetAccel;
        static void MissileLaunchFlash(object __instance, bool __state)
        {
            if (!__state || !FlashOn(2)) return;
            try
            {
                var ap = R.F(__instance, "_ap"); if (ap == null) return;
                var gm = _mGetAccel ?? (_mGetAccel = _tObjectBase.Assembly.GetType("SeaPower.MissileSimulator").GetMethod("GetAccelerationTimes", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));
                var tup = gm.Invoke(null, new object[] { ap, R.B(R.F(__instance, "_airLaunched")) });
                float burn = R.Fl(tup.GetType().GetField("Item1").GetValue(tup));
                if (!(burn > 0.2f)) burn = 1f;
                burn = Mathf.Min(burn, 20f);
                var tr = (__instance as Component) == null ? null : ((Component)__instance).transform; if (tr == null) return;
                // the bigger the missile, the stronger the booster light: brightness 25 % .. 100 % and radius 50 % .. 100 % from the lightest missile of
                // the game (15 kg) to the heaviest (7000 kg), on a gentle log scale: ln(mass + 250 kg)
                float mass = R.Fl(R.F(ap, "_massInKg"));
                float t = mass > 1f ? Mathf.Clamp01(Mathf.Log((mass + 250f) / 265f) / Mathf.Log(7250f / 265f)) : 1f;   // log of (mass + 250): gentler than a plain log
                Flash(tr.position, 12f * (0.5f + 0.5f * t), burn, HitCol, 5f * (0.25f + 0.75f * t), tr, 0.7f);
            }
            catch (Exception e) { Trace("missile flash: " + e.Message); }
        }

        // torpedoes, bombs and ASW mortar rounds (RBU) detonating: one flash by the warhead power (the game's ammunition Power, 0.1 for a 0.5 in bullet
        // .. 35 for a 16 in shell: the guns' scale; the heavier ones are capped), on the hit scale: brightness 25..100 %, radius 100..600 m.
        // Their explosion effects are underwater or the ship-hit ones: while this runs the generic ExplosionFlash is off, so the flash is not doubled.
        static bool _inDetonation;

        static bool IsDetonationType(object wb)
        {
            var ap = R.F(wb, "_ap"); if (ap == null) return false;
            var ty = R.F(ap, "_type"); var n = ty == null ? "" : ty.ToString();
            return n == "Torpedo" || n == "Bomb" || n == "RBU";
        }

        static void DetonationEnter(object __instance) { try { _inDetonation = IsDetonationType(__instance); } catch { _inDetonation = false; } }
        static Exception DetonationLeave(Exception __exception) { _inDetonation = false; return __exception; }

        static void DetonationFlash(object __instance, Vector3 position)
        {
            if (!_inDetonation) return;
            _inDetonation = false;
            try
            {
                if (!FlashOn(0)) return;
                if (R.B(R.F(__instance, "_warheadMalfunction"))) return;
                float power = R.Fl(R.F(R.F(__instance, "_ap"), "_warheadPower"));
                float t = Mathf.Clamp01(Mathf.Log((power + 2f) / 2.1f) / Mathf.Log(37f / 2.1f));
                position.y = Mathf.Max(position.y, 0.3f);   // a light under the water would light nothing
                Flash(position, (100f + 500f * t) / Metre, 0.392f * (1f + 2f * t), HitCol, 14.9f * (0.25f + 0.75f * t));
            }
            catch (Exception e) { Trace("detonation flash: " + e.Message); }
        }

        // every explosion the game spawns (shell / missile / bomb / warhead hits, flak, ammo explosions) by the name or class of its effect;
        // size from Tiny / Small / Medium / Large / VeryLarge in the name. Splashes and underwater explosions: none.
        static void ExplosionFlash(GameObject fx, object ep)
        {
            if (_inDetonation || !FlashOn(0) || fx == null || ep == null) return;
            try
            {
                var ef = R.F(ep, "_effect"); if (ef == null) return;
                var n = ((R.S(R.F(ef, "_effectClassName")) ?? "") + " " + (R.S(R.F(ef, "_name")) ?? "")).ToLowerInvariant();
                if (!(n.Contains("explosion") || n.Contains("impact_") || n.Contains("blast") || n.Contains("detonat"))) return;
                if (n.Contains("water") || n.Contains("splash") || n.Contains("underwater") || n.Contains("wake") || n.Contains("starshell")) return;
                // the size tier in the effect's name is the strength: Tiny .. VeryLarge = brightness 25..100 % and radius 50..100 %
                float t = n.Contains("verylarge") ? 1f : n.Contains("large") ? 0.75f : n.Contains("medium") ? 0.5f : n.Contains("small") ? 0.25f : n.Contains("tiny") ? 0f : 0.5f;
                float lo = n.Contains("flak") ? 0.7f : 1f;
                // the machine guns' / CIWS / autocannons' hits (0.5 in .. 35 mm: impact_small_shell_multi) have a radius of 100 m
                float radius = n.Contains("small_shell_multi") ? 100f : lo * (100f + 500f * t);
                Flash(fx.transform.position, radius / Metre, 0.392f * (1f + 2f * (n.Contains("small_shell_multi") ? 0f : t)), HitCol, 14.9f * lo * (0.25f + 0.75f * t));   // radius 100 .. 600 m
            }
            catch (Exception e) { Trace("explosion flash: " + e.Message); }
        }
    }
}
