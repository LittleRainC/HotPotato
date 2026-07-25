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
            dimImg.color = new Color(0f, 0f, 0f, 0f); // 透明遮罩，不挡画面
            dimImg.raycastTarget = false;

            // 右下区域；x=1350 y=0（相对画布左下，右下角 pivot）
            var panelGo = CreatePanel("Panel", transform,
                new Vector2(0f, 0f), new Vector2(0f, 0f));
            var panelRt = panelGo.GetComponent<RectTransform>();
            panelRt.pivot = new Vector2(1f, 0f);
            panelRt.anchoredPosition = new Vector2(1930f, 0f);

            panelImage = panelGo.GetComponent<Image>();
            panelImage.raycastTarget = true;
            panelImage.type = Image.Type.Simple;
            panelImage.preserveAspect = false;

            Sprite panelSprite = LoadPanelSprite();
            float targetW = 820f;
            float aspect = 1f;
            if (panelSprite != null)
            {
                panelImage.sprite = panelSprite;
                panelImage.color = Color.white;
                if (panelSprite.rect.height > 0.01f)
                    aspect = panelSprite.rect.width / panelSprite.rect.height;
            }
            else
            {
                panelImage.color = new Color(0.85f, 0.85f, 0.85f, 0.98f);
            }
            panelRt.sizeDelta = new Vector2(targetW, targetW / aspect);

            panelButton = panelGo.AddComponent<Button>();
            panelButton.transition = Selectable.Transition.None;
            panelButton.targetGraphic = panelImage;
            panelButton.onClick.AddListener(() => PanelClicked?.Invoke());

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            // 文字区：对齐 chatbox 不透明中部；加高以容纳倾斜 speaker
            var textAreaGo = CreatePanel("TextArea", panelGo.transform,
                new Vector2(0.20f, 0.34f), new Vector2(0.78f, 0.64f));
            var textAreaImg = textAreaGo.GetComponent<Image>();
            textAreaImg.color = Color.clear;
            textAreaImg.raycastTarget = false;
            textAreaGo.AddComponent<RectMask2D>();

            var ink = new Color(0.08f, 0.08f, 0.08f, 1f);
            speakerText = CreateText("Speaker", textAreaGo.transform, font, 26,
                new Vector2(0f, 0.54f), new Vector2(0.92f, 0.82f), FontStyle.Bold, ink, 20f);
            bodyText = CreateText("Body", textAreaGo.transform, font, 22,
                new Vector2(0f, -0.06f), new Vector2(0.92f, 0.50f), FontStyle.Normal, ink, 3f);

            transform.SetAsLastSibling();
            Hide();
        }

        static Sprite LoadPanelSprite()
        {
            var fromRes = Resources.Load<Sprite>("UI/chatbox");
            if (fromRes != null)
                return fromRes;
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/Art/美术素材/UI/chatbox.png");
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
            Vector2 anchorMin, Vector2 anchorMax, FontStyle style, Color color, float zRotation = 0f)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(0f, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localEulerAngles = new Vector3(0f, 0f, zRotation);
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
