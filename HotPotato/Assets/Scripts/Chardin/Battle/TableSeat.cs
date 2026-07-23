using UnityEngine;

namespace Chardin
{
    /// <summary>
    /// 圆桌座位基类：玩家与敌人都挂这个（或子类），再拖进 BattleController 的顺时针列表。
    /// </summary>
    public class TableSeat : MonoBehaviour
    {
        [SerializeField] string displayName;
        [SerializeField] bool isPlayer;
        [SerializeField] Transform bombAnchor;

        public string DisplayName => string.IsNullOrEmpty(displayName) ? gameObject.name : displayName;
        public bool IsPlayer => isPlayer;
        public bool IsAlive { get; private set; } = true;
        public Transform BombAnchor => bombAnchor != null ? bombAnchor : transform;

        public IBattleAi GetAi()
        {
            var behaviours = GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IBattleAi ai)
                    return ai;
            }
            return null;
        }

        public virtual void SetAlive(bool alive)
        {
            IsAlive = alive;
        }

        public virtual void ResetSeat()
        {
            SetAlive(true);
        }

        public void Configure(string name, bool player)
        {
            displayName = name;
            isPlayer = player;
        }
    }
}
