ALTERNATIVE WEAPONS CONTROL

Version 1.4.0.

Adds a new Weapons tab in the Unit Status window of your units, and a set of combat improvements.

FEATURES

Weapons tab in Unit Status

1. All launchers at a glance, by kind (SAM, anti-ship, land attack, ASW, guns, CIWS), launchers of the same system in one row (×N), opened to every launcher and every type of ammunition. Groups with several ammunition types have an ammunition selector with its own target-type switches and Cease fire. Section titles with several groups have common Fire / Stop / Reload.
   State of each launcher and its current target, ammunition left, fire-control channels of the directors, links to the reference.
   Drag a group title (SAM, GUNS, …) to reorder the groups.
2. Ground vehicles, land installations, aircraft and helicopters get the tab too (each can be switched off in the settings).
3. Fire orders for a group, an ammunition or one launcher: pick the target on the map, salvo and launch options, cease fire, reload. A launcher given its own target fires at it and waits while it is busy or no fire-control channel is free. Kept in saved games.
4. Automatic use per launcher and target domain (aircraft, missiles, surface ships, submarines, land): switch off what the AI may use by itself - e.g. keep the long-range SAMs for aircraft. Merged with the unit's Standing Orders; kept in saved games. Manual fire is never restricted.
5. Weapons status (Free / Tight / Hold) with "On Tight: engage hostile aircraft" and "On Free: engage unidentified aircraft" at the top of the tab. The second one engages fast (500 kn+) unidentified aircraft heading towards the ship within 20 nm. It does not know whose aircraft it is. Default: off for your ships, on for enemy AI.

SAM launchers

1. Readiness button: load the missiles onto the rails now, or put them away. They are put away automatically after 15 minutes if unused. Missiles stay loaded after a launch, and a hot launcher loads the next ones by itself while they are needed; otherwise it goes cold.
2. Reload button: unloads the rails, waits a moment, then loads them again. It is grey while the launcher is busy.
3. Clear statuses: Rails empty, Loading rails, Unloading rails, a countdown of the pause after an unloading, and Empty (grey) for a launcher out of missiles.
4. Auto-charge when an unknown aircraft approaches. Works for AI ships too.

Fire and guns

1. Fire shows the weapon's firing arcs in red, with a weak red fill, on the map (Sea Power 0.8.7) and in 3D while the target is being picked.
2. Aim beyond maximum range: guns, CIWS and machine guns turn towards a target out of range or outside their firing sector (as far as the sector allows) without firing.
   Option "Engage targets outside the firing sector" (on by default): big guns that the game never gives a target outside their sector take such a target and turn to the edge of the sector (applied to units loaded afterwards).
   Option "Remember earlier targets" (on by default): a new order to fire still switches the guns to the new target, but earlier targets are kept; a gun that cannot reach the new one (range, firing sector) goes on with the newest earlier target it can reach. Cease fire clears the list.
   Option "Bottom panel weapons act as Fire" (off by default): a click on a weapon of the panel at the bottom of the screen shows the red zones and hides the game's range circle, like Fire of the tab.
3. Disable AI cheating: the game's AI engages only contacts whose true side is hostile, which you cannot know. Your ships now engage the contacts you marked hostile on the plot (on Tight: aircraft only).
4. Close-range accuracy: "Guns: change maximum accuracy" - best accuracy of guns (default 5 m, the game has about 40 m).
5. Ammunition pack: the accuracy and warhead power of 47 gun ammunition types are taken from the author's pack (ammunition_fix.txt in the mod folder). The table in the Guns tab of the settings window ("Guns: alter accuracy and power", on by default) lists them with the game's names, the mod's values and the game's own values; you can edit every value and reset it. The table and other dependent settings are hidden while their switch is off; applied to ammunition loaded after Apply (a mission start).
6. Ship chase options: a ship keeps its course instead of running after RBU targets or aircraft to get them into range.

Fire control (optional)

1. Guns take one director, not all of them as in the game, so the others stay free for other guns and for missiles. On by default.
2. Guns without a free channel aim on local control (less accurate, worse at night) until a channel frees. Off by default.

Special effects

Special effects (flashes, fire light, binoculars normal view, machine gun and hit sound fixes) moved to the mod Alternative Special Effects; install it to keep them. Settings are taken over on its first start.

Settings and languages

1. Settings can be opened from the main menu and the pause menu; with several of the author's mods installed there is one shared "Alternative Mods" entry.
2. Settings window in the game with tabs: Weapons panel, Fire Control, AI, Guns, Debug, Language.
3. Localized into 9 languages (en, ru, cn, de, es, fr, ko, ja, vn). The lang/<code>.ini files can be edited to translate or change texts.

COMPATIBILITY

- Supports Sea Power version 0.8.7 (Release and Stable Beta).
- Requires Anchor Chain.
- Can be switched on/off in the game's Mods menu.

INSTALL

1. Install the Anchor Chain preloader as described in the documentation (https://seapower-modders.github.io/AnchorChain/install-anchor-chain.html).
2. Subscribe to this mod.
3. Restart the game.

USAGE

- Unit Status → Weapons: point at states, numbers and switches for details.
- Settings in the game: the gear next to (i) on the Weapons tab button.
