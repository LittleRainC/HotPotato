using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Chardin
{
    /// <summary>
    /// 占位 AI：1.5–3s 后随机「传」给一名存活对手。Ruilin 的人格 AI 就绪后替换。
    /// </summary>
    public sealed class PlaceholderBattleAi : MonoBehaviour, IBattleAi
    {
        [SerializeField] Vector2 thinkDelay = new Vector2(1.5f, 3f);

        public void Decide(BattleSnapshot snapshot, Action<AiMove> onDecided)
        {
            StartCoroutine(DecideRoutine(snapshot, onDecided));
        }

        IEnumerator DecideRoutine(BattleSnapshot snapshot, Action<AiMove> onDecided)
        {
            yield return new WaitForSeconds(UnityEngine.Random.Range(thinkDelay.x, thinkDelay.y));

            var targets = new List<int>();
            for (int i = 0; i < snapshot.Participants.Count; i++)
            {
                var p = snapshot.Participants[i];
                if (p.IsAlive && p.Id != snapshot.SelfId)
                    targets.Add(p.Id);
            }

            if (targets.Count == 0)
            {
                onDecided?.Invoke(new AiMove { Action = BombAction.Pass, TargetId = snapshot.SelfId });
                yield break;
            }

            // 有拆线且数字很小：偶尔拆（占位，真正逻辑归 Ruilin）
            BombAction action = BombAction.Pass;
            if (snapshot.HolderCountdown.HasValue
                && snapshot.HolderCountdown.Value <= 4
                && snapshot.SharedDefuseCharges > 0
                && UnityEngine.Random.value < 0.5f)
            {
                action = BombAction.Defuse;
            }
            else if (snapshot.HolderCountdown.HasValue
                     && snapshot.HolderCountdown.Value <= 6
                     && UnityEngine.Random.value < 0.35f)
            {
                action = BombAction.Shove;
            }

            onDecided?.Invoke(new AiMove
            {
                Action = action,
                TargetId = targets[UnityEngine.Random.Range(0, targets.Count)]
            });
        }
    }
}
