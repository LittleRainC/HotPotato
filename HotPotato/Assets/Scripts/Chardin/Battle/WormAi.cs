using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Chardin
{
    /// <summary>
    /// Worm（怂包）：数字一小就慌。
    /// ≤4 优先拆，否则塞；≤7 传；更大则传且思考偏慢。
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

            BombAction action;
            if (countdown <= panicThreshold)
            {
                action = snapshot.SharedDefuseCharges > 0 ? BombAction.Defuse : BombAction.Shove;
            }
            else
            {
                action = BombAction.Pass;
            }

            // 传：交给 Battle 按顺时针解析；这里 TargetId 对 Pass 可忽略
            // 塞/拆：随机选一个非自己的存活目标（塞的玩家瞄准另走 UI）
            int targetId = PickRandomTarget(snapshot);
            onDecided?.Invoke(new AiMove { Action = action, TargetId = targetId });
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
