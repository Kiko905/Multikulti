using System;
using System.Collections.Generic;
using System.Text;
using Multikulti.Buildings;
using Multikulti.Waves;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UI;

namespace Multikulti.UI
{
    /// <summary>
    /// Forecast of the next wave on the Home screen (SCRUM-22, UI handoff S1): while AwaitingWave the player reads
    /// "Next wave: Rooms 16 · Drinks 18 · Food 14" right above the Start Wave button and moves Workers by it.
    /// Shown exactly when Start Wave is offered (state "Čaká na vlnu"), hidden during the wave and after the last wave.
    /// The numbers come from WaveLoop.NextWaveForecast, so a need whose building is bought between waves joins
    /// the forecast at once (AC7a).
    ///
    /// Graybox like the Start Wave button: own uGUI canvas, built-in font, texts from the Localization table UI
    /// (wave.forecast, need.*). Nothing on it takes taps, so taps still reach the buildings.
    /// </summary>
    public sealed class WaveForecastPanel : MonoBehaviour
    {
        const string TableName = "UI";
        const string HeaderKey = "wave.forecast";
        const string Separator = " · ";

        static readonly Color k_PillColor = new Color(0f, 0f, 0f, 0.6f);
        static readonly Color k_TextColor = Color.white;

        [SerializeField] WaveLoop waveLoop;
        [SerializeField, Min(8)] int fontSize = 30;
        [SerializeField, Min(0f), Tooltip("Distance from the bottom right corner of the safe area, like the Start Wave button.")]
        float margin = 48f;
        [SerializeField, Min(0f), Tooltip("Height of the Start Wave button plus the gap; the forecast sits above it.")]
        float aboveButton = 136f;

        readonly Dictionary<string, string> m_Strings = new Dictionary<string, string>();
        readonly List<(LocalizedString text, LocalizedString.ChangeHandler handler)> m_Localized =
            new List<(LocalizedString, LocalizedString.ChangeHandler)>();

        GameObject m_Pill;
        Text m_Text;
        RectTransform m_SafeArea;
        Rect m_AppliedSafeArea;
        Vector2Int m_AppliedScreenSize;
        string m_AppliedKey;
        bool m_TextsDirty = true;

        void Awake()
        {
            if (waveLoop == null)
                waveLoop = FindAnyObjectByType<WaveLoop>();

            BuildCanvas();
            Localize(HeaderKey);
            foreach (Need need in Enum.GetValues(typeof(Need)))
                Localize(NeedKey(need));
        }

        void Start()
        {
            if (waveLoop == null)
            {
                Debug.LogError("Wave forecast: no WaveLoop in the scene.", this);
                enabled = false;
                m_Pill.SetActive(false);
            }
        }

        void OnDestroy()
        {
            foreach (var (text, handler) in m_Localized)
                text.StringChanged -= handler;
            m_Localized.Clear();
        }

        void LateUpdate()
        {
            ApplySafeArea();

            var forecast = waveLoop.Machine.CanStartWave ? waveLoop.NextWaveForecast() : null;
            var visible = forecast != null && forecast.Needs.Count > 0;
            if (m_Pill.activeSelf != visible)
                m_Pill.SetActive(visible);
            if (!visible) return;

            // Rebuild the text only when the forecast or a translation changed.
            var key = Key(forecast);
            if (!m_TextsDirty && key == m_AppliedKey) return;
            m_TextsDirty = false;
            m_AppliedKey = key;
            m_Text.text = Format(forecast);
        }

        /// <summary>"Next wave: Rooms 16 · Drinks 18 · Food 14" in the current language.</summary>
        string Format(WaveForecast forecast)
        {
            var needs = new StringBuilder();
            foreach (var need in forecast.Needs)
            {
                if (needs.Length > 0) needs.Append(Separator);
                needs.Append(Localized(NeedKey(need.Need), need.Need.ToString())).Append(' ').Append(need.Units);
            }

            var header = Localized(HeaderKey, "Next wave: {0}");
            try
            {
                return string.Format(header, needs);
            }
            catch (FormatException)
            {
                return $"{header} {needs}";
            }
        }

        static string Key(WaveForecast forecast)
        {
            var key = new StringBuilder().Append(forecast.Wave);
            foreach (var need in forecast.Needs)
                key.Append('|').Append((int)need.Need).Append(':').Append(need.Units);
            return key.ToString();
        }

        string Localized(string key, string fallback) =>
            m_Strings.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value) ? value : fallback;

        void Localize(string key)
        {
            var text = new LocalizedString(TableName, key);
            LocalizedString.ChangeHandler handler = value =>
            {
                m_Strings[key] = value;
                m_TextsDirty = true;
            };
            text.StringChanged += handler;
            m_Localized.Add((text, handler));
        }

        static string NeedKey(Need need) => need switch
        {
            Need.Rooms => "need.rooms",
            Need.Drinks => "need.drinks",
            Need.Food => "need.food",
            Need.Relax => "need.relax",
            Need.OceanView => "need.ocean_view",
            _ => throw new ArgumentOutOfRangeException(nameof(need), need, null)
        };

        // Keeps the forecast out of the notch and the rounded corners in landscape.
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

            // No GraphicRaycaster: the forecast only informs, taps go through to the island.
            var canvasObject = new GameObject("Wave Forecast Canvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
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

            // Bottom right, right above the Start Wave button; grows to the left with the text.
            var pill = CreateRect("Wave Forecast", m_SafeArea);
            pill.anchorMin = new Vector2(1f, 0f);
            pill.anchorMax = new Vector2(1f, 0f);
            pill.pivot = new Vector2(1f, 0f);
            pill.anchoredPosition = new Vector2(-margin, margin + aboveButton);
            pill.gameObject.AddComponent<Image>().color = k_PillColor;
            var layout = pill.gameObject.AddComponent<HorizontalLayoutGroup>();
            var padding = Mathf.RoundToInt(fontSize * 0.6f);
            layout.padding = new RectOffset(padding, padding, padding / 2, padding / 2);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            var fitter = pill.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var textRect = CreateRect("Text", pill);
            m_Text = textRect.gameObject.AddComponent<Text>();
            m_Text.font = font;
            m_Text.fontSize = fontSize;
            m_Text.alignment = TextAnchor.MiddleRight;
            m_Text.color = k_TextColor;
            m_Text.horizontalOverflow = HorizontalWrapMode.Overflow;
            m_Text.raycastTarget = false;

            m_Pill = pill.gameObject;
            m_Pill.SetActive(false);
        }

        static RectTransform CreateRect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }
    }
}
