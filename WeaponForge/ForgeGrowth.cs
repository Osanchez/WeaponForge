using UnityEngine;

namespace WeaponForge
{
    // A shot that changes SIZE as it travels - a pinprick that arrives as a
    // wrecking ball, or a fat slug that tapers away to nothing.
    //
    // This is cheap for one specific reason: Projectile.Radius is a public
    // property and the game rebuilds its collision sweep from it every frame
    //     Physics2D.CircleCast(position, this.Radius, Velocity, ...)
    // so growing the number grows the actual hitbox. The art is just
    // transform.localScale. Nothing has to be kept in sync by hand - the thing
    // you see and the thing that hits are driven by the same progress value.
    //
    // Every write is computed from the values captured at Shoot rather than
    // from the current ones, so the effect can never compound frame over frame.
    public class ForgeGrowth : MonoBehaviour
    {
        // Size multipliers relative to the shot's NORMAL size, so 1 is
        // "unchanged". from > to shrinks instead of grows.
        public float from = 0.4f;
        public float to = 3f;

        // false = progress measured by DISTANCE travelled (what "grows as it
        // flies" usually means), true = by seconds alive.
        public bool overTime;

        // Distance in world units, or seconds, to reach the "to" size.
        // 0 = borrow the weapon's own range, which is almost always what you
        // want: the shot then peaks exactly as it runs out.
        public float span;

        // Grow the hitbox with the art. Off makes it purely cosmetic.
        public bool hitbox = true;

        // Damage multiplier at full size. 1 = damage never changes.
        public float damageAtFull = 1f;

        // Shape of the ramp. 1 = linear, >1 = stays small then swells late,
        // <1 = swells fast then eases.
        public float curve = 1f;

        // Stop at "to", or keep going past it for as long as the shot lives.
        public bool clamp = true;

        // --- runtime, captured at Shoot -------------------------------
        [System.NonSerialized] public bool shot;
        [System.NonSerialized] public float startTime;
        [System.NonSerialized] public Vector2 origin;
        [System.NonSerialized] public Vector3 baseScale;
        [System.NonSerialized] public float baseRadius;
        [System.NonSerialized] public float baseDamage;
        [System.NonSerialized] public float resolvedSpan;
    }
}
