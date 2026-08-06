using System;
using System.Collections.Generic;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace WeaponForge
{
    // Drives ForgeHoming: acquire a target when the shot is fired, then swing
    // its Velocity toward that target every FixedUpdate.
    //
    // Two patches, mirroring ForgeWavePatch:
    //   Projectile.Shoot       postfix -> stamp the fire time, grab a target
    //   Projectile.FixedUpdate prefix  -> rotate Velocity before the game
    //                                    sweeps and moves along it
    //
    // The prefix ordering matters and is the whole trick: Projectile.FixedUpdate
    // reads Velocity to build its CircleCast, so writing Velocity in a PREFIX
    // means this frame's collision sweep already follows the new heading. The
    // hitbox curves with the shot for free.
    public static class ForgeHomingPatch
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("WeaponForge.Homing");

        // Reused for every acquisition scan so a 20-shots-per-second stream
        // does not allocate a fresh list 20 times a second. Physics2D fills it
        // in place; the List form is the one Unity 6 wants (the older
        // OverlapCircleNonAlloc array overload is deprecated).
        private static readonly List<Collider2D> _hits =
            new List<Collider2D>(48);

        [HarmonyPatch(typeof(Projectile), "Shoot")]
        public class OnShoot
        {
            static void Postfix(Projectile __instance)
            {
                var homing = __instance.GetComponent<ForgeHoming>();

                if (homing == null)
                    return;

                homing.shot = true;
                homing.startTime = Time.time;
                homing.turned = 0f;
                homing.target = null;
                homing.targetAim = null;
                homing.nextScan = 0f;

                // The same mask the projectile itself collides with, computed
                // the same way the game computes it (Projectile.Shoot does
                // Physics2D.GetLayerCollisionMask on its own layer). Deriving
                // it rather than reading the private collisionLayerMask field
                // sidesteps the FieldRefAccess value-type trap - that field is
                // a LayerMask, not an int, and asking Harmony for the wrong one
                // throws.
                homing.mask =
                    Physics2D.GetLayerCollisionMask(
                        __instance.gameObject.layer);

                Acquire(__instance, homing);
            }
        }

        [HarmonyPatch(typeof(Projectile), "FixedUpdate")]
        public class OnFixedUpdate
        {
            static void Prefix(Projectile __instance)
            {
                var homing = __instance.GetComponent<ForgeHoming>();

                if (homing == null || !homing.shot)
                    return;

                try
                {
                    Steer(__instance, homing);
                }
                catch (Exception e)
                {
                    // Never let a steering slip kill the projectile's own
                    // FixedUpdate - that would freeze the shot in mid-air
                    // instead of just flying straight.
                    homing.shot = false;
                    Log.LogError("Homing failed, shot flies straight: " + e);
                }
            }
        }

        private static void Steer(Projectile p, ForgeHoming h)
        {
            if (h.delay > 0f && Time.time - h.startTime < h.delay)
                return;

            if (h.maxTurn > 0f && h.turned >= h.maxTurn)
                return;

            // Lost the target (died, despawned) - look for another.
            if (h.target == null ||
                !h.target.gameObject.activeInHierarchy)
            {
                h.target = null;
                h.targetAim = null;

                if (!h.retarget)
                    return;

                if (Time.time < h.nextScan)
                    return;

                Acquire(p, h);

                if (h.target == null)
                    return;
            }

            Vector2 velocity = p.Velocity;
            float speed = velocity.magnitude;

            if (speed <= 0.0001f)
                return;

            Vector2 here = p.transform.position;
            Vector2 aim = h.target.position;

            if (h.predict && h.targetAim != null)
            {
                // Lead the target: where will it be when we get there? One
                // pass is enough - iterating would chase its own tail.
                float eta = Vector2.Distance(here, aim) / speed;
                aim += h.targetAim.Velocity * eta;
            }

            Vector2 toTarget = aim - here;

            if (toTarget.sqrMagnitude <= 0.000001f)
                return;

            float current =
                Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;

            float desired =
                Mathf.Atan2(toTarget.y, toTarget.x) * Mathf.Rad2Deg;

            float delta = Mathf.DeltaAngle(current, desired);
            float step = h.turnRate * Time.fixedDeltaTime;

            step = Mathf.Clamp(delta, -step, step);

            if (h.maxTurn > 0f)
            {
                float left = h.maxTurn - h.turned;

                if (Mathf.Abs(step) > left)
                    step = Mathf.Sign(step) * left;

                h.turned += Mathf.Abs(step);
            }

            if (Mathf.Abs(step) < 0.0001f)
                return;

            // Rotate about +Z so a positive step turns counter-clockwise,
            // matching the sign convention of the angles measured above.
            Vector2 steered =
                Quaternion.AngleAxis(step, Vector3.forward) * velocity;

            // Re-normalise to the original speed: rotating a vector should not
            // change its length, but the quaternion round-trip drifts by a hair
            // and thousands of frames of drift would visibly change the speed.
            p.Velocity = steered.normalized * speed;

            if (h.faceTravel)
            {
                p.transform.rotation =
                    Quaternion.FromToRotation(Vector3.right, p.Velocity);
            }
        }

        private static void Acquire(Projectile p, ForgeHoming h)
        {
            h.nextScan = Time.time + 0.1f;

            Vector2 forward = p.Velocity;

            if (forward.sqrMagnitude <= 0.000001f)
                forward = p.transform.right;

            AimAssistTarget found =
                FindTarget(
                    p, p.transform.position, forward,
                    h.range, h.cone, h.mask);

            h.target = found != null ? found.transform : null;
            h.targetAim = found;
        }

        // Nearest-to-straight-ahead target inside the cone, which is how the
        // game's own PhysicsProjectile.TryFindTarget chooses: smallest angle
        // off the current heading wins, not smallest distance. That keeps a
        // stream sweeping smoothly instead of snapping between enemies.
        //
        // Public because ricochet's "seek" asks exactly the same question -
        // "what should this shot turn toward from here" - and one
        // implementation means both features agree on what counts as a target.
        public static AimAssistTarget FindTarget(
            Projectile p,
            Vector2 here,
            Vector2 forward,
            float range,
            float cone,
            int mask)
        {
            // useTriggers on: an enemy's hurtbox is often a trigger collider,
            // and skipping those would make half the targets invisible here.
            // A default ContactFilter2D filters nothing until a use* flag is
            // set, so this ends up as "these layers, triggers included".
            // SetLayerMask flips useLayerMask on for us.
            var filter = new ContactFilter2D();
            filter.useTriggers = true;
            filter.SetLayerMask(mask);

            int count = Physics2D.OverlapCircle(here, range, filter, _hits);

            float bestAngle = cone;
            AimAssistTarget best = null;

            for (int i = 0; i < count && i < _hits.Count; i++)
            {
                Collider2D col = _hits[i];

                if (col == null)
                    continue;

                // AimAssistTarget is the game's own "this is a legitimate
                // thing to shoot at" marker, so reusing it means homing picks
                // exactly what the player's aim assist would - and never
                // terrain, which the collision mask alone would include.
                var aimTarget = col.GetComponentInParent<AimAssistTarget>();

                if (aimTarget == null)
                    continue;

                // Don't chase our own side.
                if (p != null && p.Owner != null)
                {
                    var unit = col.GetComponentInParent<Unit>();

                    if (unit != null && p.Owner.IsFriendsWith(unit))
                        continue;
                }

                Vector2 to = (Vector2)aimTarget.transform.position - here;

                if (to.sqrMagnitude <= 0.000001f)
                    continue;

                float angle = Vector2.Angle(forward, to);

                if (angle < bestAngle)
                {
                    bestAngle = angle;
                    best = aimTarget;
                }
            }

            return best;
        }
    }
}
