Version 1.3.0.

Adds a new [b]Weapons[/b] tab in the Unit Status window of your units, and a set of combat improvements.

[h1]Features[/h1]
[h3]Weapons tab in Unit Status[/h3]
[olist]
[*][b]All launchers at a glance[/b], by kind (SAM, anti-ship, land attack, ASW, guns, CIWS), launchers of the same system in one row (×N), opened to every launcher and every type of ammunition. Groups with several ammunition types have an ammunition selector with its own target-type switches and Cease fire. Section titles with several groups have common Fire / Stop / Reload.
State of each launcher and its current target, ammunition left, fire-control channels of the directors, links to the reference.
Drag a section title (SAM, GUNS, …) to reorder the sections.
[*][b]Ground vehicles, land installations, aircraft and helicopters[/b] get the tab too (each can be switched off in the settings).
[*][b]Fire orders[/b] for a group, an ammunition or one launcher: pick the target on the map, salvo and launch options, cease fire, reload. A launcher given its own target fires at it and waits while it is busy or no fire-control channel is free. Kept in saved games.
[*][b]Automatic use[/b] per launcher and target domain (aircraft, missiles, surface ships, submarines, land): switch off what the AI may use by itself - e.g. keep the long-range SAMs for aircraft. Merged with the unit's Standing Orders; kept in saved games. Manual fire is never restricted.
[*][b]Weapons status[/b] (Free / Tight / Hold) with "On Tight: engage hostile aircraft" and "On Free: engage unidentified aircraft" at the top of the tab. The second one engages fast (500 kn+) unidentified aircraft heading towards the ship within 20 nm. It does not know whose aircraft it is. Default: off for your ships, on for enemy AI.
[/olist]

[h3]SAM launchers[/h3]
[olist]
[*][b]Readiness button:[/b] load the missiles onto the rails now, or put them away. They are put away automatically after 15 minutes if unused. Missiles stay loaded after a launch, and a hot launcher loads the next ones by itself while they are needed; otherwise it goes cold.
[*][b]Reload button:[/b] unloads the rails, waits a moment, then loads them again. It is grey while the launcher is busy.
[*][b]Clear statuses:[/b] Rails empty, Loading rails, Unloading rails, a countdown of the pause after an unloading, and Empty (grey) for a launcher out of missiles.
[*][b]Auto-charge[/b] when an unknown aircraft approaches. Works for AI ships too.
[/olist]

[h3]Fire and guns[/h3]
[olist]
[*][b]Fire shows the weapon's firing arcs[/b] in red, with a weak red fill, on the map (Sea Power 0.8.7) and in 3D while the target is being picked.
[*][b]Aim beyond maximum range:[/b] guns, CIWS and machine guns turn towards a target out of range without firing.
[*][b]Disable AI cheating:[/b] the game's AI engages only contacts whose true side is hostile, which you cannot know. Your ships now engage the contacts you marked hostile on the plot (on Tight: aircraft only).
[*][b]Close-range accuracy:[/b] "Guns: change maximum accuracy" - best accuracy of guns (default 5 m, the game has about 40 m).
[*][b]Ammunition pack:[/b] the accuracy and warhead power of 47 gun ammunition types are taken from the author's pack (ammunition_fix.txt in the mod folder). The table in the Guns tab of the settings window ("Guns: alter accuracy and power", on by default) lists them with the game's names, the mod's values and the game's own values; you can edit every value and reset it. The table and other dependent settings are hidden while their switch is off; applied to ammunition loaded after Apply (a mission start).
[*][b]Ship chase options:[/b] a ship keeps its course instead of running after RBU targets or aircraft to get them into range.
[/olist]

[h3]Fire control (optional)[/h3]
[olist]
[*][b]Guns take one director,[/b] not all of them as in the game, so the others stay free for other guns and for missiles. On by default.
[*][b]Guns without a free channel aim on local control[/b] (less accurate, worse at night) until a channel frees. Off by default.
[/olist]

[h3]Special effects[/h3]
Special effects (flashes, fire light, binoculars normal view, machine gun and hit sound fixes) moved to the mod Alternative Special Effects; install it to keep them. Settings are taken over on its first start.

[h3]Settings and languages[/h3]
[olist]
[*]Settings can be opened from the main menu and the pause menu; with several of the author's mods installed there is one shared "Alternative Mods" entry.
[*]Settings window in the game with tabs: Weapons panel, Fire Control, AI, Guns, Debug, Language.
[*]Localized into 9 languages (en, ru, cn, de, es, fr, ko, ja, vn). The [b]lang/<code>.ini[/b] files can be edited to translate or change texts.
[/olist]

[h1]Compatibility[/h1]
[list]
[*]Supports Sea Power version 0.8.7 (Release and Stable Beta).
[*]Requires Anchor Chain.
[*]Can be switched on/off in the game's Mods menu.
[/list]
[h1]Install[/h1]
[olist]
[*]Install the Anchor Chain preloader as described in the [url=https://seapower-modders.github.io/AnchorChain/install-anchor-chain.html]documentation[/url].
[*]Subscribe to this mod.
[*]Restart the game.
[/olist]

[h1]Usage[/h1]
[list]
[*][b]Unit Status[/b] → [b]Weapons[/b]: point at states, numbers and switches for details.
[*][b]Settings in the game:[/b] the gear next to (i) on the Weapons tab button.
[/list]