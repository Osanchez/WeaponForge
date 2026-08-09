using UnityEngine;

namespace WeaponForge
{
    // A shot that clears enemy fire out of the air as it travels - destroying
    // it, or turning it around and sending it back.
    //
    // This fills a category WeaponForge did not have at all: a DEFENSIVE
    // weapon. Everything else here hurts things; this one protects you, and a
    // slow deflector disc swept across incoming fire plays completely
    // differently from any other weapon in the mod.
    //
    // The one non-obvious fact that makes or breaks it: PUNK projectiles
    // resolve their own collisions with a CircleCast, so their Collider2D is
    // DISABLED on the prefab. Enemy bullets are therefore INVISIBLE to
    // Physics2D.OverlapCircle - a physics query finds nothing, no matter how
    // big the radius. They have to come from ForgeProjectileTracker, which
    // registers each enemy shot as it is fired.
    public class ForgeDeflect : MonoBehaviour
    {
        // How far from this shot incoming fire is caught.
        public float radius = 2f;

        // 0 = destroy it, 1 = send it back as YOUR bullet.
        public int mode;

        // Where a reflected bullet is aimed. 0 = straight back the way it
        // came, 1 = at the nearest enemy.
        public int aim;

        // Total shots this projectile may deal with over its whole life.
        // 0 = unlimited.
        public int maxTotal;

        // Seconds between sweeps. Not every frame: the sweep is a distance
        // test over every live enemy bullet, and 20/sec is indistinguishable
        // from 50/sec here.
        public float interval = 0.05f;

        // Applied to a reflected bullet.
        public float damageMultiplier = 1f;
        public float speedMultiplier = 1f;

        // How far a reflected shot looks for a target when aim = nearest.
        public float aimRange = 25f;

        // --- runtime -------------------------------------------------
        [System.NonSerialized] public bool shot;
        [System.NonSerialized] public int used;
        [System.NonSerialized] public float nextSweep;
    }
}
