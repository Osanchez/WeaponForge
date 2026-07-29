using System;

namespace WeaponForge
{
    // Weapon-module effect: while the weapon is equipped, speeds up the
    // burn tick FREQUENCY on enemies (additive, clamped, player excluded).
    // Routes through ForgeBurnCompat so it feeds ModuleForge when that mod
    // is present, or WeaponForge's own ForgeBurn engine otherwise.
    //
    // Save/load safe: a weapon module persists by Id and its effects are
    // rebuilt from the registry via Clone() on continue, re-firing
    // OnInstalled so the boost re-registers.
    [Serializable]
    public class ForgeBurnRateEffect : ModuleEffect
    {
        public float ticksPerSecond;

        private bool _registered;
        private float _applied;
        private Unit.Data _owner;

        public override void OnInstalled(Unit.Data unit)
        {
            if (_registered)
                return;

            _applied = ticksPerSecond;
            _owner = unit;
            ForgeBurnCompat.AddRate(unit, _applied);
            _registered = true;
        }

        public override void OnUninstalled(Unit.Data unit)
        {
            if (!_registered)
                return;

            ForgeBurnCompat.RemoveRate(_owner ?? unit, _applied);
            _registered = false;
            _owner = null;
            _applied = 0f;
        }

        public override ModuleEffect Clone()
        {
            return new ForgeBurnRateEffect
            {
                ticksPerSecond = this.ticksPerSecond
            };
        }
    }
}
