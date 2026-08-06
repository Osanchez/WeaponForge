using UnityEngine;

namespace WeaponForge
{
    // Bullet ricochet - bounce limit, falloff, scatter and smart redirection.
    //
    // The game ALREADY bounces. ProjectileBounceData { enabled, layerMask } is
    // checked in Projectile.OnObjectHit, and because the damage call (TryHit)
    // runs BEFORE that branch, a shot that ricochets off an enemy has already
    // hurt it. Terrain is layer bit 1024 ("Ground"), enemies 128 ("Entities"),
    // so "which things bounce" is just that mask - WeaponBuilder sets it from a
    // plain-English "targets" word.
    //
    // What the game has NO concept of is a bounce COUNT: nothing anywhere
    // tracks how many times a shot has reflected, so a stock bouncer bounces
    // forever until range or lifetime kills it. That is what this component
    // exists for, plus the per-bounce shaping that only makes sense once you
    // are counting.
    public class ForgeRicochet : MonoBehaviour
    {
        // How many bounces are allowed. NEGATIVE = unlimited (what the game
        // does on its own).
        public int bounces = 3;

        // Fraction of speed kept per bounce. 1 = arcade, no loss.
        public float speedMultiplier = 1f;

        // Fraction of damage kept per bounce. 1 = arcade, no loss.
        public float damageMultiplier = 1f;

        // Degrees of random scatter added to each reflection. Besides feeling
        // less mechanical, this is the practical escape from a shot trapped
        // between two parallel walls, which a perfect mirror bounce would ping
        // between forever.
        public float scatter;

        // Aim each bounce at the nearest valid enemy instead of reflecting off
        // the surface - what makes a ricochet reliably "hit something else"
        // rather than sailing off into empty space.
        public bool seek;
        public float seekRange = 20f;

        // How far off the true reflected heading a seek is allowed to pull.
        // 180 = anywhere, including back the way it came.
        public float seekCone = 180f;

        // Runtime state.
        [System.NonSerialized] public int used;

        // Set by the Reflect prefix, read by the postfix, so the postfix knows
        // this bounce was actually allowed to happen.
        [System.NonSerialized] public bool bouncing;

        public bool Unlimited
        {
            get { return this.bounces < 0; }
        }
    }
}
