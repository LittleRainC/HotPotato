using UnityEngine;

namespace Chardin
{
    /// <summary>
    /// 用 Unity 系统光标 API（Cursor.SetCursor）切换自定义鼠标：
    /// 平时 m1，按住左键或右键为 m2。
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public sealed class GameCursor : MonoBehaviour
    {
        static GameCursor _instance;

        [SerializeField] Texture2D normalCursor;
        [SerializeField] Texture2D pressedCursor;
        [SerializeField] int cursorSize = 64;
        // 热点：相对原图左上角的归一化坐标（指尖附近）
        [SerializeField] Vector2 normalHotspotNorm = new Vector2(0.324f, 0.167f);
        [SerializeField] Vector2 pressedHotspotNorm = new Vector2(0.393f, 0.293f);

        Texture2D _normalTex;
        Texture2D _pressedTex;
        Vector2 _normalHotspot;
        Vector2 _pressedHotspot;
        bool _pressed;
        bool _ready;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            Ensure();
        }

        public static GameCursor Ensure()
        {
            if (_instance != null)
                return _instance;

            var existing = FindObjectOfType<GameCursor>();
            if (existing != null)
            {
                _instance = existing;
                return _instance;
            }

            var go = new GameObject("GameCursor");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<GameCursor>();
            return _instance;
        }

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
            BuildCursorTextures();
            ApplyCursor(pressed: false);
        }

        void OnDestroy()
        {
            if (_instance == this)
                _instance = null;

            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);

            if (_normalTex != null)
                Destroy(_normalTex);
            if (_pressedTex != null)
                Destroy(_pressedTex);
        }

        void Update()
        {
            if (!_ready)
                return;

            bool pressed = Input.GetMouseButton(0) || Input.GetMouseButton(1);
            if (pressed == _pressed)
                return;

            _pressed = pressed;
            ApplyCursor(_pressed);
        }

        void BuildCursorTextures()
        {
            if (normalCursor == null)
                normalCursor = LoadCursorTexture("m1", "Assets/Art/美术素材/UI/m1.png");
            if (pressedCursor == null)
                pressedCursor = LoadCursorTexture("m2", "Assets/Art/美术素材/UI/m2.png");

            if (normalCursor == null || pressedCursor == null)
            {
                Debug.LogError("[GameCursor] 找不到 m1/m2 光标贴图。");
                _ready = false;
                return;
            }

            int size = Mathf.Clamp(cursorSize, 16, 128);
            _normalTex = CreateSizedCursor(normalCursor, size);
            _pressedTex = CreateSizedCursor(pressedCursor, size);
            _normalHotspot = new Vector2(normalHotspotNorm.x * size, normalHotspotNorm.y * size);
            _pressedHotspot = new Vector2(pressedHotspotNorm.x * size, pressedHotspotNorm.y * size);
            _ready = _normalTex != null && _pressedTex != null;
        }

        static Texture2D LoadCursorTexture(string resourcesName, string editorAssetPath)
        {
            // Resources 里优先拿 Texture2D（Sprite 源图同资源）
            var tex = Resources.Load<Texture2D>("UI/" + resourcesName);
            if (tex != null)
                return tex;

            var sprite = Resources.Load<Sprite>("UI/" + resourcesName);
            if (sprite != null)
                return sprite.texture;

#if UNITY_EDITOR
            tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(editorAssetPath);
            if (tex != null)
                return tex;
            var sp = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(editorAssetPath);
            if (sp != null)
                return sp.texture;
#endif
            return null;
        }

        /// <summary>
        /// 缩放到适合系统光标的尺寸。不依赖源图 Read/Write。
        /// hotspot 使用左上角原点（与 Cursor.SetCursor 一致）。
        /// </summary>
        static Texture2D CreateSizedCursor(Texture2D source, int size)
        {
            var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;

            Graphics.Blit(source, rt);
            RenderTexture.active = rt;

            var result = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                name = source.name + "_cursor" + size
            };
            result.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            result.Apply(false, false); // 保持可读，Cursor.SetCursor 需要

            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return result;
        }

        void ApplyCursor(bool pressed)
        {
            if (!_ready)
                return;

            Texture2D tex = pressed ? _pressedTex : _normalTex;
            Vector2 hotspot = pressed ? _pressedHotspot : _normalHotspot;
            // ForceSoftware：大尺寸自定义光标更稳（硬件光标常限制 32x32）
            Cursor.SetCursor(tex, hotspot, CursorMode.ForceSoftware);
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }
    }
}
