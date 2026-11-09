using Multikulti.Waves;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Localization;
using UnityEngine.UI;

namespace Multikulti.UI
{
    /// <summary>
    /// Start Wave button (SCRUM-23, UI handoff S1). Shown only while AwaitingWave and a next wave exists;
    /// a tap starts the wave (AwaitingWave → WaveIncoming), the ship then sails in (ShipArrival) and the
    /// wave moves on to WaveActive.
    ///
    /// Graybox: the button builds its own uGUI canvas with the built-in font, like the Wave Needs Panel,
    /// so the scene only needs this component. It sits in the bottom right corner of the safe area, where the
    /// thumb reaches it in landscape (UI handoff §2). The text comes from the Localization table UI (wave.start).
    /// If the scene has no EventSystem, the button creates one with the Input System UI module.
    /// </summary>
    public sealed class StartWaveButton : MonoBehaviour
    {
        const string TableName = "UI";
        const string LabelKey = "wave.start";

        static readonly Color k_ButtonColor = new Color(0.16f, 0.55f, 0.32f, 0.95f);
        static readonly Color k_TextColor = Color.white;

        [SerializeField] WaveLoop waveLoop;
        [SerializeField, Min(8)] int fontSize = 44;
        [SerializeField] Vector2 size = new Vector2(380f, 120f);
        [SerializeField, Min(0f), Tooltip("Distance from the bottom right corner of the safe area.")]
        float margin = 48f;

        LocalizedString m_Label;
        LocalizedString.ChangeHandler m_LabelChanged;

        GameObject m_Button;
        Text m_Text;
        RectTransform m_SafeArea;
        Rect m_AppliedSafeArea;
        Vector2Int m_AppliedScreenSize;

        void Awake()
        {
            if (waveLoop == null)
                waveLoop = FindAnyObjectByType<WaveLoop>();

            EnsureEventSystem();
            BuildCanvas();

            m_Label = new LocalizedString(TableName, LabelKey);
            m_LabelChanged = value => m_Text.text = value;
            m_Label.StringChanged += m_LabelChanged;
        }

        void Start()
        {
            if (waveLoop == null)
            {
                Debug.LogError("Start Wave button: no WaveLoop in the scene.", this);
                enabled = false;
                m_Button.SetActive(false);
            }
        }

        void OnDestroy()
        {
            if (m_Label != null)
                m_Label.StringChanged -= m_LabelChanged;
        }

        void LateUpdate()
        {
            ApplySafeArea();

            var visible = waveLoop.Machine.CanStartWave;
            if (m_Button.activeSelf != visible)
                m_Button.SetActive(visible);
        }

        /// <summary>Starts the wave; ignored if Start Wave is not available any more (e.g. a double tap).</summary>
        public void OnStartWave()
        {
            var machine = waveLoop.Machine;
            if (!machine.CanStartWave) return;
            machine.StartWave();
        }

        static void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null) return;
            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            Debug.Log("Start Wave button: created an EventSystem (none in the scene).", eventSystem);
        }

        // Keeps the button out of the notch and the rounded corners in landscape.
        void ApplySafeArea()
        {
            var screenSize = new Vector2Int(Screen.width, Screen.height);
            var safeArea = Screen.safeArea;
            if (screenSize == m_AppliedScreenSize && safeArea == m_AppliedSafeArea) return;
            m_AppliedScreenSize = screenSize;
            m_AppliedSafeArea = safeArea;
            if (screenSize.x <= 0 || screenSize.y <= 0) return;

            m_SafeArea.anchorMin = new Vector2(safeArea.xMin / screenSize.x, safeArea.yMin / screenSize.y);
            m_SafeArea.anchorMax = new Vector2(safeArea.xMax / screenSize.x, safeArea.yMax / screenSize.y);
            m_SafeArea.offsetMin = Vector2.zero;
            m_SafeArea.offsetMax = Vector2.zero;
        }

        void BuildCanvas()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvasObject = new GameObject("Start Wave Canvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            m_SafeArea = CreateRect("Safe Area", canvasObject.transform);
            m_SafeArea.anchorMin = Vector2.zero;
            m_SafeArea.anchorMax = Vector2.one;
            m_SafeArea.offsetMin = Vector2.zero;
            m_SafeArea.offsetMax = Vector2.zero;

            // Bottom right corner: in reach of the right thumb (UI handoff §2).
            var buttonRect = CreateRect("Start Wave Button", m_SafeArea);
            buttonRect.anchorMin = new Vector2(1f, 0f);
            buttonRect.anchorMax = new Vector2(1f, 0f);
            buttonRect.pivot = new Vector2(1f, 0f);
            buttonRect.anchoredPosition = new Vector2(-margin, margin);
            buttonRect.sizeDelta = size;

            var image = buttonRect.gameObject.AddComponent<Image>();
            image.color = k_ButtonColor;
            var button = buttonRect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(OnStartWave);

            var textRect = CreateRect("Label", buttonRect);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            m_Text = textRect.gameObject.AddComponent<Text>();
            m_Text.font = font;
            m_Text.fontSize = fontSize;
            m_Text.fontStyle = FontStyle.Bold;
            m_Text.alignment = TextAnchor.MiddleCenter;
            m_Text.color = k_TextColor;
            m_Text.raycastTarget = false;

            m_Button = buttonRect.gameObject;
            m_Button.SetActive(false);
        }

        static RectTransform CreateRect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }
    }
}
