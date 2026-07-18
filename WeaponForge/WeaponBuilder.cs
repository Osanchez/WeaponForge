using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace WeaponForge
{
    // Turns one weapon definition JSON file into a playable starting
    // loadout: clones the template module + weapon assets, applies the
    // JSON field overrides, and registers a loadout in the pool.
    public static class WeaponBuilder
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("WeaponForge");

        public static void BuildFromJson(
            string filePath,
            LoadoutPool pool)
        {
            string fileName = Path.GetFileName(filePath);

            JObject root =
                JObject.Parse(File.ReadAllText(filePath));

            string name = (string)root["name"];

            if (string.IsNullOrEmpty(name))
            {
                Log.LogError(
                    fileName + ": missing required \"name\"");
                return;
            }

            string loadoutName = "Forge_" + name;

            if (pool.loadouts.Any(
                x => x != null && x.name == loadoutName))
            {
                return;
            }

            string templateName = (string)root["template"];

            if (string.IsNullOrEmpty(templateName))
            {
                Log.LogError(
                    fileName + ": missing required \"template\" " +
                    "(a WeaponModuleData asset name, e.g. " +
                    "\"Module Weapon White Popper\")");
                return;
            }

            var templateModule =
                JsonFieldMapper.FindAsset(
                    typeof(WeaponModuleData),
                    templateName) as WeaponModuleData;

            if (templateModule == null)
            {
                Log.LogError(
                    fileName + ": template module '" +
                    templateName + "' not found");
                return;
            }

            if (templateModule.weapon == null)
            {
                Log.LogError(
                    fileName + ": template module '" +
                    templateName + "' has no weapon");
                return;
            }

            string baseLoadoutName =
                (string)root["baseLoadout"] ?? "Starter_Popper";

            var baseLoadout =
                JsonFieldMapper.FindAsset(
                    typeof(LoadoutTemplate),
                    baseLoadoutName) as LoadoutTemplate;

            if (baseLoadout == null)
            {
                Log.LogError(
                    fileName + ": base loadout '" +
                    baseLoadoutName + "' not found");
                return;
            }

            string displayName =
                (string)root["displayName"] ??
                name.ToUpperInvariant();

            string description =
                (string)root["description"] ??
                "Custom weapon built by Weapon Forge.";

            // Clone the module and weapon so the originals in the
            // game are never touched.
            var module =
                ScriptableObject.Instantiate(templateModule);

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

            var weapon =
                ScriptableObject.Instantiate(
                    templateModule.weapon);

            weapon.name = "Forge Weapon " + name;

            module.weapon = weapon;
            module.displayName = displayName;
            module.description = description;

            var weaponJson = root["weapon"] as JObject;

            if (weaponJson != null)
            {
                JsonFieldMapper.Apply(
                    weapon,
                    weaponJson,
                    name + ".weapon");
            }

            var moduleJson = root["module"] as JObject;

            if (moduleJson != null)
            {
                // "resourceGain" is a friendly alias handled here,
                // not a real ModuleData field — pull it out before
                // the generic mapper sees it.
                var resourceGain =
                    moduleJson["resourceGain"] as JObject;

                if (resourceGain != null)
                    moduleJson.Remove("resourceGain");

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
            }

            var loadout =
                ScriptableObject.Instantiate(baseLoadout);

            loadout.hideFlags = HideFlags.None;
            loadout.name = loadoutName;
            loadout.displayName = displayName;
            loadout.description = description;
            loadout.primary = module;

            // Forge loadouts are never locked behind progression.
            loadout.unlockingModules = new ModuleData[0];

            pool.loadouts.Add(loadout);

            Log.LogInfo(
                "Added loadout '" + displayName +
                "' from " + fileName);
        }

        // Changes which resource the module grants when equipped
        // (and how much) by retargeting the module's
        // ModifyResourceCapacity effects. JSON shape:
        //   "resourceGain": { "resource": "Resource White",
        //                     "amount": 12 }
        private static void ApplyResourceGain(
            WeaponModuleData module,
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

            // Template module had no capacity effect at all —
            // add one so the weapon comes with its own ammo.
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
