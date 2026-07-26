using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class Level5VictoryCamera : MonoBehaviour
{
    [SerializeField] Transform target;
    [SerializeField] float duration = 3f;
    [SerializeField] float targetOrthographicSize = 2.5f;

    [Header("Circular Iris")]
    [SerializeField, Range(0f, 2f)] float startRadius = 1.2f;
    [SerializeField, Range(0f, 1f)] float endRadius = 0.24f;
    [SerializeField, Range(0.001f, 0.1f)] float edgeSoftness = 0.015f;
    [SerializeField] int overlaySortingOrder = 5000;

    Camera cam;
    Material irisMaterial;
    GameObject irisOverlay;

    static readonly int CenterId = Shader.PropertyToID("_Center");
    static readonly int RadiusId = Shader.PropertyToID("_Radius");
    static readonly int SoftnessId = Shader.PropertyToID("_Softness");

    void Awake()
    {
        cam = GetComponent<Camera>();
    }

    [ContextMenu("Preview Victory Transition")]
    void PreviewVictoryTransition()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning(
                "[VictoryCamera] Enter Play Mode before previewing the transition.");
            return;
        }

        StopAllCoroutines();
        StartCoroutine(PlayVictoryZoom());
    }

    public IEnumerator PlayVictoryZoom()
    {
        if (cam == null)
            cam = GetComponent<Camera>();
        if (cam == null || target == null)
        {
            Debug.LogError("[VictoryCamera] Camera or target is missing.");
            yield break;
        }

        EnsureIrisOverlay();

        Vector3 startPosition = transform.position;
        float startSize = cam.orthographicSize;

        Vector3 endPosition = new Vector3(
            target.position.x,
            target.position.y,
            startPosition.z);

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // 平滑起步和停止
            t = t * t * (3f - 2f * t);

            transform.position = Vector3.Lerp(
                startPosition, endPosition, t);

            cam.orthographicSize = Mathf.Lerp(
                startSize, targetOrthographicSize, t);

            UpdateIris(Mathf.Lerp(startRadius, endRadius, t));
            yield return null;
        }

        transform.position = endPosition;
        cam.orthographicSize = targetOrthographicSize;
        UpdateIris(endRadius);
    }

    void EnsureIrisOverlay()
    {
        if (irisOverlay != null)
        {
            irisOverlay.SetActive(true);
            return;
        }

        Shader shader = Shader.Find("UI/HotPotatoCircularIris");
        if (shader == null)
        {
            Debug.LogError("[VictoryCamera] Circular iris shader is missing.");
            return;
        }

        var canvasGo = new GameObject("VictoryIrisOverlay",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = overlaySortingOrder;

        var imageGo = new GameObject("BlackIrisMask",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        imageGo.transform.SetParent(canvasGo.transform, false);
        var rt = (RectTransform)imageGo.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var image = imageGo.GetComponent<Image>();
        image.color = Color.white;
        image.raycastTarget = true;

        irisMaterial = new Material(shader);
        irisMaterial.name = "Victory Circular Iris (Runtime)";
        image.material = irisMaterial;
        irisOverlay = canvasGo;

        UpdateIris(startRadius);
    }

    void UpdateIris(float radius)
    {
        if (irisMaterial == null || cam == null || target == null)
            return;

        Vector3 viewport = cam.WorldToViewportPoint(target.position);
        irisMaterial.SetVector(CenterId,
            new Vector4(viewport.x, viewport.y, 0f, 0f));
        irisMaterial.SetFloat(RadiusId, Mathf.Max(0f, radius));
        irisMaterial.SetFloat(SoftnessId, Mathf.Max(0.001f, edgeSoftness));
    }

    void OnDestroy()
    {
        if (irisMaterial != null)
            Destroy(irisMaterial);
    }
}
