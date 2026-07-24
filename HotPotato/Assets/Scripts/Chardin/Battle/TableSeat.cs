using System.Collections;
using UnityEngine;

namespace Chardin
{
    /// <summary>
    /// 圆桌座位基类：玩家与敌人都挂这个（或子类），再拖进 BattleController 的顺时针列表。
    /// 炸弹落点：{Name}-BombPosition；半身像：子物体 Sprite。
    /// </summary>
    public class TableSeat : MonoBehaviour
    {
        [SerializeField] string displayName;
        [SerializeField] bool isPlayer;
        [SerializeField] Transform bombAnchor;
        [SerializeField] Transform visualRoot;
        [SerializeField] string visualChildName = "Sprite";
        [SerializeField] Vector3 bombAnchorLocalOffset = new Vector3(0f, 0.65f, 0f);

        Vector3 _visualRestLocalPos;
        Vector3 _visualRestLocalScale = Vector3.one;
        Coroutine _reactRoutine;

        public string DisplayName => string.IsNullOrEmpty(displayName) ? gameObject.name : displayName;
        public bool IsPlayer => isPlayer;
        public bool IsAlive { get; private set; } = true;
        public Transform BombAnchor => bombAnchor != null ? bombAnchor : transform;
        public Transform VisualRoot
        {
            get
            {
                CacheVisualRoot();
                return visualRoot;
            }
        }

        protected virtual void Awake()
        {
            EnsureBombPosition();
            CacheVisualRoot();
            CacheVisualRest();
        }

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
            if (_reactRoutine != null)
            {
                StopCoroutine(_reactRoutine);
                _reactRoutine = null;
            }
            RestoreVisualRest();
            SetAlive(true);
        }

        public void Configure(string name, bool player)
        {
            displayName = name;
            isPlayer = player;
        }

        /// <summary>确保存在 {Name}-BombPosition，并写入 bombAnchor。</summary>
        public void EnsureBombPosition()
        {
            string anchorName = gameObject.name + "-BombPosition";
            if (bombAnchor != null)
            {
                if (bombAnchor.parent != transform)
                    bombAnchor.SetParent(transform, true);
                return;
            }

            Transform existing = transform.Find(anchorName);
            if (existing == null)
                existing = transform.Find("BombPosition");

            if (existing == null)
            {
                var go = new GameObject(anchorName);
                go.transform.SetParent(transform, false);
                CacheVisualRoot();
                if (visualRoot != null)
                    go.transform.localPosition = visualRoot.localPosition + bombAnchorLocalOffset;
                else
                    go.transform.localPosition = bombAnchorLocalOffset;
                existing = go.transform;
            }

            bombAnchor = existing;
        }

        public void PlayPassLandReact()
        {
            RestartReact(PassLandReact());
        }

        public void PlayShoveLandReact()
        {
            RestartReact(ShoveLandReact());
        }

        void RestartReact(IEnumerator routine)
        {
            if (_reactRoutine != null)
                StopCoroutine(_reactRoutine);
            RestoreVisualRest();
            CacheVisualRest();
            _reactRoutine = StartCoroutine(WrapReact(routine));
        }

        IEnumerator WrapReact(IEnumerator routine)
        {
            yield return routine;
            RestoreVisualRest();
            _reactRoutine = null;
        }

        IEnumerator PassLandReact()
        {
            CacheVisualRoot();
            if (visualRoot == null)
                yield break;

            const float duration = 0.28f;
            const float amp = 0.08f;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float w = 1f - Mathf.Clamp01(t / duration);
                float x = Mathf.Sin(t * 42f) * amp * w;
                visualRoot.localPosition = _visualRestLocalPos + new Vector3(x, 0f, 0f);
                yield return null;
            }
        }

        IEnumerator ShoveLandReact()
        {
            CacheVisualRoot();
            if (visualRoot == null)
                yield break;

            const float duration = 0.34f;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / duration);
                // 弹跳：先上后落
                float y = Mathf.Sin(u * Mathf.PI) * 0.22f;
                float s = 1f + Mathf.Sin(u * Mathf.PI) * 0.08f;
                visualRoot.localPosition = _visualRestLocalPos + new Vector3(0f, y, 0f);
                visualRoot.localScale = _visualRestLocalScale * s;
                yield return null;
            }
        }

        protected void CacheVisualRoot()
        {
            if (visualRoot != null)
                return;

            var named = transform.Find(visualChildName);
            if (named != null)
            {
                visualRoot = named;
                return;
            }

            var sr = GetComponentInChildren<SpriteRenderer>(true);
            if (sr != null && sr.transform != transform)
                visualRoot = sr.transform;
        }

        void CacheVisualRest()
        {
            if (visualRoot == null)
                return;
            _visualRestLocalPos = visualRoot.localPosition;
            _visualRestLocalScale = visualRoot.localScale;
        }

        void RestoreVisualRest()
        {
            if (visualRoot == null)
                return;
            visualRoot.localPosition = _visualRestLocalPos;
            visualRoot.localScale = _visualRestLocalScale;
        }
    }
}
