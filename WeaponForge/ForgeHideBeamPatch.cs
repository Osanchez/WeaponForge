using System.Collections.Generic;
using HarmonyLib;

namespace WeaponForge
{
    // Hides the base hitscan beam (fire line, warm-up line, impact
    // particles, light) for Forge weapons flagged "hideBeam" - e.g. the
    // White Tesla, whose visible effect is the electricity chain, not the
    // laser. The discharge chain beams are separate objects and stay
    // visible. Generalized from WhiteTeslaMod's TeslaLaserVisualPatch.
    [HarmonyPatch(typeof(HitscanWeapon), "InitializeVisuals")]
    public class ForgeHideBeamPatch
    {
        static void Postfix(HitscanWeapon __instance)
        {
            ForgeElectric.Config cfg;

            if (!ForgeElectric.TryGet(__instance.TemplateData, out cfg))
                return;

            if (!cfg.hideBeam)
                return;

            var visuals =
                Traverse.Create(__instance)
                    .Field("visualsInstances")
                    .GetValue<List<HitscanWeaponVisual>>();

            if (visuals == null)
                return;

            foreach (var visual in visuals)
            {
                if (visual != null)
                    visual.gameObject.SetActive(false);
            }
        }
    }
}
