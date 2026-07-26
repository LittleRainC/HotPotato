using System;
using System.Collections;
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
        [SerializeField] RectTransform decisionTimerFillRect;
        [SerializeField] Text decisionTimerText;
        [SerializeField] Color timerNormal = Color.white;
        [SerializeField] Color timerWarn = new Color(1f, 0.28f, 0.22f, 1f);
        const float TimerFlashRemainingSeconds = 5f; // 剩余≤5秒开始红闪（写死，避免场景旧序列化成3）
        [SerializeField] float timerFlashCycle = 0.28f; // 一整轮：红 + 白
        [SerializeField, Range(0.5f, 0.95f)] float timerFlashRedDuty = 0.72f; // 红占比（白更短）
        [SerializeField] float timerFillHalfSpan = 0.43f; // t=1 时左右各占比例（相对父宽）
        [SerializeField] float timerFillYMin = 0.22f;
        [SerializeField] float timerFillYMax = 0.78f;

        [Header("UI Art (美术素材/Button)")]
        [SerializeField] Sprite timerBarBg;
        [SerializeField] Sprite timerBarFill;
        [SerializeField] Sprite defuseBadgeSprite;

        [Header("Damage Flash")]
        [SerializeField] Image fullscreenDamageFlash;
        [SerializeField] float fullscreenFlashSeconds = 0.65f; // 与 Enemy.deathFlashSeconds 一致
        [SerializeField] float fullscreenFlashHz = 14f;

        readonly List<Image> _heartIcons = new List<Image>();
        static Sprite _whiteSprite;
        Transform _canvas;

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

            _canvas = canvas;
            EnsureUiArtSprites();
            EnsureHeartSprites();
            SetupHearts(canvas);
            SetupDecisionTimer(canvas);
            ApplyDefuseBadgeArt();
            EnsureFullscreenDamageFlash(canvas);
            WireButtons();
            EnsureActionButtonHovers();
            EnsureActionButtonsOnTop();
            SetDecisionTimerVisible(false);
        }

        void EnsureUiArtSprites()
        {
            if (timerBarBg == null)
                timerBarBg = LoadUiSprite("ui_timerbar_bg_01", "Assets/Art/美术素材/Button/ui_timerbar_bg_01.png");
            if (timerBarFill == null)
                timerBarFill = LoadUiSprite("ui_timerbar_fill_01", "Assets/Art/美术素材/Button/ui_timerbar_fill_01.png");
            if (defuseBadgeSprite == null)
                defuseBadgeSprite = LoadUiSprite("ui_badge_count_01", "Assets/Art/美术素材/Button/ui_badge_count_01.png");
        }

        static Sprite LoadUiSprite(string resourcesName, string editorAssetPath)
        {
            var fromResources = Resources.Load<Sprite>("UI/" + resourcesName);
            if (fromResources != null)
                return fromResources;

#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(editorAssetPath);
#else
            return null;
#endif
        }

        /// <summary>与 Level5 拆按钮 Badge 一致：圆形素材 + scale/位置。</summary>
        void ApplyDefuseBadgeArt()
        {
            if (btnDefuse == null)
                return;

            var badge = btnDefuse.transform.Find("Badge") as RectTransform;
            if (badge == null)
                return;

            badge.anchorMin = new Vector2(0.5f, 0.5f);
            badge.anchorMax = new Vector2(0.5f, 0.5f);
            badge.pivot = new Vector2(0.5f, 0.5f);
            badge.anchoredPosition = new Vector2(28f, 28f);
            badge.sizeDelta = new Vector2(70f, 70f);
            badge.localScale = new Vector3(0.3f, 0.3f, 0.3f);

            var img = badge.GetComponent<Image>();
            if (img != null)
            {
                if (defuseBadgeSprite != null)
                    img.sprite = defuseBadgeSprite;
                img.color = Color.white;
                img.type = Image.Type.Simple;
                img.preserveAspect = true;
            }

            if (defuseBadgeText == null)
                defuseBadgeText = badge.GetComponentInChildren<Text>(true);
            if (defuseBadgeText != null)
            {
                defuseBadgeText.fontSize = 28;
                defuseBadgeText.alignment = TextAnchor.MiddleCenter;
                defuseBadgeText.color = Color.white;
            }
        }

        void EnsureFullscreenDamageFlash(Transform canvas)
        {
            if (fullscreenDamageFlash != null)
                return;

            var existing = canvas.Find("DamageFlash");
            if (existing != null)
            {
                fullscreenDamageFlash = existing.GetComponent<Image>();
                if (fullscreenDamageFlash != null)
                {
                    fullscreenDamageFlash.gameObject.SetActive(false);
                    return;
                }
            }

            var go = CreateUiPanel("DamageFlash", canvas,
                Vector2.zero, Vector2.one, new Color(1f, 0f, 0f, 0.55f));
            go.transform.SetAsLastSibling();
            fullscreenDamageFlash = go.GetComponent<Image>();
            fullscreenDamageFlash.raycastTarget = false;
            go.SetActive(false);
        }

        /// <summary>玩家挨炸：全屏红白闪烁。</summary>
        public IEnumerator PlayFullscreenDamageFlash()
        {
            if (_canvas != null)
                EnsureFullscreenDamageFlash(_canvas);
            if (fullscreenDamageFlash == null)
                yield break;

            fullscreenDamageFlash.gameObject.SetActive(true);
            fullscreenDamageFlash.transform.SetAsLastSibling();

            float t = 0f;
            while (t < fullscreenFlashSeconds)
            {
                t += Time.deltaTime;
                bool useRed = Mathf.FloorToInt(t * fullscreenFlashHz) % 2 == 0;
                fullscreenDamageFlash.color = useRed
                    ? new Color(1f, 0.05f, 0.05f, 0.62f)
                    : new Color(1f, 1f, 1f, 0.48f);
                yield return null;
            }

            fullscreenDamageFlash.gameObject.SetActive(false);
        }

        void EnsureHeartSprites()
        {
            heartFilled = LoadUiSprite("hp1", "Assets/Art/美术素材/UI/hp1.png");
            heartEmpty = LoadUiSprite("hp0", "Assets/Art/美术素材/UI/hp0.png");

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
            layout.spacing = -12f;
            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            for (int i = 0; i < maxHearts; i++)
            {
                var go = new GameObject($"HeartIcon_{i}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.transform.SetParent(heartsRoot, false);
                var rt = go.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(150f, 150f);
                var img = go.GetComponent<Image>();
                img.sprite = heartFilled;
                img.preserveAspect = true;
                img.raycastTarget = false;
                _heartIcons.Add(img);
            }
        }

        void SetupDecisionTimer(Transform canvas)
        {
            EnsureUiArtSprites();

            var existing = canvas.Find("DecisionTimer");
            if (existing != null)
                Destroy(existing.gameObject);

            // 背景：略变窄、略变高（相对原先 0.25–0.75 / 0.84–0.89）
            decisionTimerRoot = CreateUiPanel("DecisionTimer", canvas,
                new Vector2(0.32f, 0.825f), new Vector2(0.68f, 0.905f),
                Color.white);
            var bg = decisionTimerRoot.GetComponent<Image>();
            if (timerBarBg != null)
                bg.sprite = timerBarBg;
            bg.type = Image.Type.Simple;
            bg.preserveAspect = false;
            bg.color = Color.white;

            // 填充：居中，随剩余时间向中间缩短（不用左对齐 Filled）
            var fillGo = CreateUiPanel("Fill", decisionTimerRoot.transform,
                new Vector2(0.5f - timerFillHalfSpan, timerFillYMin),
                new Vector2(0.5f + timerFillHalfSpan, timerFillYMax),
                Color.white);
            decisionTimerFillRect = fillGo.GetComponent<RectTransform>();
            decisionTimerFill = fillGo.GetComponent<Image>();
            if (timerBarFill != null)
                decisionTimerFill.sprite = timerBarFill;
            decisionTimerFill.type = Image.Type.Simple;
            decisionTimerFill.color = timerNormal;
            decisionTimerFill.preserveAspect = false;

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

        void EnsureActionButtonHovers()
        {
            SetupButtonFrames(btnDefuse, "defuse");
            SetupButtonFrames(btnPass, "pass");
            SetupButtonFrames(btnShove, "shove");
        }

        /// <summary>ActionButtons 高于 TutorialDialogue（sorting 500），避免对话挡住技能键。</summary>
        void EnsureActionButtonsOnTop()
        {
            Transform actions = null;
            if (btnDefuse != null)
                actions = btnDefuse.transform.parent;
            else if (btnPass != null)
                actions = btnPass.transform.parent;
            else if (btnShove != null)
                actions = btnShove.transform.parent;
            if (actions == null)
                return;

            var canvas = actions.GetComponent<Canvas>();
            if (canvas == null)
                canvas = actions.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 600;

            if (actions.GetComponent<GraphicRaycaster>() == null)
                actions.gameObject.AddComponent<GraphicRaycaster>();
        }

        void SetupButtonFrames(Button button, string prefix)
        {
            if (button == null)
                return;

            var anim = button.GetComponent<UiButtonFrameAnim>();
            if (anim == null)
                anim = button.gameObject.AddComponent<UiButtonFrameAnim>();

            Sprite f1 = LoadButtonFrame(prefix, 1);
            Sprite f2 = LoadButtonFrame(prefix, 2);
            Sprite f3 = LoadButtonFrame(prefix, 3);
            Sprite f4 = LoadButtonFrame(prefix, 4);
            var clip = Resources.Load<AnimationClip>("UI/Buttons/btn_" + prefix + "_click");
#if UNITY_EDITOR
            if (clip == null)
                clip = UnityEditor.AssetDatabase.LoadAssetAtPath<AnimationClip>(
                    "Assets/Art/Animations/Buttons/btn_" + prefix + "_click.anim");
#endif
            anim.Configure(f1, f2, f3, f4, clip);

            // 默认显示 04，并同步 Art / 根节点
            if (f4 != null)
            {
                var art = button.transform.Find("Art");
                var artImg = art != null ? art.GetComponent<Image>() : null;
                if (artImg != null) artImg.sprite = f4;
                var rootImg = button.GetComponent<Image>();
                if (rootImg != null) rootImg.sprite = f4;
                var pivot = button.GetComponent<UiHonorSpritePivot>();
                if (pivot != null) pivot.Align();
            }
        }

        static Sprite LoadButtonFrame(string prefix, int frame)
        {
            string resName = "btn_" + prefix + "_0" + frame;
            var fromRes = Resources.Load<Sprite>("UI/Buttons/" + resName);
            if (fromRes != null)
                return fromRes;

            // fallback：原中文文件名
            string cn;
            if (prefix == "defuse") cn = "捏0" + frame;
            else if (prefix == "pass") cn = "传0" + frame;
            else cn = frame == 4 ? "塞04 gif" : "塞0" + frame;

            return LoadUiSprite(resName, "Assets/Art/美术素材/Button/" + cn + ".png");
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

        bool _tutorialGateActive;
        BombAction? _tutorialOnlyAction;
        bool _tutorialDefuseAvailable;
        TutorialButtonPulse _passTutorialPulse;
        TutorialButtonPulse _shoveTutorialPulse;
        TutorialButtonPulse _defuseTutorialPulse;

        public void SetActionsInteractable(bool interactable, bool defuseAvailable)
        {
            if (_tutorialGateActive)
            {
                ApplyTutorialGate();
                return;
            }

            if (btnPass != null) btnPass.interactable = interactable;
            if (btnShove != null) btnShove.interactable = interactable;
            if (btnDefuse != null) btnDefuse.interactable = interactable && defuseAvailable;
        }

        /// <summary>
        /// Tutorial 门控：onlyAction=null 表示全部锁定；否则只开对应技能。
        /// </summary>
        public void SetTutorialGate(BombAction? onlyAction, bool defuseAvailable)
        {
            EnsureTutorialButtonPulses();
            _tutorialGateActive = true;
            _tutorialOnlyAction = onlyAction;
            _tutorialDefuseAvailable = defuseAvailable;
            ApplyTutorialGate();
        }

        public void ClearTutorialGate()
        {
            _tutorialGateActive = false;
            _tutorialOnlyAction = null;
            SetTutorialPulse(null);
        }

        void ApplyTutorialGate()
        {
            bool pass = _tutorialOnlyAction == BombAction.Pass;
            bool shove = _tutorialOnlyAction == BombAction.Shove;
            bool defuse = _tutorialOnlyAction == BombAction.Defuse && _tutorialDefuseAvailable;
            if (btnPass != null) btnPass.interactable = pass;
            if (btnShove != null) btnShove.interactable = shove;
            if (btnDefuse != null) btnDefuse.interactable = defuse;
            SetTutorialPulse(pass ? BombAction.Pass
                : shove ? BombAction.Shove
                : defuse ? BombAction.Defuse
                : (BombAction?)null);
        }

        void EnsureTutorialButtonPulses()
        {
            _passTutorialPulse = EnsureTutorialPulse(btnPass, _passTutorialPulse);
            _shoveTutorialPulse = EnsureTutorialPulse(btnShove, _shoveTutorialPulse);
            _defuseTutorialPulse = EnsureTutorialPulse(btnDefuse, _defuseTutorialPulse);
        }

        static TutorialButtonPulse EnsureTutorialPulse(
            Button button, TutorialButtonPulse current)
        {
            if (current != null || button == null)
                return current;
            var pulse = button.GetComponent<TutorialButtonPulse>();
            return pulse != null ? pulse : button.gameObject.AddComponent<TutorialButtonPulse>();
        }

        void SetTutorialPulse(BombAction? action)
        {
            EnsureTutorialButtonPulses();
            if (_passTutorialPulse != null)
                _passTutorialPulse.SetPulsing(action == BombAction.Pass);
            if (_shoveTutorialPulse != null)
                _shoveTutorialPulse.SetPulsing(action == BombAction.Shove);
            if (_defuseTutorialPulse != null)
                _defuseTutorialPulse.SetPulsing(action == BombAction.Defuse);
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

            if (decisionTimerFillRect != null)
            {
                // 剩余时间越少，左右锚点越靠近 0.5 → 向中间缩短
                float half = timerFillHalfSpan * t;
                decisionTimerFillRect.anchorMin = new Vector2(0.5f - half, timerFillYMin);
                decisionTimerFillRect.anchorMax = new Vector2(0.5f + half, timerFillYMax);
                decisionTimerFillRect.offsetMin = Vector2.zero;
                decisionTimerFillRect.offsetMax = Vector2.zero;
            }

            if (decisionTimerFill != null)
            {
                if (remaining <= TimerFlashRemainingSeconds)
                {
                    // 剩余≤5秒：持续红闪（红长白短）
                    float cycle = Mathf.Max(0.05f, timerFlashCycle);
                    float phase = Mathf.Repeat(Time.unscaledTime, cycle) / cycle;
                    bool showRed = phase < timerFlashRedDuty;
                    decisionTimerFill.color = showRed ? timerWarn : Color.white;
                }
                else
                {
                    decisionTimerFill.color = Color.white;
                }
            }

            if (decisionTimerText != null)
            {
                decisionTimerText.text = remaining.ToString("0.0");
                decisionTimerText.color = Color.white;
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
