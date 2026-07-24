using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Chardin
{
    /// <summary>
    /// Snake（老千）——公平版进攻：
    /// 优先把压力给玩家，但禁止靠「和 Worm 互塞/互传」磨倒计时再砸脸。
    ///
    /// 硬规则：
    /// 1) C=1 且下家是玩家 → 传
    /// 2) 第一手默认只考虑：对玩家塞 /（允许时）传 / 必死时拆
    /// 3) 禁止第一手塞给其他 AI；仅当「对玩家出手会害死自己」时才允许塞 AI 保命
    /// 4) 评分：炸玩家 > 炸别人 > 炸自己；炸玩家时步数少更好；
    ///    若玩家死前从未轮到过（没摸过炸弹），大惩罚（不当真·最优）
    /// </summary>
    public sealed class SnakeAi : MonoBehaviour, IBattleAi
    {
        struct SimOutcome
        {
            public int DeathId;
            public int Steps;
            public bool PlayerHadTurn; // 爆炸前玩家是否曾以 C>0 持有并行动过
        }

        [SerializeField] Vector2 thinkDelay = new Vector2(1.5f, 2.5f);
        [SerializeField] int maxSimSteps = 48;
        [SerializeField] int wormPanicThreshold = 4;
        [SerializeField] int ashPreferShoveBelowOrEqual = 99;

        const int PlayerSurviveScore = 100;
        const int PlayerDieScore = 0;

        public void Decide(BattleSnapshot snapshot, Action<AiMove> onDecided)
        {
            StartCoroutine(DecideRoutine(snapshot, onDecided));
        }

        IEnumerator DecideRoutine(BattleSnapshot snapshot, Action<AiMove> onDecided)
        {
            yield return new WaitForSeconds(UnityEngine.Random.Range(thinkDelay.x, thinkDelay.y));
            onDecided?.Invoke(ChooseMove(snapshot));
        }

        public AiMove ChooseMove(BattleSnapshot snapshot)
        {
            int countdown = snapshot.HolderCountdown ?? 99;
            int dir = snapshot.PassDirection >= 0 ? 1 : -1;
            var ring = BuildAliveRing(snapshot);
            int self = snapshot.SelfId;
            int player = FindPlayerId(snapshot);
            int next = NextAlive(ring, self, dir);
            int charges = snapshot.SharedDefuseCharges;

            if (countdown < 2 && player >= 0 && next == player)
                return new AiMove { Action = BombAction.Pass, TargetId = next };

            // —— 公平候选：不对 AI 磨刀 ——
            var fairMoves = new List<AiMove>(8);

            // 传：仅当不会「交给玩家让别人收割」的假动作，且不是交给 AI 空转
            // - 下家玩家且 C≥2：不传
            // - 下家是 AI：不传（避免 AI 圈里空转）；C=1 传给 AI 等于处决 AI，允许
            bool nextIsPlayer = player >= 0 && next == player;
            bool nextIsAi = player < 0 || next != player;
            if (nextIsPlayer && countdown < 2)
                fairMoves.Add(new AiMove { Action = BombAction.Pass, TargetId = next });
            else if (nextIsAi && countdown < 2)
                fairMoves.Add(new AiMove { Action = BombAction.Pass, TargetId = next });
            // C≥2 下家玩家：不传
            // C≥2 下家 AI：不传（防互传磨刀）

            if (player >= 0 && player != self && IndexOf(ring, player) >= 0)
                fairMoves.Add(new AiMove { Action = BombAction.Shove, TargetId = player });

            // 用「若只传」粗判是否必死，决定要不要拆
            SimOutcome passProbe = PredictOutcomeAfterMove(
                snapshot, ring, self, countdown, charges, dir,
                new AiMove { Action = BombAction.Pass, TargetId = next });
            bool mustDie = passProbe.DeathId == self;
            if (mustDie && charges > 0)
                fairMoves.Add(new AiMove { Action = BombAction.Defuse, TargetId = next });

            if (fairMoves.Count == 0 && player >= 0)
                fairMoves.Add(new AiMove { Action = BombAction.Shove, TargetId = player });
            if (fairMoves.Count == 0)
                fairMoves.Add(new AiMove { Action = BombAction.Pass, TargetId = next });

            AiMove bestFair = default;
            SimOutcome bestFairOutcome = default;
            int bestFairTie = int.MinValue;
            bool hasFair = false;
            PickBest(snapshot, ring, self, player, countdown, charges, dir, mustDie,
                fairMoves, ref bestFair, ref bestFairOutcome, ref bestFairTie, ref hasFair);

            // 保命例外：公平招都会炸自己 → 才允许塞其他 AI
            if (hasFair && bestFairOutcome.DeathId != self)
                return bestFair;

            var survivalMoves = new List<AiMove>(fairMoves);
            for (int i = 0; i < ring.Count; i++)
            {
                int t = ring[i];
                if (t == self || t == player)
                    continue;
                survivalMoves.Add(new AiMove { Action = BombAction.Shove, TargetId = t });
            }

            // C≥2 下家 AI 时，保命也可试传（总比自杀强）
            if (nextIsAi && countdown >= 2)
                survivalMoves.Add(new AiMove { Action = BombAction.Pass, TargetId = next });

            AiMove best = bestFair;
            SimOutcome bestOutcome = bestFairOutcome;
            int bestTie = bestFairTie;
            bool hasBest = hasFair;
            PickBest(snapshot, ring, self, player, countdown, charges, dir, true,
                survivalMoves, ref best, ref bestOutcome, ref bestTie, ref hasBest);

            return hasBest ? best : new AiMove { Action = BombAction.Pass, TargetId = next };
        }

        void PickBest(
            BattleSnapshot snapshot,
            List<int> ring,
            int self,
            int player,
            int countdown,
            int charges,
            int dir,
            bool mustDie,
            List<AiMove> moves,
            ref AiMove best,
            ref SimOutcome bestOutcome,
            ref int bestTie,
            ref bool hasBest)
        {
            for (int i = 0; i < moves.Count; i++)
            {
                AiMove move = moves[i];
                SimOutcome outcome = PredictOutcomeAfterMove(
                    snapshot, ring, self, countdown, charges, dir, move);
                int tie = ActionTieBreak(move, mustDie, player);
                if (!hasBest || IsBetterOutcome(outcome, bestOutcome, self, player, tie, bestTie))
                {
                    hasBest = true;
                    best = move;
                    bestOutcome = outcome;
                    bestTie = tie;
                }
            }
        }

        /// <summary>
        /// 炸玩家(且玩家摸过炸弹) > 炸玩家(没摸过，惩罚) > 炸别人 > 炸自己；
        /// 同档比步数；再比动作偏好。
        /// </summary>
        static bool IsBetterOutcome(
            SimOutcome candidate,
            SimOutcome current,
            int self,
            int player,
            int candidateActionTie,
            int currentActionTie)
        {
            int cRank = DeathRank(candidate, self, player);
            int curRank = DeathRank(current, self, player);
            if (cRank != curRank)
                return cRank > curRank;

            if (player >= 0 && candidate.DeathId == player)
            {
                if (candidate.Steps != current.Steps)
                    return candidate.Steps < current.Steps;
            }
            else if (candidate.DeathId == self)
            {
                if (candidate.Steps != current.Steps)
                    return candidate.Steps > current.Steps;
            }
            else if (candidate.Steps != current.Steps)
            {
                return candidate.Steps < current.Steps;
            }

            return candidateActionTie > currentActionTie;
        }

        static int DeathRank(SimOutcome o, int self, int player)
        {
            if (player >= 0 && o.DeathId == player)
            {
                // 玩家死前从未轮到过：不当作完美击杀（防磨刀后 0 砸脸刷分）
                return o.PlayerHadTurn ? 4 : 2;
            }
            if (o.DeathId != self)
                return 1;
            return 0;
        }

        static int ActionTieBreak(AiMove move, bool mustDie, int player)
        {
            int actionRank;
            if (mustDie)
            {
                if (move.Action == BombAction.Defuse) actionRank = 30;
                else if (move.Action == BombAction.Shove) actionRank = 20;
                else actionRank = 10;
            }
            else
            {
                // 公平进攻：对玩家塞 > 传
                if (move.Action == BombAction.Shove && move.TargetId == player) actionRank = 30;
                else if (move.Action == BombAction.Pass) actionRank = 20;
                else if (move.Action == BombAction.Shove) actionRank = 15;
                else actionRank = 10;
            }

            int targetRank = (player >= 0 && move.TargetId == player) ? 2 : 1;
            return actionRank * 10 + targetRank;
        }

        SimOutcome PredictOutcomeAfterMove(
            BattleSnapshot snapshot,
            List<int> ring,
            int self,
            int countdown,
            int charges,
            int dir,
            AiMove myMove)
        {
            if (!TryApplyAction(ring, self, ref countdown, ref charges, dir, myMove,
                    out int holder, out int exploded))
            {
                // 立刻炸死接手者：若接手是玩家，未轮到行动
                return new SimOutcome
                {
                    DeathId = exploded,
                    Steps = 1,
                    PlayerHadTurn = false
                };
            }

            return SimulateUntilDeath(snapshot, ring, self, holder, countdown, charges, dir,
                playerSelfPreserve: true, stepsSoFar: 1, playerHadTurn: false);
        }

        SimOutcome SimulateUntilDeath(
            BattleSnapshot snapshot,
            List<int> ring,
            int self,
            int holder,
            int countdown,
            int charges,
            int dir,
            bool playerSelfPreserve,
            int stepsSoFar,
            bool playerHadTurn)
        {
            int player = FindPlayerId(snapshot);

            for (int step = 0; step < maxSimSteps; step++)
            {
                if (countdown <= 0)
                {
                    return new SimOutcome
                    {
                        DeathId = holder,
                        Steps = stepsSoFar,
                        PlayerHadTurn = playerHadTurn
                    };
                }

                if (player >= 0 && holder == player && countdown > 0)
                    playerHadTurn = true;

                AiMove move;
                if (player >= 0 && holder == player)
                {
                    move = playerSelfPreserve
                        ? PickPlayerSelfPreserveMove(snapshot, ring, self, player, countdown, charges, dir)
                        : new AiMove { Action = BombAction.Pass, TargetId = NextAlive(ring, holder, dir) };
                }
                else
                {
                    move = PolicyMove(snapshot, ring, self, holder, countdown, charges, dir);
                }

                if (!TryApplyAction(ring, holder, ref countdown, ref charges, dir, move,
                        out holder, out int exploded))
                {
                    return new SimOutcome
                    {
                        DeathId = exploded,
                        Steps = stepsSoFar + 1,
                        PlayerHadTurn = playerHadTurn
                    };
                }

                stepsSoFar++;
            }

            return new SimOutcome
            {
                DeathId = DeathSeatIfAllPass(ring, holder, countdown, dir),
                Steps = stepsSoFar + Mathf.Max(1, countdown),
                PlayerHadTurn = playerHadTurn
            };
        }

        AiMove PickPlayerSelfPreserveMove(
            BattleSnapshot snapshot,
            List<int> ring,
            int snakeId,
            int player,
            int countdown,
            int charges,
            int dir)
        {
            int next = NextAlive(ring, player, dir);
            AiMove best = new AiMove { Action = BombAction.Pass, TargetId = next };
            int bestScore = int.MinValue;
            int bestTie = int.MinValue;

            void Consider(AiMove candidate)
            {
                int c = countdown;
                int ch = charges;
                int death;
                if (!TryApplyAction(ring, player, ref c, ref ch, dir, candidate,
                        out int holder, out int exploded))
                {
                    death = exploded;
                }
                else
                {
                    death = SimulateUntilDeath(snapshot, ring, snakeId, holder, c, ch, dir,
                        playerSelfPreserve: false, stepsSoFar: 0, playerHadTurn: true).DeathId;
                }

                int score = death == player ? PlayerDieScore : PlayerSurviveScore;
                int tie = PlayerTieBreak(candidate, death, player);
                if (score > bestScore || (score == bestScore && tie > bestTie))
                {
                    bestScore = score;
                    bestTie = tie;
                    best = candidate;
                }
            }

            Consider(new AiMove { Action = BombAction.Pass, TargetId = next });
            for (int i = 0; i < ring.Count; i++)
            {
                int t = ring[i];
                if (t == player) continue;
                Consider(new AiMove { Action = BombAction.Shove, TargetId = t });
            }
            if (charges > 0)
                Consider(new AiMove { Action = BombAction.Defuse, TargetId = next });

            return best;
        }

        static int PlayerTieBreak(AiMove move, int deathSeat, int player)
        {
            bool survives = deathSeat != player;
            if (survives)
            {
                if (move.Action == BombAction.Pass) return 30;
                if (move.Action == BombAction.Defuse) return 20;
                return 10;
            }
            if (move.Action == BombAction.Defuse) return 30;
            if (move.Action == BombAction.Shove) return 20;
            return 10;
        }

        AiMove PolicyMove(
            BattleSnapshot snapshot,
            List<int> ring,
            int self,
            int holder,
            int countdown,
            int charges,
            int dir)
        {
            int next = NextAlive(ring, holder, dir);
            int player = FindPlayerId(snapshot);
            SeatPersonality personality = GetPersonality(snapshot, holder);

            if (holder == self || personality == SeatPersonality.Snake)
                return new AiMove { Action = BombAction.Pass, TargetId = next };

            switch (personality)
            {
                case SeatPersonality.Worm:
                    // 与 WormAi 一致：C=1 优先传（下家必死），勿先拆
                    if (countdown < 2)
                        return new AiMove { Action = BombAction.Pass, TargetId = next };
                    if (countdown <= wormPanicThreshold)
                    {
                        if (charges > 0)
                            return new AiMove { Action = BombAction.Defuse, TargetId = next };
                        return new AiMove
                        {
                            Action = BombAction.Shove,
                            TargetId = PreferTarget(ring, holder, player)
                        };
                    }
                    return new AiMove { Action = BombAction.Pass, TargetId = next };

                case SeatPersonality.Ash:
                    if (countdown <= ashPreferShoveBelowOrEqual)
                    {
                        return new AiMove
                        {
                            Action = BombAction.Shove,
                            TargetId = PreferTarget(ring, holder, player)
                        };
                    }
                    return new AiMove { Action = BombAction.Pass, TargetId = next };

                default:
                    return new AiMove { Action = BombAction.Pass, TargetId = next };
            }
        }

        static bool TryApplyAction(
            List<int> ring,
            int actor,
            ref int countdown,
            ref int charges,
            int dir,
            AiMove move,
            out int newHolder,
            out int explodedId)
        {
            explodedId = -1;
            int nextForced = NextAlive(ring, actor, dir);

            switch (move.Action)
            {
                case BombAction.Defuse:
                    if (charges <= 0)
                    {
                        countdown -= 1;
                        newHolder = nextForced;
                    }
                    else
                    {
                        charges--;
                        countdown += 2;
                        newHolder = nextForced;
                    }
                    break;

                case BombAction.Shove:
                {
                    int target = move.TargetId;
                    if (IndexOf(ring, target) < 0 || target == actor)
                        target = PreferTarget(ring, actor, -1);
                    countdown -= 2;
                    newHolder = target;
                    break;
                }

                default:
                    countdown -= 1;
                    newHolder = nextForced;
                    break;
            }

            if (countdown <= 0)
            {
                explodedId = newHolder;
                return false;
            }

            return true;
        }

        static int PreferTarget(List<int> ring, int self, int player)
        {
            if (player >= 0 && player != self && IndexOf(ring, player) >= 0)
                return player;
            for (int i = 0; i < ring.Count; i++)
                if (ring[i] != self)
                    return ring[i];
            return self;
        }

        static SeatPersonality GetPersonality(BattleSnapshot snapshot, int id)
        {
            for (int i = 0; i < snapshot.Participants.Count; i++)
            {
                if (snapshot.Participants[i].Id == id)
                    return snapshot.Participants[i].Personality;
            }
            return SeatPersonality.Unknown;
        }

        public static int DeathSeatIfAllPass(IReadOnlyList<int> ring, int startId, int countdown, int dir)
        {
            if (ring == null || ring.Count == 0)
                return startId;
            if (countdown <= 0)
                return startId;

            int idx = IndexOf(ring, startId);
            if (idx < 0) idx = 0;
            int n = ring.Count;
            int offset = Mod(dir * countdown, n);
            return ring[(idx + offset) % n];
        }

        public static List<int> BuildAliveRing(BattleSnapshot snapshot)
        {
            var ring = new List<int>();
            for (int i = 0; i < snapshot.Participants.Count; i++)
            {
                if (snapshot.Participants[i].IsAlive)
                    ring.Add(snapshot.Participants[i].Id);
            }
            return ring;
        }

        static int FindPlayerId(BattleSnapshot snapshot)
        {
            for (int i = 0; i < snapshot.Participants.Count; i++)
            {
                var p = snapshot.Participants[i];
                if (p.IsPlayer && p.IsAlive)
                    return p.Id;
            }
            return -1;
        }

        static int NextAlive(IReadOnlyList<int> ring, int fromId, int dir)
        {
            int idx = IndexOf(ring, fromId);
            if (idx < 0 || ring.Count == 0)
                return fromId;
            return ring[Mod(idx + dir, ring.Count)];
        }

        static int IndexOf(IReadOnlyList<int> ring, int id)
        {
            for (int i = 0; i < ring.Count; i++)
                if (ring[i] == id)
                    return i;
            return -1;
        }

        static int Mod(int x, int m)
        {
            if (m <= 0) return 0;
            int r = x % m;
            return r < 0 ? r + m : r;
        }
    }
}
