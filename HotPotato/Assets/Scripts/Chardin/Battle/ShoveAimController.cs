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
        [SerializeField] LineRenderer arrowLine;
        [SerializeField] Color arrowColor = new Color(1f, 0.45f, 0.15f, 0.95f);
        [SerializeField] float arrowWidth = 0.08f;
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
            if (arrowLine != null)
                return;

            var go = new GameObject("ShoveArrow");
            go.transform.SetParent(transform, false);
            arrowLine = go.AddComponent<LineRenderer>();
            arrowLine.positionCount = 2;
            arrowLine.useWorldSpace = true;
            arrowLine.startWidth = arrowWidth;
            arrowLine.endWidth = arrowWidth * 0.4f;
            arrowLine.numCapVertices = 4;
            arrowLine.sortingOrder = 20;

            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
            arrowLine.material = new Material(shader);
            arrowLine.startColor = arrowColor;
            arrowLine.endColor = arrowColor;
        }

        public void BeginAim(List<TableSeat> validTargets, Transform from)
        {
            _validTargets = validTargets ?? new List<TableSeat>();
            _from = from;
            _hovered = null;
            _aiming = true;
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
                SetArrowVisible(true);
                arrowLine.SetPosition(0, _from.position);
                arrowLine.SetPosition(1, _hovered.BombAnchor.position);

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

        TableSeat RaycastValidEnemy()
        {
            if (_cam == null || _validTargets == null || _validTargets.Count == 0)
                return null;

            Vector3 world = _cam.ScreenToWorldPoint(Input.mousePosition);
            Vector2 point = world;

            // Prefer overlap collider
            var hits = Physics2D.OverlapPointAll(point);
            for (int i = 0; i < hits.Length; i++)
            {
                var seat = hits[i].GetComponent<TableSeat>() ?? hits[i].GetComponentInParent<TableSeat>();
                if (seat != null && _validTargets.Contains(seat) && seat.IsAlive && !seat.IsPlayer)
                    return seat;
            }

            // Fallback: nearest valid by distance
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
            if (arrowLine != null)
                arrowLine.enabled = visible;
        }
    }
}
