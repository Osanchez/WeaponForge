using System;
using System.Collections.Generic;
using BepInEx.Logging;
using HarmonyLib;

namespace WeaponForge
{
    // Shows a weapon's OWN (JSON-baked) phasing / pierce cap on its stat
    // card: "PHASING ON" and "PIERCE n". Keyed off WeaponBase.TemplateData
    // via ForgeWeaponInfo, which WeaponBuilder fills in as it applies each
    // weapon's phasing / pierceLimit. (ModuleForge separately shows the
    // phasing/pierce coming from installed MODULES.)
    //
    // WeaponBase.GetPropertyList builds the stat list for both the equipped
    // weapon and the weapon-module preview, so one postfix covers both.
    [HarmonyPatch(typeof(WeaponBase), "GetPropertyList")]
    public class ForgeWeaponStatsPatch
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("WeaponForge");

        static void Postfix(WeaponBase __instance, List<DisplayableProperty> results)
        {
            if (results == null)
                return;

            // When ModuleForge is installed it shows a single COMBINED
            // phasing/pierce line (weapon-baked + modules), reading our baked
            // values via reflection - so we stay quiet to avoid a second line.
            if (ForgePierceCompat.ModuleForgePresent)
                return;

            try
            {
                WeaponData td = __instance.TemplateData;
                if (td == null)
                    return;

                if (ForgeWeaponInfo.IsPhasing(td))
                {
                    results.Add(new DisplayableProperty(
                        TextFormatter.ColoredText(
                            TextFormatter.electronColor, "PHASING"),
                        "ON"));
                }

                int limit;
                if (ForgeWeaponInfo.TryGetPierce(td, out limit))
                {
                    results.Add(new DisplayableProperty(
                        TextFormatter.ColoredText(
                            TextFormatter.capsColor, "PIERCE"),
                        limit.ToString()));
                }
            }
            catch (Exception e)
            {
                Log.LogError("Weapon phasing/pierce stat failed: " + e);
            }
        }
    }
}
