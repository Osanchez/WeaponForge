using UnityEngine;

namespace WeaponForge
{
    // Tag on a projectile prefab marking it as "phasing" - it should pass
    // through terrain but still hit enemies. ForgePhasingPatch reads this.
    public class ForgePhasing : MonoBehaviour
    {
    }
}
