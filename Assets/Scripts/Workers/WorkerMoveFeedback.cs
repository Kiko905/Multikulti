using System.Collections.Generic;
using Multikulti.Buildings;
using Multikulti.Config;
using UnityEngine;

namespace Multikulti.Workers
{
    /// <summary>
    /// Visual feedback after a Worker move (SCRUM-41, build document §4.5 AC2 and AC2b, UI handoff S3).
    ///
    /// - In front of every building, on the side facing the camera, stands its service meter (ServiceMeter):
    ///   one bar per seat that fills with the service progress, and speed pips. With a Worker the bars fill
    ///   about three times faster and all pips are lit; without a Worker one pip is lit.
    /// - Every Worker is a token standing next to the meter. A building without a Worker has no token,
    ///   is darker and its meter turns orange: it must look unstaffed (build document §4.5).
    /// - A move shows in the same frame: the target building flashes green and pops with its meter,
    ///   its pips light up and its bars speed up; the source building flashes red, shrinks for a moment,
    ///   turns dark and slows down; the token hops from the source to the target. The player sees what was
    ///   gained and what was given up (AC2b: allocation is a zero-sum decision).
    /// - A new Worker from an unlock (WorkerRoster.AddBuilding) drops onto its building from above.
    ///
    /// Workers are anonymous: the token has no name, face or job (no named staff in the prototype).
    /// Graybox: tokens are capsules, meters are flat cubes; buildings are tinted with a MaterialPropertyBlock,
    /// their shared material is not changed. Animations run on unscaled time, so they finish the same way
    /// at any time scale.
    /// </summary>
    [RequireComponent(typeof(WorkerManager))]
    public sealed class WorkerMoveFeedback : MonoBehaviour
    {
        static readonly int k_BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int k_ColorId = Shader.PropertyToID("_Color");
        const float TokenHalfHeight = 0.3f;
        const float TokenRadius = 0.175f;

        [SerializeField] Color workerColor = new Color(1f, 0.76f, 0.18f);
        [SerializeField, Range(0f, 1f), Tooltip("Brightness of a building without a Worker (1 = same as with a Worker).")]
        float emptyBrightness = 0.5f;
        [SerializeField, Tooltip("Flash of the building that got the Worker.")]
        Color gainColor = new Color(0.35f, 0.95f, 0.45f);
        [SerializeField, Tooltip("Flash of the building the Worker left.")]
        Color lossColor = new Color(0.95f, 0.35f, 0.3f);
        [SerializeField, Tooltip("Service meter of a building with a Worker (full speed).")]
        Color fastColor = new Color(0.35f, 0.9f, 0.45f);
        [SerializeField, Tooltip("Service meter of a building without a Worker (slow service).")]
        Color slowColor = new Color(0.95f, 0.55f, 0.2f);
        [SerializeField, Min(0.05f)] float hopSeconds = 0.45f;
        [SerializeField, Min(0f)] float hopHeight = 1.5f;
        [SerializeField, Min(0.05f)] float pulseSeconds = 0.5f;
        [SerializeField, Range(0f, 0.5f), Tooltip("How much the target building grows at the peak of its pop.")]
        float gainPop = 0.18f;
        [SerializeField, Range(0f, 0.5f), Tooltip("How much the source building shrinks at the peak of its dip.")]
        float lossDip = 0.1f;
        [SerializeField, Min(0f), Tooltip("Gap between the building and its token and meter standing in front of it.")]
        float tokenGap = 0.55f;
        [SerializeField, Min(0.2f)] float meterWidth = 0.9f;
        [SerializeField, Min(0.1f)] float meterBarHeight = 0.55f;

        sealed class BuildingView
        {
            public Building Building;
            public Renderer[] Renderers;
            public Color[] BaseColors;
            public Vector3 BaseScale;
            public Vector3 CenterOffset;
            public Vector3 Extents;
            public float GroundY;
            public float PulseStart = -1f;
            public bool Gained;
            public ServiceMeter Meter;
        }

        sealed class Token
        {
            public Transform Transform;
            public int Building;
            public Vector3 HopFrom;
            public float HopStart = -1f;
        }

        WorkerManager m_Manager;
        readonly List<BuildingView> m_Buildings = new List<BuildingView>();
        readonly List<Token> m_Tokens = new List<Token>();
        readonly List<Material> m_Materials = new List<Material>();
        MaterialPropertyBlock m_Block;
        Material m_MeterMaterial;
        Transform m_Root;
        Vector3 m_ToCamera = Vector3.back;
        Vector3 m_CameraRight = Vector3.right;
        Quaternion m_CameraRotation = Quaternion.identity;

        /// <summary>Building index the Worker token is on or flying to.</summary>
        public int TokenBuilding(int worker) => m_Tokens[worker].Building;

        /// <summary>Token of a Worker (for tests and later art).</summary>
        public Transform TokenOf(int worker) => m_Tokens[worker].Transform;

        /// <summary>Service meter in front of a building (for tests and later art).</summary>
        public ServiceMeter MeterOf(int building) => m_Buildings[building].Meter;

        /// <summary>Where the token of a Worker on this building stands.</summary>
        public Vector3 StandPoint(int building)
        {
            if (!TryFront(building, out var front))
                return transform.position;
            var point = front - m_CameraRight * (TokenRadius + 0.12f);
            point.y += TokenHalfHeight;
            return point;
        }

        /// <summary>Lit speed pips for a queue: all at full speed with a Worker, fewer when slower.</summary>
        public static int SpeedPips(BuildingQueue queue, float fullSpeed)
        {
            if (fullSpeed <= 0f) return ServiceMeter.PipCount;
            var speed = queue.Utilization * queue.SpeedMultiplier / fullSpeed;
            return Mathf.Clamp(Mathf.RoundToInt(speed * ServiceMeter.PipCount), 1, ServiceMeter.PipCount);
        }

        void Awake()
        {
            m_Manager = GetComponent<WorkerManager>();
            m_Block = new MaterialPropertyBlock();
            m_Root = new GameObject("Move Feedback (graybox)").transform;
            m_Root.SetParent(transform, false);
            m_MeterMaterial = CreateMatteMaterial();
        }

        // The meter faces the camera: with the default gloss its dark panel reflects the sky and turns light grey,
        // and the bars lose their contrast. A matte copy keeps the colours as set.
        Material CreateMatteMaterial()
        {
            var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            probe.SetActive(false);
            var material = new Material(probe.GetComponent<Renderer>().sharedMaterial) { name = "Service Meter (graybox)" };
            Destroy(probe);
            material.SetFloat("_Smoothness", 0f);
            material.SetFloat("_Glossiness", 0f);
            material.SetFloat("_SpecularHighlights", 0f);
            material.SetFloat("_EnvironmentReflections", 0f);
            material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            m_Materials.Add(material);
            return material;
        }

        void Start()
        {
            m_Manager.Roster.WorkerMoved += OnWorkerMoved;
            Sync();
            ReadCamera();
            foreach (var token in m_Tokens)
                token.Transform.position = StandPoint(token.Building);
        }

        void OnDestroy()
        {
            if (m_Manager != null && m_Manager.Roster != null)
                m_Manager.Roster.WorkerMoved -= OnWorkerMoved;
            foreach (var view in m_Buildings)
            {
                if (view.Building == null) continue;
                view.Building.transform.localScale = view.BaseScale;
                foreach (var renderer in view.Renderers)
                    if (renderer != null)
                        renderer.SetPropertyBlock(null);
            }
            foreach (var material in m_Materials)
                Destroy(material);
        }

        void OnWorkerMoved(int worker, int from, int to)
        {
            Sync();
            ReadCamera();
            var now = Time.unscaledTime;
            var token = m_Tokens[worker];
            // A new Worker (from < 0) drops onto its building from above.
            token.HopFrom = from >= 0 ? token.Transform.position : StandPoint(to) + Vector3.up * (hopHeight * 2f);
            token.Building = to;
            token.HopStart = now;

            if (from >= 0)
                StartPulse(from, false, now);
            StartPulse(to, true, now);
            // Apply at once, so the change is on screen in this frame even if LateUpdate already ran.
            Apply(now);
        }

        void StartPulse(int building, bool gained, float now)
        {
            if (building < 0 || building >= m_Buildings.Count) return;
            m_Buildings[building].PulseStart = now;
            m_Buildings[building].Gained = gained;
        }

        void LateUpdate()
        {
            ReadCamera();
            Apply(Time.unscaledTime);
        }

        void ReadCamera()
        {
            var camera = Camera.main;
            if (camera == null) return;
            var forward = camera.transform.forward;
            var toCamera = new Vector3(-forward.x, 0f, -forward.z);
            if (toCamera.sqrMagnitude < 0.0001f) return;
            m_ToCamera = toCamera.normalized;
            m_CameraRight = Vector3.Cross(Vector3.up, -m_ToCamera);
            m_CameraRotation = camera.transform.rotation;
        }

        void Apply(float now)
        {
            var roster = m_Manager.Roster;
            var fullSpeed = WaveConfig.Current.Service.UtilizationWorker;
            for (var i = 0; i < m_Buildings.Count; i++)
                ApplyBuilding(i, roster.HasWorker(i), fullSpeed, now);
            foreach (var token in m_Tokens)
                ApplyToken(token, now);
        }

        void ApplyBuilding(int index, bool hasWorker, float fullSpeed, float now)
        {
            var view = m_Buildings[index];
            if (view.Building == null) return;

            var brightness = hasWorker ? 1f : emptyBrightness;
            var flash = 0f;
            var scale = 1f;
            if (view.PulseStart >= 0f)
            {
                var t = (now - view.PulseStart) / pulseSeconds;
                if (t >= 1f)
                {
                    view.PulseStart = -1f;
                }
                else
                {
                    flash = 1f - t;
                    var wave = Mathf.Sin(Mathf.PI * t);
                    scale = view.Gained ? 1f + gainPop * wave : 1f - lossDip * wave;
                }
            }

            view.Building.transform.localScale = view.BaseScale * scale;
            var flashColor = view.Gained ? gainColor : lossColor;
            for (var r = 0; r < view.Renderers.Length; r++)
            {
                var renderer = view.Renderers[r];
                if (renderer == null) continue;
                var baseColor = view.BaseColors[r];
                var color = new Color(baseColor.r * brightness, baseColor.g * brightness, baseColor.b * brightness, baseColor.a);
                color = Color.Lerp(color, flashColor, flash * flash);
                renderer.GetPropertyBlock(m_Block);
                m_Block.SetColor(k_BaseColorId, color);
                m_Block.SetColor(k_ColorId, color);
                renderer.SetPropertyBlock(m_Block);
            }

            if (view.Meter != null && TryFront(index, out var front))
            {
                var meter = view.Meter;
                var point = front + m_CameraRight * (meterWidth * 0.5f + 0.15f);
                meter.Root.SetPositionAndRotation(point, m_CameraRotation);
                meter.Root.localScale = Vector3.one * scale;
                var queue = view.Building.Queue;
                if (queue != null)
                    meter.Show(queue, SpeedPips(queue, fullSpeed), hasWorker ? fastColor : slowColor);
            }
        }

        void ApplyToken(Token token, float now)
        {
            var rest = StandPoint(token.Building);
            if (token.HopStart < 0f)
            {
                token.Transform.position = rest;
                return;
            }

            var t = (now - token.HopStart) / hopSeconds;
            if (t >= 1f)
            {
                token.HopStart = -1f;
                token.Transform.position = rest;
                return;
            }

            var eased = Mathf.SmoothStep(0f, 1f, t);
            token.Transform.position = Vector3.Lerp(token.HopFrom, rest, eased) + Vector3.up * (hopHeight * 4f * t * (1f - t));
        }

        // The ground point in front of the building, on the side that faces the camera, clear of its corner.
        bool TryFront(int building, out Vector3 front)
        {
            front = default;
            if (building < 0 || building >= m_Buildings.Count || m_Buildings[building].Building == null)
                return false;
            var view = m_Buildings[building];
            // Distance from the centre to the building's side in the camera direction (its box seen from the corner).
            var reach = Mathf.Abs(m_ToCamera.x) * view.Extents.x + Mathf.Abs(m_ToCamera.z) * view.Extents.z;
            var center = view.Building.transform.position + view.CenterOffset;
            front = center + m_ToCamera * (reach + tokenGap);
            front.y = view.GroundY;
            return true;
        }

        // Keeps one view per building and one token per Worker; buildings and Workers only get added (unlocks).
        void Sync()
        {
            var buildings = m_Manager.Buildings;
            for (var i = m_Buildings.Count; i < buildings.Count; i++)
                m_Buildings.Add(CreateView(buildings[i]));

            var roster = m_Manager.Roster;
            for (var w = m_Tokens.Count; w < roster.WorkerCount; w++)
                m_Tokens.Add(new Token { Transform = CreateToken(w), Building = roster.BuildingOf(w) });
        }

        BuildingView CreateView(Building building)
        {
            var renderers = building.GetComponentsInChildren<Renderer>();
            var colors = new Color[renderers.Length];
            var bounds = new Bounds(building.transform.position, Vector3.one);
            for (var r = 0; r < renderers.Length; r++)
            {
                var material = renderers[r].sharedMaterial;
                colors[r] = material == null ? Color.white
                    : material.HasProperty(k_BaseColorId) ? material.GetColor(k_BaseColorId)
                    : material.HasProperty(k_ColorId) ? material.GetColor(k_ColorId)
                    : Color.white;
                if (r == 0) bounds = renderers[r].bounds;
                else bounds.Encapsulate(renderers[r].bounds);
            }

            return new BuildingView
            {
                Building = building,
                Renderers = renderers,
                BaseColors = colors,
                BaseScale = building.transform.localScale,
                CenterOffset = bounds.center - building.transform.position,
                Extents = bounds.extents,
                GroundY = bounds.min.y,
                Meter = new ServiceMeter($"Service Meter ({building.name})", m_Root, m_MeterMaterial, m_Block, meterWidth, meterBarHeight)
            };
        }

        Transform CreateToken(int worker)
        {
            var token = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            token.name = $"Worker {worker}";
            token.transform.SetParent(m_Root, false);
            token.transform.localScale = new Vector3(TokenRadius * 2f, TokenHalfHeight, TokenRadius * 2f);
            // Taps are meant for the buildings (WorkerTapInput raycasts).
            Destroy(token.GetComponent<Collider>());

            var renderer = token.GetComponent<Renderer>();
            var material = new Material(renderer.sharedMaterial) { color = workerColor };
            m_Materials.Add(material);
            renderer.sharedMaterial = material;
            return token.transform;
        }
    }
}
