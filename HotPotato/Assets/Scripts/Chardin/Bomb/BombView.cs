using UnityEngine;

namespace Chardin
{
    /// <summary>
    /// 炸弹外观：头/脸 Sprite 四态；Critical 时头在 4↔5 间闪；持有者可见精确数字。
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class BombView : MonoBehaviour
    {
        [Header("Renderers")]
        [SerializeField] SpriteRenderer headRenderer;
        [SerializeField] SpriteRenderer faceRenderer;
        [SerializeField] Vector3 faceLocalOffset = Vector3.zero;
        [SerializeField] int faceSortingOffset = 1;

        [Header("Head (1–5)")]
        [SerializeField] Sprite headSafe;       // 1
        [SerializeField] Sprite headWarning;    // 2
        [SerializeField] Sprite headDanger;     // 3
        [SerializeField] Sprite headCriticalA;  // 4
        [SerializeField] Sprite headCriticalB;  // 5 — Critical 闪烁另一帧

        [Header("Face / Eyes (face1–4)")]
        [SerializeField] Sprite faceSafe;       // face1
        [SerializeField] Sprite faceWarning;    // face2
        [SerializeField] Sprite faceDanger;     // face3
        [SerializeField] Sprite faceCritical;   // face4

        [Header("Countdown Label")]
        [SerializeField] TextMesh countdownLabel;
        [SerializeField] Vector3 labelLocalOffset = new Vector3(0f, 0.55f, 0f);

        [Header("Shake")]
        [SerializeField] float warningShake = 0.02f;
        [SerializeField] float dangerShake = 0.05f;
        [SerializeField] float criticalShake = 0.09f;
        [SerializeField] float criticalFlashSpeed = 8f;

        Vector3 _restLocalPos;
        bool _viewerIsHolder;
        int _countdown;
        BombAppearanceTier _tier = BombAppearanceTier.Safe;
        float _flash;
        bool _motionLocked;

        public int PopupSortingOrder
        {
            get
            {
                int order = headRenderer != null ? headRenderer.sortingOrder : 0;
                return order + faceSortingOffset + 5;
            }
        }

        public void SetMotionLocked(bool locked)
        {
            _motionLocked = locked;
            if (!locked)
                CaptureRestPosition();
        }

        void Awake()
        {
            EnsureRenderers();
            _restLocalPos = transform.localPosition;
            EnsureLabel();
            ApplyVisuals();
        }

        void EnsureRenderers()
        {
            if (headRenderer == null)
                headRenderer = GetComponent<SpriteRenderer>();

            if (headRenderer != null)
                headRenderer.color = Color.white;

            if (faceRenderer == null)
            {
                var faceTf = transform.Find("Face");
                if (faceTf != null)
                    faceRenderer = faceTf.GetComponent<SpriteRenderer>();
            }

            if (faceRenderer == null)
            {
                var go = new GameObject("Face");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = faceLocalOffset;
                faceRenderer = go.AddComponent<SpriteRenderer>();
            }
            else
            {
                faceRenderer.transform.localPosition = faceLocalOffset;
            }

            faceRenderer.color = Color.white;
            if (headRenderer != null)
                faceRenderer.sortingOrder = headRenderer.sortingOrder + faceSortingOffset;
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
                mr.sortingOrder = (headRenderer != null ? headRenderer.sortingOrder : 0) + faceSortingOffset + 1;
        }

        void Update()
        {
            if (_motionLocked)
                return;

            float amp = 0f;
            switch (_tier)
            {
                case BombAppearanceTier.Warning: amp = warningShake; break;
                case BombAppearanceTier.Danger: amp = dangerShake; break;
                case BombAppearanceTier.Critical: amp = criticalShake; break;
            }

            if (amp > 0f)
                transform.localPosition = _restLocalPos + (Vector3)(Random.insideUnitCircle * amp);
            else
                transform.localPosition = _restLocalPos;

            if (_tier == BombAppearanceTier.Critical && headRenderer != null)
            {
                _flash += Time.deltaTime * criticalFlashSpeed;
                bool useB = Mathf.Sin(_flash) > 0f;
                Sprite flashHead = useB && headCriticalB != null ? headCriticalB : headCriticalA;
                if (flashHead != null)
                    headRenderer.sprite = flashHead;
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
            EnsureRenderers();

            if (headRenderer != null)
            {
                headRenderer.color = Color.white;
                Sprite head = HeadForTier(_tier);
                if (head != null)
                    headRenderer.sprite = head;
            }

            if (faceRenderer != null)
            {
                faceRenderer.color = Color.white;
                Sprite face = FaceForTier(_tier);
                if (face != null)
                {
                    faceRenderer.enabled = true;
                    faceRenderer.sprite = face;
                }
            }

            if (countdownLabel != null)
            {
                countdownLabel.gameObject.SetActive(_viewerIsHolder);
                if (_viewerIsHolder)
                    countdownLabel.text = _countdown.ToString();
            }
        }

        Sprite HeadForTier(BombAppearanceTier tier)
        {
            switch (tier)
            {
                case BombAppearanceTier.Safe: return headSafe;
                case BombAppearanceTier.Warning: return headWarning;
                case BombAppearanceTier.Danger: return headDanger;
                default: return headCriticalA != null ? headCriticalA : headCriticalB;
            }
        }

        Sprite FaceForTier(BombAppearanceTier tier)
        {
            switch (tier)
            {
                case BombAppearanceTier.Safe: return faceSafe;
                case BombAppearanceTier.Warning: return faceWarning;
                case BombAppearanceTier.Danger: return faceDanger;
                default: return faceCritical;
            }
        }

#if UNITY_EDITOR
        /// <summary>编辑器：从 Art/美术素材/Bomb 自动填入 Sprite 引用。</summary>
        public void EditorAssignDefaultSprites()
        {
            const string folder = "Assets/Art/美术素材/Bomb";
            headSafe = LoadSprite(folder + "/1.png");
            headWarning = LoadSprite(folder + "/2.png");
            headDanger = LoadSprite(folder + "/3.png");
            headCriticalA = LoadSprite(folder + "/4.png");
            headCriticalB = LoadSprite(folder + "/5.png");
            faceSafe = LoadSprite(folder + "/face1.png");
            faceWarning = LoadSprite(folder + "/face2.png");
            faceDanger = LoadSprite(folder + "/face3.png");
            faceCritical = LoadSprite(folder + "/face4.png");
            EnsureRenderers();
            ApplyVisuals();
        }

        static Sprite LoadSprite(string path)
        {
            return UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
#endif
    }
}
