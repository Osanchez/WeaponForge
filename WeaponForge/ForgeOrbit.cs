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

            // Concentric rings. rings=1 is the classic single ring.
            //   ringsFullCount false -> the weapon's projectileCount is SPLIT
            //     across the rings (round-robin, so they stay even).
            //   ringsFullCount true  -> EVERY ring gets the full count, so
            //     the orb total is projectileCount * rings.
            public int rings = 1;
            public float ringSpacing = 1.5f;  // gap between one ring and the next
            public bool ringsFullCount;
            public float ringStagger = -1f;   // <0 = auto half-step; else deg per ring
            public bool ringAlternate;        // flip spin direction every other ring
            public float ringSpeedStep = 1f;  // speed multiplier per ring outward

            // Spiral-outward behaviour (see SpiralMode above). When on,
            // "radius" is the OUTER edge and pulse/fling are ignored.
            public SpiralMode spiral = SpiralMode.Off;
            public float spiralInner = 0.4f;      // radius the spiral starts at
            public float spiralOutTime = 0.8f;    // seconds inner -> outer
            public float spiralLaunchSpeed = 12f; // fly-off speed (Launch)
            public float spiralKillDistance = 14f;// remove launched orb past this

            // What the orbs do.
            // weaponEffects: pass the weapon's FULL hit through - burn, the
            // got-attacked/aggro event, kill credit, plus its explosion and
            // discharge. Off = plain contact damage only.
            public bool weaponEffects = true;
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

            // Terrain. Orbs are not real projectiles - they have no
            // collision layer of their own - so digging is opt-in and routed
            // through the level's own projectile listener, exactly like a
            // normal shot hitting a wall (cell damage, shake, burn).
            public bool damageTerrain;
            public float terrainRepeatDelay = 0.15f;  // per orb, sec per cell

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
