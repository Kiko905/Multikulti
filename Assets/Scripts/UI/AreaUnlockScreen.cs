using System;
using System.Collections.Generic;
using System.Globalization;
using Multikulti.Buildings;
using Multikulti.Economy;
using Multikulti.Unlocks;
using Multikulti.Waves;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Localization;
using UnityEngine.UI;

namespace Multikulti.UI
{
    /// <summary>
    /// S9 Area Unlock (SCRUM-112, UI handoff §C): modal that offers a new area for Coins.
    ///
    /// - Shows the area, what it adds (the need's demand, higher satisfaction potential), that a Worker comes
    ///   with it ("Includes: 1 Worker") and the price. CTA Unlock buys it through AreaUnlockService, Later closes it.
    /// - Not affordable: Unlock is not active and the screen shows the missing Coins ("Missing: 400 Coins").
    /// - One template for both unlocks (Pool after Wave 3, Bungalows after Wave 6, AC7a/AC7b): the layout is the
    ///   same, only the accent colour, the icon and the flavour line belong to the area, so the second unlock
    ///   is a second celebration, not a recycled dialog.
    /// - Opens by itself once when an unlock becomes available and the game returns to AwaitingWave (between
    ///   S5 and S1); after Later the site stays available and a tap on it opens S9 again (AreaUnlockSiteInput).
    ///
    /// Graybox: the screen builds its own uGUI canvas and installs itself at runtime on the AreaUnlockService,
    /// so the scene does not change. The dimmed background catches every tap while the screen is open.
    /// </summary>
    public sealed class AreaUnlockScreen : MonoBehaviour
    {
        const string TableName = "UI";
        const string TitleKey = "area_unlock.title";
        const string AddsKey = "area_unlock.adds";
        const string SatisfactionKey = "area_unlock.satisfaction";
        const string WorkerKey = "area_unlock.includes_worker";
        const string CostKey = "area_unlock.cost";
        const string UnlockKey = "area_unlock.unlock";
        const string LaterKey = "area_unlock.later";
        const string MissingKey = "economy.missing_coins";

        const int TitleSize = 34;
        const int AreaSize = 64;
        const int BodySize = 30;
        const int ButtonSize = 34;
        const float PanelWidth = 760f;

        static readonly Color k_DimColor = new Color(0f, 0f, 0f, 0.55f);
        static readonly Color k_PanelColor = new Color(0.07f, 0.09f, 0.12f, 0.96f);
        static readonly Color k_TextColor = Color.white;
        static readonly Color k_MutedColor = new Color(1f, 1f, 1f, 0.7f);
        static readonly Color k_MissingColor = new Color(1f, 0.45f, 0.4f);
        static readonly Color k_LaterColor = new Color(1f, 1f, 1f, 0.12f);
        static readonly Color k_DisabledColor = new Color(0.35f, 0.37f, 0.4f);

        [SerializeField] AreaUnlockService service;
        [SerializeField] ResortBalances balances;

        readonly Dictionary<string, string> m_Texts = new Dictionary<string, string>();
        readonly List<(LocalizedString Text, LocalizedString.ChangeHandler Handler)> m_Labels =
            new List<(LocalizedString, LocalizedString.ChangeHandler)>();
        readonly HashSet<Need> m_AutoShown = new HashSet<Need>();

        WaveLoop m_WaveLoop;
        GameObject m_Root;
        RectTransform m_SafeArea;
        Rect m_AppliedSafeArea;
        Vector2Int m_AppliedScreenSize;
        Font m_Font;

        Image m_Accent;
        Text m_Icon;
        Text m_Title;
        Text m_Area;
        Text m_Flavour;
        Text m_Adds;
        Text m_Satisfaction;
        Text m_Worker;
        Text m_Cost;
        Text m_Missing;
        Button m_UnlockButton;
        Image m_UnlockImage;
        Text m_UnlockText;
        Text m_LaterText;

        AreaUnlock? m_Shown;
        int m_ShownCoins = -1;
        bool m_TextsChanged;

        /// <summary>True while the screen is on (for tests).</summary>
        public bool IsVisible => m_Root != null && m_Root.activeSelf;

        /// <summary>Area on the screen (for tests).</summary>
        public AreaUnlock? Shown => IsVisible ? m_Shown : null;

        /// <summary>True if Unlock can be pressed (for tests).</summary>
        public bool UnlockInteractable => m_UnlockButton != null && m_UnlockButton.interactable;

        /// <summary>Texts on the screen from top to bottom (for tests).</summary>
        public string[] VisibleTexts => new[]
        {
            m_Title.text, m_Area.text, m_Flavour.text, m_Adds.text, m_Satisfaction.text, m_Worker.text, m_Cost.text,
            m_Missing.gameObject.activeSelf ? m_Missing.text : string.Empty, m_UnlockText.text, m_LaterText.text
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var service = FindAnyObjectByType<AreaUnlockService>();
            if (service != null && service.GetComponent<AreaUnlockScreen>() == null)
                service.gameObject.AddComponent<AreaUnlockScreen>();
        }

        void Awake()
        {
            if (service == null)
                service = GetComponent<AreaUnlockService>();
            if (service == null)
                service = FindAnyObjectByType<AreaUnlockService>();
            if (balances == null)
                balances = FindAnyObjectByType<ResortBalances>();
            m_WaveLoop = FindAnyObjectByType<WaveLoop>();

            m_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            EnsureEventSystem();
            BuildCanvas();

            foreach (var key in new[] { TitleKey, AddsKey, SatisfactionKey, WorkerKey, CostKey, UnlockKey, LaterKey, MissingKey })
                Listen(key);
            foreach (var need in new[] { Need.Relax, Need.OceanView })
            {
                Listen(HomeHud.BuildingKey(need));
                Listen(NeedKey(need));
                Listen(FlavourKey(need));
            }
        }

        void Start()
        {
            if (service == null)
            {
                Debug.LogWarning("Area Unlock screen: no AreaUnlockService in the scene.", this);
                enabled = false;
                return;
            }

            service.Requested += OnRequested;
            service.Closed += Hide;
            if (m_WaveLoop != null && m_WaveLoop.Machine != null)
                m_WaveLoop.Machine.StateChanged += OnWaveStateChanged;

            // A loaded game may already wait between waves with a fresh unlock.
            OfferNewUnlock();
        }

        void OnDestroy()
        {
            if (service != null)
            {
                service.Requested -= OnRequested;
                service.Closed -= Hide;
            }
            if (m_WaveLoop != null && m_WaveLoop.Machine != null)
                m_WaveLoop.Machine.StateChanged -= OnWaveStateChanged;
            foreach (var (text, handler) in m_Labels)
                text.StringChanged -= handler;
        }

        void LateUpdate()
        {
            ApplySafeArea();
            if (!IsVisible || !m_Shown.HasValue) return;

            // Android back / Escape = Later.
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                OnLater();
                return;
            }

            var coins = balances != null ? balances.Coins : 0;
            if (coins != m_ShownCoins || m_TextsChanged)
                Fill(m_Shown.Value);
        }

        // Between S5 and S1: the first time an unlock is available, S9 opens by itself (UI handoff, flow).
        void OnWaveStateChanged(WaveState previous, WaveState next)
        {
            if (next == WaveState.AwaitingWave)
                OfferNewUnlock();
        }

        void OfferNewUnlock()
        {
            if (service.IsOpen || service.WaveState != WaveState.AwaitingWave) return;
            foreach (var unlock in service.Unlocks.All)
            {
                if (service.StateOf(unlock.Need) != AreaUnlockState.Available || !m_AutoShown.Add(unlock.Need)) continue;
                service.TryOpen(unlock.Need);
                return;
            }
        }

        void OnRequested(AreaUnlock unlock)
        {
            // Opened from the site too: it does not need to open by itself any more.
            m_AutoShown.Add(unlock.Need);
            m_Shown = unlock;
            Fill(unlock);
            m_Root.SetActive(true);
        }

        void Hide()
        {
            m_Shown = null;
            if (m_Root != null)
                m_Root.SetActive(false);
        }

        /// <summary>CTA Unlock: buys the area; the service closes the screen after the purchase.</summary>
        public void OnUnlock()
        {
            if (!m_Shown.HasValue) return;
            if (!service.TryBuy(m_Shown.Value.Need))
                Fill(m_Shown.Value);
        }

        /// <summary>CTA Later: closes the screen; the site stays available.</summary>
        public void OnLater() => service.Close();

        void Fill(AreaUnlock unlock)
        {
            m_TextsChanged = false;
            m_ShownCoins = balances != null ? balances.Coins : 0;

            var accent = AccentOf(unlock.Need);
            m_Accent.color = accent;
            m_Icon.text = IconOf(unlock.Need);
            m_Title.text = Text(TitleKey);
            m_Title.color = accent;
            m_Area.text = Fallback(Text(HomeHud.BuildingKey(unlock.Need)), unlock.Area);
            m_Flavour.text = Text(FlavourKey(unlock.Need));
            m_Adds.text = Format(Text(AddsKey), Fallback(Text(NeedKey(unlock.Need)), unlock.Need.ToString()));
            m_Satisfaction.text = Text(SatisfactionKey);
            m_Worker.text = Text(WorkerKey);
            m_Cost.text = Format(Text(CostKey), Amount(unlock.Price));

            var missing = service.MissingCoins(unlock.Need);
            m_Missing.text = Format(Text(MissingKey), Amount(missing));
            m_Missing.gameObject.SetActive(missing > 0);

            var canBuy = service.CanBuy(unlock.Need);
            m_UnlockButton.interactable = canBuy;
            m_UnlockImage.color = canBuy ? accent : k_DisabledColor;
            m_UnlockText.text = Text(UnlockKey);
            m_LaterText.text = Text(LaterKey);
        }

        // Each area has its own accent and icon; the layout stays the same (AC7b).
        static Color AccentOf(Need need) => need == Need.OceanView
            ? new Color(1f, 0.55f, 0.35f)
            : new Color(0.3f, 0.8f, 0.95f);

        static string IconOf(Need need) => need == Need.OceanView ? "⌂" : "≈";

        static string NeedKey(Need need) => need == Need.OceanView ? "need.ocean_view" : "need.relax";

        static string FlavourKey(Need need) => need == Need.OceanView ? "area_unlock.flavour.ocean_view" : "area_unlock.flavour.relax";

        static string Amount(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

        static string Fallback(string text, string fallback) => string.IsNullOrEmpty(text) ? fallback : text;

        static string Format(string format, string value)
        {
            if (string.IsNullOrEmpty(format)) return value;
            try
            {
                return string.Format(format, value);
            }
            catch (FormatException)
            {
                return format;
            }
        }

        void Listen(string key)
        {
            if (m_Texts.ContainsKey(key)) return;
            m_Texts[key] = string.Empty;
            var text = new LocalizedString(TableName, key);
            LocalizedString.ChangeHandler handler = value =>
            {
                m_Texts[key] = value;
                m_TextsChanged = true;
            };
            text.StringChanged += handler;
            m_Labels.Add((text, handler));
        }

        string Text(string key) => m_Texts.TryGetValue(key, out var value) ? value : string.Empty;

        static void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        // Keeps the panel out of the notch and the rounded corners in landscape.
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
            var canvasObject = new GameObject("Area Unlock Canvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Modal: above the HUD (90), the Start Wave button (100) and the toasts (110).
            canvas.sortingOrder = 200;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            // The dim covers the whole screen (also under the notch) and catches every tap under the modal.
            var dim = CreateRect("Dim", canvasObject.transform);
            Stretch(dim);
            dim.gameObject.AddComponent<Image>().color = k_DimColor;
            m_Root = dim.gameObject;

            m_SafeArea = CreateRect("Safe Area", dim);
            Stretch(m_SafeArea);

            var panel = CreateRect("S9 Area Unlock", m_SafeArea);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.gameObject.AddComponent<Image>().color = k_PanelColor;
            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(0, 0, 0, 40);
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            panel.sizeDelta = new Vector2(PanelWidth, 0f);

            // Accent band with the area icon: the part that makes Pool and Bungalows look different.
            var band = CreateRect("Accent", panel);
            m_Accent = band.gameObject.AddComponent<Image>();
            var bandElement = band.gameObject.AddComponent<LayoutElement>();
            bandElement.minHeight = bandElement.preferredHeight = 120f;
            m_Icon = CreateText("Icon", band, 84, FontStyle.Bold, k_PanelColor, TextAnchor.MiddleCenter);
            Stretch(m_Icon.rectTransform);

            var body = CreateRect("Body", panel);
            var bodyLayout = body.gameObject.AddComponent<VerticalLayoutGroup>();
            bodyLayout.padding = new RectOffset(56, 56, 24, 8);
            bodyLayout.spacing = 10f;
            bodyLayout.childAlignment = TextAnchor.UpperCenter;
            bodyLayout.childControlWidth = true;
            bodyLayout.childControlHeight = true;
            bodyLayout.childForceExpandWidth = true;
            bodyLayout.childForceExpandHeight = false;

            m_Title = CreateText("Title", body, TitleSize, FontStyle.Bold, k_TextColor, TextAnchor.MiddleCenter);
            m_Area = CreateText("Area", body, AreaSize, FontStyle.Bold, k_TextColor, TextAnchor.MiddleCenter);
            m_Flavour = CreateText("Flavour", body, BodySize - 4, FontStyle.Italic, k_MutedColor, TextAnchor.MiddleCenter);
            Spacer(body, 14f);
            m_Adds = CreateText("Adds", body, BodySize, FontStyle.Normal, k_TextColor, TextAnchor.MiddleLeft);
            m_Satisfaction = CreateText("Satisfaction", body, BodySize, FontStyle.Normal, k_TextColor, TextAnchor.MiddleLeft);
            m_Worker = CreateText("Includes Worker", body, BodySize, FontStyle.Bold, k_TextColor, TextAnchor.MiddleLeft);
            Spacer(body, 14f);
            m_Cost = CreateText("Cost", body, BodySize + 4, FontStyle.Bold, k_TextColor, TextAnchor.MiddleCenter);
            m_Missing = CreateText("Missing", body, BodySize, FontStyle.Bold, k_MissingColor, TextAnchor.MiddleCenter);

            var buttons = CreateRect("Buttons", panel);
            var row = buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(56, 56, 16, 0);
            row.spacing = 24f;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = true;
            row.childForceExpandHeight = false;

            // Unlock is the main CTA: wider, in the accent colour, on the right (in reach of the right thumb).
            var later = CreateButton("Later", buttons, k_LaterColor, OnLater, out m_LaterText);
            later.GetComponent<LayoutElement>().flexibleWidth = 1f;
            m_UnlockButton = CreateButton("Unlock", buttons, k_DisabledColor, OnUnlock, out m_UnlockText);
            m_UnlockImage = m_UnlockButton.GetComponent<Image>();
            m_UnlockButton.GetComponent<LayoutElement>().flexibleWidth = 1.6f;
            m_UnlockText.color = k_PanelColor;

            m_Root.SetActive(false);
        }

        Button CreateButton(string name, Transform parent, Color color, UnityEngine.Events.UnityAction onClick, out Text label)
        {
            var rect = CreateRect(name + " Button", parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            // The colour of the button itself shows the state; no extra grey tint.
            var colors = button.colors;
            colors.disabledColor = Color.white;
            button.colors = colors;
            button.onClick.AddListener(onClick);
            var element = rect.gameObject.AddComponent<LayoutElement>();
            element.minHeight = element.preferredHeight = 96f;

            label = CreateText("Label", rect, ButtonSize, FontStyle.Bold, k_TextColor, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);
            return button;
        }

        static void Spacer(Transform parent, float height)
        {
            var element = CreateRect("Spacer", parent).gameObject.AddComponent<LayoutElement>();
            element.minHeight = element.preferredHeight = height;
        }

        Text CreateText(string name, Transform parent, int size, FontStyle style, Color color, TextAnchor alignment)
        {
            var text = CreateRect(name, parent).gameObject.AddComponent<Text>();
            text.font = m_Font;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        static RectTransform CreateRect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }
    }
}
