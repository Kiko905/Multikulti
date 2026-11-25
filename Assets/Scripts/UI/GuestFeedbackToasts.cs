using System;
using System.Collections.Generic;
using Multikulti.Buildings;
using Multikulti.Waves;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UI;

namespace Multikulti.UI
{
    /// <summary>
    /// Guest feedback toasts (SCRUM-33): short, actionable messages in the Wave screen (S2) when a need
    /// changes its state, in the same frame as the change.
    ///
    /// - BOTTLENECK: amber toast "{Building} queue growing" with a guest line, e.g. "Food takes a bit too long."
    /// - RECOVERING: green toast "{Building} is catching up" with "That's more like it!". It has its own colour and
    ///   icon, so it never looks like the warning and does not look finished.
    ///
    /// A need has at most one toast; a new state replaces it. The same toast of a need comes again only after
    /// a cooldown, so a queue that keeps crossing the threshold does not spam the screen. Toasts disappear when
    /// the wave ends. Texts come from the Localization table UI (toast.*, building.*).
    ///
    /// Graybox: the component builds its own uGUI canvas and installs itself at runtime on the NeedStateBoard,
    /// so the scene does not change. Nothing in it is a raycast target, so it never blocks a tap.
    /// </summary>
    public sealed class GuestFeedbackToasts : MonoBehaviour
    {
        const string TableName = "UI";
        const string QueueGrowingKey = "toast.queue_growing";
        const string CatchingUpKey = "toast.catching_up";
        const string BetterKey = "toast.guest.better";

        /// <summary>Seconds a toast is fully visible, before it fades out.</summary>
        public const float Lifetime = 3.5f;

        /// <summary>Seconds of the fade out.</summary>
        public const float FadeDuration = 0.4f;

        /// <summary>Seconds before the same toast of the same need may show again.</summary>
        public const float Cooldown = 8f;

        /// <summary>Most toasts on screen at once; the oldest goes first.</summary>
        public const int MaxVisible = 3;

        const float PopDuration = 0.15f;
        const int TitleSize = 30;
        const int SubtitleSize = 24;
        const float BottomOffset = 150f;

        static readonly Color k_BackgroundColor = new Color(0.05f, 0.06f, 0.08f, 0.85f);
        static readonly Color k_BottleneckColor = new Color(1f, 0.62f, 0.2f);
        static readonly Color k_RecoveringColor = new Color(0.35f, 0.9f, 0.6f);
        static readonly Color k_SubtitleColor = new Color(1f, 1f, 1f, 0.8f);

        [SerializeField] NeedStateBoard stateBoard;
        [SerializeField] WaveLoop waveLoop;

        readonly List<Toast> m_Toasts = new List<Toast>();
        readonly Dictionary<(Need, NeedState), float> m_LastShown = new Dictionary<(Need, NeedState), float>();
        readonly Dictionary<string, string> m_Texts = new Dictionary<string, string>();
        readonly List<(LocalizedString Text, LocalizedString.ChangeHandler Handler)> m_Labels =
            new List<(LocalizedString, LocalizedString.ChangeHandler)>();

        RectTransform m_SafeArea;
        RectTransform m_Stack;
        Rect m_AppliedSafeArea;
        Vector2Int m_AppliedScreenSize;
        Font m_Font;
        bool m_Subscribed;

        /// <summary>Toasts on screen (for tests).</summary>
        public int VisibleCount => m_Toasts.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var board = FindAnyObjectByType<NeedStateBoard>();
            if (board != null && board.GetComponent<GuestFeedbackToasts>() == null)
                board.gameObject.AddComponent<GuestFeedbackToasts>();
        }

        /// <summary>Toast of a need on screen (for tests).</summary>
        public bool TryGetToast(Need need, out NeedState kind, out string title, out string subtitle)
        {
            foreach (var toast in m_Toasts)
            {
                if (toast.Need != need) continue;
                kind = toast.Kind;
                title = toast.Title.text;
                subtitle = toast.Subtitle.text;
                return true;
            }

            kind = NeedState.Ok;
            title = subtitle = string.Empty;
            return false;
        }

        void Awake()
        {
            if (stateBoard == null)
                stateBoard = GetComponent<NeedStateBoard>();
            if (stateBoard == null)
                stateBoard = FindAnyObjectByType<NeedStateBoard>();
            if (waveLoop == null)
                waveLoop = FindAnyObjectByType<WaveLoop>();

            m_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildCanvas();

            Listen(QueueGrowingKey);
            Listen(CatchingUpKey);
            Listen(BetterKey);
            foreach (Need need in Enum.GetValues(typeof(Need)))
            {
                Listen(HomeHud.BuildingKey(need));
                Listen(GuestKey(need));
            }
        }

        void Start()
        {
            if (stateBoard == null || waveLoop == null)
            {
                Debug.LogWarning("Guest Feedback Toasts: no NeedStateBoard or WaveLoop in the scene, no toasts.", this);
                enabled = false;
                return;
            }

            stateBoard.StateChanged += OnNeedStateChanged;
            if (waveLoop.Machine != null)
                waveLoop.Machine.StateChanged += OnWaveStateChanged;
            m_Subscribed = true;
        }

        void OnDestroy()
        {
            if (m_Subscribed)
            {
                stateBoard.StateChanged -= OnNeedStateChanged;
                if (waveLoop.Machine != null)
                    waveLoop.Machine.StateChanged -= OnWaveStateChanged;
            }
            foreach (var (text, handler) in m_Labels)
                text.StringChanged -= handler;
        }

        void LateUpdate()
        {
            ApplySafeArea();

            if (!waveLoop.State.IsInWave())
            {
                Clear();
                return;
            }

            var now = Time.unscaledTime;
            for (var i = m_Toasts.Count - 1; i >= 0; i--)
            {
                var toast = m_Toasts[i];
                var age = now - toast.ShownAt;
                if (age >= Lifetime + FadeDuration)
                {
                    Remove(i);
                    continue;
                }

                toast.Group.alpha = age <= Lifetime ? 1f : 1f - (age - Lifetime) / FadeDuration;
                var pop = Mathf.Clamp01(age / PopDuration);
                toast.Root.localScale = Vector3.one * Mathf.Lerp(0.9f, 1f, 1f - (1f - pop) * (1f - pop));
            }
        }

        void OnWaveStateChanged(WaveState previous, WaveState next)
        {
            if (next != WaveState.WaveIncoming) return;
            Clear();
            m_LastShown.Clear();
        }

        void OnNeedStateChanged(Need need, NeedState previous, NeedState next)
        {
            if (next != NeedState.Bottleneck && next != NeedState.Recovering) return;
            if (!waveLoop.State.IsInWave()) return;

            var index = m_Toasts.FindIndex(toast => toast.Need == need);
            var now = Time.unscaledTime;
            if (m_LastShown.TryGetValue((need, next), out var lastShown) && now - lastShown < Cooldown)
            {
                // Too soon for the same toast again; an older toast of the other state would now be wrong.
                if (index >= 0 && m_Toasts[index].Kind != next)
                    Remove(index);
                return;
            }

            if (index >= 0)
                Remove(index);
            while (m_Toasts.Count >= MaxVisible)
                Remove(0);

            m_LastShown[(need, next)] = now;
            m_Toasts.Add(CreateToast(need, next, now));
        }

        Toast CreateToast(Need need, NeedState kind, float now)
        {
            var building = Text(HomeHud.BuildingKey(need));
            if (string.IsNullOrEmpty(building))
                building = need.ToString();

            var isWarning = kind == NeedState.Bottleneck;
            var accent = isWarning ? k_BottleneckColor : k_RecoveringColor;
            var title = SafeFormat(Text(isWarning ? QueueGrowingKey : CatchingUpKey), building);
            var subtitle = Text(isWarning ? GuestKey(need) : BetterKey);

            var root = CreateRect($"Toast {need} {kind}", m_Stack);
            var group = root.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            var background = root.gameObject.AddComponent<Image>();
            background.color = k_BackgroundColor;
            background.raycastTarget = false;

            var layout = root.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(0, 24, 0, 0);
            layout.spacing = 16f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            // Coloured strip with an icon: "!" for the warning, "»" for catching up (never a tick, it is not done).
            var icon = CreateRect("Icon", root);
            var iconImage = icon.gameObject.AddComponent<Image>();
            iconImage.color = accent;
            iconImage.raycastTarget = false;
            var iconElement = icon.gameObject.AddComponent<LayoutElement>();
            iconElement.minWidth = iconElement.preferredWidth = 56f;
            var glyph = CreateText("Glyph", icon, TitleSize + 6, k_BackgroundColor, TextAnchor.MiddleCenter);
            glyph.fontStyle = FontStyle.Bold;
            glyph.text = isWarning ? "!" : "»";
            var glyphRect = glyph.rectTransform;
            glyphRect.anchorMin = Vector2.zero;
            glyphRect.anchorMax = Vector2.one;
            glyphRect.offsetMin = Vector2.zero;
            glyphRect.offsetMax = Vector2.zero;

            var texts = CreateRect("Texts", root);
            var textLayout = texts.gameObject.AddComponent<VerticalLayoutGroup>();
            textLayout.padding = new RectOffset(0, 0, 10, 12);
            textLayout.spacing = 2f;
            textLayout.childControlWidth = true;
            textLayout.childControlHeight = true;
            textLayout.childForceExpandWidth = false;
            textLayout.childForceExpandHeight = false;

            var titleText = CreateText("Title", texts, TitleSize, accent, TextAnchor.MiddleLeft);
            titleText.fontStyle = FontStyle.Bold;
            titleText.text = title;
            var subtitleText = CreateText("Subtitle", texts, SubtitleSize, k_SubtitleColor, TextAnchor.MiddleLeft);
            subtitleText.fontStyle = FontStyle.Italic;
            subtitleText.text = string.IsNullOrEmpty(subtitle) ? string.Empty : $"“{subtitle}”";
            subtitleText.gameObject.SetActive(!string.IsNullOrEmpty(subtitle));

            root.localScale = Vector3.one * 0.9f;
            return new Toast(need, kind, root, group, titleText, subtitleText, now);
        }

        void Remove(int index)
        {
            // Inactive first, so the stack lays out without it already in this frame.
            var root = m_Toasts[index].Root.gameObject;
            root.SetActive(false);
            Destroy(root);
            m_Toasts.RemoveAt(index);
        }

        void Clear()
        {
            for (var i = m_Toasts.Count - 1; i >= 0; i--)
                Remove(i);
        }

        void Listen(string key)
        {
            if (m_Texts.ContainsKey(key)) return;
            m_Texts[key] = string.Empty;
            var text = new LocalizedString(TableName, key);
            LocalizedString.ChangeHandler handler = value => m_Texts[key] = value;
            text.StringChanged += handler;
            m_Labels.Add((text, handler));
        }

        string Text(string key) => m_Texts.TryGetValue(key, out var value) ? value : string.Empty;

        static string SafeFormat(string format, string building)
        {
            if (string.IsNullOrEmpty(format)) return building;
            try
            {
                return string.Format(format, building);
            }
            catch (FormatException)
            {
                return format;
            }
        }

        static string GuestKey(Need need) => need switch
        {
            Need.Rooms => "toast.guest.rooms",
            Need.Drinks => "toast.guest.drinks",
            Need.Food => "toast.guest.food",
            Need.Relax => "toast.guest.relax",
            Need.OceanView => "toast.guest.ocean_view",
            _ => throw new ArgumentOutOfRangeException(nameof(need), need, null)
        };

        // Keeps the toasts out of the notch and the rounded corners in landscape.
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
            var canvasObject = new GameObject("Guest Feedback Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above the Wave Needs Panel (100).
            canvas.sortingOrder = 110;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            m_SafeArea = CreateRect("Safe Area", canvasObject.transform);
            m_SafeArea.anchorMin = Vector2.zero;
            m_SafeArea.anchorMax = Vector2.one;
            m_SafeArea.offsetMin = Vector2.zero;
            m_SafeArea.offsetMax = Vector2.zero;

            // Bottom centre, pivot at the bottom: the newest toast is the lowest, older ones move up.
            m_Stack = CreateRect("Toasts", m_SafeArea);
            m_Stack.anchorMin = new Vector2(0.5f, 0f);
            m_Stack.anchorMax = new Vector2(0.5f, 0f);
            m_Stack.pivot = new Vector2(0.5f, 0f);
            m_Stack.anchoredPosition = new Vector2(0f, BottomOffset);

            var layout = m_Stack.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.LowerCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var fitter = m_Stack.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        static RectTransform CreateRect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        Text CreateText(string name, Transform parent, int size, Color color, TextAnchor alignment)
        {
            var text = CreateRect(name, parent).gameObject.AddComponent<Text>();
            text.font = m_Font;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        sealed class Toast
        {
            public Toast(Need need, NeedState kind, RectTransform root, CanvasGroup group, Text title, Text subtitle, float shownAt)
            {
                Need = need;
                Kind = kind;
                Root = root;
                Group = group;
                Title = title;
                Subtitle = subtitle;
                ShownAt = shownAt;
            }

            public Need Need { get; }
            public NeedState Kind { get; }
            public RectTransform Root { get; }
            public CanvasGroup Group { get; }
            public Text Title { get; }
            public Text Subtitle { get; }
            public float ShownAt { get; }
        }
    }
}
