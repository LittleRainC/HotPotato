using System.Collections;
using UnityEngine;

namespace Chardin
{
    /// <summary>
    /// 炸弹程序动画：传（滑移）、塞（抛物线）、塞失败（翻转+震屏），以及落点反馈与飘字。
    /// </summary>
    public sealed class BombTransferFx : MonoBehaviour
    {
        [SerializeField] BombView bombView;
        [SerializeField] Transform shakeTarget;
        [SerializeField] float passDuration = 0.38f;
        [SerializeField] float shoveDuration = 0.48f;
        [SerializeField] float slipDuration = 0.55f;
        [SerializeField] float shoveArcHeight = 1.15f;
        [SerializeField] float screenShakeAmp = 0.12f;
        [SerializeField] float popupRise = 0.85f;
        [SerializeField] float popupDuration = 0.7f;
        [SerializeField] Color lossPopupColor = new Color(1f, 0.22f, 0.18f, 1f);
        [SerializeField] Color gainPopupColor = new Color(0.35f, 0.95f, 0.45f, 1f);

        Vector3 _shakeRest;
        bool _shakeRestCached;

        void Awake()
        {
            if (bombView == null)
                bombView = GetComponent<BombView>() ?? GetComponentInChildren<BombView>();
            if (shakeTarget == null && Camera.main != null)
                shakeTarget = Camera.main.transform;
        }

        public IEnumerator PlayPass(Transform bomb, Vector3 from, Vector3 to, TableSeat landSeat, string popup)
        {
            yield return PlayTransfer(bomb, from, to, popup, lossPopupColor, arc: false, passDuration);
            if (landSeat != null)
            {
                landSeat.PlayPassLandReact();
                yield return new WaitForSeconds(0.18f);
            }
        }

        public IEnumerator PlayShove(Transform bomb, Vector3 from, Vector3 to, TableSeat landSeat, string popup)
        {
            yield return PlayTransfer(bomb, from, to, popup, lossPopupColor, arc: true, shoveDuration);
            if (landSeat != null)
            {
                landSeat.PlayShoveLandReact();
                yield return new WaitForSeconds(0.22f);
            }
        }

        public IEnumerator PlayDefuseTransfer(Transform bomb, Vector3 from, Vector3 to, TableSeat landSeat)
        {
            yield return PlayTransfer(bomb, from, to, "+2", gainPopupColor, arc: false, passDuration);
            if (landSeat != null)
            {
                landSeat.PlayPassLandReact();
                yield return new WaitForSeconds(0.18f);
            }
        }

        public IEnumerator PlaySlip(Transform bomb, string popup = "-2")
        {
            if (bomb == null)
                yield break;

            if (bombView != null)
                bombView.SetMotionLocked(true);

            SpawnPopup(bomb, popup, lossPopupColor);

            Vector3 startPos = bomb.position;
            Quaternion startRot = bomb.rotation;
            CacheShakeRest();

            float t = 0f;
            while (t < slipDuration)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / slipDuration);
                float ease = 1f - Mathf.Pow(1f - u, 2f);

                // 原地微滑 + 360° 翻转
                float wobble = Mathf.Sin(u * Mathf.PI * 2f) * 0.18f * (1f - u);
                bomb.position = startPos + new Vector3(wobble, Mathf.Abs(Mathf.Sin(u * Mathf.PI)) * 0.12f, 0f);
                bomb.rotation = startRot * Quaternion.Euler(0f, 0f, ease * 360f);

                if (shakeTarget != null)
                {
                    float damp = (1f - u);
                    Vector2 jolt = Random.insideUnitCircle * screenShakeAmp * damp;
                    shakeTarget.localPosition = _shakeRest + (Vector3)jolt;
                }

                yield return null;
            }

            bomb.position = startPos;
            bomb.rotation = startRot;
            RestoreShake();

            if (bombView != null)
            {
                bombView.CaptureRestPosition();
                bombView.SetMotionLocked(false);
            }
        }

        IEnumerator PlayTransfer(
            Transform bomb,
            Vector3 from,
            Vector3 to,
            string popup,
            Color popupColor,
            bool arc,
            float duration)
        {
            if (bomb == null)
                yield break;

            if (bombView != null)
                bombView.SetMotionLocked(true);

            bomb.position = from;
            SpawnPopup(bomb, popup, popupColor);

            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / duration);
                float ease = arc ? SmoothStep(u) : SmoothStep(u);
                Vector3 p = Vector3.LerpUnclamped(from, to, ease);
                if (arc)
                    p.y += shoveArcHeight * 4f * u * (1f - u);
                bomb.position = p;
                yield return null;
            }

            bomb.position = to;
            bomb.rotation = Quaternion.identity;

            if (bombView != null)
            {
                bombView.CaptureRestPosition();
                bombView.SetMotionLocked(false);
            }
        }

        void SpawnPopup(Transform bomb, string text, Color color)
        {
            if (bomb == null || string.IsNullOrEmpty(text))
                return;

            var go = new GameObject("BombDeltaPopup");
            go.transform.SetParent(bomb, false);
            go.transform.localPosition = new Vector3(0f, 0.55f, 0f);

            var mesh = go.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.characterSize = 0.14f;
            mesh.fontSize = 72;
            mesh.color = color;

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                int order = 20;
                if (bombView != null)
                    order = bombView.PopupSortingOrder;
                mr.sortingOrder = order;
            }

            StartCoroutine(AnimatePopup(go.transform, mesh, color));
        }

        IEnumerator AnimatePopup(Transform popup, TextMesh mesh, Color baseColor)
        {
            Vector3 start = popup.localPosition;
            float t = 0f;
            while (t < popupDuration && popup != null)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / popupDuration);
                popup.localPosition = start + Vector3.up * (popupRise * u);
                var c = baseColor;
                c.a = 1f - u;
                mesh.color = c;
                yield return null;
            }

            if (popup != null)
                Destroy(popup.gameObject);
        }

        void CacheShakeRest()
        {
            if (shakeTarget == null || _shakeRestCached)
                return;
            _shakeRest = shakeTarget.localPosition;
            _shakeRestCached = true;
        }

        void RestoreShake()
        {
            if (shakeTarget != null && _shakeRestCached)
                shakeTarget.localPosition = _shakeRest;
        }

        static float SmoothStep(float u)
        {
            return u * u * (3f - 2f * u);
        }
    }
}
