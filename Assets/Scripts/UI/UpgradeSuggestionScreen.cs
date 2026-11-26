using System;
using System.Collections.Generic;
using System.Globalization;
using Multikulti.Buildings;
using Multikulti.Config;
using Multikulti.Economy;
using Multikulti.Waves;
using Multikulti.Workers;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Localization;
using UnityEngine.UI;

namespace Multikulti.UI
{
    /// <summary>
    /// S5 Upgrade Suggestion (SCRUM-48, UI handoff §A "S5 — Upgrade Suggestion", US11, US12): after Claim Rewards the
    /// game suggests the next logical upgrade, tied to the bottleneck the player has just seen.
    ///
    /// - AC6: the suggested building is the one whose need was in BOTTLENECK the longest in the wave
    ///   (WaveCompleteScreen.LongestBottleneck, the top line of Needs improvement on S4). PlaytestMetricsRecorder
    ///   writes upgrade_offered for the same building.
    /// - Shows the reason (how long the queue grew), before/after in seats ("Seats 3 → 5", the only thing an upgrade
    ///   changes in the prototype), the price in Coins and the Resort Value gain (SCRUM-56).
    ///   Not affordable: Upgrade Now is not active and the screen shows "Missing: … Coins".
    /// - CTA Upgrade Now buys the upgrade through UpgradeService, Later skips it; both end the Upgrade state
    ///   (Machine.FinishUpgrade, Upgrade → AwaitingWave), so S9 Area Unlock can follow and then S1.
    /// - Nothing to suggest (no bottleneck in the wave, or that building is already at max level): S5 is skipped
    ///   and the game goes on to AwaitingWave by itself.
    /// - Never a pushy tone (UI handoff "Nikdy: agresívny push"): Later is always there, also Escape / back.
    ///
    /// Graybox: the screen builds its own uGUI canvas and installs itself at runtime on the UpgradeService,
    /// so the scene does not change. The dimmed background catches every tap while the screen is open.
    /// </summary>
    public sealed class UpgradeSuggestionScreen : MonoBehaviour
    {
        const string TableName = "UI";
        const string TitleKey = "upgrade.title";
        const string ReasonKey = "upgrade.reason";
        const string SeatsKey = "upgrade.seats";
        const string LevelKey = "upgrade.level";
        const string HintKey = "upgrade.hint";
        const string CostKey = "upgrade.cost";
        const string ResortValueKey = "upgrade.resort_value";
        const string UpgradeNowKey = "upgrade.upgrade_now";
        const string LaterKey = "upgrade.later";
        const string MissingKey = "economy.missing_coins";

        const int TitleSize = 34;
        const int BuildingSize = 64;
        const int BodySize = 30;
        const int SeatsSize = 44;
        const int ButtonSize = 34;
        const float PanelWidth = 820f;
        const float SeatSize = 44f;

        static readonly Color k_DimColor = new Color(0f, 0f, 0f, 0.55f);
        // Fully opaque: the project renders in linear colour space, where the bright island would show through.
        static readonly Color k_PanelColor = new Color(0.07f, 0.09f, 0.12f, 1f);
        static readonly Color k_AccentColor = new Color(0.45f, 0.85f, 0.5f);
        static readonly Color k_ReasonColor = new Color(1f, 0.72f, 0.3f);
        static readonly Color k_TextColor = Color.white;
        static readonly Color k_MutedColor = new Color(1f, 1f, 1f, 0.7f);
        static readonly Color k_SeatColor = new Color(1f, 1f, 1f, 0.85f);
        static readonly Color k_EmptySeatColor = new Color(1f, 1f, 1f, 0.12f);
        static readonly Color k_MissingColor = new Color(1f, 0.45f, 0.4f);
        static readonly Color k_ResortValueColor = new Color(0.75f, 0.6f, 1f);
        static readonly Color k_LaterColor = new Color(1f, 1f, 1f, 0.12f);
        static readonly Color k_DisabledColor = new Color(0.35f, 0.37f, 0.4f);

        [SerializeField] UpgradeService upgrades;
        [SerializeField] WaveLoop waveLoop;
        [SerializeField] WorkerManager workers;
        [SerializeField] ResortBalances balances;
        [SerializeField] ResortValueService resortValue;
        [SerializeField] WaveCompleteScreen results;

        readonly Dictionary<string, string> m_Texts = new Dictionary<string, string>();
        readonly List<(LocalizedString Text, LocalizedString.ChangeHandler Handler)> m_Labels =
            new List<(LocalizedString, LocalizedString.ChangeHandler)>();
        readonly List<Image> m_Seats = new List<Image>();

        GameObject m_Root;
        RectTransform m_SafeArea;
        Rect m_AppliedSafeArea;
        Vector2Int m_AppliedScreenSize;
        Font m_Font;

        Text m_Title;
        Text m_Building;
        Text m_Reason;
        Text m_SeatsText;
        Text m_Level;
        Text m_Hint;
        Text m_Cost;
        Text m_ResortValue;
        Text m_Missing;
        Button m_UpgradeButton;
        Image m_UpgradeImage;
        Text m_UpgradeText;
        Text m_LaterText;

        Building m_Shown;
        int m_ShownCoins = -1;
        bool m_TextsChanged;
        bool m_SkipPending;

        /// <summary>True while the screen is on (for tests).</summary>
        public bool IsVisible => m_Root != null && m_Root.activeSelf;

        /// <summary>Suggested building on the screen (for tests).</summary>
        public Building Shown => IsVisible ? m_Shown : null;

        /// <summary>True if Upgrade Now can be pressed (for tests).</summary>
        public bool UpgradeInteractable => m_UpgradeButton != null && m_UpgradeButton.interactable;

        /// <summary>Seats drawn as taken now and as added by the upgrade (for tests).</summary>
        public (int Now, int Added) SeatsShown
        {
            get
            {
                int now = 0, added = 0;
                foreach (var seat in m_Seats)
                {
                    if (!seat.gameObject.activeSelf) continue;
                    if (seat.color == k_SeatColor) now++;
                    else if (seat.color == k_AccentColor) added++;
                }
                return (now, added);
            }
        }

        /// <summary>Texts on the screen from top to bottom (for tests).</summary>
        public string[] VisibleTexts => new[]
        {
            m_Title.text, m_Building.text, m_Reason.text, m_SeatsText.text, m_Level.text, m_Hint.text, m_Cost.text,
            m_ResortValue.gameObject.activeSelf ? m_ResortValue.text : string.Empty,
            m_Missing.gameObject.activeSelf ? m_Missing.text : string.Empty, m_UpgradeText.text, m_LaterText.text
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var service = FindAnyObjectByType<UpgradeService>();
            if (service != null && service.GetComponent<UpgradeSuggestionScreen>() == null)
                service.gameObject.AddComponent<UpgradeSuggestionScreen>();
        }

        void Awake()
        {
            if (upgrades == null)
                upgrades = GetComponent<UpgradeService>();
            if (upgrades == null)
                upgrades = FindAnyObjectByType<UpgradeService>();
            if (waveLoop == null)
                waveLoop = FindAnyObjectByType<WaveLoop>();
            if (workers == null)
                workers = FindAnyObjectByType<WorkerManager>();
            if (balances == null)
                balances = FindAnyObjectByType<ResortBalances>();

            m_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            EnsureEventSystem();
            BuildCanvas();

            foreach (var key in new[] { TitleKey, ReasonKey, SeatsKey, LevelKey, HintKey, CostKey, ResortValueKey, UpgradeNowKey, LaterKey, MissingKey })
                Listen(key);
            foreach (Need need in Enum.GetValues(typeof(Need)))
                Listen(HomeHud.BuildingKey(need));
        }

        // Start: WaveLoop.Machine is created in its Awake, S4 is installed at runtime like this screen.
        void Start()
        {
            if (results == null)
                results = FindAnyObjectByType<WaveCompleteScreen>();
            if (resortValue == null)
                resortValue = FindAnyObjectByType<ResortValueService>();
            if (upgrades == null || waveLoop == null || waveLoop.Machine == null)
            {
                Debug.LogWarning("Upgrade Suggestion screen: no UpgradeService or WaveLoop in the scene.", this);
                enabled = false;
                return;
            }

            waveLoop.Machine.StateChanged += OnWaveStateChanged;
            if (waveLoop.State == WaveState.Upgrade)
                Offer();
        }

        void OnDestroy()
        {
            if (waveLoop != null && waveLoop.Machine != null)
                waveLoop.Machine.StateChanged -= OnWaveStateChanged;
            foreach (var (text, handler) in m_Labels)
                text.StringChanged -= handler;
        }

        void Update()
        {
            // Skipped outside the StateChanged event, so every listener of Claim sees the Upgrade state first.
            if (!m_SkipPending) return;
            m_SkipPending = false;
            if (waveLoop.State == WaveState.Upgrade)
            {
                Debug.Log($"Wave {waveLoop.Wave}: no upgrade to suggest (no bottleneck or already max level), S5 skipped");
                waveLoop.Machine.FinishUpgrade();
            }
        }

        void LateUpdate()
        {
            ApplySafeArea();
            if (!IsVisible || m_Shown == null) return;

            // Android back / Escape = Later.
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                OnLater();
                return;
            }

            var coins = balances != null ? balances.Coins : 0;
            if (coins != m_ShownCoins || m_TextsChanged)
                Fill(m_Shown);
        }

        void OnWaveStateChanged(WaveState previous, WaveState next)
        {
            if (next == WaveState.Upgrade)
                Offer();
            else if (next != WaveState.WavePaused)
                Hide();
        }

        /// <summary>
        /// AC6: the building of the need that was in BOTTLENECK the longest, if it can still be upgraded;
        /// null when there is nothing to suggest.
        /// </summary>
        public Building Suggested()
        {
            var need = results != null ? results.LongestBottleneck : null;
            if (need == null || workers == null) return null;
            foreach (var building in workers.Buildings)
            {
                if (building != null && building.Need == need.Value && !building.IsMaxLevel)
                    return building;
            }
            return null;
        }

        void Offer()
        {
            var building = Suggested();
            if (building == null)
            {
                Hide();
                m_SkipPending = true;
                return;
            }

            m_Shown = building;
            Fill(building);
            m_Root.SetActive(true);
        }

        void Hide()
        {
            m_Shown = null;
            if (m_Root != null)
                m_Root.SetActive(false);
        }

        /// <summary>CTA Upgrade Now: buys the upgrade and goes on to AwaitingWave.</summary>
        public void OnUpgradeNow()
        {
            if (m_Shown == null || waveLoop.State != WaveState.Upgrade) return;
            if (!upgrades.TryUpgrade(m_Shown))
            {
                Fill(m_Shown);
                return;
            }
            Debug.Log($"Wave {waveLoop.Wave}: upgraded {m_Shown.Need} to L{m_Shown.Level} from S5");
            waveLoop.Machine.FinishUpgrade();
        }

        /// <summary>CTA Later: skips the upgrade and goes on to AwaitingWave; the upgrade stays possible between waves.</summary>
        public void OnLater()
        {
            if (waveLoop.State != WaveState.Upgrade) return;
            waveLoop.Machine.FinishUpgrade();
        }

        void Fill(Building building)
        {
            m_TextsChanged = false;
            m_ShownCoins = balances != null ? balances.Coins : 0;

            var buildingName = Fallback(Text(HomeHud.BuildingKey(building.Need)), building.Need.ToString());
            var capacities = WaveConfig.Current.Buildings.CapacityByLevel;
            var now = capacities[building.Level - 1];
            var after = capacities[building.Level];
            var max = capacities[capacities.Count - 1];
            var seconds = results != null ? Mathf.Max(1, Mathf.RoundToInt(results.BottleneckSeconds(building.Need))) : 0;

            m_Title.text = Text(TitleKey);
            m_Building.text = buildingName;
            m_Reason.text = Format(Text(ReasonKey), buildingName, seconds.ToString(CultureInfo.InvariantCulture));
            m_SeatsText.text = Format(Text(SeatsKey), now.ToString(CultureInfo.InvariantCulture), after.ToString(CultureInfo.InvariantCulture));
            m_Level.text = Format(Text(LevelKey), building.Level.ToString(CultureInfo.InvariantCulture),
                (building.Level + 1).ToString(CultureInfo.InvariantCulture));
            m_Hint.text = Text(HintKey);
            ShowSeats(now, after, max);

            m_Cost.text = Format(Text(CostKey), Amount(upgrades.PriceOf(building)));
            // Resort Value gain (UI handoff S5, SCRUM-56): what the upgrade adds to the status of the resort.
            var gain = resortValue != null ? resortValue.UpgradeGain(building) : 0;
            m_ResortValue.text = Format(Text(ResortValueKey), Amount(gain));
            m_ResortValue.gameObject.SetActive(gain > 0);
            var missing = upgrades.MissingCoins(building);
            m_Missing.text = Format(Text(MissingKey), Amount(missing));
            m_Missing.gameObject.SetActive(missing > 0);

            var canBuy = upgrades.CanUpgrade(building);
            m_UpgradeButton.interactable = canBuy;
            m_UpgradeImage.color = canBuy ? k_AccentColor : k_DisabledColor;
            m_UpgradeText.text = Text(UpgradeNowKey);
            m_LaterText.text = Text(LaterKey);
        }

        // The seats as the player sees them on the building: taken now, added by the upgrade, still possible later.
        void ShowSeats(int now, int after, int max)
        {
            for (var i = 0; i < m_Seats.Count; i++)
            {
                m_Seats[i].gameObject.SetActive(i < max);
                m_Seats[i].color = i < now ? k_SeatColor : i < after ? k_AccentColor : k_EmptySeatColor;
            }
        }

        static string Amount(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

        static string Fallback(string text, string fallback) => string.IsNullOrEmpty(text) ? fallback : text;

        static string Format(string format, params object[] values)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;
            try
            {
                return string.Format(format, values);
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
            var canvasObject = new GameObject("Upgrade Suggestion Canvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Like S4: above the HUD (90), the wave panels (100) and the toasts (110); below S9 Area Unlock (200).
            canvas.sortingOrder = 150;
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

            var panel = CreateRect("S5 Upgrade Suggestion", m_SafeArea);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.gameObject.AddComponent<Image>().color = k_PanelColor;
            var layout = Vertical(panel.gameObject, new RectOffset(0, 0, 0, 40), 0f);
            layout.childAlignment = TextAnchor.UpperCenter;
            var fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            panel.sizeDelta = new Vector2(PanelWidth, 0f);

            var band = CreateRect("Accent", panel);
            band.gameObject.AddComponent<Image>().color = k_AccentColor;
            var bandElement = band.gameObject.AddComponent<LayoutElement>();
            bandElement.minHeight = bandElement.preferredHeight = 12f;

            var body = CreateRect("Body", panel);
            Vertical(body.gameObject, new RectOffset(56, 56, 28, 8), 10f).childAlignment = TextAnchor.UpperCenter;

            m_Title = CreateText("Title", body, TitleSize, FontStyle.Bold, k_AccentColor, TextAnchor.MiddleCenter);
            m_Building = CreateText("Building", body, BuildingSize, FontStyle.Bold, k_TextColor, TextAnchor.MiddleCenter);
            m_Reason = CreateText("Reason", body, BodySize, FontStyle.Normal, k_ReasonColor, TextAnchor.MiddleCenter);
            Spacer(body, 12f);

            // Before / after: the seats, the only thing an upgrade changes in the prototype.
            var seats = CreateRect("Seats", body);
            var seatRow = seats.gameObject.AddComponent<HorizontalLayoutGroup>();
            seatRow.spacing = 14f;
            seatRow.childAlignment = TextAnchor.MiddleCenter;
            seatRow.childControlWidth = seatRow.childControlHeight = true;
            seatRow.childForceExpandWidth = seatRow.childForceExpandHeight = false;
            var maxSeats = WaveConfig.Current.Buildings.CapacityByLevel[WaveConfig.Current.Buildings.CapacityByLevel.Count - 1];
            for (var i = 0; i < maxSeats; i++)
            {
                var seat = CreateRect("Seat " + (i + 1), seats);
                var element = seat.gameObject.AddComponent<LayoutElement>();
                element.minWidth = element.preferredWidth = SeatSize;
                element.minHeight = element.preferredHeight = SeatSize;
                var image = seat.gameObject.AddComponent<Image>();
                image.raycastTarget = false;
                m_Seats.Add(image);
            }
            m_SeatsText = CreateText("Seats Text", body, SeatsSize, FontStyle.Bold, k_TextColor, TextAnchor.MiddleCenter);
            m_Level = CreateText("Level", body, BodySize - 4, FontStyle.Normal, k_MutedColor, TextAnchor.MiddleCenter);
            m_Hint = CreateText("Hint", body, BodySize - 4, FontStyle.Italic, k_MutedColor, TextAnchor.MiddleCenter);
            Spacer(body, 12f);
            m_Cost = CreateText("Cost", body, BodySize + 4, FontStyle.Bold, k_TextColor, TextAnchor.MiddleCenter);
            m_ResortValue = CreateText("Resort Value", body, BodySize, FontStyle.Bold, k_ResortValueColor, TextAnchor.MiddleCenter);
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

            // Same order as S9: Later on the left, the main CTA wider on the right.
            var later = CreateButton("Later", buttons, k_LaterColor, OnLater, out m_LaterText);
            later.GetComponent<LayoutElement>().flexibleWidth = 1f;
            m_UpgradeButton = CreateButton("Upgrade Now", buttons, k_DisabledColor, OnUpgradeNow, out m_UpgradeText);
            m_UpgradeImage = m_UpgradeButton.GetComponent<Image>();
            m_UpgradeButton.GetComponent<LayoutElement>().flexibleWidth = 1.6f;
            m_UpgradeText.color = k_PanelColor;

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
            element.preferredWidth = 1f;

            label = CreateText("Label", rect, ButtonSize, FontStyle.Bold, k_TextColor, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);
            return button;
        }

        static VerticalLayoutGroup Vertical(GameObject target, RectOffset padding, float spacing)
        {
            var layout = target.AddComponent<VerticalLayoutGroup>();
            layout.padding = padding;
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return layout;
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
