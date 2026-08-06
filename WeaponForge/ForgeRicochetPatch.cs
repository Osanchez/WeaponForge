using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace WeaponForge
{
    // Counts and shapes bounces for ForgeRicochet.
    //
    // Projectile.Reflect(normal) is the single choke point - it is public, it is
    // tiny, and it is the ONLY place a projectile's heading gets mirrored - so
    // both the counting and the shaping hang off it:
    //
    //   prefix  -> out of bounces? finish the shot and skip the reflect.
    //              otherwise count it and apply the damage falloff.
    //   postfix -> the game has just mirrored Velocity (and idealDirection);
    //              now apply speed loss, scatter and seek to the new heading.
    //
    // Splitting it that way lets the GAME own the actual mirror maths and the
    // idealDirection bookkeeping, so wave and wobble keep working off a heading
    // that still means what they think it means.
    //
    // One caveat worth knowing: Reflect is also called by the impactBehaviour
    // safety-distance path (a shot that hits something within safetyDistance of
    // the muzzle is bounced instead of exploding, so weapons do not blow up in
    // your face). Those consume a bounce too. That is a rare edge case and
    // arguably correct, but it is why a point-blank shot can look like it lost
    // one early.
    public static class ForgeRicochetPatch
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("WeaponForge.Ricochet");

        // idealDirection is what movementNoiseData and the wave patch measure
        // from, so scatter/seek have to move it in step with Velocity or a
        // ricocheting wave beam would weave around a heading it no longer has.
        private static AccessTools.FieldRef<Projectile, Vector2> _idealRef;

        // SpawnExplosion / FireSub are private on Projectile, and re-creating
        // what they do would mean duplicating explosion + sub-emitter wiring
        // that already works.
        private static MethodInfo _spawnExplosion;
        private static MethodInfo _fireSub;
        private static bool _ready;

        private static void Ensure()
        {
            if (_ready)
                return;

            _ready = true;

            try
            {
                _idealRef =
                    AccessTools.FieldRefAccess<Projectile, Vector2>(
                        "idealDirection");
            }
            catch (Exception e)
            {
                Log.LogWarning(
                    "Could not reach Projectile.idealDirection (" + e.Message +
                    ") - ricochet still works, but a ricocheting wave/wobble " +
                    "shot will weave around its pre-bounce heading.");
            }

            _spawnExplosion =
                AccessTools.Method(typeof(Projectile), "SpawnExplosion");

            _fireSub = AccessTools.Method(typeof(Projectile), "FireSub");
        }

        [HarmonyPatch(typeof(Projectile), "Reflect")]
        public class OnReflect
        {
            static bool Prefix(Projectile __instance, Vector2 normal)
            {
                var ric = __instance.GetComponent<ForgeRicochet>();

                if (ric == null)
                    return true;

                ric.bouncing = false;

                try
                {
                    Ensure();

                    if (!ric.Unlimited && ric.used >= ric.bounces)
                    {
                        Finish(__instance);
                        return false;
                    }

                    ric.used++;
                    ric.bouncing = true;

                    if (ric.damageMultiplier != 1f)
                    {
                        Damage d = __instance.Damage;
                        d.amount *= ric.damageMultiplier;
                        __instance.Damage = d;
                    }
                }
                catch (Exception e)
                {
                    // Let the plain bounce happen rather than stranding the
                    // shot mid-air with no heading.
                    Log.LogError("Ricochet bookkeeping failed: " + e);
                    return true;
                }

                return true;
            }

            static void Postfix(Projectile __instance)
            {
                var ric = __instance.GetComponent<ForgeRicochet>();

                // Harmony runs postfixes even when a prefix skipped the
                // original, so this flag is what tells us the bounce really
                // happened.
                if (ric == null || !ric.bouncing)
                    return;

                ric.bouncing = false;

                try
                {
                    Shape(__instance, ric);
                }
                catch (Exception e)
                {
                    Log.LogError("Ricochet shaping failed: " + e);
                }
            }
        }

        private static void Shape(Projectile p, ForgeRicochet ric)
        {
            Vector2 v = p.Velocity;
            float speed = v.magnitude;

            if (speed <= 0.0001f)
                return;

            if (ric.speedMultiplier != 1f)
                speed *= ric.speedMultiplier;

            Vector2 dir = v.normalized;

            // Seek first, then scatter, so the scatter reads as imprecision in
            // the redirect rather than being swallowed by it.
            if (ric.seek)
            {
                int mask =
                    Physics2D.GetLayerCollisionMask(p.gameObject.layer);

                AimAssistTarget found =
                    ForgeHomingPatch.FindTarget(
                        p, p.transform.position, dir,
                        ric.seekRange, ric.seekCone, mask);

                if (found != null)
                {
                    Vector2 to =
                        (Vector2)found.transform.position -
                        (Vector2)p.transform.position;

                    if (to.sqrMagnitude > 0.000001f)
                        dir = to.normalized;
                }
            }

            if (ric.scatter > 0f)
            {
                float jitter =
                    UnityEngine.Random.Range(-ric.scatter, ric.scatter);

                dir = Quaternion.AngleAxis(jitter, Vector3.forward) * dir;
            }

            p.Velocity = dir * speed;

            if (_idealRef != null)
                _idealRef(p) = dir;

            // Keep the sprite pointing where it is now going. Projectile only
            // sets its rotation at Shoot, so without this a ricocheted shot
            // flies sideways for the rest of its life.
            p.transform.rotation =
                Quaternion.FromToRotation(Vector3.right, (Vector3)p.Velocity);
        }

        // Out of bounces: run the weapon's own impact behaviour if it has one,
        // then destroy. This is the same set of effects the game fires on a
        // normal terminal hit, so a ricochet weapon's last bounce explodes /
        // fires its sub-emitter exactly like its first hit would have.
        private static void Finish(Projectile p)
        {
            Vector2 pos = p.transform.position;

            try
            {
                ProjectileImpactBehaviour ib = p.ImpactBehaviour;

                if (ib.enabled)
                {
                    if (ib.destroyEffect != null)
                    {
                        UnityEngine.Object.Instantiate(
                            ib.destroyEffect, pos, p.transform.rotation);
                    }

                    if (ib.spawnExplosion && _spawnExplosion != null)
                        _spawnExplosion.Invoke(p, new object[] { pos });

                    if (ib.fireSub && _fireSub != null)
                        _fireSub.Invoke(p, null);

                    if (ib.discharge)
                    {
                        ElectricityManager em;

                        if (ServiceLocator.TryGet<ElectricityManager>(out em))
                            em.SpawnDischarge(p.DischargeData, pos);
                    }
                }
            }
            catch (Exception e)
            {
                Log.LogError(
                    "Ricochet could not run the final impact effects: " + e);
            }

            UnityEngine.Object.Destroy(p.gameObject);
        }
    }
}
