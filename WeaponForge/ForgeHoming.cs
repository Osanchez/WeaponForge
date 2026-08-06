using UnityEngine;

namespace WeaponForge
{
    // Homing for a PLAIN (non-physics) projectile - the fast straight kind the
    // Popper fires.
    //
    // Why this has to exist: the game's own homingData is wired up ONLY for
    // PhysicsProjectile. ProjectileWeapon.FireSingle assigns HomingData inside
    // the UsePhysics branch and nowhere else, so a Popper-style shot can never
    // home no matter what the JSON says. And PhysicsProjectile steers by
    // AddTorque on a rigidbody, a missile flight model that needs mass and
    // angular velocity - none of which a plain Projectile has.
    //
    // A plain Projectile is much simpler to steer, and better for this: its
    // FixedUpdate does
    //     Physics2D.CircleCast(position, Radius, this.Velocity, ...)
    // so the COLLISION SWEEP FOLLOWS Velocity. Rotating the velocity bends the
    // hitbox along with the art automatically - there is no second raycast to
    // keep in sync, which is exactly the trap that makes bending a hitscan beam
    // hard (see ForgeBeam).
    //
    // Params live here; per-instance runtime state is filled in by
    // ForgeHomingPatch when the shot is actually fired. Same three-piece shape
    // as ForgeWaveMotion / ForgeWavePatch.
    public class ForgeHoming : MonoBehaviour
    {
        // Degrees per second the heading may swing. THE dial that decides
        // whether this reads as a gentle curve or a hard chase.
        public float turnRate = 180f;

        // How far away a target may be to get picked up, in world units.
        public float range = 20f;

        // Only acquire something within this many degrees of where the shot is
        // already travelling, so bullets don't turn round and fly backwards.
        // 180 = anything, including behind.
        public float cone = 90f;

        // Look for a new target if the current one dies or gets out of range.
        public bool retarget = true;

        // Seconds of dead-straight flight before homing starts. Small values
        // read really well on a beam-like stream: it leaves the muzzle straight
        // and only bends further out.
        public float delay;

        // Aim where the target is GOING rather than where it is.
        public bool predict;

        // Total degrees this shot may ever turn (0 = no limit). A budget stops
        // a missed shot from circling forever.
        public float maxTurn;

        // Rotate the sprite to match the new heading each frame. Needed for any
        // art with a nose, and for a segment stream to read as one curve.
        public bool faceTravel = true;

        // Runtime state, set by ForgeHomingPatch at Shoot.
        [System.NonSerialized] public bool shot;
        [System.NonSerialized] public float startTime;
        [System.NonSerialized] public Transform target;
        [System.NonSerialized] public AimAssistTarget targetAim;
        [System.NonSerialized] public float turned;
        [System.NonSerialized] public int mask;
        [System.NonSerialized] public float nextScan;
    }
}
