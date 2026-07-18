using BepInEx;
using BepInEx.Unity.Mono;
using HarmonyLib;

namespace WeaponForge
{
    [BepInPlugin(
        "com.andy.weaponforge",
        "Weapon Forge",
        "1.0.0")]
    public class WeaponForgePlugin : BaseUnityPlugin
    {
        private void Awake()
        {
            Logger.LogInfo("Weapon Forge loaded");

            var harmony =
                new Harmony("com.andy.weaponforge");

            harmony.PatchAll();

            Logger.LogInfo("Weapon Forge patches applied");
        }
    }
}
