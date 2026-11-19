Version 1.9.0.

Adds a new [b]Weapons[/b] tab in the Unit Status window of your units, and a set of combat improvements.

[h1]Mod require [b]Anchor Chain[/b] or [b]R.A.T.S. Keel[/b][/h1]

[h1]Features[/h1]
[h3]Weapons tab in Unit Status[/h3]
[olist]
[*][b]All launchers at a glance[/b], by kind (SAM, anti-ship, land attack, ASW, guns, CIWS), launchers of the same system in one row (×N), opened to every launcher and ammunition type. Groups with several ammunition types have an ammunition selector with its own switches and Cease fire; section titles have common Fire / Stop / Reload.
State of each launcher and its current target, ammunition left, fire-control channels of the directors, links to the reference.
Drag a group title (SAM, GUNS, …) to reorder the groups.
[*][b]Ground vehicles, land installations, aircraft and helicopters[/b] get the tab too.
[*][b]Fire orders[/b] for a group, an ammunition or one launcher: pick the target on the map, salvo and launch options, cease fire, reload. A launcher given its own target fires at it and waits while it is busy or no fire-control channel is free. Kept in saved games.
[*][b]Automatic use[/b] per launcher and target domain (aircraft, missiles, surface ships, submarines, land): switch off what the AI may use by itself - e.g. keep the long-range SAMs for aircraft. Merged with the unit's Standing Orders; kept in saved games; manual fire is never restricted. A click goes On, Off, then back to Standing Orders. A rule with Always forces a switch on at any status, Hold included (amber ring, green core).
[*][b]Weapons status[/b] (Free / Tight / Hold) with "On Tight: engage hostile aircraft" and "On Free: engage unidentified aircraft" at the top of the tab. The second one engages fast (500 kn+) unidentified aircraft heading towards the ship within 20 nm; it does not know whose they are. Default: off for your ships, on for enemy AI.
[*][b]Strict EMCON[/b] next to Free / Tight / Hold: turns the unit's radars off and none can be turned on, even to engage a target; they come back when it ends. Radar directors that serve only guns keep working as silent optical ones: not heard by ESM, accuracy between local control and the radar (a Guns tab setting), no tracking of shells in flight, range and night by the rules of optics.
[*][b]Launcher statuses[/b] also tell "Assessing n%" and "Waiting for sensor" (the crew has not assessed the target yet) and "Sensor inoperable". Guns, RBU and bombs do not show "Sensors Offline".
[/olist]

[h3]SAM launchers[/h3]
[olist]
[*][b]Readiness button:[/b] load the missiles onto the rails now, or put them away (automatically after 15 minutes if unused). A hot launcher loads the next missiles by itself while they are needed.
[*][b]Reload button:[/b] unloads the rails, waits a moment, then loads them again. It is grey while the launcher is busy.
[*][b]Clear statuses:[/b] Rails empty, Loading rails, Unloading rails, a countdown of the pause after an unloading, and Empty (grey) for a launcher out of missiles.
[*][b]Auto-charge[/b] when an unknown aircraft approaches. Works for AI ships too.
[*][b]On Tight: fire only in no-escape zone[/b] (Fire; off): the ships' automatic SAM fire on Weapons Tight waits until the aircraft is inside the missile's no-escape zone.
[/olist]

[h3]Fire and guns[/h3]
[olist]
[*][b]Fire shows the weapon's firing arcs[/b] in red on the map and in 3D while the target is being picked; the radius follows the range against the contact under the cursor.
[*][b]Aim beyond maximum range:[/b] guns, CIWS and machine guns turn towards a target out of range or outside their firing sector (as far as the sector allows) without firing.
[*][b]Engage targets outside the firing sector[/b] (on by default): big guns that the game never gives a target outside their sector take such a target and turn to the edge of the sector.
[*][b]Remember earlier targets[/b] (on by default): a new order switches the guns to the new target, but earlier targets are kept; a gun that cannot reach the new one goes on with the newest earlier target it can reach. Cease fire clears the list.
[*][b]Bottom panel weapons act as Fire[/b] (on by default): a click on a weapon of the bottom panel shows the red zones, like Fire of the tab.
[*][b]Disable AI cheating:[/b] the game's AI engages only contacts whose true side is hostile, which you cannot know. Your ships now engage the contacts you marked hostile on the plot (on Tight: aircraft only).
[*][b]Close-range accuracy:[/b] "Guns: change maximum accuracy" - best accuracy of guns (default 5 m, the game has about 40 m).
[*][b]Ammunition pack:[/b] the accuracy and warhead power of 48 gun ammunition types are taken from the author's pack (ammunition_fix.txt in the mod folder). The table in the Guns tab of the settings ("Guns: alter accuracy and power", on by default) lists the mod's and the game's values; edit or reset any of them. Applied to ammunition loaded after Apply (a mission start).
[*][b]Gun fixes:[/b] "Click on a target: all guns" orders every gun ammunition that reaches the target, not one; "Unstick guns at long range" fixes a gun that idled for minutes with an aim point beyond its firing table; "Local control: no spotting wait" lets a gun without a working director fire at its normal rate.
[*][b]Directors and accuracy[/b] (Guns tab): presets Base / Advanced / WW2 Mod, boost of weak directors, firing solution quality and speed, shell and aiming accuracy. The gun tooltip shows the director, solution and aiming error.
[*][b]Ship chase options:[/b] a ship keeps its course instead of running after RBU targets or aircraft to get them into range.
[*][b]Fire at a point of the map[/b] (option, on): a right click on the empty map while a gun is being aimed puts an invisible decoy on that point and the guns engage it; the cursor turns grey over the map.
[*][b]Scatter for every barrel of a turret[/b] (option, on): each barrel gets its own shell scatter. [b]Big guns without a director[/b] build their own firing solution (option, on).
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
[*]Settings open from the main menu and the pause menu (one shared "Alternative Mods" entry for the author's mods).
[*]Settings window in the game with tabs: Weapons panel, Auto fire, Guns, Accuracy (General, Directors), AI, Debug; search by names.
[*]Localized into 9 languages (en, ru, cn, de, es, fr, ko, ja, vn). Edit [b]lang/<code>.ini[/b] to change texts.
[/olist]

[h1]Compatibility[/h1]
[list]
[*]Supports Sea Power version 0.9.0 and newer.
[*]Loaders: Anchor Chain or R.A.T.S. Keel.
[*]Compatible with the WW2 mod; it is recommended to use that mod without its optional dll.
[*]Can be switched on/off in the game's Mods menu.
[/list]
[h1]Install[/h1]
[olist]
[*]Subscribe to [url=https://steamcommunity.com/workshop/filedetails/?id=3380210757]Anchor Chain[/url] or [url=https://steamcommunity.com/workshop/filedetails/?id=3835527459]RATS Keel[/url] (temporarily unavailable) and follow instructions on mods page.
[*]Subscribe to this mod.
[*]Start [b]Sea Power[/b].
[*]Enable mod in game's [b]Mod Manager[/b] and press [b]Apply[/b] button.
[/olist]

[h1]Usage[/h1]
[list]
[*][b]Unit Status[/b] → [b]Weapons[/b]: point at states, numbers and switches for details.
[*][b]Settings in the game:[/b] the gear next to (i) on the Weapons tab button.
[/list]