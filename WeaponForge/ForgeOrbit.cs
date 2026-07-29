using System.Collections.Generic;
using UnityEngine;

namespace WeaponForge
{
    // Registry + config for "orbit" weapons: projectiles that circle the
    // player instead of flying outward. A weapon is an orbit weapon when
    // its JSON sets "orbit": true. This is a fully custom behaviour (the
    // game has no orbital mechanic), driven by ForgeOrbitPatch + rendered/
    // damaged by ForgeOrbitController.
    public static class ForgeOrbit
    {
        public enum Mode { Passive, Hold, Toggle, Fire }
        public enum RegenMode { Both, Timer, Fire }

        // How the orbs travel outward instead of holding a fixed ring:
        //   Off    - classic fixed-radius ring (default).
        //   Launch - orbs spiral out from the ship, then detach at the outer
        //            radius and fly straight off toward enemies (they don't
        //            come back), respawning from the center. A "vortex cannon".
        //   Sweep  - orbs spiral out to the outer radius and reset to the
        //            center, over and over - a continuous rotating-sprinkler
        //            sweep. They never leave, they just don't orbit back.
        public enum SpiralMode { Off, Launch, Sweep }

        public class Config
        {
            public Mode mode = Mode.Passive;
            public bool clockwise = true;

            // Ring shape / motion.
            public float radius = 3f;         // circle size ("range" / outer)
            public float speed = 120f;        // rotation, degrees/second
            public float hitRadius = 0.6f;    // contact size per orb

            // Spiral-outward behaviour (see SpiralMode above). When on,
            // "radius" is the OUTER edge and pulse/fling are ignored.
            public SpiralMode spiral = SpiralMode.Off;
            public float spiralInner = 0.4f;      // radius the spiral starts at
            public float spiralOutTime = 0.8f;    // seconds inner -> outer
            public float spiralLaunchSpeed = 12f; // fly-off speed (Launch)
            public float spiralKillDistance = 14f;// remove launched orb past this

            // What the orbs do.
            public bool contactDamage = true;
            public float damageRepeatDelay = 0.3f;  // per enemy
            public bool blockProjectiles;     // destroy enemy shots touched
            public float pushForce;           // shove enemies (0 = off)

            // Extras.
            public float pulseAmount;         // radius breathe amplitude
            public float pulseSpeed = 1f;
            public float spinUpSeconds;       // ramp to full spin
            public bool fling;                // fire = surge outward + back
            public float flingReach = 4f;     // extra radius during a fling
            public float flingDuration = 0.35f;

            // Destructible orbs + regeneration.
            public bool destroyOnEnemy;       // orb dies when it hits an enemy
            public bool destroyOnTerrain;     // orb dies when it hits terrain
            public RegenMode regenMode = RegenMode.Both;
            public float regenTime = 3f;      // seconds to respawn (Timer/Both)
            public bool popExplosion;         // dead orb bursts (AoE damage)
            public float popRadius = 1.5f;

            // Activation costs / timing.
            public bool suppressFire = true;  // don't also shoot forward
            public float holdDrainPerSecond;  // Hold mode resource drain
            public float fireDuration = 3f;   // Fire mode ring lifetime

            // Visual (a Projectile prefab used purely as art).
            public GameObject visualPrefab;
        }

        private static readonly Dictionary<WeaponData, Config> _weapons =
            new Dictionary<WeaponData, Config>();

        public static void Register(WeaponData weapon, Config config)
        {
            if (weapon != null && config != null)
                _weapons[weapon] = config;
        }

        public static bool TryGet(WeaponData weapon, out Config config)
        {
            config = null;
            return weapon != null && _weapons.TryGetValue(weapon, out config);
        }
    }
}
