using System;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace WeaponForge
{
    // Recolors and resizes weapon visuals. The game bakes projectile /
    // beam appearance into the prefab's child renderers and shares those
    // prefabs with the stock weapon, so we never mutate an original:
    // we clone the visual prefab, tint/scale the clone, and hand the
    // clone back to the weapon. Clones are parented under a permanently
    // inactive holder so instantiating them never runs Awake/OnEnable
    // (no ghost projectiles/minions at world origin); each still has
    // activeSelf == true, so the game's own Instantiate spawns them
    // active as normal.
    public static class VisualCustomizer
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("WeaponForge");

        private static Transform _holder;

        private static Transform Holder()
        {
            if (_holder == null)
            {
                var go = new GameObject("WeaponForge Prefab Cache");
                go.SetActive(false);
                go.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(go);
                _holder = go.transform;
            }

            return _holder;
        }

        public static GameObject ClonePrefab(GameObject original)
        {
            // Parented to an inactive holder => Awake/OnEnable do not
            // fire on the clone.
            GameObject clone =
                UnityEngine.Object.Instantiate(original, Holder());

            clone.hideFlags = HideFlags.HideAndDontSave;
            return clone;
        }

        // Accepts "#rrggbb" / "#rrggbbaa", HTML color names ("red",
        // "cyan"...), or a game ColorAsset name ("ColorPurple").
        public static bool TryParseColor(string text, out Color color)
        {
            color = Color.white;

            if (string.IsNullOrEmpty(text))
                return false;

            text = text.Trim();

            if (text.StartsWith("#"))
                return ColorUtility.TryParseHtmlString(text, out color);

            var colorAsset =
                JsonFieldMapper.FindAsset(
                    typeof(ColorAsset), text) as ColorAsset;

            if (colorAsset != null)
            {
                color = colorAsset.color;
                return true;
            }

            // Fall back to Unity's HTML color names.
            return ColorUtility.TryParseHtmlString(text, out color);
        }

        // Tint every renderer in the hierarchy. Covers projectile
        // sprites/trails and the hitscan beam's sprites/light/particle.
        public static void Recolor(GameObject root, Color c)
        {
            foreach (var sr in
                root.GetComponentsInChildren<SpriteRenderer>(true))
            {
                sr.color = c;
            }

            foreach (var lr in
                root.GetComponentsInChildren<LineRenderer>(true))
            {
                lr.startColor = c;
                lr.endColor = c;
            }

            foreach (var tr in
                root.GetComponentsInChildren<TrailRenderer>(true))
            {
                tr.startColor = c;
                tr.endColor =
                    new Color(c.r, c.g, c.b, 0f);
            }

            foreach (var ps in
                root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.startColor = c;
            }

            foreach (var light in
                root.GetComponentsInChildren<Light2D>(true))
            {
                light.color = c;
            }
        }

        // --- Faction / targeting ---------------------------------
        // A projectile only hits what its GameObject layer collides
        // with. Enemy projectiles bake to "EnemyProjectiles" (hits the
        // player); "PlayerProjectiles" hits enemies and, via the physics
        // matrix, spares the player. We re-faction by SWAPPING only the
        // projectile-faction layer, leaving every other layer (ground,
        // triggers, deployables) untouched - blanket re-layering breaks
        // prefabs like air mines that mix layers on purpose. Resolved by
        // name so it survives layer-index changes; -1 if the layer is
        // missing.
        public static int EnemyProjectileLayer()
        {
            return LayerMask.NameToLayer("EnemyProjectiles");
        }

        public static int PlayerProjectileLayer()
        {
            return LayerMask.NameToLayer("PlayerProjectiles");
        }

        // The layer a re-factioned prefab's root is EXPECTED to be on if
        // it still needs swapping ("from"), and what to swap it to.
        public static int FactionFromLayer(string target)
        {
            return target == "player"
                ? PlayerProjectileLayer()
                : EnemyProjectileLayer();
        }

        public static int FactionToLayer(string target)
        {
            return target == "player"
                ? EnemyProjectileLayer()
                : PlayerProjectileLayer();
        }

        // Recursively remap ONLY the objects currently on `from` to `to`.
        public static void RemapLayer(GameObject go, int from, int to)
        {
            if (go == null || from < 0 || to < 0)
                return;

            if (go.layer == from)
                go.layer = to;

            foreach (Transform child in go.transform)
                RemapLayer(child.gameObject, from, to);
        }

        // Hitscan has no faction check - only its layerMask matters.
        // "enemies" => Entities+Ground+Fruits (like the player laser);
        // "player" => Player+Ground+Fruits (like the enemy laser).
        public static LayerMask HitscanMask(string target)
        {
            if (target == "player")
                return LayerMask.GetMask("Player", "Ground", "Fruits");

            return LayerMask.GetMask("Entities", "Ground", "Fruits");
        }

        // Attach the RGB/rainbow cycler to a cloned prefab so every
        // instance spawned from it animates its own color.
        public static void ApplyRainbow(GameObject root, float speed)
        {
            var animator = root.AddComponent<RgbAnimator>();
            animator.speed = speed;
        }

        // Multiply the visual (and, for a caller-passed radius, physics)
        // size of a projectile-style prefab.
        public static void Scale(GameObject root, float multiplier)
        {
            root.transform.localScale =
                root.transform.localScale * multiplier;
        }

        // Beam thickness lives in the sprite renderers' size.y (the
        // beam is a sliced sprite; size.x = length is rewritten every
        // frame, size.y = thickness persists). Both fields are private.
        public static void ScaleBeamThickness(
            HitscanWeaponVisual visual,
            float multiplier)
        {
            ScaleSpriteHeight(visual, "fireSpriteRenderer", multiplier);
            ScaleSpriteHeight(visual, "warmUpSpriteRenderer", multiplier);
        }

        private static void ScaleSpriteHeight(
            HitscanWeaponVisual visual,
            string fieldName,
            float multiplier)
        {
            FieldInfo field =
                typeof(HitscanWeaponVisual).GetField(
                    fieldName,
                    BindingFlags.NonPublic | BindingFlags.Instance);

            if (field == null)
                return;

            var sr = field.GetValue(visual) as SpriteRenderer;

            if (sr == null)
                return;

            sr.size = new Vector2(
                sr.size.x,
                sr.size.y * multiplier);
        }
    }
}
