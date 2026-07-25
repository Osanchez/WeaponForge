using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace WeaponForge
{
    // Makes shop-enabled Forge weapons purchasable. PUNK gates the shop
    // by a station threshold: a counter (unlockedShopCount, also exposed
    // as UnlockedStationCount) is BOTH "stations unlocked" and the shop
    // tier index. Each station unlock rolls one tier of
    // ShopUpgradeData.perLevelData[N], so a weapon placed at tier N can
    // only appear after the player has unlocked N stations.
    //
    // We inject at a RunData.Initialize prefix - the single safe choke
    // point where both ShopUpgradeData and ShopItemsConfig are live and
    // populated, and which runs before the game's first shop roll.
    //
    // Two things are required per weapon:
    //   1. A ShopItemConfig (price) keyed by the module's Id. WITHOUT it
    //      the shop throws a NullReferenceException when it tries to
    //      create the item - so the config is done first and the pool
    //      entry is skipped if the config can't be built.
    //   2. A dedicated single-module PerLevelGroup (probablity 1) added
    //      to perLevelData[shopUnlockLevel], so the weapon appears
    //      deterministically at its tier rather than competing in the
    //      big weighted pool.
    [HarmonyPatch(typeof(RunData), "Initialize")]
    public class ForgeShopPatch
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("WeaponForge");

        // Module ids already injected this session (the shop assets
        // persist across runs, so inject once).
        private static readonly HashSet<string> _injected =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static int _nextLineNumber = 9000;

        static void Prefix()
        {
            try
            {
                ForgeRegistry.BuildAll();

                ShopUpgradeData shopData;
                ShopItemsConfig config;

                if (!ServiceLocator.TryGet<ShopUpgradeData>(out shopData) ||
                    shopData == null)
                {
                    return;
                }

                if (!ServiceLocator.TryGet<ShopItemsConfig>(out config) ||
                    config == null)
                {
                    return;
                }

                bool configChanged = false;

                foreach (ForgeEntry entry in ForgeRegistry.Entries)
                {
                    if (!entry.inShop || entry.module == null)
                        continue;

                    if (_injected.Contains(entry.module.Id))
                        continue;

                    // 1. Price config (mandatory) - build it first; if it
                    //    can't be made, skip the pool too (no price =
                    //    crash when the shop tries to show it).
                    if (!EnsureConfig(config, entry, ref configChanged))
                        continue;

                    // 2. Pool entry at the chosen station tier.
                    InjectPool(shopData, entry.module, entry.shopUnlockLevel);

                    _injected.Add(entry.module.Id);

                    Log.LogInfo(
                        "Added '" + entry.displayName +
                        "' to the shop at unlock level " +
                        entry.shopUnlockLevel + " for " +
                        entry.shopPrice + ".");
                }

                if (configChanged)
                {
                    // Rebuild the id -> config dictionary so Get() sees
                    // our additions (throws on duplicate keys - guarded
                    // by the _injected / Get(id) checks above).
                    config.Initialize();
                }
            }
            catch (Exception e)
            {
                Log.LogError("Shop injection failed: " + e);
            }
        }

        // Returns true if the module has (or now has) a price config.
        private static bool EnsureConfig(
            ShopItemsConfig config,
            ForgeEntry entry,
            ref bool configChanged)
        {
            if (config.Get(entry.module.Id) != null)
                return true;   // already present (e.g. a prior session)

            var money =
                JsonFieldMapper.FindAsset(
                    typeof(Resource), "Resource Money") as Resource;

            if (money == null)
            {
                Log.LogWarning(
                    "Can't add '" + entry.displayName + "' to the shop: " +
                    "'Resource Money' currency not found.");
                return false;
            }

            var itemConfig = new ShopItemConfig
            {
                id = entry.module.Id,
                lineNumber = _nextLineNumber++,
                price = new List<Price>
                {
                    new Price
                    {
                        currencyType = Price.CurrencyType.Resource,
                        resource = money,
                        amount = entry.shopPrice
                    }
                },
                priceIncrement = new List<Price>
                {
                    new Price
                    {
                        currencyType = Price.CurrencyType.Resource,
                        resource = money,
                        amount = 0f
                    }
                },
                unlockRequirements = new List<Ingredient>()
            };

            // itemList is protected on the generic base
            // ConfigRegistry<ShopItemConfig,string>.
            FieldInfo itemListField =
                typeof(ShopItemsConfig).BaseType.GetField(
                    "itemList",
                    BindingFlags.NonPublic | BindingFlags.Instance);

            if (itemListField == null)
            {
                Log.LogError(
                    "ShopItemsConfig itemList field not found.");
                return false;
            }

            var itemList =
                itemListField.GetValue(config) as List<ShopItemConfig>;

            if (itemList == null)
                return false;

            itemList.Add(itemConfig);
            configChanged = true;
            return true;
        }

        // Add a dedicated probablity-1 single-module group at the given
        // station tier. Grows perLevelData if the tier is beyond the
        // stock range.
        private static void InjectPool(
            ShopUpgradeData shopData,
            ModuleData module,
            int level)
        {
            if (level < 0)
                level = 0;

            if (shopData.perLevelData == null)
                shopData.perLevelData =
                    new ShopUpgradeData.PerLevelData[0];

            if (level >= shopData.perLevelData.Length)
            {
                var resized =
                    new ShopUpgradeData.PerLevelData[level + 1];

                Array.Copy(
                    shopData.perLevelData, resized,
                    shopData.perLevelData.Length);

                for (int i = shopData.perLevelData.Length;
                     i <= level; i++)
                {
                    resized[i] = new ShopUpgradeData.PerLevelData
                    {
                        groups =
                            new ShopUpgradeData.PerLevelData.PerLevelGroup[0]
                    };
                }

                shopData.perLevelData = resized;
            }

            var group =
                ScriptableObject.CreateInstance<ShopItemGroup>();

            group.hideFlags = HideFlags.HideAndDontSave;
            group.moduleDistribution.Add(module, 1f);

            var plg =
                new ShopUpgradeData.PerLevelData.PerLevelGroup
                {
                    probablity = 1f,
                    group = group
                };

            ShopUpgradeData.PerLevelData tier =
                shopData.perLevelData[level];

            var existing = tier.groups
                ?? new ShopUpgradeData.PerLevelData.PerLevelGroup[0];

            var newGroups =
                new ShopUpgradeData.PerLevelData.PerLevelGroup[
                    existing.Length + 1];

            Array.Copy(existing, newGroups, existing.Length);
            newGroups[existing.Length] = plg;

            tier.groups = newGroups;
            shopData.perLevelData[level] = tier;   // write struct back
        }
    }
}
