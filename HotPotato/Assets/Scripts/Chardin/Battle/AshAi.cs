using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Chardin
{
    /// <summary>
    /// Ash（赌徒）：热爱塞，从不用拆。
    /// ≤6：70% 塞 / 30% 传；更大：60% 塞 / 40% 传且整体手快。
    /// 破绽：突然变慢 + 传 ≈ 数字还很大、在享受。
    /// </summary>
    public sealed class AshAi : MonoBehaviour, IBattleAi
    {
        [SerializeField] Vector2 thinkDelayFast = new Vector2(0.8f, 1.4f);
        [SerializeField] Vector2 thinkDelayNormal = new Vector2(1.2f, 2.0f);
        [SerializeField] Vector2 thinkDelaySlowPass = new Vector2(2.2f, 3.2f);
        [SerializeField] int aggressiveThreshold = 6;
        [SerializeField] [Range(0f, 1f)] float shoveChanceWhenLow = 0.70f;
        [SerializeField] [Range(0f, 1f)] float shoveChanceWhenHigh = 0.60f;

        public void Decide(BattleSnapshot snapshot, Action<AiMove> onDecided)
        {
            StartCoroutine(DecideRoutine(snapshot, onDecided));
        }

        IEnumerator DecideRoutine(BattleSnapshot snapshot, Action<AiMove> onDecided)
        {
            int countdown = snapshot.HolderCountdown ?? 99;
            bool low = countdown <= aggressiveThreshold;
            float shoveChance = low ? shoveChanceWhenLow : shoveChanceWhenHigh;
            bool shove = UnityEngine.Random.value < shoveChance;
            BombAction action = shove ? BombAction.Shove : BombAction.Pass;

            // 永不拆。高数字还选择「传」时故意变慢，露出「在享受」的破绽。
            Vector2 delay;
            if (!low && action == BombAction.Pass)
                delay = thinkDelaySlowPass;
            else if (!low)
                delay = thinkDelayFast;
            else
                delay = thinkDelayNormal;

            yield return new WaitForSeconds(UnityEngine.Random.Range(delay.x, delay.y));

            onDecided?.Invoke(new AiMove
            {
                Action = action,
                TargetId = PickRandomTarget(snapshot)
            });
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
