using System;
using System.Collections.Generic;
using System.Globalization;
using Multikulti.Buildings;
using Multikulti.Config;
using Multikulti.Economy;
using Multikulti.Waves;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Localization;
using UnityEngine.UI;

namespace Multikulti.UI
{
    /// <summary>
    /// S4 Wave Complete (SCRUM-46, UI handoff §A "S4 — Wave Complete / Result", US9, US10): the moment the wave closes.
    ///
    /// - Opens when the wave enters WaveComplete and shows the result of WaveLoop.Hodnotenie: the rating (stars, soft
    ///   tone), Guests served, Wave Satisfaction (the number is shown here for the first time), the rewards (Coins,
    ///   Energy), the Resort Value gain and the breakdown What went well / Needs improvement.
    /// - CTA Claim Rewards only calls WaveLoop.Machine.Claim() (WaveComplete → Upgrade); WaveLoop itself credits the
    ///   Coins and Energy in ZobratOdmenu exactly once (SCRUM-45), so the screen never credits anything.
    /// - Needs improvement is the bridge to the upgrade (AC6): needs that were in BOTTLENECK come first, the longest
    ///   one on top, so it matches the upgrade S5 recommends (SCRUM-48). LongestBottleneck gives that need.
    /// - Never a punishing tone: the worst result is 1★ with non-zero Coins, the subtitle is "you can do better",
    ///   and What went well always has at least the served guests.
    /// - Template for S8 VIP Complete as well (UI handoff §F.3): the content is a list of tiles and two lists,
    ///   the accent colour and the title belong to the result, so a VIP variant only changes the style.
    ///
    /// Graybox: the screen builds its own uGUI canvas and installs itself at runtime on the WaveLoop, so the scene
    /// does not change. The dimmed background catches every tap while the screen is open.
    /// </summary>
    public sealed class WaveCompleteScreen : MonoBehaviour
    {
        const string TableName = "UI";
        const string TitleKey = "wave_complete.title";
        const string ToneLowKey = "wave_complete.tone.low";
        const string ToneMidKey = "wave_complete.tone.mid";
        const string ToneHighKey = "wave_complete.tone.high";
        const string TonePerfectKey = "wave_complete.tone.perfect";
        const string GuestsKey = "wave_complete.guests_served";
        const string SatisfactionKey = "wave_complete.satisfaction";
        const string RewardsKey = "wave_complete.rewards";
        const string WentWellKey = "wave_complete.went_well";
        const string NeedsImprovementKey = "wave_complete.needs_improvement";
        const string AllServedKey = "wave_complete.well.all_served";
        const string ServedQuicklyKey = "wave_complete.well.quick";
        const string QueueGrewKey = "wave_complete.improve.queue";
        const string WaitedLongKey = "wave_complete.improve.slow";
        const string NothingToImproveKey = "wave_complete.improve.none";
        const string ClaimKey = "wave_complete.claim";
        const string CoinsKey = "home.coins";
        const string EnergyKey = "home.energy";
        const string ResortValueKey = "home.resort_value";

        const int TitleSize = 56;
        const int ToneSize = 30;
        const int TileLabelSize = 24;
        const int TileValueSize = 48;
        const int HeaderSize = 28;
        const int LineSize = 26;
        const int ButtonSize = 36;
        const float PanelWidth = 1240f;
        const float StarSize = 76f;
        const int MaxLines = 5;

        /// <summary>A need served at least as well as the 4★ threshold counts as "went well".</summary>
        const int WentWellThresholdIndex = 3;

        static readonly Color k_DimColor = new Color(0f, 0f, 0f, 0.6f);
        // Fully opaque: the project renders in linear colour space, where the bright island would show through.
        static readonly Color k_PanelColor = new Color(0.07f, 0.09f, 0.12f, 1f);
        static readonly Color k_TileColor = new Color(0.12f, 0.15f, 0.19f, 1f);
        static readonly Color k_TextColor = Color.white;
        static readonly Color k_MutedColor = new Color(1f, 1f, 1f, 0.7f);
        static readonly Color k_StarOnColor = new Color(1f, 0.8f, 0.25f);
        static readonly Color k_StarOffColor = new Color(1f, 1f, 1f, 0.16f);
        static readonly Color k_PerfectColor = new Color(0.45f, 0.9f, 1f);
        static readonly Color k_WentWellColor = new Color(0.45f, 0.85f, 0.5f);
        static readonly Color k_ImproveColor = new Color(1f, 0.72f, 0.3f);
        static readonly Color k_CoinsColor = new Color(1f, 0.82f, 0.3f);
        static readonly Color k_EnergyColor = new Color(0.45f, 0.85f, 1f);
        static readonly Color k_ResortValueColor = new Color(0.75f, 0.6f, 1f);
        static readonly Color k_ClaimColor = new Color(1f, 0.8f, 0.25f);

        static Sprite s_Star;

        [SerializeField] WaveLoop waveLoop;
        [SerializeField] ResortBalances balances;
        [SerializeField] NeedStateBoard needStates;

        readonly Dictionary<string, string> m_Texts = new Dictionary<string, string>();
        readonly List<(LocalizedString Text, LocalizedString.ChangeHandler Handler)> m_Labels =
            new List<(LocalizedString, LocalizedString.ChangeHandler)>();
        readonly Dictionary<Need, float> m_BottleneckSince = new Dictionary<Need, float>();
        readonly Dictionary<Need, float> m_BottleneckSeconds = new Dictionary<Need, float>();
        readonly List<string> m_WentWell = new List<string>();
        readonly List<string> m_NeedsImprovement = new List<string>();

        GameObject m_Root;
        RectTransform m_SafeArea;
        Rect m_AppliedSafeArea;
        Vector2Int m_AppliedScreenSize;
        Font m_Font;

        Image m_Accent;
        Text m_Title;
        Text m_Tone;
        readonly Image[] m_Stars = new Image[5];
        Text m_GuestsLabel;
        Text m_GuestsValue;
        Text m_SatisfactionLabel;
        Text m_SatisfactionValue;
        Text m_RewardsHeader;
        Text m_CoinsLabel;
        Text m_CoinsValue;
        Text m_EnergyLabel;
        Text m_EnergyValue;
        GameObject m_EnergyTile;
        GameObject m_ResortValueTile;
        Text m_ResortValueLabel;
        Text m_ResortValueValue;
        Text m_WentWellHeader;
        Text m_NeedsImprovementHeader;
        readonly Text[] m_WentWellLines = new Text[MaxLines];
        readonly Text[] m_ImproveLines = new Text[MaxLines];
        Button m_ClaimButton;
        Text m_ClaimText;

        HodnotenieVlny m_Shown;
        int m_ResortValueAtStart;
        bool m_Dirty;

        /// <summary>True while the screen is on (for tests).</summary>
        public bool IsVisible => m_Root != null && m_Root.activeSelf;

        /// <summary>Result on the screen (for tests).</summary>
        public HodnotenieVlny Shown => IsVisible ? m_Shown : null;

        /// <summary>Stars lit on the screen, 1–5 (for tests).</summary>
        public int LitStars
        {
            get
            {
                var lit = 0;
                foreach (var star in m_Stars)
                    if (star != null && star.color != k_StarOffColor)
                        lit++;
                return lit;
            }
        }

        /// <summary>True if Claim Rewards can be pressed (for tests).</summary>
        public bool ClaimInteractable => m_ClaimButton != null && m_ClaimButton.interactable && IsVisible;

        /// <summary>Lines of What went well (for tests).</summary>
        public IReadOnlyList<string> WentWell => m_WentWell;

        /// <summary>Lines of Needs improvement (for tests).</summary>
        public IReadOnlyList<string> NeedsImprovement => m_NeedsImprovement;

        /// <summary>Texts of the header and the tiles from top to bottom (for tests).</summary>
        public string[] VisibleTexts => new[]
        {
            m_Title.text, m_Tone.text, m_GuestsLabel.text, m_GuestsValue.text, m_SatisfactionLabel.text,
            m_SatisfactionValue.text, m_RewardsHeader.text, m_CoinsValue.text, m_CoinsLabel.text,
            m_EnergyTile.activeSelf ? m_EnergyValue.text : string.Empty, m_EnergyLabel.text,
            m_ResortValueTile.activeSelf ? m_ResortValueValue.text : string.Empty,
            m_WentWellHeader.text, m_NeedsImprovementHeader.text, m_ClaimText.text
        };

        /// <summary>Resort Value gained since Start Wave; the tile is shown only when it is above 0.</summary>
        public int ResortValueGain => balances != null ? Mathf.Max(0, balances.ResortValue - m_ResortValueAtStart) : 0;

        /// <summary>Seconds the need was in BOTTLENECK in the current or last wave.</summary>
        public float BottleneckSeconds(Need need) =>
            m_BottleneckSeconds.TryGetValue(need, out var seconds) ? seconds : 0f;

        /// <summary>
        /// AC6: the need that was in BOTTLENECK the longest in the last wave, the top line of Needs improvement
        /// and the upgrade S5 should recommend (SCRUM-48); null after a wave without a bottleneck.
        /// </summary>
        public Need? LongestBottleneck
        {
            get
            {
                Need? longest = null;
                var seconds = 0f;
                foreach (var pair in m_BottleneckSeconds)
                {
                    if (pair.Value <= seconds) continue;
                    seconds = pair.Value;
                    longest = pair.Key;
                }
                return longest;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var loop = FindAnyObjectByType<WaveLoop>();
            if (loop != null && loop.GetComponent<WaveCompleteScreen>() == null)
                loop.gameObject.AddComponent<WaveCompleteScreen>();
        }

        void Awake()
        {
            if (waveLoop == null)
                waveLoop = GetComponent<WaveLoop>();
            if (waveLoop == null)
                waveLoop = FindAnyObjectByType<WaveLoop>();
            if (balances == null)
                balances = FindAnyObjectByType<ResortBalances>();
            if (needStates == null)
                needStates = FindAnyObjectByType<NeedStateBoard>();

            m_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            EnsureEventSystem();
            BuildCanvas();

            foreach (var key in new[]
                     {
                         TitleKey, ToneLowKey, ToneMidKey, ToneHighKey, TonePerfectKey, GuestsKey, SatisfactionKey,
                         RewardsKey, WentWellKey, NeedsImprovementKey, AllServedKey, ServedQuicklyKey, QueueGrewKey,
                         WaitedLongKey, NothingToImproveKey, ClaimKey, CoinsKey, EnergyKey, ResortValueKey
                     })
                Listen(key);
            foreach (Need need in Enum.GetValues(typeof(Need)))
                Listen(HomeHud.BuildingKey(need));
        }

        // Start: WaveLoop.Machine is created in its Awake.
        void Start()
        {
            if (waveLoop == null || waveLoop.Machine == null)
            {
                Debug.LogWarning("Wave Complete screen: no WaveLoop in the scene.", this);
                enabled = false;
                return;
            }

            waveLoop.Machine.StateChanged += OnWaveStateChanged;
            if (needStates != null)
                needStates.StateChanged += OnNeedStateChanged;
            if (balances != null)
            {
                balances.Changed += OnBalancesChanged;
                m_ResortValueAtStart = balances.ResortValue;
            }

            // A restored game may already wait on the result.
            if (waveLoop.State == WaveState.WaveComplete)
                Show();
        }

        void OnDestroy()
        {
            if (waveLoop != null && waveLoop.Machine != null)
                waveLoop.Machine.StateChanged -= OnWaveStateChanged;
            if (needStates != null)
                needStates.StateChanged -= OnNeedStateChanged;
            if (balances != null)
                balances.Changed -= OnBalancesChanged;
            foreach (var (text, handler) in m_Labels)
                text.StringChanged -= handler;
        }

        void LateUpdate()
        {
            ApplySafeArea();
            if (IsVisible && m_Dirty && m_Shown != null)
                Fill(m_Shown);
        }

        void OnWaveStateChanged(WaveState previous, WaveState next)
        {
            switch (next)
            {
                case WaveState.WaveIncoming:
                    m_BottleneckSince.Clear();
                    m_BottleneckSeconds.Clear();
                    m_ResortValueAtStart = balances != null ? balances.ResortValue : 0;
                    break;
                case WaveState.WaveComplete:
                    CloseBottlenecks();
                    Show();
                    break;
            }

            if (next != WaveState.WaveComplete && next != WaveState.WavePaused)
                Hide();
        }

        void OnNeedStateChanged(Need need, NeedState previous, NeedState next)
        {
            var now = waveLoop.WaveTime;
            if (next == NeedState.Bottleneck)
            {
                m_BottleneckSince[need] = now;
            }
            else if (previous == NeedState.Bottleneck && m_BottleneckSince.TryGetValue(need, out var since))
            {
                AddBottleneckTime(need, now - since);
                m_BottleneckSince.Remove(need);
            }
        }

        void OnBalancesChanged() => m_Dirty = true;

        /// <summary>A need still in BOTTLENECK when the wave ends counts until the end of the wave.</summary>
        void CloseBottlenecks()
        {
            var now = waveLoop.WaveTime;
            foreach (var pair in m_BottleneckSince)
                AddBottleneckTime(pair.Key, now - pair.Value);
            m_BottleneckSince.Clear();
        }

        void AddBottleneckTime(Need need, float seconds)
        {
            m_BottleneckSeconds.TryGetValue(need, out var total);
            m_BottleneckSeconds[need] = total + Mathf.Max(0f, seconds);
        }

        void Show()
        {
            var result = waveLoop.Hodnotenie;
            if (result == null)
            {
                Debug.LogWarning($"Wave {waveLoop.Wave}: WaveComplete without a rating, nothing to show.", this);
                return;
            }

            m_Shown = result;
            Fill(result);
            m_Root.SetActive(true);
        }

        void Hide()
        {
            m_Shown = null;
            if (m_Root != null)
                m_Root.SetActive(false);
        }

        /// <summary>
        /// CTA Claim Rewards: WaveComplete → Upgrade. WaveLoop credits the reward itself (ZobratOdmenu, SCRUM-45);
        /// the screen closes on the state change.
        /// </summary>
        public void OnClaim()
        {
            if (waveLoop == null || waveLoop.Machine == null || waveLoop.State != WaveState.WaveComplete) return;
            waveLoop.Machine.Claim();
        }

        void Fill(HodnotenieVlny result)
        {
            m_Dirty = false;

            var accent = result.IsPerfect ? k_PerfectColor : k_StarOnColor;
            m_Accent.color = accent;
            m_Title.text = Format(Text(TitleKey), result.Wave.ToString(CultureInfo.InvariantCulture));
            m_Tone.text = Text(ToneKey(result.Rating));
            for (var i = 0; i < m_Stars.Length; i++)
                m_Stars[i].color = i < result.Stars ? accent : k_StarOffColor;

            var served = result.Satisfaction.Units;
            var total = Mathf.Max(result.Units, served);
            m_GuestsLabel.text = Text(GuestsKey);
            m_GuestsValue.text = $"{Amount(served)}/{Amount(total)}";
            m_SatisfactionLabel.text = Text(SatisfactionKey);
            m_SatisfactionValue.text = Percent(result.Satisfaction.Value);

            m_RewardsHeader.text = Text(RewardsKey);
            m_CoinsValue.text = "+" + Amount(result.Coins);
            m_CoinsLabel.text = Text(CoinsKey);
            m_EnergyValue.text = "+" + Amount(result.Energy);
            m_EnergyLabel.text = Text(EnergyKey);
            // energy_per_rating can be 0 for a rating; a "+0" tile would look like a missing reward.
            m_EnergyTile.SetActive(result.Energy > 0);
            var gain = ResortValueGain;
            m_ResortValueTile.SetActive(gain > 0);
            m_ResortValueValue.text = "+" + Amount(gain);
            m_ResortValueLabel.text = Text(ResortValueKey);

            m_WentWellHeader.text = Text(WentWellKey);
            m_NeedsImprovementHeader.text = Text(NeedsImprovementKey);
            BuildBreakdown(result);
            ShowLines(m_WentWellLines, m_WentWell);
            ShowLines(m_ImproveLines, m_NeedsImprovement);

            m_ClaimText.text = Text(ClaimKey);
        }

        /// <summary>
        /// What went well / Needs improvement from the needs of the wave: a need that was in BOTTLENECK or whose
        /// guests waited below the 4★ threshold needs improvement (BOTTLENECK first, the longest on top, AC6);
        /// the others went well. The served guests always went well.
        /// </summary>
        void BuildBreakdown(HodnotenieVlny result)
        {
            m_WentWell.Clear();
            m_NeedsImprovement.Clear();
            var satisfaction = result.Satisfaction;
            m_WentWell.Add(Format(Text(AllServedKey), Amount(satisfaction.Units)));

            var thresholds = WaveConfig.Current.Satisfaction.RatingThresholds;
            var wellFrom = thresholds.Count > WentWellThresholdIndex ? thresholds[WentWellThresholdIndex] : 0.8f;

            var improve = new List<(Need Need, float Bottleneck, float Satisfaction)>();
            foreach (Need need in Enum.GetValues(typeof(Need)))
            {
                if (satisfaction.UnitsOf(need) == 0) continue;
                var bottleneck = BottleneckSeconds(need);
                var value = satisfaction.Of(need);
                if (bottleneck > 0f || value + 0.0001f < wellFrom)
                    improve.Add((need, bottleneck, value));
                else
                    m_WentWell.Add(Format(Text(ServedQuicklyKey), BuildingName(need)));
            }

            improve.Sort((a, b) => a.Bottleneck != b.Bottleneck
                ? b.Bottleneck.CompareTo(a.Bottleneck)
                : a.Satisfaction.CompareTo(b.Satisfaction));
            foreach (var item in improve)
            {
                m_NeedsImprovement.Add(item.Bottleneck > 0f
                    ? Format(Text(QueueGrewKey), BuildingName(item.Need),
                        Mathf.Max(1, Mathf.RoundToInt(item.Bottleneck)).ToString(CultureInfo.InvariantCulture))
                    : Format(Text(WaitedLongKey), BuildingName(item.Need)));
            }

            if (m_NeedsImprovement.Count == 0)
                m_NeedsImprovement.Add(Text(NothingToImproveKey));
        }

        static void ShowLines(Text[] lines, List<string> values)
        {
            for (var i = 0; i < lines.Length; i++)
            {
                var on = i < values.Count;
                lines[i].gameObject.SetActive(on);
                lines[i].text = on ? "• " + values[i] : string.Empty;
            }
        }

        string BuildingName(Need need) => Fallback(Text(HomeHud.BuildingKey(need)), need.ToString());

        // Soft tone: even 1★ is "a good start", never "you lost" (UI handoff S4 "Nikdy").
        static string ToneKey(WaveRating rating) => rating switch
        {
            WaveRating.Perfect => TonePerfectKey,
            WaveRating.FiveStars or WaveRating.FourStars => ToneHighKey,
            WaveRating.ThreeStars => ToneMidKey,
            _ => ToneLowKey
        };

        static string Amount(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

        static string Percent(float value) =>
            Mathf.RoundToInt(Mathf.Clamp01(value) * 100f).ToString(CultureInfo.InvariantCulture) + "%";

        static string Fallback(string text, string fallback) => string.IsNullOrEmpty(text) ? fallback : text;

        static string Format(string format, params object[] values)
        {
            if (string.IsNullOrEmpty(format)) return values.Length > 0 ? values[0]?.ToString() : string.Empty;
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
                m_Dirty = true;
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
            var canvasObject = new GameObject("Wave Complete Canvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above the HUD (90), the wave panels (100) and the toasts (110); below S9 Area Unlock (200).
            canvas.sortingOrder = 150;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            // The dim covers the whole screen (also under the notch) and catches every tap under the screen.
            var dim = CreateRect("Dim", canvasObject.transform);
            Stretch(dim);
            dim.gameObject.AddComponent<Image>().color = k_DimColor;
            m_Root = dim.gameObject;

            m_SafeArea = CreateRect("Safe Area", dim);
            Stretch(m_SafeArea);

            var panel = CreateRect("S4 Wave Complete", m_SafeArea);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.gameObject.AddComponent<Image>().color = k_PanelColor;
            var layout = Vertical(panel.gameObject, new RectOffset(0, 0, 0, 36), 0f);
            layout.childAlignment = TextAnchor.UpperCenter;
            var fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            panel.sizeDelta = new Vector2(PanelWidth, 0f);

            // Accent band on top: gold for a normal wave, light blue for Perfect (and a VIP style later).
            var band = CreateRect("Accent", panel);
            m_Accent = band.gameObject.AddComponent<Image>();
            Fixed(band, 12f);

            var header = CreateRect("Header", panel);
            Vertical(header.gameObject, new RectOffset(48, 48, 28, 8), 6f).childAlignment = TextAnchor.UpperCenter;
            m_Title = CreateText("Title", header, TitleSize, FontStyle.Bold, k_TextColor, TextAnchor.MiddleCenter);

            var stars = CreateRect("Stars", header);
            var starRow = stars.gameObject.AddComponent<HorizontalLayoutGroup>();
            starRow.spacing = 12f;
            starRow.childAlignment = TextAnchor.MiddleCenter;
            starRow.childControlWidth = starRow.childControlHeight = true;
            starRow.childForceExpandWidth = starRow.childForceExpandHeight = false;
            for (var i = 0; i < m_Stars.Length; i++)
            {
                var star = CreateRect("Star " + (i + 1), stars);
                var element = star.gameObject.AddComponent<LayoutElement>();
                element.minWidth = element.preferredWidth = StarSize;
                element.minHeight = element.preferredHeight = StarSize;
                m_Stars[i] = star.gameObject.AddComponent<Image>();
                m_Stars[i].sprite = Star();
                m_Stars[i].preserveAspect = true;
                m_Stars[i].raycastTarget = false;
            }
            m_Tone = CreateText("Tone", header, ToneSize, FontStyle.Italic, k_MutedColor, TextAnchor.MiddleCenter);

            // Result tiles: Guests served and Wave Satisfaction (the first time the number is shown).
            var stats = Row("Stats", panel, new RectOffset(48, 48, 18, 0));
            CreateTile(stats, "Guests Served", k_TextColor, out m_GuestsValue, out m_GuestsLabel);
            CreateTile(stats, "Wave Satisfaction", k_TextColor, out m_SatisfactionValue, out m_SatisfactionLabel);

            // Rewards: Coins, Energy and the Resort Value gain (shown only when there is one).
            var rewardsHeader = CreateRect("Rewards Header", panel);
            Vertical(rewardsHeader.gameObject, new RectOffset(48, 48, 22, 0), 0f);
            m_RewardsHeader = CreateText("Label", rewardsHeader, HeaderSize, FontStyle.Bold, k_MutedColor, TextAnchor.MiddleLeft);
            var rewards = Row("Rewards", panel, new RectOffset(48, 48, 8, 0));
            CreateTile(rewards, "Coins", k_CoinsColor, out m_CoinsValue, out m_CoinsLabel);
            m_EnergyTile = CreateTile(rewards, "Energy", k_EnergyColor, out m_EnergyValue, out m_EnergyLabel);
            m_ResortValueTile = CreateTile(rewards, "Resort Value", k_ResortValueColor,
                out m_ResortValueValue, out m_ResortValueLabel);

            // Breakdown: why the rating is what it is; Needs improvement leads to the upgrade (S5).
            var breakdown = Row("Breakdown", panel, new RectOffset(48, 48, 22, 0));
            breakdown.childAlignment = TextAnchor.UpperCenter;
            m_WentWellHeader = CreateList(breakdown, "What Went Well", k_WentWellColor, m_WentWellLines);
            m_NeedsImprovementHeader = CreateList(breakdown, "Needs Improvement", k_ImproveColor, m_ImproveLines);

            var buttons = Row("Buttons", panel, new RectOffset(48, 48, 28, 0));
            m_ClaimButton = CreateButton("Claim Rewards", buttons.transform, k_ClaimColor, OnClaim, out m_ClaimText);
            m_ClaimText.color = k_PanelColor;

            m_Root.SetActive(false);
        }

        GameObject CreateTile(HorizontalLayoutGroup row, string name, Color valueColor, out Text value, out Text label)
        {
            var tile = CreateRect(name, row.transform);
            tile.gameObject.AddComponent<Image>().color = k_TileColor;
            var layout = Vertical(tile.gameObject, new RectOffset(16, 16, 14, 14), 2f);
            layout.childAlignment = TextAnchor.MiddleCenter;
            // Same width for every tile in a row, whatever its text.
            var element = tile.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = 1f;
            element.flexibleWidth = 1f;
            value = CreateText("Value", tile, TileValueSize, FontStyle.Bold, valueColor, TextAnchor.MiddleCenter);
            label = CreateText("Label", tile, TileLabelSize, FontStyle.Normal, k_MutedColor, TextAnchor.MiddleCenter);
            return tile.gameObject;
        }

        Text CreateList(HorizontalLayoutGroup row, string name, Color color, Text[] lines)
        {
            var list = CreateRect(name, row.transform);
            list.gameObject.AddComponent<Image>().color = k_TileColor;
            Vertical(list.gameObject, new RectOffset(24, 24, 16, 18), 6f).childAlignment = TextAnchor.UpperLeft;
            var element = list.gameObject.AddComponent<LayoutElement>();
            element.flexibleWidth = 1f;
            element.preferredWidth = 1f;
            element.minHeight = 200f;
            var header = CreateText("Header", list, HeaderSize, FontStyle.Bold, color, TextAnchor.MiddleLeft);
            for (var i = 0; i < lines.Length; i++)
            {
                lines[i] = CreateText("Line " + (i + 1), list, LineSize, FontStyle.Normal, k_TextColor, TextAnchor.MiddleLeft);
                lines[i].gameObject.SetActive(false);
            }
            return header;
        }

        Button CreateButton(string name, Transform parent, Color color, UnityEngine.Events.UnityAction onClick, out Text label)
        {
            var rect = CreateRect(name + " Button", parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);
            var element = rect.gameObject.AddComponent<LayoutElement>();
            element.minHeight = element.preferredHeight = 100f;
            element.flexibleWidth = 1f;

            label = CreateText("Label", rect, ButtonSize, FontStyle.Bold, k_TextColor, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);
            return button;
        }

        HorizontalLayoutGroup Row(string name, Transform parent, RectOffset padding)
        {
            var rect = CreateRect(name, parent);
            var row = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = padding;
            row.spacing = 20f;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = true;
            row.childForceExpandHeight = true;
            return row;
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

        static void Fixed(RectTransform rect, float height)
        {
            var element = rect.gameObject.AddComponent<LayoutElement>();
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

        /// <summary>
        /// Five-pointed star drawn once at runtime: the built-in font has no reliable ★ glyph on every device,
        /// and the graybox has no sprite assets yet.
        /// </summary>
        static Sprite Star()
        {
            if (s_Star != null) return s_Star;

            const int size = 128;
            const int samples = 4;
            var points = new Vector2[10];
            for (var i = 0; i < points.Length; i++)
            {
                var angle = Mathf.PI * 0.5f + i * Mathf.PI / 5f;
                var radius = i % 2 == 0 ? 0.5f : 0.2f;
                points[i] = new Vector2(0.5f + Mathf.Cos(angle) * radius, 0.47f + Mathf.Sin(angle) * radius);
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Star", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var inside = 0;
                for (var sy = 0; sy < samples; sy++)
                for (var sx = 0; sx < samples; sx++)
                {
                    var p = new Vector2((x + (sx + 0.5f) / samples) / size, (y + (sy + 0.5f) / samples) / size);
                    if (Inside(points, p)) inside++;
                }
                var alpha = (byte)(255 * inside / (samples * samples));
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            s_Star = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            s_Star.name = "Star";
            return s_Star;
        }

        static bool Inside(Vector2[] polygon, Vector2 point)
        {
            var inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                var a = polygon[i];
                var b = polygon[j];
                if ((a.y > point.y) != (b.y > point.y) &&
                    point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
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
