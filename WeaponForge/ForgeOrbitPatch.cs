using System;
using System.Collections.Generic;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace WeaponForge
{
    // Drives orbit weapons off the ship's Shooter:
    //   Update  -> ensure a ForgeOrbitController exists for the equipped
    //              orbit weapon and set whether it's active (by mode).
    //   Shoot   -> handle mode-specific fire behaviour (toggle on/off,
    //              (re)summon a timed ring, fling), and suppress the
    //              weapon's normal forward fire so it's a pure orbital.
    public static class ForgeOrbitPatch
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("WeaponForge");

        private static readonly Dictionary<Shooter, ForgeOrbitController> _ctrls =
            new Dictionary<Shooter, ForgeOrbitController>();

        private static ForgeOrbitController GetOrCreate(
            Shooter shooter, ForgeOrbit.Config cfg)
        {
            ForgeOrbitController ctrl;
            if (_ctrls.TryGetValue(shooter, out ctrl) && ctrl != null)
                return ctrl;

            var go = new GameObject("Forge Orbit");
            if (shooter.Unit != null)
                go.transform.SetParent(shooter.Unit.transform, false);

            ctrl = go.AddComponent<ForgeOrbitController>();
            ctrl.Init(cfg, shooter);
            _ctrls[shooter] = ctrl;
            return ctrl;
        }

        private static void Remove(Shooter shooter)
        {
            ForgeOrbitController ctrl;
            if (_ctrls.TryGetValue(shooter, out ctrl))
            {
                if (ctrl != null)
                    UnityEngine.Object.Destroy(ctrl.gameObject);
                _ctrls.Remove(shooter);
            }
        }

        [HarmonyPatch(typeof(Shooter), "Update")]
        public class OnUpdate
        {
            static void Postfix(Shooter __instance)
            {
                try
                {
                    WeaponBase weapon = __instance.Weapon;
                    ForgeOrbit.Config cfg;

                    if (weapon == null ||
                        !ForgeOrbit.TryGet(weapon.TemplateData, out cfg))
                    {
                        Remove(__instance);
                        return;
                    }

                    ForgeOrbitController ctrl = GetOrCreate(__instance, cfg);

                    bool active;
                    switch (cfg.mode)
                    {
                        case ForgeOrbit.Mode.Hold:
                            active = __instance.IsShooting;
                            break;
                        case ForgeOrbit.Mode.Toggle:
                            active = ctrl.toggledOn;
                            break;
                        case ForgeOrbit.Mode.Fire:
                            active = Time.time < ctrl.fireActiveUntil;
                            break;
                        default: // Passive
                            active = true;
                            break;
                    }

                    ctrl.SetActive(active);

                    if (cfg.mode == ForgeOrbit.Mode.Hold && active &&
                        cfg.holdDrainPerSecond > 0f)
                    {
                        Drain(__instance, weapon,
                            cfg.holdDrainPerSecond * Time.deltaTime);
                    }
                }
                catch (Exception e)
                {
                    Log.LogError("Orbit update failed: " + e);
                }
            }
        }

        [HarmonyPatch(typeof(Shooter), "Shoot")]
        public class OnShoot
        {
            static bool Prefix(Shooter __instance)
            {
                try
                {
                    WeaponBase weapon = __instance.Weapon;
                    ForgeOrbit.Config cfg;

                    if (weapon == null ||
                        !ForgeOrbit.TryGet(weapon.TemplateData, out cfg))
                    {
                        return true; // normal weapon - fire as usual
                    }

                    ForgeOrbitController ctrl = GetOrCreate(__instance, cfg);

                    if (cfg.mode == ForgeOrbit.Mode.Toggle)
                        ctrl.toggledOn = !ctrl.toggledOn;
                    else if (cfg.mode == ForgeOrbit.Mode.Fire)
                        ctrl.fireActiveUntil = Time.time + cfg.fireDuration;

                    ctrl.TriggerFling();
                    ctrl.RegenOnFire();

                    // We're skipping the game's Shoot (to suppress forward
                    // fire), so charge the cost ourselves for active modes.
                    if (cfg.suppressFire &&
                        (cfg.mode == ForgeOrbit.Mode.Toggle ||
                         cfg.mode == ForgeOrbit.Mode.Fire))
                    {
                        Drain(__instance, weapon, weapon.Cost);
                    }

                    return !cfg.suppressFire;
                }
                catch (Exception e)
                {
                    Log.LogError("Orbit shoot failed: " + e);
                    return true;
                }
            }
        }

        private static void Drain(Shooter shooter, WeaponBase weapon, float amount)
        {
            try
            {
                if (amount <= 0f || shooter.Unit == null || weapon.ResourceUsed == null)
                    return;
                if (shooter.Unit.HasTank(weapon.ResourceUsed))
                    shooter.Unit.GetTank(weapon.ResourceUsed).Value -= amount;
            }
            catch { }
        }
    }
}
