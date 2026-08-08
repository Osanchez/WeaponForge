using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace WeaponForge
{
    // Drives ForgeDeflect: sweep for enemy bullets near the shot and either
    // destroy them or turn them around.
    //
    // Turning one around is more than reversing its velocity. A projectile's
    // FACTION is its GameObject layer, and the game bakes that into a private
    // collisionLayerMask at Shoot time - so a bullet whose layer is changed
    // afterwards would keep colliding with whatever it was originally told to.
    // Both have to move together, plus Owner, or the reflected shot either
    // passes through everything or comes back and hits you.
    public static class ForgeDeflectPatch
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("WeaponForge.Deflect");

        // Reused so a sweep does not allocate.
        private static readonly List<Projectile> _caught =
            new List<Projectile>(16);

        // collisionLayerMask is private AND typed LayerMask, not int. Plain
        // FieldInfo rather than Harmony's FieldRefAccess<T,F>, which throws on
        // exactly that kind of value-type mismatch (it is what broke phasing
        // once already).
        private static FieldInfo _maskField;
        private static bool _maskLooked;

        private static int _playerLayer = -2;
        private static int _enemyLayer = -2;

        [HarmonyPatch(typeof(Projectile), "Shoot")]
        public class OnShoot
        {
            static void Postfix(Projectile __instance)
            {
                var d = __instance.GetComponent<ForgeDeflect>();

                if (d == null)
                    return;

                d.shot = true;
                d.used = 0;
                d.nextSweep = 0f;
            }
        }

        [HarmonyPatch(typeof(Projectile), "FixedUpdate")]
        public class OnFixedUpdate
        {
            static void Prefix(Projectile __instance)
            {
                var d = __instance.GetComponent<ForgeDeflect>();

                if (d == null || !d.shot)
                    return;

                if (Time.time < d.nextSweep)
                    return;

                d.nextSweep = Time.time + Mathf.Max(0.01f, d.interval);

                try
                {
                    Sweep(__instance, d);
                }
                catch (Exception e)
                {
                    d.shot = false;
                    Log.LogError("Deflect failed, shot flies on: " + e);
                }
            }
        }

        private static void Sweep(Projectile p, ForgeDeflect d)
        {
            if (d.maxTotal > 0 && d.used >= d.maxTotal)
                return;

            int room = (d.maxTotal > 0) ? (d.maxTotal - d.used) : 0;

            _caught.Clear();

            int n = ForgeProjectileTracker.CollectNear(
                p.transform.position, d.radius, room, _caught);

            if (n == 0)
                return;

            d.used += n;

            for (int i = 0; i < _caught.Count; i++)
            {
                Projectile bullet = _caught[i];

                if (bullet == null)
                    continue;

                if (d.mode == 0)
                {
                    UnityEngine.Object.Destroy(bullet.gameObject);
                    continue;
                }

                Reflect(bullet, p, d);
            }

            _caught.Clear();
        }

        // Turn an enemy bullet into one of ours.
        private static void Reflect(
            Projectile bullet, Projectile source, ForgeDeflect d)
        {
            try
            {
                EnsureLayers();

                float speed = bullet.Velocity.magnitude;

                if (speed <= 0.01f)
                    speed = 10f;

                speed *= Mathf.Max(0.05f, d.speedMultiplier);

                Vector2 dir = -bullet.Velocity.normalized;

                if (d.aim == 1)
                {
                    // Same target-finder homing and ricochet use, so all three
                    // agree on what counts as a target and none of them can
                    // pick terrain or your own side.
                    int mask =
                        (_playerLayer >= 0)
                            ? Physics2D.GetLayerCollisionMask(_playerLayer)
                            : Physics2D.GetLayerCollisionMask(
                                bullet.gameObject.layer);

                    AimAssistTarget found =
                        ForgeHomingPatch.FindTarget(
                            source, bullet.transform.position, dir,
                            d.aimRange, 180f, mask);

                    if (found != null)
                    {
                        Vector2 to =
                            (Vector2)found.transform.position -
                            (Vector2)bullet.transform.position;

                        if (to.sqrMagnitude > 0.000001f)
                            dir = to.normalized;
                    }
                }

                bullet.Velocity = dir * speed;

                // Face the new heading, or a directional sprite flies
                // backwards for the rest of its life.
                bullet.transform.rotation =
                    Quaternion.FromToRotation(Vector3.right, (Vector3)dir);

                // Whose side it is on. The layer is the faction, and the
                // baked mask has to follow it.
                if (_playerLayer >= 0 && _enemyLayer >= 0)
                {
                    VisualCustomizer.RemapLayer(
                        bullet.gameObject, _enemyLayer, _playerLayer);

                    SetMask(
                        bullet,
                        Physics2D.GetLayerCollisionMask(_playerLayer));
                }

                // So the game's friendly-fire check treats it as ours - and,
                // usefully, so a kill with a reflected bullet is credited to
                // the player.
                if (source.Owner != null)
                    bullet.Owner = source.Owner;

                if (d.damageMultiplier != 1f)
                {
                    Damage dmg = bullet.Damage;
                    dmg.amount *= d.damageMultiplier;
                    bullet.Damage = dmg;
                }
            }
            catch (Exception e)
            {
                // A bullet we half-converted is worse than one we simply
                // removed.
                Log.LogError(
                    "Could not reflect a shot, destroying it instead: " + e);

                if (bullet != null)
                    UnityEngine.Object.Destroy(bullet.gameObject);
            }
        }

        private static void EnsureLayers()
        {
            if (_playerLayer == -2)
                _playerLayer = LayerMask.NameToLayer("PlayerProjectiles");

            if (_enemyLayer == -2)
                _enemyLayer = LayerMask.NameToLayer("EnemyProjectiles");
        }

        private static void SetMask(Projectile p, LayerMask mask)
        {
            if (!_maskLooked)
            {
                _maskLooked = true;
                _maskField =
                    typeof(Projectile).GetField(
                        "collisionLayerMask",
                        BindingFlags.NonPublic | BindingFlags.Instance);
            }

            if (_maskField == null)
                return;

            // Boxed as LayerMask, matching the field's real type.
            _maskField.SetValue(p, mask);
        }
    }
}
