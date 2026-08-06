using UnityEngine;

namespace WeaponForge
{
    // "This object picked its own colour - leave it alone."
    //
    // Recolor and RgbAnimator both sweep the WHOLE hierarchy and write
    // startColor on every child ParticleSystem they find. That is right for
    // projectileColor (one shot, one colour) but wrong the moment a child
    // asks for a colour of its own: a red projectileColor would repaint a
    // trail the file explicitly asked to be blue, and a rainbow root would
    // overwrite it again every frame with nothing in the log to explain it.
    //
    // Marking the child instead of special-casing the sweeps keeps the rule
    // in one place, so anything added later (a muzzle child, an impact puff)
    // gets the same protection by adding the marker.
    public class ForgeColorLock : MonoBehaviour
    {
    }
}
