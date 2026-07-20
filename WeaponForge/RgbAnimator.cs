using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace WeaponForge
{
    // Cycles the color of every renderer on this object (and its
    // children) through the hue wheel each frame - the "RGB / rainbow"
    // effect. Attached to a cloned projectile or beam-visual prefab, so
    // every spawned instance self-animates; no per-frame Harmony patch
    // is needed. Renderers are cached on Awake (which runs when the
    // instance spawns active, not on the inactive prefab template).
    public class RgbAnimator : MonoBehaviour
    {
        // Hue cycles per second (1 = a full rainbow every second).
        public float speed = 0.5f;
        public float saturation = 1f;
        public float brightness = 1f;

        private float _hue;

        private SpriteRenderer[] _sprites;
        private TrailRenderer[] _trails;
        private LineRenderer[] _lines;
        private ParticleSystem[] _particles;
        private Light2D[] _lights;

        private void Awake()
        {
            _sprites =
                GetComponentsInChildren<SpriteRenderer>(true);
            _trails =
                GetComponentsInChildren<TrailRenderer>(true);
            _lines =
                GetComponentsInChildren<LineRenderer>(true);
            _particles =
                GetComponentsInChildren<ParticleSystem>(true);
            _lights =
                GetComponentsInChildren<Light2D>(true);
        }

        private void Update()
        {
            _hue += Time.deltaTime * this.speed;

            if (_hue > 1f)
                _hue -= Mathf.Floor(_hue);

            Color c =
                Color.HSVToRGB(_hue, this.saturation, this.brightness);

            for (int i = 0; i < _sprites.Length; i++)
            {
                if (_sprites[i] == null)
                    continue;

                _sprites[i].color =
                    new Color(c.r, c.g, c.b, _sprites[i].color.a);
            }

            for (int i = 0; i < _lines.Length; i++)
            {
                if (_lines[i] == null)
                    continue;

                _lines[i].startColor = c;
                _lines[i].endColor = c;
            }

            for (int i = 0; i < _trails.Length; i++)
            {
                if (_trails[i] == null)
                    continue;

                _trails[i].startColor = c;
                _trails[i].endColor =
                    new Color(c.r, c.g, c.b, 0f);
            }

            for (int i = 0; i < _particles.Length; i++)
            {
                if (_particles[i] == null)
                    continue;

                var main = _particles[i].main;
                main.startColor = c;
            }

            for (int i = 0; i < _lights.Length; i++)
            {
                if (_lights[i] == null)
                    continue;

                _lights[i].color = c;
            }
        }
    }
}
