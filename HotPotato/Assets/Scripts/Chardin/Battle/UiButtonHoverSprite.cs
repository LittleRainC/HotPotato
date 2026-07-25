using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Chardin
{
    /// <summary>
    /// 鼠标悬停时切换按钮 Art 贴图，移开还原。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiButtonHoverSprite : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] Image target;
        [SerializeField] Sprite normalSprite;
        [SerializeField] Sprite hoverSprite;

        Button _button;
        UiHonorSpritePivot _pivot;
        bool _hovered;

        public void Configure(Sprite normal, Sprite hover)
        {
            normalSprite = normal;
            hoverSprite = hover;
            EnsureRefs();
            Apply(_hovered && IsInteractable());
        }

        void Awake()
        {
            EnsureRefs();
        }

        void OnDisable()
        {
            _hovered = false;
            Apply(false);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hovered = true;
            if (IsInteractable())
                Apply(true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hovered = false;
            Apply(false);
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

        bool IsInteractable()
        {
            return _button == null || _button.IsInteractable();
        }

        void Apply(bool useHover)
        {
            if (target == null)
                return;

            Sprite sprite = useHover && hoverSprite != null ? hoverSprite : normalSprite;
            if (sprite == null)
                return;

            target.sprite = sprite;

            // 根节点若还挂着同名引用，一并保持一致
            var rootImg = GetComponent<Image>();
            if (rootImg != null && rootImg != target && rootImg.sprite != null)
                rootImg.sprite = sprite;

            if (_pivot != null)
                _pivot.Align();
        }
    }
}
