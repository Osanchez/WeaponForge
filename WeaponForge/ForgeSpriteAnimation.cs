using UnityEngine;

namespace WeaponForge
{
    // Flips a SpriteRenderer through a list of frames - the animated
    // projectile art the game itself never does (every stock ammo prefab
    // is one static sprite with no Animator).
    //
    // Added to the CLONED projectile prefab, so each spawned shot animates
    // itself: no Harmony patch and no per-frame game hook, the same trick
    // RgbAnimator uses for the rainbow.
    public class ForgeSpriteAnimation : MonoBehaviour
    {
        public enum LoopMode { Loop, Once, PingPong }

        public Sprite[] frames;
        public float fps = 12f;
        public LoopMode loop = LoopMode.Loop;

        // Start each shot on a random frame. Without this a 10-pellet
        // shotgun fires ten sprites flipping in lock-step, which reads as
        // one big strobe rather than ten spinning bullets.
        public bool randomStart;

        private SpriteRenderer _renderer;
        private float _startTime;
        private int _lastIndex = -1;

        private void Awake()
        {
            // Same resolution order as VisualCustomizer.SwapSprite: the
            // stock ammo prefabs carry their renderer on the root, with
            // children being particle systems.
            _renderer = GetComponent<SpriteRenderer>();

            if (_renderer == null)
                _renderer = GetComponentInChildren<SpriteRenderer>(true);

            _startTime = Time.time;

            // Awake runs per INSTANCE (the prefab clone sits under an
            // inactive holder), so this genuinely desyncs each shot.
            if (randomStart && frames != null && frames.Length > 1 && fps > 0f)
                _startTime -= Random.Range(0f, frames.Length / fps);

            Apply();
        }

        private void Update()
        {
            Apply();
        }

        private void Apply()
        {
            if (_renderer == null || frames == null || frames.Length == 0)
                return;

            int index;

            if (frames.Length == 1 || fps <= 0f)
            {
                index = 0;
            }
            else
            {
                float elapsed = (Time.time - _startTime) * fps;

                switch (loop)
                {
                    case LoopMode.Once:
                        index = Mathf.Min(
                            frames.Length - 1, Mathf.FloorToInt(elapsed));
                        break;

                    case LoopMode.PingPong:
                        // Runs 0..n-1..0 without repeating the end frames,
                        // so a 4-frame ping-pong is a 6-step cycle.
                        index = Mathf.RoundToInt(
                            Mathf.PingPong(elapsed, frames.Length - 1));
                        break;

                    default:
                        index = Mathf.FloorToInt(
                            Mathf.Repeat(elapsed, frames.Length));
                        break;
                }
            }

            index = Mathf.Clamp(index, 0, frames.Length - 1);

            if (index == _lastIndex)
                return;

            _lastIndex = index;

            if (frames[index] != null)
                _renderer.sprite = frames[index];
        }
    }
}
