using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Chardin
{
    /// <summary>
    /// 单场对局状态机（2P 白盒）。爆炸后 AI 出局则换新弹；玩家挨炸则扣心重开本场。
    /// </summary>
    public sealed class BattleController : MonoBehaviour
    {
        public enum Phase
        {
            Boot,
            AwaitingPlayerAction,
            AwaitingAiAction,
            Resolving,
            MatchOver
        }

        [Header("Refs")]
        [SerializeField] Bomb bomb;
        [SerializeField] Transform playerSeat;
        [SerializeField] Transform opponentSeat;
        [SerializeField] BattleHud hud;
        [SerializeField] PlaceholderBattleAi placeholderAi;

        [Header("Match")]
        [SerializeField] int startingHearts = 3;
        [SerializeField] int sharedDefusePerMatch = 3;
        [SerializeField] float decisionSeconds = 10f;
        [SerializeField] float slipChance = 0.2f;
        [SerializeField] string opponentName = "Worm";

        [Header("Countdown by alive count")]
        [SerializeField] Vector2Int range2 = new Vector2Int(8, 12);
        [SerializeField] Vector2Int range3 = new Vector2Int(10, 16);
        [SerializeField] Vector2Int range4 = new Vector2Int(14, 20);

        readonly List<BattleParticipant> _participants = new List<BattleParticipant>();
        int _holderId;
        int _hearts;
        int _defuseCharges;
        float _decisionDeadline;
        bool _busy;
        Phase _phase = Phase.Boot;

        public Phase CurrentPhase => _phase;
        public int HolderId => _holderId;
        public int DefuseCharges => _defuseCharges;

        void Awake()
        {
            if (bomb == null)
                bomb = GetComponentInChildren<Bomb>(true);
            if (hud == null)
                hud = GetComponent<BattleHud>() ?? gameObject.AddComponent<BattleHud>();

            // 关掉炸弹键盘调试，避免和流程抢输入
            var debug = bomb != null ? bomb.GetComponent<BombDebugDriver>() : null;
            if (debug != null)
                debug.enabled = false;

            AutoWireSeats();
            hud.BindFromHierarchy(transform);
            hud.ActionClicked += OnPlayerActionClicked;
        }

        void OnDestroy()
        {
            if (hud != null)
                hud.ActionClicked -= OnPlayerActionClicked;
        }

        void Start()
        {
            BeginMatch();
        }

        void Update()
        {
            if (_busy || _phase == Phase.MatchOver || _phase == Phase.Boot)
            {
                if (_phase != Phase.AwaitingPlayerAction)
                    hud.SetDecisionTimerVisible(false);
                return;
            }

            if (_phase == Phase.AwaitingPlayerAction)
            {
                float remaining = _decisionDeadline - Time.time;
                hud.SetDecisionTimer(remaining, decisionSeconds);

                if (remaining <= 0f)
                    ForceTimeoutPass();
            }
            else
            {
                hud.SetDecisionTimerVisible(false);
            }
        }

        void AutoWireSeats()
        {
            if (playerSeat == null)
            {
                var seat = transform.Find("PlayerSeat");
                if (seat == null)
                {
                    var go = new GameObject("PlayerSeat");
                    go.transform.SetParent(transform, false);
                    go.transform.localPosition = new Vector3(0f, -1.1f, 0f);
                    playerSeat = go.transform;
                }
                else playerSeat = seat;
            }

            if (opponentSeat == null)
            {
                var opp = transform.Find("Opponent");
                opponentSeat = opp != null ? opp : transform;
            }

            if (placeholderAi == null)
            {
                placeholderAi = GetComponent<PlaceholderBattleAi>();
                if (placeholderAi == null)
                    placeholderAi = gameObject.AddComponent<PlaceholderBattleAi>();
            }
        }

        public void BeginMatch()
        {
            StopAllCoroutines();
            _busy = false;
            _hearts = startingHearts;
            _participants.Clear();

            _participants.Add(new BattleParticipant(0, "Player", true, playerSeat));
            _participants.Add(new BattleParticipant(1, opponentName, false, opponentSeat));

            hud.SetOpponentName(opponentName);
            hud.SetHearts(_hearts);
            StartFightRound(reviveAll: true);
        }

        void StartFightRound(bool reviveAll)
        {
            if (reviveAll)
            {
                for (int i = 0; i < _participants.Count; i++)
                    _participants[i].IsAlive = true;

                if (opponentSeat != null)
                {
                    var sr = opponentSeat.GetComponent<SpriteRenderer>();
                    if (sr != null)
                    {
                        var c = sr.color;
                        c.a = 1f;
                        sr.color = c;
                    }
                }
            }

            _defuseCharges = sharedDefusePerMatch;
            hud.SetDefuseCharges(_defuseCharges);

            int alive = CountAlive();
            int initial = RollInitialCountdown(alive);
            _holderId = PickRandomAliveId();

            bomb.Arm(initial, viewerIsHolder: IsPlayerHolder());
            MoveBombToHolder();

            hud.SetBroadcast($"新炸弹 {initial} · {_participants[IndexOf(_holderId)].DisplayName} 持有");
            Debug.Log($"[Battle] Round start countdown={initial} holder={_participants[IndexOf(_holderId)].DisplayName}");

            BeginHolderTurn();
        }

        void BeginHolderTurn()
        {
            _busy = false;
            bomb.SetViewerIsHolder(IsPlayerHolder());
            MoveBombToHolder();

            var holder = _participants[IndexOf(_holderId)];
            if (holder.IsPlayer)
            {
                _phase = Phase.AwaitingPlayerAction;
                _decisionDeadline = Time.time + decisionSeconds;
                hud.SetActionsInteractable(true, _defuseCharges > 0);
                hud.SetDecisionTimer(decisionSeconds, decisionSeconds);
                hud.SetBroadcast($"你的回合 · 炸弹 {bomb.Logic.Countdown}");
            }
            else
            {
                _phase = Phase.AwaitingAiAction;
                hud.SetActionsInteractable(false, false);
                hud.SetDecisionTimerVisible(false);
                hud.SetBroadcast($"等待 {holder.DisplayName} 行动…");
                RequestAiMove(holder);
            }
        }

        void RequestAiMove(BattleParticipant holder)
        {
            IBattleAi ai = holder.Seat != null
                ? holder.Seat.GetComponentInChildren<IBattleAi>()
                : null;
            if (ai == null)
                ai = placeholderAi;

            var snapshot = BuildSnapshot(holder.Id);
            ai.Decide(snapshot, move =>
            {
                if (_phase != Phase.AwaitingAiAction)
                    return;
                StartCoroutine(ResolveMove(holder.Id, move.Action, move.TargetId, fromTimeout: false));
            });
        }

        void OnPlayerActionClicked(BombAction action)
        {
            if (_phase != Phase.AwaitingPlayerAction || _busy)
                return;
            if (action == BombAction.Defuse && _defuseCharges <= 0)
                return;

            // 2P：唯一合法目标自动选中
            int targetId = FindOnlyOtherAlive(_holderId);
            if (targetId < 0)
                return;

            StartCoroutine(ResolveMove(_holderId, action, targetId, fromTimeout: false));
        }

        void ForceTimeoutPass()
        {
            if (_phase != Phase.AwaitingPlayerAction || _busy)
                return;

            int targetId = PickRandomAliveExcept(_holderId);
            if (targetId < 0)
                return;

            hud.SetBroadcast("超时！强制传");
            StartCoroutine(ResolveMove(_holderId, BombAction.Pass, targetId, fromTimeout: true));
        }

        IEnumerator ResolveMove(int actorId, BombAction action, int targetId, bool fromTimeout)
        {
            if (_busy)
                yield break;

            _busy = true;
            _phase = Phase.Resolving;
            hud.SetActionsInteractable(false, false);
            hud.SetDecisionTimerVisible(false);

            if (!_participants[IndexOf(actorId)].IsAlive || actorId != _holderId)
            {
                _busy = false;
                BeginHolderTurn();
                yield break;
            }

            if (targetId == actorId || !IsAlive(targetId))
                targetId = PickRandomAliveExcept(actorId);

            if (action == BombAction.Defuse)
            {
                if (_defuseCharges <= 0)
                    action = BombAction.Pass;
                else
                {
                    _defuseCharges--;
                    hud.SetDefuseCharges(_defuseCharges);
                }
            }

            var actor = _participants[IndexOf(actorId)];
            BombActionResult result;
            switch (action)
            {
                case BombAction.Shove:
                    result = bomb.Shove(slipChance);
                    break;
                case BombAction.Defuse:
                    result = bomb.Defuse();
                    break;
                default:
                    result = bomb.Pass();
                    break;
            }

            string line = $"{actor.DisplayName} {ActionLabel(action)}";
            if (fromTimeout) line += "（超时）";
            if (result.Slipped) line += " · 手滑！";
            if (action == BombAction.Defuse) line += "（全场广播）";
            hud.SetBroadcast(line);
            Debug.Log($"[Battle] {line} -> {result.CountdownAfter} transfer={result.ShouldTransfer}");

            yield return new WaitForSeconds(0.35f);

            if (result.Slipped)
            {
                // 手滑弹回 = 一次接手判定
                if (result.ExplodedOnSelfAfterSlip)
                {
                    yield return HandleExplosion(actorId);
                    yield break;
                }

                // 仍持有，继续自己的回合
                _busy = false;
                BeginHolderTurn();
                yield break;
            }

            // 移交
            _holderId = targetId;
            MoveBombToHolder();
            bomb.SetViewerIsHolder(IsPlayerHolder());

            if (bomb.CheckExplodeOnReceive())
            {
                yield return HandleExplosion(targetId);
                yield break;
            }

            yield return new WaitForSeconds(0.2f);
            _busy = false;
            BeginHolderTurn();
        }

        IEnumerator HandleExplosion(int victimId)
        {
            var victim = _participants[IndexOf(victimId)];
            hud.SetBroadcast($"{victim.DisplayName} 挨炸！");
            Debug.Log($"[Battle] EXPLODE {victim.DisplayName}");
            yield return new WaitForSeconds(0.6f);

            if (victim.IsPlayer)
            {
                _hearts--;
                hud.SetHearts(_hearts);
                if (_hearts <= 0)
                {
                    _phase = Phase.MatchOver;
                    hud.SetBroadcast("心已耗尽 · Run 结束");
                    hud.SetActionsInteractable(false, false);
                    _busy = false;
                    yield break;
                }

                hud.SetBroadcast("你挨炸了 · 本场重打");
                yield return new WaitForSeconds(0.8f);
                _busy = false;
                StartFightRound(reviveAll: true);
                yield break;
            }

            // AI 出局
            victim.IsAlive = false;
            if (opponentSeat != null)
            {
                var sr = opponentSeat.GetComponent<SpriteRenderer>();
                if (sr != null)
                {
                    var c = sr.color;
                    c.a = 0.35f;
                    sr.color = c;
                }
            }

            if (CountAlive() <= 1 && IsAlive(0))
            {
                _phase = Phase.MatchOver;
                hud.SetBroadcast("胜利！");
                hud.SetActionsInteractable(false, false);
                _busy = false;
                yield break;
            }

            // 爆炸后重置：新炸弹，残局继续
            hud.SetBroadcast("换新炸弹…");
            yield return new WaitForSeconds(0.5f);
            _busy = false;
            StartFightRound(reviveAll: false);
        }

        BattleSnapshot BuildSnapshot(int selfId)
        {
            var list = new List<BattleParticipantInfo>(_participants.Count);
            for (int i = 0; i < _participants.Count; i++)
                list.Add(new BattleParticipantInfo(_participants[i]));

            return new BattleSnapshot
            {
                SelfId = selfId,
                HolderId = _holderId,
                SharedDefuseCharges = _defuseCharges,
                AliveCount = CountAlive(),
                HolderCountdown = selfId == _holderId ? bomb.Logic.Countdown : (int?)null,
                AppearanceRatio = bomb.Logic.RemainingRatio,
                AppearanceTier = bomb.Logic.GetAppearanceTier(),
                Participants = list
            };
        }

        void MoveBombToHolder()
        {
            var seat = _participants[IndexOf(_holderId)].Seat;
            if (seat == null || bomb == null)
                return;

            bomb.transform.position = seat.position + Vector3.down * 0.15f;
            var view = bomb.GetComponent<BombView>();
            if (view != null)
                view.CaptureRestPosition();
        }

        int RollInitialCountdown(int aliveCount)
        {
            Vector2Int range = range2;
            if (aliveCount >= 4) range = range4;
            else if (aliveCount == 3) range = range3;
            return Random.Range(range.x, range.y + 1);
        }

        int CountAlive()
        {
            int n = 0;
            for (int i = 0; i < _participants.Count; i++)
                if (_participants[i].IsAlive) n++;
            return n;
        }

        bool IsAlive(int id)
        {
            int i = IndexOf(id);
            return i >= 0 && _participants[i].IsAlive;
        }

        bool IsPlayerHolder() => _participants[IndexOf(_holderId)].IsPlayer;

        int IndexOf(int id)
        {
            for (int i = 0; i < _participants.Count; i++)
                if (_participants[i].Id == id) return i;
            return -1;
        }

        int PickRandomAliveId()
        {
            var ids = new List<int>();
            for (int i = 0; i < _participants.Count; i++)
                if (_participants[i].IsAlive) ids.Add(_participants[i].Id);
            return ids[Random.Range(0, ids.Count)];
        }

        int PickRandomAliveExcept(int exceptId)
        {
            var ids = new List<int>();
            for (int i = 0; i < _participants.Count; i++)
                if (_participants[i].IsAlive && _participants[i].Id != exceptId)
                    ids.Add(_participants[i].Id);
            if (ids.Count == 0) return -1;
            return ids[Random.Range(0, ids.Count)];
        }

        int FindOnlyOtherAlive(int exceptId)
        {
            int found = -1;
            for (int i = 0; i < _participants.Count; i++)
            {
                if (!_participants[i].IsAlive || _participants[i].Id == exceptId)
                    continue;
                if (found >= 0)
                    return found; // 多于一个时仍返回第一个；3P+ 再做点选
                found = _participants[i].Id;
            }
            return found;
        }

        static string ActionLabel(BombAction a)
        {
            switch (a)
            {
                case BombAction.Shove: return "塞了";
                case BombAction.Defuse: return "拆了";
                default: return "传了";
            }
        }
    }
}
