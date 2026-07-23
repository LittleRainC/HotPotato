namespace Chardin
{
    /// <summary>非持有者可见的外观档位（按剩余比例）。</summary>
    public enum BombAppearanceTier
    {
        Safe,       // > 60% 黄
        Warning,    // 30–60% 橙
        Danger,     // 10–30% 红
        Critical    // < 10% 深红闪
    }
}
