using System.Collections.Generic;
using UnityEngine;

namespace WeaponForge
{
    // Tag on a projectile prefab giving its (otherwise infinite) piercing a
    // cap: after passing through `limit` enemies it's destroyed on the next
    // contact. Optional damage falloff per pierce and an explosion when it
    // caps out. ForgePiercePatch does the counting.
    public class ForgePierceCap : MonoBehaviour
    {
        public int limit = 2;
        public float falloff;          // 0..1 damage lost per enemy pierced
        public bool explodeOnLimit;

        // Distinct listeners already pierced (per projectile instance).
        public readonly HashSet<object> seen = new HashSet<object>();
    }
}
