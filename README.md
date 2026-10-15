# Alternative Weapons Control

Adds a new **Weapons** tab in the Unit Status window of your ships and submarines.

## Features

### Weapons tab in Unit Status

1. **All launchers at a glance**, by kind (SAM, anti-ship, land attack, ASW, guns, CIWS), launchers of the same system in one row (×N), opened to every launcher and every type of ammunition.
   State of each launcher and its current target (Track N, with the name once identified).
   Ammunition left / at the start, coloured by what is left.
   Fire-control channels of the directors: targets tracked and guided weapons in flight at a time; point at them for each director, with a link to its reference.
   Links to the reference of every launcher and weapon.
2. **Fire orders** for a group, an ammunition or one launcher: pick the target on the map, salvo and launch options, cease fire, reload. A launcher given its own target fires at it, waits while it is busy or no fire-control channel is free, and does not take other launchers off their targets. Kept in saved games.
3. **Automatic use** per launcher and target domain (aircraft, missiles, surface ships, submarines, land): switch off what the AI may use by itself - e.g. keep the long-range SAMs for aircraft and leave missiles to the short-range ones. Merged with the unit's Standing Orders; kept in saved games. Manual fire is never restricted.
4. **Weapons status** (Free / Tight / Hold) and "On Tight: engage hostile aircraft" at the top of the tab.
5. A legend behind the (i) next to the tab's name.

### Fire control (optional)

1. **Guns take one director.** A gun opening fire takes a channel on one of its directors, not on all of them as in the game, so the others stay free for other guns and for missiles sharing them. On by default.
2. **Guns without a free channel aim on local control** (less accurate, worse at night) until a channel frees. Off by default.

## Compatibility

- Supports Sea Power version 0.8.3 - 0.8.5.
- Requires Anchor Chain.
- Can be switched on/off in the game's Mods menu.

## Install

1. Install the Anchor Chain preloader as described in the [documentation](https://seapower-modders.github.io/AnchorChain/install-anchor-chain.html).
2. Subscribe to this mod.
3. Restart the game.

## Usage

- **Unit Status** → **Weapons**: point at states, numbers and switches for details.
- **Settings in the game:** the gear next to (i) on the Weapons tab button.
