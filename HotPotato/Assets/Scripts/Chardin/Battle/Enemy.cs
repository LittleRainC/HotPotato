using System.Collections;
using UnityEngine;

namespace Chardin
{
    /// <summary>
    /// 敌人基本体：可复制后换皮/换 AI。拖进 BattleController.clockwiseOrder。
    /// 外观在子物体 Sprite 上；死亡时红白闪后关掉该子物体。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider2D))]
    public class Enemy : TableSeat
    {
        [SerializeField] float deathFlashSeconds = 0.65f;
        [SerializeField] float deathFlashHz = 14f;

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
            {
                if (alive)
                    RestoreSpriteColors();
                VisualRoot.gameObject.SetActive(alive);
            }
        }

        public override void ResetSeat()
        {
            CacheVisualRoot();
            if (VisualRoot != null)
            {
                RestoreSpriteColors();
                VisualRoot.gameObject.SetActive(true);
            }
            base.ResetSeat();
        }

        /// <summary>出局前：半身像红白闪烁，随后由 SetAlive(false) 隐藏。</summary>
        public IEnumerator PlayDeathFlash()
        {
            CacheVisualRoot();
            if (VisualRoot == null)
                yield break;

            VisualRoot.gameObject.SetActive(true);
            var renderers = VisualRoot.GetComponentsInChildren<SpriteRenderer>(true);
            if (renderers == null || renderers.Length == 0)
                yield break;

            var baseColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
                baseColors[i] = renderers[i].color;

            float t = 0f;
            while (t < deathFlashSeconds)
            {
                t += Time.deltaTime;
                bool useRed = Mathf.FloorToInt(t * deathFlashHz) % 2 == 0;
                Color flash = useRed ? Color.red : Color.white;
                for (int i = 0; i < renderers.Length; i++)
                    renderers[i].color = flash;
                yield return null;
            }

            for (int i = 0; i < renderers.Length; i++)
                renderers[i].color = baseColors[i];
        }

        void RestoreSpriteColors()
        {
            if (VisualRoot == null)
                return;
            var renderers = VisualRoot.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                renderers[i].color = Color.white;
        }
    }
}
