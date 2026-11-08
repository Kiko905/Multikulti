using System;
using System.Collections.Generic;
using Multikulti.Buildings;
using Multikulti.Config;
using Multikulti.Waves;
using Multikulti.Workers;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UI;

namespace Multikulti.UI
{
    /// <summary>
    /// Wave Needs Panel (SCRUM-29). While a wave runs (WaveIncoming, WaveActive, WavePaused) it shows next to
    /// the island, for every active need, the served and total units of the wave (e.g. "Food 2/6") and
    /// a Worker badge on the needs whose building has a Worker.
    ///
    /// Graybox: the panel builds its own uGUI canvas with the built-in font, so the scene only needs this
    /// component. Texts come from the Localization table UI (need.*, wave_needs.worker).
    /// The panel only reads the game; none of its graphics is a raycast target, so it never blocks a tap.
    ///
    /// Layout for 3–5 needs (SCRUM-79): the panel hangs from a fixed point in the top right corner, below the
    /// space kept for the top bar (topOffset), and every row has the same fixed size. A need that joins in a later
    /// wave is added at the bottom, so the rows above it never move and the panel only grows downwards.
    /// In the editor and in development builds the panel warns once if it covers a building.
    /// </summary>
    public sealed class WaveNeedsPanel : MonoBehaviour
    {
        const string TableName = "UI";
        const string WorkerKey = "wave_needs.worker";

        static readonly Color k_TextColor = Color.white;
        static readonly Color k_NoBuildingColor = new Color(1f, 1f, 1f, 0.45f);
        static readonly Color k_WorkerColor = new Color(1f, 0.82f, 0.3f);
        static readonly Color k_BackgroundColor = new Color(0f, 0f, 0f, 0.6f);

        [SerializeField] WaveLoop waveLoop;
        [SerializeField] WorkerManager workers;
        [SerializeField, Min(8)] int fontSize = 32;
        [SerializeField, Min(0f), Tooltip("Distance from the right edge of the safe area.")]
        float margin = 32f;
        [SerializeField, Min(0f), Tooltip("Distance from the top of the safe area; keeps the top bar (Resort Value, currencies) free.")]
        float topOffset = 140f;

        readonly WaveNeedsProgress m_Progress = new WaveNeedsProgress();
        readonly List<NeedProgress> m_Active = new List<NeedProgress>();
        readonly List<Row> m_Rows = new List<Row>();
        readonly HashSet<Building> m_CoveredBuildings = new HashSet<Building>();

        LocalizedString m_WorkerLabel;
        LocalizedString.ChangeHandler m_WorkerLabelChanged;
        string m_WorkerText = string.Empty;

        GameObject m_Panel;
        RectTransform m_PanelRect;
        int m_VisibleRows;
        RectTransform m_SafeArea;
        Rect m_AppliedSafeArea;
        Vector2Int m_AppliedScreenSize;

        IReadOnlyList<Building> Buildings =>
            workers != null ? workers.Buildings : (IReadOnlyList<Building>)Array.Empty<Building>();

        void Awake()
        {
            if (waveLoop == null)
                waveLoop = FindAnyObjectByType<WaveLoop>();
            if (workers == null)
                workers = FindAnyObjectByType<WorkerManager>();

            BuildCanvas();

            m_WorkerLabel = new LocalizedString(TableName, WorkerKey);
            m_WorkerLabelChanged = value =>
            {
                m_WorkerText = value;
                foreach (var row in m_Rows)
                    row.Shown = null;
            };
            m_WorkerLabel.StringChanged += m_WorkerLabelChanged;
        }

        void Start()
        {
            if (waveLoop == null)
            {
                Debug.LogError("Wave Needs Panel: no WaveLoop in the scene.", this);
                enabled = false;
                return;
            }

            if (workers == null)
                Debug.LogWarning("Wave Needs Panel: no WorkerManager in the scene, every need shows 0 served.", this);

            waveLoop.Machine.StateChanged += OnStateChanged;
            m_Progress.StartWave(Buildings);
        }

        void OnDestroy()
        {
            if (waveLoop != null && waveLoop.Machine != null)
                waveLoop.Machine.StateChanged -= OnStateChanged;
            if (m_WorkerLabel != null)
                m_WorkerLabel.StringChanged -= m_WorkerLabelChanged;
            foreach (var row in m_Rows)
                row.Label.StringChanged -= row.LabelChanged;
        }

        // LateUpdate, so the numbers include the units the buildings served in this frame's Update.
        void LateUpdate()
        {
            ApplySafeArea();

            var visible = waveLoop.State.IsInWave();
            if (m_Panel.activeSelf != visible)
                m_Panel.SetActive(visible);
            if (!visible)
            {
                m_VisibleRows = 0;
                return;
            }

            m_Progress.Collect(CurrentWave(), Buildings, m_Active);

            // m_Rows and m_Active are both in WaveNeedsProgress.Needs order.
            var next = 0;
            var visibleRows = 0;
            foreach (var row in m_Rows)
            {
                var isActive = next < m_Active.Count && m_Active[next].Need == row.Need;
                if (isActive)
                {
                    Show(row, m_Active[next++]);
                    visibleRows |= 1 << (int)row.Need;
                }
                if (row.Root.activeSelf != isActive)
                    row.Root.SetActive(isActive);
            }

            // The panel changes its size only when a row appears or disappears; check it then.
            if (visibleRows != m_VisibleRows)
            {
                m_VisibleRows = visibleRows;
                WarnIfCoveringBuildings();
            }
        }

        void OnStateChanged(WaveState previous, WaveState next)
        {
            if (next == WaveState.WaveIncoming)
                m_Progress.StartWave(Buildings);
        }

        WaveDefinition CurrentWave()
        {
            var waves = WaveConfig.Current.Waves;
            var index = waveLoop.Wave - 1;
            return index >= 0 && index < waves.Count ? waves[index] : null;
        }

        void Show(Row row, NeedProgress progress)
        {
            if (row.Shown.HasValue && row.Shown.Value.Equals(progress)) return;
            row.Shown = progress;

            row.Count.text = $"{progress.Served}/{progress.Total}";
            row.Worker.text = progress.HasWorker ? m_WorkerText : string.Empty;

            var color = progress.HasBuilding ? k_TextColor : k_NoBuildingColor;
            row.Name.color = color;
            row.Count.color = color;
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

        /// <summary>
        /// SCRUM-79: the panel must not cover the island. Logs a warning once per building whose on-screen bounds
        /// overlap the panel, e.g. after the camera or the island layout changes. Editor and development builds only.
        /// </summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        void WarnIfCoveringBuildings()
        {
            var camera = Camera.main;
            if (camera == null) return;

            LayoutRebuilder.ForceRebuildLayoutImmediate(m_PanelRect);
            var panel = ScreenRect(m_PanelRect);

            foreach (var building in Buildings)
            {
                if (building == null || m_CoveredBuildings.Contains(building)) continue;
                if (!TryGetScreenRect(building, camera, out var bounds) || !panel.Overlaps(bounds)) continue;

                m_CoveredBuildings.Add(building);
                Debug.LogWarning(
                    $"Wave Needs Panel covers {building.name} (SCRUM-79). Change margin or topOffset of the panel, or the camera.",
                    this);
            }
        }

        /// <summary>Rect of a RectTransform on a Screen Space Overlay canvas, in screen pixels.</summary>
        static Rect ScreenRect(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        /// <summary>Screen pixels covered by the renderers of a building; false if it has none in front of the camera.</summary>
        static bool TryGetScreenRect(Building building, Camera camera, out Rect rect)
        {
            rect = default;
            var found = false;
            var min = Vector2.positiveInfinity;
            var max = Vector2.negativeInfinity;

            foreach (var renderer in building.GetComponentsInChildren<Renderer>())
            {
                var bounds = renderer.bounds;
                for (var corner = 0; corner < 8; corner++)
                {
                    var world = new Vector3(
                        (corner & 1) == 0 ? bounds.min.x : bounds.max.x,
                        (corner & 2) == 0 ? bounds.min.y : bounds.max.y,
                        (corner & 4) == 0 ? bounds.min.z : bounds.max.z);
                    var screen = camera.WorldToScreenPoint(world);
                    if (screen.z <= 0f) continue;

                    min = Vector2.Min(min, screen);
                    max = Vector2.Max(max, screen);
                    found = true;
                }
            }

            if (found)
                rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            return found;
        }

        void BuildCanvas()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvasObject = new GameObject("Wave Needs Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            // Landscape game: the panel keeps its size relative to the screen height.
            scaler.matchWidthOrHeight = 1f;

            m_SafeArea = CreateRect("Safe Area", canvasObject.transform);
            m_SafeArea.anchorMin = Vector2.zero;
            m_SafeArea.anchorMax = Vector2.one;
            m_SafeArea.offsetMin = Vector2.zero;
            m_SafeArea.offsetMax = Vector2.zero;

            // Top right corner below the top bar, pivot at the top: a new row only makes the panel longer
            // downwards, the rows above it stay where they were (SCRUM-79).
            var panel = CreateRect("Wave Needs Panel", m_SafeArea);
            panel.anchorMin = Vector2.one;
            panel.anchorMax = Vector2.one;
            panel.pivot = Vector2.one;
            panel.anchoredPosition = new Vector2(-margin, -topOffset);
            m_PanelRect = panel;

            var background = panel.gameObject.AddComponent<Image>();
            background.color = k_BackgroundColor;
            background.raycastTarget = false;

            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 16, 16);
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            foreach (var need in WaveNeedsProgress.Needs)
                m_Rows.Add(CreateRow(need, panel, font));

            m_Panel = panel.gameObject;
            m_Panel.SetActive(false);
        }

        Row CreateRow(Need need, Transform parent, Font font)
        {
            var root = CreateRect(need.ToString(), parent);
            var layout = root.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 16f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            // Every row has the same height, so 3, 4 or 5 rows always stack the same way (SCRUM-79).
            var rowElement = root.gameObject.AddComponent<LayoutElement>();
            rowElement.minHeight = Mathf.Ceil(fontSize * 1.3f);
            rowElement.preferredHeight = rowElement.minHeight;

            // Fixed column widths, so the numbers do not shift the row while they change.
            var badgeSize = Mathf.RoundToInt(fontSize * 0.8f);
            var row = new Row(need, root.gameObject,
                CreateText("Need", root, font, fontSize, fontSize * 6.5f, TextAnchor.MiddleLeft),
                CreateText("Count", root, font, fontSize, fontSize * 3.5f, TextAnchor.MiddleRight),
                CreateText("Worker", root, font, badgeSize, badgeSize * 4.5f, TextAnchor.MiddleLeft),
                new LocalizedString(TableName, LabelKey(need)));
            row.Worker.color = k_WorkerColor;

            row.LabelChanged = value => row.Name.text = value;
            row.Label.StringChanged += row.LabelChanged;

            root.gameObject.SetActive(false);
            return row;
        }

        static string LabelKey(Need need) => need switch
        {
            Need.Rooms => "need.rooms",
            Need.Drinks => "need.drinks",
            Need.Food => "need.food",
            Need.Relax => "need.relax",
            Need.OceanView => "need.ocean_view",
            _ => throw new ArgumentOutOfRangeException(nameof(need), need, null)
        };

        static RectTransform CreateRect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        static Text CreateText(string name, Transform parent, Font font, int size, float width, TextAnchor alignment)
        {
            var rect = CreateRect(name, parent);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = k_TextColor;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            var element = rect.gameObject.AddComponent<LayoutElement>();
            element.minWidth = width;
            element.preferredWidth = width;
            return text;
        }

        sealed class Row
        {
            public Row(Need need, GameObject root, Text name, Text count, Text worker, LocalizedString label)
            {
                Need = need;
                Root = root;
                Name = name;
                Count = count;
                Worker = worker;
                Label = label;
            }

            public Need Need { get; }
            public GameObject Root { get; }
            public Text Name { get; }
            public Text Count { get; }
            public Text Worker { get; }
            public LocalizedString Label { get; }
            public LocalizedString.ChangeHandler LabelChanged { get; set; }

            /// <summary>Values on screen; null forces the next Show to write the texts again.</summary>
            public NeedProgress? Shown { get; set; }
        }
    }
}
