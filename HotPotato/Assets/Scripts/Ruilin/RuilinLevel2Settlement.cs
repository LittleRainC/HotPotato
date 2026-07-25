using System.Collections.Generic;
using Chardin;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Ruilin
{
    /// <summary>
    /// 关卡结算UI。无需改场景；进入 RunLevelOrder 中的关卡时自动挂到 BattleController。
    /// 每关获胜选道具 → 继承到下一关；Level5 选完清空背包并回 Start。
    /// </summary>
    [ExecuteAlways]
    public sealed class RuilinLevel2Settlement : MonoBehaviour
    {
        /// <summary>与 Build Settings 中启用的关卡顺序一致：赢关后按此链加载下一关。</summary>
        static readonly string[] RunLevelOrder =
        {
            "Level1", "Level2", "Level3", "Level4", "Level5"
        };

        /// <summary>教学关：开局无续跑标记时清空背包；每关通关都选卡并继承到下一关。</summary>
        const string TutorialSceneName = "Level1";
        const string StartSceneName = "Start";

        BattleController battle;
        Canvas canvas;
        GameObject gameOverPanel;
        GameObject rewardPanel;
        GameObject replacePanel;
        Transform itemBar;
        Button nextButton;
        readonly Button[] rewardCards = new Button[2];
        readonly ItemDefinition[] rewards = new ItemDefinition[2];
        ItemDefinition pendingReward;
        bool settled;
        bool rewardCommitted;
        bool runtimeInitialized;
        Font font;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            string scene = SceneManager.GetActiveScene().name;
            if (System.Array.IndexOf(RunLevelOrder, scene) < 0)
                return;

            BattleController controller = Object.FindObjectOfType<BattleController>();
            if (controller != null && controller.GetComponent<RuilinLevel2Settlement>() == null)
                controller.gameObject.AddComponent<RuilinLevel2Settlement>();
        }

        void Awake()
        {
            if (!Application.isPlaying)
            {
                EnsureEditableHierarchy();
                return;
            }

            InitializeRuntime();
        }

        void Start()
        {
            if (Application.isPlaying)
                InitializeRuntime();
        }

        void InitializeRuntime()
        {
            if (runtimeInitialized)
                return;

            battle = GetComponent<BattleController>();
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            // 必须用战斗根 Canvas（含 BottomBar），不能 FindObjectOfType——结算层嵌套 Canvas 会抢先
            canvas = FindBattleCanvas();
            if (battle == null || canvas == null)
            {
                Debug.LogError("[Ruilin] Level2需要BattleController和Canvas。");
                enabled = false;
                return;
            }

            runtimeInitialized = true;
            // 先绑道具栏（依赖战斗 Canvas），再给结算层加嵌套 Canvas，避免找错 Canvas
            if (!BindExistingUi())
                BuildUi();
            else
                WireUiButtons();
            EnsurePlayerBombItemBar();
            HideLegacyItemBar();
            EnsureSettlementOverlayCanvases();

            // 无续跑标记：新开一局清空；有续跑：继承上一关道具
            bool continuing = RunInventory.ConsumeRunContinuing();
            if (continuing)
            {
                RunInventory.EnsureLoadedFromPrefs();
            }
            else
            {
                // Start→Level1，或直接进关，都视为新 Run
                RunInventory.ClearRun();
            }
            Debug.Log("[Ruilin] Enter " + SceneManager.GetActiveScene().name +
                      " continuing=" + continuing +
                      " items=" + RunInventory.Items.Count);

            RunInventory.ResetUsesForMatch();
            RunInventory.Changed += RefreshItemBar;
            RefreshItemBar();
        }

        Canvas FindBattleCanvas()
        {
            if (battle != null)
            {
                Transform underBattle = battle.transform.Find("Canvas");
                if (underBattle != null)
                {
                    var c = underBattle.GetComponent<Canvas>();
                    if (c != null)
                        return c;
                }
            }

            Canvas[] all = Object.FindObjectsOfType<Canvas>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null)
                    continue;
                if (all[i].transform.Find("BottomBar") != null)
                    return all[i];
            }

            return Object.FindObjectOfType<Canvas>();
        }

        void OnEnable()
        {
            if (Application.isPlaying)
                InitializeRuntime();
            else
                EnsureEditableHierarchy();
        }

        void EnsureEditableHierarchy()
        {
#if UNITY_EDITOR
            canvas = FindBattleCanvas();
            if (canvas == null)
                canvas = Object.FindObjectOfType<Canvas>();
            if (canvas == null || canvas.transform.Find("RuilinGameOver") != null)
                return;
            battle = GetComponent<BattleController>();
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            BuildUi();
            WireUiButtons();
            EnsurePlayerBombItemBar();
            HideLegacyItemBar();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
        }

        void OnDestroy()
        {
            RunInventory.Changed -= RefreshItemBar;
            Time.timeScale = 1f;
        }

        void Update()
        {
            if (!Application.isPlaying)
                return;
            if (!runtimeInitialized)
                InitializeRuntime();
            if (!runtimeInitialized || battle == null)
                return;
            if (settled || battle.CurrentPhase != BattleController.Phase.MatchOver)
                return;

            settled = true;
            bool playerAlive = false;
            bool enemyAlive = false;
            IReadOnlyList<TableSeat> seats = battle.ClockwiseOrder;
            for (int i = 0; i < seats.Count; i++)
            {
                if (seats[i] == null)
                    continue;
                if (seats[i].IsPlayer)
                    playerAlive |= seats[i].IsAlive;
                else
                    enemyAlive |= seats[i].IsAlive;
            }

            if (playerAlive && !enemyAlive)
                ShowRewards();
            else
                ShowGameOver();
        }

        void ShowGameOver()
        {
            EnsureSettlementOverlayCanvases();
            if (gameOverPanel != null)
            {
                gameOverPanel.transform.SetAsLastSibling();
                gameOverPanel.SetActive(true);
            }
            Time.timeScale = 0f;
        }

        void Restart()
        {
            Time.timeScale = 1f;
            RunInventory.ResetUsesForMatch();
            RunInventory.MarkRunContinuing();
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        void ShowRewards()
        {
            EnsureSettlementOverlayCanvases();
            EnsureRewardDim();
            LayoutRewardCards();
            RollTwoRewards();
            rewardCommitted = false;
            pendingReward = null;
            if (nextButton != null)
                nextButton.gameObject.SetActive(false);
            if (rewardPanel != null)
            {
                // 提到 OverlayHost 最前，盖住 BottomBar / ActionButtons
                rewardPanel.transform.SetAsLastSibling();
                rewardPanel.SetActive(true);
            }
            Time.timeScale = 0f;
        }

        /// <summary>结算时压暗全屏背景，卡牌在遮罩之上。</summary>
        void EnsureRewardDim()
        {
            if (rewardPanel == null)
                return;

            var panelRt = rewardPanel.GetComponent<RectTransform>();
            panelRt.anchorMin = Vector2.zero;
            panelRt.anchorMax = Vector2.one;
            panelRt.offsetMin = Vector2.zero;
            panelRt.offsetMax = Vector2.zero;
            panelRt.localScale = Vector3.one;
            panelRt.anchoredPosition = Vector2.zero;

            var parentImg = rewardPanel.GetComponent<Image>();
            if (parentImg != null)
            {
                parentImg.sprite = null;
                parentImg.color = new Color(0f, 0f, 0f, 0f);
                parentImg.raycastTarget = true;
            }

            Transform dimTf = rewardPanel.transform.Find("FullDim");
            Image dimImg;
            if (dimTf == null)
            {
                var go = new GameObject("FullDim", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.transform.SetParent(rewardPanel.transform, false);
                dimTf = go.transform;
                dimImg = go.GetComponent<Image>();
            }
            else
            {
                dimImg = dimTf.GetComponent<Image>();
                if (dimImg == null)
                    dimImg = dimTf.gameObject.AddComponent<Image>();
            }

            var dimRt = (RectTransform)dimTf;
            dimRt.anchorMin = Vector2.zero;
            dimRt.anchorMax = Vector2.one;
            dimRt.pivot = new Vector2(0.5f, 0.5f);
            dimRt.anchoredPosition = new Vector2(0f, 400f);
            dimRt.offsetMin = Vector2.zero;
            dimRt.offsetMax = Vector2.zero;
            dimRt.localScale = new Vector3(3f, 10f, 1f);
            dimRt.SetAsFirstSibling();
            dimImg.sprite = null;
            dimImg.color = new Color(0f, 0f, 0f, 0.82f);
            dimImg.raycastTarget = true;
        }

        /// <summary>
        /// 结算层挂到独立 ScreenSpaceOverlay Host（sorting 5000），
        /// 避免嵌在战斗 Canvas 下时被 ActionButtons(600)/道具栏(650) 盖住。
        /// </summary>
        void EnsureSettlementOverlayCanvases()
        {
            Transform host = EnsureOverlayHost();
            PromoteToOverlayHost(rewardPanel, host);
            PromoteToOverlayHost(replacePanel, host);
            PromoteToOverlayHost(gameOverPanel, host);
        }

        Transform EnsureOverlayHost()
        {
            const string hostName = "RuilinOverlayHost";
            Transform parent = null;
            if (canvas != null)
                parent = canvas.transform.parent; // Battle_2P
            if (parent == null && battle != null)
                parent = battle.transform;

            Transform host = null;
            if (parent != null)
                host = parent.Find(hostName);
            if (host == null)
            {
                // 全局再找一次（避免重复创建）
                var existing = GameObject.Find(hostName);
                if (existing != null)
                    host = existing.transform;
            }

            if (host == null)
            {
                var go = new GameObject(hostName, typeof(RectTransform), typeof(Canvas),
                    typeof(CanvasScaler), typeof(GraphicRaycaster));
                if (parent != null)
                    go.transform.SetParent(parent, false);
                host = go.transform;
            }

            var hostCanvas = host.GetComponent<Canvas>();
            if (hostCanvas == null)
                hostCanvas = host.gameObject.AddComponent<Canvas>();
            hostCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            hostCanvas.overrideSorting = true;
            hostCanvas.sortingOrder = 5000;

            if (host.GetComponent<GraphicRaycaster>() == null)
                host.gameObject.AddComponent<GraphicRaycaster>();

            var scaler = host.GetComponent<CanvasScaler>();
            if (scaler == null)
                scaler = host.gameObject.AddComponent<CanvasScaler>();
            var mainScaler = canvas != null ? canvas.GetComponent<CanvasScaler>() : null;
            if (mainScaler != null)
            {
                scaler.uiScaleMode = mainScaler.uiScaleMode;
                scaler.referenceResolution = mainScaler.referenceResolution;
                scaler.screenMatchMode = mainScaler.screenMatchMode;
                scaler.matchWidthOrHeight = mainScaler.matchWidthOrHeight;
            }
            else
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
            }

            host.SetAsLastSibling();
            return host;
        }

        static void PromoteToOverlayHost(GameObject panel, Transform host)
        {
            if (panel == null || host == null)
                return;

            if (panel.transform.parent != host)
                panel.transform.SetParent(host, false);

            var rt = panel.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                rt.localScale = Vector3.one;
                rt.localRotation = Quaternion.identity;
            }

            // 去掉面板上的嵌套 Canvas，统一由 Host 排序，避免再被战斗 UI 盖住
            var nestedRaycaster = panel.GetComponent<GraphicRaycaster>();
            if (nestedRaycaster != null)
                Object.DestroyImmediate(nestedRaycaster);
            var nestedCanvas = panel.GetComponent<Canvas>();
            if (nestedCanvas != null)
                Object.DestroyImmediate(nestedCanvas);
        }

        static void EnsureOverlayCanvas(GameObject panel, int sortingOrder)
        {
            // 兼容旧调用：仍强制提到高 sorting（若尚未 Promote）
            if (panel == null)
                return;
            var c = panel.GetComponent<Canvas>();
            if (c == null)
                c = panel.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.overrideSorting = true;
            c.sortingOrder = sortingOrder;
            if (panel.GetComponent<GraphicRaycaster>() == null)
                panel.AddComponent<GraphicRaycaster>();
        }

        void LayoutRewardCards()
        {
            LayoutOneRewardCard(rewardCards[0], new Vector2(-430f, 30f));
            LayoutOneRewardCard(rewardCards[1], new Vector2(430f, 30f));
            if (nextButton == null)
                return;
            var rt = nextButton.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 56f);
            rt.sizeDelta = new Vector2(320f, 80f);
            var label = nextButton.GetComponentInChildren<Text>(true);
            if (label != null)
                label.fontSize = 36;
        }

        static void LayoutOneRewardCard(Button card, Vector2 anchoredPos)
        {
            if (card == null)
                return;
            var rt = card.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = new Vector2(780f, 1050f);
            rt.localScale = Vector3.one;
            card.transition = Selectable.Transition.None;
        }

        void RollTwoRewards()
        {
            var candidates = new List<ItemDefinition>();
            IReadOnlyList<ItemDefinition> all = ItemCatalog.All;
            for (int i = 0; i < all.Count; i++)
                if (!RunInventory.Contains(all[i].Id))
                    candidates.Add(all[i]);

            if (candidates.Count < 2)
                for (int i = 0; i < all.Count; i++)
                    if (!candidates.Contains(all[i]))
                        candidates.Add(all[i]);

            int first = Random.Range(0, candidates.Count);
            rewards[0] = candidates[first];
            candidates.RemoveAt(first);
            rewards[1] = candidates[Random.Range(0, candidates.Count)];

            SetCard(rewardCards[0], rewards[0]);
            SetCard(rewardCards[1], rewards[1]);
        }

        void ChooseReward(int index)
        {
            if (rewardCommitted || index < 0 || index >= rewards.Length)
                return;

            pendingReward = rewards[index];
            rewardCommitted = true;
            SetCardSelected(rewardCards[index], true);
            SetCardSelected(rewardCards[1 - index], false);
            rewardCards[0].interactable = false;
            rewardCards[1].interactable = false;

            if (RunInventory.Items.Count < RunInventory.Capacity)
            {
                if (!RunInventory.TryAdd(pendingReward.Id))
                    Debug.LogWarning("[Ruilin] TryAdd 失败 id=" + pendingReward.Id);
                Debug.Log("[Ruilin] Chose reward " + pendingReward.Name +
                          " inventory=" + RunInventory.Items.Count);
                RunInventory.MarkRunContinuing();
                NextLevel();
            }
            else
            {
                ShowReplacePanel();
            }
        }

        void ShowReplacePanel()
        {
            if (replacePanel == null)
            {
                Debug.LogError("[Ruilin] replacePanel 缺失");
                return;
            }

            if (RunInventory.Items.Count < 2)
            {
                Debug.LogError("[Ruilin] 替换需要已有 2 件道具，当前=" + RunInventory.Items.Count);
                if (nextButton != null)
                    nextButton.gameObject.SetActive(true);
                return;
            }

            EnsureSettlementOverlayCanvases();

            // 选卡层先关掉，避免盖住替换层；替换层提到 OverlayHost 最前
            if (rewardPanel != null)
                rewardPanel.SetActive(false);

            var replaceImg = replacePanel.GetComponent<Image>();
            if (replaceImg != null)
            {
                replaceImg.sprite = null;
                replaceImg.color = new Color(0f, 0f, 0f, 0.88f);
                replaceImg.raycastTarget = true;
            }

            EnsureReplaceDim();

            Text title = replacePanel.GetComponentInChildren<Text>(true);
            if (title != null && title.transform.parent == replacePanel.transform)
            {
                title.text = "道具栏已满，请选择要替换的道具";
                title.fontSize = 42;
                title.color = Color.white;
                var trt = title.rectTransform;
                trt.anchorMin = new Vector2(0.1f, 0.82f);
                trt.anchorMax = new Vector2(0.9f, 0.95f);
                trt.offsetMin = Vector2.zero;
                trt.offsetMax = Vector2.zero;
                title.transform.SetAsLastSibling();
            }

            Button[] slotButtons = EnsureReplaceSlotButtons();
            if (slotButtons == null || slotButtons.Length < 2)
            {
                Debug.LogError("[Ruilin] 替换槽位未就绪");
                if (nextButton != null)
                    nextButton.gameObject.SetActive(true);
                return;
            }

            for (int i = 0; i < 2; i++)
            {
                Button button = slotButtons[i];
                if (button == null)
                    continue;

                LayoutOneRewardCard(button, new Vector2(i == 0 ? -430f : 430f, 30f));

                int captured = i;
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => ReplaceItem(captured));
                button.interactable = true;

                ItemDefinition ownedDef = ItemCatalog.Get(RunInventory.Items[i].Id);
                ApplyRewardCardVisual(button, ownedDef);
                // 标题改为提示替换
                Text label = null;
                for (int c = 0; c < button.transform.childCount; c++)
                {
                    var child = button.transform.GetChild(c);
                    if (child.name == "Icon" || child.name == "Desc")
                        continue;
                    label = child.GetComponent<Text>();
                    if (label != null)
                        break;
                }
                if (label != null)
                    label.text = "替换：" + ownedDef.Name + "  [" + ownedDef.Type + "]";
            }

            replacePanel.transform.SetAsLastSibling();
            replacePanel.SetActive(true);
            Debug.Log("[Ruilin] Replace panel on top, inventory=" + RunInventory.Items.Count);
        }

        void EnsureReplaceDim()
        {
            if (replacePanel == null)
                return;
            Transform dimTf = replacePanel.transform.Find("FullDim");
            Image dimImg;
            if (dimTf == null)
            {
                var go = new GameObject("FullDim", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.transform.SetParent(replacePanel.transform, false);
                dimTf = go.transform;
                dimImg = go.GetComponent<Image>();
            }
            else
            {
                dimImg = dimTf.GetComponent<Image>();
                if (dimImg == null)
                    dimImg = dimTf.gameObject.AddComponent<Image>();
            }

            var dimRt = (RectTransform)dimTf;
            dimRt.anchorMin = Vector2.zero;
            dimRt.anchorMax = Vector2.one;
            dimRt.pivot = new Vector2(0.5f, 0.5f);
            dimRt.anchoredPosition = new Vector2(0f, 400f);
            dimRt.offsetMin = Vector2.zero;
            dimRt.offsetMax = Vector2.zero;
            dimRt.localScale = new Vector3(3f, 10f, 1f);
            dimRt.SetAsFirstSibling();
            dimImg.sprite = null;
            dimImg.color = new Color(0f, 0f, 0f, 0.82f);
            dimImg.raycastTarget = true;
        }

        Button[] EnsureReplaceSlotButtons()
        {
            Transform slots = replacePanel.transform.Find("Slots");
            if (slots == null)
            {
                for (int i = 0; i < replacePanel.transform.childCount; i++)
                {
                    var child = replacePanel.transform.GetChild(i);
                    if (child.name == "FullDim")
                        continue;
                    if (child.GetComponentsInChildren<Button>(true).Length >= 2)
                    {
                        slots = child;
                        break;
                    }
                }
            }

            if (slots == null)
            {
                GameObject slotsGo = MakePanel(replacePanel.transform, "Slots",
                    Vector2.zero, Vector2.one, Color.clear);
                slots = slotsGo.transform;
            }

            // 关掉 LayoutGroup，改用与选卡相同的绝对定位大卡
            var layout = slots.GetComponent<HorizontalLayoutGroup>();
            if (layout != null)
                layout.enabled = false;

            var slotsRt = slots as RectTransform;
            if (slotsRt != null)
            {
                slotsRt.anchorMin = Vector2.zero;
                slotsRt.anchorMax = Vector2.one;
                slotsRt.offsetMin = Vector2.zero;
                slotsRt.offsetMax = Vector2.zero;
            }

            while (slots.childCount < 2)
                MakeButton(slots, "槽位", null, Vector2.zero, Vector2.one);

            var result = new Button[2];
            for (int i = 0; i < 2; i++)
            {
                var btn = slots.GetChild(i).GetComponent<Button>();
                if (btn == null)
                    btn = slots.GetChild(i).gameObject.AddComponent<Button>();
                var img = slots.GetChild(i).GetComponent<Image>();
                if (img == null)
                    img = slots.GetChild(i).gameObject.AddComponent<Image>();
                btn.targetGraphic = img;
                result[i] = btn;
            }

            slots.SetAsLastSibling();
            return result;
        }

        void ReplaceItem(int slot)
        {
            if (pendingReward == null)
                return;
            if (slot < 0 || slot >= RunInventory.Items.Count)
                return;

            RunInventory.ReplaceAt(slot, pendingReward.Id);
            if (replacePanel != null)
                replacePanel.SetActive(false);
            if (rewardPanel != null)
                rewardPanel.SetActive(false);
            RefreshItemBar();
            RunInventory.MarkRunContinuing();
            NextLevel();
        }

        void NextLevel()
        {
            if (!rewardCommitted || (replacePanel != null && replacePanel.activeSelf))
                return;

            Time.timeScale = 1f;
            string current = SceneManager.GetActiveScene().name;
            int idx = System.Array.IndexOf(RunLevelOrder, current);
            if (idx >= 0 && idx + 1 < RunLevelOrder.Length)
            {
                // 还有下一关：带着背包继续
                RunInventory.MarkRunContinuing();
                SceneManager.LoadScene(RunLevelOrder[idx + 1]);
            }
            else
            {
                // Level5 选完 = 全通关：清空背包，回主菜单
                RunInventory.ClearRun();
                RunInventory.ClearContinueFlag();
                if (rewardPanel != null)
                    rewardPanel.SetActive(false);
                settled = false;
                rewardCommitted = false;
                pendingReward = null;
                SceneManager.LoadScene(StartSceneName);
            }
        }

        void BuildUi()
        {
            gameOverPanel = MakeOverlay("RuilinGameOver", new Color(0f, 0f, 0f, 0.82f));
            MakeText(gameOverPanel.transform, "GAME OVER", 76, new Vector2(0.2f, 0.56f), new Vector2(0.8f, 0.72f));
            MakeButton(gameOverPanel.transform, "RESTART", Restart,
                new Vector2(0.38f, 0.36f), new Vector2(0.62f, 0.47f));
            gameOverPanel.SetActive(false);

            rewardPanel = MakeOverlay("RuilinReward", new Color(0.03f, 0.04f, 0.06f, 0.92f));
            MakeText(rewardPanel.transform, "选择一件道具", 52,
                new Vector2(0.2f, 0.82f), new Vector2(0.8f, 0.94f));
            rewardCards[0] = MakeButton(rewardPanel.transform, "", () => ChooseReward(0),
                new Vector2(0.14f, 0.30f), new Vector2(0.46f, 0.78f));
            rewardCards[1] = MakeButton(rewardPanel.transform, "", () => ChooseReward(1),
                new Vector2(0.54f, 0.30f), new Vector2(0.86f, 0.78f));
            nextButton = MakeButton(rewardPanel.transform, "NEXT", NextLevel,
                new Vector2(0.39f, 0.10f), new Vector2(0.61f, 0.21f));
            rewardPanel.SetActive(false);

            replacePanel = MakeOverlay("RuilinReplace", new Color(0f, 0f, 0f, 0.92f));
            MakeText(replacePanel.transform, "道具栏已满，请选择要替换的道具", 38,
                new Vector2(0.15f, 0.62f), new Vector2(0.85f, 0.78f));
            GameObject slots = MakePanel(replacePanel.transform, "Slots",
                new Vector2(0.22f, 0.35f), new Vector2(0.78f, 0.56f), Color.clear);
            var layout = slots.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 30f;
            layout.childForceExpandWidth = true;
            MakeButton(slots.transform, "槽位1", null, Vector2.zero, Vector2.one);
            MakeButton(slots.transform, "槽位2", null, Vector2.zero, Vector2.one);
            replacePanel.SetActive(false);
        }

        bool BindExistingUi()
        {
            Transform gameOver = FindOverlayPanel("RuilinGameOver");
            Transform reward = FindOverlayPanel("RuilinReward");
            Transform replace = FindOverlayPanel("RuilinReplace");
            if (gameOver == null || reward == null || replace == null)
                return false;

            Button[] gameOverButtons = gameOver.GetComponentsInChildren<Button>(true);
            Button[] rewardButtons = reward.GetComponentsInChildren<Button>(true);
            Button[] replaceButtons = replace.GetComponentsInChildren<Button>(true);
            if (gameOverButtons.Length < 1 || rewardButtons.Length < 3 || replaceButtons.Length < 2)
                return false;

            gameOverPanel = gameOver.gameObject;
            rewardPanel = reward.gameObject;
            replacePanel = replace.gameObject;
            rewardCards[0] = rewardButtons[0];
            rewardCards[1] = rewardButtons[1];
            nextButton = rewardButtons[2];
            return true;
        }

        Transform FindOverlayPanel(string name)
        {
            if (canvas != null)
            {
                Transform t = canvas.transform.Find(name);
                if (t != null)
                    return t;
            }
            if (battle != null)
            {
                Transform host = battle.transform.Find("RuilinOverlayHost");
                if (host != null)
                {
                    Transform t = host.Find(name);
                    if (t != null)
                        return t;
                }
                Transform[] all = battle.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                    if (all[i].name == name)
                        return all[i];
            }
            var go = GameObject.Find(name);
            return go != null ? go.transform : null;
        }

        void EnsurePlayerBombItemBar()
        {
            Transform panel = FindPlayerBombPanel();
            if (panel == null)
            {
                Debug.LogError("[Ruilin] 未找到 PlayerBombPanel，道具栏无法绑定。");
                itemBar = null;
                return;
            }

            // 去掉丑框：背景 Invisible，标题隐藏
            var panelImg = panel.GetComponent<Image>();
            if (panelImg != null)
            {
                panelImg.sprite = null;
                panelImg.color = Color.clear;
                panelImg.raycastTarget = false;
                panelImg.enabled = false;
            }

            for (int i = 0; i < panel.childCount; i++)
            {
                var child = panel.GetChild(i);
                if (child.name == "Slots")
                    continue;
                var title = child.GetComponent<Text>();
                if (title != null)
                    child.gameObject.SetActive(false);
            }

            // 脱离原窄框：左下自由区域，只放两张大卡
            var panelRt = panel as RectTransform;
            if (panelRt != null)
            {
                panelRt.anchorMin = new Vector2(0f, 0f);
                panelRt.anchorMax = new Vector2(0f, 0f);
                panelRt.pivot = new Vector2(0f, 0f);
                panelRt.anchoredPosition = new Vector2(18f, 18f); // 略上移，躲开桌沿/文字
                panelRt.sizeDelta = new Vector2(480f, 320f);
                panelRt.localScale = Vector3.one;
            }

            Transform slots = panel.Find("Slots");
            if (slots == null)
            {
                GameObject slotsGo = MakePanel(panel, "Slots",
                    Vector2.zero, Vector2.one, Color.clear);
                slots = slotsGo.transform;
            }

            var slotsRt = slots as RectTransform;
            if (slotsRt != null)
            {
                slotsRt.anchorMin = Vector2.zero;
                slotsRt.anchorMax = Vector2.one;
                slotsRt.offsetMin = Vector2.zero;
                slotsRt.offsetMax = Vector2.zero;
            }

            var slotsImg = slots.GetComponent<Image>();
            if (slotsImg != null)
            {
                slotsImg.color = Color.clear;
                slotsImg.raycastTarget = false;
            }

            // 不用 LayoutGroup，绝对定位大卡
            var layout = slots.GetComponent<HorizontalLayoutGroup>();
            if (layout != null)
                layout.enabled = false;

            while (slots.childCount < 2)
                MakeButton(slots, "", null, Vector2.zero, Vector2.one);

            // 再放大一档：约 190×265
            const float cardW = 190f;
            const float cardH = 265f;
            const float gap = 20f;

            for (int i = 0; i < 2; i++)
            {
                var slotRt = slots.GetChild(i) as RectTransform;
                if (slotRt == null)
                    continue;

                var le = slotRt.GetComponent<LayoutElement>();
                if (le != null)
                    le.enabled = false;

                slotRt.anchorMin = new Vector2(0f, 0f);
                slotRt.anchorMax = new Vector2(0f, 0f);
                slotRt.pivot = new Vector2(0.5f, 0.5f);
                slotRt.sizeDelta = new Vector2(cardW, cardH);
                slotRt.anchoredPosition = new Vector2(
                    cardW * 0.5f + i * (cardW + gap),
                    cardH * 0.5f + 8f); // 卡面上移约 8px
                slotRt.localScale = Vector3.one;

                var btn = slotRt.GetComponent<Button>();
                if (btn == null)
                    btn = slotRt.gameObject.AddComponent<Button>();
                var img = slotRt.GetComponent<Image>();
                if (img == null)
                    img = slotRt.gameObject.AddComponent<Image>();
                img.color = Color.clear;
                img.raycastTarget = true;
                btn.targetGraphic = img;
                btn.transition = Selectable.Transition.None;

                Text label = slotRt.GetComponentInChildren<Text>(true);
                if (label == null)
                    label = MakeText(slotRt, "", 14, Vector2.zero, Vector2.one);
                label.text = "";
            }

            var barCanvas = panel.GetComponent<Canvas>();
            if (barCanvas == null)
                barCanvas = panel.gameObject.AddComponent<Canvas>();
            barCanvas.overrideSorting = true;
            barCanvas.sortingOrder = 650;
            if (panel.GetComponent<GraphicRaycaster>() == null)
                panel.gameObject.AddComponent<GraphicRaycaster>();

            itemBar = slots;
            Debug.Log("[Ruilin] ItemBar ready (frameless) slots=" + slots.childCount +
                      " inventory=" + RunInventory.Items.Count);
        }

        Transform FindPlayerBombPanel()
        {
            Transform root = canvas != null ? canvas.transform : null;
            if (root == null && battle != null)
                root = battle.transform.Find("Canvas");

            if (root != null)
            {
                Transform direct = root.Find("BottomBar/PlayerBombPanel");
                if (direct != null)
                    return direct;
            }

            if (battle != null)
            {
                Transform[] underBattle = battle.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < underBattle.Length; i++)
                    if (underBattle[i].name == "PlayerBombPanel")
                        return underBattle[i];
            }

            Transform[] all = Object.FindObjectsOfType<Transform>(true);
            for (int i = 0; i < all.Length; i++)
                if (all[i].name == "PlayerBombPanel")
                    return all[i];
            return null;
        }

        void HideLegacyItemBar()
        {
            if (canvas == null)
                return;
            Transform legacy = canvas.transform.Find("RuilinItemBar");
            if (legacy == null)
            {
                Transform[] all = canvas.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i].name == "RuilinItemBar")
                    {
                        legacy = all[i];
                        break;
                    }
                }
            }
            if (legacy != null)
                legacy.gameObject.SetActive(false);
        }

        void WireUiButtons()
        {
            Button restart = gameOverPanel.GetComponentInChildren<Button>(true);
            restart.onClick.RemoveAllListeners();
            restart.onClick.AddListener(Restart);

            rewardCards[0].onClick.RemoveAllListeners();
            rewardCards[0].onClick.AddListener(() => ChooseReward(0));
            rewardCards[1].onClick.RemoveAllListeners();
            rewardCards[1].onClick.AddListener(() => ChooseReward(1));
            nextButton.onClick.RemoveAllListeners();
            nextButton.onClick.AddListener(NextLevel);

            gameOverPanel.SetActive(false);
            rewardPanel.SetActive(false);
            replacePanel.SetActive(false);
        }

        void RefreshItemBar()
        {
            if (itemBar == null)
                return;
            for (int i = 0; i < 2; i++)
            {
                if (i >= itemBar.childCount)
                    break;
                Transform slot = itemBar.GetChild(i);
                Button slotButton = slot.GetComponent<Button>();
                if (slotButton == null)
                    slotButton = slot.gameObject.AddComponent<Button>();
                Text label = slot.GetComponentInChildren<Text>(true);
                if (label == null)
                    label = MakeText(slot, "", 14, Vector2.zero, Vector2.one);

                slotButton.onClick.RemoveAllListeners();
                slotButton.interactable = false;
                if (i >= RunInventory.Items.Count)
                {
                    ClearItemVisual(slotButton);
                    label.text = "";
                    slot.gameObject.SetActive(false);
                    continue;
                }

                slot.gameObject.SetActive(true);
                OwnedItem owned = RunInventory.Items[i];
                ItemDefinition definition = ItemCatalog.Get(owned.Id);
                string status = definition.IsActive
                    ? definition.Name + "\n×" + owned.RemainingUses
                    : definition.Name + "\n被动";
                ApplyItemVisual(slotButton, definition, status, rewardCardLayout: false);

                if (definition.IsActive && owned.RemainingUses > 0)
                {
                    ItemId captured = owned.Id;
                    slotButton.interactable = true;
                    slotButton.onClick.AddListener(() => UseActiveItem(captured));
                }
            }
        }

        void UseActiveItem(ItemId id)
        {
            switch (id)
            {
                case ItemId.Peek:
                    battle.TryUsePeek();
                    break;
                case ItemId.ReflectGlove:
                    battle.TryUseReflectGlove();
                    break;
                case ItemId.FateDie:
                    battle.TryUseFateDie();
                    break;
            }
            RefreshItemBar();
        }

        void SetCard(Button card, ItemDefinition item)
        {
            card.interactable = true;
            ApplyRewardCardVisual(card, item);
            SetCardSelected(card, false);
        }

        static void SetCardSelected(Button card, bool selected)
        {
            if (card == null)
                return;
            card.transform.localScale = selected ? Vector3.one * 1.08f : Vector3.one;
            if (card.image != null)
                card.image.color = selected ? new Color(1f, 1f, 0.9f, 1f) : Color.white;
        }

        /// <summary>结算大卡：卡面 sprite；名称与描述在卡下方。</summary>
        static void ApplyRewardCardVisual(Button button, ItemDefinition item)
        {
            if (button == null || item == null)
                return;

            Sprite sprite = ItemCatalog.GetIcon(item.Id);
            Transform oldIcon = button.transform.Find("Icon");
            if (oldIcon != null)
                oldIcon.gameObject.SetActive(false);

            if (button.image != null)
            {
                button.image.sprite = sprite;
                button.image.color = Color.white;
                button.image.type = Image.Type.Simple;
                button.image.preserveAspect = true;
            }

            Font font = null;
            Text title = null;
            for (int i = 0; i < button.transform.childCount; i++)
            {
                var child = button.transform.GetChild(i);
                if (child.name == "Icon" || child.name == "Desc")
                    continue;
                title = child.GetComponent<Text>();
                if (title != null)
                    break;
            }

            if (title != null)
            {
                font = title.font;
                title.text = item.Name + "  [" + item.Type + "]";
                title.fontSize = 30;
                title.fontStyle = FontStyle.Bold;
                title.alignment = TextAnchor.MiddleCenter;
                title.color = Color.white;
                title.horizontalOverflow = HorizontalWrapMode.Wrap;
                title.verticalOverflow = VerticalWrapMode.Truncate;
                title.raycastTarget = false;
                var lrt = title.rectTransform;
                lrt.anchorMin = new Vector2(0.05f, 0f);
                lrt.anchorMax = new Vector2(0.95f, 0f);
                lrt.pivot = new Vector2(0.5f, 1f);
                lrt.anchoredPosition = new Vector2(0f, 150f);
                lrt.sizeDelta = new Vector2(0f, 40f);
            }

            if (font == null)
            {
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (font == null)
                    font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            Text desc = EnsureRewardDescText(button.transform, font);
            desc.text = item.Description;
        }

        static Text EnsureRewardDescText(Transform card, Font font)
        {
            Transform existing = card.Find("Desc");
            Text text;
            if (existing != null)
            {
                text = existing.GetComponent<Text>();
            }
            else
            {
                var go = new GameObject("Desc", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
                go.transform.SetParent(card, false);
                text = go.GetComponent<Text>();
            }

            var rt = text.rectTransform;
            rt.anchorMin = new Vector2(0.04f, 0f);
            rt.anchorMax = new Vector2(0.96f, 0f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, 106f);
            rt.sizeDelta = new Vector2(0f, 150f);

            text.font = font;
            text.fontSize = 26;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.UpperCenter;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.transform.SetAsLastSibling();
            return text;
        }

        static void ApplyItemVisual(Button button, ItemDefinition item, string labelText, bool rewardCardLayout)
        {
            if (button == null || item == null)
                return;

            if (rewardCardLayout)
            {
                ApplyRewardCardVisual(button, item);
                return;
            }

            Image icon = EnsureIconImage(button.transform);
            Sprite sprite = ItemCatalog.GetIcon(item.Id);
            // 道具栏：整张按钮就是卡面，不再套深色底框
            if (icon != null)
                icon.gameObject.SetActive(false);

            if (button.image != null)
            {
                button.image.sprite = sprite;
                button.image.enabled = sprite != null;
                button.image.color = Color.white;
                button.image.type = Image.Type.Simple;
                button.image.preserveAspect = true;
                button.image.raycastTarget = true;
            }

            Text label = button.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                label.text = labelText ?? "";
                var lrt = label.rectTransform;
                // 文字挂在卡面下方，避免与图重叠
                lrt.anchorMin = new Vector2(0f, 0f);
                lrt.anchorMax = new Vector2(1f, 0f);
                lrt.pivot = new Vector2(0.5f, 1f);
                lrt.anchoredPosition = new Vector2(0f, -6f);
                lrt.sizeDelta = new Vector2(0f, 42f);
                label.fontSize = 18;
                label.fontStyle = FontStyle.Bold;
                label.alignment = TextAnchor.UpperCenter;
                label.color = Color.white;
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                label.verticalOverflow = VerticalWrapMode.Truncate;
                label.raycastTarget = false;
            }
        }

        static void ClearItemVisual(Button button)
        {
            Image icon = button != null ? button.transform.Find("Icon")?.GetComponent<Image>() : null;
            if (icon != null)
            {
                icon.sprite = null;
                icon.enabled = false;
            }
            if (button != null && button.image != null)
            {
                button.image.sprite = null;
                button.image.color = Color.clear;
            }
            Text label = button != null ? button.GetComponentInChildren<Text>(true) : null;
            if (label != null)
                label.text = "";
        }

        static Image EnsureIconImage(Transform button)
        {
            Transform existing = button.Find("Icon");
            if (existing != null)
                return existing.GetComponent<Image>();

            var go = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(button, false);
            go.transform.SetAsFirstSibling();
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            img.type = Image.Type.Simple;
            img.preserveAspect = true;
            return img;
        }

        GameObject MakeOverlay(string name, Color color)
        {
            return MakePanel(canvas.transform, name, Vector2.zero, Vector2.one, color);
        }

        GameObject MakePanel(Transform parent, string name, Vector2 min, Vector2 max, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            Image image = go.GetComponent<Image>();
            image.color = color;
            return go;
        }

        Text MakeText(Transform parent, string value, int size, Vector2 min, Vector2 max)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            Text text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.text = value;
            text.raycastTarget = false;
            return text;
        }

        Button MakeButton(Transform parent, string label, UnityEngine.Events.UnityAction action,
            Vector2 min, Vector2 max)
        {
            GameObject go = MakePanel(parent, "Button", min, max, new Color(0.24f, 0.29f, 0.38f, 1f));
            Button button = go.AddComponent<Button>();
            button.targetGraphic = go.GetComponent<Image>();
            if (action != null)
                button.onClick.AddListener(action);
            MakeText(go.transform, label, 30, Vector2.zero, Vector2.one);
            return button;
        }
    }
}
