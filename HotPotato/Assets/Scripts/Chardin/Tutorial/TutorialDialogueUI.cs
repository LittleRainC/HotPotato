using System;
using UnityEngine;
using UnityEngine.UI;

namespace Chardin
{
    /// <summary>Tutorial 弹窗：speaker + body；可点击推进（bubble 步）。</summary>
    public sealed class TutorialDialogueUI : MonoBehaviour
    {
        [SerializeField] GameObject root;
        [SerializeField] Image panelImage;
        [SerializeField] Button panelButton;
        [SerializeField] Text speakerText;
        [SerializeField] Text bodyText;
        [SerializeField] Text hintText;

        public event Action PanelClicked;

        public bool Visible => root != null && root.activeSelf;

        public static TutorialDialogueUI Create(Transform canvas)
        {
            var host = new GameObject("TutorialDialogue", typeof(RectTransform));
            host.transform.SetParent(canvas, false);
            var ui = host.AddComponent<TutorialDialogueUI>();
            ui.Build(canvas);
            return ui;
        }

        void Build(Transform canvas)
        {
            root = gameObject;
            var rt = (RectTransform)transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            // 独立排序，保证盖在结算/其它 UI 之上
            var overlayCanvas = gameObject.AddComponent<Canvas>();
            overlayCanvas.overrideSorting = true;
            overlayCanvas.sortingOrder = 500;
            gameObject.AddComponent<GraphicRaycaster>();

            var dimGo = CreatePanel("Dim", transform, Vector2.zero, Vector2.one);
            var dimImg = dimGo.GetComponent<Image>();
            dimImg.color = new Color(0f, 0f, 0f, 0.45f);
            dimImg.raycastTarget = false;

            // 偏右下、HP 上方，尺寸加大更容易看见
            var panelGo = CreatePanel("Panel", transform,
                new Vector2(0.52f, 0.24f), new Vector2(0.97f, 0.52f));
            panelImage = panelGo.GetComponent<Image>();
            panelImage.raycastTarget = true;
            panelImage.preserveAspect = false;
            panelImage.type = Image.Type.Sliced;

            Sprite panelSprite = LoadPanelSprite();
            if (panelSprite != null)
            {
                panelImage.sprite = panelSprite;
                panelImage.color = Color.white;
            }
            else
            {
                panelImage.color = new Color(0.95f, 0.92f, 0.85f, 0.98f);
            }

            panelButton = panelGo.AddComponent<Button>();
            panelButton.transition = Selectable.Transition.None;
            panelButton.targetGraphic = panelImage;
            panelButton.onClick.AddListener(() => PanelClicked?.Invoke());

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            // 浅色面板用深色字
            var ink = new Color(0.12f, 0.1f, 0.08f, 1f);
            speakerText = CreateText("Speaker", panelGo.transform, font, 30,
                new Vector2(0.06f, 0.74f), new Vector2(0.94f, 0.94f), FontStyle.Bold, ink);
            bodyText = CreateText("Body", panelGo.transform, font, 26,
                new Vector2(0.06f, 0.22f), new Vector2(0.94f, 0.72f), FontStyle.Normal, ink);
            hintText = CreateText("Hint", panelGo.transform, font, 18,
                new Vector2(0.06f, 0.04f), new Vector2(0.94f, 0.20f), FontStyle.Italic,
                new Color(0.25f, 0.22f, 0.18f, 0.85f));
            hintText.alignment = TextAnchor.LowerRight;
            hintText.text = "Click to continue";

            transform.SetAsLastSibling();
            Hide();
        }

        static Sprite LoadPanelSprite()
        {
            var fromRes = Resources.Load<Sprite>("UI/ui_panel_generic_01");
            if (fromRes != null)
                return fromRes;
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/Art/美术素材/UI/ui_panel_generic_01.png");
#else
            return null;
#endif
        }

        static GameObject CreatePanel(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return go;
        }

        static Text CreateText(string name, Transform parent, Font font, int size,
            Vector2 anchorMin, Vector2 anchorMax, FontStyle style, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        public void Show(string speaker, string body, bool clickable)
        {
            if (root != null)
                root.SetActive(true);
            if (speakerText != null)
                speakerText.text = string.IsNullOrEmpty(speaker) ? "" : speaker;
            if (bodyText != null)
                bodyText.text = body ?? "";
            if (hintText != null)
                hintText.text = clickable ? "Click to continue" : "Follow the highlighted action";
            SetClickable(clickable);
            transform.SetAsLastSibling();
        }

        public void SetClickable(bool clickable)
        {
            if (panelButton != null)
                panelButton.interactable = clickable;
            if (panelImage != null)
                panelImage.raycastTarget = true; // 始终可接收，非 bubble 时由 controller 忽略
            if (panelButton != null && !clickable)
                panelButton.interactable = false;
        }

        public void Hide()
        {
            if (root != null)
                root.SetActive(false);
        }
    }
}
