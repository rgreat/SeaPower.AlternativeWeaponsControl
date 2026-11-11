using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace AlternativeWeaponsControl
{
    /// <summary>
    /// [Guns] RememberTargets. The game hands every new order of the player to ALL guns with the ammunition, overwriting what they
    /// shot at, and forgets the old order. Here the guns (WeaponSystemGun, not CIWS) keep the player's orders of a unit in a list
    /// (newest first): the new order is taken by every gun as before, but a gun that cannot reach that target (range, firing
    /// sector) goes on with the newest remembered target it can reach, and returns to the newest one when it can. Cease fire
    /// (the tab's stop, the unit's Cease fire, Hold) clears the list; targets that are gone drop out of it.
    /// </summary>
    public partial class AwcPlugin
    {
        sealed class GunMem { public object Task, Target; public string Ammo; }
        sealed class RefCmp : IEqualityComparer<object> { public new bool Equals(object a, object b) { return ReferenceEquals(a, b); } public int GetHashCode(object o) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o); } }
        static readonly Dictionary<object, List<GunMem>> GunMems = new Dictionary<object, List<GunMem>>(new RefCmp());
        static readonly Dictionary<object, HashSet<object>> GunSeen = new Dictionary<object, HashSet<object>>(new RefCmp());   // the orders already put into the list (one per order, not per gun)
        static float _nextGunMem;
        static Type _tGunExact;

        void HookGunTargets(Harmony harmony, Assembly asm)
        {
            const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _tGunExact = asm.GetType("SeaPower.WeaponSystemGun");
            var cf = _tObjectBase == null ? null : _tObjectBase.GetMethod("CeaseFire", inst | BindingFlags.DeclaredOnly);
            if (cf != null) harmony.Patch(cf, postfix: Hm("GunMemCeasePostfix")); else Trace("ObjectBase.CeaseFire not found: the remembered targets are not cleared by Cease fire");
        }

        static void GunMemCeasePostfix(object __instance) { if (__instance != null) { Trace("CeaseFire of " + R.S(R.Call(__instance, "getUIDAndName"))); GunMems.Remove(__instance); GunSeen.Remove(__instance); } }

        /// <summary>Cease fire of the tab for these ammunition: forget the unit's remembered targets of it.</summary>
        internal static void ForgetTargets(object unit, ICollection<string> ammo)
        {
            List<GunMem> l;
            if (unit == null || !GunMems.TryGetValue(unit, out l)) return;
            l.RemoveAll(m => ammo == null || ammo.Contains(m.Ammo));
            if (l.Count == 0) { GunMems.Remove(unit); GunSeen.Remove(unit); }
        }

        static bool Gone(object t)
        {
            if (t == null) return true;
            var uo = t as UnityEngine.Object; if (uo != null && !uo) return true;
            try { return R.B(R.P(t, "IsDestroyed")); } catch { return false; }
        }

        static bool IsPlainGun(object ws) { return ws != null && ws.GetType() == _tGunExact; }

        /// <summary>After the game's hand-out of orders: the player's (not automatic, not tied to a launcher) orders the guns hold go to the list.</summary>
        static void RememberGunOrders(object unit)
        {
            var a = Active; if (a == null || !Enabled || a._gunMemory == null || !a._gunMemory.Value) return;
            if (!R.B(R.P(unit, "IsPlayerObject"))) return;
            var guns = R.F(R.F(unit, "_obp"), "_gunWeapons") as IList; if (guns == null) return;
            foreach (var g in guns)
            {
                if (!IsPlainGun(g) || !R.B(R.F(g, "_executingEngageTask"))) continue;
                var task = R.F(g, "_takenEngageTask");
                if (task == null || R.B(R.F(task, "_isAutoAttack")) || PinOf(task) != null) continue;
                var tgt = R.F(task, "_targetObject"); if (tgt == null) continue;
                // every gun holds the order: it goes to the list once (the guns that switched to an older target hold the older order, which must not move to the front again)
                HashSet<object> seen; if (!GunSeen.TryGetValue(unit, out seen)) { seen = new HashSet<object>(new RefCmp()); GunSeen[unit] = seen; }
                if (!seen.Add(task)) continue;
                List<GunMem> l; if (!GunMems.TryGetValue(unit, out l)) { l = new List<GunMem>(); GunMems[unit] = l; }
                string ammo = R.S(R.F(task, "_ammoId"));
                l.RemoveAll(m => ReferenceEquals(m.Target, tgt) && m.Ammo == ammo);
                l.Insert(0, new GunMem { Task = task, Target = tgt, Ammo = ammo });
                if (l.Count > 8) l.RemoveAt(l.Count - 1);
                Trace("remembered targets of " + R.S(R.Call(unit, "getUIDAndName")) + ": " + l.Count);
            }
        }

        /// <summary>Can the gun hit the target now: in range (and the direct-fire limit for aircraft) and in its firing sector.</summary>
        static bool GunCanReach(object gun, GunMem m)
        {
            try
            {
                if (Gone(m.Target)) return false;
                var tr = R.P(m.Target, "transform") as Transform; if (tr == null) return false;
                var ap = R.F(R.F(m.Task, "_ammunition"), "_ap"); if (ap == null) return false;
                var mount = R.F(gun, "_mountObject") as GameObject;
                Vector3 from = mount != null ? mount.transform.position : ((R.P(R.F(gun, "_baseObject"), "transform") as Transform) ?? tr).position;
                float max = ((Vector2)R.F(ap, "_launchRangesInUnity")).y;
                Vector3 d = tr.position - from;
                if (R.B(R.Call(m.Target, "isAirTarget"))) max = Mathf.Min(max, AirDirectFire(ap, d.y) * 1.15f);
                if (max <= 0f || d.magnitude > max) return false;
                // the game ends a gun's order at once when its side has lost the target (no contact)
                if (!R.B(R.F(gun, "_ignoreIfUndetected")) && R.Call(m.Target, "GetMapVehicle", R.F(R.F(gun, "_baseObject"), "_taskforce")) == null) return false;
                return R.B(R.Call(gun, "targetIsInFiringArc", tr.position));
            }
            catch { return false; }
        }

        static bool GunUsesAmmo(object gun, string ammo)
        {
            try
            {
                var cur = R.S(R.F(R.F(R.F(gun, "_ammoForEngage"), "_ap"), "_ammunitionFileName"));
                return cur == ammo || R.I(R.Call(gun, "getLoadedAmmoCount", ammo)) > 0 || R.I(R.Call(gun, "getMagazineAmmoCount", ammo)) > 0;
            }
            catch { return false; }
        }

        // trace only: a gun that has held an order for 20 s and is still not shooting - what it waits for
        static void GunWaitTrace(object g, object unit, object cur)
        {
            if (!_debug) return;
            try
            {
                double age = MissionTime() - Convert.ToDouble(R.F(g, "_engageStartTime"));
                if (age < 20) return;
                // the game stamps _deviationCheckTime at (about) every salvo: a gun that fired in the last 20 s is working, not waiting
                double sinceShot = MissionTime() - Convert.ToDouble(R.F(g, "_deviationCheckTime"));
                if (sinceShot < 20) return;
                var fc = R.Call(g, "GetFireControlSensor");
                string st = R.S(R.F(g, "_engageState"));
                if (st == "Firing" || st == "Shooting") return;
                TraceLimited("gw" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(g), 20f,
                    "gun " + R.S(R.F(g, "_systemName")) + " of " + R.S(R.Call(unit, "getUIDAndName")) + " waits " + (int)age + " s (last salvo " + (sinceShot > 3600 ? "never" : (int)sinceShot + " s ago") + ") for " + R.S(R.Call(cur, "getUIDAndName")) +
                    ": state=" + st + " reload=" + R.B(R.F(g, "_isReloading")) + " inArc=" + R.B(R.Call(g, "targetIsInFiringArc", ((Transform)R.P(cur, "transform")).position)) +
                    " director=" + (fc == null ? "none(local)" : (Holds(fc, cur) ? "holds" : "busy")) + " ignoreUndet=" + R.B(R.F(g, "_ignoreIfUndetected")) + GunWaitDetail(g, unit, cur));
            }
            catch { }
        }

        /// <summary>Trace only: a copy of the checks in GunBallistics.TrySolveSurface (the game's silent "no firing solution"): where the lead point of the target falls against the fire table.</summary>
        static string SolveWhy(object g, object cur, object ap, Transform tr)
        {
            var sb = new System.Text.StringBuilder(" | solve:");
            try
            {
                var gb = _tObjectBase.Assembly.GetType("SeaPower.GunBallistics");
                var table = gb.GetMethod("GetTable", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(null, new[] { ap });
                if (table == null) return sb.Append(" no table").ToString();
                var barrels = R.F(g, "_barrels") as IList; var b0 = barrels[0];
                var go = R.F(b0, "_gunObject") as GameObject;
                Vector3 own = go.transform.position + go.transform.rotation * (Vector3)R.F(b0, "_spawnPosition");
                float muz = R.Fl(R.F(table, "_muzzleVelocity")), maxR = R.Fl(R.F(table, "_maxRange")), minR = R.Fl(R.F(table, "_minRange"));
                var vel = (Vector3)R.P(cur, "UnityVelocityVector"); vel.y = 0f;
                sb.Append(" tableMax=").Append((maxR * AimNmPerUnit).ToString("0.00")).Append("nm targetSpeed=").Append((vel.magnitude / 0.0076554087f).ToString("0")).Append("kn");
                var trySample = gb.GetMethod("TrySample", BindingFlags.Static | BindingFlags.NonPublic);
                float num = (tr.position - own).magnitude / Mathf.Max(0.7f * muz, 0.001f);
                for (int i = 0; i < 4; i++)
                {
                    Vector3 v2 = tr.position + vel * num - own; float y = v2.y; v2.y = 0f; float mag = v2.magnitude;
                    sb.Append(" it").Append(i).Append(" lead=").Append((mag * AimNmPerUnit).ToString("0.00")).Append("nm");
                    if (mag < 0.0001f || mag > maxR) { sb.Append(" -> BEYOND TABLE"); break; }
                    if (Mathf.Abs(y) > 0.15f * mag) { sb.Append(" -> TOO STEEP"); break; }
                    var a1 = new object[] { table, mag, null };
                    if (!(bool)trySample.Invoke(null, a1)) { sb.Append(" -> NO SAMPLE"); break; }
                    float ang = R.Fl(R.F(a1[2], "_impactAngleRad"));
                    float num2 = mag + y / Mathf.Max(Mathf.Tan(ang), 0.05f);
                    var a2 = new object[] { table, Mathf.Clamp(num2, minR + 0.001f, maxR), null };
                    if (!(bool)trySample.Invoke(null, a2)) { sb.Append(" -> NO SAMPLE2"); break; }
                    num = R.Fl(R.F(a2[2], "_timeOfFlight")) - (num2 - mag) / Mathf.Max(R.Fl(R.F(a2[2], "_impactHorizontalSpeed")), 0.1f);
                    if (num <= 0f) { sb.Append(" -> NEGATIVE FLIGHT"); break; }
                    if (i == 3) sb.Append(" -> ok ").Append(num.ToString("0")).Append("s");
                }
            }
            catch (Exception e) { sb.Append(" failed: ").Append(e.Message); }
            return sb.ToString();
        }

        /// <summary>Trace only: the conditions inside the game's WeaponSystemGun.AlignTurret that keep a gun in AligningLauncher (each silently returns "not aligned").</summary>
        static string GunWaitDetail(object g, object unit, object cur)
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                var vwp = R.F(g, "_vwp");
                var barrels = R.F(g, "_barrels") as IList;
                var tr = R.P(cur, "transform") as Transform;
                var ap = R.F(R.F(g, "_ammoForEngage"), "_ap");
                if (barrels != null && barrels.Count > 0 && tr != null && ap != null)
                {
                    var go = R.F(barrels[0], "_gunObject") as GameObject;
                    float max = ((Vector2)R.F(ap, "_launchRangesInUnity")).y, dist = (tr.position - go.transform.position).magnitude;
                    sb.Append(" | dist=").Append((dist * AimNmPerUnit).ToString("0.00")).Append("nm max=").Append((max * AimNmPerUnit).ToString("0.00")).Append("nm").Append(dist > max ? " BEYOND" : "");
                    sb.Append(" y=").Append(tr.position.y.ToString("0.000"));
                }
                sb.Append(" solution=").Append(R.B(R.F(g, "_solutionCalculated"))).Append(" flight=").Append(Convert.ToDouble(R.F(g, "_projectileToTargetTime")).ToString("0.0"));
                var rd = R.F(g, "_rotationDeltas"); var th = R.F(g, "_rotationThesholds");
                if (rd is Vector2 && th is Vector2) sb.Append(" rotDelta=").Append(((Vector2)rd).x.ToString("0.0")).Append("/").Append(((Vector2)rd).y.ToString("0.0")).Append(" thr=").Append(((Vector2)th).x.ToString("0.0")).Append("/").Append(((Vector2)th).y.ToString("0.0"));
                var mo = R.F(vwp, "_mountObject") as GameObject;
                var arcs = R.F(vwp, "_firingArcs") as IList;
                if (mo != null) sb.Append(" mountYaw=").Append(mo.transform.localEulerAngles.y.ToString("0"));
                // where the mount should point (the solution's bearing from the ship's bow, same sign as the mount's yaw) and how long ago AlignTurret last got past its early exits
                var bt = R.P(R.F(g, "_baseObject"), "transform") as Transform; var svv = R.F(g, "_solutionVector");
                if (bt != null && svv is Vector3)
                {
                    Vector3 f = bt.forward, sf = (Vector3)svv; f.y = 0f; sf.y = 0f;
                    if (f.sqrMagnitude > 1e-6f && sf.sqrMagnitude > 1e-6f) sb.Append(" want=").Append(Vector3.SignedAngle(f, sf, Vector3.up).ToString("0"));
                }
                sb.Append(" idle=").Append((MissionTime() - Convert.ToDouble(R.F(g, "_lastUsageTime"))).ToString("0.0")).Append("s rest=").Append(R.B(R.F(g, "_isInRestPosition")));
                if (arcs != null && arcs.Count > 0 && arcs[0] is Vector2) sb.Append(" arc=").Append(((Vector2)arcs[0]).x.ToString("0")).Append("..").Append(((Vector2)arcs[0]).y.ToString("0"));
                sb.Append(" degPerSec=").Append(Convert.ToSingle(R.F(vwp, "_horizontalDegreesPerSecond")).ToString("0.0"));
                double now = MissionTime(), start = Convert.ToDouble(R.F(g, "_targetAcquisitionStartTime"));
                var rt = _tObjectBase.Assembly.GetType("SeaPower.ReactionTime");
                var acq = rt == null ? null : rt.GetMethod("Acquisition", BindingFlags.Static | BindingFlags.Public);
                float need = acq == null ? -1f : Convert.ToSingle(acq.Invoke(null, new object[] { R.F(g, "_baseObject"), vwp, cur }));
                sb.Append(" acq=").Append((now - start).ToString("0")).Append("/").Append(need.ToString("0.0")).Append("s");
                var gt = _tObjectBase.Assembly.GetType("SeaPower.GameTime");
                var tcp = gt == null ? null : gt.GetProperty("TimeCompression", BindingFlags.Static | BindingFlags.Public);
                if (tcp != null) sb.Append(" tc=x").Append(Convert.ToSingle(tcp.GetValue(null, null)).ToString("0"));
                // vertical aim: the solution's elevation, the mount's limits and speed, the container's current angle
                var sv = R.F(g, "_solutionVector"); var ea = R.F(vwp, "_elevationArc"); var cb = R.F(vwp, "_containerBaseObject") as GameObject;
                if (sv is Vector3 && ea is Vector2 && cb != null)
                {
                    float cx = cb.transform.localEulerAngles.x; if (cx > 180f) cx -= 360f;
                    sb.Append(" elev: solution=").Append((Mathf.Asin(((Vector3)sv).normalized.y) * 57.29578f).ToString("0.0")).Append(" arc=").Append(((Vector2)ea).x.ToString("0")).Append("..").Append(((Vector2)ea).y.ToString("0"))
                      .Append(" now=").Append((-cx).ToString("0.0")).Append(" vdps=").Append(Convert.ToSingle(R.F(vwp, "_verticalDegreesPerSecond")).ToString("0.0"));
                }
                if (barrels != null && barrels.Count > 0 && tr != null && ap != null && !R.B(R.Call(cur, "isAirTarget"))) sb.Append(SolveWhy(g, cur, ap, tr));
            }
            catch (Exception e) { sb.Append(" detail failed: ").Append(e.Message); }
            return sb.ToString();
        }

        static void GunTargetsStep(float now)
        {
            if (now < _nextGunMem) return;
            _nextGunMem = now + 0.5f;
            var a = Active; if (a == null || !Enabled || GunMems.Count == 0) return;
            if (a._gunMemory == null || !a._gunMemory.Value) { GunMems.Clear(); return; }
            List<object> dead = null;
            foreach (var kv in GunMems)
            {
                var unit = kv.Key; var l = kv.Value;
                if (Gone(unit)) { (dead ?? (dead = new List<object>())).Add(unit); continue; }
                l.RemoveAll(m => Gone(m.Target));
                if (l.Count == 0) { (dead ?? (dead = new List<object>())).Add(unit); continue; }
                var guns = R.F(R.F(unit, "_obp"), "_gunWeapons") as IList; if (guns == null) continue;
                foreach (var g in guns)
                {
                    if (!IsPlainGun(g)) continue;
                    bool exec = R.B(R.F(g, "_executingEngageTask"));
                    var taken = exec ? R.F(g, "_takenEngageTask") : null;
                    if (exec && (taken == null || R.B(R.F(taken, "_isAutoAttack")) || PinOf(taken) != null)) continue;   // the AI's or a launcher's own order
                    var cur = exec ? R.F(g, "_targetObject") : null;
                    if (exec && cur != null) GunWaitTrace(g, unit, cur);
                    int ci = -1, bi = -1;
                    for (int i = 0; i < l.Count; i++)
                    {
                        var m = l[i];
                        if (ci < 0 && cur != null && ReferenceEquals(m.Target, cur)) ci = i;
                        if (bi < 0 && GunUsesAmmo(g, m.Ammo) && GunCanReach(g, m)) bi = i;
                    }
                    if (bi < 0) continue;                                         // nothing it can hit: it stays as it is
                    if (ci >= 0 && ci <= bi && GunCanReach(g, l[ci])) continue;    // already on the newest one it can hit
                    if (ci < 0 && exec && cur != null) { var cm = new GunMem { Task = taken, Target = cur }; if (GunCanReach(g, cm)) continue; }
                    GunTake(g, l[bi]);
                    Trace("gun " + R.S(R.F(g, "_systemName")) + " of " + R.S(R.Call(unit, "getUIDAndName")) + " goes on to " + R.S(R.Call(l[bi].Target, "getUIDAndName")));
                }
            }
            if (dead != null) foreach (var u in dead) { GunMems.Remove(u); GunSeen.Remove(u); }
        }

        // ------------------------------------------------------------------ orders loaded from a save "from the future"
        // The game saves an order's creation time (mission time), but GameTime.LoadStateFromFile reads the mission time and drops it: the clock goes on from
        // wherever it was. A loaded order with a creation time ahead of the clock is "newer" than every order given later, and the game breaks off a mount's
        // engagement for a new order only when the mount's order is OLDER (HandleEngageTasks): such mounts could never be re-targeted by the player.
        // Here a creation time ahead of the clock is set back to just before it.
        static void FixFutureTasks(object unit)
        {
            try
            {
                // (called at the start of the game's handling of the unit's orders: nothing to do while its list is empty - the common case)
                var tasks = R.F(unit, "_currentEngageTasks") as IList;
                if (tasks == null || tasks.Count == 0 || !R.B(R.P(unit, "IsPlayerObject"))) return;
                double now = MissionTime(); if (now <= 0) return;
                int n = 0; double max = 0;
                Action<object> fix = t =>
                {
                    if (t == null) return;
                    double c = Convert.ToDouble(R.P(t, "CreationTime"));
                    if (c <= now) return;
                    R.SetF(t, "_creationTime", now - 1.0);
                    n++; if (c > max) max = c;
                };
                foreach (var t in tasks) fix(t);
                HumanFirst(tasks);
                var wss = R.F(R.F(unit, "_obp"), "_weaponSystems") as IList;
                if (wss != null) foreach (var ws in wss) if (ws != null && R.B(R.F(ws, "_executingEngageTask"))) fix(R.F(ws, "_takenEngageTask"));
                if (n > 0) TraceLimited("future" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(unit), 30f, "orders of " + UName(unit) + ": creation time ahead of the clock (" + (int)max + " vs " + (int)now + ") set back for " + n + " order(s)");
            }
            catch (Exception e) { TraceLimited("future-err", 10f, "future orders: " + e.Message); }
        }

        // ------------------------------------------------------------------ a click on a contact: all guns
        // AI.AutoAttackByClick (right click on a hostile contact, own ship selected) orders ONE gun ammunition: SelectAmmunition takes, in the game's
        // order, the next one whose maximum range is longer than the previous pick's AND that has a mount usable now (WeaponSelector.isWeaponSystemUsable
        // with sensors: a gun whose director is destroyed, or one that is reloading, is not usable) - the calibre of such guns is passed over.
        // With the option every gun ammunition the ship has left that reaches the target (range only: the firing sector does not matter) gets the order too.
        static MethodInfo _miAmmoForTarget, _miMaxRange;

        static void ClickAttackPostfix(object __instance, object targetObject, bool showPlayerFeedback)
        {
            var a = Active; if (a == null || !Enabled || a._clickAllGuns == null || !a._clickAllGuns.Value || targetObject == null || !showPlayerFeedback) return;
            try
            {
                var unit = R.F(__instance, "_baseObject");
                if (unit == null || !R.B(R.P(unit, "IsPlayerObject")) || !R.Is(unit, _tVessel)) return;
                var tasks = R.F(unit, "_currentEngageTasks") as IList; if (tasks == null) return;
                if (_miAmmoForTarget == null)
                {
                    _miAmmoForTarget = __instance.GetType().GetMethod("AmmunitionForTarget", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    _miMaxRange = unit.GetType().GetMethod("GetMaxRangeForAmmo", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }
                if (_miAmmoForTarget == null || _miMaxRange == null) return;
                var projectile = Enum.ToObject(_miAmmoForTarget.GetParameters()[1].ParameterType, 1);   // Ammunition.Type.Projectile
                var list = _miAmmoForTarget.Invoke(__instance, new[] { targetObject, projectile }) as IList; if (list == null) return;
                var up = ((Component)unit).transform.position; var tp = ((Component)targetObject).transform.position;
                float dist = (tp - up).magnitude;
                int added = 0; string names = "";
                foreach (var am in list)
                {
                    var ap = R.F(am, "_ap"); if (ap == null) continue;
                    string id = R.S(R.F(ap, "_ammunitionFileName"));
                    bool have = false;
                    foreach (var t in tasks) if (t != null && R.S(R.F(t, "_ammoId")) == id && ReferenceEquals(R.F(t, "_targetObject"), targetObject)) { have = true; break; }
                    if (have) continue;
                    var rng = (Vector2)R.F(ap, "_launchRangesInUnity");
                    if (dist < rng.x || dist > Convert.ToSingle(_miMaxRange.Invoke(unit, new[] { am, targetObject }))) continue;
                    var task = Activator.CreateInstance(tasks.GetType().GetGenericArguments()[0], id, targetObject, unit, 1, 1000, false, false);
                    // an extra order waits in the list until a gun is free for it, and the game breaks off the guns of ANOTHER target for a waiting order that is newer
                    // than theirs (HandleEngageTasks) - with two targets ordered one after the other the waiting extras of the first kept tearing the guns off the second.
                    // Creation time 0: older than every order, so an extra only takes free guns; the click's own order (the game's) still takes over as before.
                    R.SetF(task, "_creationTime", 0.0);
                    R.Call(unit, "AddEngageTask", task);
                    added++; names += " " + id;
                }
                if (added > 0) Trace("click on " + UName(targetObject) + ": " + added + " more gun ammunition ordered:" + names);
            }
            catch (Exception e) { Trace("click attack: " + e.Message); }
        }

        /// <summary>The player's orders (not the AI's) are the highest priority: priority 0 and first in the list, whatever way they were made (the Weapons tab's Fire,
        /// a click on a contact = AI.AutoAttackByClick, which makes the task with the default priority 1000 - the lowest). The AI takes a mount over only with a
        /// better priority than the mount's.</summary>
        static void HumanFirst(IList tasks)
        {
            List<object> up = null;
            foreach (var t in tasks)
                if (t != null && !R.B(R.F(t, "_isAutoAttack")) && R.I(R.F(t, "_priority")) != 0) (up ?? (up = new List<object>())).Add(t);
            if (up == null) return;
            foreach (var t in up) { R.SetF(t, "_priority", 0); tasks.Remove(t); }
            for (int i = up.Count - 1; i >= 0; i--) tasks.Insert(0, up[i]);
        }

        // ------------------------------------------------------------------ trace only: what becomes of the player's orders
        // An order of the player (engage task, not automatic) is followed from its birth: taken by mounts / dropped by the game without
        // being taken / still waiting (every 10 s, with the state of the mounts that have the ammunition).
        sealed class OrderRec { public object Unit, Task, Target; public string Ammo; public double Born; public double Next; }
        static readonly List<OrderRec> OrderRecs = new List<OrderRec>();
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, object> OrderSeen = new System.Runtime.CompilerServices.ConditionalWeakTable<object, object>();
        static MethodInfo _miUsable;

        static string UName(object o) { try { return R.S(R.Call(o, "getUIDAndName")); } catch { return "?"; } }

        static void OrderWatch(object unit)
        {
            if (!_debug || unit == null) return;
            try
            {
                if (!R.B(R.P(unit, "IsPlayerObject"))) return;
                var tasks = R.F(unit, "_currentEngageTasks") as IList;
                double now = MissionTime();
                if (tasks != null)
                    foreach (var t in tasks)
                    {
                        object tmp;
                        if (t == null || R.B(R.F(t, "_isAutoAttack")) || OrderSeen.TryGetValue(t, out tmp)) continue;
                        OrderSeen.Add(t, new object());
                        var r = new OrderRec { Unit = unit, Task = t, Target = R.F(t, "_targetObject"), Ammo = R.S(R.F(t, "_ammoId")), Born = now, Next = now + 3 };
                        OrderRecs.Add(r);
                        Trace("order: " + UName(unit) + " " + r.Ammo + " -> " + (r.Target == null ? "position" : UName(r.Target)) + " priority=" + R.I(R.F(t, "_priority")) + " pinned=" + (PinOf(t) != null) + " tasks in list=" + tasks.Count);
                    }
                var wss = R.F(R.F(unit, "_obp"), "_weaponSystems") as IList;
                for (int i = OrderRecs.Count - 1; i >= 0; i--)
                {
                    var r = OrderRecs[i];
                    if (!ReferenceEquals(r.Unit, unit)) continue;
                    int holders = 0;
                    if (wss != null) foreach (var ws in wss) if (ws != null && R.B(R.F(ws, "_executingEngageTask")) && ReferenceEquals(R.F(ws, "_takenEngageTask"), r.Task)) holders++;
                    if (holders > 0) { Trace("order taken: " + UName(unit) + " " + r.Ammo + " -> " + (r.Target == null ? "position" : UName(r.Target)) + " by " + holders + " mount(s) after " + (int)(now - r.Born) + " s"); OrderRecs.RemoveAt(i); continue; }
                    bool inList = tasks != null && tasks.Contains(r.Task);
                    if (!inList) { Trace("order DROPPED without being taken: " + UName(unit) + " " + r.Ammo + " -> " + (r.Target == null ? "position" : UName(r.Target)) + " after " + (int)(now - r.Born) + " s" + OrderDetail(unit, r, wss)); OrderRecs.RemoveAt(i); continue; }
                    if (now >= r.Next) { r.Next = now + 10; Trace("order waits " + (int)(now - r.Born) + " s: " + UName(unit) + " " + r.Ammo + " -> " + (r.Target == null ? "position" : UName(r.Target)) + OrderDetail(unit, r, wss)); }
                }
            }
            catch (Exception e) { TraceLimited("orderwatch", 10f, "order watch: " + e.Message); }
        }

        static string OrderDetail(object unit, OrderRec r, IList wss)
        {
            var sb = new System.Text.StringBuilder(" |");
            try
            {
                var tgt = r.Target;
                if (tgt != null)
                {
                    var ai = R.F(unit, "_ai");
                    sb.Append(" processed=").Append(R.B(R.Call(ai, "IsProcessedForEngagement", tgt))).Append(" autoOnly=").Append(R.B(R.Call(ai, "IsAutomaticMountOnlyTarget", tgt)));
                    var up = ((Component)unit).transform.position; var tp = ((Component)tgt).transform.position;
                    sb.Append(" dist=").Append(((tp - up).magnitude * AimNmPerUnit).ToString("0.0")).Append("nm");
                }
                if (_miUsable == null)
                    foreach (var m in _tObjectBase.Assembly.GetType("SeaPower.WeaponSelector").GetMethods(BindingFlags.Static | BindingFlags.Public))
                        if (m.Name == "isWeaponSystemUsable" && m.GetParameters()[1].ParameterType == typeof(string)) _miUsable = m;
                int shown = 0, withAmmo = 0;
                double born = Convert.ToDouble(R.P(r.Task, "CreationTime"));
                if (wss != null)
                    foreach (var ws in wss)
                    {
                        if (ws == null) continue;
                        bool has = false;
                        try { has = R.I(R.Call(ws, "getLoadedAmmoCount", r.Ammo)) > 0 || R.I(R.Call(ws, "getMagazineAmmoCount", r.Ammo)) > 0 || R.S(R.F(R.F(R.F(ws, "_ammoForEngage"), "_ap"), "_ammunitionFileName")) == r.Ammo; } catch { }
                        if (!has) continue;
                        withAmmo++;
                        if (shown >= 8) continue;
                        shown++;
                        bool exec = R.B(R.F(ws, "_executingEngageTask"));
                        var taken = exec ? R.F(ws, "_takenEngageTask") : null;
                        var cur = exec ? R.F(ws, "_targetObject") : null;
                        sb.Append(" [").Append(R.S(R.F(ws, "_systemName"))).Append(": ").Append(exec ? "busy with " + (cur == null ? "position" : UName(cur)) + (taken == null ? " (no task)" : R.B(R.F(taken, "_isAutoAttack")) ? " (auto, " : " (order, ") + (taken == null ? "" : (Convert.ToDouble(R.P(taken, "CreationTime")) >= born ? "newer" : "older") + ")") : "free")
                          .Append(" reload=").Append(R.B(R.F(ws, "_isReloading"))).Append(" empty=").Append(R.B(R.F(ws, "_isEmpty")));
                        try { if (_miUsable != null && tgt != null) sb.Append(" usable=").Append(R.B(_miUsable.Invoke(null, new object[] { ws, r.Ammo, tgt, true, true, true }))); } catch { }
                        try { if (tgt != null) sb.Append(" inArc=").Append(R.B(R.Call(ws, "targetIsInFiringArc", ((Component)tgt).transform.position))); } catch { }
                        sb.Append(']');
                    }
                sb.Append(" mounts with the ammunition=").Append(withAmmo).Append(" clock=").Append((int)MissionTime()).Append(" order born=").Append((int)born);
            }
            catch (Exception e) { sb.Append(" detail: ").Append(e.Message); }
            return sb.ToString();
        }

        /// <summary>The gun takes the order as the game's hand-out of orders gives it to a gun.</summary>
        static void GunTake(object g, GunMem m)
        {
            var t = m.Task;
            R.SetF(g, "_takenEngageTask", t);
            R.SetF(g, "_executingEngageTask", true);
            R.SetF(g, "_engageTaskPriority", R.F(t, "_priority"));
            R.SetF(g, "_ammoForEngage", R.F(t, "_ammunition"));
            R.SetF(g, "_targetObject", m.Target);
            R.SetF(g, "_currentTargetPoint", R.F(t, "_curentTargetPoint"));
            R.SetF(g, "_targetPosition", R.P(t, "TargetPosition"));
            R.SetF(g, "_alignmentUID", R.F(t, "_uid"));
            R.SetF(g, "_engageStartTime", MissionTime());
            R.SetF(g, "_engageDelay", 0f);
        }
    }
}
