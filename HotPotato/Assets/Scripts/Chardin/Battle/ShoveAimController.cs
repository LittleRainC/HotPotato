using System;
using System.Collections.Generic;
using UnityEngine;

namespace Chardin
{
    /// <summary>
    /// 「塞」瞄准：悬停敌人显示箭头，左键确认，右键取消。
    /// </summary>
    public sealed class ShoveAimController : MonoBehaviour
    {
        [SerializeField] SpriteRenderer arrowRenderer;
        [SerializeField] Sprite arrowSprite;
        [SerializeField] float arrowThicknessScale = 0.55f;
        [SerializeField] float arrowScaleMultiplier = 2f;
        [SerializeField] float tipInset = 0.2f;
        [SerializeField] int sortingOrder = 20;
        [SerializeField] LayerMask enemyMask = ~0;

        Camera _cam;
        Transform _from;
        List<TableSeat> _validTargets;
        TableSeat _hovered;
        bool _aiming;

        public event Action<TableSeat> Confirmed;
        public event Action Cancelled;

        public bool IsAiming => _aiming;

        void Awake()
        {
            _cam = Camera.main;
            EnsureArrow();
            SetArrowVisible(false);
        }

        void EnsureArrow()
        {
            if (arrowRenderer == null)
            {
                var go = new GameObject("ShoveArrow");
                go.transform.SetParent(transform, false);
                arrowRenderer = go.AddComponent<SpriteRenderer>();
            }

            if (arrowSprite == null)
                arrowSprite = LoadArrowSprite();

            arrowRenderer.sprite = arrowSprite;
            arrowRenderer.sortingOrder = sortingOrder;
            arrowRenderer.color = Color.white;
        }

        static Sprite LoadArrowSprite()
        {
            Sprite fromResources = Resources.Load<Sprite>("UI/Arrow");
            if (fromResources != null)
                return fromResources;

#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/Art/美术素材/UI/Arrow.png");
#else
            return null;
#endif
        }

        public void BeginAim(List<TableSeat> validTargets, Transform from)
        {
            _validTargets = validTargets ?? new List<TableSeat>();
            _from = from;
            _hovered = null;
            _aiming = true;
            EnsureArrow();
            SetArrowVisible(false);
        }

        public void CancelAim()
        {
            if (!_aiming)
                return;
            _aiming = false;
            _hovered = null;
            SetArrowVisible(false);
            Cancelled?.Invoke();
        }

        void Update()
        {
            if (!_aiming)
                return;

            if (_cam == null)
                _cam = Camera.main;

            if (Input.GetMouseButtonDown(1))
            {
                _aiming = false;
                _hovered = null;
                SetArrowVisible(false);
                Cancelled?.Invoke();
                return;
            }

            _hovered = RaycastValidEnemy();
            if (_hovered != null && _from != null)
            {
                UpdateArrowTransform(_from.position, _hovered.BombAnchor.position);
                SetArrowVisible(true);

                if (Input.GetMouseButtonDown(0))
                {
                    var target = _hovered;
                    _aiming = false;
                    _hovered = null;
                    SetArrowVisible(false);
                    Confirmed?.Invoke(target);
                }
            }
            else
            {
                SetArrowVisible(false);
            }
        }

        void UpdateArrowTransform(Vector3 from, Vector3 to)
        {
            if (arrowRenderer == null || arrowRenderer.sprite == null)
                return;

            Vector3 delta = to - from;
            float dist = delta.magnitude;
            if (dist < 0.05f)
            {
                SetArrowVisible(false);
                return;
            }

            Vector3 dir = delta / dist;
            Vector3 start = from + dir * tipInset;
            Vector3 end = to - dir * tipInset;
            Vector3 span = end - start;
            float length = span.magnitude;
            if (length < 0.05f)
            {
                SetArrowVisible(false);
                return;
            }

            // 素材默认从下指向上（+Y），Atan2 以 +X 为 0°，故减 90°
            float angle = Mathf.Atan2(span.y, span.x) * Mathf.Rad2Deg - 90f;
            float naturalLength = Mathf.Max(0.001f, arrowRenderer.sprite.bounds.size.y);
            float mul = Mathf.Max(0.01f, arrowScaleMultiplier);

            Transform t = arrowRenderer.transform;
            t.position = start + span * 0.5f;
            t.rotation = Quaternion.Euler(0f, 0f, angle);
            // X = 厚度（*2），Y = 沿指向刚好铺满 from→to
            t.localScale = new Vector3(
                arrowThicknessScale * mul,
                length / naturalLength,
                1f);
        }

        TableSeat RaycastValidEnemy()
        {
            if (_cam == null || _validTargets == null || _validTargets.Count == 0)
                return null;

            Vector3 world = _cam.ScreenToWorldPoint(Input.mousePosition);
            Vector2 point = world;

            var hits = Physics2D.OverlapPointAll(point);
            for (int i = 0; i < hits.Length; i++)
            {
                var seat = hits[i].GetComponent<TableSeat>() ?? hits[i].GetComponentInParent<TableSeat>();
                if (seat != null && _validTargets.Contains(seat) && seat.IsAlive && !seat.IsPlayer)
                    return seat;
            }

            TableSeat best = null;
            float bestDist = 0.75f;
            for (int i = 0; i < _validTargets.Count; i++)
            {
                var s = _validTargets[i];
                if (s == null || !s.IsAlive) continue;
                float d = Vector2.Distance(point, s.BombAnchor.position);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = s;
                }
            }
            return best;
        }

        void SetArrowVisible(bool visible)
        {
            if (arrowRenderer != null)
                arrowRenderer.enabled = visible;
        }
    }
}
