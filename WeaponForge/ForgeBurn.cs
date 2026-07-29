using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace WeaponForge
{
    // WeaponForge's OWN copy of the burn tick-rate + burn-color engine
    // (mirrors ModuleForge's ModuleForgeBurn). It is only USED when
    // ModuleForge is NOT installed - when ModuleForge is present,
    // WeaponForge defers to it (see ForgeBurnCompat) so the two mods never
    // both patch the same game methods. Keeping a full copy here means the
    // burn options still work with WeaponForge alone.
    //
    // Burn tick rate lives on the VICTIM (enemy Unit.burnProperties.
    // fireTickRate); we raise the tick FREQUENCY additively while a burn
    // weapon is equipped, clamped, and never for the player (owner).
    public static class ForgeBurn
    {
        public static float MaxTicksPerSecond = 100f;

        public static float Delta { get; private set; }
        public static bool EverModified { get; private set; }

        private static readonly Dictionary<Unit.Data, int> _excluded =
            new Dictionary<Unit.Data, int>();

        private static readonly ConditionalWeakTable<Unit.Data, Box> _base =
            new ConditionalWeakTable<Unit.Data, Box>();

        private static readonly List<ForgeBurnColorEffect> _colors =
            new List<ForgeBurnColorEffect>();

        private class Box { public float baseInterval; public bool set; }

        public static void AddBooster(Unit.Data owner, float amount)
        {
            Delta += amount;
            ExcludeOwner(owner);
        }

        public static void RemoveBooster(Unit.Data owner, float amount)
        {
            Delta -= amount;
            if (Delta < 0f)
                Delta = 0f;
            ReleaseOwner(owner);
        }

        public static void ExcludeOwner(Unit.Data owner)
        {
            if (owner == null)
                return;

            int n;
            _excluded.TryGetValue(owner, out n);
            _excluded[owner] = n + 1;
        }

        public static void ReleaseOwner(Unit.Data owner)
        {
            if (owner == null)
                return;

            int n;
            if (_excluded.TryGetValue(owner, out n))
            {
                if (n <= 1)
                    _excluded.Remove(owner);
                else
                    _excluded[owner] = n - 1;
            }
        }

        public static void AddColor(Unit.Data owner, ForgeBurnColorEffect effect)
        {
            if (effect != null && !_colors.Contains(effect))
                _colors.Add(effect);
            ExcludeOwner(owner);
        }

        public static void RemoveColor(Unit.Data owner, ForgeBurnColorEffect effect)
        {
            _colors.Remove(effect);
            ReleaseOwner(owner);
        }

        public static bool HasColor
        {
            get { return _colors.Count > 0; }
        }

        public static bool ColorTerrain
        {
            get
            {
                return _colors.Count > 0 &&
                       _colors[_colors.Count - 1].includeTerrain;
            }
        }

        public static Color GetEmitColor()
        {
            if (_colors.Count == 0)
                return Color.white;
            return _colors[_colors.Count - 1].GetEmitColor();
        }

        public static bool IsExcluded(Unit.Data data)
        {
            return data != null && _excluded.ContainsKey(data);
        }

        public static void Reset()
        {
            Delta = 0f;
            EverModified = false;
            _excluded.Clear();
            _colors.Clear();
        }

        public static void ApplyTo(Unit.Data data)
        {
            if (data == null)
                return;

            Box box = _base.GetValue(data, Create);
            if (!box.set)
            {
                box.baseInterval = data.burnProperties.fireTickRate;
                box.set = true;
            }

            float baseInterval = box.baseInterval;
            float desired;

            if (Delta <= 0f || baseInterval <= 0f || IsExcluded(data))
            {
                desired = baseInterval;
            }
            else
            {
                float freq = 1f / baseInterval + Delta;
                if (MaxTicksPerSecond > 0f && freq > MaxTicksPerSecond)
                    freq = MaxTicksPerSecond;
                desired = 1f / freq;
                EverModified = true;
            }

            if (data.burnProperties.fireTickRate != desired)
                data.burnProperties.fireTickRate = desired;
        }

        private static Box Create(Unit.Data key)
        {
            return new Box();
        }
    }
}
