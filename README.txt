ALTERNATIVE WEAPONS CONTROL

Adds a new Weapons tab in the Unit Status window of your units, and a set of combat and visual improvements.

FEATURES

Weapons tab in Unit Status

1. All launchers at a glance, by kind (SAM, anti-ship, land attack, ASW, guns, CIWS), launchers of the same system in one row (×N), opened to every launcher and every type of ammunition. Groups with several ammunition types have an ammunition selector with its own target-type switches and Cease fire. Section titles with several groups have common Fire / Stop / Reload.
   State of each launcher and its current target, ammunition left, fire-control channels of the directors, links to the reference.
   Drag a section title (SAM, GUNS, …) to reorder the sections.
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
2. Aim beyond maximum range: guns, CIWS and machine guns turn towards a target out of range without firing.
3. Disable AI cheating: the game's AI engages only contacts whose true side is hostile, which you cannot know. Your ships now engage the contacts you marked hostile on the plot (on Tight: aircraft only).
4. Close-range accuracy: option for the best accuracy of guns (default 5 m).
5. Ship chase options: a ship keeps its course instead of running after RBU targets or aircraft to get them into range.

Fire control (optional)

1. Guns take one director, not all of them as in the game, so the others stay free for other guns and for missiles. On by default.
2. Guns without a free channel aim on local control (less accurate, worse at night) until a channel frees. Off by default.

Visual and optics

1. Light effects: gunfire, hits, missile launches and detonations light up their surroundings in 3D, and the bigger the weapon, the brighter and longer the flash. Fires on ships flicker. By day the lights are dimmed (option, default 0.5) and the flashes alive at once share a total brightness limit, so the picture does not blow out into white and pink. Every effect can be switched off, and brightness is adjustable.
2. Machine guns: tracers and the muzzle burst sound now last for the whole burst.
3. Binoculars start in the normal view at maximum zoom, with no night or thermal filter by default (option: only by day).

Settings and languages

1. Settings window in the game with tabs: Weapons panel, Fire Control, AI, Guns, Visual effects, Debug, Language.
2. Localized into 9 languages (en, ru, cn, de, es, fr, ko, ja, vn). The lang/<code>.ini files can be edited to translate or change texts.

COMPATIBILITY

- Supports Sea Power versions 0.8.6 – 0.8.7.
- Requires Anchor Chain.
- Can be switched on/off in the game's Mods menu.

INSTALL

1. Install the Anchor Chain preloader as described in the documentation (https://seapower-modders.github.io/AnchorChain/install-anchor-chain.html).
2. Subscribe to this mod.
3. Restart the game.

USAGE

- Unit Status → Weapons: point at states, numbers and switches for details.
- Settings in the game: the gear next to (i) on the Weapons tab button.
