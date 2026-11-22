ALTERNATIVE WEAPONS CONTROL

Version 2.0.1.

Adds a new Weapons tab in the Unit Status window of your units, and a set of combat improvements.

MOD REQUIRE ANCHOR CHAIN OR R.A.T.S. KEEL

FEATURES

Weapons tab in Unit Status

1. All launchers at a glance, by kind (SAM, anti-ship, land attack, ASW, guns, CIWS), launchers of the same system in one row (×N), opened to every launcher and every type of ammunition. Groups with several ammunition types have an ammunition selector with its own target-type switches and Cease fire. Section titles with several groups have common Fire / Stop / Reload.
   State of each launcher and its current target, ammunition left, fire-control channels of the directors, links to the reference.
   Drag a group title (SAM, GUNS, …) to reorder the groups.
2. Ground vehicles, land installations, aircraft and helicopters get the tab too.
3. Fire orders for a group, an ammunition or one launcher: pick the target on the map, salvo and launch options, cease fire, reload. A launcher given its own target fires at it and waits while it is busy or no fire-control channel is free. Kept in saved games.
4. Automatic use per launcher and target domain (aircraft, missiles, surface ships, submarines, land): switch off what the AI may use by itself - e.g. keep the long-range SAMs for aircraft. Merged with the unit's Standing Orders; kept in saved games. Manual fire is never restricted. A click on a switch goes On, Off, then back to the unit's Standing Orders. A rule with Always forces a switch on at any status, Hold included (amber ring, green core); Free / Hold turn amber where rules override them.
5. Weapons status (Free / Tight / Hold) with "On Tight: engage hostile aircraft" and "On Free: engage unidentified aircraft" at the top of the tab. The second one engages fast (500 kn+) unidentified aircraft heading towards the ship within 20 nm. It does not know whose aircraft it is. Default: off for your ships, on for enemy AI.
6. Strict EMCON next to Free / Tight / Hold: turns the unit's radars off and none can be turned on, even to engage a target (saved with the unit); they come back on when it ends. Radar directors that serve only guns are not switched off: they work as silent optical directors - not heard by ESM, accuracy between the guns' local control and the radar (a setting of the Guns tab), no tracking of shells in flight, range and night by the rules of optics. The directors window and the gun tooltip say "radar off: optical mode".
7. Aircraft: "Max-rate turns" button (menu of minimum speed, G meter): tight turns with all the load factor the aircraft has. Aircraft with missiles turned off chase and shoot with their cannon (AI tab option), and the AI no longer wipes your waiting orders.
8. Launcher statuses also tell "Assessing n%" and "Waiting for sensor" (the crew has not assessed the target yet), and "Sensor inoperable" (all directors destroyed). Weapons that need no sensors (guns, RBU, bombs) do not show "Sensors Offline".

SAM launchers

1. Readiness button: load the missiles onto the rails now, or put them away. They are put away automatically after 15 minutes if unused. Missiles stay loaded after a launch, and a hot launcher loads the next ones by itself while they are needed; otherwise it goes cold.
2. Reload button: unloads the rails, waits a moment, then loads them again. It is grey while the launcher is busy.
3. Clear statuses: Rails empty, Loading rails, Unloading rails, a countdown of the pause after an unloading, and Empty (grey) for a launcher out of missiles.
4. Auto-charge when an unknown aircraft approaches. Works for AI ships too.
5. On Tight: fire only in no-escape zone (Fire; off): the ships' automatic SAM fire on Weapons Tight waits until the aircraft is inside the missile's no-escape zone.

Fire and guns

1. Fire shows the weapon's firing arcs in red on the map and in 3D while the target is being picked; the radius follows the range the game works out against the contact under the cursor (a SAM against a ship is shorter than against an aircraft).
2. Aim beyond maximum range: guns, CIWS and machine guns turn towards a target out of range or outside their firing sector (as far as the sector allows) without firing.
3. Engage targets outside the firing sector (on by default): big guns that the game never gives a target outside their sector take such a target and turn to the edge of the sector.
4. Remember earlier targets (on by default): a new order to fire still switches the guns to the new target, but earlier targets are kept; a gun that cannot reach the new one (range, firing sector) goes on with the newest earlier target it can reach. Cease fire clears the list.
5. Bottom panel weapons act as Fire (on by default): a click on a weapon of the panel at the bottom of the screen shows the red zones and hides the game's range circle, like Fire of the tab.
6. Disable AI cheating: the game's AI engages only contacts whose true side is hostile, which you cannot know. Your ships now engage the contacts you marked hostile on the plot (on Tight: aircraft only).
7. Close-range accuracy: "Guns: change maximum accuracy" - best accuracy of guns (default 5 m, the game has about 40 m).
8. Ammunition pack: the accuracy and warhead power of 48 gun ammunition types are taken from the author's pack (ammunition_fix.txt in the mod folder). The table in the Guns tab of the settings window ("Guns: alter accuracy and power", on by default) lists them with the game's names, the mod's values and the game's own values; you can edit every value and reset it. The table and other dependent settings are hidden while their switch is off; applied to ammunition loaded after Apply (a mission start).
9. Gun fixes: "Click on a target: all guns" orders every gun ammunition that reaches the target, not one; "Unstick guns at long range" gives a gun a new aiming error when the old one put the aim point beyond its firing table (it idled for minutes); "Local control: no spotting wait" lets a gun without a working director fire at its normal rate.
10. Directors and accuracy (Guns tab): presets Base / Advanced / WW2 Mod, boost of weak directors, firing solution quality and build speed, shell and aiming accuracy. The gun tooltip shows the director, the firing solution and the aiming error at the target's range.
11. Ship chase options: a ship keeps its course instead of running after RBU targets or aircraft to get them into range.
12. Fire at a point of the map (option, on): a right click on the empty map while a gun is being aimed puts an invisible decoy on that point, and the guns engage it like a target; the cursor turns grey over the map.
13. Scatter for every barrel of a turret (option, on): each barrel gets its own shell scatter. Big guns without a director build their own firing solution (option, on).

Fire control (optional)

1. Guns take one director, not all of them as in the game, so the others stay free for other guns and for missiles. On by default.
2. Guns without a free channel aim on local control (less accurate, worse at night) until a channel frees. Off by default.

Special effects

Special effects (flashes, fire light, binoculars normal view, machine gun and hit sound fixes) moved to the mod Alternative Special Effects; install it to keep them. Settings are taken over on its first start.

Settings and languages

1. Settings can be opened from the main menu and the pause menu; with several of the author's mods installed there is one shared "Alternative Mods" entry.
2. Settings window in the game with tabs: Weapons panel, SAM, AI, Guns, Fire Control (General, Directors), Debug, Language.
3. Localized into 9 languages (en, ru, cn, de, es, fr, ko, ja, vn). The lang/<code>.ini files can be edited to translate or change texts.

COMPATIBILITY

- Supports Sea Power version 0.9.3 and newer.
- Loaders: Anchor Chain or R.A.T.S. Keel.
- Compatible with the WW2 mod; it is recommended to use that mod without its optional dll.
- Can be switched on/off in the game's Mods menu.

INSTALL

1. Subscribe to Anchor Chain (https://steamcommunity.com/workshop/filedetails/?id=3380210757) or RATS Keel (https://steamcommunity.com/workshop/filedetails/?id=3835527459) (temporarily unavailable) and follow instructions on mods page.
2. Subscribe to this mod.
3. Start Sea Power.
4. Enable mod in game's Mod Manager and press Apply button.

USAGE

- Unit Status → Weapons: point at states, numbers and switches for details.
- Settings in the game: the gear next to (i) on the Weapons tab button.
