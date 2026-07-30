using System.Collections.Generic;

namespace WeaponForge
{
    // Records which built weapons got phasing and/or a pierce cap, so the
    // weapon stat card (ForgeWeaponStatsPatch) can show it. Keyed by the
    // WeaponData - matched at runtime via WeaponBase.TemplateData, the same
    // key ForgeOrbit uses.
    public static class ForgeWeaponInfo
    {
        private static readonly HashSet<WeaponData> _phasing =
            new HashSet<WeaponData>();
        private static readonly Dictionary<WeaponData, int> _pierce =
            new Dictionary<WeaponData, int>();

        public static void SetPhasing(WeaponData weapon)
        {
            if (weapon != null)
                _phasing.Add(weapon);
        }

        public static bool IsPhasing(WeaponData weapon)
        {
            return weapon != null && _phasing.Contains(weapon);
        }

        public static void SetPierce(WeaponData weapon, int limit)
        {
            if (weapon != null)
                _pierce[weapon] = limit;
        }

        public static bool TryGetPierce(WeaponData weapon, out int limit)
        {
            limit = 0;
            return weapon != null && _pierce.TryGetValue(weapon, out limit);
        }
    }
}
