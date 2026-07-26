using UnityEngine;

namespace Chardin
{
    /// <summary>
    /// Realtime breathing flash used by Tutorial action prompts.
    /// Kept separate from UiButtonFrameAnim so hover/click sprites still work.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TutorialButtonPulse : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] float cyclesPerSecond = 1.8f;
        [SerializeField, Range(0.05f, 1f)] float minimumAlpha = 0.35f;

        CanvasGroup _canvasGroup;
        bool _pulsing;
        float _startedAt;

        void Awake()
        {
            EnsureCanvasGroup();
        }

        void Update()
        {
            if (!_pulsing || _canvasGroup == null)
                return;

            float elapsed = Time.unscaledTime - _startedAt;
            float wave = 0.5f + 0.5f * Mathf.Sin(
                elapsed * cyclesPerSecond * Mathf.PI * 2f);
            _canvasGroup.alpha = Mathf.Lerp(minimumAlpha, 1f, wave);
        }

        public void SetPulsing(bool pulsing)
        {
            EnsureCanvasGroup();
            if (_pulsing == pulsing)
                return;

            _pulsing = pulsing;
            _startedAt = Time.unscaledTime;
            if (!_pulsing && _canvasGroup != null)
                _canvasGroup.alpha = 1f;
        }

        void OnDisable()
        {
            _pulsing = false;
            if (_canvasGroup != null)
                _canvasGroup.alpha = 1f;
        }

        void EnsureCanvasGroup()
        {
            if (_canvasGroup == null)
                _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
    }
}
