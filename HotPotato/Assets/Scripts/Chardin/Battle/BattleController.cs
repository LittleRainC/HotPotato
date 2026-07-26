using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
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
        bool _reflectGloveArmed;
        Phase _phase = Phase.Boot;
        BombAction? _pendingPlayerAction;

        public Phase CurrentPhase => _phase;
        public int DefuseCharges => _defuseCharges;
        public IReadOnlyList<TableSeat> ClockwiseOrder => clockwiseOrder;
        public Bomb CurrentBomb => bomb;

        /// <summary>Tutorial：强制玩家开局持弹。</summary>
        public bool ForcePlayerFirstHolder { get; set; }

        /// <summary>玩家行动真正开始结算时抛出（含塞确认后）。</summary>
        public event Action<BombAction> PlayerActionResolved;

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
            if (SceneManager.GetActiveScene().name == "Tutorial")
            {
                ForcePlayerFirstHolder = true;
                TutorialDirector.TryAttachToBattle();
            }
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
            _reflectGloveArmed = false;
            _hearts = startingHearts;
            _passDirection = 1;
            _previousHolderIndex = -1;
            AudioManager.Ensure().PlayBattleBgm();

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

            // 按世界坐标从左到右，和 UI 上的 OpponentName 标签对齐；阵亡者留空以隐藏名牌
            enemies.Sort((a, b) => a.BombAnchor.position.x.CompareTo(b.BombAnchor.position.x));
            var names = new List<string>(enemies.Count);
            for (int i = 0; i < enemies.Count; i++)
                names.Add(enemies[i].IsAlive ? enemies[i].DisplayName : string.Empty);
            hud.SetOpponentNames(names);
        }

        void StartFightRound(bool reviveAll)
        {
            if (reviveAll)
            {
                for (int i = 0; i < clockwiseOrder.Count; i++)
                    clockwiseOrder[i].ResetSeat();
                SyncOpponentNameLabels();
            }

            _reflectGloveArmed = false;
            _defuseCharges = sharedDefusePerMatch + RunInventory.DefuseBonus;
            hud.SetDefuseCharges(_defuseCharges);

            int alive = CountAlive();
            int initial = RollInitialCountdown(alive);
            if (ForcePlayerFirstHolder)
            {
                int playerIdx = FindPlayerSeatIndex();
                _holderIndex = playerIdx >= 0 ? playerIdx : PickRandomAliveIndex();
            }
            else
            {
                _holderIndex = PickRandomAliveIndex();
            }

            bomb.Arm(initial, viewerIsHolder: IsPlayerHolder());
            MoveBombToHolder();
            SyncTickAudio();

            var holder = clockwiseOrder[_holderIndex];
            hud.SetBroadcast($"New bomb · {holder.DisplayName} has it");
            Debug.Log($"[Battle] Round start countdown={initial} holder={holder.DisplayName}");

            BeginHolderTurn();
        }

        void SyncTickAudio(bool armed = true)
        {
            if (bomb == null || !armed)
            {
                AudioManager.Ensure().StopTick();
                return;
            }

            AudioManager.Ensure().SyncBombTick(bomb.Logic.GetAppearanceTier(), true);
        }

        void BeginHolderTurn()
        {
            _busy = false;
            _pendingPlayerAction = null;
            bomb.SetViewerIsHolder(IsPlayerHolder());
            MoveBombToHolder();
            SyncTickAudio();

            var holder = clockwiseOrder[_holderIndex];
            if (holder.IsPlayer)
            {
                _phase = Phase.AwaitingPlayerAction;
                _decisionDeadline = Time.time + decisionSeconds;
                hud.SetActionsInteractable(true, _defuseCharges > 0);
                hud.SetDecisionTimer(decisionSeconds, decisionSeconds);
                hud.SetBroadcast($"Your turn · Bomb {bomb.Logic.Countdown}");
            }
            else
            {
                _phase = Phase.AwaitingAiAction;
                hud.SetActionsInteractable(false, false);
                hud.SetDecisionTimerVisible(false);
                hud.SetBroadcast($"Waiting for {holder.DisplayName}…");
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
            hud.SetBroadcast("SHOVE: Aim at an enemy · Left-click to confirm · Right-click to cancel");
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
            hud.SetBroadcast($"Your turn · Bomb {bomb.Logic.Countdown}");
        }

        void ForceTimeoutPass()
        {
            if ((_phase != Phase.AwaitingPlayerAction && _phase != Phase.AimingShove) || _busy)
                return;

            int next = GetClockwiseNextIndex(_holderIndex);
            if (next < 0)
                return;

            hud.SetBroadcast("Time's up! Forced pass");
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

            if (clockwiseOrder[actorIndex] != null && clockwiseOrder[actorIndex].IsPlayer)
                PlayerActionResolved?.Invoke(action);

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
            if (fromTimeout) line += " (timed out)";
            if (result.Slipped) line += " · Fumbled!";
            if (action == BombAction.Defuse) line += " (broadcast to all)";
            hud.SetBroadcast(line);
            Debug.Log($"[Battle] {line} -> {result.CountdownAfter} transfer={result.ShouldTransfer}");
            AudioManager.Ensure().PlayActionSfx(action, result.Slipped);
            SyncTickAudio();

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
                SyncTickAudio();
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

            // 反弹手套：炸弹到玩家位 → 跳过该回合，原路弹回；≤0 也不在玩家手上爆，弹回后再爆
            if (IsPlayerHolder() && _reflectGloveArmed && TryGetAliveBounceTarget(out int bounceTo))
            {
                yield return ResolveReflectBounce(bounceTo);
                yield break;
            }

            if (bomb.CheckExplodeOnReceive())
            {
                yield return HandleExplosion(targetIndex);
                yield break;
            }

            SyncTickAudio();
            yield return new WaitForSeconds(0.12f);
            _busy = false;
            BeginHolderTurn();
        }

        bool TryGetAliveBounceTarget(out int bounceTo)
        {
            bounceTo = _previousHolderIndex;
            if (bounceTo >= 0 && bounceTo < clockwiseOrder.Count
                && clockwiseOrder[bounceTo] != null && clockwiseOrder[bounceTo].IsAlive
                && bounceTo != _holderIndex)
                return true;

            bounceTo = GetClockwiseNextIndex(_holderIndex);
            return bounceTo >= 0;
        }

        IEnumerator ResolveReflectBounce(int bounceTo)
        {
            _reflectGloveArmed = false;
            int playerIndex = _holderIndex;

            hud.SetBroadcast("BOUNCE GLOVE: Turn skipped · Bomb returned to sender!");
            yield return new WaitForSeconds(0.12f);

            TableSeat fromSeat = clockwiseOrder[playerIndex];
            TableSeat toSeat = clockwiseOrder[bounceTo];
            Vector3 fromPos = bomb != null ? bomb.transform.position : fromSeat.BombAnchor.position;
            Vector3 toPos = toSeat != null ? toSeat.BombAnchor.position : fromPos;

            if (transferFx != null)
                yield return transferFx.PlayPass(bomb.transform, fromPos, toPos, toSeat, null);
            else
                yield return new WaitForSeconds(0.35f);

            _previousHolderIndex = playerIndex;
            _holderIndex = bounceTo;
            MoveBombToHolder();
            bomb.SetViewerIsHolder(IsPlayerHolder());

            if (bomb.CheckExplodeOnReceive())
            {
                yield return HandleExplosion(bounceTo);
                yield break;
            }

            SyncTickAudio();
            yield return new WaitForSeconds(0.12f);
            _busy = false;
            BeginHolderTurn();
        }

        IEnumerator HandleExplosion(int victimIndex)
        {
            var victim = clockwiseOrder[victimIndex];
            Debug.Log($"[Battle] EXPLODE {victim.DisplayName}");
            Vector3 explosionPosition = bomb != null
                ? bomb.transform.position
                : victim.BombAnchor.position;

            SyncTickAudio(armed: false);
            AudioManager.Ensure().PlayExplosion();
            if (!victim.IsPlayer)
                AudioManager.Ensure().PlayDeathVoDelayed(DetectPersonality(victim));

            if (victim.IsPlayer)
            {
                hud.SetBroadcast("The bomb blew up in your hands!");
                yield return hud.PlayFullscreenDamageFlash();
                yield return hud.PlayExplosionAt(explosionPosition);
                if (bomb != null)
                    bomb.SetVisible(false);

                _hearts--;
                hud.SetHearts(_hearts);
                if (_hearts <= 0)
                {
                    _phase = Phase.MatchOver;
                    hud.SetBroadcast("No hearts left · Run over");
                    hud.SetActionsInteractable(false, false);
                    hud.SetDecisionTimerVisible(false);
                    _busy = false;
                    yield break;
                }

                hud.SetBroadcast("You got blown up · Restarting the round");
                yield return new WaitForSeconds(0.45f);
                _busy = false;
                StartFightRound(reviveAll: true);
                yield break;
            }

            hud.SetBroadcast($"{victim.DisplayName} is out");
            if (victim is Enemy enemy)
                yield return enemy.PlayDeathFlash();
            else
                yield return new WaitForSeconds(0.45f);

            yield return hud.PlayExplosionAt(explosionPosition);
            if (bomb != null)
                bomb.SetVisible(false);
            victim.SetAlive(false);
            SyncOpponentNameLabels();

            if (CountAlive() <= 1 && PlayerStillAlive())
            {
                _phase = Phase.MatchOver;
                SyncTickAudio(armed: false);
                if (SceneManager.GetActiveScene().name == "Level5")
                    hud.SetBroadcast("");
                else
                    hud.SetBroadcast("Victory!");
                hud.SetActionsInteractable(false, false);
                hud.SetDecisionTimerVisible(false);
                _busy = false;
                yield break;
            }

            hud.SetBroadcast("Bringing in a new bomb…");
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
            // 窥视：仅敌人持弹（敌人回合）时可查看精确倒计时；玩家回合不可用
            if (_busy || bomb == null || _phase == Phase.MatchOver || _phase == Phase.Boot)
                return false;
            if (IsPlayerHolder() || _phase == Phase.AwaitingPlayerAction || _phase == Phase.AimingShove)
            {
                hud.SetBroadcast("PEEK can only be used while an enemy holds the bomb");
                return false;
            }
            if (_phase != Phase.AwaitingAiAction)
                return false;
            if (!RunInventory.TryConsume(ItemId.Peek))
                return false;
            AudioManager.Ensure().PlayItemUse();
            hud.SetBroadcast($"PEEK: Exact countdown is {bomb.Logic.Countdown}");
            return true;
        }

        /// <summary>窥视是否当前可点（敌人持弹等待 AI）。</summary>
        public bool CanUsePeek()
        {
            return !_busy && bomb != null
                && _phase == Phase.AwaitingAiAction
                && !IsPlayerHolder();
        }

        /// <summary>玩家回合主动道具（命运骰等）。</summary>
        public bool CanUsePlayerTurnItem()
        {
            return !_busy
                && _phase == Phase.AwaitingPlayerAction
                && IsPlayerHolder();
        }

        /// <summary>反弹手套是否已点亮，等待下次炸弹到手。</summary>
        public bool IsReflectGloveArmed => _reflectGloveArmed;

        /// <summary>反弹手套：对局中任意时刻可激活（未就绪时）。</summary>
        public bool CanUseReflectGlove()
        {
            return !_reflectGloveArmed
                && bomb != null
                && _phase != Phase.MatchOver
                && _phase != Phase.Boot;
        }

        public bool TryUseFateDie()
        {
            if (_phase != Phase.AwaitingPlayerAction || _busy || !IsPlayerHolder())
                return false;
            if (!RunInventory.TryConsume(ItemId.FateDie))
                return false;
            AudioManager.Ensure().PlayItemUse();
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
            SyncTickAudio();
            hud.SetBroadcast($"FATE DIE: Rolled {roll} · {bomb.Logic.Countdown} remaining");
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
            SyncTickAudio();
            _busy = false;
            BeginHolderTurn();
        }

        public bool TryUseReflectGlove()
        {
            if (!CanUseReflectGlove())
                return false;
            if (!RunInventory.TryConsume(ItemId.ReflectGlove))
                return false;
            AudioManager.Ensure().PlayItemUse();
            _reflectGloveArmed = true;
            hud.SetBroadcast("BOUNCE GLOVE armed: Your next turn will be skipped and the bomb returned");
            return true;
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

        int FindPlayerSeatIndex()
        {
            for (int i = 0; i < clockwiseOrder.Count; i++)
            {
                if (clockwiseOrder[i] != null && clockwiseOrder[i].IsPlayer)
                    return i;
            }
            return -1;
        }

        /// <summary>Tutorial：把玩家决策倒计时拉满，避免引导时超时。</summary>
        public void SoftExtendPlayerDeadline()
        {
            if (_phase != Phase.AwaitingPlayerAction && _phase != Phase.AimingShove)
                return;
            _decisionDeadline = Time.time + decisionSeconds;
            hud.SetDecisionTimer(decisionSeconds, decisionSeconds);
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
                case BombAction.Shove: return "shoved the bomb";
                case BombAction.Defuse: return "defused the bomb";
                default: return "passed the bomb";
            }
        }
    }
}
