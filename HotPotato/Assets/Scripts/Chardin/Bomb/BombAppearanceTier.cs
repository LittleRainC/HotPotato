namespace Chardin
{
    /// <summary>非持有者可见的外观档位（按剩余比例 → 头/脸 Sprite）。</summary>
    public enum BombAppearanceTier
    {
        Safe,       // > 60%  → 1 / face1
        Warning,    // 30–60% → 2 / face2
        Danger,     // 10–30% → 3 / face3
        Critical    // < 10%  → 4↔5 闪 / face4
    }
}
