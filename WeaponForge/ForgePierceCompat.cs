using System;
using HarmonyLib;

namespace WeaponForge
{
    // Pierce coordination with ModuleForge (mirrors ForgeBurnCompat).
    //
    // When ModuleForge is installed it OWNS pierce-cap counting, so a weapon's
    // baked pierceLimit and any equipped pierce MODULES ADD UP into one cap on
    // one counter (weapon 2 + module 1 = 3). WeaponForge then stands down its
    // own counter (ForgePiercePatch) and its own stat line (ForgeWeaponStats-
    // Patch); it still enables PiercingData and leaves a ForgePierceCap data
    // component on the projectile for ModuleForge to read. Standalone (no
    // ModuleForge), WeaponForge counts + displays it itself.
    public static class ForgePierceCompat
    {
        private static bool _init;
        private static bool _present;

        public static bool ModuleForgePresent
        {
            get
            {
                if (!_init)
                {
                    _init = true;
                    try
                    {
                        _present = AccessTools.TypeByName(
                            "ModuleForge.ModuleForgeProjectile") != null;
                    }
                    catch
                    {
                        _present = false;
                    }
                }
                return _present;
            }
        }
    }
}
