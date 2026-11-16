using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace AlternativeWeaponsControl
{
    /// <summary>
    /// [Guns] PointFire. The game fires guns only at objects (an order of a gun needs a target object; a click on the empty map says "target required").
    /// With the option, a right click on the map while a GUN ammunition is being targeted (Fire) puts an invisible decoy land unit of the enemy
    /// taskforce on that point and orders the guns to engage it as an ordinary target. The decoy is removed when no gun of the player has an order on it any more.
    /// </summary>
    public partial class AwcPlugin
    {
        sealed class PfDummy { public object Obj, Src; public float Born, Idle0; }
        static readonly List<PfDummy> PfDummies = new List<PfDummy>();
        static float _pfNext;
        const string PfIni = "civ_comm_buildings";

        static void PfPatch(Harmony h, Assembly asm, string type, string method, string what, string prefix, string postfix)
        {
            try
            {
                var t = asm.GetType(type);
                var m = t == null ? null : t.GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (m == null && t != null && what == "setter") { var pr = t.GetProperty(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); m = pr == null ? null : pr.GetSetMethod(true); }
                if (m == null) { Trace("point fire: " + type + "." + method + " not found"); return; }
                h.Patch(m, prefix: prefix == null ? null : Hm(prefix), postfix: postfix == null ? null : Hm(postfix));
            }
            catch (Exception e) { Trace("point fire: patch " + type + "." + method + " failed: " + e.GetType().Name + " " + e.Message); }
        }

        void HookPointFire(Harmony harmony, Assembly asm)
        {
            // each patch on its own: one that fails must not stop the others
            PfPatch(harmony, asm, "SeapowerUI.ViewModels.AttackingState", "MouseAction", "", "PfMousePrefix", null);
            PfPatch(harmony, asm, "SeapowerUI.ViewModels.AttackingState", "OnExit", "", "PfExitPrefix", null);
            PfPatch(harmony, asm, "SeaPower.AttackTask", "OnExecute", "", "PfAttackExecPrefix", null);             // no "Engage Track ???" text and voice for the decoy
            PfPatch(harmony, asm, "SeaPower.Vehicle", "IsVisible", "", null, "PfVisiblePostfix");                   // the decoy is no contact of the player: no map marker, no reports
            PfPatch(harmony, asm, "SeaPower.AI", "IsProcessed", "", null, "PfProcessedPostfix");                   // the decoy is known by definition: no wait for a detection (the order waited for ever beyond the sensors' range)
            PfPatch(harmony, asm, "SeapowerUI.ViewModels.InteractionState", "CursorName", "setter", "PfCursorPrefix", null);
            try { Application.onBeforeRender += PfBeforeRender; } catch (Exception e) { Trace("point fire: onBeforeRender: " + e.Message); }
        }

        static bool PfIsDummy(object o) { if (o == null) return false; foreach (var d in PfDummies) if (ReferenceEquals(d.Obj, o)) return true; return false; }

        static void PfProcessedPostfix(object targetObject, ref bool __result)
        {
            if (__result || PfDummies.Count == 0) return;
            try { if (PfIsDummy(targetObject)) __result = true; } catch { }
        }

        static void PfVisiblePostfix(object __instance, ref bool __result)
        {
            if (!__result || PfDummies.Count == 0) return;
            try { if (PfIsDummy(R.F(__instance, "Object"))) __result = false; } catch { }
        }

        // no "Engage Track ???" text and voice for the decoy
        static void PfAttackExecPrefix(object __instance)
        {
            if (PfDummies.Count == 0) return;
            try { if (PfIsDummy(R.P(R.F(__instance, "targetObject"), "value"))) R.SetF(__instance, "_displayOrderText", false); } catch { }
        }

        // ---- cursor: over the empty map while a gun is being targeted the cursor is a dark-grey reticle -------------------------------------------------------------
        static Texture2D _pfTex; static float _pfLastClick, _pfWantUntil; static bool _pfOverMap, _pfDark;

        static Texture2D PfReticle()
        {
            if (_pfTex != null) return _pfTex;
            const int N = 32; float c = (N - 1) / 2f;
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = x - c, dy = y - c, r = Mathf.Sqrt(dx * dx + dy * dy);
                    float dRing = Mathf.Abs(r - 9f);
                    float dH = (Mathf.Abs(dx) <= 13f && Mathf.Abs(dx) >= 4f) ? Mathf.Abs(dy) : 99f;
                    float dV = (Mathf.Abs(dy) <= 13f && Mathf.Abs(dy) >= 4f) ? Mathf.Abs(dx) : 99f;
                    float d = Mathf.Min(dRing, Mathf.Min(dH, dV));
                    px[y * N + x] = d <= 1.1f ? new Color32(200,200,200,255) : new Color32(0, 0, 0, 0);   // opaque, no light/translucent edge: the system cursor takes the alpha as it is
                }
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            t.SetPixels32(px); t.Apply();
            return _pfTex = t;
        }

        /// <summary>The game sets the cursor name on every mouse move over the map ("attacking" / "unavailable" / "attackunit"): note when it is the empty map of a gun's targeting.</summary>
        static void PfCursorPrefix(object __instance, ref string value)
        {
            _pfDark = false;
            var a = Active;
            if (a == null || !Enabled || a._pointFire == null || !a._pointFire.Value || __instance == null || __instance.GetType().Name != "AttackingState") return;
            try
            {
                if (value != "attacking" && !(value == "unavailable" && _pfOverMap)) return;
                var src = R.P(__instance, "Source"); var ammo = src == null ? null : R.Call(src, "getAmmunitionByName", R.S(R.P(__instance, "Ammunition")));
                var ap = ammo == null ? null : R.F(ammo, "_ap");
                if (ap == null || R.S(R.F(ap, "_type")) != "Projectile") return;
                _pfDark = true; _pfWantUntil = Time.unscaledTime + 0.3f;
            }
            catch { }
        }

        // Noesis puts the cursor on when the name changes; here, after that and just before the frame is drawn, the dark one is put over it while it is wanted
        static void PfBeforeRender()
        {
            if (!_pfDark || Time.unscaledTime > _pfWantUntil) return;
            try { var t = PfReticle(); Cursor.SetCursor(t, new Vector2(t.width / 2f, t.height / 2f), CursorMode.Auto); } catch { _pfDark = false; }
        }

        /// <summary>The event log line (own text: point and ammunition) and the game's own voice of an order without a target (RogerThat / YesCommander / Confirmed).</summary>
        static void PfAnnounce(object src, object geo, string ammoName)
        {
            try
            {
                var asm = GameAssemblyStatic();
                string key = new[] { "RogerThat", "YesCommander", "Confirmed" }[UnityEngine.Random.Range(0, 3)];
                double lat = Convert.ToDouble(R.F(geo, "_latitude")), lon = Convert.ToDouble(R.F(geo, "_longitude"));
                string pos = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.000}{1} {2:0.000}{3}", Math.Abs(lat), lat >= 0 ? "N" : "S", Math.Abs(lon), lon >= 0 ? "E" : "W");
                var g = asm.GetType("SeaPower.Globals").GetField("_mainGameViewModel", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null);
                MethodInfo ra = null;
                foreach (var mi in g.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                { var ps = mi.GetParameters(); if (mi.Name == "RaiseAlert" && ps.Length == 2 && ps[1].ParameterType == typeof(string) && ps[0].ParameterType.IsAssignableFrom(src.GetType())) { ra = mi; break; } }
                if (ra != null) ra.Invoke(g, new[] { src, "${Name}: " + Loc.F("pointfire.log", pos, ammoName) });
                var vm = PfSingleton("VoiceMessage"); MethodInfo pv = null;
                foreach (var mi in vm.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                { var ps = mi.GetParameters(); if (mi.Name == "PlayVoiceMessage" && ps.Length == 2 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType.IsAssignableFrom(src.GetType())) { pv = mi; break; } }
                if (pv != null) pv.Invoke(vm, new[] { key, src });
            }
            catch (Exception e) { Trace("point fire announce: " + e.GetType().Name + " " + e.Message); }
        }

        // why a targeting ends (for the trace): the first frames of the caller
        static void PfExitPrefix()
        {
            try
            {
                var st = new System.Diagnostics.StackTrace(2, false); string r = "";
                for (int i = 0; i < st.FrameCount && i < 5; i++) { var m = st.GetFrame(i).GetMethod(); r += (i > 0 ? " < " : "") + (m.DeclaringType == null ? "" : m.DeclaringType.Name + ".") + m.Name; }
                Trace("targeting ended: " + r);
            }
            catch { }
        }

        static bool PfShift()
        {
            try
            {
                var t = Type.GetType("UnityEngine.Input, UnityEngine.InputLegacyModule"); var m = t.GetMethod("GetKey", new[] { typeof(KeyCode) });
                return (bool)m.Invoke(null, new object[] { KeyCode.LeftShift }) || (bool)m.Invoke(null, new object[] { KeyCode.RightShift });
            }
            catch { return false; }
        }

        static object PfSingleton(string type)
        {
            var asm = GameAssemblyStatic();
            var t = asm.GetType(string.Join(".", "SeaPower", type)); var g = asm.GetType("SeaPower.Singleton`1");
            return g.MakeGenericType(t).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public).GetValue(null, null);
        }

        static bool PfMousePrefix(object __instance, object v, object args, object mousePosition, ref bool handled)
        {
            var a = Active;
            if (a == null || !Enabled || a._pointFire == null || !a._pointFire.Value) return true;
            bool ordered = false;
            try
            {
                _pfOverMap = v != null && v.GetType().Name == "MapViewModel";
                if (v == null || v.GetType().Name == "MapUnitViewModel" || args == null || args.GetType().Name != "MouseButtonEventArgs") return true;
                if (R.S(R.P(args, "RightButton")) != "Pressed") return true;
                if (R.B(R.F(__instance, "_isWaypointEdit"))) return true;
                var src = R.P(__instance, "Source"); if (src == null || !R.B(R.P(src, "IsPlayerObject"))) return true;
                string ammoName = R.S(R.P(__instance, "Ammunition"));
                var ammo = R.Call(src, "getAmmunitionByName", ammoName); var ap = ammo == null ? null : R.F(ammo, "_ap");
                if (ap == null || R.S(R.F(ap, "_type")) != "Projectile") return true;
                if (Convert.ToInt32(R.Call(src, "getAmmunitionAmountByName", ammoName)) < 1) return true;
                if (Time.unscaledTime - _pfLastClick < 0.3f) { handled = true; return false; }   // the same click arrives for more than one element
                _pfLastClick = Time.unscaledTime;
                var dummy = PfSpawn(mousePosition, src);
                if (dummy == null) return true;
                R.Call(__instance, "AddAttackTask", src, dummy, mousePosition, 1);
                ordered = true; handled = true;
                Trace("point fire: " + UName(src) + " " + ammoName + " at " + mousePosition);
                PfAnnounce(src, mousePosition, R.S(R.F(ap, "_displayedName")));
                // as after a click on a real target: the targeting mode ends (Shift keeps it)
                if (!PfShift())
                {
                    var mgr = R.F(__instance, "_manager"); MethodInfo pop = null;
                    if (mgr != null) foreach (var mi in mgr.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                        if (mi.Name == "PopState" && mi.IsGenericMethodDefinition) { pop = mi; break; }
                    if (pop != null) pop.MakeGenericMethod(__instance.GetType()).Invoke(mgr, new object[] { true });
                    else Trace("point fire: PopState<T> not found");
                }
                return false;
            }
            catch (Exception e) { Trace("point fire: " + e.GetType().Name + " " + e.Message); return !ordered; }   // an order that is given is never followed by the game's "target required"
        }

        // the plotting table never takes a track number (7001, 7002 ...) back: its counter only grows. Recount like the game does when it restores a table:
        // from the vehicles that really exist, so the numbers of the removed decoys are given out again.
        static void PfFreeTracks(object src)
        {
            try
            {
                var tabs = new List<object>();
                var pt = R.F(GameAssemblyStatic().GetType("SeaPower.Globals").GetField("_playerTaskforce", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null), "PlottingTable");
                if (pt != null) tabs.Add(pt);
                var up = R.F(src, "PlottingTable"); if (up != null) tabs.Add(up);
                foreach (var t in tabs)
                {
                    var vs = R.P(t, "Vehicles") as System.Collections.IEnumerable; if (vs == null) continue;
                    var ids = R.F(t, "_allocatedTrackIds") as HashSet<int>; if (ids == null) continue;
                    int max = 7001; ids.Clear();
                    foreach (var v in vs) { int id = (int)R.P(v, "Id"); ids.Add(id); if (id >= 7001 && id.ToString()[0] == '7' && id >= max) max = id + 1; }
                    int old = (int)R.F(t, "_maxForeignTrackId");
                    if (old != max) { R.SetF(t, "_maxForeignTrackId", max); Trace("point fire: track counter " + old + " -> " + max); }
                }
            }
            catch (Exception e) { Trace("point fire tracks: " + e.Message); }
        }

        static object PfSpawn(object geo, object src)
        {
            var asm = GameAssemblyStatic();
            var mgr = PfSingleton("ObjectsManager"); var sc = PfSingleton("SceneCreator");
            var globals = asm.GetType("SeaPower.Globals");
            var tf = globals.GetField("_enemyTaskforce", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null);
            if (tf == null) { Trace("point fire: no enemy taskforce"); return null; }
            var center = R.F(sc, "GeoCenterPosition");
            var mi = mgr.GetType().GetMethod("createLandUnit", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var ps = mi.GetParameters(); var vals = new object[ps.Length];
            for (int i = 0; i < ps.Length; i++) vals[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : null;
            vals[0] = ""; vals[1] = ""; vals[2] = PfIni; vals[3] = 1; vals[4] = center; vals[5] = geo; vals[6] = 0; vals[7] = Vector3.zero; vals[8] = tf;
            PfFreeTracks(src);
            var obj = mi.Invoke(mgr, vals);
            if (obj == null || ((obj as UnityEngine.Object) != null && !(obj as UnityEngine.Object))) { Trace("point fire: the decoy was not created"); return null; }
            try { foreach (var r in ((Component)obj).GetComponentsInChildren<Renderer>(true)) r.enabled = false; } catch { }
            try
            {
                var tCol = Type.GetType("UnityEngine.Collider, UnityEngine.PhysicsModule"); int nc = 0;
                if (tCol != null) foreach (var c in ((Component)obj).GetComponentsInChildren(tCol, true)) { tCol.GetProperty("enabled").SetValue(c, false, null); nc++; }
                Trace("point fire: " + nc + " colliders of the decoy off");
            }
            catch (Exception e) { Trace("point fire colliders: " + e.Message); }   // nothing to hit: the shells fall on the point
            PfDummies.Add(new PfDummy { Obj = obj, Src = src, Born = Time.unscaledTime, Idle0 = -1f });
            Trace("point fire: decoy " + UName(obj) + " created");
            return obj;
        }

        /// <summary>Why the decoy is still wanted (null = nobody orders it). An order lives in two places: the unit's list of engage tasks while it waits for a mount, and
        /// in the mounts that took it (_takenEngageTask, _executingEngageTask) - from then on the list may no longer hold it, and a mount keeps it while it builds its firing solution.</summary>
        static string PfUsed(object dummy)
        {
            var all = PfSingleton("ObjectsManager");
            var list = R.P(all, "_listOfAllPlayerObjects") as IList; if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
            {
                var u = list[i]; if (u == null) continue;
                var tasks = R.F(u, "_currentEngageTasks") as IList;
                if (tasks != null) foreach (var t in tasks) if (t != null && ReferenceEquals(R.F(t, "_targetObject"), dummy)) return "engage task of " + UName(u);
                var wss = R.F(R.F(u, "_obp"), "_weaponSystems") as IList; if (wss == null) continue;
                foreach (var w in wss)
                {
                    if (w == null || !R.B(R.F(w, "_executingEngageTask"))) continue;
                    var tk = R.F(w, "_takenEngageTask");
                    if (tk != null && ReferenceEquals(R.F(tk, "_targetObject"), dummy)) return "mount of " + UName(u);
                }
            }
            return null;
        }

        static void PfRemove(object obj)
        {
            try
            {
                MethodInfo m = null;
                for (var ty = obj.GetType(); ty != null && m == null; ty = ty.BaseType) m = ty.GetMethod("RemoveObjectFromScene", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                var ps = m.GetParameters();
                m.Invoke(obj, new object[] { false, false, Activator.CreateInstance(ps[2].ParameterType) });
                var go = ((Component)obj).gameObject; if (go) UnityEngine.Object.Destroy(go);
            }
            catch (Exception e) { Trace("point fire remove: " + e.GetType().Name + " " + e.Message); }
        }

        static void PointFireStep(float now)
        {
            if (PfDummies.Count == 0 || now < _pfNext) return;
            _pfNext = now + 1f;
            for (int i = PfDummies.Count - 1; i >= 0; i--)
            {
                var d = PfDummies[i];
                var uo = d.Obj as UnityEngine.Object;
                if (uo == null || !uo) { PfDummies.RemoveAt(i); continue; }
                if (now - d.Born < 2f) continue;                      // the order appears in the list a moment after the click
                if (PfUsed(d.Obj) != null) { d.Idle0 = -1f; continue; }
                if (d.Idle0 < 0f) { d.Idle0 = now; continue; }
                if (now - d.Idle0 < 2f) continue;                     // the order passes from the list to a mount: not one check without it
                Trace("point fire: decoy " + UName(d.Obj) + " removed");
                PfRemove(d.Obj); PfDummies.RemoveAt(i);
            }
        }
    }
}
