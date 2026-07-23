using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Chardin
{
    /// <summary>战斗 UI：动作按钮、播报、心形血量、决策倒计时。</summary>
    public sealed class BattleHud : MonoBehaviour
    {
        [SerializeField] Button btnPass;
        [SerializeField] Button btnShove;
        [SerializeField] Button btnDefuse;
        [SerializeField] Text defuseBadgeText;
        [SerializeField] Text broadcastText;
        [SerializeField] Text opponentNameText;

        [Header("Hearts")]
        [SerializeField] Transform heartsRoot;
        [SerializeField] Sprite heartFilled;
        [SerializeField] Sprite heartEmpty;
        [SerializeField] int maxHearts = 3;

        [Header("Decision Timer")]
        [SerializeField] GameObject decisionTimerRoot;
        [SerializeField] Image decisionTimerFill;
        [SerializeField] Text decisionTimerText;
        [SerializeField] Color timerNormal = new Color(0.35f, 0.75f, 0.45f, 1f);
        [SerializeField] Color timerUrgent = new Color(0.9f, 0.2f, 0.2f, 1f);

        readonly List<Image> _heartIcons = new List<Image>();
        static Sprite _whiteSprite;

        public event Action<BombAction> ActionClicked;

        public void BindFromHierarchy(Transform battleRoot)
        {
            var canvas = battleRoot.Find("Canvas");
            if (canvas == null)
            {
                Debug.LogError("[BattleHud] Canvas not found under Battle_2P");
                return;
            }

            broadcastText = FindText(canvas, "BroadcastBoard/Text");
            opponentNameText = FindText(canvas, "OpponentName/Text");

            var actions = canvas.Find("BottomBar/ActionButtons");
            if (actions != null)
            {
                btnDefuse = actions.Find("BtnDefuse")?.GetComponent<Button>();
                btnPass = actions.Find("BtnPass")?.GetComponent<Button>();
                btnShove = actions.Find("BtnShove")?.GetComponent<Button>();
                defuseBadgeText = FindText(actions, "BtnDefuse/Badge/Text");
            }

            EnsureHeartSprites();
            SetupHearts(canvas);
            SetupDecisionTimer(canvas);
            WireButtons();
            SetDecisionTimerVisible(false);
        }

        void EnsureHeartSprites()
        {
            if (heartFilled == null)
                heartFilled = Resources.Load<Sprite>("Whitebox/heart");
            if (heartEmpty == null)
                heartEmpty = Resources.Load<Sprite>("Whitebox/heart_empty");

            if (heartFilled == null)
                heartFilled = MakeSolidSprite(new Color(0.86f, 0.22f, 0.28f, 1f));
            if (heartEmpty == null)
                heartEmpty = MakeSolidSprite(new Color(0.3f, 0.3f, 0.34f, 0.9f));
        }

        static Sprite MakeSolidSprite(Color color)
        {
            var tex = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            var pixels = new Color[64];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = color;
            tex.SetPixels(pixels);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f), 8f);
        }

        void SetupHearts(Transform canvas)
        {
            heartsRoot = canvas.Find("BottomBar/Hearts");
            if (heartsRoot == null)
                return;

            var oldText = heartsRoot.Find("Text");
            if (oldText != null)
                oldText.gameObject.SetActive(false);

            for (int i = heartsRoot.childCount - 1; i >= 0; i--)
            {
                var child = heartsRoot.GetChild(i);
                if (child.name.StartsWith("HeartIcon"))
                    Destroy(child.gameObject);
            }
            _heartIcons.Clear();

            var layout = heartsRoot.GetComponent<HorizontalLayoutGroup>();
            if (layout == null)
                layout = heartsRoot.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 12f;
            layout.padding = new RectOffset(8, 8, 4, 4);
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            for (int i = 0; i < maxHearts; i++)
            {
                var go = new GameObject($"HeartIcon_{i}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.transform.SetParent(heartsRoot, false);
                var rt = go.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(56f, 56f);
                var img = go.GetComponent<Image>();
                img.sprite = heartFilled;
                img.preserveAspect = true;
                img.raycastTarget = false;
                _heartIcons.Add(img);
            }
        }

        void SetupDecisionTimer(Transform canvas)
        {
            var existing = canvas.Find("DecisionTimer");
            if (existing != null)
                Destroy(existing.gameObject);

            decisionTimerRoot = CreateUiPanel("DecisionTimer", canvas,
                new Vector2(0.25f, 0.84f), new Vector2(0.75f, 0.89f),
                new Color(0.08f, 0.08f, 0.1f, 0.85f));

            var fillGo = CreateUiPanel("Fill", decisionTimerRoot.transform,
                new Vector2(0.02f, 0.18f), new Vector2(0.98f, 0.82f),
                timerNormal);
            decisionTimerFill = fillGo.GetComponent<Image>();
            decisionTimerFill.type = Image.Type.Filled;
            decisionTimerFill.fillMethod = Image.FillMethod.Horizontal;
            decisionTimerFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            decisionTimerFill.fillAmount = 1f;

            var labelGo = new GameObject("TimerText", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            labelGo.transform.SetParent(decisionTimerRoot.transform, false);
            var lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;
            decisionTimerText = labelGo.GetComponent<Text>();
            decisionTimerText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                                     ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            decisionTimerText.fontSize = 28;
            decisionTimerText.fontStyle = FontStyle.Bold;
            decisionTimerText.alignment = TextAnchor.MiddleCenter;
            decisionTimerText.color = Color.white;
            decisionTimerText.raycastTarget = false;
            decisionTimerText.text = "10.0";
        }

        void WireButtons()
        {
            if (btnPass != null)
            {
                btnPass.onClick.RemoveAllListeners();
                btnPass.onClick.AddListener(() => ActionClicked?.Invoke(BombAction.Pass));
            }
            if (btnShove != null)
            {
                btnShove.onClick.RemoveAllListeners();
                btnShove.onClick.AddListener(() => ActionClicked?.Invoke(BombAction.Shove));
            }
            if (btnDefuse != null)
            {
                btnDefuse.onClick.RemoveAllListeners();
                btnDefuse.onClick.AddListener(() => ActionClicked?.Invoke(BombAction.Defuse));
            }
        }

        public void SetOpponentName(string name)
        {
            if (opponentNameText != null)
                opponentNameText.text = name;
        }

        /// <summary>
        /// 按屏幕从左到右，把每个敌人名字写到对应的 OpponentName 标签上（不再拼成一个字符串）。
        /// </summary>
        public void SetOpponentNames(IReadOnlyList<string> names)
        {
            var labels = CollectOpponentNameLabels();
            int count = names != null ? names.Count : 0;
            for (int i = 0; i < labels.Count; i++)
            {
                if (i < count && !string.IsNullOrEmpty(names[i]))
                {
                    labels[i].gameObject.SetActive(true);
                    labels[i].text = names[i];
                }
                else
                {
                    labels[i].text = string.Empty;
                    // 多出来的标签藏掉，避免残留旧名字
                    if (i >= count)
                        labels[i].gameObject.SetActive(false);
                }
            }

            // 兼容旧单字段引用
            if (count > 0 && opponentNameText != null && labels.Count == 0)
                opponentNameText.text = names[0];
        }

        List<Text> CollectOpponentNameLabels()
        {
            var result = new List<Text>();

            Transform canvas = null;
            if (opponentNameText != null)
            {
                canvas = opponentNameText.canvas != null
                    ? opponentNameText.canvas.transform
                    : opponentNameText.transform.root;
            }

            if (canvas == null)
                return result;

            var transforms = canvas.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                var t = transforms[i];
                if (!t.name.StartsWith("OpponentName"))
                    continue;
                var text = t.GetComponentInChildren<Text>(true);
                if (text != null && !result.Contains(text))
                    result.Add(text);
            }

            result.Sort((a, b) => a.rectTransform.position.x.CompareTo(b.rectTransform.position.x));
            return result;
        }

        public void SetBroadcast(string message)
        {
            if (broadcastText != null)
                broadcastText.text = message;
        }

        public void SetHearts(int hearts)
        {
            hearts = Mathf.Clamp(hearts, 0, Mathf.Max(maxHearts, _heartIcons.Count));
            for (int i = 0; i < _heartIcons.Count; i++)
            {
                bool filled = i < hearts;
                _heartIcons[i].sprite = filled ? heartFilled : heartEmpty;
                _heartIcons[i].color = Color.white;
            }
        }

        public void SetDefuseCharges(int charges)
        {
            if (defuseBadgeText != null)
                defuseBadgeText.text = "×" + charges;
        }

        public void SetActionsInteractable(bool interactable, bool defuseAvailable)
        {
            if (btnPass != null) btnPass.interactable = interactable;
            if (btnShove != null) btnShove.interactable = interactable;
            if (btnDefuse != null) btnDefuse.interactable = interactable && defuseAvailable;
        }

        public void SetDecisionTimerVisible(bool visible)
        {
            if (decisionTimerRoot != null)
                decisionTimerRoot.SetActive(visible);
        }

        public void SetDecisionTimer(float remaining, float total)
        {
            if (decisionTimerRoot == null)
                return;

            decisionTimerRoot.SetActive(true);
            remaining = Mathf.Max(0f, remaining);
            total = Mathf.Max(0.01f, total);
            float t = Mathf.Clamp01(remaining / total);
            bool urgent = remaining <= 3f;

            if (decisionTimerFill != null)
            {
                decisionTimerFill.fillAmount = t;
                decisionTimerFill.color = urgent ? timerUrgent : timerNormal;
            }

            if (decisionTimerText != null)
            {
                decisionTimerText.text = remaining.ToString("0.0");
                decisionTimerText.color = urgent ? timerUrgent : Color.white;
            }
        }

        static Text FindText(Transform root, string path)
        {
            var t = root.Find(path);
            return t != null ? t.GetComponent<Text>() : null;
        }

        static GameObject CreateUiPanel(string name, Transform parent, Vector2 amin, Vector2 amax, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = amin;
            rt.anchorMax = amax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.sprite = WhiteSprite();
            img.color = color;
            img.raycastTarget = false;
            return go;
        }

        static Sprite WhiteSprite()
        {
            if (_whiteSprite != null)
                return _whiteSprite;
            var tex = Texture2D.whiteTexture;
            _whiteSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            return _whiteSprite;
        }
    }
}
