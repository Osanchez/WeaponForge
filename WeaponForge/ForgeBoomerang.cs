using System.Collections.Generic;
using UnityEngine;

namespace WeaponForge
{
    // A shot that flies out, turns around, and comes back to you.
    //
    // The game does more of this than you would expect. A weapon with
    // rangeData.slowDown decelerates as it approaches its range - the
    // DiscGun ships exactly that, range 8 with slowDown on - so by the time it
    // reaches the far end its speed is already ZERO. That is a free, natural
    // pivot point: no snap turn, no extra tuning. The outbound half of a
    // boomerang is stock behaviour; all that was missing is "don't die there,
    // come back".
    //
    // Which is also why the turn is handled by switching the projectile's OWN
    // RangeData off at the pivot rather than fighting it: while outbound the
    // game's slowDown line rewrites Velocity every frame (it preserves
    // direction and overwrites magnitude), so anything we set would be lost -
    // and once elapsed passes timeToReachRange that line would pin the speed at
    // zero forever. Disabling RangeData on the instance hands us the velocity
    // cleanly for the trip home.
    public class ForgeBoomerang : MonoBehaviour
    {
        // Also turn around on hitting TERRAIN, instead of only at max range.
        // Enemies are handled by piercing, so they never stop it.
        public bool returnOnHit;

        // false = curve toward wherever the ship is NOW (forgiving, always
        // reaches you). true = fly back along the path it came out on, which
        // is faithful to the throw and lines up a second hit on the same row -
        // but if you have moved, it returns to where you were.
        public bool retrace;

        // Multiplier on the outbound launch speed for the trip home.
        public float returnSpeed = 1f;

        // Degrees per second while curving. Generous by default: a boomerang
        // that takes a wide lazy arc home stops reading as a boomerang.
        public float turnRate = 540f;

        // How close to the ship counts as caught.
        public float catchRadius = 1.2f;

        // 0 = vanish, 1 = refund part of the firing cost, 2 = loop out again.
        public int onCatch;

        public float refundFraction = 0.5f;
        public Resource refundResource;
        public float refundAmount;

        // onCatch = loop: total out-and-back trips before it gives up.
        public int passes = 2;

        // May it hit the same enemy again on the way back?
        public bool rehit = true;

        // Damage multiplier applied once, when the return leg begins.
        public float returnDamage = 1f;

        // Safety net. rangeData.destroyWhenReached is switched off for a
        // boomerang (otherwise it dies at the pivot), so without this a shot
        // whose return somehow stalls would live forever.
        public float maxLife = 12f;

        // --- runtime -------------------------------------------------
        [System.NonSerialized] public bool shot;
        [System.NonSerialized] public float startTime;
        [System.NonSerialized] public float startSpeed;
        [System.NonSerialized] public Vector2 origin;
        [System.NonSerialized] public Vector2 fireDir;
        [System.NonSerialized] public bool returning;
        [System.NonSerialized] public int pass;
        [System.NonSerialized] public bool damageApplied;
        [System.NonSerialized] public float nextSample;

        // How far the FIRST outbound leg got before pivoting. Later laps have
        // no RangeData left to decelerate them (it is switched off at the first
        // pivot), so they turn by distance instead using this.
        [System.NonSerialized] public float outRange;

        // Breadcrumbs for retrace, walked backwards on the way home.
        [System.NonSerialized] public List<Vector2> crumbs;
        [System.NonSerialized] public int crumbIndex;
    }
}
