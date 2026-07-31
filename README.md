# Weapon Forge

**Build custom weapons for PUNK by writing small JSON files — no C#, no compiling, no Harmony patches.**

Weapon Forge clones a real in-game weapon as a starting point, then applies whatever stats you listed in your JSON on top. Everything you *don't* mention keeps the template's value, so a five-line file is a perfectly valid weapon.

Built against **PUNK Playtest v0.12.9**.

---

## Requirements

- **BepInEx** — see [PunkMods](https://github.com/Osanchez/PunkMods) for a good walkthrough of setting up mods for PUNK.

## Install

1. Copy `WeaponForge.dll` into `...\PUNK Playtest\BepInEx\plugins\` (its own folder is fine).
2. Run the game once, then close it.
3. A **`weapons`** folder now sits next to the DLL, containing `README.txt` and several example weapons.
4. Drop your own `.json` files in there and restart.

Your weapons appear as extra choices at the end of the starting-loadout list (past all the question marks if you haven't unlocked much).

> Files are read at **startup**. There's no hot reload — restart the game to see changes.
>
> Errors never crash the game. A bad file is skipped and the reason is written to `BepInEx\LogOutput.log` — search for `WeaponForge`.

---

## The Builder (recommended)

Open **`Weapon Builder.html`** in any browser. It's a single self-contained page — no install, no internet.

- Every field is documented inline, with the game's own stock values as reference points
- Only shows the fields that actually apply to the weapon type you picked
- Ready-made presets: White Tesla, Orbit Ring, Triple Ring, Sentry Disc, Wave Beam, Spiral Vortex
- **LOAD .JSON** to edit an existing weapon, **SAVE .JSON FILE** when you're done

There's also **`HOW TO MAKE WEAPONS.txt`** — the full written reference, including a per-type breakdown of what applies and what silently does nothing.

---

## What you can make

### Weapon types
All four of the game's weapon types are supported, and the builder knows which fields each one really honours:

| Type | Examples | Notes |
|---|---|---|
| **Projectile** | White Popper, Bolt, Shotgun, Worm | the full option set |
| **Lobbed / physics** | White Derbis, Purple Flack, Caps Loon | arcs, tumbles, bounces (`usePhysics`) |
| **Missile** | White Dart, Purple/Caps/Electron Rocket | homing — **requires `usePhysics`** |
| **Hitscan / laser** | Caps Laser, Crawler Laser | instant beam; can also **terraform** |
| **Minion spawner** | the 5 raw minion weapons | spawns a real pet unit |

### Slots & availability
- Equip to **primary**, **secondary**, or **gadget1/2/3** (the 1/2/3 keys)
- `source`: `starter`, `loot`, `starterAndLoot`, or **`none`** (built and referenceable but never offered — for helper weapons)
- Optional in-run **shop** entry with price and unlock tier
- `target`: point a weapon at enemies or at the player — this also *fixes* enemy weapon templates so you can use them

### Visuals
Recolor projectiles and beams (hex, HTML names, game colors, or animated **rainbow**), rescale them, or swap in any other weapon's ammo with `projectilePrefab`.

### Custom behaviours
These are Weapon Forge additions, not stock game features:

- **Orbit weapons** — projectiles that circle your ship. Passive / hold / toggle / fire modes, contact damage, enemy-bullet blocking, push, pulse, spin-up, destructible orbs with regeneration, and **multiple concentric rings** (spacing, stagger, counter-rotation, per-ring speed)
- **Spiral orbit** — orbs spiral outward and either sling off toward enemies or sweep like a sprinkler
- **Wave motion** — a clean repeating sine "S" (Super Metroid wave beam), with synced and double-helix variants
- **Deployable turret / mine** — a shot that glides to a stop and then keeps firing another weapon while it lives, sweeping in a circle or auto-targeting the nearest enemy, damaging what touches it
- **Phasing** — shots pass through terrain but still hit enemies
- **Pierce cap** — pierce *N* enemies then vanish, with optional damage falloff
- **Chain lightning** — turn any weapon into a Tesla-style arc, with configurable lightning colour (including RGB) and reach
- **Burn control** — burn tick rate and flame colour, optionally recolouring burning terrain too
- **Sub-emitters** — fire a *whole other weapon* when a shot expires or hits, chainable as deep as you like

### Module card
Customise the grid module your weapon lives on: icon, colour, description, power-core capacity, and which resource equipping it grants.

---

## Notes & known limitations

- **Save/continue is safe.** Weapons are registered at startup, so you can save mid-run and use Continue. If you *rename or delete* a weapon file, an old save that still references it will lose that weapon — keep the file around while any save needs it.
- **Not every option applies to every type.** Wave, phasing, pierce cap and turret need a straight-flying projectile; homing needs `usePhysics`; a hitscan beam ignores everything projectile-shaped. The builder and the txt reference spell this out per type.
- **The drone gadgets** (`Module Active Drone …`) are a different module type that holds a *unit* instead of a weapon, so they can't be used as a template. Use a minion-spawner weapon and set `minionPrefab` instead.
- **A pet's own stats** (its damage, health, weapon) live on its unit prefab and aren't editable from JSON.
- `Resource Money` can't power a weapon — the mod reverts it with a warning rather than letting the game hang.

Bugs are likely; this moves fast. Log output is verbose on purpose, so start there.

---

## Companion mod

**Module Forge** (separate repo — https://github.com/Sugarheady/ModuleForge) does the same thing for grid *modules*. The two cooperate automatically when both are installed — they share one burn engine, add their pierce caps together into a single number, and merge their stat-card lines instead of double-reporting.

---

