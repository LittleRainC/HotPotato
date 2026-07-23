using System;

namespace Chardin
{
    /// <summary>
    /// Ruilin：实现此接口挂到 AI 角色上。Decide 必须恰好回调一次。
    /// </summary>
    public interface IBattleAi
    {
        void Decide(BattleSnapshot snapshot, Action<AiMove> onDecided);
    }
}
