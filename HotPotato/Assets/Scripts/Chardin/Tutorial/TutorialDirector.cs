using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Chardin
{
    /// <summary>
    /// Tutorial 引导：CSV 弹窗混合推进（点面板 / Pass / Shove / Defuse），结束后开放正常战斗。
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class TutorialDirector : MonoBehaviour
    {
        BattleController _battle;
        BattleHud _hud;
        TutorialDialogueUI _ui;
        readonly Dictionary<string, TutorialDialogueLine> _byId = new Dictionary<string, TutorialDialogueLine>();
        TutorialDialogueLine _current;
        bool _guiding;
        bool _started;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (SceneManager.GetActiveScene().name != "tutorial")
                return;

            var battle = Object.FindObjectOfType<BattleController>();
            if (battle == null)
                return;
            if (battle.GetComponent<TutorialDirector>() == null)
                battle.gameObject.AddComponent<TutorialDirector>();
        }

        void Awake()
        {
            _battle = GetComponent<BattleController>();
            _hud = GetComponent<BattleHud>();
            if (_hud == null && _battle != null)
                _hud = _battle.GetComponent<BattleHud>();
            if (_battle != null)
                _battle.ForcePlayerFirstHolder = true;
        }

        void OnEnable()
        {
            if (_battle != null)
                _battle.PlayerActionResolved += OnPlayerActionResolved;
        }

        void OnDisable()
        {
            if (_battle != null)
                _battle.PlayerActionResolved -= OnPlayerActionResolved;
            if (_ui != null)
                _ui.PanelClicked -= OnPanelClicked;
        }

        void Start()
        {
            if (_started)
                return;
            _started = true;
            BeginTutorial();
        }

        void Update()
        {
            if (!_guiding || _battle == null)
                return;

            // 引导期间不让决策倒计时逼玩家
            if (_battle.CurrentPhase == BattleController.Phase.AwaitingPlayerAction
                || _battle.CurrentPhase == BattleController.Phase.AimingShove)
            {
                _battle.SoftExtendPlayerDeadline();
            }

            ApplyGateForCurrentLine();
        }

        void BeginTutorial()
        {
            var lines = TutorialDialogueCsv.LoadFromResources();
            _byId.Clear();
            for (int i = 0; i < lines.Count; i++)
                _byId[lines[i].Id] = lines[i];

            if (lines.Count == 0)
            {
                Debug.LogWarning("[Tutorial] No dialogue lines; skipping guide.");
                EndGuide();
                return;
            }

            var canvas = Object.FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                Debug.LogError("[Tutorial] Canvas missing.");
                EndGuide();
                return;
            }

            _ui = TutorialDialogueUI.Create(canvas.transform);
            _ui.PanelClicked += OnPanelClicked;

            _guiding = true;
            if (_battle != null)
                _battle.ForcePlayerFirstHolder = true;

            ShowLine(lines[0]);
        }

        void ShowLine(TutorialDialogueLine line)
        {
            _current = line;
            if (line == null)
            {
                EndGuide();
                return;
            }

            bool bubble = line.Advance == TutorialAdvance.ClickBubble;
            _ui.Show(line.Speaker, line.Text, clickable: bubble);
            HandleEvent(line.Event);
            ApplyGateForCurrentLine();
            Debug.Log("[Tutorial] Show " + line.Id + " advance=" + line.Advance + " event=" + line.Event);
        }

        void HandleEvent(string evt)
        {
            if (string.IsNullOrEmpty(evt) || _hud == null)
                return;

            string key = evt.Trim().ToLowerInvariant();
            if (key.Contains("highlight the pass") || key.Contains("highlightsnuff") || key.Contains("highlight the number")
                || key.Contains("highlight hp") || key.Contains("bomb returns"))
            {
                // 轻量提示：写进播报，不挡流程
                _hud.SetBroadcast(evt);
            }
        }

        void ApplyGateForCurrentLine()
        {
            if (_hud == null || !_guiding || _current == null)
                return;

            bool playerTurn = _battle != null
                && (_battle.CurrentPhase == BattleController.Phase.AwaitingPlayerAction
                    || _battle.CurrentPhase == BattleController.Phase.AimingShove);
            bool defuseOk = _battle == null || _battle.DefuseCharges > 0;

            switch (_current.Advance)
            {
                case TutorialAdvance.ClickPass:
                    _hud.SetTutorialGate(playerTurn ? (BombAction?)BombAction.Pass : null, defuseOk);
                    break;
                case TutorialAdvance.ClickStuff:
                    _hud.SetTutorialGate(playerTurn ? (BombAction?)BombAction.Shove : null, defuseOk);
                    break;
                case TutorialAdvance.ClickSnuff:
                    _hud.SetTutorialGate(playerTurn ? (BombAction?)BombAction.Defuse : null, defuseOk);
                    break;
                default:
                    _hud.SetTutorialGate(null, false); // 锁全部，等点面板
                    break;
            }
        }

        void OnPanelClicked()
        {
            if (!_guiding || _current == null)
                return;
            if (_current.Advance != TutorialAdvance.ClickBubble)
                return;
            Advance();
        }

        void OnPlayerActionResolved(BombAction action)
        {
            if (!_guiding || _current == null)
                return;

            bool match =
                (_current.Advance == TutorialAdvance.ClickPass && action == BombAction.Pass)
                || (_current.Advance == TutorialAdvance.ClickStuff && action == BombAction.Shove)
                || (_current.Advance == TutorialAdvance.ClickSnuff && action == BombAction.Defuse);

            if (match)
                Advance();
        }

        void Advance()
        {
            if (_current == null)
            {
                EndGuide();
                return;
            }

            string nextId = _current.NextId;
            if (string.IsNullOrEmpty(nextId) || !_byId.ContainsKey(nextId))
            {
                EndGuide();
                return;
            }

            ShowLine(_byId[nextId]);
        }

        void EndGuide()
        {
            _guiding = false;
            _current = null;
            if (_ui != null)
                _ui.Hide();
            if (_hud != null)
            {
                _hud.ClearTutorialGate();
                bool defuseOk = _battle == null || _battle.DefuseCharges > 0;
                bool playerTurn = _battle != null
                    && (_battle.CurrentPhase == BattleController.Phase.AwaitingPlayerAction
                        || _battle.CurrentPhase == BattleController.Phase.AimingShove);
                _hud.SetActionsInteractable(playerTurn, defuseOk);
                _hud.SetBroadcast("Tutorial complete — good luck.");
            }
            Debug.Log("[Tutorial] Guide finished; free play.");
        }
    }
}
