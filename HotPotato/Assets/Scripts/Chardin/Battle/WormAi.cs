using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Chardin
{
    /// <summary>
    /// Worm（怂包）：数字一小就慌。
    /// C=1：传（下家必死，不必浪费拆）；
    /// ≤4：有拆就拆，否则塞；更大：传。
    /// </summary>
    public sealed class WormAi : MonoBehaviour, IBattleAi
    {
        [SerializeField] Vector2 thinkDelayNormal = new Vector2(1.5f, 2.2f);
        [SerializeField] Vector2 thinkDelayRelaxed = new Vector2(2.2f, 3f);
        [SerializeField] int panicThreshold = 4;
        [SerializeField] int nervousThreshold = 7;

        public void Decide(BattleSnapshot snapshot, Action<AiMove> onDecided)
        {
            StartCoroutine(DecideRoutine(snapshot, onDecided));
        }

        IEnumerator DecideRoutine(BattleSnapshot snapshot, Action<AiMove> onDecided)
        {
            int countdown = snapshot.HolderCountdown ?? 99;
            Vector2 delay = countdown > nervousThreshold ? thinkDelayRelaxed : thinkDelayNormal;
            yield return new WaitForSeconds(UnityEngine.Random.Range(delay.x, delay.y));

            int next = NextAlive(snapshot);
            BombAction action;

            // C=1：传则下家接手即炸。优先传（尤其下家是玩家），不要先拆。
            if (countdown < 2)
            {
                action = BombAction.Pass;
            }
            else if (countdown <= panicThreshold)
            {
                action = snapshot.SharedDefuseCharges > 0 ? BombAction.Defuse : BombAction.Shove;
            }
            else
            {
                action = BombAction.Pass;
            }

            int targetId = action == BombAction.Pass || action == BombAction.Defuse
                ? next
                : PickRandomTarget(snapshot);

            onDecided?.Invoke(new AiMove { Action = action, TargetId = targetId });
        }

        static int NextAlive(BattleSnapshot snapshot)
        {
            int dir = snapshot.PassDirection >= 0 ? 1 : -1;
            var ring = new List<int>();
            for (int i = 0; i < snapshot.Participants.Count; i++)
            {
                if (snapshot.Participants[i].IsAlive)
                    ring.Add(snapshot.Participants[i].Id);
            }
            if (ring.Count == 0)
                return snapshot.SelfId;

            int idx = -1;
            for (int i = 0; i < ring.Count; i++)
            {
                if (ring[i] == snapshot.SelfId)
                {
                    idx = i;
                    break;
                }
            }
            if (idx < 0) idx = 0;
            int n = ring.Count;
            int nextIdx = ((idx + dir) % n + n) % n;
            return ring[nextIdx];
        }

        static int PickRandomTarget(BattleSnapshot snapshot)
        {
            var targets = new List<int>();
            for (int i = 0; i < snapshot.Participants.Count; i++)
            {
                var p = snapshot.Participants[i];
                if (p.IsAlive && p.Id != snapshot.SelfId)
                    targets.Add(p.Id);
            }
            if (targets.Count == 0)
                return snapshot.SelfId;
            return targets[UnityEngine.Random.Range(0, targets.Count)];
        }
    }
}
