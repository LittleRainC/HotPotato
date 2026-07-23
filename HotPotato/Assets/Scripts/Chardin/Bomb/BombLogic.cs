using UnityEngine;

namespace Chardin
{
    /// <summary>
    /// 炸弹纯逻辑（无 MonoBehaviour）。Ruilin 的 AI 可读 Countdown / Tier，不要直接改字段。
    /// </summary>
    public sealed class BombLogic
    {
        public int Countdown { get; private set; }
        public int InitialCountdown { get; private set; }

        public float RemainingRatio
        {
            get
            {
                if (InitialCountdown <= 0)
                    return 0f;
                return Mathf.Clamp01((float)Countdown / InitialCountdown);
            }
        }

        public bool IsArmed => InitialCountdown > 0;

        public void Reset(int initialCountdown)
        {
            InitialCountdown = Mathf.Max(1, initialCountdown);
            Countdown = InitialCountdown;
        }

        public BombAppearanceTier GetAppearanceTier()
        {
            float r = RemainingRatio;
            if (r > 0.60f) return BombAppearanceTier.Safe;
            if (r > 0.30f) return BombAppearanceTier.Warning;
            if (r > 0.10f) return BombAppearanceTier.Danger;
            return BombAppearanceTier.Critical;
        }

        /// <summary>接手瞬间：倒计时 ≤ 0 则爆炸。</summary>
        public static bool ExplodesOnReceive(int countdownAfterAction) => countdownAfterAction <= 0;

        public BombActionResult ApplyPass()
        {
            Countdown -= 1;
            return new BombActionResult
            {
                Action = BombAction.Pass,
                CountdownAfter = Countdown,
                Slipped = false,
                ShouldTransfer = true,
                ExplodedOnSelfAfterSlip = false
            };
        }

        /// <param name="slipChance">默认 0.2；道具可降到 0.08。</param>
        public BombActionResult ApplyShove(float slipChance = 0.2f)
        {
            slipChance = Mathf.Clamp01(slipChance);
            bool slipped = Random.value < slipChance;

            if (slipped)
            {
                Countdown -= 1;
                bool exploded = ExplodesOnReceive(Countdown);
                return new BombActionResult
                {
                    Action = BombAction.Shove,
                    CountdownAfter = Countdown,
                    Slipped = true,
                    ShouldTransfer = false,
                    ExplodedOnSelfAfterSlip = exploded
                };
            }

            Countdown -= 2;
            return new BombActionResult
            {
                Action = BombAction.Shove,
                CountdownAfter = Countdown,
                Slipped = false,
                ShouldTransfer = true,
                ExplodedOnSelfAfterSlip = false
            };
        }

        public BombActionResult ApplyDefuse()
        {
            Countdown += 2;
            return new BombActionResult
            {
                Action = BombAction.Defuse,
                CountdownAfter = Countdown,
                Slipped = false,
                ShouldTransfer = true,
                ExplodedOnSelfAfterSlip = false
            };
        }
    }
}
