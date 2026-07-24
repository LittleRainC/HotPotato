using UnityEngine;

namespace Chardin
{
    /// <summary>
    /// 敌人基本体：可复制后换皮/换 AI。拖进 BattleController.clockwiseOrder。
    /// 外观在子物体 Sprite 上；死亡时关掉该子物体。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider2D))]
    public class Enemy : TableSeat
    {
        void Reset()
        {
            Configure(gameObject.name, player: false);
            EnsureCollider();
            EnsureBombPosition();
            CacheVisualRoot();
        }

        protected override void Awake()
        {
            base.Awake();
            EnsureCollider();
        }

        void EnsureCollider()
        {
            var col = GetComponent<Collider2D>();
            if (col == null)
            {
                var box = gameObject.AddComponent<BoxCollider2D>();
                box.isTrigger = true;
                box.size = new Vector2(1.2f, 1.6f);
            }
            else
            {
                col.isTrigger = true;
            }
        }

        public override void SetAlive(bool alive)
        {
            base.SetAlive(alive);
            CacheVisualRoot();
            if (VisualRoot != null)
                VisualRoot.gameObject.SetActive(alive);
        }

        public override void ResetSeat()
        {
            CacheVisualRoot();
            if (VisualRoot != null)
                VisualRoot.gameObject.SetActive(true);
            base.ResetSeat();
        }
    }
}
