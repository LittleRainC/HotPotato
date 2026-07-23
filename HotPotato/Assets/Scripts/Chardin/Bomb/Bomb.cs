using System;
using UnityEngine;

namespace Chardin
{
    /// <summary>
    /// 场景炸弹入口。持有逻辑 + 刷新视图；移交/爆炸由上层 Battle 处理。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BombView))]
    public sealed class Bomb : MonoBehaviour
    {
        [SerializeField] BombView view;
        [SerializeField] float defaultSlipChance = 0.2f;

        public BombLogic Logic { get; } = new BombLogic();

        /// <summary>当前视角是否为持有者（决定是否显示精确数字）。</summary>
        public bool ViewerIsHolder { get; private set; }

        public event Action<BombActionResult> ActionResolved;
        public event Action ExplodedOnSelf;

        void Awake()
        {
            if (view == null)
                view = GetComponent<BombView>();
        }

        public void Arm(int initialCountdown, bool viewerIsHolder = true)
        {
            Logic.Reset(initialCountdown);
            ViewerIsHolder = viewerIsHolder;
            view.CaptureRestPosition();
            view.Refresh(Logic, ViewerIsHolder);
        }

        public void SetViewerIsHolder(bool isHolder)
        {
            ViewerIsHolder = isHolder;
            view.Refresh(Logic, ViewerIsHolder);
        }

        public BombActionResult Pass()
        {
            var result = Logic.ApplyPass();
            AfterAction(result);
            return result;
        }

        public BombActionResult Shove(float? slipChance = null)
        {
            var result = Logic.ApplyShove(slipChance ?? defaultSlipChance);
            AfterAction(result);
            return result;
        }

        public BombActionResult Defuse()
        {
            var result = Logic.ApplyDefuse();
            AfterAction(result);
            return result;
        }

        /// <summary>对方接手时调用：若 ≤0 返回 true（应由 Battle 触发挨炸）。</summary>
        public bool CheckExplodeOnReceive()
        {
            return BombLogic.ExplodesOnReceive(Logic.Countdown);
        }

        void AfterAction(BombActionResult result)
        {
            view.Refresh(Logic, ViewerIsHolder);
            ActionResolved?.Invoke(result);

            if (result.ExplodedOnSelfAfterSlip)
                ExplodedOnSelf?.Invoke();
        }
    }
}
