using HarmonyLib;

namespace WeaponForge
{
    // Reset WeaponForge's own burn accumulator at each run entry (so a
    // boost from a previous run can't leak). Only active when WeaponForge
    // owns the burn engine (ModuleForge absent) - otherwise ModuleForge's
    // own reset handles it and WeaponForge's effects re-register on
    // continue via OnInstalled.
    public static class ForgeBurnResetPatch
    {
        [HarmonyPatch(typeof(RunData), "Initialize")]
        public class OnNewRun
        {
            static void Prefix()
            {
                if (ForgeBurnCompat.OwnsPatches)
                    ForgeBurn.Reset();
            }
        }

        [HarmonyPatch(typeof(Punk.SaveLoad.GameSaver), "Load")]
        public class OnContinue
        {
            static void Prefix()
            {
                if (ForgeBurnCompat.OwnsPatches)
                    ForgeBurn.Reset();
            }
        }
    }
}
