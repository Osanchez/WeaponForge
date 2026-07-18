================================================================
 WEAPON FORGE - HOW TO MAKE CUSTOM WEAPONS WITH JSON
================================================================

Weapon Forge lets you create new starting weapons for PUNK by
writing small JSON text files. No programming needed. The DLL does
the work; the JSON files are the weapons.

----------------------------------------------------------------
 1. HOW IT WORKS
----------------------------------------------------------------

When the loadout (ship select) screen opens, Weapon Forge:

  1. Looks in the "weapons" folder that sits next to
     WeaponForge.dll (inside BepInEx\plugins\...).
  2. Reads every *.json file in it.
  3. For each file it CLONES an existing game weapon (the
     "template"), then overwrites any stats you listed in the
     file. Everything you don't mention stays exactly like the
     template.
  4. Adds the result to the loadout list as a new starting ship.

Because your weapon starts life as a clone of a real game weapon,
it automatically gets that weapon's projectile model, particles,
sounds, and behavior class. Your JSON then bends it into whatever
you want: faster, meaner, homing, exploding, chaining, etc.

YES - editing the JSON is enough. Change a number, save, restart
the game, and the weapon behaves differently. The DLL never needs
to be rebuilt for stat/physics/property changes.

IMPORTANT: files are read when the loadout screen first loads.
Edits made while the game is running do NOT hot-reload - restart
the game (or fully quit to desktop and relaunch) to see changes.

----------------------------------------------------------------
 2. QUICK START
----------------------------------------------------------------

  1. Run the game once with WeaponForge.dll installed. The
     "weapons" folder appears next to the DLL with an example
     file (ExamplePopper.json) already inside.
  2. Copy ExamplePopper.json, rename the copy (MyGun.json).
  3. Open it in Notepad. Change "name" to something unique,
     change "displayName" to what you want on the menu card.
  4. Edit stats. Save. Launch the game.
  5. Your weapon is a new entry on the loadout select screen.

If something is wrong with a file, the game does not crash - the
weapon is skipped and the reason is written to:
     <game folder>\BepInEx\LogOutput.log
Search the log for "WeaponForge".

----------------------------------------------------------------
 3. FILE STRUCTURE
----------------------------------------------------------------

{
  "name": "MyGun",                <- REQUIRED. Unique internal id.
                                     Letters/numbers, no spaces.
  "displayName": "MY GUN",        <- Name shown on the menu card.
  "description": "It shoots.",    <- Card description text.
  "template": "Module Weapon White Popper",
                                  <- REQUIRED. Which game weapon to
                                     clone. Decides base behavior,
                                     projectile model, sounds.
  "baseLoadout": "Starter_Popper",<- Optional. Which starting ship
                                     loadout to clone for the ship
                                     and module slots.
  "weapon": {                     <- Stat overrides for the weapon.
     ...see section 5...
  },
  "module": {                     <- Optional overrides for the
     ...see section 8...             module card (icon, color...).
  }
}

Rules of thumb:
- Anything you leave out keeps the template's value.
- Field names are not case-sensitive.
- A field name the game doesn't know logs a warning and is
  skipped (check the log if a change doesn't seem to apply).

----------------------------------------------------------------
 4. TEMPLATES - PICKING YOUR BASE WEAPON
----------------------------------------------------------------

The template decides the weapon's TYPE. There are four types, and
each type has its own extra fields (section 6).

PROJECTILE WEAPONS (bullets, machine guns, rifles, missiles):
  Module Weapon White Popper      - basic machine gun
  Module Weapon White Bolt        - rifle-style bolt
  Module Weapon White Dart        - darts / missiles
  Module Weapon White Shotgun     - spread shot
  Module Weapon White Blades      - blades
  Module Weapon White Derbis      - debris chunks
  Module Weapon White DiscGun     - discs
  Module Weapon Purple Marbles    - bouncing marbles
  Module Weapon Purple Flack      - flak burst
  Module Weapon Purple Shard      - shards
  Module Weapon Purple Rocket     - rockets
  Module Weapon Caps Rocket       - rockets (caps resource)
  Module Weapon Caps Flame        - flamethrower-ish
  Module Weapon Electron Rocket   - electric rocket
  Module Weapon Electron Zapper   - electric chain projectile
  Module Weapon Electron Toroid   - electric toroid
  Module Weapon White Worm

HITSCAN WEAPONS (instant laser beams):
  Module Weapon Caps Laser

PHYSICS WEAPONS (heavy lobbed objects with gravity/physics):
  Module Weapon Caps Loon
  Module Weapon Purple Can
  Module Weapon Purple ClusterCube
  Module Weapon Tech Dandelion    - (the "purple dandelion" - its
                                     real asset name is Tech)

MINION SPAWNERS (pet/drone weapons):
  
  Module Weapon Fly
  

----------------------------------------------------------------
 5. WEAPON FIELDS - ALL WEAPON TYPES
----------------------------------------------------------------

These go inside the "weapon": { } block and work on every type.

  "damage": { "amount": 3, "damageType": "Resource White" }
      Contact damage per hit and its element/resource type.
  "fireRate": 5             Shots per second.
  "warmupTime": 0.5         Seconds of holding fire before the
                            first shot (charge-up).
  "burstSize": 3            Shots per trigger pull (burst fire).
  "burstDelay": 0.08        Seconds between burst shots.
  "projectileCount": 5      Projectiles per shot (shotgun!).
  "spread": 30              Total spread angle in degrees when
                            projectileCount > 1.
  "angleVariance": 4        Random inaccuracy in degrees.
  "angleOffset": 0          Rotates the whole firing pattern.
  "knockbackForce": 2       Recoil pushing YOUR ship backward.
  "pushForce": 1            Force applied to whatever you hit.
  "resourceUsed": "Resource White"
                            Which ammo tank the weapon drains.
                            (White/stam, Caps/orange, Purple,
                             Electron...)
  "cost": 0.5               Ammo drained per shot.
  "barrelLength": 0.5       How far from the ship shots spawn.
  "explosion": {
      "radius": 2,
      "damages": [ { "amount": 4, "damageType": "Resource Caps" } ]
  }
  "discharge": {            Chain lightning burst (needs a
      "damage": { "amount": 3, "damageType": "Resource White" },
      "chainLength": 5,        trigger - see lifetimeData /
      "subSystem": "Player"    impactBehaviour "discharge": true)
  }
  "aimAssistData": { "enabled": true, "maxAngle": 15,
                     "isPredictive": true }

Sound fields (shootSfx, startSfx, warmupSfx, releaseSfx...) exist
but must exactly match sound ids already in the game's audio
database - wrong ids are silent, not broken. Easiest is to keep
the template's sounds.

----------------------------------------------------------------
 6. TYPE-SPECIFIC FIELDS
----------------------------------------------------------------

PROJECTILE templates also accept:

  "projectileSpeed": 30
  "projectileSpeedVariance": 2
  "projectileRadius": 0.2      Hit size of the projectile.
  "usePhysics": false
  "collidesWithSlime": true
  "projectilePrefab": "..."    Asset name of ANOTHER weapon's
                               projectile - steal its look!
  "rangeData": { "enabled": true, "range": 10, "variance": 1,
                 "slowDown": false, "destroyWhenReached": true,
                 "spawnExplosion": false, "fireSub": false }
  "lifetimeData": { "enabled": true, "time": 3,
                    "timeVariance": 0.5, "spawnExplosion": true,
                    "fireSub": false, "discharge": false }
  "homingData": { "enabled": true,
                  "targetMode": "AutoSeekWhenShot",
                  "acceleration": 20, "torque": 200,
                  "maxSpeed": 15, "maxAngularVelocity": 400,
                  "turbulenceTorque": 0,
                  "turbulenceFrequency": 0 }
        targetMode options: FromLockOnly, AutoSeekWhenShot,
                            AutoSeekWhenNeeded
  "piercingData": { "enabled": true, "damageRepeatDelay": 0.2,
                    "knockBackRepeatDelay": 0.2 }
  "projectileBounceData": { "enabled": true }
  "movementNoiseData": { "enabled": true, "angle": 15,
                         "frequency": 6 }
  "impactBehaviour": { "enabled": true, "spawnExplosion": true,
                       "fireSub": false, "discharge": false,
                       "safetyDistance": 1 }
  "electricityData": { "enabled": true, "isSource": true,
                       "chainLength": 6, "conductivity": 20,
                       "minConductivity": 0,
                       "emittedSystem": "Player",
                       "conductedSystem": "Player",
                       "showPreviewBeam": true,
                       "showBeamParticles": true,
                       "damageRadius": 0.75,
                       "damageRepeatDelay": 0.1,
                       "damage": { "amount": 3,
                                   "damageType": "Resource White" } }

HITSCAN templates also accept:

  "range": 12                Beam length.
  "rayWidth": 0.3            Beam thickness.
  "damageRepeatDelay": 0.25  Seconds between damage ticks while
                             the beam stays on a target.

PHYSICS templates also accept:

  "projectileSpeed": 10
  "projectileSpeedVariance": 1
  "projectilePrefab": "..."  (a physics object prefab name)

MINION SPAWNER templates also accept:

  "minionPrefab": "..."      Which unit gets spawned.
  "projectileSpeed": 5       Launch speed of the minion.
  "minionFacesShootDirection": true

----------------------------------------------------------------
 7. MIXING AND MATCHING
----------------------------------------------------------------

Because asset references are looked up by name, you can combine
parts of different weapons:

  - Popper stats with the Dart's missile model:
      "template": "Module Weapon White Popper",
      "weapon": { "projectilePrefab": "<dart projectile name>" }

  - A shotgun that fires homing pellets:
      "template": "Module Weapon White Shotgun",
      "weapon": { "homingData": { "enabled": true,
                  "targetMode": "AutoSeekWhenShot",
                  "acceleration": 15, "torque": 250 } }

  - A rifle whose shots explode AND chain lightning on impact:
      "weapon": { "impactBehaviour": { "enabled": true,
                    "spawnExplosion": true, "discharge": true },
                  "explosion": { "radius": 1.5, "damages":
                    [ { "amount": 3, "damageType": "White" } ] },
                  "discharge": { "chainLength": 4,
                    "subSystem": "Player",
                    "damage": { "amount": 2,
                                "damageType": "White" } } }

Exact prefab/asset names come from an AssetRipper export of the
game - the file names ARE the asset names (drop the .asset/.prefab
extension).

----------------------------------------------------------------
 8. THE MODULE CARD ("module" BLOCK)
----------------------------------------------------------------

Optional block that changes how the weapon's module looks in the
grid/inventory UI and what it grants when equipped:

  "module": {
    "displayName": "MY GUN MK II",   Card title (defaults to the
                                     top-level displayName)
    "description": "...",
    "level": 2,
    "icon": "<sprite asset name>",   Steal any game sprite.
    "color": "ColorPurple",          Card color tint - a game
                                     ColorAsset name OR a hex value
                                     like "#7fd4ff" (a new color is
                                     created on the fly).
    "resourceGain": {                Change which resource (and how
      "resource": "Resource White",  much) equipping this weapon
      "amount": 12                   adds to your ship. Overrides
    }                                the template's bonus (e.g. the
                                     Caps Laser normally grants
                                     orange/caps capacity).
  }

Game ColorAsset names:
  ColorWhite   ColorOrange   ColorPurple   ColorBlue
  ColorRed     ColorYellow   Color Tech    ColorPower

----------------------------------------------------------------
 9. RESOURCE NAMES
----------------------------------------------------------------

  Resource White      white / stamina
  Resource Caps       orange / caps
  Resource Purple     purple
  Resource Electron   electric/blue

Shorthand works too: "damageType": "White" finds Resource White.
Resources decide BOTH what ammo you drain (resourceUsed) and what
damage color/type you deal (damageType) - they don't have to
match.

----------------------------------------------------------------
 10. TROUBLESHOOTING
----------------------------------------------------------------

Weapon doesn't appear on the loadout screen:
  - Check BepInEx\LogOutput.log for "WeaponForge" lines. A
    missing "name"/"template" or a typo'd template name is
    logged with your file's name.
  - Two files with the same "name" - only the first loads.
  - JSON syntax error (missing comma, quote). Paste the file into
    an online JSON validator.

A stat change didn't apply:
  - Restart the game fully - files are read at menu load only.
  - Check the log for a "does not match any field" warning: the
    field name may be misspelled, or it belongs to a different
    weapon type than your template.
  - A few game fields have intentionally odd spelling, e.g.
    "multiplyer" - copy names from this guide exactly.

Weapon appears but is silent / invisible projectiles:
  - You probably swapped in an sfx id or prefab name that doesn't
    exist. Remove the override to fall back to the template's.

What JSON canNOT do (yet):
  - Import brand-new art or sound files from outside the game.
    You can only reference assets that already exist in PUNK.
  - Make weapons drop in-game / appear in shops. Planned for a
    future version.
  - Invent behavior no game weapon has (e.g. a true black-hole
    gun). Stats and combinations only - new mechanics need C#.

================================================================
