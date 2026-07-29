using System.Collections.Generic;
using UnityEngine;

namespace WeaponForge
{
    // Runs the orbit: keeps N orb visuals evenly spaced on a circle around
    // the ship, spins them, and (optionally) damages enemies on contact,
    // blocks enemy shots, and pushes enemies. Orbs can be destructible -
    // dying when they hit an enemy and/or terrain - and regenerate by a
    // timer and/or on fire. Orb count and per-hit damage are read LIVE from
    // the equipped weapon, so +projectile/+damage modules scale the ring.
    public class ForgeOrbitController : MonoBehaviour
    {
        private class Orb
        {
            public GameObject go;
            public bool alive = true;
            public float regenAt;

            // Spiral state (unused when cfg.spiral == Off).
            public float cycleStart;   // when this spiral-out cycle began
            public bool launched;      // Launch mode: detached, flying free
            public Vector3 launchPos;  // integrated position while launched
            public Vector3 launchVel;  // world velocity while launched
            public Vector3 prevPos;    // last spiral position (for launch dir)
            public bool hasPrev;
        }

        private ForgeOrbit.Config cfg;
        private Shooter shooter;

        // Driven by ForgeOrbitPatch for Toggle / Fire modes.
        public bool toggledOn;
        public float fireActiveUntil;

        private bool active;
        private float angleDeg;
        private float activeSince;
        private float flingUntil;

        private readonly List<Orb> orbs = new List<Orb>();
        private readonly Dictionary<HealthBase, float> lastHit =
            new Dictionary<HealthBase, float>();

        private ContactFilter2D enemyFilter;
        private ContactFilter2D projFilter;
        private ContactFilter2D groundFilter;
        private readonly List<Collider2D> buffer = new List<Collider2D>();
        private bool _filtersReady;

        public void Init(ForgeOrbit.Config config, Shooter owner)
        {
            cfg = config;
            shooter = owner;
        }

        public void SetActive(bool a)
        {
            if (a == active)
                return;
            active = a;
            if (active)
            {
                activeSince = Time.time;
                // Restart each orb's spiral from the CURRENT ship position.
                // While inactive, Time.time keeps advancing and the orbs kept
                // stale cycle timing + world positions from the last time the
                // ring ran; without this, a Launch-spiral orb would sling off
                // in the wrong direction (or pop in far away) on reactivation.
                for (int i = 0; i < orbs.Count; i++)
                {
                    orbs[i].cycleStart = Time.time;
                    orbs[i].hasPrev = false;
                    orbs[i].launched = false;
                }
            }
        }

        public void TriggerFling()
        {
            if (cfg != null && cfg.fling)
                flingUntil = Time.time + cfg.flingDuration;
        }

        // Called when the weapon fires / re-toggles: regenerate dead orbs
        // if regen is fire-based.
        public void RegenOnFire()
        {
            if (cfg == null)
                return;
            if (cfg.regenMode != ForgeOrbit.RegenMode.Fire &&
                cfg.regenMode != ForgeOrbit.RegenMode.Both)
                return;

            // Only revive orbs that are actually DEAD. Reviving live orbs
            // would reset their spiral (cycleStart = now) on every shot, and
            // because a suppressed-fire orbit fires every frame while held,
            // that would freeze spiraling orbs at the inner radius forever.
            for (int i = 0; i < orbs.Count; i++)
                if (!orbs[i].alive)
                    Revive(orbs[i]);
        }

        private void EnsureFilters()
        {
            if (_filtersReady)
                return;
            _filtersReady = true;

            enemyFilter = new ContactFilter2D();
            enemyFilter.useTriggers = true;
            enemyFilter.SetLayerMask(LayerMask.GetMask("Entities", "Fruits"));

            projFilter = new ContactFilter2D();
            projFilter.useTriggers = true;
            projFilter.SetLayerMask(LayerMask.GetMask("EnemyProjectiles"));

            groundFilter = new ContactFilter2D();
            groundFilter.useTriggers = true;
            groundFilter.SetLayerMask(LayerMask.GetMask("Ground"));
        }

        private void Update()
        {
            if (cfg == null || shooter == null || shooter.Unit == null)
                return;

            if (!active)
            {
                for (int i = 0; i < orbs.Count; i++)
                    if (orbs[i].go != null)
                        orbs[i].go.SetActive(false);
                return;
            }

            EnsureFilters();

            int count = 4;
            if (shooter.Weapon != null)
                count = Mathf.Max(1, Mathf.RoundToInt(shooter.Weapon.ProjectileCount));
            SetOrbCount(count);

            float spinMul = (cfg.spinUpSeconds > 0f)
                ? Mathf.Clamp01((Time.time - activeSince) / cfg.spinUpSeconds)
                : 1f;

            float dir = cfg.clockwise ? -1f : 1f;
            angleDeg += dir * cfg.speed * spinMul * Time.deltaTime;

            // Fixed-ring radius (with pulse/fling). Spiral modes drive their
            // own per-orb radius instead, so pulse/fling don't apply there.
            float ringRadius = cfg.radius;
            if (cfg.spiral == ForgeOrbit.SpiralMode.Off)
            {
                if (cfg.pulseAmount > 0f)
                    ringRadius += Mathf.Sin(Time.time * cfg.pulseSpeed) * cfg.pulseAmount;
                if (cfg.fling && Time.time < flingUntil)
                {
                    float p = 1f - (flingUntil - Time.time) / cfg.flingDuration;
                    ringRadius += Mathf.Sin(p * Mathf.PI) * cfg.flingReach;
                }
            }

            Vector3 center = shooter.Unit.transform.position;
            float step = 360f / count;

            for (int i = 0; i < orbs.Count; i++)
            {
                Orb orb = orbs[i];

                // The orb list only ever grows; if the live count shrank
                // (e.g. a +projectile module lost power), hide and skip the
                // extras so they don't render or keep dealing contact damage.
                if (i >= count)
                {
                    if (orb.go != null)
                        orb.go.SetActive(false);
                    continue;
                }

                if (!orb.alive)
                {
                    // Timer regen.
                    if ((cfg.regenMode == ForgeOrbit.RegenMode.Timer ||
                         cfg.regenMode == ForgeOrbit.RegenMode.Both) &&
                        Time.time >= orb.regenAt)
                    {
                        Revive(orb);
                    }
                    else
                    {
                        if (orb.go != null)
                            orb.go.SetActive(false);
                        continue;
                    }
                }

                float a = (angleDeg + i * step) * Mathf.Deg2Rad;
                Vector3 pos = (cfg.spiral == ForgeOrbit.SpiralMode.Off)
                    ? center + new Vector3(Mathf.Cos(a) * ringRadius,
                                           Mathf.Sin(a) * ringRadius, 0f)
                    : SpiralPos(orb, a, center);

                if (orb.go != null)
                {
                    orb.go.SetActive(true);
                    orb.go.transform.position = pos;
                }

                bool hitEnemy = false;
                if (cfg.contactDamage || cfg.destroyOnEnemy)
                    hitEnemy = DamageAt(pos);
                if (cfg.pushForce > 0f)
                    PushAt(pos, center);
                if (cfg.blockProjectiles)
                    BlockAt(pos);

                bool destroy = false;
                if (cfg.destroyOnEnemy && hitEnemy)
                    destroy = true;
                if (!destroy && cfg.destroyOnTerrain && TerrainAt(pos))
                    destroy = true;

                if (destroy)
                    Kill(orb, pos);
            }
        }

        // Per-orb position for the spiral modes. Grows the radius from
        // spiralInner to the outer radius over spiralOutTime, then either
        // detaches and flies straight off (Launch) or snaps back to the
        // center and repeats (Sweep). Mutates the orb's spiral state.
        private Vector3 SpiralPos(Orb orb, float angleRad, Vector3 center)
        {
            // A launched orb flies straight until it's far enough away,
            // then recycles back to a fresh spiral from the inner radius.
            if (orb.launched)
            {
                orb.launchPos += orb.launchVel * Time.deltaTime;
                if ((orb.launchPos - center).magnitude <= cfg.spiralKillDistance)
                    return orb.launchPos;

                orb.launched = false;
                orb.hasPrev = false;
                orb.cycleStart = Time.time;
                // fall through: place it back at the inner radius this frame
            }

            float dur = Mathf.Max(0.01f, cfg.spiralOutTime);
            float frac = (Time.time - orb.cycleStart) / dur;

            if (frac >= 1f)
            {
                Vector3 outerPos = Polar(center, angleRad, cfg.radius);

                if (cfg.spiral == ForgeOrbit.SpiralMode.Launch)
                {
                    // Detach: keep drifting the way the spiral was carrying
                    // it (mostly tangential + outward) so it slings away.
                    Vector3 fly = orb.hasPrev
                        ? (outerPos - orb.prevPos)
                        : (outerPos - center);
                    if (fly.sqrMagnitude < 1e-5f)
                        fly = (outerPos - center);

                    orb.launched = true;
                    orb.launchVel = fly.normalized * cfg.spiralLaunchSpeed;
                    orb.launchPos = outerPos;
                    return outerPos;
                }

                // Sweep: restart the spiral from the inner radius.
                orb.cycleStart = Time.time;
                frac = 0f;
            }

            Vector3 pos = Polar(center, angleRad,
                Mathf.Lerp(cfg.spiralInner, cfg.radius, frac));
            orb.prevPos = pos;
            orb.hasPrev = true;
            return pos;
        }

        private static Vector3 Polar(Vector3 center, float angleRad, float r)
        {
            return center + new Vector3(
                Mathf.Cos(angleRad) * r, Mathf.Sin(angleRad) * r, 0f);
        }

        private bool DamageAt(Vector3 pos)
        {
            if (shooter.Weapon == null)
                return false;

            bool hit = false;
            int n = Physics2D.OverlapCircle(pos, cfg.hitRadius, enemyFilter, buffer);
            for (int j = 0; j < n; j++)
            {
                var hb = buffer[j].GetComponentInParent<HealthBase>();
                if (hb == null)
                    continue;

                hit = true;

                if (!cfg.contactDamage)
                    continue;

                float last;
                if (lastHit.TryGetValue(hb, out last) &&
                    Time.time - last < cfg.damageRepeatDelay)
                {
                    continue;
                }

                hb.TakeDamage(shooter.Weapon.Damage);
                lastHit[hb] = Time.time;
            }
            return hit;
        }

        private bool TerrainAt(Vector3 pos)
        {
            return Physics2D.OverlapCircle(pos, cfg.hitRadius, groundFilter, buffer) > 0;
        }

        private void PushAt(Vector3 pos, Vector3 center)
        {
            int n = Physics2D.OverlapCircle(pos, cfg.hitRadius, enemyFilter, buffer);
            for (int j = 0; j < n; j++)
            {
                var rb = buffer[j].attachedRigidbody;
                if (rb == null)
                    continue;
                Vector2 away = ((Vector2)rb.transform.position - (Vector2)center).normalized;
                rb.AddForce(away * cfg.pushForce);
            }
        }

        private void BlockAt(Vector3 pos)
        {
            int n = Physics2D.OverlapCircle(pos, cfg.hitRadius, projFilter, buffer);
            for (int j = 0; j < n; j++)
                if (buffer[j] != null)
                    Destroy(buffer[j].transform.root.gameObject);
        }

        private void Kill(Orb orb, Vector3 pos)
        {
            orb.alive = false;
            if (orb.go != null)
                orb.go.SetActive(false);

            if (cfg.popExplosion)
                Pop(pos);

            // Timer-based regen schedules a respawn; fire-only waits for a
            // shot (RegenOnFire), so park it far in the future.
            orb.regenAt = (cfg.regenMode == ForgeOrbit.RegenMode.Fire)
                ? float.MaxValue
                : Time.time + cfg.regenTime;
        }

        private void Pop(Vector3 pos)
        {
            if (shooter.Weapon == null)
                return;

            int n = Physics2D.OverlapCircle(pos, cfg.popRadius, enemyFilter, buffer);
            for (int j = 0; j < n; j++)
            {
                var hb = buffer[j].GetComponentInParent<HealthBase>();
                if (hb != null)
                    hb.TakeDamage(shooter.Weapon.Damage);
            }
        }

        private void Revive(Orb orb)
        {
            orb.alive = true;
            orb.regenAt = 0f;
            // Restart its spiral cleanly from the inner radius.
            orb.launched = false;
            orb.hasPrev = false;
            orb.cycleStart = Time.time;
        }

        private void SetOrbCount(int count)
        {
            float dur = Mathf.Max(0.01f, cfg.spiralOutTime);
            while (orbs.Count < count)
            {
                // Stagger each orb's spiral cycle so they bloom/launch in
                // sequence rather than all at once (harmless when Off).
                int idx = orbs.Count;
                orbs.Add(new Orb
                {
                    go = SpawnOrb(),
                    alive = true,
                    cycleStart = Time.time - dur * ((idx % count) / (float)count)
                });
            }

            for (int i = 0; i < orbs.Count; i++)
                if (i >= count && orbs[i].go != null)
                    orbs[i].go.SetActive(false);
        }

        private GameObject SpawnOrb()
        {
            GameObject orb;

            if (cfg.visualPrefab != null)
            {
                orb = Instantiate(cfg.visualPrefab);
                var proj = orb.GetComponentInChildren<Projectile>(true);
                if (proj != null)
                    proj.enabled = false;
                foreach (var rb in orb.GetComponentsInChildren<Rigidbody2D>(true))
                    rb.simulated = false;
                foreach (var col in orb.GetComponentsInChildren<Collider2D>(true))
                    col.enabled = false;
            }
            else
            {
                orb = new GameObject("Forge Orb");
            }

            orb.name = "Forge Orb";
            return orb;
        }

        private void OnDestroy()
        {
            for (int i = 0; i < orbs.Count; i++)
                if (orbs[i].go != null)
                    Destroy(orbs[i].go);
            orbs.Clear();
        }
    }
}
