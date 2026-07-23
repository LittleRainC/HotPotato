using UnityEngine;

namespace Chardin
{
    /// <summary>
    /// 玩家座位：拖进 BattleController.clockwiseOrder。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerSeat : TableSeat
    {
        void Reset()
        {
            Configure("Player", player: true);
        }

        void Awake()
        {
            Configure(DisplayName, player: true);
        }
    }
}
