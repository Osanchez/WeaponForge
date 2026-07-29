using UnityEngine;

namespace WeaponForge
{
    // Attached to a projectile prefab by WeaponBuilder.ApplyWave when a
    // weapon sets "wave": true. It makes the shot fly a clean, repeating
    // sine "S" (a Super Metroid-style wave beam) instead of a straight
    // line. The params live here; the per-instance runtime state (start
    // time + phase) is filled in by ForgeWavePatch when the projectile is
    // actually shot. ForgeWavePatch's FixedUpdate prefix reads these and
    // swings the projectile's heading each frame.
    public class ForgeWaveMotion : MonoBehaviour
    {
        // How far the heading swings off the fire direction, in degrees
        // (the "width"/sharpness of the S).
        public float angleDeg = 30f;

        // Wiggles per second (with speed, this sets the wavelength).
        public float frequency = 2f;

        // 0 = single   : phase measured from THIS shot's fire time, so each
        //                shot's wave starts centered.
        // 1 = synced   : phase measured from a shared clock, so a rapid-fire
        //                stream forms one continuous ribbon.
        // 2 = helix    : like synced, but alternate projectiles start 180
        //                out of phase - the woven "double helix" look.
        public int mode = 0;

        // Runtime state, set by ForgeWavePatch at Shoot.
        [System.NonSerialized] public bool shot;
        [System.NonSerialized] public float startTime;
        [System.NonSerialized] public float phaseOffset;
    }
}
