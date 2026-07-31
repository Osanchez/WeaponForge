using HarmonyLib;

namespace WeaponForge
{
    // Reset WeaponForge's per-run state at each run entry (so nothing from
    // a previous run leaks). Covers the burn accumulator and the tracked
    // enemy-projectile list.
    //
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
                ForgeProjectileTracker.Reset();
            }
        }

        [HarmonyPatch(typeof(Punk.SaveLoad.GameSaver), "Load")]
        public class OnContinue
        {
            static void Prefix()
            {
                if (ForgeBurnCompat.OwnsPatches)
                    ForgeBurn.Reset();
                ForgeProjectileTracker.Reset();
            }
        }
    }
}
