using UnityEngine;

namespace Chardin
{
    /// <summary>
    /// 敌人基本体：可复制后换皮/换 AI。拖进 BattleController.clockwiseOrder。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider2D))]
    public class Enemy : TableSeat
    {
        [SerializeField] SpriteRenderer spriteRenderer;
        [SerializeField] float eliminatedAlpha = 0.35f;
        Color _baseColor = Color.white;

        void Reset()
        {
            Configure(gameObject.name, player: false);
            EnsureCollider();
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        void Awake()
        {
            if (spriteRenderer == null)
                spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
                _baseColor = spriteRenderer.color;
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
            if (spriteRenderer == null)
                spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer == null)
                return;

            var c = _baseColor;
            c.a = alive ? _baseColor.a : eliminatedAlpha;
            spriteRenderer.color = c;
        }

        public override void ResetSeat()
        {
            if (spriteRenderer == null)
                spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
                spriteRenderer.color = _baseColor;
            base.ResetSeat();
        }
    }
}
