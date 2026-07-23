namespace Chardin
{
    public struct BombActionResult
    {
        public BombAction Action;
        public int CountdownAfter;
        public bool Slipped;
        /// <summary>是否应移交炸弹（手滑时为 false）。</summary>
        public bool ShouldTransfer;
        /// <summary>
        /// 手滑弹回自己时：是否在「接手判定」后爆炸。
        /// 正常移交的爆炸由接收方接手时判定，不在此字段。
        /// </summary>
        public bool ExplodedOnSelfAfterSlip;
    }
}
