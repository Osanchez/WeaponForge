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

        // A subEmitter names another weapon, which may well be one of YOUR
        // weapons sitting in a file that sorts later in the folder. Resolving
        // it during the build made success depend on filename order -
        // "cocktail.json" sorts before "cocktailFlame.json", so the cocktail
        // looked for a flame that did not exist yet and silently got none.
        // So the name is parked here and resolved once every file is built,
        // which also lets chains of any depth work regardless of order.
        private class PendingSub
        {
            public WeaponData weapon;
            public string subName;
            public string fileName;
        }

        private static readonly List<PendingSub> _pendingSubs =
            new List<PendingSub>();

        public static void ResolvePendingSubEmitters()
        {
            foreach (PendingSub p in _pendingSubs)
            {
                if (p.weapon == null || string.IsNullOrEmpty(p.subName))
                    continue;

                // Accept the bare name too: a Forge weapon called "Flame"
                // becomes the asset "Forge Weapon Flame", and expecting
                // people to know that prefix is a needless trip hazard.
                var sub = JsonFieldMapper.FindAsset(
                    typeof(WeaponData), p.subName) as WeaponData;

                if (sub == null && !p.subName.StartsWith("Forge Weapon "))
                {
                    sub = JsonFieldMapper.FindAsset(
                        typeof(WeaponData),
                        "Forge Weapon " + p.subName) as WeaponData;
                }

                if (sub == null)
                {
                    Log.LogWarning(
                        p.fileName + ": subEmitter '" + p.subName +
                        "' was not found, so this weapon fires no sub. " +
                        "For one of your own weapons use its \"name\" (or " +
                        "\"Forge Weapon <name>\"); for a stock one use the " +
                        "asset name like \"Weapon Caps Flame\".");
                    continue;
                }

                if (sub == p.weapon)
                {
                    Log.LogWarning(
                        p.fileName + ": subEmitter points at this same " +
                        "weapon - that would recurse forever, so it is " +
                        "ignored.");
                    continue;
                }

                p.weapon.subEmitter = sub;

                Log.LogInfo(
                    p.fileName + ": subEmitter -> '" + sub.name + "'.");
            }

            _pendingSubs.Clear();
        }

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
            string muzzleColor = null;
            float muzzleRgbSpeed = 0.5f;
            string projectileSprite = null;
            string subEmitterName = null;
            string explosionColor = null;
            float explosionRgbSpeed = 0.5f;
            bool spriteOnly = false;
            string projectileMaterial = null;
            bool? projectileGlow = null;
            ForgeTrail.Spec trailSpec = null;
            ForgeBeam.Spec beamSpec = null;

            // Burn options (enemy burn tick-rate + burn color) - not real
            // weapon fields, so pulled out and turned into module effects.
            JToken burnTickRateTok = null;
            string burnColor = null;
            float burnRgbSpeed = 0.5f;
            bool burnColorTerrain = false;

            var weaponJson = root["weapon"] as JObject;

            // Remember the template's resources so we can restore them
            // if the JSON tries to switch to an unusable (shared) one.
            Resource originalResourceUsed = weapon.resourceUsed;
            Resource originalDamageType = weapon.damage.damageType;

            // The template's own sound ids, captured BEFORE the mapper can
            // overwrite them. A custom sound inherits its volume, mixer group
            // and looping from whatever it is replacing, so we need to know
            // what was in each slot to begin with.
            SfxSlots originalSfx = CaptureSfx(weapon);

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

                muzzleColor = (string)weaponJson["muzzleColor"];
                muzzleRgbSpeed =
                    (float?)weaponJson["muzzleRgbSpeed"] ?? 0.5f;

                projectileSprite =
                    (string)weaponJson["projectileSprite"];

                projectileMaterial =
                    (string)weaponJson["projectileMaterial"];
                projectileGlow = (bool?)weaponJson["projectileGlow"];

                // Held back from the mapper and resolved after every file
                // is built - see ResolvePendingSubEmitters.
                subEmitterName = (string)weaponJson["subEmitter"];

                explosionColor = (string)weaponJson["explosionColor"];
                explosionRgbSpeed =
                    (float?)weaponJson["explosionRgbSpeed"] ?? 0.5f;

                spriteOnly =
                    (bool?)weaponJson["projectileSpriteOnly"] ?? false;

                trailSpec =
                    ForgeTrail.Parse(weaponJson["trail"], fileName);

                beamSpec =
                    ForgeBeam.Parse(weaponJson["beam"], fileName);

                burnTickRateTok = weaponJson["burnTickRate"];
                burnColor = (string)weaponJson["burnColor"];
                burnRgbSpeed =
                    (float?)weaponJson["burnRgbSpeed"] ?? 0.5f;
                burnColorTerrain =
                    (bool?)weaponJson["burnColorTerrain"] ?? false;

                weaponJson.Remove("projectileColor");
                weaponJson.Remove("projectileScale");
                weaponJson.Remove("rainbowSpeed");
                weaponJson.Remove("muzzleColor");
                weaponJson.Remove("muzzleRgbSpeed");
                weaponJson.Remove("projectileSprite");
                weaponJson.Remove("projectileMaterial");
                weaponJson.Remove("projectileGlow");
                weaponJson.Remove("subEmitter");
                weaponJson.Remove("explosionColor");
                weaponJson.Remove("explosionRgbSpeed");
                weaponJson.Remove("projectileSpriteOnly");
                weaponJson.Remove("trail");
                weaponJson.Remove("beam");
                weaponJson.Remove("burnTickRate");
                weaponJson.Remove("burnColor");
                weaponJson.Remove("burnRgbSpeed");
                weaponJson.Remove("burnColorTerrain");

                JsonFieldMapper.Apply(
                    weapon,
                    weaponJson,
                    name + ".weapon");

                // Any sound field that now names a file from the "sounds"
                // folder gets swapped for a real registered Sfx id. Done
                // AFTER the mapper because the sfx fields are ordinary
                // strings - the mapper writes them like any other field, and
                // we only reinterpret what it wrote.
                ApplyCustomSounds(weapon, originalSfx, fileName);

                if (!string.IsNullOrEmpty(subEmitterName))
                {
                    _pendingSubs.Add(new PendingSub
                    {
                        weapon = weapon,
                        subName = subEmitterName.Trim(),
                        fileName = fileName
                    });
                }
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
                fileName,
                projectileSprite,
                projectileMaterial,
                projectileGlow,
                explosionColor,
                explosionRgbSpeed,
                spriteOnly,
                trailSpec,
                beamSpec);

            ApplyMuzzleColor(
                weapon,
                muzzleColor,
                muzzleRgbSpeed,
                fileName);

            ApplyBurn(
                module,
                burnTickRateTok,
                burnColor,
                burnRgbSpeed,
                burnColorTerrain,
                fileName);

            ApplyElectric(weapon, root, fileName);

            ApplyOrbit(weapon, root, fileName);

            ApplyPhasing(weapon, root, fileName);

            ApplyPierce(weapon, root, fileName);

            ApplyWave(weapon, root, fileName);

            // After wave: both clone the projectile prefab and both attach a
            // FixedUpdate-driven steerer, so running homing second means it
            // lands on the clone wave already made rather than a stale one.
            ApplyHoming(weapon, root, fileName);

            // Last of the three prefab-cloning motion features, so it lands on
            // the clone the others have already made.
            ApplyRicochet(weapon, root, fileName);

            ApplyTurret(weapon, root, fileName);

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

            // "none" = built and registered, but offered NOWHERE. Use it for
            // helper weapons that only exist to be referenced by something
            // else (a subEmitter stage, turret ammo) - they still resolve by
            // name, they just never show up as a pick, a drop or in a crate.
            bool hidden =
                source == "none" || source == "nowhere" ||
                source == "hidden" || source == "never";

            bool inStarter =
                source == "starter" ||
                source == "starterandloot" || source == "both";

            bool inLoot =
                source == "loot" ||
                source == "starterandloot" || source == "both";

            // Unknown value -> default to starter so it isn't lost. ("none"
            // is a deliberate choice, so it skips that safety net.)
            if (!inStarter && !inLoot && !hidden)
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
                    (float?)root["lootWeight"] ?? 10f,
                inShop =
                    (bool?)root["shop"] ?? false,
                shopPrice =
                    (float?)root["shopPrice"] ?? 100f,
                shopUnlockLevel =
                    (int?)root["shopUnlockLevel"] ?? 1
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
            string fileName,
            string projectileSpriteName,
            string projectileMaterialName,
            bool? projectileGlow,
            string explosionColorText,
            float explosionRgbSpeed,
            bool spriteOnly,
            ForgeTrail.Spec trailSpec,
            ForgeBeam.Spec beamSpec)
        {
            Color? color = null;
            bool rainbow = false;

            // Custom art from the "sprites" folder. Its own namespace, so
            // the name can never clash with the game's ~450 atlas sprites.
            ForgeSpriteLibrary.Art customArt = null;
            if (!string.IsNullOrEmpty(projectileSpriteName))
            {
                if (!ForgeSpriteLibrary.TryGetArt(
                        projectileSpriteName, out customArt))
                {
                    string known = ForgeSpriteLibrary.Count > 0
                        ? " Loaded sprites: " +
                          string.Join(", ",
                              System.Linq.Enumerable.ToArray(
                                  ForgeSpriteLibrary.Names))
                        : " No custom sprites loaded at all - is there a " +
                          "PNG in the 'sprites' folder next to the DLL?";

                    Log.LogWarning(
                        fileName + ": projectileSprite '" +
                        projectileSpriteName + "' was not found." + known);
                }
            }

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

            // Resolve the render material. "glow" is the plain-English
            // switch between the game's own two sprite materials; an
            // explicit name is the escape hatch for any other one.
            string materialName = projectileMaterialName;
            if (string.IsNullOrEmpty(materialName) && projectileGlow.HasValue)
            {
                materialName = projectileGlow.Value
                    ? "EmissiveUnlitSprite"
                    : "SpriteUnlitAA";
            }

            Material customMaterial = null;
            if (!string.IsNullOrEmpty(materialName))
            {
                customMaterial = JsonFieldMapper.FindAsset(
                    typeof(Material), materialName) as Material;

                if (customMaterial == null)
                {
                    Log.LogWarning(
                        fileName + ": material '" + materialName +
                        "' was not found - the shot keeps the template's. " +
                        "The two that matter are 'SpriteUnlitAA' (draws art " +
                        "as painted) and 'EmissiveUnlitSprite' (glows).");
                }
            }

            // Explosion tint. The game gives an explosion no colour of its
            // own - the burst comes from damages[0].damageType - so this is
            // carried on the shot and applied as the blast spawns.
            ForgeExplosionColor explosionTint = null;
            if (!string.IsNullOrEmpty(explosionColorText))
            {
                bool exRainbow = VisualCustomizer.IsRainbow(explosionColorText);
                Color exColor = Color.white;

                if (exRainbow ||
                    VisualCustomizer.TryParseColor(explosionColorText, out exColor))
                {
                    explosionTint = new ForgeExplosionColor
                    {
                        color = exColor,
                        rainbow = exRainbow,
                        rgbSpeed = explosionRgbSpeed
                    };
                }
                else
                {
                    Log.LogWarning(
                        fileName + ": explosionColor '" + explosionColorText +
                        "' is not a hex value, colour name, ColorAsset or " +
                        "\"rainbow\" - ignored.");
                }
            }

            bool hasVisual =
                color.HasValue || rainbow || scale.HasValue ||
                customArt != null || customMaterial != null ||
                explosionTint != null || spriteOnly || trailSpec != null ||
                beamSpec != null;

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
                        fromLayer, toLayer, hasVisual, customArt,
                        customMaterial, explosionTint, spriteOnly,
                        trailSpec);

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
                            fromLayer, toLayer, hasVisual, customArt,
                            customMaterial, explosionTint, spriteOnly,
                            trailSpec);

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

                    // Art before the tint, same order as the projectile path:
                    // the tint writes the renderer's colour, so it has to land
                    // on whichever sprite ends up sitting there.
                    if (beamSpec != null)
                        ForgeBeam.Apply(visual, beamSpec);

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
                        fromLayer, toLayer, hasVisual,
                        trailSpec: trailSpec);

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
            bool hasVisual,
            ForgeSpriteLibrary.Art customArt = null,
            Material customMaterial = null,
            ForgeExplosionColor explosionTint = null,
            bool spriteOnly = false,
            ForgeTrail.Spec trailSpec = null)
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

            // Art first, then colour: the tint lands on the renderer, so it
            // applies to whatever sprite is sitting there.
            if (customArt != null)
                VisualCustomizer.ApplyArt(clone, customArt);

            // After the swap, so the renderer we just wrote to is the one
            // kept and the template's glow/trail extras go quiet.
            if (spriteOnly)
                VisualCustomizer.IsolateSprite(clone);

            // Before the tint: the tint writes sr.color, the material
            // decides how that colour is then shaded.
            if (customMaterial != null)
                VisualCustomizer.SwapMaterial(clone, customMaterial);

            // After IsolateSprite, which switches off every child particle
            // system including any trail. ForgeTrail writes emission and the
            // renderer back on explicitly, so "one sprite, plus a trail" is a
            // legal combination and the two features cannot fight.
            if (trailSpec != null)
                ForgeTrail.Apply(clone, trailSpec);

            // A marker the explosion patch reads off the live shot.
            if (explosionTint != null)
            {
                var tag = clone.AddComponent<ForgeExplosionColor>();
                tag.color = explosionTint.color;
                tag.rainbow = explosionTint.rainbow;
                tag.rgbSpeed = explosionTint.rgbSpeed;
            }

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

        // The seven sound slots a weapon has, as they stood on the template.
        private struct SfxSlots
        {
            public string shoot;
            public string reload;
            public string continuous;
            public string start;
            public string release;
            public string warmup;
            public string explosion;
        }

        private static SfxSlots CaptureSfx(WeaponData w)
        {
            return new SfxSlots
            {
                shoot = w.shootSfx,
                reload = w.reloadSfx,
                continuous = w.continousShootSfx,
                start = w.startSfx,
                release = w.releaseSfx,
                warmup = w.warmupSfx,
                explosion = w.explosion.sfx
            };
        }

        // Swap any sound field that names a file in the "sounds" folder for
        // the Sfx id we registered for it. A field the JSON never touched, or
        // one holding a real game sound id, is left exactly as it was.
        private static void ApplyCustomSounds(
            WeaponData w,
            SfxSlots original,
            string fileName)
        {
            w.shootSfx = SwapSound(
                w.shootSfx, original.shoot, false, "shootSfx", fileName);

            w.reloadSfx = SwapSound(
                w.reloadSfx, original.reload, false, "reloadSfx", fileName);

            w.startSfx = SwapSound(
                w.startSfx, original.start, false, "startSfx", fileName);

            w.releaseSfx = SwapSound(
                w.releaseSfx, original.release, false, "releaseSfx",
                fileName);

            // These two are HELD sounds: Shooter starts them, keeps the
            // handle, and calls AudioManager.Stop later. That only works on a
            // looping Sfx, so the slot forces looping on regardless of what
            // the replaced sound did.
            w.continousShootSfx = SwapSound(
                w.continousShootSfx, original.continuous, true,
                "continousShootSfx", fileName);

            w.warmupSfx = SwapSound(
                w.warmupSfx, original.warmup, true, "warmupSfx", fileName);

            // Explosion is a STRUCT field, so it has to be read out, changed
            // and written back - editing w.explosion.sfx in place would only
            // change a temporary copy.
            string newExplosion = SwapSound(
                w.explosion.sfx, original.explosion, false, "explosion.sfx",
                fileName);

            if (newExplosion != w.explosion.sfx)
            {
                Explosion ex = w.explosion;
                ex.sfx = newExplosion;
                w.explosion = ex;
            }
        }

        private static string SwapSound(
            string current,
            string original,
            bool forceLoop,
            string slot,
            string fileName)
        {
            if (string.IsNullOrEmpty(current))
                return current;

            // Untouched by the JSON - still the template's own sound.
            if (current == original)
                return current;

            string guid = ForgeSfxRegistry.Resolve(
                current, original, forceLoop, fileName);

            if (guid != null)
                return guid;

            // Neither a custom sound nor a real game sound id. This is the
            // silent-weapon trap, so say so loudly: the game's own reaction to
            // an unknown id is to return -1 and play nothing at all.
            if (!ForgeSfxRegistry.KnownGuid(current))
            {
                string known = ForgeSoundLibrary.Count > 0
                    ? " Loaded custom sounds: " +
                      string.Join(", ",
                          System.Linq.Enumerable.ToArray(
                              ForgeSoundLibrary.Names)) + "."
                    : " No custom sounds are loaded - is there an audio " +
                      "file in the 'sounds' folder next to the DLL?";

                Log.LogWarning(
                    fileName + ": " + slot + " is set to '" + current +
                    "', which is neither a custom sound nor a sound id this " +
                    "game has, so that slot will be SILENT." + known +
                    " A custom sound is named after its file, without the " +
                    "extension.");
            }

            return current;
        }

        // Recolor the muzzle flash. The prefab is SHARED - 53 stock
        // weapons point at "MuzzleParticle Popper" - so we tint a private
        // clone and hand the weapon that, otherwise recoloring one weapon
        // would recolor most of the game. Multiply-tinted rather than
        // overwritten so the flash keeps its gradient and alpha fade
        // (see VisualCustomizer.Tint).
        private static void ApplyMuzzleColor(
            WeaponData weapon,
            string muzzleColor,
            float muzzleRgbSpeed,
            string fileName)
        {
            if (weapon == null || string.IsNullOrEmpty(muzzleColor))
                return;

            if (weapon.muzzleParticlePrefab == null)
            {
                Log.LogWarning(
                    fileName + ": muzzleColor was set but this template " +
                    "has no muzzleParticlePrefab - nothing to tint. Set " +
                    "muzzleParticlePrefab too (e.g. \"MuzzleParticle " +
                    "Popper\").");
                return;
            }

            bool rainbow = VisualCustomizer.IsRainbow(muzzleColor);

            Color color = Color.white;
            if (!rainbow &&
                !VisualCustomizer.TryParseColor(muzzleColor, out color))
            {
                Log.LogWarning(
                    fileName + ": muzzleColor '" + muzzleColor +
                    "' is not a hex value, color name, ColorAsset or " +
                    "\"rainbow\" - ignored.");
                return;
            }

            GameObject clone = VisualCustomizer.ClonePrefab(
                weapon.muzzleParticlePrefab.gameObject);

            var ps = clone.GetComponent<ParticleSystem>();
            if (ps == null)
                ps = clone.GetComponentInChildren<ParticleSystem>(true);

            if (ps == null)
            {
                Log.LogWarning(
                    fileName + ": the muzzle prefab '" +
                    weapon.muzzleParticlePrefab.name + "' has no " +
                    "ParticleSystem to tint - muzzleColor ignored.");
                UnityEngine.Object.Destroy(clone);
                return;
            }

            // ClonePrefab stamps HideAndDontSave, but the game spawns THIS
            // prefab itself (one instance per projectile) and Instantiate
            // copies hideFlags - so those instances would stop being
            // cleaned up on scene unload and pile up run after run, which
            // matters most for a gadget weapon (nothing ever disposes it).
            // The clone survives regardless: it lives under the
            // DontDestroyOnLoad holder, and it is the holder being
            // INACTIVE - not the flag - that keeps Awake from firing.
            clone.hideFlags = HideFlags.None;

            // Four stock muzzle prefabs (PopperRed, Laser, LaserRed,
            // CrawlerLaser) bake a colored fade into colorOverLifetime,
            // which Unity multiplies into startColor - so a cyan tint on
            // PopperRed's red ramp would come out black. Drain the ramp of
            // hue first, keeping its brightness curve.
            VisualCustomizer.NeutralizeColorRamp(clone);

            if (rainbow)
                VisualCustomizer.ApplyRainbow(clone, muzzleRgbSpeed, true);
            else
                VisualCustomizer.Tint(clone, color);

            weapon.muzzleParticlePrefab = ps;

            Log.LogInfo(
                fileName + ": muzzle flash tinted " +
                (rainbow ? "rainbow" : muzzleColor) + ".");
        }

        // Phasing: the projectile/beam passes through terrain but still
        // hits enemies. Projectiles get a ForgePhasing tag (a Shoot patch
        // strips the Ground collision bit); hitscan just drops Ground from
        // its layerMask, and can be made effectively unlimited-range.
        private static void ApplyPhasing(
            WeaponData weapon,
            JObject root,
            string fileName)
        {
            bool phasing = (bool?)root["phasing"] ?? false;
            if (!phasing)
                return;

            int ground = LayerMask.NameToLayer("Ground");

            var pw = weapon as ProjectileWeaponData;
            if (pw != null && pw.projectilePrefab != null)
            {
                GameObject clone = VisualCustomizer.ClonePrefab(
                    pw.projectilePrefab.gameObject);
                var comp = clone.GetComponentInChildren<Projectile>(true);
                if (comp != null)
                {
                    // Attach to the SAME GameObject as the Projectile so the
                    // Shoot patch's GetComponent<ForgePhasing>() finds it, and
                    // so it isn't dropped when the prefab is instantiated from
                    // a Projectile that sits on a child of the prefab root.
                    if (comp.GetComponent<ForgePhasing>() == null)
                        comp.gameObject.AddComponent<ForgePhasing>();
                    pw.projectilePrefab = comp;
                }
                ForgeWeaponInfo.SetPhasing(weapon);
                Log.LogInfo(fileName + ": phasing projectile");
            }

            var hs = weapon as HitscanWeaponData;
            if (hs != null && ground >= 0)
            {
                int m = hs.layerMask.value & ~(1 << ground);
                hs.layerMask = m;

                if ((bool?)root["phaseInfiniteRange"] ?? false)
                    hs.range = 1000f;

                ForgeWeaponInfo.SetPhasing(weapon);
                Log.LogInfo(fileName + ": phasing beam (terrain ignored)");
            }
        }

        // Pierce cap: caps the game's otherwise-infinite piercing. Enables
        // piercing and tags the projectile with a ForgePierceCap (a TryHit
        // patch counts distinct enemies and destroys past the limit).
        // Optional per-pierce damage falloff + explode-on-final-hit.
        private static void ApplyPierce(
            WeaponData weapon,
            JObject root,
            string fileName)
        {
            int? limit = (int?)root["pierceLimit"];
            if (!limit.HasValue)
                return;

            var pw = weapon as ProjectileWeaponData;
            if (pw == null || pw.projectilePrefab == null)
            {
                Log.LogWarning(
                    fileName + ": pierceLimit only works on projectile " +
                    "weapons - ignored.");
                return;
            }

            // Piercing must be ON for the projectile to pass through
            // enemies in the first place.
            var pd = pw.piercingData;
            pd.enabled = true;
            if (pd.damageRepeatDelay <= 0f)
                pd.damageRepeatDelay = 0.15f;
            pw.piercingData = pd;

            GameObject clone = VisualCustomizer.ClonePrefab(
                pw.projectilePrefab.gameObject);

            var comp = clone.GetComponentInChildren<Projectile>(true);
            if (comp == null)
            {
                Log.LogWarning(fileName + ": pierce - no Projectile on prefab.");
                return;
            }

            // Attach to the Projectile's OWN GameObject (see phasing note) so
            // the TryHit patch finds it and it survives instantiation.
            var cap = comp.GetComponent<ForgePierceCap>();
            if (cap == null)
                cap = comp.gameObject.AddComponent<ForgePierceCap>();

            cap.limit = Mathf.Max(0, limit.Value);
            cap.falloff = (float?)root["pierceDamageFalloff"] ?? 0f;
            cap.explodeOnLimit = (bool?)root["pierceExplodeOnLimit"] ?? false;

            ForgeWeaponInfo.SetPierce(weapon, cap.limit);
            pw.projectilePrefab = comp;

            Log.LogInfo(
                fileName + ": pierce limit " + cap.limit +
                (cap.falloff > 0f ? " (falloff " + cap.falloff + ")" : "") +
                (cap.explodeOnLimit ? " (explode on last)" : ""));
        }

        // Wave / wobble motion for projectiles.
        //   "wobble": true  -> the game's built-in organic waver
        //     (movementNoiseData) - a Perlin wander. Free; also settable
        //     directly under "weapon".movementNoiseData.
        //   "wave": true     -> a clean, repeating sine "S" (Super Metroid
        //     wave beam). Custom: tags the projectile with ForgeWaveMotion,
        //     which ForgeWavePatch swings each FixedUpdate. waveMode picks
        //     single / synced / helix (see ForgeWaveMotion).
        // Both only apply to projectile weapons.
        private static void ApplyWave(
            WeaponData weapon,
            JObject root,
            string fileName)
        {
            var pw = weapon as ProjectileWeaponData;

            // Organic wobble alias -> the game's own movementNoiseData.
            if ((bool?)root["wobble"] ?? false)
            {
                if (pw != null)
                {
                    var nd = pw.movementNoiseData;
                    nd.enabled = true;
                    nd.angle = (float?)root["wobbleAngle"] ?? 30f;
                    nd.frequency = (float?)root["wobbleFrequency"] ?? 3f;
                    pw.movementNoiseData = nd;
                    Log.LogInfo(
                        fileName + ": organic wobble (angle " + nd.angle +
                        ", freq " + nd.frequency + ")");
                }
                else
                {
                    Log.LogWarning(
                        fileName + ": wobble only works on projectile " +
                        "weapons - ignored.");
                }
            }

            // Clean sine wave beam (custom).
            if (!((bool?)root["wave"] ?? false))
                return;

            if (pw == null || pw.projectilePrefab == null)
            {
                Log.LogWarning(
                    fileName + ": wave only works on projectile weapons - " +
                    "ignored.");
                return;
            }

            // usePhysics weapons fire a PhysicsProjectile (a different class
            // the FixedUpdate patch never runs on), so the wave would be a
            // silent no-op. Fail loudly instead.
            if (pw.usePhysics)
            {
                Log.LogWarning(
                    fileName + ": wave is not supported on usePhysics " +
                    "projectiles - ignored.");
                return;
            }

            // The wave prefix sets the projectile heading each FixedUpdate,
            // but the game's own movementNoiseData branch runs AFTER it and
            // would recompute (and clobber) the heading from idealDirection.
            // Wave wins: turn the noise off so the clean sine is visible.
            var noise = pw.movementNoiseData;
            if (noise.enabled)
            {
                noise.enabled = false;
                pw.movementNoiseData = noise;
                Log.LogWarning(
                    fileName + ": wave overrides wobble/movementNoiseData - " +
                    "disabling the noise so the sine wave shows.");
            }

            GameObject clone = VisualCustomizer.ClonePrefab(
                pw.projectilePrefab.gameObject);

            var comp = clone.GetComponentInChildren<Projectile>(true);
            if (comp == null)
            {
                Log.LogWarning(fileName + ": wave - no Projectile on prefab.");
                return;
            }

            // Attach to the SAME GameObject as the Projectile so the patch's
            // GetComponent<ForgeWaveMotion>() finds it.
            var wave = comp.GetComponent<ForgeWaveMotion>();
            if (wave == null)
                wave = comp.gameObject.AddComponent<ForgeWaveMotion>();

            wave.angleDeg = (float?)root["waveAngle"] ?? 30f;
            wave.frequency = (float?)root["waveFrequency"] ?? 2f;

            string wm =
                ((string)root["waveMode"] ?? "single")
                    .Trim().ToLowerInvariant();
            wave.mode = (wm == "helix") ? 2 : (wm == "synced" ? 1 : 0);

            pw.projectilePrefab = comp;

            Log.LogInfo(
                fileName + ": wave beam (angle " + wave.angleDeg +
                ", freq " + wave.frequency + ", " + wm + ")");
        }

        // Homing for a PLAIN projectile - "homing": { ... } on the weapon.
        //
        // The game's own homingData is unreachable here: ProjectileWeapon
        // assigns it only inside the UsePhysics branch, so a fast straight shot
        // like the Popper's can never home with stock data no matter what the
        // JSON says. This attaches a ForgeHoming marker instead, which
        // ForgeHomingPatch steers by rotating Velocity - and because the
        // projectile's own collision sweep is built from Velocity, the hitbox
        // curves with it.
        private static void ApplyHoming(
            WeaponData weapon,
            JObject root,
            string fileName)
        {
            JToken token = root["homing"];

            if (token == null || token.Type == JTokenType.Null)
                return;

            // Two accepted forms: "homing": true is the whole feature at
            // defaults, and an object carries settings. The object's own
            // "enabled" is what the builder page writes, and it must be able
            // to say false - a block cannot offer both a bare boolean AND
            // nested keys under one name, because the bare value would be
            // overwritten the moment a nested one is set.
            var o = token as JObject;

            if (o == null)
            {
                if (token.Type != JTokenType.Boolean || !(bool)token)
                {
                    Log.LogWarning(
                        fileName + ": \"homing\" should be true or an " +
                        "object - ignored.");
                    return;
                }
            }
            else if (!((bool?)o["enabled"] ?? true))
            {
                return;
            }

            var pw = weapon as ProjectileWeaponData;

            if (pw == null || pw.projectilePrefab == null)
            {
                Log.LogWarning(
                    fileName + ": homing only works on projectile weapons - " +
                    "ignored. (A hitscan beam hits instantly, so there is no " +
                    "flight to steer.)");
                return;
            }

            // A usePhysics weapon fires a PhysicsProjectile, which our
            // FixedUpdate patch never runs on - but that class DOES support the
            // game's own homing, so point them at it rather than just refusing.
            if (pw.usePhysics)
            {
                Log.LogWarning(
                    fileName + ": \"homing\" is for plain projectiles, and " +
                    "this weapon has usePhysics true. Lobbed/physics shots " +
                    "have the game's OWN homing instead - use " +
                    "\"homingData\": { \"enabled\": true, \"torque\": 40, " +
                    "\"maxSpeed\": 20, \"acceleration\": 30, " +
                    "\"maxAngularVelocity\": 200, \"targetMode\": " +
                    "\"AutoSeekWhenShot\" } for those. Ignored.");
                return;
            }

            // movementNoiseData recomputes the heading from idealDirection
            // AFTER our prefix runs, which would wipe out every turn we make.
            // Same clash wave has, same resolution.
            var noise = pw.movementNoiseData;
            if (noise.enabled)
            {
                noise.enabled = false;
                pw.movementNoiseData = noise;
                Log.LogWarning(
                    fileName + ": homing overrides wobble/movementNoiseData " +
                    "- disabling the noise, which would otherwise recompute " +
                    "the heading and undo the homing every frame.");
            }

            GameObject clone = VisualCustomizer.ClonePrefab(
                pw.projectilePrefab.gameObject);

            var comp = clone.GetComponentInChildren<Projectile>(true);
            if (comp == null)
            {
                Log.LogWarning(
                    fileName + ": homing - no Projectile on prefab.");
                return;
            }

            // Same GameObject as the Projectile, so the patch's
            // GetComponent<ForgeHoming>() finds it.
            var homing = comp.GetComponent<ForgeHoming>();
            if (homing == null)
                homing = comp.gameObject.AddComponent<ForgeHoming>();

            if (o != null)
            {
                homing.turnRate = (float?)o["turnRate"] ?? 180f;
                homing.range = (float?)o["range"] ?? 20f;
                homing.cone = (float?)o["cone"] ?? 90f;
                homing.retarget = (bool?)o["retarget"] ?? true;
                homing.delay = (float?)o["delay"] ?? 0f;
                homing.predict = (bool?)o["predict"] ?? false;
                homing.maxTurn = (float?)o["maxTurn"] ?? 0f;
                homing.faceTravel = (bool?)o["faceTravel"] ?? true;
            }

            pw.projectilePrefab = comp;

            // The turn RADIUS is what people actually see, and it grows with
            // the square of speed - so the same turnRate that whips a slow
            // shot around barely bends a fast one. Do that sum for them,
            // because "my homing does nothing" is otherwise a mystery.
            float speed = pw.projectileSpeed;

            if (speed > 0f && homing.turnRate > 0f)
            {
                float radius =
                    speed / (homing.turnRate * Mathf.Deg2Rad);

                Log.LogInfo(
                    fileName + ": homing (turnRate " + homing.turnRate +
                    " deg/s, range " + homing.range + ", cone " +
                    homing.cone + ") - at projectileSpeed " + speed +
                    " the tightest turn circle is about " +
                    radius.ToString("0.0") + " units across the radius." +
                    (radius > 12f
                        ? " That is WIDE - the curve will barely show. Raise " +
                          "turnRate or lower projectileSpeed if you want a " +
                          "visible bend."
                        : string.Empty));
            }
            else
            {
                Log.LogInfo(
                    fileName + ": homing (turnRate " + homing.turnRate +
                    " deg/s, range " + homing.range + ")");
            }
        }

        // Bullet ricochet - "ricochet": { ... } on the weapon.
        //
        // The bouncing itself is the GAME's (ProjectileBounceData), including
        // which layers bounce, and damage already lands before the bounce does.
        // What the game has no notion of is a bounce COUNT - nothing tracks
        // reflections, so a stock bouncer bounces until range or lifetime kills
        // it. ForgeRicochet supplies the count plus the per-bounce shaping.
        private static void ApplyRicochet(
            WeaponData weapon,
            JObject root,
            string fileName)
        {
            JToken token = root["ricochet"];

            if (token == null || token.Type == JTokenType.Null)
                return;

            var o = token as JObject;

            if (o == null)
            {
                if (token.Type != JTokenType.Boolean || !(bool)token)
                {
                    Log.LogWarning(
                        fileName + ": \"ricochet\" should be true or an " +
                        "object - ignored.");
                    return;
                }
            }
            else if (!((bool?)o["enabled"] ?? true))
            {
                return;
            }

            var pw = weapon as ProjectileWeaponData;

            if (pw == null || pw.projectilePrefab == null)
            {
                Log.LogWarning(
                    fileName + ": ricochet only works on projectile weapons " +
                    "- ignored.");
                return;
            }

            // PhysicsProjectile has no bounce code at all - the game's whole
            // bounce implementation lives in Projectile.OnObjectHit - so a
            // lobbed shot cannot ricochet however it is configured.
            if (pw.usePhysics)
            {
                Log.LogWarning(
                    fileName + ": ricochet does not work on usePhysics " +
                    "(lobbed) shots - the game's bounce code only exists on " +
                    "the plain projectile. Ignored.");
                return;
            }

            // What bounces. Terrain is the "Ground" layer, enemies "Entities";
            // resolved by NAME so a layer reshuffle can't silently retarget it.
            string targets =
                ((string)(o != null ? o["targets"] : null) ?? "terrain")
                    .Trim().ToLowerInvariant();

            int ground = LayerMask.NameToLayer("Ground");
            int entities = LayerMask.NameToLayer("Entities");

            int bits = 0;
            bool hitsEnemies = false;

            switch (targets)
            {
                case "none":
                    break;

                case "enemies":
                case "enemy":
                    if (entities >= 0) bits |= 1 << entities;
                    hitsEnemies = true;
                    break;

                case "both":
                case "all":
                    if (ground >= 0) bits |= 1 << ground;
                    if (entities >= 0) bits |= 1 << entities;
                    hitsEnemies = true;
                    break;

                case "terrain":
                case "ground":
                case "walls":
                    if (ground >= 0) bits |= 1 << ground;
                    break;

                default:
                    Log.LogWarning(
                        fileName + ": ricochet targets '" + targets +
                        "' is not recognised - use \"terrain\", " +
                        "\"enemies\", \"both\" or \"none\". Defaulting to " +
                        "terrain.");
                    if (ground >= 0) bits |= 1 << ground;
                    break;
            }

            // A FRESH object, never the one already on the weapon.
            // ProjectileBounceData is a CLASS, so if Unity's ScriptableObject
            // clone shares that reference with the stock asset, mutating it in
            // place would make every White Popper in the game ricochet. Same
            // trap the shared muzzle prefab had; the cheap fix is to never
            // touch the existing instance.
            var bounce = new ProjectileBounceData();
            bounce.enabled = bits != 0;
            bounce.layerMask = bits;
            pw.projectileBounceData = bounce;

            if (bits == 0)
            {
                Log.LogInfo(
                    fileName + ": ricochet targets \"none\" - bouncing " +
                    "turned off.");
                return;
            }

            // Pierce beats bounce ON ENEMIES, in the game's own ordering:
            // OnObjectHit returns early on a pierce before it ever reaches the
            // bounce branch (terrain is exempt from that early-out, so walls
            // still bounce). Both stock bouncers ship with piercing off. Which
            // one wins is the weapon's call.
            bool pierceWins =
                (bool?)(o != null ? o["pierceWins"] : null) ?? false;

            var pierce = pw.piercingData;

            if (hitsEnemies && pierce.enabled)
            {
                if (pierceWins)
                {
                    Log.LogWarning(
                        fileName + ": piercingData is on and pierceWins is " +
                        "true, so shots will PASS THROUGH enemies rather " +
                        "than bounce off them - only terrain will ricochet. " +
                        "Set \"pierceWins\": false to get enemy bounces.");
                }
                else
                {
                    pierce.enabled = false;
                    pw.piercingData = pierce;

                    Log.LogWarning(
                        fileName + ": ricochet targets enemies, so piercing " +
                        "has been turned OFF - the game checks piercing " +
                        "first and would pass through enemies instead of " +
                        "bouncing off them. Set \"pierceWins\": true to keep " +
                        "piercing and ricochet off terrain only.");
                }
            }

            GameObject clone = VisualCustomizer.ClonePrefab(
                pw.projectilePrefab.gameObject);

            var comp = clone.GetComponentInChildren<Projectile>(true);
            if (comp == null)
            {
                Log.LogWarning(
                    fileName + ": ricochet - no Projectile on prefab.");
                return;
            }

            var ric = comp.GetComponent<ForgeRicochet>();
            if (ric == null)
                ric = comp.gameObject.AddComponent<ForgeRicochet>();

            ric.bounces = ParseBounces(o, fileName);

            if (o != null)
            {
                ric.speedMultiplier = (float?)o["speedMultiplier"] ?? 1f;
                ric.damageMultiplier = (float?)o["damageMultiplier"] ?? 1f;
                ric.scatter = Mathf.Max(0f, (float?)o["scatter"] ?? 0f);
                ric.seek = (bool?)o["seek"] ?? false;
                ric.seekRange = (float?)o["seekRange"] ?? 20f;
                ric.seekCone = (float?)o["seekCone"] ?? 180f;
            }

            pw.projectilePrefab = comp;

            // Without a way to expire, an unlimited bouncer is immortal - and
            // the two things that would stop it are exactly the two a beginner
            // has not set.
            if (ric.Unlimited &&
                !pw.rangeData.enabled && !pw.lifetimeData.enabled)
            {
                Log.LogWarning(
                    fileName + ": ricochet bounces is unlimited and this " +
                    "weapon has neither rangeData nor lifetimeData enabled, " +
                    "so these shots will bounce around FOREVER and pile up. " +
                    "Give it \"lifetimeData\": { \"enabled\": true, " +
                    "\"time\": 3 } or a bounce limit.");
            }

            Log.LogInfo(
                fileName + ": ricochet off " + targets + ", " +
                (ric.Unlimited
                    ? "unlimited bounces"
                    : ric.bounces + " bounce(s)") +
                (ric.seek ? ", seeking" : string.Empty) +
                (ric.scatter > 0f
                    ? ", scatter " + ric.scatter + " deg"
                    : string.Empty) +
                (ric.speedMultiplier != 1f
                    ? ", speed x" + ric.speedMultiplier + "/bounce"
                    : string.Empty) +
                (ric.damageMultiplier != 1f
                    ? ", damage x" + ric.damageMultiplier + "/bounce"
                    : string.Empty) + ".");
        }

        // "bounces" accepts a number, or "infinite"/"unlimited"/-1. Anything
        // unlimited is stored as -1.
        private static int ParseBounces(JObject o, string fileName)
        {
            JToken token = o != null ? o["bounces"] : null;

            if (token == null || token.Type == JTokenType.Null)
                return 3;

            if (token.Type == JTokenType.String)
            {
                string s = ((string)token ?? string.Empty)
                    .Trim().ToLowerInvariant();

                if (s == "infinite" || s == "unlimited" ||
                    s == "forever" || s == "-1")
                {
                    return -1;
                }

                int parsed;
                if (int.TryParse(s, out parsed))
                    return parsed < 0 ? -1 : parsed;

                Log.LogWarning(
                    fileName + ": ricochet bounces '" + s + "' is not a " +
                    "number or \"infinite\" - using 3.");
                return 3;
            }

            int n = (int?)token ?? 3;
            return n < 0 ? -1 : n;
        }

        // Deployable turret / mine: the projectile repeatedly FIRES another
        // weapon while it is alive, and can damage things touching it. The
        // game has no while-alive hook (subEmitter is death-only), so this
        // attaches a ForgeTurret component that ticks itself. Pair it with
        // rangeData.slowDown + lifetimeData to get a shot that glides to a
        // stop, works for a few seconds, then vanishes.
        private static void ApplyTurret(
            WeaponData weapon,
            JObject root,
            string fileName)
        {
            if (!((bool?)root["turret"] ?? false))
                return;

            string turretWeapon = (string)root["turretWeapon"];
            if (string.IsNullOrEmpty(turretWeapon))
            {
                Log.LogWarning(
                    fileName + ": \"turret\" is on but no \"turretWeapon\" " +
                    "was given - ignored.");
                return;
            }

            // A turret that fires ITSELF multiplies without bound.
            if (string.Equals(turretWeapon, weapon.name,
                    System.StringComparison.OrdinalIgnoreCase))
            {
                Log.LogWarning(
                    fileName + ": turretWeapon '" + turretWeapon + "' is " +
                    "this weapon itself - ignored (it would spawn turrets " +
                    "endlessly).");
                return;
            }

            var pw = weapon as ProjectileWeaponData;
            if (pw == null || pw.projectilePrefab == null)
            {
                Log.LogWarning(
                    fileName + ": turret only works on projectile weapons - " +
                    "ignored.");
                return;
            }

            // usePhysics fires a PhysicsProjectile, a different class that
            // this component's Projectile lookup wouldn't find.
            if (pw.usePhysics)
            {
                Log.LogWarning(
                    fileName + ": turret is not supported on usePhysics " +
                    "projectiles - ignored.");
                return;
            }

            // An orbit weapon snapshots its orb visual BEFORE this runs, so
            // the two can't be combined - say so instead of silently
            // dropping the turret.
            ForgeOrbit.Config orbitCfg;
            if (ForgeOrbit.TryGet(weapon, out orbitCfg))
            {
                Log.LogWarning(
                    fileName + ": turret can't be combined with orbit - " +
                    "the turret is ignored on an orbit weapon.");
                return;
            }

            // The carrier MUST be able to die, or every shot leaves a
            // permanent turret behind (clones are HideAndDontSave, so one
            // would even survive into the next run).
            var lt = pw.lifetimeData;
            bool diesByRange = pw.rangeData.enabled && pw.rangeData.destroyWhenReached;
            if (!lt.enabled && !diesByRange && !pw.impactBehaviour.enabled)
            {
                lt.enabled = true;
                if (lt.time <= 0f)
                    lt.time = 8f;
                pw.lifetimeData = lt;
                Log.LogWarning(
                    fileName + ": turret has no way to expire - forcing " +
                    "lifetimeData " + lt.time + "s so it can't live forever.");
            }

            GameObject clone = VisualCustomizer.ClonePrefab(
                pw.projectilePrefab.gameObject);

            var comp = clone.GetComponentInChildren<Projectile>(true);
            if (comp == null)
            {
                Log.LogWarning(fileName + ": turret - no Projectile on prefab.");
                return;
            }

            // Must live on the Projectile's own GameObject - the component
            // reads its Owner/Damage via GetComponent<Projectile>().
            var turret = comp.GetComponent<ForgeTurret>();
            if (turret == null)
                turret = comp.gameObject.AddComponent<ForgeTurret>();

            turret.weaponName = turretWeapon;
            turret.interval = (float?)root["turretInterval"] ?? 0.5f;
            turret.rotationSpeed = (float?)root["turretRotation"] ?? 90f;
            turret.startAngle = (float?)root["turretStartAngle"] ?? 0f;
            turret.searchRange = (float?)root["turretRange"] ?? 12f;
            turret.startDelay = (float?)root["turretDelay"] ?? 0f;
            turret.contactDamage = (bool?)root["turretContactDamage"] ?? true;
            turret.contactRadius = (float?)root["turretContactRadius"] ?? 0.5f;
            turret.contactRepeatDelay =
                (float?)root["turretContactDelay"] ?? 0.4f;

            string aim =
                ((string)root["turretAim"] ?? "rotate")
                    .Trim().ToLowerInvariant();
            turret.aimMode = (aim == "nearest" || aim == "enemy" ||
                              aim == "target")
                ? ForgeTurret.AimNearest
                : ForgeTurret.AimRotate;

            string dirn =
                ((string)root["turretDirection"] ?? "cw")
                    .Trim().ToLowerInvariant();
            turret.clockwise = !(dirn == "ccw" ||
                dirn == "counterclockwise" || dirn == "counter");

            // Keep the turret PARKED. Without piercing, touching an enemy
            // runs the game's impact path, which snaps the projectile onto
            // the hit point - the "hovering" disc gets dragged around by
            // whatever bumps it. Piercing makes enemy contact a pass-
            // through instead (ground still collides, so it can still
            // bounce off walls). The repeat delays keep the engine's own
            // knockback from firing every physics step.
            var pd = pw.piercingData;
            if (!pd.enabled)
            {
                pd.enabled = true;
                pd.damageRepeatDelay =
                    Mathf.Max(pd.damageRepeatDelay, turret.contactRepeatDelay);
                pd.knockBackRepeatDelay =
                    Mathf.Max(pd.knockBackRepeatDelay, turret.contactRepeatDelay);
                pw.piercingData = pd;
            }

            pw.projectilePrefab = comp;

            Log.LogInfo(
                fileName + ": turret (" + aim + ", every " +
                turret.interval + "s, fires '" + turretWeapon + "'" +
                (turret.aimMode == ForgeTurret.AimRotate
                    ? ", " + turret.rotationSpeed + " deg/s " +
                      (turret.clockwise ? "cw" : "ccw")
                    : "") + ")");
        }

        // Wires up an "orbit" weapon: projectiles that circle the player.
        // A fully custom behaviour (ForgeOrbit + ForgeOrbitController +
        // ForgeOrbitPatch) - the game has no orbital mechanic. All knobs
        // are read here from root-level JSON. Orb COUNT and per-hit DAMAGE
        // are read live from the weapon at runtime, so +projectile and
        // +damage modules scale the ring.
        private static void ApplyOrbit(
            WeaponData weapon,
            JObject root,
            string fileName)
        {
            bool orbit = (bool?)root["orbit"] ?? false;
            if (!orbit)
                return;

            var cfg = new ForgeOrbit.Config();

            string mode =
                ((string)root["orbitMode"] ?? "passive")
                    .Trim().ToLowerInvariant();
            if (mode == "hold") cfg.mode = ForgeOrbit.Mode.Hold;
            else if (mode == "toggle") cfg.mode = ForgeOrbit.Mode.Toggle;
            else if (mode == "fire") cfg.mode = ForgeOrbit.Mode.Fire;
            else cfg.mode = ForgeOrbit.Mode.Passive;

            string dirn =
                ((string)root["orbitDirection"] ?? "cw")
                    .Trim().ToLowerInvariant();
            cfg.clockwise = !(dirn == "ccw" ||
                dirn == "counterclockwise" || dirn == "counter");

            cfg.radius = (float?)root["orbitRadius"] ?? 3f;

            // Concentric rings.
            cfg.rings = (int?)root["orbitRings"] ?? 1;
            if (cfg.rings < 1) cfg.rings = 1;
            cfg.ringSpacing = (float?)root["orbitRingSpacing"] ?? 1.5f;
            cfg.ringAlternate = (bool?)root["orbitRingAlternate"] ?? false;
            cfg.ringSpeedStep = (float?)root["orbitRingSpeedStep"] ?? 1f;

            string ringMode =
                ((string)root["orbitRingMode"] ?? "split")
                    .Trim().ToLowerInvariant();
            cfg.ringsFullCount = (ringMode == "full" || ringMode == "each" ||
                                  ringMode == "perring");

            // "auto" (default) staggers each ring by half a slot so the orbs
            // interleave; a number sets the offset per ring in degrees.
            JToken staggerTok = root["orbitRingStagger"];
            if (staggerTok == null)
            {
                cfg.ringStagger = -1f;
            }
            else
            {
                string st = staggerTok.ToString().Trim().ToLowerInvariant();
                if (st == "auto" || st == "")
                    cfg.ringStagger = -1f;
                else if (st == "aligned" || st == "none")
                    cfg.ringStagger = 0f;
                else
                    cfg.ringStagger = (float?)staggerTok ?? -1f;
            }
            cfg.speed = (float?)root["orbitSpeed"] ?? 120f;
            cfg.hitRadius = (float?)root["orbitHitRadius"] ?? 0.6f;
            cfg.contactDamage = (bool?)root["orbitContactDamage"] ?? true;
            cfg.weaponEffects = (bool?)root["orbitWeaponEffects"] ?? true;
            cfg.damageRepeatDelay = (float?)root["orbitDamageRepeatDelay"] ?? 0.3f;
            cfg.blockProjectiles = (bool?)root["orbitBlockProjectiles"] ?? false;
            // Only pay for enemy-projectile tracking if something wants it.
            if (cfg.blockProjectiles)
                ForgeProjectileTracker.Enabled = true;
            cfg.pushForce = (float?)root["orbitPushForce"] ?? 0f;
            cfg.pulseAmount = (float?)root["orbitPulse"] ?? 0f;
            cfg.pulseSpeed = (float?)root["orbitPulseSpeed"] ?? 1f;
            cfg.spinUpSeconds = (float?)root["orbitSpinUp"] ?? 0f;
            cfg.fling = (bool?)root["orbitFling"] ?? false;
            cfg.flingReach = (float?)root["orbitFlingReach"] ?? 4f;
            cfg.flingDuration = (float?)root["orbitFlingDuration"] ?? 0.35f;
            cfg.suppressFire = (bool?)root["orbitSuppressFire"] ?? true;
            cfg.holdDrainPerSecond =
                (float?)root["orbitHoldDrainPerSecond"] ?? 0f;
            cfg.fireDuration = (float?)root["orbitFireDuration"] ?? 3f;

            // Spiral-outward mode: orbs travel out from the ship instead of
            // holding a fixed ring. "launch" = spiral out then fly off toward
            // enemies (don't return); "sweep" = spiral out and recycle.
            string spiral =
                ((string)root["orbitSpiral"] ?? "off")
                    .Trim().ToLowerInvariant();
            if (spiral == "launch" || spiral == "launchandleave")
                cfg.spiral = ForgeOrbit.SpiralMode.Launch;
            else if (spiral == "sweep" || spiral == "recycle")
                cfg.spiral = ForgeOrbit.SpiralMode.Sweep;
            else
                cfg.spiral = ForgeOrbit.SpiralMode.Off;

            cfg.spiralInner = (float?)root["orbitSpiralInner"] ?? 0.4f;
            cfg.spiralOutTime = (float?)root["orbitSpiralTime"] ?? 0.8f;
            cfg.spiralLaunchSpeed = (float?)root["orbitSpiralLaunchSpeed"] ?? 12f;
            cfg.spiralKillDistance = (float?)root["orbitSpiralRange"] ?? 14f;

            // Terrain digging. Orbs have no collision layer of their own, so
            // this is what makes them chew walls the way a normal shot does.
            cfg.damageTerrain = (bool?)root["orbitDamageTerrain"] ?? false;
            cfg.terrainRepeatDelay =
                (float?)root["orbitTerrainRepeatDelay"] ?? 0.15f;

            // Destructible orbs + regen.
            cfg.destroyOnEnemy = (bool?)root["orbitDestroyOnEnemy"] ?? false;
            cfg.destroyOnTerrain = (bool?)root["orbitDestroyOnTerrain"] ?? false;
            cfg.regenTime = (float?)root["orbitRegenTime"] ?? 3f;
            cfg.popExplosion = (bool?)root["orbitPopExplosion"] ?? false;
            cfg.popRadius = (float?)root["orbitPopRadius"] ?? 1.5f;

            string regen =
                ((string)root["orbitRegenMode"] ?? "both")
                    .Trim().ToLowerInvariant();
            if (regen == "timer") cfg.regenMode = ForgeOrbit.RegenMode.Timer;
            else if (regen == "fire") cfg.regenMode = ForgeOrbit.RegenMode.Fire;
            else cfg.regenMode = ForgeOrbit.RegenMode.Both;

            // Orb visual: an explicit prefab, else the weapon's own.
            string vis = (string)root["orbitVisual"];
            if (!string.IsNullOrEmpty(vis))
            {
                var p = JsonFieldMapper.FindAsset(
                    typeof(Projectile), vis) as Projectile;
                if (p != null)
                    cfg.visualPrefab = p.gameObject;
            }

            if (cfg.visualPrefab == null)
            {
                var pw = weapon as ProjectileWeaponData;
                if (pw != null && pw.projectilePrefab != null)
                    cfg.visualPrefab = pw.projectilePrefab.gameObject;
            }

            ForgeOrbit.Register(weapon, cfg);

            Log.LogInfo(
                fileName + ": orbit weapon (" + mode +
                (cfg.clockwise ? ", cw" : ", ccw") +
                ", radius " + cfg.radius +
                (cfg.spiral != ForgeOrbit.SpiralMode.Off
                    ? ", spiral " + spiral
                    : "") + ")");
        }

        // Wires up "electric" (chain-lightning) behavior from root-level
        // flags, generalizing the standalone White Tesla. A hitscan weapon
        // with "dischargeOnFire" fires a discharge from the gun; enemies
        // are made to conduct so it chains; optional buildup/hideBeam
        // polish; and an optional global lightningColor (base or rainbow).
        private static void ApplyElectric(
            WeaponData weapon,
            JObject root,
            string fileName)
        {
            bool dischargeOnFire =
                (bool?)root["dischargeOnFire"] ?? false;

            // Reach override for player chain-lightning (per hop). Global
            // to player electricity; independent of dischargeOnFire.
            var lightningRange = (float?)root["lightningRange"];
            if (lightningRange.HasValue && lightningRange.Value > 0f)
            {
                ForgeElectric.SetLightningRange(lightningRange.Value);
                Log.LogInfo(
                    fileName + ": lightning range " + lightningRange.Value);
            }

            string lightningColor = (string)root["lightningColor"];

            // Lightning color is independent of dischargeOnFire so it can
            // also recolor projectile-discharge weapons if desired.
            if (!string.IsNullOrEmpty(lightningColor))
            {
                string lc = lightningColor.Trim();

                if (lc.Equals("rainbow", StringComparison.OrdinalIgnoreCase) ||
                    lc.Equals("rgb", StringComparison.OrdinalIgnoreCase))
                {
                    ForgeElectric.SetLightningRgb(
                        (float?)root["lightningRgbSpeed"] ?? 0.5f);
                    Log.LogInfo(fileName + ": lightning color RGB");
                }
                else
                {
                    Color parsed;

                    if (VisualCustomizer.TryParseColor(lc, out parsed))
                    {
                        ForgeElectric.SetLightningStatic(parsed);
                        Log.LogInfo(
                            fileName + ": lightning color " + lightningColor);
                    }
                    else
                    {
                        Log.LogWarning(
                            fileName + ": lightningColor '" + lightningColor +
                            "' is not a valid color - ignored");
                    }
                }
            }

            if (!dischargeOnFire)
                return;

            // A chain weapon needs enemies to conduct, so default that on.
            bool chainThroughEnemies =
                (bool?)root["chainThroughEnemies"] ?? true;

            var config = new ForgeElectric.Config
            {
                dischargeOnFire = true,
                chainThroughEnemies = chainThroughEnemies,
                buildupSeconds = (float?)root["buildupSeconds"] ?? 2f,
                hideBeam = (bool?)root["hideBeam"] ?? false
            };

            ForgeElectric.Register(weapon, config);

            // Match the discharge's targeting to the weapon's (set by
            // "target"), mirroring the standalone Tesla. Only hitscan
            // weapons carry a layerMask to copy from.
            var hitscan = weapon as HitscanWeaponData;

            if (hitscan != null)
            {
                var d = weapon.discharge;
                d.layerMask = hitscan.layerMask;
                weapon.discharge = d;
            }

            Log.LogInfo(
                fileName + ": electric weapon (dischargeOnFire" +
                (chainThroughEnemies ? ", chains through enemies" : "") +
                (config.hideBeam ? ", beam hidden" : "") + ")");
        }

        // Attaches burn effects to the module: a tick-rate booster (speeds
        // up how fast burn ticks on enemies) and/or a burn recolor (solid
        // or "rainbow"/"rgb", optionally including terrain). These are
        // ModuleEffects that route through ForgeBurnCompat, so they feed
        // ModuleForge's engine when it's installed or WeaponForge's own
        // otherwise. The player's burn is never affected.
        private static void ApplyBurn(
            ModuleData module,
            JToken tickRate,
            string colorText,
            float rgbSpeed,
            bool includeTerrain,
            string fileName)
        {
            if (module.effects == null)
                module.effects = new List<ModuleEffect>();

            if (tickRate != null)
            {
                float tr = (float)tickRate;

                module.effects.Add(
                    new ForgeBurnRateEffect { ticksPerSecond = tr });

                Log.LogInfo(
                    fileName + ": burn tick rate +" + tr + "/s");
            }

            if (!string.IsNullOrEmpty(colorText))
            {
                var effect = new ForgeBurnColorEffect
                {
                    rgbSpeed = rgbSpeed,
                    includeTerrain = includeTerrain
                };

                string c = colorText.Trim();

                if (c.Equals("rainbow", StringComparison.OrdinalIgnoreCase) ||
                    c.Equals("rgb", StringComparison.OrdinalIgnoreCase))
                {
                    effect.rgb = true;
                    effect.colorLabel = "RGB";
                }
                else
                {
                    Color parsed;

                    if (VisualCustomizer.TryParseColor(c, out parsed))
                    {
                        effect.color = parsed;
                        effect.colorLabel = colorText;
                    }
                    else
                    {
                        Log.LogWarning(
                            fileName + ": burnColor '" + colorText +
                            "' is not a valid color - skipped");
                        return;
                    }
                }

                module.effects.Add(effect);

                Log.LogInfo(
                    fileName + ": burn color " +
                    (effect.rgb ? "RGB" : colorText) +
                    (includeTerrain ? " +terrain" : ""));
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
