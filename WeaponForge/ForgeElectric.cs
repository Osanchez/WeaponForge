using System.Collections.Generic;
using UnityEngine;

namespace WeaponForge
{
    // Registry + shared state for "electric" weapons (chain-lightning like
    // the White Tesla). A weapon is electric when its JSON sets
    // "dischargeOnFire": true; that generalizes the old standalone
    // WhiteTeslaMod behaviour so any JSON weapon can chain lightning.
    public static class ForgeElectric
    {
        // Per-weapon config, keyed by the built (cloned) WeaponData asset,
        // matched at runtime against HitscanWeapon.TemplateData.
        public class Config
        {
            public bool dischargeOnFire;      // fire a discharge from the gun
            public bool chainThroughEnemies;  // make enemies conduct
            public float buildupSeconds = 2f;  // telegraph before the strike
            public bool hideBeam;             // hide the base hitscan beam
        }

        private static readonly Dictionary<WeaponData, Config> _weapons =
            new Dictionary<WeaponData, Config>();

        // True if ANY loaded electric weapon wants chaining, so the global
        // enemy-conductor patch only activates when it's actually needed.
        public static bool AnyChainWeapon { get; private set; }

        // Global lightning color (the player-subsystem beam is shared, so
        // this can't be per-weapon; last electric weapon to set it wins).
        public static bool HasLightningColor { get; private set; }
        public static bool LightningRgb { get; private set; }
        public static Color LightningColor { get; private set; } = Color.white;
        public static float LightningRgbSpeed { get; private set; } = 0.5f;

        // Global override for the PLAYER electricity beamRange (how far the
        // lightning can reach/arc per hop). Applied to the player subsystem
        // only, so enemy electric attacks keep their stock reach. Shared
        // across all player chain weapons; last one to set it wins.
        public static bool HasLightningRange { get; private set; }
        public static float LightningRange { get; private set; }

        public static void Register(WeaponData weapon, Config config)
        {
            if (weapon == null || config == null)
                return;

            _weapons[weapon] = config;

            if (config.chainThroughEnemies)
                AnyChainWeapon = true;
        }

        public static bool TryGet(WeaponData weapon, out Config config)
        {
            config = null;
            return weapon != null && _weapons.TryGetValue(weapon, out config);
        }

        public static void SetLightningStatic(Color color)
        {
            HasLightningColor = true;
            LightningRgb = false;
            LightningColor = color;
        }

        public static void SetLightningRgb(float speed)
        {
            HasLightningColor = true;
            LightningRgb = true;
            LightningRgbSpeed = speed;
        }

        public static void SetLightningRange(float range)
        {
            if (range <= 0f)
                return;

            HasLightningRange = true;
            LightningRange = range;
        }
    }

    // Tag placed on a spawned discharge source so the beam-buildup patch
    // knows the telegraph length without matching a hard-coded name (which
    // would cross-trigger between different electric weapons).
    public class ForgeDischargeMarker : MonoBehaviour
    {
        public float buildupSeconds = 2f;
    }
}
