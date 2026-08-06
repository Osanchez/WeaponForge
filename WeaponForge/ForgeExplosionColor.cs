using UnityEngine;

namespace WeaponForge
{
    // Marks a projectile whose EXPLOSION should be recoloured.
    //
    // The game gives an explosion no colour of its own: the visual comes
    // from damages[0].damageType.explosionBasePrefab, so the only stock way
    // to change it is to change the damage type - which also changes what
    // the damage counts as. This carries a colour on the shot instead, and
    // ForgeExplosionColorPatch tints the burst as it spawns.
    public class ForgeExplosionColor : MonoBehaviour
    {
        public Color color = Color.white;

        // An explosion is a one-shot burst, so "rainbow" cannot cycle
        // within one blast - instead each blast takes the next hue, so a
        // rapid-fire explosive walks the wheel.
        public bool rainbow;
        public float rgbSpeed = 0.5f;

        public Color Resolve()
        {
            if (!rainbow)
                return color;

            float hue = Mathf.Repeat(Time.time * rgbSpeed, 1f);
            return Color.HSVToRGB(hue, 1f, 1f);
        }
    }
}
