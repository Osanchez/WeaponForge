using HarmonyLib;

namespace WeaponForge
{
    // Stretches the preview (telegraph) phase of beams spawned by a Forge
    // discharge to that weapon's buildupSeconds. While in preview the beam
    // is a thin no-damage arc; when it expires it flips to the full
    // lightning and damages. Identified by the ForgeDischargeMarker on the
    // source (not a hard-coded name), so different electric weapons don't
    // cross-trigger. Other electricity beams are untouched.
    [HarmonyPatch(typeof(ElectricityBeam), "Setup")]
    public class ForgeBeamChargePatch
    {
        static void Postfix(
            ElectricityBeam __instance,
            ElectricityConductor sourceConductor)
        {
            if (sourceConductor == null)
                return;

            var marker =
                sourceConductor.GetComponent<ForgeDischargeMarker>();

            if (marker == null)
                return;

            Traverse.Create(__instance)
                .Field("previewDuration")
                .SetValue(marker.buildupSeconds);
        }
    }
}
