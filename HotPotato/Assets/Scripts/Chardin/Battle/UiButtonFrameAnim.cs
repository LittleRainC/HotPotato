using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Chardin
{
    /// <summary>
    /// 技能按钮四帧：常态 04、悬停 01、按下播放 01→04。
    /// 必须在 PointerDown 播动画：Click 时 Button 可能已把 interactable 关掉。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiButtonFrameAnim : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler
    {
        [SerializeField] Image target;
        [SerializeField] Sprite[] frames = new Sprite[4]; // 0=01 .. 3=04
        [SerializeField] float clickFps = 6f;
        [SerializeField] AnimationClip clickClip;

        Button _button;
        UiHonorSpritePivot _pivot;
        bool _hovered;
        bool _playing;
        Coroutine _co;

        public void Configure(Sprite frame01, Sprite frame02, Sprite frame03, Sprite frame04,
            AnimationClip clip = null)
        {
            frames = new[] { frame01, frame02, frame03, frame04 };
            clickClip = clip;
            EnsureRefs();

            var old = GetComponent<UiButtonHoverSprite>();
            if (old != null)
                old.enabled = false;

            if (_button != null)
            {
                _button.onClick.RemoveListener(OnButtonClickedPlay);
                _button.onClick.AddListener(OnButtonClickedPlay);
            }

            ApplyIdleOrHover();
        }

        void Awake()
        {
            EnsureRefs();
        }

        void OnEnable()
        {
            EnsureRefs();
            // 再保险：按钮真正点下时也播（不依赖 EventSystem 接口顺序）
            if (_button != null)
            {
                _button.onClick.RemoveListener(OnButtonClickedPlay);
                _button.onClick.AddListener(OnButtonClickedPlay);
            }
        }

        void OnDisable()
        {
            if (_button != null)
                _button.onClick.RemoveListener(OnButtonClickedPlay);

            _hovered = false;
            StopPlay(resetSprite: true);
        }

        void OnButtonClickedPlay()
        {
            // onClick 时可能已不可交互，仍然播完点击动画
            TryPlayClick();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hovered = true;
            if (!_playing)
                ApplyIdleOrHover();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hovered = false;
            if (!_playing)
                ApplyIdleOrHover();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData != null && eventData.button != PointerEventData.InputButton.Left)
                return;
            // 按下立刻播，赶在 Button 把 interactable 关掉之前
            if (_button != null && !_button.IsInteractable())
                return;
            TryPlayClick();
        }

        void TryPlayClick()
        {
            if (frames == null || frames.Length < 4)
                return;
            if (frames[0] == null || frames[3] == null)
                return;
            if (_co != null)
                StopCoroutine(_co);
            _co = StartCoroutine(PlayClick());
        }

        IEnumerator PlayClick()
        {
            _playing = true;
            float frameTime = 1f / Mathf.Max(1f, clickFps);
            for (int i = 0; i < 4; i++)
            {
                SetSprite(frames[i]);
                yield return new WaitForSecondsRealtime(frameTime);
            }
            _playing = false;
            _co = null;
            ApplyIdleOrHover();
        }

        void StopPlay(bool resetSprite)
        {
            _playing = false;
            if (_co != null)
            {
                StopCoroutine(_co);
                _co = null;
            }
            if (resetSprite)
                ApplyIdleOrHover();
        }

        void EnsureRefs()
        {
            if (_button == null)
                _button = GetComponent<Button>();
            if (_pivot == null)
                _pivot = GetComponent<UiHonorSpritePivot>();
            if (target == null)
            {
                var art = transform.Find("Art");
                if (art != null)
                    target = art.GetComponent<Image>();
            }
            if (target == null)
                target = GetComponent<Image>();
        }

        void ApplyIdleOrHover()
        {
            if (frames == null || frames.Length < 4)
                return;
            bool showHover = _hovered && (_button == null || _button.IsInteractable());
            SetSprite(showHover ? frames[0] : frames[3]);
        }

        void SetSprite(Sprite sprite)
        {
            if (sprite == null || target == null)
                return;
            target.sprite = sprite;
            var rootImg = GetComponent<Image>();
            if (rootImg != null && rootImg != target)
                rootImg.sprite = sprite;
            if (_pivot != null)
                _pivot.Align();
        }
    }
}
