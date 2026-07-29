using System;
using UnityEngine;

namespace WeaponForge
{
    // Weapon-module effect: while the weapon is equipped, recolors the
    // burn flames on enemies (solid color or RGB rainbow), optionally
    // including terrain. The player's own burn is never recolored. Routes
    // through ForgeBurnCompat (feeds ModuleForge when present, else the
    // bundled ForgeBurn engine).
    [Serializable]
    public class ForgeBurnColorEffect : ModuleEffect
    {
        public bool rgb;
        public Color color = Color.white;
        public string colorLabel;
        public float rgbSpeed = 0.5f;
        public float saturation = 1f;
        public float brightness = 1f;
        public bool includeTerrain;

        private bool _registered;
        private Unit.Data _owner;
        private object _handle;   // opaque token from ForgeBurnCompat

        public Color GetEmitColor()
        {
            if (!rgb)
                return color;

            float hue = Time.time * rgbSpeed;
            hue -= Mathf.Floor(hue);
            return Color.HSVToRGB(hue, saturation, brightness);
        }

        public override void OnInstalled(Unit.Data unit)
        {
            if (_registered)
                return;

            _owner = unit;
            _handle = ForgeBurnCompat.AddColor(unit, this);
            _registered = true;
        }

        public override void OnUninstalled(Unit.Data unit)
        {
            if (!_registered)
                return;

            ForgeBurnCompat.RemoveColor(_owner ?? unit, this, _handle);
            _registered = false;
            _owner = null;
            _handle = null;
        }

        public override ModuleEffect Clone()
        {
            return new ForgeBurnColorEffect
            {
                rgb = this.rgb,
                color = this.color,
                colorLabel = this.colorLabel,
                rgbSpeed = this.rgbSpeed,
                saturation = this.saturation,
                brightness = this.brightness,
                includeTerrain = this.includeTerrain
            };
        }
    }
}
