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

        // false (default) = overwrite every color with the hue, which is
        // what a projectile sprite wants. true = MULTIPLY the artist's
        // own colors by the hue, so a particle burst keeps its gradient,
        // its alpha fade and its random spread. Muzzle flashes need this
        // - flattening their startColor gradient turns a flicker into a
        // solid blob.
        public bool multiply;

        private float _hue;

        private SpriteRenderer[] _sprites;
        private TrailRenderer[] _trails;
        private LineRenderer[] _lines;
        private ParticleSystem[] _particles;
        private Light2D[] _lights;

        // Multiply-mode baselines, captured once so tinting never
        // compounds frame over frame.
        private Color[] _spriteBase;
        private Color[] _trailStartBase;
        private Color[] _trailEndBase;
        private Color[] _lineStartBase;
        private Color[] _lineEndBase;
        private Color[] _lightBase;
        private ParticleBase[] _particleBase;

        // One particle system's original startColor.
        private class ParticleBase
        {
            public ParticleSystemGradientMode mode;
            public Color color;
            public Color colorMin;
            public Color colorMax;
            public Track a;   // the single / min gradient
            public Track b;   // the max gradient (TwoGradients only)
        }

        // A gradient's key data, captured once, plus same-sized scratch
        // arrays and one scratch Gradient we overwrite in place. Reusing
        // the Gradient alone would NOT be allocation-free: Unity's
        // Gradient.colorKeys / .alphaKeys are array-returning properties,
        // so reading them each frame allocates. Caching the key arrays is
        // what actually keeps the per-frame tint off the heap.
        private class Track
        {
            public GradientMode mode;
            public GradientColorKey[] baseColors;
            public GradientAlphaKey[] baseAlphas;
            public GradientColorKey[] outColors;
            public GradientAlphaKey[] outAlphas;
            public Gradient outGradient;

            public static Track From(Gradient g)
            {
                if (g == null)
                    return null;

                var t = new Track();
                t.mode = g.mode;
                t.baseColors = g.colorKeys;
                t.baseAlphas = g.alphaKeys;
                t.outColors =
                    new GradientColorKey[t.baseColors.Length];
                t.outAlphas =
                    new GradientAlphaKey[t.baseAlphas.Length];
                t.outGradient = new Gradient();
                return t;
            }

            public Gradient Tinted(Color c)
            {
                for (int i = 0; i < baseColors.Length; i++)
                {
                    outColors[i].time = baseColors[i].time;
                    outColors[i].color =
                        VisualCustomizer.Multiply(baseColors[i].color, c);
                }

                for (int i = 0; i < baseAlphas.Length; i++)
                {
                    outAlphas[i].time = baseAlphas[i].time;
                    outAlphas[i].alpha = baseAlphas[i].alpha * c.a;
                }

                outGradient.mode = mode;
                outGradient.colorKeys = outColors;
                outGradient.alphaKeys = outAlphas;
                return outGradient;
            }
        }

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

            Prune();

            if (this.multiply)
                CaptureBaselines();
        }

        // Drop anything that claimed its own colour (a trail with an explicit
        // trail.color, say) so the hue cycle does not overwrite it every
        // frame. Entries are NULLED rather than the arrays rebuilt because
        // every loop below - and CaptureBaselines - already skips nulls, so
        // this stays a one-line change per array with no index bookkeeping.
        private void Prune()
        {
            for (int i = 0; i < _sprites.Length; i++)
                if (VisualCustomizer.ColorLocked(_sprites[i], gameObject))
                    _sprites[i] = null;

            for (int i = 0; i < _trails.Length; i++)
                if (VisualCustomizer.ColorLocked(_trails[i], gameObject))
                    _trails[i] = null;

            for (int i = 0; i < _lines.Length; i++)
                if (VisualCustomizer.ColorLocked(_lines[i], gameObject))
                    _lines[i] = null;

            for (int i = 0; i < _particles.Length; i++)
                if (VisualCustomizer.ColorLocked(_particles[i], gameObject))
                    _particles[i] = null;

            for (int i = 0; i < _lights.Length; i++)
                if (VisualCustomizer.ColorLocked(_lights[i], gameObject))
                    _lights[i] = null;
        }

        private void CaptureBaselines()
        {
            _spriteBase = new Color[_sprites.Length];
            for (int i = 0; i < _sprites.Length; i++)
                if (_sprites[i] != null)
                    _spriteBase[i] = _sprites[i].color;

            _trailStartBase = new Color[_trails.Length];
            _trailEndBase = new Color[_trails.Length];
            for (int i = 0; i < _trails.Length; i++)
            {
                if (_trails[i] == null)
                    continue;
                _trailStartBase[i] = _trails[i].startColor;
                _trailEndBase[i] = _trails[i].endColor;
            }

            _lineStartBase = new Color[_lines.Length];
            _lineEndBase = new Color[_lines.Length];
            for (int i = 0; i < _lines.Length; i++)
            {
                if (_lines[i] == null)
                    continue;
                _lineStartBase[i] = _lines[i].startColor;
                _lineEndBase[i] = _lines[i].endColor;
            }

            _lightBase = new Color[_lights.Length];
            for (int i = 0; i < _lights.Length; i++)
                if (_lights[i] != null)
                    _lightBase[i] = _lights[i].color;

            _particleBase = new ParticleBase[_particles.Length];
            for (int i = 0; i < _particles.Length; i++)
            {
                if (_particles[i] == null)
                    continue;

                ParticleSystem.MinMaxGradient g =
                    _particles[i].main.startColor;

                var b = new ParticleBase();
                b.mode = g.mode;
                b.color = g.color;
                b.colorMin = g.colorMin;
                b.colorMax = g.colorMax;

                // Snapshot the key data now - the Gradient objects the
                // module handed us are live and we are about to replace
                // them.
                if (g.mode == ParticleSystemGradientMode.TwoGradients)
                {
                    b.a = Track.From(g.gradientMin);
                    b.b = Track.From(g.gradientMax);
                }
                else if (g.mode == ParticleSystemGradientMode.Gradient ||
                         g.mode == ParticleSystemGradientMode.RandomColor)
                {
                    b.a = Track.From(g.gradientMax);
                }

                _particleBase[i] = b;
            }
        }

        private void Update()
        {
            _hue += Time.deltaTime * this.speed;

            // Repeat, not a one-sided wrap: a NEGATIVE speed (a perfectly
            // reasonable way to ask for a reverse cycle) would otherwise
            // walk the hue below zero, where HSVToRGB falls off the end of
            // its switch and returns black - a permanently invisible
            // effect with nothing in the log.
            _hue = Mathf.Repeat(_hue, 1f);

            Color c =
                Color.HSVToRGB(_hue, this.saturation, this.brightness);

            if (this.multiply)
            {
                // Baselines are normally captured in Awake, but a caller
                // that flips 'multiply' after the component is already
                // awake would miss that - so capture on demand too.
                if (_particleBase == null)
                    CaptureBaselines();

                UpdateMultiplied(c);
                return;
            }

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

        // Every write is baseline * hue, never current * hue, so the
        // colors cycle instead of decaying toward black.
        private void UpdateMultiplied(Color c)
        {
            for (int i = 0; i < _sprites.Length; i++)
                if (_sprites[i] != null)
                    _sprites[i].color =
                        VisualCustomizer.Multiply(_spriteBase[i], c);

            for (int i = 0; i < _lines.Length; i++)
            {
                if (_lines[i] == null)
                    continue;
                _lines[i].startColor =
                    VisualCustomizer.Multiply(_lineStartBase[i], c);
                _lines[i].endColor =
                    VisualCustomizer.Multiply(_lineEndBase[i], c);
            }

            for (int i = 0; i < _trails.Length; i++)
            {
                if (_trails[i] == null)
                    continue;
                _trails[i].startColor =
                    VisualCustomizer.Multiply(_trailStartBase[i], c);
                _trails[i].endColor =
                    VisualCustomizer.Multiply(_trailEndBase[i], c);
            }

            for (int i = 0; i < _lights.Length; i++)
                if (_lights[i] != null)
                    _lights[i].color =
                        VisualCustomizer.Multiply(_lightBase[i], c);

            for (int i = 0; i < _particles.Length; i++)
            {
                if (_particles[i] == null || _particleBase[i] == null)
                    continue;

                ParticleBase b = _particleBase[i];
                var main = _particles[i].main;

                switch (b.mode)
                {
                    case ParticleSystemGradientMode.TwoColors:
                        main.startColor =
                            new ParticleSystem.MinMaxGradient(
                                VisualCustomizer.Multiply(b.colorMin, c),
                                VisualCustomizer.Multiply(b.colorMax, c));
                        break;

                    case ParticleSystemGradientMode.Gradient:
                        main.startColor =
                            new ParticleSystem.MinMaxGradient(
                                b.a.Tinted(c));
                        break;

                    case ParticleSystemGradientMode.TwoGradients:
                        main.startColor =
                            new ParticleSystem.MinMaxGradient(
                                b.a.Tinted(c), b.b.Tinted(c));
                        break;

                    case ParticleSystemGradientMode.RandomColor:
                        var random =
                            new ParticleSystem.MinMaxGradient(
                                b.a.Tinted(c));
                        random.mode =
                            ParticleSystemGradientMode.RandomColor;
                        main.startColor = random;
                        break;

                    default:
                        main.startColor =
                            new ParticleSystem.MinMaxGradient(
                                VisualCustomizer.Multiply(b.color, c));
                        break;
                }
            }
        }
    }
}
