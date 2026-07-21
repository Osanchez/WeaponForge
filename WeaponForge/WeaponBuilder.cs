using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Logging;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace WeaponForge
{
    // Turns one weapon definition JSON file into a configured, ready-to-
    // register module (clone of a template module + weapon with the JSON
    // overrides applied). Does NOT touch the loadout pool or the registry
    // — ForgeRegistry owns those steps.
    public static class WeaponBuilder
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("WeaponForge");

        // Returns null if the weapon can't/shouldn't be built (already
        // built, missing required keys, or missing assets).
        public static ForgeEntry BuildModule(
            string filePath,
            HashSet<string> alreadyBuilt)
        {
            string fileName = Path.GetFileName(filePath);

            JObject root =
                JObject.Parse(File.ReadAllText(filePath));

            string name = (string)root["name"];

            if (string.IsNullOrEmpty(name))
            {
                Log.LogError(
                    fileName + ": missing required \"name\"");
                return null;
            }

            string loadoutName = "Forge_" + name;

            if (alreadyBuilt != null &&
                alreadyBuilt.Contains(loadoutName))
            {
                return null;
            }

            string templateName = (string)root["template"];

            if (string.IsNullOrEmpty(templateName))
            {
                Log.LogError(
                    fileName + ": missing required \"template\" " +
                    "(a weapon module like \"Module Weapon White " +
                    "Popper\", or a raw weapon like \"Weapon Grunt\")");
                return null;
            }

            // slot decides where the weapon goes and therefore what
            // kind of module shell wraps it. Weapon-type modules only
            // fit weapon slots; gadget (active) modules only fit the
            // 1/2/3 slots - that restriction is the game's ModuleType
            // compatibility, which we get right by cloning a shell of
            // the correct type.
            string slot =
                ((string)root["slot"] ?? "primary")
                    .Trim().ToLowerInvariant();

            bool isGadget =
                slot == "gadget1" || slot == "gadget2" ||
                slot == "gadget3" || slot == "gadget";

            if (slot == "gadget")
                slot = "gadget1";

            // Resolve the template. It can be a weapon module, a gadget
            // module (WeaponBasedActiveModuleData - e.g. Air Mine), or a
            // raw weapon asset (enemy weapons). The weapon BEHAVIOR comes
            // from whichever it is; the SLOT decides the module type we
            // wrap it in.
            var templateModule =
                JsonFieldMapper.FindAsset(
                    typeof(WeaponModuleData),
                    templateName) as WeaponModuleData;

            var templateGadget =
                templateModule != null
                    ? null
                    : JsonFieldMapper.FindAsset(
                        typeof(WeaponBasedActiveModuleData),
                        templateName) as WeaponBasedActiveModuleData;

            WeaponData weaponSource;

            if (templateModule != null)
                weaponSource = templateModule.weapon;
            else if (templateGadget != null)
                weaponSource = templateGadget.weaponData;
            else
                weaponSource =
                    JsonFieldMapper.FindAsset(
                        typeof(WeaponData), templateName) as WeaponData;

            if (weaponSource == null)
            {
                Log.LogError(
                    fileName + ": template '" + templateName +
                    "' not found or has no weapon (expected a weapon " +
                    "module, a gadget module, or a weapon asset)");
                return null;
            }

            string displayName =
                (string)root["displayName"] ??
                name.ToUpperInvariant();

            string description =
                (string)root["description"] ??
                "Custom weapon built by Weapon Forge.";

            var weapon =
                ScriptableObject.Instantiate(weaponSource);

            weapon.name = "Forge Weapon " + name;

            // Build the module shell of the right type for the slot,
            // preferring to clone the template itself when its native
            // type already matches the slot (keeps its icon/behavior).
            ModuleData module =
                BuildShell(
                    isGadget,
                    templateModule,
                    templateGadget,
                    fileName);

            if (module == null)
                return null;

            module.name = "Forge Module " + name;

            var idField =
                typeof(ModuleData).GetField(
                    "id",
                    BindingFlags.NonPublic |
                    BindingFlags.Instance);

            if (idField != null)
            {
                idField.SetValue(
                    module,
                    "FORGE-" + name.ToUpperInvariant());
            }

            AssignWeapon(module, weapon);

            module.displayName = displayName;
            module.description = description;

            string projectileColor = null;
            float? projectileScale = null;
            float rainbowSpeed = 0.5f;

            var weaponJson = root["weapon"] as JObject;

            // Remember the template's resources so we can restore them
            // if the JSON tries to switch to an unusable (shared) one.
            Resource originalResourceUsed = weapon.resourceUsed;
            Resource originalDamageType = weapon.damage.damageType;

            if (weaponJson != null)
            {
                // Pull custom visual aliases out before the generic
                // mapper sees them (they aren't real weapon fields).
                projectileColor =
                    (string)weaponJson["projectileColor"];

                projectileScale =
                    (float?)weaponJson["projectileScale"];

                rainbowSpeed =
                    (float?)weaponJson["rainbowSpeed"] ?? 0.5f;

                weaponJson.Remove("projectileColor");
                weaponJson.Remove("projectileScale");
                weaponJson.Remove("rainbowSpeed");

                JsonFieldMapper.Apply(
                    weapon,
                    weaponJson,
                    name + ".weapon");
            }

            // A weapon that fires from a SHARED resource (e.g. Money)
            // makes the game install a per-unit ammo tank that collides
            // with the run-wide shared tank -> duplicate-key crash that
            // hangs loading. Fall back to the template's resource.
            if (weapon.resourceUsed != null &&
                weapon.resourceUsed.isShared)
            {
                Log.LogWarning(
                    fileName + ": resourceUsed '" +
                    weapon.resourceUsed.name + "' is a shared/currency " +
                    "resource and can't power a weapon (it would hang " +
                    "the game) - keeping '" +
                    (originalResourceUsed != null
                        ? originalResourceUsed.name
                        : "template default") + "' instead.");

                weapon.resourceUsed = originalResourceUsed;
            }

            // Same story for the damage element: a shared resource
            // (Money) as damageType is busted, so revert it.
            if (weapon.damage.damageType != null &&
                weapon.damage.damageType.isShared)
            {
                Log.LogWarning(
                    fileName + ": damage type '" +
                    weapon.damage.damageType.name + "' is a shared/" +
                    "currency resource and isn't usable - keeping '" +
                    (originalDamageType != null
                        ? originalDamageType.name
                        : "template default") + "' instead.");

                var dmg = weapon.damage;
                dmg.damageType = originalDamageType;
                weapon.damage = dmg;
            }

            // target: who the weapon hurts. "enemies" (default) makes it
            // hit enemies and not the player - this also fixes enemy
            // weapon templates, which otherwise only hurt the player.
            // "player" keeps the original enemy-style targeting.
            string target =
                ((string)root["target"] ?? "enemies")
                    .Trim().ToLowerInvariant();

            ApplyVisuals(
                weapon,
                projectileColor,
                projectileScale,
                rainbowSpeed,
                target,
                fileName);

            var moduleJson = root["module"] as JObject;

            if (moduleJson != null)
            {
                // Friendly aliases handled here, not real ModuleData
                // fields — pull them out before the generic mapper.
                var resourceGain =
                    moduleJson["resourceGain"] as JObject;

                JToken powerNodes = moduleJson["powerNodes"];

                if (resourceGain != null)
                    moduleJson.Remove("resourceGain");

                if (powerNodes != null)
                    moduleJson.Remove("powerNodes");

                JsonFieldMapper.Apply(
                    module,
                    moduleJson,
                    name + ".module");

                if (resourceGain != null)
                {
                    ApplyResourceGain(
                        module,
                        resourceGain,
                        fileName);
                }

                if (powerNodes != null)
                {
                    ApplyPowerNodes(module, powerNodes, fileName);
                }
            }

            Log.LogInfo(
                "Built weapon '" + displayName +
                "' from " + fileName);

            // source: where the weapon can appear. "starter" (default),
            // "loot", or "starterAndLoot"/"both".
            string source =
                ((string)root["source"] ?? "starter")
                    .Trim().ToLowerInvariant();

            bool inStarter =
                source == "starter" ||
                source == "starterandloot" || source == "both";

            bool inLoot =
                source == "loot" ||
                source == "starterandloot" || source == "both";

            // Unknown value -> default to starter so it isn't lost.
            if (!inStarter && !inLoot)
                inStarter = true;

            return new ForgeEntry
            {
                loadoutName = loadoutName,
                displayName = displayName,
                description = description,
                baseLoadoutName =
                    (string)root["baseLoadout"] ?? "Starter_Popper",
                module = module,
                slot = slot,
                inStarter = inStarter,
                inLoot = inLoot,
                lootWeight =
                    (float?)root["lootWeight"] ?? 10f
            };
        }

        // Default shells to clone for the module type each slot needs.
        // The shell supplies the ModuleType (weapon vs active) that the
        // slot's compatibility check requires, plus icon/plumbing.
        private const string DefaultWeaponShell =
            "Module Weapon White Popper";
        private const string DefaultGadgetShell =
            "Module Active Purple AirMines";

        private static ModuleData BuildShell(
            bool isGadget,
            WeaponModuleData templateModule,
            WeaponBasedActiveModuleData templateGadget,
            string fileName)
        {
            if (!isGadget)
            {
                // Weapon slot: clone the template weapon module directly
                // when it is one (keeps its icon/color). Otherwise (raw
                // weapon, or a gadget template dropped into a weapon
                // slot) clone the default weapon-module shell for its
                // weapon ModuleType.
                var shell = templateModule;

                if (shell == null)
                {
                    shell =
                        JsonFieldMapper.FindAsset(
                            typeof(WeaponModuleData),
                            DefaultWeaponShell) as WeaponModuleData;
                }

                if (shell == null)
                {
                    Log.LogError(
                        fileName + ": weapon module shell '" +
                        DefaultWeaponShell + "' not found");
                    return null;
                }

                return ScriptableObject.Instantiate(shell);
            }

            // Gadget slot: clone the template gadget module directly when
            // the template IS a gadget (keeps its icon + native gadget
            // behavior). Otherwise (a weapon template turned into a
            // gadget) clone the default gadget shell for its "active"
            // ModuleType.
            var gadgetShell = templateGadget;

            if (gadgetShell == null)
            {
                gadgetShell =
                    JsonFieldMapper.FindAsset(
                        typeof(WeaponBasedActiveModuleData),
                        DefaultGadgetShell)
                        as WeaponBasedActiveModuleData;
            }

            if (gadgetShell == null)
            {
                Log.LogError(
                    fileName + ": gadget shell '" +
                    DefaultGadgetShell + "' not found");
                return null;
            }

            return ScriptableObject.Instantiate(gadgetShell);
        }

        // Weapon-module shells store the weapon in `weapon`, gadget
        // shells in `weaponData`.
        private static void AssignWeapon(
            ModuleData module,
            WeaponData weapon)
        {
            var weaponModule = module as WeaponModuleData;

            if (weaponModule != null)
            {
                weaponModule.weapon = weapon;
                return;
            }

            var gadgetModule = module as WeaponBasedActiveModuleData;

            if (gadgetModule != null)
            {
                gadgetModule.weaponData = weapon;
            }
        }

        // Recolor / resize the weapon's projectile or beam AND set who
        // it hurts (target). Prefabs are cloned only when something
        // actually changes. Behavior per weapon type is documented in
        // the README / builder page.
        private static void ApplyVisuals(
            WeaponData weapon,
            string colorText,
            float? scale,
            float rainbowSpeed,
            string target,
            string fileName)
        {
            Color? color = null;
            bool rainbow = false;

            if (!string.IsNullOrEmpty(colorText))
            {
                string c = colorText.Trim();

                if (c.Equals("rainbow", StringComparison.OrdinalIgnoreCase) ||
                    c.Equals("rgb", StringComparison.OrdinalIgnoreCase))
                {
                    rainbow = true;
                }
                else
                {
                    Color parsed;

                    if (VisualCustomizer.TryParseColor(c, out parsed))
                    {
                        color = parsed;
                    }
                    else
                    {
                        Log.LogWarning(
                            fileName + ": projectileColor '" +
                            colorText + "' is not a valid color");
                    }
                }
            }

            bool hasVisual =
                color.HasValue || rainbow || scale.HasValue;

            int fromLayer = VisualCustomizer.FactionFromLayer(target);
            int toLayer = VisualCustomizer.FactionToLayer(target);

            var projectileData = weapon as ProjectileWeaponData;

            if (projectileData != null)
            {
                var newProjectile =
                    ReskinProjectile(
                        projectileData.projectilePrefab != null
                            ? projectileData.projectilePrefab.gameObject
                            : null,
                        color, rainbow, rainbowSpeed, scale,
                        fromLayer, toLayer, hasVisual);

                if (newProjectile != null)
                {
                    var comp =
                        newProjectile
                            .GetComponentInChildren<Projectile>(true);

                    if (comp != null)
                        projectileData.projectilePrefab = comp;

                    if (scale.HasValue)
                        projectileData.projectileRadius *= scale.Value;
                }

                if (projectileData.usePhysics &&
                    projectileData.physicsProjectilePrefab != null)
                {
                    var pp =
                        ReskinProjectile(
                            projectileData
                                .physicsProjectilePrefab.gameObject,
                            color, rainbow, rainbowSpeed, scale,
                            fromLayer, toLayer, hasVisual);

                    if (pp != null)
                    {
                        var comp =
                            pp.GetComponentInChildren<PhysicsProjectile>(
                                true);

                        if (comp != null)
                            projectileData.physicsProjectilePrefab = comp;
                    }
                }

                return;
            }

            var hitscanData = weapon as HitscanWeaponData;

            if (hitscanData != null)
            {
                // Hitscan targeting is purely the layerMask (a data
                // field - no prefab clone needed).
                hitscanData.layerMask =
                    VisualCustomizer.HitscanMask(target);

                if (hasVisual && hitscanData.visual != null)
                {
                    GameObject clone =
                        VisualCustomizer.ClonePrefab(
                            hitscanData.visual.gameObject);

                    var visual =
                        clone.GetComponent<HitscanWeaponVisual>();

                    Paint(clone, color, rainbow, rainbowSpeed);

                    if (scale.HasValue)
                        VisualCustomizer.ScaleBeamThickness(
                            visual, scale.Value);

                    hitscanData.visual = visual;
                }

                return;
            }

            var physicsData = weapon as PhysicsWeaponData;

            if (physicsData != null)
            {
                var pp =
                    ReskinProjectile(
                        physicsData.projectilePrefab != null
                            ? physicsData.projectilePrefab.gameObject
                            : null,
                        color, rainbow, rainbowSpeed, scale,
                        fromLayer, toLayer, hasVisual);

                if (pp != null)
                {
                    var comp =
                        pp.GetComponentInChildren<Rigidbody2D>(true);

                    if (comp != null)
                        physicsData.projectilePrefab = comp;
                }

                return;
            }

            var minionData = weapon as MinionSpawnerWeaponData;

            if (minionData != null)
            {
                // Minions have their own Unit faction (not a projectile
                // layer), so "target" doesn't apply here - only recolor.
                // Scaling a Unit can break its AI/colliders, so skip it.
                if (minionData.minionPrefab != null &&
                    (color.HasValue || rainbow))
                {
                    GameObject clone =
                        VisualCustomizer.ClonePrefab(
                            minionData.minionPrefab.gameObject);

                    Paint(clone, color, rainbow, rainbowSpeed);

                    minionData.minionPrefab =
                        clone.GetComponent<Unit>();
                }
            }
        }

        // Clone + recolor/scale + re-faction a projectile prefab, but
        // only if something actually changes. Re-factioning happens ONLY
        // when the prefab's root is on the "from" (wrong-faction) layer,
        // i.e. it's genuinely an enemy weapon needing to be flipped - a
        // weapon already on the right faction (e.g. a player gadget like
        // air mines) is left alone so its mixed collision layers survive.
        // Returns the clone, or null if no change was needed.
        private static GameObject ReskinProjectile(
            GameObject original,
            Color? color,
            bool rainbow,
            float rainbowSpeed,
            float? scale,
            int fromLayer,
            int toLayer,
            bool hasVisual)
        {
            if (original == null)
                return null;

            bool needFaction =
                fromLayer >= 0 && toLayer >= 0 &&
                original.layer == fromLayer;

            if (!hasVisual && !needFaction)
                return null;

            GameObject clone =
                VisualCustomizer.ClonePrefab(original);

            Paint(clone, color, rainbow, rainbowSpeed);

            if (scale.HasValue)
                VisualCustomizer.Scale(clone, scale.Value);

            if (needFaction)
                VisualCustomizer.RemapLayer(clone, fromLayer, toLayer);

            return clone;
        }

        // Static color or animated rainbow, whichever was requested.
        private static void Paint(
            GameObject clone,
            Color? color,
            bool rainbow,
            float rainbowSpeed)
        {
            if (rainbow)
            {
                VisualCustomizer.ApplyRainbow(clone, rainbowSpeed);
            }
            else if (color.HasValue)
            {
                VisualCustomizer.Recolor(clone, color.Value);
            }
        }

        // Sets how many power cores can attach to the weapon module -
        // the "0 / N" cap in the grid. The game rolls
        // Random.Range(powerLevel.Min, powerLevel.Max) (Max exclusive),
        // so we treat the JSON max as inclusive and add 1.
        //   "powerNodes": 6                -> always 6
        //   "powerNodes": { "min":4,"max":8 } -> random 4..8
        private static void ApplyPowerNodes(
            ModuleData module,
            JToken powerNodes,
            string fileName)
        {
            int min;
            int max;

            var range = powerNodes as JObject;

            if (range != null)
            {
                min = (int?)range["min"] ?? 1;
                max = (int?)range["max"] ?? min;
            }
            else
            {
                min = (int)powerNodes;
                max = min;
            }

            if (min < 0) min = 0;
            if (max < min) max = min;

            try
            {
                // powerLevel is a MyBox MinMaxInt struct field; set its
                // Min/Max through the boxed value (Max exclusive).
                FieldInfo plField =
                    typeof(ModuleData).GetField(
                        "powerLevel",
                        BindingFlags.Public | BindingFlags.Instance);

                if (plField == null)
                {
                    Log.LogWarning(
                        fileName + ": powerLevel field not found");
                    return;
                }

                object boxed = plField.GetValue(module);
                Type t = boxed.GetType();

                t.GetField("Min").SetValue(boxed, min);
                t.GetField("Max").SetValue(boxed, max + 1);

                plField.SetValue(module, boxed);
            }
            catch (Exception e)
            {
                Log.LogWarning(
                    fileName + ": failed to set powerNodes: " +
                    e.Message);
            }
        }

        // Changes which resource the module grants when equipped (and
        // how much) by retargeting the module's ModifyResourceCapacity
        // effects. JSON: "resourceGain": { "resource": "...", "amount": n }
        private static void ApplyResourceGain(
            ModuleData module,
            JObject resourceGain,
            string fileName)
        {
            string resourceName =
                (string)resourceGain["resource"];

            float? amount =
                (float?)resourceGain["amount"];

            Resource resource = null;

            if (!string.IsNullOrEmpty(resourceName))
            {
                resource =
                    JsonFieldMapper.FindAsset(
                        typeof(Resource),
                        resourceName) as Resource;

                if (resource == null)
                {
                    Log.LogWarning(
                        fileName +
                        ": resourceGain resource '" +
                        resourceName + "' not found");
                }
                else if (resource.isShared)
                {
                    // Shared resources (e.g. Money) are managed by the
                    // run-wide shared-tank system, not per-unit. Giving
                    // one as capacity installs a duplicate tank and
                    // throws during unit setup, which hangs loading.
                    Log.LogWarning(
                        fileName + ": resourceGain resource '" +
                        resourceName + "' is a shared resource and " +
                        "can't be gained per-weapon - ignoring it " +
                        "(this would otherwise hang the game).");
                    return;
                }
            }

            bool found = false;

            foreach (var effect in module.effects)
            {
                var capacity =
                    effect as ModifyResourceCapacity;

                if (capacity == null)
                    continue;

                found = true;

                if (resource != null)
                    capacity.resource = resource;

                if (amount.HasValue)
                    capacity.delta.baseValue = amount.Value;
            }

            if (!found && (resource != null || amount.HasValue))
            {
                var capacity = new ModifyResourceCapacity();

                capacity.resource = resource;
                capacity.delta.baseValue = amount ?? 10f;
                capacity.delta.increaseMethod =
                    FloatSeries.IncreaseMethod.Add;
                capacity.delta.change = 0f;

                module.effects.Add(capacity);
            }
        }
    }
}
