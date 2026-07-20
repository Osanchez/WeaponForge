using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace WeaponForge
{
    // Makes loot-enabled Forge weapons drop from crates. There is no
    // global "droppable" flag in the game: a module only drops if it's
    // a member of a DropTableWeightedGroup that a crate's DropTable
    // references. So just before any table-based loot roll, we add our
    // loot weapons into that table's MODULE groups (the crate module
    // pools). We only touch groups that already contain module entries,
    // so resource/prefab-only tables (e.g. enemy drops) are untouched.
    //
    // Hooking SelectLoot (rather than editing group assets at startup)
    // guarantees the groups are loaded - they arrive as the argument.
    [HarmonyPatch(typeof(LootSelector), "SelectLoot")]
    public class ForgeLootPatch
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("WeaponForge");

        // Groups we've already augmented, so we add our modules once.
        private static readonly HashSet<DropTableWeightedGroup> _done =
            new HashSet<DropTableWeightedGroup>();

        private static FieldInfo _groupField;

        static void Prefix(DropTable dropTable)
        {
            try
            {
                if (dropTable == null || dropTable.items == null)
                    return;

                if (_groupField == null)
                {
                    _groupField =
                        typeof(DropTableItem).GetField(
                            "group",
                            BindingFlags.NonPublic |
                            BindingFlags.Instance);
                }

                if (_groupField == null)
                    return;

                foreach (DropTableItem item in dropTable.items)
                {
                    var group =
                        _groupField.GetValue(item)
                            as DropTableWeightedGroup;

                    if (group != null)
                        Augment(group);
                }
            }
            catch (Exception e)
            {
                Log.LogError("Loot injection failed: " + e);
            }
        }

        private static void Augment(DropTableWeightedGroup group)
        {
            if (!_done.Add(group))
                return;   // already processed this group

            var dist = group.itemDistribution;

            if (dist == null)
                return;

            // Only inject into groups that are MODULE pools, and skip
            // any of our modules already present.
            bool hasModuleEntry = false;
            var present =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var distItem in dist.Items)
            {
                if (distItem.Value.droppableType !=
                    DroppabbleType.Module)
                {
                    continue;
                }

                hasModuleEntry = true;

                if (distItem.Value.module != null &&
                    distItem.Value.module.Id != null)
                {
                    present.Add(distItem.Value.module.Id);
                }
            }

            if (!hasModuleEntry)
                return;   // resource/prefab pool - leave it alone

            int added = 0;

            foreach (ForgeEntry entry in ForgeRegistry.Entries)
            {
                if (!entry.inLoot || entry.module == null)
                    continue;

                if (present.Contains(entry.module.Id))
                    continue;

                dist.Add(
                    new DroppabbleItem
                    {
                        droppableType = DroppabbleType.Module,
                        module = entry.module
                    },
                    entry.lootWeight);

                added++;
            }

            if (added > 0)
            {
                Log.LogInfo(
                    "Added " + added +
                    " Forge weapon(s) to drop group '" +
                    group.name + "'");
            }
        }
    }
}
