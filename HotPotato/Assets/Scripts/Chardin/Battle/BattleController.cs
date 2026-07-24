using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using Ruilin;

namespace Chardin
{
    /// <summary>
    /// 单场对局状态机。
    /// 圆桌顺序手动拖入 clockwiseOrder；「传」只顺时针传给下一位存活者。
    /// 「塞」进入瞄准：悬停显示箭头，左键确认，右键取消。
    /// </summary>
    public sealed class BattleController : MonoBehaviour
    {
        public enum Phase
        {
            Boot,
            AwaitingPlayerAction,
            AimingShove,
            AwaitingAiAction,
            Resolving,
            MatchOver
        }

        [Header("Refs")]
        [SerializeField] Bomb bomb;
        [SerializeField] BattleHud hud;
        [SerializeField] ShoveAimController shoveAim;
        [SerializeField] PlaceholderBattleAi placeholderAi;
        [SerializeField] BombTransferFx transferFx;

        [Header("圆桌顺序（顺时针，手动拖入 TableSeat / Enemy）")]
        [SerializeField] List<TableSeat> clockwiseOrder = new List<TableSeat>();

        [Header("Match")]
        [SerializeField] int startingHearts = 3;
        [SerializeField] int sharedDefusePerMatch = 3;
        [SerializeField] float decisionSeconds = 10f;
        [SerializeField] float slipChance = 0.2f;

        [Header("初始倒计时随机区间（按存活人数，x=最小 y=最大，含两端）")]
        [FormerlySerializedAs("range2")]
        [SerializeField] Vector2Int countdownRange2Alive = new Vector2Int(8, 12);
        [FormerlySerializedAs("range3")]
        [SerializeField] Vector2Int countdownRange3Alive = new Vector2Int(10, 16);
        [FormerlySerializedAs("range4")]
        [SerializeField] Vector2Int countdownRange4PlusAlive = new Vector2Int(14, 20);

        int _holderIndex;
        int _previousHolderIndex = -1;
        int _passDirection = 1;
        int _hearts;
        int _defuseCharges;
        float _decisionDeadline;
        bool _busy;
        Phase _phase = Phase.Boot;
        BombAction? _pendingPlayerAction;

        public Phase CurrentPhase => _phase;
        public int DefuseCharges => _defuseCharges;
        public IReadOnlyList<TableSeat> ClockwiseOrder => clockwiseOrder;
        public Bomb CurrentBomb => bomb;

        void Awake()
        {
            if (bomb == null)
                bomb = GetComponentInChildren<Bomb>(true);
            if (hud == null)
                hud = GetComponent<BattleHud>() ?? gameObject.AddComponent<BattleHud>();
            if (shoveAim == null)
                shoveAim = GetComponent<ShoveAimController>() ?? gameObject.AddComponent<ShoveAimController>();
            if (transferFx == null && bomb != null)
                transferFx = bomb.GetComponent<BombTransferFx>() ?? bomb.gameObject.AddComponent<BombTransferFx>();

            if (clockwiseOrder != null)
            {
                for (int i = 0; i < clockwiseOrder.Count; i++)
                {
                    if (clockwiseOrder[i] != null)
                        clockwiseOrder[i].EnsureBombPosition();
                }
            }

            var debug = bomb != null ? bomb.GetComponent<BombDebugDriver>() : null;
            if (debug != null)
                debug.enabled = false;

            hud.BindFromHierarchy(transform);
            hud.ActionClicked += OnPlayerActionClicked;
            shoveAim.Confirmed += OnShoveConfirmed;
            shoveAim.Cancelled += OnShoveCancelled;
        }

        void OnDestroy()
        {
            if (hud != null)
                hud.ActionClicked -= OnPlayerActionClicked;
            if (shoveAim != null)
            {
                shoveAim.Confirmed -= OnShoveConfirmed;
                shoveAim.Cancelled -= OnShoveCancelled;
            }
        }

        void Start()
        {
            BeginMatch();
        }

        void Update()
        {
            if (_phase == Phase.AimingShove)
            {
                // 瞄准期间仍走决策超时
                float remainingAim = _decisionDeadline - Time.time;
                hud.SetDecisionTimer(remainingAim, decisionSeconds);
                if (remainingAim <= 0f)
                {
                    shoveAim.CancelAim();
                    ForceTimeoutPass();
                }
                return;
            }

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

        public void BeginMatch()
        {
            StopAllCoroutines();
            _busy = false;
            _pendingPlayerAction = null;
            _hearts = startingHearts;
            _passDirection = 1;
            _previousHolderIndex = -1;

            if (clockwiseOrder == null || clockwiseOrder.Count < 2)
            {
                Debug.LogError("[Battle] clockwiseOrder 需要至少 2 个 TableSeat（含玩家）");
                _phase = Phase.MatchOver;
                return;
            }

            for (int i = 0; i < clockwiseOrder.Count; i++)
            {
                if (clockwiseOrder[i] == null)
                {
                    Debug.LogError($"[Battle] clockwiseOrder[{i}] 为空");
                    _phase = Phase.MatchOver;
                    return;
                }
            }

            SyncOpponentNameLabels();

            hud.SetHearts(_hearts);
            StartFightRound(reviveAll: true);
        }

        void SyncOpponentNameLabels()
        {
            var enemies = new List<TableSeat>();
            for (int i = 0; i < clockwiseOrder.Count; i++)
            {
                var s = clockwiseOrder[i];
                if (s != null && !s.IsPlayer)
                    enemies.Add(s);
            }

            // 按世界坐标从左到右，和 UI 上的 OpponentName 标签对齐
            enemies.Sort((a, b) => a.BombAnchor.position.x.CompareTo(b.BombAnchor.position.x));
            var names = new List<string>(enemies.Count);
            for (int i = 0; i < enemies.Count; i++)
                names.Add(enemies[i].DisplayName);
            hud.SetOpponentNames(names);
        }

        void StartFightRound(bool reviveAll)
        {
            if (reviveAll)
            {
                for (int i = 0; i < clockwiseOrder.Count; i++)
                    clockwiseOrder[i].ResetSeat();
            }

            _defuseCharges = sharedDefusePerMatch + RunInventory.DefuseBonus;
            hud.SetDefuseCharges(_defuseCharges);

            int alive = CountAlive();
            int initial = RollInitialCountdown(alive);
            _holderIndex = PickRandomAliveIndex();

            bomb.Arm(initial, viewerIsHolder: IsPlayerHolder());
            MoveBombToHolder();

            var holder = clockwiseOrder[_holderIndex];
            hud.SetBroadcast($"新炸弹 · {holder.DisplayName} 持有");
            Debug.Log($"[Battle] Round start countdown={initial} holder={holder.DisplayName}");

            BeginHolderTurn();
        }

        void BeginHolderTurn()
        {
            _busy = false;
            _pendingPlayerAction = null;
            bomb.SetViewerIsHolder(IsPlayerHolder());
            MoveBombToHolder();

            var holder = clockwiseOrder[_holderIndex];
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

        void RequestAiMove(TableSeat holder)
        {
            IBattleAi ai = holder.GetAi() ?? placeholderAi;
            if (ai == null)
            {
                Debug.LogError($"[Battle] {holder.DisplayName} 没有 IBattleAi");
                return;
            }

            var snapshot = BuildSnapshot(holder);
            ai.Decide(snapshot, move =>
            {
                if (_phase != Phase.AwaitingAiAction)
                    return;

                int targetIndex = ResolveAiTarget(holder, move);
                StartCoroutine(ResolveMove(_holderIndex, move.Action, targetIndex, fromTimeout: false));
            });
        }

        int ResolveAiTarget(TableSeat holder, AiMove move)
        {
            // 传 / 拆：强制方向上下一位；塞：可用自选目标
            if (move.Action == BombAction.Pass || move.Action == BombAction.Defuse)
                return GetClockwiseNextIndex(_holderIndex);

            int idx = FindIndexById(move.TargetId);
            if (idx >= 0 && idx != _holderIndex && clockwiseOrder[idx].IsAlive)
                return idx;
            return GetClockwiseNextIndex(_holderIndex);
        }

        void OnPlayerActionClicked(BombAction action)
        {
            if (_phase != Phase.AwaitingPlayerAction || _busy)
                return;
            if (action == BombAction.Defuse && _defuseCharges <= 0)
                return;

            if (action == BombAction.Pass)
            {
                int next = GetClockwiseNextIndex(_holderIndex);
                if (next < 0) return;
                StartCoroutine(ResolveMove(_holderIndex, BombAction.Pass, next, fromTimeout: false));
                return;
            }

            if (action == BombAction.Defuse)
            {
                // 拆后移交：与传一样，强制方向上下一位
                int next = GetClockwiseNextIndex(_holderIndex);
                if (next < 0) return;
                StartCoroutine(ResolveMove(_holderIndex, BombAction.Defuse, next, fromTimeout: false));
                return;
            }

            // 塞：进入瞄准
            _pendingPlayerAction = BombAction.Shove;
            _phase = Phase.AimingShove;
            hud.SetActionsInteractable(false, false);
            hud.SetBroadcast("塞：指向敌人，左键确认，右键取消");
            shoveAim.BeginAim(GetAliveEnemiesExceptHolder(), bomb.transform);
        }

        void OnShoveConfirmed(TableSeat target)
        {
            if (_phase != Phase.AimingShove || _pendingPlayerAction != BombAction.Shove)
                return;
            int targetIndex = clockwiseOrder.IndexOf(target);
            if (targetIndex < 0 || !target.IsAlive)
            {
                OnShoveCancelled();
                return;
            }
            StartCoroutine(ResolveMove(_holderIndex, BombAction.Shove, targetIndex, fromTimeout: false));
        }

        void OnShoveCancelled()
        {
            if (_phase != Phase.AimingShove)
                return;
            _pendingPlayerAction = null;
            _phase = Phase.AwaitingPlayerAction;
            hud.SetActionsInteractable(true, _defuseCharges > 0);
            hud.SetBroadcast($"你的回合 · 炸弹 {bomb.Logic.Countdown}");
        }

        void ForceTimeoutPass()
        {
            if ((_phase != Phase.AwaitingPlayerAction && _phase != Phase.AimingShove) || _busy)
                return;

            int next = GetClockwiseNextIndex(_holderIndex);
            if (next < 0)
                return;

            hud.SetBroadcast("超时！强制传");
            StartCoroutine(ResolveMove(_holderIndex, BombAction.Pass, next, fromTimeout: true));
        }

        IEnumerator ResolveMove(int actorIndex, BombAction action, int targetIndex, bool fromTimeout)
        {
            if (_busy && _phase == Phase.Resolving)
                yield break;

            _busy = true;
            _phase = Phase.Resolving;
            _pendingPlayerAction = null;
            hud.SetActionsInteractable(false, false);
            hud.SetDecisionTimerVisible(false);

            if (actorIndex < 0 || actorIndex >= clockwiseOrder.Count)
                yield break;
            if (!clockwiseOrder[actorIndex].IsAlive || actorIndex != _holderIndex)
            {
                _busy = false;
                BeginHolderTurn();
                yield break;
            }

            if (targetIndex < 0 || targetIndex >= clockwiseOrder.Count
                || targetIndex == actorIndex || !clockwiseOrder[targetIndex].IsAlive)
            {
                targetIndex = GetClockwiseNextIndex(actorIndex);
            }

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

            // 传 / 拆：强制改成方向上下一位
            if (action == BombAction.Pass || action == BombAction.Defuse)
                targetIndex = GetClockwiseNextIndex(actorIndex);

            var actor = clockwiseOrder[actorIndex];
            BombActionResult result;
            switch (action)
            {
                case BombAction.Shove:
                    result = bomb.Shove(actor.IsPlayer ? RunInventory.PlayerSlipChance : slipChance);
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

            // 刷新炸弹外观（倒计时已变），再播程序动画
            bomb.SetViewerIsHolder(IsPlayerHolder());

            Vector3 fromPos = actor.BombAnchor.position;
            if (bomb != null)
                fromPos = bomb.transform.position;

            if (result.Slipped)
            {
                if (transferFx != null)
                    yield return transferFx.PlaySlip(bomb.transform, "-2");
                else
                    yield return new WaitForSeconds(0.35f);

                if (result.ExplodedOnSelfAfterSlip)
                {
                    yield return HandleExplosion(actorIndex);
                    yield break;
                }

                MoveBombToHolder();
                _busy = false;
                BeginHolderTurn();
                yield break;
            }

            TableSeat target = clockwiseOrder[targetIndex];
            Vector3 toPos = target != null ? target.BombAnchor.position : fromPos;

            if (transferFx != null)
            {
                if (action == BombAction.Shove)
                    yield return transferFx.PlayShove(bomb.transform, fromPos, toPos, target, "-2");
                else if (action == BombAction.Defuse)
                    yield return transferFx.PlayDefuseTransfer(bomb.transform, fromPos, toPos, target);
                else
                    yield return transferFx.PlayPass(bomb.transform, fromPos, toPos, target, "-1");
            }
            else
            {
                yield return new WaitForSeconds(0.35f);
            }

            _previousHolderIndex = actorIndex;
            _holderIndex = targetIndex;
            MoveBombToHolder();
            bomb.SetViewerIsHolder(IsPlayerHolder());

            if (bomb.CheckExplodeOnReceive())
            {
                yield return HandleExplosion(targetIndex);
                yield break;
            }

            yield return new WaitForSeconds(0.12f);
            _busy = false;
            BeginHolderTurn();
        }

        IEnumerator HandleExplosion(int victimIndex)
        {
            var victim = clockwiseOrder[victimIndex];
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
                    hud.SetDecisionTimerVisible(false);
                    _busy = false;
                    yield break;
                }

                hud.SetBroadcast("你挨炸了 · 本场重打");
                yield return new WaitForSeconds(0.8f);
                _busy = false;
                StartFightRound(reviveAll: true);
                yield break;
            }

            victim.SetAlive(false);

            if (CountAlive() <= 1 && PlayerStillAlive())
            {
                _phase = Phase.MatchOver;
                hud.SetBroadcast("胜利！");
                hud.SetActionsInteractable(false, false);
                hud.SetDecisionTimerVisible(false);
                _busy = false;
                yield break;
            }

            hud.SetBroadcast("换新炸弹…");
            yield return new WaitForSeconds(0.5f);
            _busy = false;
            StartFightRound(reviveAll: false);
        }

        BattleSnapshot BuildSnapshot(TableSeat self)
        {
            var list = new List<BattleParticipantInfo>(clockwiseOrder.Count);
            for (int i = 0; i < clockwiseOrder.Count; i++)
            {
                var s = clockwiseOrder[i];
                list.Add(new BattleParticipantInfo(
                    i, s.DisplayName, s.IsPlayer, s.IsAlive, DetectPersonality(s)));
            }

            bool holding = clockwiseOrder[_holderIndex] == self;
            return new BattleSnapshot
            {
                SelfId = clockwiseOrder.IndexOf(self),
                HolderId = _holderIndex,
                SharedDefuseCharges = _defuseCharges,
                AliveCount = CountAlive(),
                HolderCountdown = holding ? bomb.Logic.Countdown : (int?)null,
                AppearanceRatio = bomb.Logic.RemainingRatio,
                AppearanceTier = bomb.Logic.GetAppearanceTier(),
                PassDirection = _passDirection >= 0 ? 1 : -1,
                Participants = list
            };
        }

        static SeatPersonality DetectPersonality(TableSeat seat)
        {
            if (seat == null)
                return SeatPersonality.Unknown;
            if (seat.IsPlayer)
                return SeatPersonality.Player;
            if (seat.GetComponent<WormAi>() != null)
                return SeatPersonality.Worm;
            if (seat.GetComponent<AshAi>() != null)
                return SeatPersonality.Ash;
            if (seat.GetComponent<SnakeAi>() != null)
                return SeatPersonality.Snake;
            return SeatPersonality.Unknown;
        }

        void MoveBombToHolder()
        {
            var seat = clockwiseOrder[_holderIndex];
            if (seat == null || bomb == null)
                return;

            bomb.transform.position = seat.BombAnchor.position;
            var view = bomb.GetComponent<BombView>();
            if (view != null)
                view.CaptureRestPosition();
        }

        List<TableSeat> GetAliveEnemiesExceptHolder()
        {
            var list = new List<TableSeat>();
            for (int i = 0; i < clockwiseOrder.Count; i++)
            {
                var s = clockwiseOrder[i];
                if (s == null || !s.IsAlive || s.IsPlayer || i == _holderIndex)
                    continue;
                list.Add(s);
            }
            return list;
        }

        int GetClockwiseNextIndex(int fromIndex)
        {
            if (clockwiseOrder.Count == 0)
                return -1;
            for (int step = 1; step <= clockwiseOrder.Count; step++)
            {
                int idx = (fromIndex + _passDirection * step) % clockwiseOrder.Count;
                if (idx < 0) idx += clockwiseOrder.Count;
                if (clockwiseOrder[idx] != null && clockwiseOrder[idx].IsAlive)
                    return idx;
            }
            return -1;
        }

        public bool TryUsePeek()
        {
            if (_phase != Phase.AwaitingPlayerAction || !IsPlayerHolder())
                return false;
            if (!RunInventory.TryConsume(ItemId.Peek))
                return false;
            hud.SetBroadcast($"窥视：当前精确倒计时为 {bomb.Logic.Countdown}");
            return true;
        }

        public bool TryUseFateDie()
        {
            if (_phase != Phase.AwaitingPlayerAction || _busy || !IsPlayerHolder())
                return false;
            if (!RunInventory.TryConsume(ItemId.FateDie))
                return false;
            StartCoroutine(ResolveFateDie());
            return true;
        }

        IEnumerator ResolveFateDie()
        {
            _busy = true;
            _phase = Phase.Resolving;
            hud.SetActionsInteractable(false, false);
            int roll = UnityEngine.Random.Range(1, 7);
            bomb.AddCountdown(-roll);
            hud.SetBroadcast($"命运骰：掷出 {roll}，剩余 {bomb.Logic.Countdown}");
            yield return new WaitForSeconds(0.35f);

            if (bomb.CheckExplodeOnReceive())
            {
                yield return HandleExplosion(_holderIndex);
                yield break;
            }

            int from = _holderIndex;
            _previousHolderIndex = from;
            _holderIndex = GetClockwiseNextIndex(from);
            MoveBombToHolder();
            bomb.SetViewerIsHolder(IsPlayerHolder());
            _busy = false;
            BeginHolderTurn();
        }

        public bool TryUseReflectGlove()
        {
            if (_phase != Phase.AwaitingPlayerAction || _busy || !IsPlayerHolder())
                return false;
            if (_previousHolderIndex < 0 || _previousHolderIndex >= clockwiseOrder.Count
                || !clockwiseOrder[_previousHolderIndex].IsAlive)
                return false;
            if (!RunInventory.TryConsume(ItemId.ReflectGlove))
                return false;
            StartCoroutine(ResolveReflectGlove());
            return true;
        }

        IEnumerator ResolveReflectGlove()
        {
            _busy = true;
            _phase = Phase.Resolving;
            hud.SetActionsInteractable(false, false);
            int playerIndex = _holderIndex;
            int target = _previousHolderIndex;
            _passDirection *= -1;
            _previousHolderIndex = playerIndex;
            _holderIndex = target;
            MoveBombToHolder();
            bomb.SetViewerIsHolder(IsPlayerHolder());
            hud.SetBroadcast("反弹手套：炸弹原路弹回，传递方向反转！");
            yield return new WaitForSeconds(0.25f);
            _busy = false;
            BeginHolderTurn();
        }

        int PickRandomAliveIndex()
        {
            var ids = new List<int>();
            for (int i = 0; i < clockwiseOrder.Count; i++)
                if (clockwiseOrder[i] != null && clockwiseOrder[i].IsAlive)
                    ids.Add(i);
            if (ids.Count == 0) return 0;
            return ids[UnityEngine.Random.Range(0, ids.Count)];
        }

        int FindIndexById(int id)
        {
            if (id < 0 || id >= clockwiseOrder.Count)
                return -1;
            return id; // AiMove.TargetId 约定为 clockwise index
        }

        int CountAlive()
        {
            int n = 0;
            for (int i = 0; i < clockwiseOrder.Count; i++)
                if (clockwiseOrder[i] != null && clockwiseOrder[i].IsAlive) n++;
            return n;
        }

        bool IsPlayerHolder() => clockwiseOrder[_holderIndex] != null && clockwiseOrder[_holderIndex].IsPlayer;

        bool PlayerStillAlive()
        {
            for (int i = 0; i < clockwiseOrder.Count; i++)
                if (clockwiseOrder[i] != null && clockwiseOrder[i].IsPlayer && clockwiseOrder[i].IsAlive)
                    return true;
            return false;
        }

        int RollInitialCountdown(int aliveCount)
        {
            Vector2Int range = countdownRange2Alive;
            if (aliveCount >= 4) range = countdownRange4PlusAlive;
            else if (aliveCount == 3) range = countdownRange3Alive;
            int min = Mathf.Min(range.x, range.y);
            int max = Mathf.Max(range.x, range.y);
            return UnityEngine.Random.Range(min, max + 1);
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
