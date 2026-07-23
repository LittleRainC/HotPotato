using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Chardin
{
    /// <summary>
    /// Snake（老千）：
    /// 枚举传/塞/拆，按人格+玩家自保模拟到爆炸。
    /// 目标：优先炸玩家；在能炸玩家的方案里，选「玩家死得最早」（步数最少）。
    /// </summary>
    public sealed class SnakeAi : MonoBehaviour, IBattleAi
    {
        struct SimOutcome
        {
            public int DeathId;
            public int Steps; // 从 Snake 这手起，到爆炸经过的行动次数（越小越靠前）
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

            SimOutcome baseline = PredictOutcomeAfterMove(
                snapshot, ring, self, countdown, charges, dir,
                new AiMove { Action = BombAction.Pass, TargetId = next });
            bool mustDie = baseline.DeathId == self;

            AiMove best = new AiMove { Action = BombAction.Pass, TargetId = next };
            SimOutcome bestOutcome = default;
            int bestActionTie = int.MinValue;
            bool hasBest = false;

            void Consider(AiMove move)
            {
                SimOutcome outcome = PredictOutcomeAfterMove(
                    snapshot, ring, self, countdown, charges, dir, move);
                int actionTie = ActionTieBreak(move, mustDie, player);

                if (!hasBest || IsBetterOutcome(outcome, bestOutcome, self, player, actionTie, bestActionTie))
                {
                    hasBest = true;
                    best = move;
                    bestOutcome = outcome;
                    bestActionTie = actionTie;
                }
            }

            Consider(new AiMove { Action = BombAction.Pass, TargetId = next });

            for (int i = 0; i < ring.Count; i++)
            {
                int t = ring[i];
                if (t == self)
                    continue;
                Consider(new AiMove { Action = BombAction.Shove, TargetId = t });
            }

            if (mustDie && charges > 0)
                Consider(new AiMove { Action = BombAction.Defuse, TargetId = next });

            return best;
        }

        /// <summary>
        /// 比较：炸玩家 > 炸别人 > 炸自己；
        /// 同为炸玩家时步数更少更好（死亡更靠前）；
        /// 同为炸自己时步数更多更好（拖死）；
        /// 再比动作偏好。
        /// </summary>
        static bool IsBetterOutcome(
            SimOutcome candidate,
            SimOutcome current,
            int self,
            int player,
            int candidateActionTie,
            int currentActionTie)
        {
            int cRank = DeathRank(candidate.DeathId, self, player);
            int curRank = DeathRank(current.DeathId, self, player);
            if (cRank != curRank)
                return cRank > curRank;

            if (player >= 0 && candidate.DeathId == player)
            {
                // 玩家死得越早越好
                if (candidate.Steps != current.Steps)
                    return candidate.Steps < current.Steps;
            }
            else if (candidate.DeathId == self)
            {
                if (candidate.Steps != current.Steps)
                    return candidate.Steps > current.Steps;
            }
            else
            {
                // 炸其他 AI：也略偏早结束
                if (candidate.Steps != current.Steps)
                    return candidate.Steps < current.Steps;
            }

            return candidateActionTie > currentActionTie;
        }

        static int DeathRank(int deathId, int self, int player)
        {
            if (player >= 0 && deathId == player)
                return 2;
            if (deathId != self)
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
                // 进攻：同样结局时偏塞
                if (move.Action == BombAction.Shove) actionRank = 30;
                else if (move.Action == BombAction.Pass) actionRank = 20;
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
            int steps = 0;
            if (!TryApplyAction(ring, self, ref countdown, ref charges, dir, myMove,
                    out int holder, out int exploded))
            {
                return new SimOutcome { DeathId = exploded, Steps = 1 };
            }

            steps = 1;
            return SimulateUntilDeath(snapshot, ring, self, holder, countdown, charges, dir,
                playerSelfPreserve: true, stepsSoFar: steps);
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
            int stepsSoFar)
        {
            int player = FindPlayerId(snapshot);

            for (int step = 0; step < maxSimSteps; step++)
            {
                if (countdown <= 0)
                    return new SimOutcome { DeathId = holder, Steps = stepsSoFar };

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
                    return new SimOutcome { DeathId = exploded, Steps = stepsSoFar + 1 };
                }

                stepsSoFar++;
            }

            return new SimOutcome
            {
                DeathId = DeathSeatIfAllPass(ring, holder, countdown, dir),
                Steps = stepsSoFar + Mathf.Max(1, countdown)
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
                        playerSelfPreserve: false, stepsSoFar: 0).DeathId;
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
                if (t == player)
                    continue;
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
