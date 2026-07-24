using UnityEngine;
using UnityEngine.UI;

namespace Chardin
{
    /// <summary>
    /// UI Image 默认忽略 Sprite.pivot（整张图按 Rect 居中）。
    /// 把贴图放到子物体 Art，并平移，使 Sprite.pivot 对准本物体 RectTransform.pivot。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class UiHonorSpritePivot : MonoBehaviour
    {
        [SerializeField] Image rootHitArea;
        [SerializeField] Image artImage;
        [SerializeField] RectTransform art;
        [SerializeField] string artChildName = "Art";

        void OnEnable() => Align();

        void OnRectTransformDimensionsChange() => Align();

#if UNITY_EDITOR
        void OnValidate()
        {
            if (!isActiveAndEnabled)
                return;
            Align();
        }
#endif

        public void Align()
        {
            EnsureArt();
            if (artImage == null || artImage.sprite == null || art == null)
                return;

            var parent = (RectTransform)transform;
            Vector2 rectSize = parent.rect.size;
            if (rectSize.x < 0.01f || rectSize.y < 0.01f)
                return;

            Sprite sprite = artImage.sprite;
            Vector2 spriteSize = sprite.rect.size;
            Vector2 fitted = rectSize;
            if (artImage.preserveAspect)
            {
                float fit = Mathf.Min(rectSize.x / spriteSize.x, rectSize.y / spriteSize.y);
                fitted = spriteSize * fit;
            }

            Vector2 spritePivot = new Vector2(
                sprite.pivot.x / sprite.rect.width,
                sprite.pivot.y / sprite.rect.height);

            // 贴图中心默认在 rect 中心；平移后让 spritePivot 落到 parent.pivot
            Vector2 textureCenterToSpritePivot = Vector2.Scale(spritePivot - new Vector2(0.5f, 0.5f), fitted);
            Vector2 rectCenterToParentPivot = Vector2.Scale(parent.pivot - new Vector2(0.5f, 0.5f), rectSize);
            art.anchoredPosition = rectCenterToParentPivot - textureCenterToSpritePivot;
        }

        void EnsureArt()
        {
            if (rootHitArea == null)
                rootHitArea = GetComponent<Image>();

            if (art == null)
            {
                var existing = transform.Find(artChildName) as RectTransform;
                if (existing != null)
                    art = existing;
            }

            if (art == null && rootHitArea != null && rootHitArea.sprite != null
                && rootHitArea.color.a > 0.01f)
            {
                var go = new GameObject(artChildName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                art = go.GetComponent<RectTransform>();
                art.SetParent(transform, false);
                art.SetAsFirstSibling();
                art.anchorMin = Vector2.zero;
                art.anchorMax = Vector2.one;
                art.offsetMin = Vector2.zero;
                art.offsetMax = Vector2.zero;
                art.pivot = new Vector2(0.5f, 0.5f);
                art.localScale = Vector3.one;

                artImage = go.GetComponent<Image>();
                artImage.sprite = rootHitArea.sprite;
                artImage.preserveAspect = rootHitArea.preserveAspect;
                artImage.type = rootHitArea.type;
                artImage.color = Color.white;
                artImage.raycastTarget = false;

                // 根 Image：透明点击区
                rootHitArea.color = new Color(1f, 1f, 1f, 0f);
                rootHitArea.raycastTarget = true;

                var button = GetComponent<Button>();
                if (button != null)
                    button.targetGraphic = artImage;

                var badge = transform.Find("Badge");
                if (badge != null)
                    badge.SetAsLastSibling();
                var label = transform.Find("Label");
                if (label != null)
                    label.SetAsLastSibling();
            }

            if (artImage == null && art != null)
                artImage = art.GetComponent<Image>();
        }
    }
}
