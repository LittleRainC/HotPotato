using System.Collections.Generic;
using Chardin;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Ruilin
{
    /// <summary>
    /// Level2自动结算UI。无需修改场景文件；进入Level2时自动挂到BattleController。
    /// </summary>
    [ExecuteAlways]
    public sealed class RuilinLevel2Settlement : MonoBehaviour
    {
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
            if (scene != "Level2" && scene != "Level3")
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

            canvas = Object.FindObjectOfType<Canvas>();
            if (battle == null || canvas == null)
            {
                Debug.LogError("[Ruilin] Level2需要BattleController和Canvas。");
                enabled = false;
                return;
            }

            runtimeInitialized = true;
            if (!BindExistingUi())
                BuildUi();
            else
                WireUiButtons();
            EnsurePlayerBombItemBar();
            HideLegacyItemBar();

            // 直接进 Level2/3（或新开 Play）会带上 PlayerPrefs 旧背包；仅续关/重开时保留。
            if (!RunInventory.ConsumeRunContinuing())
                RunInventory.ClearRun();

            RunInventory.ResetUsesForMatch();
            RunInventory.Changed += RefreshItemBar;
            RefreshItemBar();
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
            gameOverPanel.SetActive(true);
            Time.timeScale = 0f;
        }

        void Restart()
        {
            Time.timeScale = 1f;
            RunInventory.ResetUsesForMatch();
            RunInventory.MarkRunContinuing(); // 本关重开保留背包
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        void ShowRewards()
        {
            RollTwoRewards();
            rewardCommitted = false;
            pendingReward = null;
            nextButton.gameObject.SetActive(false);
            rewardPanel.SetActive(true);
            Time.timeScale = 0f;
        }

        void RollTwoRewards()
        {
            var candidates = new List<ItemDefinition>();
            IReadOnlyList<ItemDefinition> all = ItemCatalog.All;
            for (int i = 0; i < all.Count; i++)
                if (!RunInventory.Contains(all[i].Id))
                    candidates.Add(all[i]);

            // 已收集四件以上时允许池中重新出现已有道具，避免候选不足。
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
                RunInventory.TryAdd(pendingReward.Id);
                nextButton.gameObject.SetActive(true);
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

            // 提到最前，避免被奖励层挡住点击
            replacePanel.transform.SetAsLastSibling();
            replacePanel.SetActive(true);

            Transform slots = replacePanel.transform.Find("Slots");
            if (slots == null)
            {
                // 兼容旧层级：找带多个 Button 的子节点
                for (int i = 0; i < replacePanel.transform.childCount; i++)
                {
                    var child = replacePanel.transform.GetChild(i);
                    if (child.GetComponentsInChildren<Button>(true).Length >= 2)
                    {
                        slots = child;
                        break;
                    }
                }
            }

            if (slots == null || slots.childCount < 2 || RunInventory.Items.Count < 2)
            {
                Debug.LogError("[Ruilin] 替换槽位未就绪 slots=" + (slots != null) +
                               " items=" + RunInventory.Items.Count);
                // 兜底：无法替换则放弃本奖励，仍可 NEXT
                nextButton.gameObject.SetActive(true);
                return;
            }

            for (int i = 0; i < 2; i++)
            {
                Button button = slots.GetChild(i).GetComponent<Button>();
                if (button == null)
                    button = slots.GetChild(i).GetComponentInChildren<Button>(true);
                if (button == null)
                    continue;

                // 保证可见可点
                var img = button.GetComponent<Image>();
                if (img != null)
                {
                    img.raycastTarget = true;
                    if (img.color.a < 0.5f)
                        img.color = new Color(0.24f, 0.29f, 0.38f, 1f);
                }

                int captured = i;
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => ReplaceItem(captured));
                button.interactable = true;

                Text label = button.GetComponentInChildren<Text>(true);
                if (label != null)
                    label.text = "替换 " + ItemCatalog.Get(RunInventory.Items[i].Id).Name;
            }
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
            if (nextButton != null)
                nextButton.gameObject.SetActive(true);
            RefreshItemBar();
        }

        void NextLevel()
        {
            if (!rewardCommitted || (replacePanel != null && replacePanel.activeSelf))
                return;

            Time.timeScale = 1f;
            int next = SceneManager.GetActiveScene().buildIndex + 1;
            if (next < SceneManager.sceneCountInBuildSettings)
            {
                RunInventory.MarkRunContinuing();
                SceneManager.LoadScene(next);
            }
            else
            {
                rewardPanel.SetActive(false);
                RunInventory.ResetUsesForMatch();
                settled = false;
                rewardCommitted = false;
                pendingReward = null;
                battle.BeginMatch();
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
            Transform gameOver = canvas.transform.Find("RuilinGameOver");
            Transform reward = canvas.transform.Find("RuilinReward");
            Transform replace = canvas.transform.Find("RuilinReplace");
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

        /// <summary>
        /// 复用 BottomBar/PlayerBombPanel 作为道具栏，不再单独造 RuilinItemBar。
        /// </summary>
        void EnsurePlayerBombItemBar()
        {
            Transform panel = FindPlayerBombPanel();
            if (panel == null)
            {
                Debug.LogError("[Ruilin] 未找到 PlayerBombPanel，道具栏无法绑定。");
                itemBar = null;
                return;
            }

            // 标题：保留原 Text，若没有就补一个
            Text title = null;
            for (int i = 0; i < panel.childCount; i++)
            {
                var child = panel.GetChild(i);
                if (child.name == "Slots")
                    continue;
                title = child.GetComponent<Text>();
                if (title != null)
                    break;
            }
            if (title == null)
                title = MakeText(panel, "道具栏", 18, new Vector2(0.05f, 0.72f), new Vector2(0.95f, 0.98f));
            else
            {
                title.text = "道具栏";
                var titleRt = title.rectTransform;
                titleRt.anchorMin = new Vector2(0.05f, 0.72f);
                titleRt.anchorMax = new Vector2(0.95f, 0.98f);
                titleRt.offsetMin = Vector2.zero;
                titleRt.offsetMax = Vector2.zero;
            }

            Transform slots = panel.Find("Slots");
            if (slots == null)
            {
                GameObject slotsGo = MakePanel(panel, "Slots",
                    new Vector2(0.04f, 0.06f), new Vector2(0.96f, 0.68f),
                    Color.clear);
                slots = slotsGo.transform;
                var layout = slotsGo.AddComponent<HorizontalLayoutGroup>();
                layout.spacing = 6f;
                layout.padding = new RectOffset(2, 2, 2, 2);
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = true;
                layout.childControlWidth = true;
                layout.childControlHeight = true;
            }

            while (slots.childCount < 2)
                MakeButton(slots, "空", null, Vector2.zero, Vector2.one);

            // 槽位铺满 Slots
            for (int i = 0; i < 2; i++)
            {
                var slotRt = slots.GetChild(i) as RectTransform;
                if (slotRt == null)
                    continue;
                slotRt.anchorMin = Vector2.zero;
                slotRt.anchorMax = Vector2.one;
                slotRt.offsetMin = Vector2.zero;
                slotRt.offsetMax = Vector2.zero;
                var label = slotRt.GetComponentInChildren<Text>();
                if (label != null)
                    label.fontSize = 16;
            }

            itemBar = slots;
        }

        Transform FindPlayerBombPanel()
        {
            if (canvas == null)
                return null;
            Transform direct = canvas.transform.Find("BottomBar/PlayerBombPanel");
            if (direct != null)
                return direct;

            Transform[] all = canvas.GetComponentsInChildren<Transform>(true);
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
                Button slotButton = itemBar.GetChild(i).GetComponent<Button>();
                if (slotButton == null)
                    continue;
                Text label = slotButton.GetComponentInChildren<Text>();
                if (label == null)
                    continue;
                slotButton.onClick.RemoveAllListeners();
                slotButton.interactable = false;
                if (i >= RunInventory.Items.Count)
                {
                    label.text = "空";
                    continue;
                }

                OwnedItem owned = RunInventory.Items[i];
                ItemDefinition definition = ItemCatalog.Get(owned.Id);
                label.text = definition.IsActive
                    ? definition.Name + "\n×" + owned.RemainingUses
                    : definition.Name + "\n被动";

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
            card.image.color = new Color(0.20f, 0.23f, 0.30f, 1f);
            card.GetComponentInChildren<Text>().text =
                item.Name + "\n\n[" + item.Type + "]\n\n" + item.Description;
        }

        static void SetCardSelected(Button card, bool selected)
        {
            card.image.color = selected
                ? new Color(0.35f, 0.35f, 0.35f, 1f)
                : new Color(0.14f, 0.14f, 0.14f, 1f);
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
