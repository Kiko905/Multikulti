using System;
using System.Collections.Generic;
using System.Globalization;
using Multikulti.Buildings;
using Multikulti.Economy;
using Multikulti.Island;
using Multikulti.Waves;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UI;

namespace Multikulti.UI
{
    /// <summary>
    /// Home screen overlay (SCRUM-21, UI handoff S1). The player always sees:
    /// - top bar: Resort Value at the top left, currency balances (Coins, Energy) at the top right;
    /// - resort status under the Resort Value (Ready for Guests, Wave in progress, …);
    /// - a name marker above every building and a "+" marker above every construction site of the IslandMap.
    ///
    /// The currency row is a horizontal layout that grows to the left, so Gems, Materials and Reputation (MVP)
    /// are one more AddBalance each, without a new layout. The top bar stays within 140 reference pixels from
    /// the top, the space the Wave Needs Panel keeps free (WaveNeedsPanel.topOffset); the status pill is on the left,
    /// away from that panel.
    ///
    /// Graybox like the Wave Needs Panel: own uGUI canvas, built-in font, texts from the Localization table UI.
    /// Nothing on it takes taps (no raycaster), so taps still reach the buildings (WorkerTapInput).
    /// </summary>
    public sealed class HomeHud : MonoBehaviour
    {
        const string TableName = "UI";
        const string ResortValueKey = "home.resort_value";
        const string CoinsKey = "home.coins";
        const string EnergyKey = "home.energy";

        static readonly Color k_PillColor = new Color(0f, 0f, 0f, 0.6f);
        static readonly Color k_LabelColor = new Color(1f, 1f, 1f, 0.75f);
        static readonly Color k_ValueColor = Color.white;
        static readonly Color k_SiteColor = new Color(1f, 1f, 1f, 0.85f);
        static readonly Color k_SiteTextColor = new Color(0.25f, 0.18f, 0.1f);

        [SerializeField] WaveLoop waveLoop;
        [SerializeField] IslandMap map;
        [SerializeField] ResortBalances balances;
        [SerializeField, Min(8)] int fontSize = 30;
        [SerializeField, Min(0f), Tooltip("Distance of the top bar from the edges of the safe area.")]
        float margin = 32f;
        [SerializeField, Min(24f)] float barHeight = 72f;

        sealed class BalanceView
        {
            public Text Value;
            public Func<ResortBalances, int> Read;
        }

        sealed class Marker
        {
            public RectTransform Rect;
            public Text Text;
            public Need Need;
            public bool IsSite;
            public Building Building;
            public Vector3 Point;
        }

        readonly Dictionary<string, string> m_Strings = new Dictionary<string, string>();
        readonly List<(LocalizedString text, LocalizedString.ChangeHandler handler)> m_Localized =
            new List<(LocalizedString, LocalizedString.ChangeHandler)>();
        readonly List<(Text text, string key)> m_Labels = new List<(Text, string)>();
        readonly List<BalanceView> m_Balances = new List<BalanceView>();
        readonly List<Marker> m_Markers = new List<Marker>();

        Font m_Font;
        Canvas m_Canvas;
        RectTransform m_SafeArea;
        RectTransform m_MarkerLayer;
        RectTransform m_CurrencyRow;
        Image m_StatusImage;
        Text m_StatusText;
        ResortStatus? m_AppliedStatus;
        bool m_TextsDirty = true;
        bool m_BalancesDirty = true;
        Rect m_AppliedSafeArea;
        Vector2Int m_AppliedScreenSize;

        void Awake()
        {
            if (waveLoop == null)
                waveLoop = FindAnyObjectByType<WaveLoop>();
            if (map == null)
                map = FindAnyObjectByType<IslandMap>();
            if (balances == null)
                balances = FindAnyObjectByType<ResortBalances>();

            m_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildCanvas();

            foreach (ResortStatus status in Enum.GetValues(typeof(ResortStatus)))
                Localize(status.LabelKey());
            foreach (Need need in Enum.GetValues(typeof(Need)))
                Localize(BuildingKey(need));
        }

        void OnEnable()
        {
            if (balances != null)
                balances.Changed += OnBalancesChanged;
            if (map != null)
                map.Refreshed += RebuildMarkers;
        }

        void OnDisable()
        {
            if (balances != null)
                balances.Changed -= OnBalancesChanged;
            if (map != null)
                map.Refreshed -= RebuildMarkers;
        }

        void Start()
        {
            if (waveLoop == null)
            {
                Debug.LogError("Home HUD: no WaveLoop in the scene.", this);
                enabled = false;
                return;
            }
            if (balances == null)
                Debug.LogWarning("Home HUD: no ResortBalances in the scene, the balances show 0.", this);
        }

        void OnDestroy()
        {
            foreach (var (text, handler) in m_Localized)
                text.StringChanged -= handler;
        }

        void OnBalancesChanged() => m_BalancesDirty = true;

        void LateUpdate()
        {
            ApplySafeArea();

            var status = ResortStatusRules.For(waveLoop.State, waveLoop.Machine.HasNextWave);
            if (status != m_AppliedStatus)
            {
                m_AppliedStatus = status;
                m_StatusImage.color = status.BackgroundColor();
                m_TextsDirty = true;
            }

            if (m_TextsDirty)
            {
                m_TextsDirty = false;
                ApplyTexts();
            }

            if (m_BalancesDirty)
            {
                m_BalancesDirty = false;
                foreach (var view in m_Balances)
                    view.Value.text = Format(balances != null ? view.Read(balances) : 0);
            }

            PlaceMarkers();
        }

        static string Format(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

        static string BuildingKey(Need need) => need switch
        {
            Need.Rooms => "building.rooms",
            Need.Drinks => "building.drinks",
            Need.Food => "building.food",
            Need.Relax => "building.relax",
            Need.OceanView => "building.ocean_view",
            _ => "building." + need.ToString().ToLowerInvariant()
        };

        string Localized(string key) => m_Strings.TryGetValue(key, out var value) ? value : string.Empty;

        void Localize(string key)
        {
            if (m_Strings.ContainsKey(key)) return;
            m_Strings[key] = string.Empty;
            var text = new LocalizedString(TableName, key);
            LocalizedString.ChangeHandler handler = value =>
            {
                m_Strings[key] = value;
                m_TextsDirty = true;
            };
            m_Localized.Add((text, handler));
            text.StringChanged += handler;
        }

        void ApplyTexts()
        {
            foreach (var (text, key) in m_Labels)
                text.text = Localized(key);
            if (m_AppliedStatus.HasValue)
                m_StatusText.text = Localized(m_AppliedStatus.Value.LabelKey());
            foreach (var marker in m_Markers)
            {
                var name = Localized(BuildingKey(marker.Need));
                marker.Text.text = marker.IsSite ? "+ " + name : name;
            }
        }

        // Keeps the top bar out of the notch and the rounded corners in landscape.
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

        void RebuildMarkers()
        {
            foreach (var marker in m_Markers)
                Destroy(marker.Rect.gameObject);
            m_Markers.Clear();
            if (map == null) return;

            foreach (var building in map.Buildings)
                m_Markers.Add(CreateMarker(building.Need, false, building, Vector3.zero));
            foreach (var site in map.VisibleSites)
                m_Markers.Add(CreateMarker(site.Need, true, null, site.MarkerPoint));
            m_TextsDirty = true;
        }

        void PlaceMarkers()
        {
            var camera = Camera.main;
            if (camera == null) return;
            var uiCamera = m_Canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : m_Canvas.worldCamera;

            foreach (var marker in m_Markers)
            {
                if (!marker.IsSite && marker.Building == null)
                {
                    marker.Rect.gameObject.SetActive(false);
                    continue;
                }
                var point = marker.IsSite ? marker.Point : IslandMap.MarkerPoint(marker.Building);
                var screen = camera.WorldToScreenPoint(point);
                var local = Vector2.zero;
                var visible = screen.z > 0f &&
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(m_MarkerLayer, screen, uiCamera, out local);
                if (marker.Rect.gameObject.activeSelf != visible)
                    marker.Rect.gameObject.SetActive(visible);
                if (visible)
                    marker.Rect.anchoredPosition = local;
            }
        }

        void BuildCanvas()
        {
            var canvasObject = new GameObject("Home HUD Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);
            m_Canvas = canvasObject.GetComponent<Canvas>();
            m_Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Below the Wave Needs Panel and Start Wave (100): they cover the markers, not the other way round.
            m_Canvas.sortingOrder = 90;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            // Markers first, so the top bar draws over them.
            m_MarkerLayer = CreateRect("Markers", canvasObject.transform);
            Stretch(m_MarkerLayer);

            m_SafeArea = CreateRect("Safe Area", canvasObject.transform);
            Stretch(m_SafeArea);

            var resortValue = CreatePill("Resort Value", m_SafeArea, k_PillColor, barHeight);
            Pin(resortValue, new Vector2(0f, 1f), new Vector2(margin, -margin));
            AddLabel(resortValue, ResortValueKey);
            m_Balances.Add(new BalanceView { Value = AddValue(resortValue), Read = b => b.ResortValue });

            var status = CreatePill("Resort Status", m_SafeArea, ResortStatus.ReadyForGuests.BackgroundColor(), barHeight * 0.75f);
            Pin(status, new Vector2(0f, 1f), new Vector2(margin, -margin - barHeight - 12f));
            m_StatusImage = status.GetComponent<Image>();
            m_StatusText = CreateText("Label", status, Mathf.RoundToInt(fontSize * 0.85f), FontStyle.Bold, k_ValueColor);

            m_CurrencyRow = CreateRect("Currencies", m_SafeArea);
            Pin(m_CurrencyRow, new Vector2(1f, 1f), new Vector2(-margin, -margin));
            m_CurrencyRow.sizeDelta = new Vector2(0f, barHeight);
            var row = m_CurrencyRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 16f;
            row.childAlignment = TextAnchor.UpperRight;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = true;
            m_CurrencyRow.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Prototype currencies (build document §2). MVP adds Gems, Materials and Reputation here.
            AddBalance(CoinsKey, b => b.Coins);
            AddBalance(EnergyKey, b => b.Energy);
        }

        void AddBalance(string key, Func<ResortBalances, int> read)
        {
            var pill = CreatePill(key, m_CurrencyRow, k_PillColor, barHeight, fitWidth: false);
            AddLabel(pill, key);
            m_Balances.Add(new BalanceView { Value = AddValue(pill), Read = read });
        }

        void AddLabel(RectTransform pill, string key)
        {
            Localize(key);
            var label = CreateText("Label", pill, Mathf.RoundToInt(fontSize * 0.8f), FontStyle.Normal, k_LabelColor);
            m_Labels.Add((label, key));
        }

        Text AddValue(RectTransform pill) => CreateText("Value", pill, fontSize, FontStyle.Bold, k_ValueColor);

        Marker CreateMarker(Need need, bool isSite, Building building, Vector3 point)
        {
            var name = isSite ? $"Site Marker ({need})" : $"Marker ({building.name})";
            var pill = CreatePill(name, m_MarkerLayer, isSite ? k_SiteColor : k_PillColor, fontSize * 1.4f, padding: 12);
            pill.anchorMin = new Vector2(0.5f, 0.5f);
            pill.anchorMax = new Vector2(0.5f, 0.5f);
            pill.pivot = new Vector2(0.5f, 0f);
            var text = CreateText("Label", pill, Mathf.RoundToInt(fontSize * 0.75f), FontStyle.Bold,
                isSite ? k_SiteTextColor : k_ValueColor);
            return new Marker { Rect = pill, Text = text, Need = need, IsSite = isSite, Building = building, Point = point };
        }

        RectTransform CreatePill(string name, Transform parent, Color color, float height, bool fitWidth = true, int padding = 20)
        {
            var rect = CreateRect(name, parent);
            rect.sizeDelta = new Vector2(0f, height);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;

            var layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(padding, padding, 4, 4);
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            if (fitWidth)
                rect.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            else
                rect.gameObject.AddComponent<LayoutElement>().minHeight = height;
            return rect;
        }

        Text CreateText(string name, Transform parent, int size, FontStyle style, Color color)
        {
            var text = CreateRect(name, parent).gameObject.AddComponent<Text>();
            text.font = m_Font;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        static void Pin(RectTransform rect, Vector2 corner, Vector2 position)
        {
            rect.anchorMin = corner;
            rect.anchorMax = corner;
            rect.pivot = corner;
            rect.anchoredPosition = position;
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
