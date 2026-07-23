namespace Chardin
{
    public enum BombAction
    {
        Pass,   // 传 -1
        Shove,  // 塞 -2 / 手滑 -1 自留
        Defuse  // 拆 +2，仍须移交
    }
}
