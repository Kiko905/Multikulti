using System;
using System.Collections.Generic;
using Multikulti.Buildings;
using Multikulti.Waves;
using UnityEngine;

namespace Multikulti.Island
{
    /// <summary>
    /// The island map of the Home screen (SCRUM-21, UI handoff S1): sea, island, pier for the guests' ship,
    /// the buildings of the scene and construction sites for buildings that are not built yet.
    ///
    /// A construction site belongs to a need (Pool → Relax, Bungalows → Ocean View) and is shown while
    /// no Building with that need is in the scene. The site must look like a prepared place, not like
    /// an unfinished map (build document §3, US1). Buying the unlock and the tap on a site are SCRUM-51 to SCRUM-53;
    /// whoever adds the building calls Refresh, and the site disappears.
    ///
    /// Graybox: like ShipArrival, the map builds simple shapes from primitives. Positions are world x and z;
    /// their y is ignored, heights come from groundHeight and seaLevel. The buildings stand on groundHeight
    /// (the default cube of a building reaches 0.5 below its centre).
    /// No collider is left on the map, so taps only hit buildings (WorkerTapInput).
    /// </summary>
    public sealed class IslandMap : MonoBehaviour
    {
        [Serializable]
        public struct ConstructionSite
        {
            public Need need;
            public Vector3 position;
            public Vector2 size;
        }

        /// <summary>A construction site that is shown now.</summary>
        public readonly struct VisibleSite
        {
            public readonly Need Need;
            /// <summary>World point above the site for its marker.</summary>
            public readonly Vector3 MarkerPoint;

            public VisibleSite(Need need, Vector3 markerPoint)
            {
                Need = need;
                MarkerPoint = markerPoint;
            }
        }

        static readonly Color k_SeaColor = new Color(0.16f, 0.52f, 0.62f);
        static readonly Color k_SandColor = new Color(0.93f, 0.84f, 0.62f);
        static readonly Color k_GrassColor = new Color(0.47f, 0.68f, 0.34f);
        static readonly Color k_WoodColor = new Color(0.55f, 0.38f, 0.22f);
        static readonly Color k_SiteColor = new Color(0.82f, 0.68f, 0.46f);

        const int OutlineSamples = 24;
        const float PostHeight = 0.6f;
        const float MarkerLift = 0.35f;

        [SerializeField] WaveLoop waveLoop;
        [SerializeField, Min(1f)] float islandRadius = 7.5f;
        [SerializeField, Range(0.1f, 1f), Tooltip("Grass radius as a part of the island radius; the rest is beach.")]
        float grassPart = 0.78f;
        [SerializeField, Tooltip("Height of the island surface; buildings stand on it.")]
        float groundHeight = -0.5f;
        [SerializeField] float seaLevel = -0.65f;
        [SerializeField, Tooltip("Pier from the beach along the docked ship (ShipArrival dockAt).")]
        Vector3 pierStart = new Vector3(-3.3f, 0f, -6.55f);
        [SerializeField] Vector3 pierEnd = new Vector3(-7.05f, 0f, -7.55f);
        [SerializeField, Min(0f), Tooltip("Space around the pier end that stays on screen, so the docked ship is not cut off.")]
        float berthRadius = 2.2f;
        [SerializeField] ConstructionSite[] constructionSites =
        {
            new ConstructionSite { need = Need.Relax, position = new Vector3(-4f, 0f, 3f), size = new Vector2(2.6f, 2.6f) },
            new ConstructionSite { need = Need.OceanView, position = new Vector3(2f, 0f, -4.5f), size = new Vector2(3f, 2.2f) }
        };

        readonly List<Building> m_Buildings = new List<Building>();
        readonly List<VisibleSite> m_VisibleSites = new List<VisibleSite>();
        readonly List<Vector3> m_FramingPoints = new List<Vector3>();
        readonly List<Material> m_Materials = new List<Material>();
        GameObject[] m_SiteObjects = Array.Empty<GameObject>();

        /// <summary>Raised after Refresh: buildings, construction sites or framing points may have changed.</summary>
        public event Action Refreshed;

        /// <summary>Buildings found by the last Refresh.</summary>
        public IReadOnlyList<Building> Buildings => m_Buildings;

        /// <summary>Construction sites shown after the last Refresh.</summary>
        public IReadOnlyList<VisibleSite> VisibleSites => m_VisibleSites;

        /// <summary>World points the camera must keep on screen: shore, pier, buildings and shown sites.</summary>
        public IReadOnlyList<Vector3> FramingPoints => m_FramingPoints;

        /// <summary>Centre of the island surface.</summary>
        public Vector3 Center => new Vector3(transform.position.x, groundHeight, transform.position.z);

        void Awake()
        {
            if (waveLoop == null)
                waveLoop = FindAnyObjectByType<WaveLoop>();
            BuildGraybox();
        }

        void Start()
        {
            if (waveLoop != null)
                waveLoop.Machine.StateChanged += OnStateChanged;
            Refresh();
        }

        void OnDestroy()
        {
            if (waveLoop != null && waveLoop.Machine != null)
                waveLoop.Machine.StateChanged -= OnStateChanged;
            foreach (var material in m_Materials)
                Destroy(material);
        }

        // A building bought in Upgrade (SCRUM-52) is on the map when the player is back on Home.
        void OnStateChanged(WaveState from, WaveState to)
        {
            if (to == WaveState.AwaitingWave)
                Refresh();
        }

        /// <summary>Finds the buildings again and shows a construction site for every need without a building.</summary>
        public void Refresh()
        {
            m_Buildings.Clear();
            foreach (var building in FindObjectsByType<Building>(FindObjectsSortMode.None))
                if (building.isActiveAndEnabled)
                    m_Buildings.Add(building);

            m_VisibleSites.Clear();
            for (var i = 0; i < constructionSites.Length; i++)
            {
                var site = constructionSites[i];
                var visible = !HasBuilding(site.need);
                m_SiteObjects[i].SetActive(visible);
                if (visible)
                    m_VisibleSites.Add(new VisibleSite(site.need, SitePoint(site, PostHeight + MarkerLift)));
            }

            CollectFramingPoints();
            Refreshed?.Invoke();
        }

        /// <summary>World point above a building for its marker.</summary>
        public static Vector3 MarkerPoint(Building building)
        {
            var renderer = building.GetComponentInChildren<Renderer>();
            if (renderer == null)
                return building.transform.position + Vector3.up * (0.5f + MarkerLift);
            var bounds = renderer.bounds;
            return new Vector3(bounds.center.x, bounds.max.y + MarkerLift, bounds.center.z);
        }

        bool HasBuilding(Need need)
        {
            foreach (var building in m_Buildings)
                if (building.Need == need)
                    return true;
            return false;
        }

        Vector3 SitePoint(ConstructionSite site, float lift) =>
            new Vector3(site.position.x, groundHeight + lift, site.position.z);

        void CollectFramingPoints()
        {
            m_FramingPoints.Clear();
            var center = Center;
            for (var i = 0; i < OutlineSamples; i++)
            {
                var angle = i * Mathf.PI * 2f / OutlineSamples;
                var offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * islandRadius;
                m_FramingPoints.Add(new Vector3(center.x + offset.x, groundHeight, center.z + offset.z));
                m_FramingPoints.Add(new Vector3(center.x + offset.x, seaLevel, center.z + offset.z));
            }
            for (var i = 0; i < 4; i++)
            {
                var angle = i * Mathf.PI * 0.5f;
                var berth = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * berthRadius;
                m_FramingPoints.Add(new Vector3(pierEnd.x + berth.x, seaLevel, pierEnd.z + berth.z));
                m_FramingPoints.Add(new Vector3(pierEnd.x + berth.x, groundHeight + 1.5f, pierEnd.z + berth.z));
            }

            foreach (var building in m_Buildings)
            {
                m_FramingPoints.Add(MarkerPoint(building));
                var renderer = building.GetComponentInChildren<Renderer>();
                if (renderer == null) continue;
                var bounds = renderer.bounds;
                m_FramingPoints.Add(bounds.min);
                m_FramingPoints.Add(bounds.max);
            }

            foreach (var site in m_VisibleSites)
                m_FramingPoints.Add(site.MarkerPoint);
        }

        void BuildGraybox()
        {
            var root = new GameObject("Island (graybox)").transform;
            root.SetParent(transform, false);
            var center = Center;

            var sea = CreatePart(PrimitiveType.Plane, "Sea", root, k_SeaColor);
            sea.position = new Vector3(center.x, seaLevel, center.z);
            sea.localScale = new Vector3(30f, 1f, 30f);

            // The island reaches below the sea, so its side shows as a beach edge.
            var bottom = seaLevel - 0.5f;
            var sand = CreatePart(PrimitiveType.Cylinder, "Beach", root, k_SandColor);
            sand.position = new Vector3(center.x, (groundHeight + bottom) * 0.5f, center.z);
            sand.localScale = new Vector3(islandRadius * 2f, (groundHeight - bottom) * 0.5f, islandRadius * 2f);

            var grassRadius = islandRadius * grassPart;
            var grass = CreatePart(PrimitiveType.Cylinder, "Grass", root, k_GrassColor);
            grass.position = new Vector3(center.x, groundHeight - 0.01f, center.z);
            grass.localScale = new Vector3(grassRadius * 2f, 0.02f, grassRadius * 2f);

            var pierFrom = new Vector3(pierStart.x, seaLevel + 0.12f, pierStart.z);
            var pierTo = new Vector3(pierEnd.x, seaLevel + 0.12f, pierEnd.z);
            var pierDirection = pierTo - pierFrom;
            if (pierDirection.sqrMagnitude > 0.0001f)
            {
                var pier = CreatePart(PrimitiveType.Cube, "Pier", root, k_WoodColor);
                pier.SetPositionAndRotation((pierFrom + pierTo) * 0.5f, Quaternion.LookRotation(pierDirection));
                pier.localScale = new Vector3(1f, 0.12f, pierDirection.magnitude);
            }

            m_SiteObjects = new GameObject[constructionSites.Length];
            for (var i = 0; i < constructionSites.Length; i++)
                m_SiteObjects[i] = BuildSite(constructionSites[i], root);
        }

        // A levelled wooden platform with corner posts: a prepared place, not a hole in the map.
        GameObject BuildSite(ConstructionSite site, Transform parent)
        {
            var root = new GameObject($"Construction Site ({site.need})").transform;
            root.SetParent(parent, false);
            root.position = SitePoint(site, 0f);

            var platform = CreatePart(PrimitiveType.Cube, "Platform", root, k_SiteColor);
            platform.localPosition = new Vector3(0f, 0.04f, 0f);
            platform.localScale = new Vector3(site.size.x, 0.08f, site.size.y);

            var half = new Vector3(site.size.x * 0.5f - 0.1f, 0f, site.size.y * 0.5f - 0.1f);
            for (var corner = 0; corner < 4; corner++)
            {
                var post = CreatePart(PrimitiveType.Cube, "Post", root, k_WoodColor);
                var x = (corner & 1) == 0 ? -half.x : half.x;
                var z = (corner & 2) == 0 ? -half.z : half.z;
                post.localPosition = new Vector3(x, PostHeight * 0.5f, z);
                post.localScale = new Vector3(0.12f, PostHeight, 0.12f);
            }
            return root.gameObject;
        }

        Transform CreatePart(PrimitiveType type, string name, Transform parent, Color color)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.transform.SetParent(parent, false);
            // Taps are meant for the buildings (WorkerTapInput raycasts).
            Destroy(part.GetComponent<Collider>());

            var renderer = part.GetComponent<Renderer>();
            var material = new Material(renderer.sharedMaterial) { color = color };
            m_Materials.Add(material);
            renderer.sharedMaterial = material;
            return part.transform;
        }
    }
}
