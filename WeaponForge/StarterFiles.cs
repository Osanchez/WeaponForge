using System.IO;

namespace WeaponForge
{
    // Writes the example weapon + README into a freshly created
    // "weapons" folder so first-time users have something that works
    // and something to read.
    public static class StarterFiles
    {
        public static void Write(string folder)
        {
            File.WriteAllText(
                Path.Combine(folder, "ExamplePopper.json"),
@"{
  ""name"": ""ForgeExample"",
  ""displayName"": ""FORGE EXAMPLE"",
  ""description"": ""A fast-firing cyan popper. Edit ExamplePopper.json to change me, or copy the file to make more weapons."",
  ""template"": ""Module Weapon White Popper"",
  ""baseLoadout"": ""Starter_Popper"",
  ""weapon"": {
    ""fireRate"": 8,
    ""cost"": 0.5,
    ""damage"": { ""amount"": 1.5, ""damageType"": ""Resource White"" },
    ""projectileCount"": 1,
    ""spread"": 0,
    ""angleVariance"": 3,
    ""projectileSpeed"": 30,
    ""projectileColor"": ""#33ddff"",
    ""projectileScale"": 1.25
  },
  ""module"": {
    ""color"": ""ColorBlue"",
    ""resourceGain"": { ""resource"": ""Resource White"", ""amount"": 12 }
  }
}
");

            File.WriteAllText(
                Path.Combine(folder, "README.txt"),
@"WEAPON FORGE - custom weapon definitions
=========================================

Every *.json file in this folder becomes a starting loadout in PUNK.
Copy ExamplePopper.json, rename it, and edit away. Errors are logged
to BepInEx/LogOutput.log with the file name.

Weapons are built and registered at game startup, so saving mid-run
and using Continue works. Edits are read at startup / when the ship-
select screen opens - restart the game to see changes.

TOP-LEVEL KEYS
--------------
name         (required) unique internal id, letters/numbers only
template     (required) the weapon to clone - a weapon module
             (""Module Weapon White Popper"") OR a raw weapon asset,
             including enemy weapons (""Weapon_Grunt"", ""Weapon Enemy
             Soldier""). Decides behavior + visuals/projectile model.
slot         where it equips (default ""primary""):
               ""primary""   - left click
               ""secondary"" - right click
               ""gadget1"" / ""gadget2"" / ""gadget3"" - the 1/2/3 keys
             Left/right weapons and gadgets use different module types,
             so a weapon can't sit in a gadget slot or vice versa -
             the ""slot"" you pick builds the right kind automatically.
gadgetShell  (gadgets only, optional) which gadget module to clone for
             its slot fit + icon. Default ""Module Active Purple
             AirMines"". Others: Surge, Teardrop, Caps Igniter, Caps
             Vial, Electron Circuit.
source       where the weapon can show up (default ""starter""):
               ""starter""         - only as a new-game loadout pick
               ""loot""            - only as a drop from crates
               ""starterAndLoot""  - both
lootWeight   (loot only, optional) drop chance vs other crate modules,
             default 10. Higher = more common. Crates roll from a
             weighted pool; your weapon joins it at this weight.
displayName  loadout + module card title
description  loadout + module card description
baseLoadout  loadout to clone for the ship/slots (default Starter_Popper)
weapon       { field overrides applied to the cloned weapon }
module       { field overrides applied to the cloned module card }

GADGETS: a gadget fires its weapon when you press its key (1/2/3),
on a cooldown of 1/fireRate, spending ""cost"" of ""resourceUsed"" per
use - so those weapon fields control the gadget's feel. Your gadget
is added ALONGSIDE the base loadout's normal primary weapon.

ENEMY WEAPONS: raw ""Weapon ..."" assets (many used by enemies) can be
used as templates. Most resolve fine; if one isn't loaded in memory
when you reach the menu, the log will say it wasn't found - pick a
different one.

VISUALS (inside ""weapon"")
--------------------------
projectileColor  recolor the projectile / beam. ""#33ddff"" hex, an
                 HTML name (""cyan""), a game color (""ColorBlue""), or
                 ""rainbow"" for an animated RGB cycle.
                 Projectiles: tints sprites/trails/particles.
                 Hitscan: tints the beam sprite, light, impact.
rainbowSpeed     (rainbow only) hue cycles per second, default 0.5.
projectileScale  size multiplier. Projectiles: bigger shot + matching
                 hit radius. Hitscan: beam thickness. (Minions: not
                 scaled - only recolored - to avoid breaking them.)

MODULE EXTRAS (inside ""module"")
--------------------------------
icon          module card icon sprite name, e.g. ""HUD_GridTiles_8""
              (the weapon icons). The builder page has a dropdown to
              pick one by weapon name. A few: Popper=13, Bolt=1,
              Dart/Rocket=29, Shotgun=7, Laser=8, AirMine=25, Drone=19
color         game ColorAsset name (ColorWhite, ColorOrange,
              ColorPurple, ColorBlue, ColorRed, ColorYellow,
              Color Tech, ColorPower) OR a hex value ""#7fd4ff""
resourceGain  { ""resource"": ""Resource White"", ""amount"": 12 }
              change which resource (and how much) equipping this
              weapon adds to your ship's tanks
powerNodes    how many power cores can attach to the module (the
              ""0 / N"" cap in the grid). A number = fixed (""powerNodes"":
              6 always gives 6), or a range { ""min"":4, ""max"":8 } for
              a random roll. The Popper is normally 2-3.

TEMPLATES (behavior comes from the type)
----------------------------------------
Projectile guns (machine guns, rifles, shotguns, missiles):
  Module Weapon White Popper / White Bolt / White Dart / White Shotgun
  / White Blades / White Derbis / White DiscGun / White Worm
  / Purple Marbles / Purple Flack / Purple Shard / Purple Rocket
  / Caps Rocket / Electron Rocket / Electron Zapper / Caps Flame ...
Hitscan beams:  Module Weapon Caps Laser
Lobbed physics: Module Weapon Caps Loon / Purple Can / Purple ClusterCube
                / Tech Dandelion
Minion pets:    Module Weapon Fly

WEAPON FIELDS (all weapon types)
--------------------------------
damage: { amount, damageType }   fireRate     warmupTime
burstSize    burstDelay          projectileCount   spread
angleVariance    angleOffset     knockbackForce    pushForce
resourceUsed (e.g. ""Resource White"")   cost
barrelLength     shootSfx/startSfx/... (must match existing game sfx ids)
explosion: { radius, damages: [ { amount, damageType } ] }
discharge: { damage: {...}, chainLength, subSystem: ""Player"" }
aimAssistData: { enabled, maxAngle, isPredictive }

PROJECTILE WEAPON FIELDS
------------------------
projectileSpeed    projectileSpeedVariance    projectileRadius
usePhysics         collidesWithSlime
projectilePrefab (asset name - steal another weapon's projectile!)
rangeData:    { enabled, range, variance, slowDown, destroyWhenReached,
                spawnExplosion, fireSub }
lifetimeData: { enabled, time, timeVariance, spawnExplosion, fireSub,
                discharge }
homingData:   { enabled, targetMode: ""AutoSeekWhenShot"", acceleration,
                torque, maxSpeed, maxAngularVelocity }
piercingData: { enabled, damageRepeatDelay, knockBackRepeatDelay }
projectileBounceData: { enabled }
movementNoiseData:    { enabled, angle, frequency }
impactBehaviour:      { enabled, spawnExplosion, fireSub, discharge }
electricityData:      { enabled, isSource, chainLength, conductivity,
                        damage: {...}, ... }

HITSCAN WEAPON FIELDS
---------------------
range    rayWidth    damageRepeatDelay

PHYSICS WEAPON FIELDS
---------------------
projectileSpeed    projectileSpeedVariance    projectilePrefab

MINION SPAWNER FIELDS
---------------------
minionPrefab    projectileSpeed    minionFacesShootDirection

NOTES
-----
- Resource names: ""Resource White"" (stamina), ""Resource Caps""
  (orange), ""Resource Purple"", ""Resource Electron"" (blue),
  ""Resource Health"" (red), ""Resource Money"" (yellow), ""Resource
  Fuel"", ""Resource Tech"". Shorthand ""White"" etc. also works.
- Asset references are looked up by name; use the AssetRipper export
  to find prefab/sprite/module names.
- Some game fields have quirky spelling: ""multiplyer"",
  ""repeatedDropChanceMultiplyer"", ""lowTreshold"". Match them exactly.
- Any field you leave out keeps the template's original value.
- A field name that doesn't exist on the target logs a warning to
  the BepInEx log and is skipped.
");
        }
    }
}
