using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace WeaponForge
{
    // When the loadout screen populates, load every *.json weapon
    // definition from the "weapons" folder next to WeaponForge.dll
    // and register each one as a starting loadout. On first run the
    // folder is created with a working example and a README.
    [HarmonyPatch(typeof(LoadoutSelector), "Populate")]
    public class ForgeLoadoutPatch
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("WeaponForge");

        static void Prefix(LoadoutSelector __instance)
        {
            try
            {
                var field =
                    typeof(LoadoutSelector).GetField(
                        "loadoutPool",
                        BindingFlags.NonPublic |
                        BindingFlags.Instance);

                if (field == null)
                    return;

                var pool =
                    field.GetValue(__instance) as LoadoutPool;

                if (pool == null)
                    return;

                string folder =
                    Path.Combine(
                        Path.GetDirectoryName(
                            Assembly.GetExecutingAssembly()
                                .Location),
                        "weapons");

                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                    WriteStarterFiles(folder);
                }

                string[] files =
                    Directory.GetFiles(folder, "*.json")
                        .OrderBy(x => x)
                        .ToArray();

                Log.LogInfo(
                    "Loading " + files.Length +
                    " weapon definition(s) from " + folder);

                foreach (string file in files)
                {
                    try
                    {
                        WeaponBuilder.BuildFromJson(file, pool);
                    }
                    catch (Exception e)
                    {
                        Log.LogError(
                            "Failed to build weapon from " +
                            Path.GetFileName(file) + ": " + e);
                    }
                }
            }
            catch (Exception e)
            {
                Log.LogError(
                    "Weapon Forge loadout injection failed: " + e);
            }
        }

        private static void WriteStarterFiles(string folder)
        {
            File.WriteAllText(
                Path.Combine(folder, "ExamplePopper.json"),
@"{
  ""name"": ""ForgeExample"",
  ""displayName"": ""FORGE EXAMPLE"",
  ""description"": ""A fast-firing popper variant. Edit ExamplePopper.json to change me, or copy the file to make more weapons."",
  ""template"": ""Module Weapon White Popper"",
  ""baseLoadout"": ""Starter_Popper"",
  ""weapon"": {
    ""fireRate"": 8,
    ""cost"": 0.5,
    ""damage"": { ""amount"": 1.5, ""damageType"": ""Resource White"" },
    ""projectileCount"": 1,
    ""spread"": 0,
    ""angleVariance"": 3,
    ""projectileSpeed"": 30
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

TOP-LEVEL KEYS
--------------
name         (required) unique internal id, letters/numbers only
template     (required) the module asset to clone - this decides the
             weapon's behavior AND its visuals/projectile model
displayName  loadout + module card title
description  loadout + module card description
baseLoadout  loadout to clone for the ship/slots (default Starter_Popper)
weapon       { field overrides applied to the cloned weapon }
module       { field overrides applied to the cloned module card:
               icon, color, level, powerLevel, displayName, ...
               plus the special key below }

MODULE EXTRAS
-------------
""module"": {
  ""color"": ""ColorPurple""       game ColorAsset name (ColorWhite,
                               ColorOrange, ColorPurple, ColorBlue,
                               ColorRed, ColorYellow, Color Tech,
                               ColorPower) OR a hex value ""#7fd4ff""
  ""resourceGain"": {              change which resource (and how
    ""resource"": ""Resource White"",  much) equipping this weapon
    ""amount"": 12                 adds to your ship's tanks
  }
}

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
- Resource names: ""Resource White"", ""Resource Caps"", ""Resource
  Purple"", ""Resource Electron""... (shorthand ""White"" also works)
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
