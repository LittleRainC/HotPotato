using UnityEngine;

namespace Chardin
{
    /// <summary>炸弹外观：比例颜色 / 抖动 / 持有者可见精确数字。</summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class BombView : MonoBehaviour
    {
        [SerializeField] SpriteRenderer spriteRenderer;
        [SerializeField] TextMesh countdownLabel;
        [SerializeField] Vector3 labelLocalOffset = new Vector3(0f, 0.55f, 0f);

        [Header("Tier Colors")]
        [SerializeField] Color safeColor = new Color(0.92f, 0.78f, 0.2f, 1f);
        [SerializeField] Color warningColor = new Color(1f, 0.55f, 0.15f, 1f);
        [SerializeField] Color dangerColor = new Color(0.95f, 0.2f, 0.15f, 1f);
        [SerializeField] Color criticalColor = new Color(0.65f, 0.05f, 0.1f, 1f);

        [Header("Shake")]
        [SerializeField] float warningShake = 0.02f;
        [SerializeField] float dangerShake = 0.05f;
        [SerializeField] float criticalShake = 0.09f;

        Vector3 _restLocalPos;
        bool _viewerIsHolder;
        int _countdown;
        BombAppearanceTier _tier = BombAppearanceTier.Safe;
        float _flash;

        void Awake()
        {
            if (spriteRenderer == null)
                spriteRenderer = GetComponent<SpriteRenderer>();

            _restLocalPos = transform.localPosition;
            EnsureLabel();
            ApplyVisuals();
        }

        void EnsureLabel()
        {
            if (countdownLabel != null)
                return;

            var go = new GameObject("CountdownLabel");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = labelLocalOffset;
            countdownLabel = go.AddComponent<TextMesh>();
            countdownLabel.anchor = TextAnchor.MiddleCenter;
            countdownLabel.alignment = TextAlignment.Center;
            countdownLabel.characterSize = 0.12f;
            countdownLabel.fontSize = 64;
            countdownLabel.color = Color.white;
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
                mr.sortingOrder = spriteRenderer != null ? spriteRenderer.sortingOrder + 1 : 10;
        }

        void Update()
        {
            float amp = 0f;
            switch (_tier)
            {
                case BombAppearanceTier.Warning: amp = warningShake; break;
                case BombAppearanceTier.Danger: amp = dangerShake; break;
                case BombAppearanceTier.Critical: amp = criticalShake; break;
            }

            if (amp > 0f)
            {
                transform.localPosition = _restLocalPos + (Vector3)(Random.insideUnitCircle * amp);
            }
            else
            {
                transform.localPosition = _restLocalPos;
            }

            if (_tier == BombAppearanceTier.Critical && spriteRenderer != null)
            {
                _flash += Time.deltaTime * 8f;
                float t = (Mathf.Sin(_flash) + 1f) * 0.5f;
                spriteRenderer.color = Color.Lerp(criticalColor, Color.white, t * 0.35f);
            }
        }

        /// <summary>战斗层在换位后调用，刷新静止位置基准。</summary>
        public void CaptureRestPosition()
        {
            _restLocalPos = transform.localPosition;
        }

        public void SetViewerIsHolder(bool isHolder)
        {
            _viewerIsHolder = isHolder;
            ApplyVisuals();
        }

        public void Refresh(BombLogic logic, bool viewerIsHolder)
        {
            if (logic == null || !logic.IsArmed)
                return;

            _viewerIsHolder = viewerIsHolder;
            _countdown = logic.Countdown;
            _tier = logic.GetAppearanceTier();
            ApplyVisuals();
        }

        void ApplyVisuals()
        {
            if (spriteRenderer != null && _tier != BombAppearanceTier.Critical)
                spriteRenderer.color = ColorForTier(_tier);

            if (countdownLabel != null)
            {
                countdownLabel.gameObject.SetActive(_viewerIsHolder);
                if (_viewerIsHolder)
                    countdownLabel.text = _countdown.ToString();
            }
        }

        Color ColorForTier(BombAppearanceTier tier)
        {
            switch (tier)
            {
                case BombAppearanceTier.Safe: return safeColor;
                case BombAppearanceTier.Warning: return warningColor;
                case BombAppearanceTier.Danger: return dangerColor;
                default: return criticalColor;
            }
        }
    }
}
