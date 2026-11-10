using UnityEngine;

namespace Multikulti.Island
{
    /// <summary>
    /// Isometric camera of the Home screen (SCRUM-21, UI handoff S1): an orthographic view from above at
    /// an angle, framed so that the whole island fits on the screen on every landscape aspect ratio
    /// (4:3 to 21:9) and stays out of the notch and out of the top bar.
    ///
    /// The framing is computed again when the screen size or the safe area changes, or after IslandMap.Refresh
    /// (a new building or construction site). Runs before the UI (execution order), so markers placed in
    /// LateUpdate already use the new framing.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class IslandCamera : MonoBehaviour
    {
        const float ReferenceHeight = 1080f;

        [SerializeField, Tooltip("Empty = Main Camera.")]
        Camera targetCamera;
        [SerializeField] IslandMap map;
        [SerializeField, Range(10f, 80f), Tooltip("Angle above the horizon.")]
        float pitch = 30f;
        [SerializeField, Tooltip("Rotation around the island. -45 keeps the Restaurant away from the Wave Needs Panel (top right).")]
        float yaw = -45f;
        [SerializeField, Min(1f)] float distance = 40f;
        [SerializeField, Min(0f), Tooltip("Reference pixels (screen 1080 high) kept free at the top of the safe area for the top bar.")]
        float topReserve = 120f;
        [SerializeField, Min(0f)] float bottomReserve = 24f;
        [SerializeField, Min(0f)] float sideReserve = 24f;
        [SerializeField, Range(0f, 0.3f), Tooltip("Extra space around the island, as a part of its size.")]
        float padding = 0.03f;
        [SerializeField, Tooltip("Shown where the sea plane ends.")]
        Color background = new Color(0.16f, 0.52f, 0.62f);

        bool m_Dirty = true;
        Vector2Int m_AppliedScreenSize;
        Rect m_AppliedSafeArea;

        void Awake()
        {
            if (targetCamera == null)
                targetCamera = Camera.main;
            if (map == null)
                map = FindAnyObjectByType<IslandMap>();
        }

        void OnEnable()
        {
            if (map != null)
                map.Refreshed += MarkDirty;
            m_Dirty = true;
        }

        void OnDisable()
        {
            if (map != null)
                map.Refreshed -= MarkDirty;
        }

        void Start()
        {
            if (targetCamera == null || map == null)
            {
                Debug.LogError("Island camera: needs a camera and an IslandMap in the scene.", this);
                enabled = false;
            }
        }

        void MarkDirty() => m_Dirty = true;

        void LateUpdate()
        {
            // The camera's own pixel size: it differs from the screen when the camera renders into a texture.
            var screenSize = new Vector2Int(targetCamera.pixelWidth, targetCamera.pixelHeight);
            var safeArea = SafeArea(screenSize);
            if (screenSize != m_AppliedScreenSize || safeArea != m_AppliedSafeArea)
                m_Dirty = true;
            if (!m_Dirty || map.FramingPoints.Count == 0) return;

            m_Dirty = false;
            m_AppliedScreenSize = screenSize;
            m_AppliedSafeArea = safeArea;
            Frame(screenSize, safeArea);
        }

        // Screen.safeArea in the camera's pixels.
        static Rect SafeArea(Vector2Int size)
        {
            if (Screen.width <= 0 || Screen.height <= 0)
                return new Rect(0f, 0f, size.x, size.y);
            var safe = Screen.safeArea;
            float sx = (float)size.x / Screen.width, sy = (float)size.y / Screen.height;
            return new Rect(safe.x * sx, safe.y * sy, safe.width * sx, safe.height * sy);
        }

        void Frame(Vector2Int screenSize, Rect safeArea)
        {
            if (screenSize.x <= 0 || screenSize.y <= 0) return;

            var rotation = Quaternion.Euler(pitch, yaw, 0f);
            var toView = Quaternion.Inverse(rotation);
            var origin = map.Center;

            // Island extents in view space (x right, y up); depth does not matter for an orthographic camera.
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (var point in map.FramingPoints)
            {
                var view = toView * (point - origin);
                min = Vector2.Min(min, view);
                max = Vector2.Max(max, view);
            }

            // Free part of the screen in viewport coordinates (0–1).
            var scale = screenSize.y / ReferenceHeight;
            var free = Rect.MinMaxRect(
                (safeArea.xMin + sideReserve * scale) / screenSize.x,
                (safeArea.yMin + bottomReserve * scale) / screenSize.y,
                (safeArea.xMax - sideReserve * scale) / screenSize.x,
                (safeArea.yMax - topReserve * scale) / screenSize.y);
            if (free.width <= 0f || free.height <= 0f)
                free = new Rect(0f, 0f, 1f, 1f);

            var aspect = (float)screenSize.x / screenSize.y;
            var content = (max - min) * (1f + padding * 2f);
            var size = Mathf.Max(content.y / (2f * free.height), content.x / (2f * free.width * aspect));

            // Move the camera so the island centre lands in the centre of the free part.
            var contentCenter = (min + max) * 0.5f;
            var freeCenter = new Vector2((free.center.x - 0.5f) * 2f * size * aspect, (free.center.y - 0.5f) * 2f * size);
            var offset = new Vector3(contentCenter.x - freeCenter.x, contentCenter.y - freeCenter.y, -distance);

            targetCamera.orthographic = true;
            targetCamera.orthographicSize = size;
            targetCamera.clearFlags = CameraClearFlags.SolidColor;
            targetCamera.backgroundColor = background;
            targetCamera.transform.SetPositionAndRotation(origin + rotation * offset, rotation);
        }
    }
}
