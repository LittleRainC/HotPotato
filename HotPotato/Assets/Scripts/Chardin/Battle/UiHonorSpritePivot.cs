using UnityEngine;
using UnityEngine.UI;

namespace Chardin
{
    /// <summary>
    /// UI Image 默认忽略 Sprite.pivot。
    /// 把贴图放到子物体 Art：尺寸贴合可见 sprite，pivot 对齐父节点，并由 Art 接收点击。
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

            // Art 用贴合尺寸，不再拉满父节点；点击区 = 可见图
            art.anchorMin = new Vector2(0.5f, 0.5f);
            art.anchorMax = new Vector2(0.5f, 0.5f);
            art.pivot = new Vector2(0.5f, 0.5f);
            art.sizeDelta = fitted;
            // 父 pivot 在中心时：把 sprite pivot 对准父中心
            Vector2 fromCenterToSpritePivot = Vector2.Scale(spritePivot - new Vector2(0.5f, 0.5f), fitted);
            Vector2 parentPivotOffset = Vector2.Scale(parent.pivot - new Vector2(0.5f, 0.5f), rectSize);
            art.anchoredPosition = parentPivotOffset - fromCenterToSpritePivot;

            artImage.raycastTarget = true;
            if (rootHitArea != null)
            {
                rootHitArea.raycastTarget = false;
                rootHitArea.color = new Color(1f, 1f, 1f, 0f);
            }

            var button = GetComponent<Button>();
            if (button != null)
                button.targetGraphic = artImage;
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
                art.localScale = Vector3.one;

                artImage = go.GetComponent<Image>();
                artImage.sprite = rootHitArea.sprite;
                artImage.preserveAspect = rootHitArea.preserveAspect;
                artImage.type = rootHitArea.type;
                artImage.color = Color.white;

                rootHitArea.color = new Color(1f, 1f, 1f, 0f);

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
