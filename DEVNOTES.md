# Alternative Weapons Control - dev notes (for Claude)

Condensed 30.10 (version 1.3.0): current design and decisions only. The long history (Russian, step-by-step traces, abandoned attempts) is in git: `git show b475635:DEVNOTES.md`.

Sea Power mod: a Weapons tab in the Unit Status window (own ships and submarines, ground units, aircraft): launchers, targets, ammunition, fire-control channels, per-domain auto engagement switches, Fire / Cease fire / Reload; gun fixes; SAM readiness. BepInEx 5 + Harmony, loaded through Anchor Chain (Workshop 3380210757). Built like Alternative Damage Model: settings = gear on the tab button, legend = (i). Game access by reflection (class `R` in WeaponsModel.cs: fields/properties of any visibility, methods by name with overload matching, type -> name -> member caches); Noesis is referenced directly.


## Как работать с этими заметками (для Claude: не раздувать контекст)
- Этот файл — только основное (сборка, правила, архитектура, подводные камни). **Не читать целиком чужие разделы**: подробная история функций — в `DEVNOTES-ARCHIVE.md`.
- Нужная тема: `grep -n "ключевое слово" DEVNOTES-ARCHIVE.md`, затем `sed -n 'N,Mp'` по номерам строк из индекса ниже (читать только этот раздел).
- Общий код окон настроек (SettingsKit/Hub, вкладки, поиск, Ctrl+C, язык) описан в `seapower.alternativebase/README.md`, не здесь.
- В чате: большие файлы только через `grep`/`sed -n`/`Read` с offset+limit; логи — `grep`/`tail`, не целиком; вывод команд ограничивать (`| cut -c1-200 | head`).
- Новые записи: основное — сюда (коротко); подробности и история отладки — в архив, в конец, отдельным разделом.


## Состояние релиза
- 2026-11-19: версия 1.9.0 подготовлена (csproj, PluginVersion, ChangeList.txt, ChangeList-1000.txt = текст Change Log для Steam, README). Не выложена в Steam, пока пользователь не скажет; не закоммичена.

## Индекс архива (строка в DEVNOTES-ARCHIVE.md — раздел; размер в символах)
- 3 — Auto engagement (WeaponsAi.cs) (850)
- 9 — Per-launcher target (Fire pin) (1981)
- 15 — Fire-control channels (1936)
- 21 — Ships: chasing ([Ships]) (470)
- 24 — Guns (GunFixes.cs, AmmunitionFix.cs) (7255)
- 35 — Remembered gun targets (GunTargets.cs, `[Guns] RememberTargets`, on) (1488)
- 38 — Fire zones (Zones.cs, [UI] FireShowsZones) (2343)
- 46 — On Free: unidentified aircraft (FreeAir.cs) (1313)
- 51 — SAM readiness (WarmUp.cs, WeaponsView WarmButton) (4097)
- 62 — View (WeaponsView.cs, Noesis in code) (3922)
- 75 — Settings window (SettingsWindow.cs, SettingsAmmo.cs, Loc.cs) (3063)
- 85 — Changes 30.10 (1.3.0 since 1.2.2) (525)
- 88 — Changes (1.4.0 since 1.3.0) (567)
- 91 — Startup diagnostics (1.5.0) (969)
- 98 — Gun/director tooltip (live) (317)
- 101 — Lost directors: the naked eye is not a director (898)
- 104 — Salvo trace (debug only) (573)
- 107 — WW2 preset made less accurate (1.5.0) (1443)
- 110 — Changes (1.5.0 since 1.4.0) (887)
- 113 — Custom accuracy and power (ammunition table) (1155)
- 117 — Game 0.9.0 compatibility review (2026-11-12; decompile in /home/claude/gsrc090, 0.8.7 in gsrc08 (8820)
- 155 — Fire at a point of the map (Guns/PointFire, src/PointFire.cs) (2121)
- 164 — Switch cycle with Standing Orders (WeaponsModel.cs `Cycle`, `ToOrders`) (1368)
- 169 — 1.6.0: game 0.9.2 and the loader (1662)
- 174 — Standing Orders that force fire (chip state Forced) (999)
- 182 — Free / Hold buttons overridden by Standing Orders (422)
- 186 — On adds WeaponsFree, not WeaponsTight (bug report: ships fired anti-surface weapons on Weapons  (898)
- 191 — Rules not found: no auto fire at Tight (trace of a Tight test: "AutoEngageRules were not found" (510)
- 196 — Release game build: Standing Orders as doctrine switches (no AutoEngageRules) (1474)
- 201 — Fire zones: one fill for the same arcs (1244)
- 206 — Strict EMCON (1299)
- 209 — SAM at Weapons Tight: only in the no-escape zone (`Fire/SamTightNezOnly`, off) (1189)
- 213 — Launcher statuses: assessing, waiting for sensor, sensor inoperable (2479)
- 218 — Performance notes (2133)
- 227 — Strict EMCON: gun-only radar stations as optical directors (2764)
- 235 — Attack orders on the map (WaypointFix.cs) (1253)
- 239 — "On Tight: engage hostile aircraft" in the beta (TightAir.cs) (2323)
- 248 — Point fire: the guns broke the order off (trace 18.11, Sverdlov) (713)
- 251 — Game beta 0.9.3-261116b vs 0.9.2 (decompile /home/claude/gsrc093; game files repo e1571a8) (7057)
- 265 — Chip hints: the Standing Orders rules that apply (RuleTip.cs, beta only) (1434)
- 272 — Weapons that no Standing Orders rule covers (`Fire/UncoveredWeapons`, Uncovered.cs) (4137)
- 285 — Settings window: one-line tab strip and screenshots of the tabs (settings kit, after the last r (1122)
- 288 — Settings window frame and row editors (settings kit: SettingsKit.cs, after the last release) (2467)
- 296 — Switch between the mods' settings windows (after the last release) (1433)
- 302 — Tabs, search, revert (kit) (432)
- 306 — Ctrl+C copies the text under the mouse (kit) (764)
- 309 — One shared Language (hub + kit) (1056)

## Build in the sandbox
1. `apt-get update && apt-get install -y dotnet-sdk-8.0`.
2. `add_repo` (owner rgreat, private): SeaPower.AlternativeWeaponsControl, SeaPower.GameFiles (read). Clone into `/home/claude/<lowercase name>`, one at a time, ~10 min timeout. Git remote URL has no `.git` suffix.
3. `/home/claude/game/Managed` <- `SeaPower.GameFiles/Managed/*.dll`; `/home/claude/game/bepinex` <- BepInEx_win_x64_5.4.23.4.zip (github.com/BepInEx/BepInEx/releases/download/v5.4.23.4); `/home/claude/game/ac` <- github.com/SeaPower-Modders/AnchorChain/releases/download/V1.1.0/dev.zip. An empty `nuget.config` in the repo is required.
4. `/home/claude/tools/build_awc.sh [repoDir] [zip]` (not in the repo): `dotnet build -c Release -p:ManagedDir=... -p:BepInExCore=... -p:AnchorChainDll=...`, then a zip WITHOUT a root folder (Release/, src/, csproj, nuget.config, README*, ChangeList.txt, PUBLISHING.md, DEVNOTES.md) -> `/home/claude/out/AlternativeWeaponsControl.zip` (keep this name). The build copies the dll into `Release/`; `BuildStamp` (UTC `yyyyMMdd.HHmm`) goes to the log.
5. Localization: `/home/claude/tools/gen_lang.py` (`cp help.json /tmp/help.json` first) turns `ru.json` + `langs/<code>.txt` + `langs/help_<code>.txt` into `Release/lang/*.ini` (en, ru, cn, de, es, fr, ko, ja, vn). `langs/<code>.txt` lines align by position with the non-help keys of `ru.json`; new keys go after the last non-help key and are appended to every `langs/*.txt`. "missing" for `count.format` and `unit.UI/SamReadyMinutes` is expected, `PLACEHOLDER` is not.
6. Version: `PluginVersion` (AwcPlugin.cs) and `<Version>` in the csproj - change both. Currently 1.5.0.
7. Game sources: 0.8.7 decompile in `/home/claude/gsrc087`, game files in `/home/claude/seapower.gamefiles`.

## User preferences (also in memory)
Talk Russian, short. Docs English only. One cfg switch per feature. No routine boilerplate in reports. Edit sources / commit / release only on request; commit and push together. Warn before a change that breaks a supported game version (0.8.6 and 0.8.7 are both supported). Deliver only the mod zip. List every new GUI option in release reports. User change list <= ~1000 chars (`ChangeList-1000.txt`).

## Compatibility
`_info.ini` ApproximateVersion=0.8.6; 0.8.7 supported. A missing game member = soft failure of that feature (warning in the log). Specifics for 0.8.7:
- `WeaponSelector.GetWeaponSystemsForAmmunition` has a 5-parameter overload `(obj, pos, ammoId, target, List vwsList) : bool` that nearly all calls use; exactly one is patched (new if present: `SelectorPostfix5`, else `SelectorPostfix`), logic in `SelectorCore`.
- `MissileSimulator.GetAccelerationTimes` has two overloads: `GetMethod` by argument types.
- `WeaponSystem.targetIsInFiringArc` is virtual; the CIWS override calls base only for surface units.
- The 2D firing wedge is drawn by `WeaponArcSegmentViewModel.Render` (see Zones).
- Obsolete duplicate files must not return to the repo (`Diagnostics.cs`, `Flash.cs`, `Optics.cs` were deleted).

## Structure (src/)
- `AwcPlugin.cs`: plugin, .cfg (`StreamingAssets/user/AlternativeWeaponsControl/AlternativeWeaponsControl.cfg`; an external edit is picked up 0.5 s after the write), tick in the Mods menu (`ModState`, LoadScreen / FileManager.SaveDirectories), hook installation, tab in UnitStatusViewModel. `Trace` / `TraceLimited(key, sec, msg)` (Debug only, `BepInEx/AlternativeWeaponsControl.trace.txt`). At the end of `TryPatch`: `SettingsHub.Register(...)` + `Install(...)` in their own try/catch.
- `AnchorChainEntry.cs`: entry point + the Mods-menu tick (same as ADM).
- `TabHeader.cs`: the tab button "Weapons" + (i) legend + gear. Tab buttons are RadioButtons with DataContext = UnitStatusTabViewModel; ours has our tab as DataContext; its TextBlock = Run(text) + InlineUIContainer(icons). Searched every 0.5 s until found; redrawn if the game rewrites the text.
- `SettingsWindow.cs` (+ `SettingsAmmo.cs`): see Settings window.
- `SettingsHub.cs`: shared hub, identical in every rgreat mod (canonical copy in Alternative Special Effects `src/SettingsHub.cs`); never edit here. Menu entries are installed after the load screen ends.
- `WeaponsModel.cs` (launchers, domains, switches, Standing Orders, save), `WeaponsAi.cs` (auto engagement, per-launcher target, channels), `WeaponsView.cs` (tab UI), `WarmUp.cs` (SAM readiness), `FreeAir.cs`, `Zones.cs`, `GunFixes.cs`, `AmmunitionFix.cs`, `Loc.cs`.
- Unit Status windows: postfix of the UnitStatusView constructor (weak list); every frame (Update, mod on) for visible ones: the tab button and the content host (`HostStep`: the first element with Visibility bound to "...Tab.IsSelected", its Panel gets our Border with our view). Hosts are kept in a ConditionalWeakTable `Hosts` (a Noesis Tag does not work: the tab went empty); when the game builds a new window with the same status the content is taken from the old host.
- With Smart Windows (now Alternative Game Interface): its Reference tab is kind 5, ours 6; hooks on the same UnitStatusViewModel methods do not clash. Mod off in the Mods menu: tab unavailable, AI / fire hooks change nothing (`Enabled`).

## Model (WeaponsModel.cs)
- Launchers = `_obp._weaponSystems` (index = settings key) without Chaff / Noisemaker / Hardpoint(except aircraft) / UNKNOWN; name = `DisplayName.Value`; ammunition = `_associatedAmmunitionNames`. Section by `_weaponType` (CIWS, Gun) and the first ammunition (AAW -> SAM; ASuW -> ANTI-SHIP; ASW / RBU / ASROC / torpedo vs submarines -> ASW; surface-only torpedo -> ANTI-SHIP; land strike only -> LAND ATTACK; `AmmoDomains` drops the surface domain when `_canNotAttackTypes` has Vessel and the ammunition has `_landAttack`, e.g. Tomahawk RGM-109C: TargetType=ASuW but land only -> LAND ATTACK). Group = section + name + ammunition set (x N). The group title (the draggable section card header, `MakeTitle` / `TitleText`, refreshed in `Refresh` so the option acts at once; `[UI] SectionTargetTypes`, off, only for the extras of SAMs) gets "/" + the other purposes of the ammunition of its launchers ("ANTI-SHIP/ASW" for SS-N-14b: ASuW + secondary ASW; "SAM/ANTI-SHIP" for a SAM with a secondary target; pure AAW: just SAM; a torpedo has one purpose; per-ammo domains `AmmoDom`; sections 0-3 only). The group names are not changed. Stock: a gun with a magazine counts the magazine only (the mount's `_sharedLoadedAmmoCount` is part of it); a launcher row with a magazine of its own shows the magazine's rest, not the mount's rounds (shared magazine: own rounds).
- Location label: world position of `_containers[0]._gunObject` (else `_mountObject`); fore/aft by z, port/starboard by x only if |x| > 0.02 units (and > 25% of max). Launchers of the same name standing at one point (spread < 0.03 units) use the centre of `_vwp._preferredArcs[0]` (0 fore, 180 aft, +-90 port/starboard). Same labels get "#N"; a part common to all launchers of a name is dropped ("Fore port / Fore starboard" -> "Port / Starboard"). A single-launcher group whose name equals another group of the section gets the location (or #N) appended (`GroupRow.Suffix`).
- Domains: AIR, MSL (missiles and torpedoes as targets), SUR (ships, surfaced subs), SUB, LND. Launcher domains = union over its ammunition (AAW -> AIR+MSL, ASuW -> SUR, ASW -> SUB, `_landAttack` -> LND, `_antiTorpedo` -> MSL).
- Player switches: `UnitW.Off[index]` = mask of switched-off domains (ConditionalWeakTable per unit), saved in the unit's save section (`AlternativeWeaponsControl_WeaponAuto=idx:mask,...`; postfixes of ObjectBase.Save/LoadStateFromFile).
- Standing Orders merge (`TacticalDoctrine`): AutoAttackSurface, ShipsAutoUseSAMsAgainstSurfaceTargets, ShipsAutoUseAntiShipMissilesAgainstSurfaceTargets. Chip colours: grey = off by player, yellow = forbidden by orders, green = auto, "mixed" half-circle for a group. Enabling something forbidden by orders sets the unit item to Yes and switches off the other pairs that this turned on; disabling only sets the player's Off. A change of orders from outside is detected by a snapshot of ResolvedValue (`SyncDoctrine`).
- Aircraft / helicopters: ONE `WeaponSystemHardpoint` (many Stations); ammunition of several types in `_associatedAmmunitionNames`; a Hardpoint is one Launcher (section STORES; fuel tanks, buoys skipped); switches act on the whole hardpoint by TARGET domain. Ground units (`LandUnit`) behave like ships except the AutoAttackSurface item and the Tight row.
- Stock: max = `_initialAmmoCount` of magazines (each once) + `_maxAmmoCount` of launchers without a magazine; `Cap(ws)` uses the rail slot count when `_maxAmmoCount` is 0 (Mk 141); aircraft guns with `_maxAmmoCount >= 10000` and a magazine show Stock/StockMax; "/10000" is cut from the game status text (`StatusParts`). Rails line "on launcher N/M" for single non-gun launchers with rails.

## Open points
- Not verified in the 0.8.7 game: SAM readiness (WeaponSystemLauncher.OnUpdate changed a lot, `launch` returns bool), pinned order to an empty / cold launcher (the code says the cold launcher runs the game's order flow), the ammunition table with the original ini path, hub entries after the load screen.
- Strict loadouts in Task Force Builder belong to Alternative Game Interface.

- Group Fire with several ammunition types: `GroupRow.FireIds` (chosen/active ammunition first, `OrderFireIds`); `PickAmmo(ids, pin, scope)` falls back to the first type any launcher of the scope still has. Gun trace `gun X ... waits N s ... state=..` (`GunWaitTrace`, debug only) shows why a gun holding an order for 20 s is not shooting (EngageState, reload, arc, director holds/busy/local).
- Idle guns near max range (trace 10.11, ww2 ships; cause found): `WeaponSystemGun.AlignTurret` adds the random fire-control error `_deviationVector` to the aim point BEFORE `GunBallistics.TrySolveSurface` and keeps it (`_deviationCalculated`) until a salvo has been fired (`CleanData` also resets it). Near max range (88-93% in the traces) the error can push the aim point past `FireTable._maxRange`: no solution (`_solutionCalculated=false`, `_projectileToTargetTime=-1`), AlignTurret returns false before the mount moves (rotDelta 0/0, mount at rest), no shot, so the error is never redrawn and the turret idles until the target changes, while its neighbours (other error) shoot. `[Guns] RerollAimError` (on): `AlignPostfix` -> `UnstickAim`: for the player's guns, surface targets, if the solution step failed and `TrySolveSurface` for the target WITHOUT the error succeeds, `_deviationCalculated = false` (the game's own `DeviationCheck` draws a new error on the next call). Trace "aim error redrawn: ...". Other exits of AlignTurret (beyond range, y < -0.136, `AcquisitionComplete`) are not touched.
- Not a cause: acquisition time (`acq=N/5.3s`, OODA scale of the player is 0 in the traces), director/channels, reload, time compression (x1 in the stuck cases).
- Open: some guns (Norfolk 8in, Leander 6" at x1) have a solution and are turned but the vertical delta stays 0.5-7 degrees (> `_rotationThesholds.y` 0.2) for a long time; `elev:` in the wait trace shows solution vs current angle. Not analysed yet.
- Stop: after a click the trace writes "after Stop: ..." for 4 s (cease button on/off, engaged launchers with exec/auto/task flags, unit orders). In the 10.11 trace Stop left no engaged launcher and no orders; the buttons of the children are still unexplained.
- Wait trace (`GunWaitDetail`, `SolveWhy`): dist/max, solution, flight, rotDelta/thr, mountYaw, arc, acq, tc, elev, and a copy of the TrySolveSurface checks WITHOUT the error (so "solve: ok" with `solution=False` points at the error).
- Trace 10.11 15:35 (build 20261110.1220): RerollAimError confirmed - Savage's front turrets drew a new error 12 times and stopped idling; "after Stop" shows every cease button off and no orders. The destroyers' aft turrets with a wrapped sector (`10..-10` / `40..-40`) that stayed put were not a fault: the target was ahead on the port side, outside their sectors (by design of `EngageOutsideSector` such a gun takes the target and waits until the ship turns), and they turned when the ship came round. Norfolk's two aft 8in turrets (`30..-30`, mountYaw ~312-316, horizontal error 0.8 -> 10.7 degrees, solution present) stood 289 s on Sakawa at 10.6-11.8 nm: NOT explained (no bearing in that trace); check `want` / `idle` in the next one. The wait trace prints `want` (bearing of the solution from the bow), `idle` and `rest`.
- The "waits N s" age counts from `_engageStartTime` (the order), not from the last shot, so a gun that fires a salvo now and then still shows long waits (Norfolk's aft 8in turrets may be this: the user saw them firing). The wait trace is now silent for a gun that fired within 20 s (`_deviationCheckTime`, stamped at about every salvo) and prints "last salvo N s ago / never" otherwise.

### Order watch (debug trace)
Open question: the player's Fire (rn_6in) on HMS Leander twice produced no `remembered targets` line, i.e. no gun took the order and the guns stayed on the old target. The trace now follows every non-automatic engage task of the player's units (`OrderWatch`, called from `HetLeave`): `order:` (born), `order taken:` (mounts holding it), `order DROPPED without being taken:` (the game removed it, e.g. "no systems found"), `order waits N s:` every 10 s with `processed` (AI.IsProcessedForEngagement, the OODA gate), `autoOnly`, distance and, per mount with the ammunition, busy/free, reload, usable, inArc. Tasks loaded from a save also print `order:` at the start.

### Orders loaded from a save are "newer" than new orders (fixed, no option)
Trace 02:05-02:07 (Leander, director destroyed): the player's Fire on Miyuki stayed in `_currentEngageTasks` for minutes, the 6in guns kept the order loaded from the save (Sakawa) and the order watch printed `busy with Sakawa (order, newer)`. Cause (game): `GameTime.LoadStateFromFile` reads MissionElapsedTime and discards it, while `EngageTask.CreateFromFile` restores the saved `CreationTime`; HandleEngageTasks breaks off a gun for a player's order only if `!(gun.taken.CreationTime >= order.CreationTime)`. A loaded order whose creation time is ahead of the clock therefore can never be replaced. Fix: `FixFutureTasks` (called from `HetEnter`, i.e. in the game's handling of the unit's orders, with no timer; only for the player's units whose order list is not empty) sets such creation times (list tasks and mounts' taken tasks) to clock-1 s and traces `creation time ahead of the clock (A vs B)`. The order-watch lines also print `clock=` and `order born=`.

### The player's order has priority 0
`OrderFirst` (AttackTask.OnExecute postfix): the engage task of a player's order gets `_priority = 0` and moves to the front of `_currentEngageTasks`. The game uses the target class (a ship = 10), so the AI's tasks (1..5) were processed before it and could take the guns over (`AI`: a task takes a mount when `priority < mount._engageTaskPriority`); with 0 the AI can never interrupt a gun that holds the player's order. Orders loaded from a save keep their saved priority.

### The player's orders: priority 0 whatever way they are made
A click on a contact with a unit selected (UnitSelectedState.AutoAttack -> AI.AutoAttackByClick) makes `new EngageTask(ammo, target, unit, salvo)` with the default priority 1000 (the lowest, appended last) and picks ONE gun ammunition (SelectAmmunition: the best one) - a ship with two gun calibres sends only one of them. `HumanFirst` (in `FixFutureTasks`, i.e. in the handling of the unit's orders) sets priority 0 and moves to the front every non-automatic task of a player's unit; `OrderFirst` (Fire from the tab) is the same earlier. `ClickEngagesAllGuns` (default on, GUI Guns tab): postfix of `AutoAttackByClick` adds a task for every other gun ammunition that reaches the target (min/max range only; the firing sector does not matter). The extra tasks get creation time 0: HandleEngageTasks breaks off the guns of another target for a WAITING order newer than the guns' order, so with two targets clicked one after the other the waiting extras of the first target kept tearing guns off the second (the sides no longer fired at different targets). Creation time 0 = older than all orders: an extra takes only free guns. Why the game picks one: SelectAmmunition takes the longest-range ammunition that has a mount usable NOW (`isWeaponSystemUsable` with sensors: a destroyed director = `HasFreeGuidingSensors` false, or a gun reloading), so on HMS Leander with the director destroyed the 6in were skipped and the 4.5in chosen.

### Guns on local control fire rarely (LocalControlKeepsFiring, default on)
WeaponSystemGun.fire holds every next shot (`NeedsSpottingFire`, only with DM._gunsDeviation, naval mode) until the previous shell has landed (`_canFireSpotShotTime = now + flight time`) while `_solution` < 0.95 and no round-tracking sensor. A gun with associated sensors sets `_solution` to the best working sensor's solution every check, and the splash-spotting bonus is added only to working sensors - with a destroyed director both are 0, so the solution stays 0 for ever: one shell per flight time (HMS Leander's 6in, trace 02:16: last salvo 23-43 s ago, flight 23-57 s). `SpotPrefix` returns "no spotting needed" for the player's gun when `GetFireControlSensor()` is null (no working director = local control). Accuracy is untouched (the game's gain 0.6 and a zero solution still make the aim poor). A gun with no associated sensors at all builds the solution itself (SensorUtils.BuildSolution) and is not stuck.

### "MISSING TEXT" shows the key
The game's language dictionary (`DictionaryWithFallbacks<string,string>`, default "MISSING TEXT") returns the bare default through its indexer (used by the UI bindings), and `IniToClassConverter` returns it for an unknown unit id. Postfixes (`MissingTextPostfix`, `MissingTextConvPostfix` in WeaponsAi.cs) replace the bare "MISSING TEXT" by the key / the id. `getText` / `getTextByKey` already add the key themselves. No option.

The Weapons tab's own ammunition names (group row, ammunition line, Fire tip, Fire menu) go through `AmmoName(ap, id)`: the game stores "MISSING TEXT" in `AmmunitionParameters._displayedName` when the language has no text for the ammunition (seen on a second 6in ammunition of HMS Leander) - the tab shows the ammunition id instead.

### Gun tooltips (director and accuracy)
`GunTip` / `GunInfo` (GunTip.cs): hovering a member's name (Weapons tab) or the group's name cell shows, built on MouseEnter: what the gun aims with (radar / optical director, local control), the director's gain / minimum solution (after the mod multipliers) and whether it tracks splashes, the solution %, "waits for splashes" (private `NeedsSpottingFire`), the aiming error (1 sigma, metres) from `GunFireControlModel.SurfaceSigmas` called by reflection on `CurrentFcState()` with `_fcKindMult` set (so the mod postfix applies the same multipliers as in a battle; at the target's range, else at 4/8/12 km), and the shell scatter (`PatternSigmas`). A group shows its first launcher.

WW2 preset values: weak 2, optical gain 1.5, radar gain 1.5, local gain 0.5 (= 0.3 absolute), radar min solution 1.25. Optical 1.5 lifts even the weakest pack optics (0.11 -> 0.33) above local control (0.3); radar 2 keeps radars clearly better than optics (pack radars end at 0.9-1.5, the vanilla radar range).

### Weak directors accuracy is now a share of the gap
`Guns/WeakDirectorsLift` (percent, default 10 (was 50); replaces the multiplier `WeakDirectorsAccuracy`, new key so saved multipliers are not read as percent): a director below gain 0.75 gets p% of (0.75 - gain) added (0.35 -> 0.55 at 50%); min solution scales by the same factor. Presets: Base 0, Advanced 50, WW2 50. No cap effect any more: stronger directors keep their order (0.40 stays better than 0.35).

### Solution build speed
`Guns/SolutionBuildSpeed` (default 1; WW2 preset 0.5): prefix `SolSpeedPrefix` on `SensorUtils.BuildSolution` multiplies its `multiplier` (director: gain x crew x light; gun without sensors: crew) - only the growth speed, the director accuracy is untouched. The WW2 preset also has optical / radar gain 1 now (not 1.5).

### Guns accuracy tab
The settings group "Accuracy" (max accuracy, multipliers, director options, presets, solution speed) moved from the Guns tab to its own tab "Guns accuracy" (`tabs.gunsAccuracy`, after Guns). The Guns tab keeps the aiming options and "Alter accuracy and power" with the ammunition table.

### Accuracy tab regrouped (GLOBAL / DIRECTORS)
Removed: LocalControlAccuracy / OpticalDirectorAccuracy / RadarDirectorAccuracy (and `DeviationCheck` patch, `_fcKindMult`): not needed, the director options cover it. Groups: Global = MaxAccuracyMeters ("Maximum accuracy"), AccuracyMultiplier ("Shell accuracy"), FireControlAccuracy ("Aiming accuracy"); Directors (with the preset bar, now WW2 Mod): WeakDirectorsLift ("Weak directors boost"), SolutionBuildSpeed, LocalControlGain ("Solution quality without a director"), OpticalDirectorMinSolution / RadarDirectorMinSolution ("Solution quality with optical / radar directors"), then OpticalDirectorGain / RadarDirectorGain (names unchanged). Presets no longer contain FireControlAccuracy.

### Fire Control tab
Tabs: Weapons panel, AI, SAM (was "Fire Control": SAM options only), Guns, Fire Control (was "Guns accuracy": group Fire control = GunOneDirector / GunChannelLimit, Global, Directors with the "Directors preset" bar instead of a group title), Debug. OpticalDirectorGain / RadarDirectorGain removed (config keys, code, presets).

## Settings structure (19.11)
- Tabs (the order = the order of the `Add` calls): Weapons panel (`tabs.panel`: Panel, SAM readiness button), Auto fire (`tabs.fire`, was "SAM": Standing Orders `grp.so`, On Free unidentified aircraft, SAM), Guns (`tabs.guns`: Targets `grp.gunTargets` - remember targets, click: all guns, point fire, outside sector, local control; Fire control), Accuracy (`tabs.gunsAccuracy`, was "Fire Control": General = Aiming, Global, Ammunition + the ammunition table; Directors), AI (`tabs.ai`: target selection, chasing), Language, Debug (last).
- Config keys are unchanged (only the window layout moved). The ammunition list fields are on `tabs.gunsAccuracy` now (`AmmoTable` is drawn on its General inner tab). Tab / group titles: 9 languages. Not verified in the game.
