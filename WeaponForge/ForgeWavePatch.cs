using System;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace WeaponForge
{
    // Drives the clean sine "wave beam" motion for projectiles tagged with
    // ForgeWaveMotion.
    //
    // The game already bends projectile headings every FixedUpdate for its
    // organic "movementNoiseData" wobble (Projectile.FixedUpdate rotates the
    // fixed idealDirection by a Perlin-noise angle). We reuse the exact same
    // idea, but with a DETERMINISTIC sine instead of Perlin, so the path is
    // a crisp repeating S. Because we only change the projectile's Velocity
    // DIRECTION (keeping its magnitude), the game's own CircleCast collision,
    // piercing, phasing, and range logic all follow the visible curve - no
    // desync.
    //
    //   Shoot postfix     -> mark the instance shot, stamp its start time and
    //                        (for helix) its 0/180 phase from a fire counter.
    //   FixedUpdate prefix-> set Velocity = rotate(idealDirection, sine) so it
    //                        runs BEFORE the game moves/casts the projectile.
    public static class ForgeWavePatch
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("WeaponForge");

        private static AccessTools.FieldRef<Projectile, Vector2> _idealRef;
        private static bool _ready;

        // Alternates 0 / 180 across successive shots so "helix" weaves.
        private static int _counter;

        private static void Ensure()
        {
            if (_ready)
                return;
            _idealRef = AccessTools.FieldRefAccess<Projectile, Vector2>(
                "idealDirection");
            _ready = true;
        }

        [HarmonyPatch(typeof(Projectile), "Shoot")]
        public class OnShoot
        {
            static void Postfix(Projectile __instance)
            {
                try
                {
                    var w = __instance.GetComponent<ForgeWaveMotion>();
                    if (w == null)
                        return;

                    Ensure();
                    w.shot = true;
                    w.startTime = Time.time;
                    w.phaseOffset =
                        (w.mode == 2 && (_counter++ & 1) == 1)
                            ? Mathf.PI
                            : 0f;
                }
                catch (Exception e)
                {
                    Log.LogError("Wave shoot init failed: " + e);
                }
            }
        }

        [HarmonyPatch(typeof(Projectile), "FixedUpdate")]
        public class OnFixedUpdate
        {
            static void Prefix(Projectile __instance)
            {
                try
                {
                    var w = __instance.GetComponent<ForgeWaveMotion>();
                    if (w == null || !w.shot)
                        return;

                    Ensure();
                    if (_idealRef == null)
                        return;

                    Vector2 ideal = _idealRef(__instance);
                    if (ideal == Vector2.zero)
                        return;

                    // single: phase from this shot; synced/helix: shared clock.
                    float t = (w.mode == 0)
                        ? (Time.time - w.startTime)
                        : Time.time;

                    float ang = w.angleDeg *
                        Mathf.Sin(t * w.frequency * 2f * Mathf.PI + w.phaseOffset);

                    Vector3 dir = Quaternion.AngleAxis(ang, Vector3.back) *
                        (Vector3)ideal;

                    __instance.Velocity =
                        (Vector2)dir * __instance.Velocity.magnitude;
                }
                catch (Exception e)
                {
                    Log.LogError("Wave motion failed: " + e);
                }
            }
        }
    }
}
