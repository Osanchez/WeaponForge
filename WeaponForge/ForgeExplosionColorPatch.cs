using HarmonyLib;
using UnityEngine;

namespace WeaponForge
{
    // Tints an explosion's particle burst.
    //
    // ExplosionManager.SpawnExplosion has no idea which weapon it is
    // serving, so this is a small handshake: a prefix on the projectile's own
    // SpawnExplosion records the colour that shot wants, ExplosionVisual.Setup
    // applies it to the freshly spawned burst, and the postfix clears it. The
    // window is one synchronous call, so nothing else can slip in between.
    public static class ForgeExplosionTint
    {
        private static Color? _pending;

        // Setup() recurses into childVisuals, so the postfix fires once per
        // visual in the tree. Depth lets us tint exactly once, on the way out
        // of the OUTERMOST call - tinting per-visual would multiply the tint
        // into the children several times over.
        private static int _depth;

        public static void Begin(Component projectile)
        {
            if (projectile == null)
                return;

            var tag = projectile.GetComponent<ForgeExplosionColor>();
            _pending = (tag != null) ? tag.Resolve() : (Color?)null;
        }

        public static void End()
        {
            _pending = null;
        }

        public static void Enter()
        {
            _depth++;
        }

        // Returns the colour to apply, but only as the outermost Setup call
        // unwinds.
        public static bool Exit(out Color color)
        {
            color = Color.white;

            if (_depth > 0)
                _depth--;

            if (_depth != 0 || !_pending.HasValue)
                return false;

            color = _pending.Value;
            return true;
        }
    }

    [HarmonyPatch(typeof(Projectile), "SpawnExplosion")]
    public class ForgeExplosionColorProjectilePatch
    {
        static void Prefix(Projectile __instance)
        {
            try { ForgeExplosionTint.Begin(__instance); }
            catch { }
        }

        static void Postfix()
        {
            try { ForgeExplosionTint.End(); }
            catch { }
        }
    }

    [HarmonyPatch(typeof(PhysicsProjectile), "SpawnExplosion")]
    public class ForgeExplosionColorPhysicsPatch
    {
        static void Prefix(PhysicsProjectile __instance)
        {
            try { ForgeExplosionTint.Begin(__instance); }
            catch { }
        }

        static void Postfix()
        {
            try { ForgeExplosionTint.End(); }
            catch { }
        }
    }

    [HarmonyPatch(typeof(ExplosionVisual), "Setup")]
    public class ForgeExplosionVisualPatch
    {
        static void Prefix()
        {
            try { ForgeExplosionTint.Enter(); }
            catch { }
        }

        static void Postfix(ExplosionVisual __instance)
        {
            try
            {
                Color color;
                if (!ForgeExplosionTint.Exit(out color))
                    return;

                // Multiply-tint, so the artist's gradients and alpha fades
                // survive and only the hue moves - same treatment as the
                // muzzle flash. A dark colour therefore DIMS the blast.
                VisualCustomizer.Tint(__instance.gameObject, color);
            }
            catch { }
        }
    }
}
