using System.Collections.Generic;
using Multikulti.Config;
using UnityEngine;

namespace Multikulti.Buildings
{
    /// <summary>
    /// Graybox model of a building in its 3 levels (SCRUM-101, build document §4.5 "Progresné rady", §4.8, AC2d):
    /// 5 buildings × 3 levels = 15 looks, built from primitives at runtime until the art arrives.
    ///
    /// - Seats are visible: a row of seat objects in front of the building, one per seat of its capacity
    ///   (3 → 5 → 7): armchairs at the Reception, stools at the Beach Bar, tables at the Restaurant, sun loungers
    ///   at the Pool, deck chairs at the Bungalows. A seated guest sits on its seat until served, so a full row
    ///   with guests waiting reads as "too small", a slow row as "unstaffed".
    /// - The structure grows with the level, so the level reads from a distance: the hotel gets floors, the bar
    ///   a sign and a second roof, the restaurant an upper floor, the pool parasols and a slide, the bungalows
    ///   more huts. The footprint stays the same, so nothing around the building moves.
    /// - A building without a Worker is darker and has no Worker token (WorkerMoveFeedback, SCRUM-41); the model
    ///   is built before WorkerMoveFeedback starts and only switches renderers on and off, so the tint and the pop
    ///   of a move apply to the whole model.
    ///
    /// The building's own cube stays in the scene, so the scene does not change: it is not drawn, but its mesh is
    /// replaced by the model's bounding box, so the marker above the building, the camera framing and the service
    /// meter see the real size, and its collider covers the whole model for taps.
    /// Added at runtime to every Building; a building cloned for an Area Unlock builds its own model.
    /// </summary>
    [RequireComponent(typeof(Building))]
    public sealed class BuildingModel : MonoBehaviour
    {
        const string ModelName = "Model (graybox)";
        // The building's cube spans -0.5..0.5, so its bottom is the ground.
        const float Ground = -0.5f;
        const float RowInset = 0.32f;
        const float BoundsMargin = 0.25f;
        const int AnyLevel = int.MaxValue;

        static readonly Color k_Wall = new Color(0.94f, 0.89f, 0.78f);
        static readonly Color k_White = new Color(0.95f, 0.95f, 0.92f);
        static readonly Color k_Terracotta = new Color(0.8f, 0.4f, 0.28f);
        static readonly Color k_Window = new Color(0.2f, 0.32f, 0.5f);
        static readonly Color k_Door = new Color(0.45f, 0.3f, 0.2f);
        static readonly Color k_Wood = new Color(0.55f, 0.36f, 0.22f);
        static readonly Color k_LightWood = new Color(0.72f, 0.53f, 0.34f);
        static readonly Color k_Thatch = new Color(0.86f, 0.72f, 0.42f);
        static readonly Color k_Pole = new Color(0.35f, 0.35f, 0.38f);
        static readonly Color k_Flag = new Color(0.3f, 0.75f, 0.95f);
        static readonly Color k_Teal = new Color(0.2f, 0.65f, 0.7f);
        static readonly Color k_Red = new Color(0.86f, 0.27f, 0.23f);
        static readonly Color k_Yellow = new Color(1f, 0.8f, 0.25f);
        static readonly Color k_Water = new Color(0.3f, 0.74f, 0.95f);
        static readonly Color k_Armchair = new Color(0.25f, 0.45f, 0.8f);
        static readonly Color k_Stool = new Color(0.85f, 0.3f, 0.25f);
        static readonly Color k_Chair = new Color(0.3f, 0.62f, 0.35f);
        static readonly Color k_Lounger = new Color(1f, 0.93f, 0.6f);
        static readonly Color k_DeckChair = new Color(0.35f, 0.6f, 0.95f);
        static readonly Color[] k_Guests =
        {
            new Color(0.95f, 0.45f, 0.4f), new Color(0.35f, 0.75f, 0.7f), new Color(0.65f, 0.45f, 0.85f),
            new Color(1f, 0.6f, 0.25f)
        };

        static readonly Dictionary<Color, Material> s_Materials = new Dictionary<Color, Material>();
        static Material s_Template;
        static Mesh s_Cube, s_Cylinder, s_Sphere, s_Capsule, s_Pyramid;

        sealed class Part
        {
            public Renderer Renderer;
            public int MinLevel;
            public int MaxLevel;
            public float Top;
        }

        sealed class Seat
        {
            public Transform Root;
            public Renderer[] Renderers;
            public Renderer[] Guest;
        }

        Building m_Building;
        Transform m_Model;
        readonly List<Part> m_Parts = new List<Part>();
        readonly Seat[] m_Seats = new Seat[BuildingModelRules.MaxSeats];
        Unit[] m_Occupied = new Unit[0];
        Vector2 m_Half;
        float m_RowZ;
        float m_SeatTop;
        int m_Level = -1;
        int m_SeatCount = -1;
        bool m_Warned;
        Mesh m_Bounds;

        /// <summary>Level the model shows (for tests).</summary>
        public int ShownLevel => m_Level;

        /// <summary>Seat objects shown (for tests): 3 / 5 / 7.</summary>
        public int SeatsShown => m_SeatCount;

        /// <summary>Guests sitting on seats now (for tests).</summary>
        public int GuestsShown
        {
            get
            {
                var count = 0;
                foreach (var seat in m_Seats)
                    if (seat != null && seat.Guest[0].enabled) count++;
                return count;
            }
        }

        /// <summary>Structure parts shown on this level (for tests): more parts on a higher level.</summary>
        public int PartsShown
        {
            get
            {
                var count = 0;
                foreach (var part in m_Parts)
                    if (part.Renderer.enabled) count++;
                return count;
            }
        }

        /// <summary>Height of the model above the ground on this level (for tests).</summary>
        public float Height { get; private set; }

        /// <summary>Local position of a shown seat (for tests).</summary>
        public Vector3 SeatPosition(int index) => m_Seats[index].Root.localPosition;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            // After the scene's Awake and before any Start, so WorkerMoveFeedback sees the model.
            foreach (var building in FindObjectsByType<Building>(FindObjectsSortMode.None))
            {
                if (building.GetComponent<BuildingModel>() == null)
                    building.gameObject.AddComponent<BuildingModel>();
            }
        }

        void Awake()
        {
            m_Building = GetComponent<Building>();
            // A building cloned for an Area Unlock carries the model of its template; it builds its own.
            var copied = transform.Find(ModelName);
            if (copied != null)
            {
                copied.gameObject.SetActive(false);
                Destroy(copied.gameObject);
            }

            Build();
            Apply(true);
        }

        void OnDestroy()
        {
            if (m_Bounds != null) Destroy(m_Bounds);
        }

        void LateUpdate()
        {
            Apply(false);
            ShowGuests();
        }

        void Build()
        {
            m_Model = new GameObject(ModelName).transform;
            m_Model.SetParent(transform, false);
            m_Half = BuildingModelRules.HalfFootprint(m_Building.Need);
            m_RowZ = -m_Half.y + RowInset;

            switch (m_Building.Need)
            {
                case Need.Rooms: BuildHotel(); break;
                case Need.Drinks: BuildBar(); break;
                case Need.Food: BuildRestaurant(); break;
                case Need.Relax: BuildPool(); break;
                default: BuildBungalows(); break;
            }

            for (var i = 0; i < m_Seats.Length; i++)
                m_Seats[i] = BuildSeat(i);

            // The cube stays for the scene, the marker, the framing and taps, but the model is what is drawn.
            var cube = GetComponent<MeshRenderer>();
            if (cube != null) cube.forceRenderingOff = true;
        }

        // Reception: a hotel, one floor per level, a flag on top at Lv3.
        void BuildHotel()
        {
            const float z = 0.55f, depth = 1f, width = 1.9f, floor = 0.5f;
            for (var f = 1; f <= 3; f++)
            {
                var y = Ground + floor * (f - 1);
                Box(k_Wall, new Vector3(0f, y + floor * 0.5f, z), new Vector3(width, floor, depth), f);
                Box(k_Terracotta, new Vector3(0f, y + floor + 0.05f, z), new Vector3(width + 0.15f, 0.1f, depth + 0.15f), f, f);
                for (var w = -1; w <= 1; w++)
                {
                    if (f == 1 && w == 0)
                        Box(k_Door, new Vector3(0f, Ground + 0.16f, z - depth * 0.5f - 0.01f), new Vector3(0.26f, 0.32f, 0.03f), f);
                    else
                        Box(k_Window, new Vector3(w * 0.6f, y + 0.28f, z - depth * 0.5f - 0.01f), new Vector3(0.28f, 0.2f, 0.03f), f);
                }
                for (var w = 0; w < 2; w++)
                    Box(k_Window, new Vector3(width * 0.5f + 0.01f, y + 0.28f, z - 0.25f + w * 0.5f), new Vector3(0.03f, 0.2f, 0.26f), f);
            }
            var roof = Ground + floor * 3f + 0.1f;
            Cylinder(k_Pole, new Vector3(0.7f, roof, 0.85f), 0.04f, 0.6f, 3);
            Box(k_Flag, new Vector3(0.86f, roof + 0.5f, 0.85f), new Vector3(0.3f, 0.18f, 0.02f), 3);

            m_SeatTop = 0.17f;
        }

        // Beach Bar: a counter under a thatched hut; a sign at Lv2, a second roof at Lv3.
        void BuildBar()
        {
            Box(k_Wood, new Vector3(0f, Ground + 0.18f, -0.52f), new Vector3(2.15f, 0.36f, 0.28f));
            Box(k_LightWood, new Vector3(0f, Ground + 0.385f, -0.52f), new Vector3(2.25f, 0.05f, 0.36f));
            foreach (var x in new[] { -1f, 1f })
            foreach (var zPost in new[] { -0.45f, 0.95f })
                Cylinder(k_Wood, new Vector3(x, Ground, zPost), 0.08f, 1f);
            Box(k_Wood, new Vector3(0f, Ground + 0.3f, 0.9f), new Vector3(1.7f, 0.6f, 0.16f));
            for (var b = 0; b < 5; b++)
                Cylinder(k_Guests[b % k_Guests.Length], new Vector3(-0.6f + b * 0.3f, Ground + 0.6f, 0.86f), 0.08f, 0.16f);
            Pyramid(k_Thatch, new Vector3(0f, Ground + 1f, 0.25f), new Vector3(2.4f, 0.55f, 1.85f));

            Box(k_Teal, new Vector3(0f, Ground + 1.08f, -0.7f), new Vector3(1.1f, 0.24f, 0.05f), 2);
            // No flag on the bar: it would push the marker over the Pool site behind it.
            Pyramid(k_Thatch, new Vector3(0f, Ground + 1.45f, 0.25f), new Vector3(1.1f, 0.4f, 0.85f), 3);
            Box(k_Teal, new Vector3(0f, Ground + 1.42f, 0.25f), new Vector3(1.05f, 0.06f, 0.8f), 3);

            m_SeatTop = 0.33f;
        }

        // Restaurant: a house with an awning; an upper floor at Lv2, a sign and a chimney at Lv3.
        void BuildRestaurant()
        {
            const float z = 0.5f, depth = 1.15f, width = 1.9f;
            Box(k_White, new Vector3(0f, Ground + 0.35f, z), new Vector3(width, 0.7f, depth));
            Box(k_Terracotta, new Vector3(0f, Ground + 0.75f, z), new Vector3(width + 0.1f, 0.1f, depth + 0.1f));
            Box(k_Red, new Vector3(0f, Ground + 0.6f, -0.24f), new Vector3(width + 0.1f, 0.05f, 0.42f), 1, AnyLevel,
                new Vector3(25f, 0f, 0f));
            Box(k_Door, new Vector3(0f, Ground + 0.17f, z - depth * 0.5f - 0.01f), new Vector3(0.28f, 0.34f, 0.03f));
            foreach (var x in new[] { -0.6f, 0.6f })
                Box(k_Window, new Vector3(x, Ground + 0.32f, z - depth * 0.5f - 0.01f), new Vector3(0.36f, 0.24f, 0.03f));

            Box(k_White, new Vector3(0f, Ground + 1.03f, z + 0.1f), new Vector3(1.4f, 0.46f, 0.9f), 2);
            Box(k_Terracotta, new Vector3(0f, Ground + 1.3f, z + 0.1f), new Vector3(1.5f, 0.08f, 1f), 2);
            foreach (var x in new[] { -0.35f, 0.35f })
                Box(k_Window, new Vector3(x, Ground + 1.03f, z - 0.36f), new Vector3(0.28f, 0.2f, 0.03f), 2);

            Box(k_Yellow, new Vector3(0f, Ground + 0.92f, -0.08f), new Vector3(1f, 0.22f, 0.05f), 3);
            Box(k_Terracotta, new Vector3(0.45f, Ground + 1.52f, z + 0.3f), new Vector3(0.18f, 0.36f, 0.18f), 3);
            Cylinder(k_Pole, new Vector3(-0.55f, Ground + 1.34f, z + 0.35f), 0.04f, 0.55f, 3);
            Box(k_Flag, new Vector3(-0.39f, Ground + 1.79f, z + 0.35f), new Vector3(0.3f, 0.18f, 0.02f), 3);

            m_SeatTop = 0.14f;
        }

        // Pool: a basin; parasols at Lv2, a slide at Lv3.
        void BuildPool()
        {
            const float z = 0.42f;
            Box(k_White, new Vector3(0f, Ground + 0.07f, z), new Vector3(2.2f, 0.14f, 1.25f));
            Box(k_Water, new Vector3(0f, Ground + 0.08f, z), new Vector3(1.95f, 0.15f, 1.0f));

            foreach (var x in new[] { -1.02f, 1.02f })
            {
                Cylinder(k_Pole, new Vector3(x, Ground, 1.08f), 0.04f, 0.8f, 2);
                Pyramid(k_Red, new Vector3(x, Ground + 0.7f, 1.08f), new Vector3(0.6f, 0.2f, 0.6f), 2);
            }

            Box(k_Yellow, new Vector3(-0.85f, Ground + 0.4f, 0.95f), new Vector3(0.3f, 0.8f, 0.3f), 3);
            Box(k_Yellow, new Vector3(-0.85f, Ground + 0.45f, 0.48f), new Vector3(0.22f, 0.04f, 0.8f), 3,
                AnyLevel, new Vector3(-40f, 0f, 0f));
            Box(k_LightWood, new Vector3(0.7f, Ground + 0.2f, 0.85f), new Vector3(0.18f, 0.04f, 0.55f), 3);

            m_SeatTop = 0.13f;
        }

        // Bungalows: a deck by the sea with one hut per level.
        void BuildBungalows()
        {
            Box(k_LightWood, new Vector3(0f, Ground + 0.04f, 0f), new Vector3(m_Half.x * 2f, 0.08f, m_Half.y * 2f));
            Hut(0f, 1, 1);
            Hut(-0.55f, 2, 2);
            Hut(0.55f, 2, 2);
            Hut(-0.9f, 3);
            Hut(0f, 3);
            Hut(0.9f, 3);
            m_SeatTop = 0.2f;
        }

        void Hut(float x, int minLevel, int maxLevel = AnyLevel)
        {
            const float z = 0.45f;
            Box(k_Wall, new Vector3(x, Ground + 0.08f + 0.21f, z), new Vector3(0.6f, 0.42f, 0.55f), minLevel, maxLevel);
            Pyramid(k_Thatch, new Vector3(x, Ground + 0.5f, z), new Vector3(0.76f, 0.34f, 0.7f), minLevel, maxLevel);
            Box(k_Door, new Vector3(x, Ground + 0.08f + 0.12f, z - 0.285f), new Vector3(0.16f, 0.24f, 0.03f), minLevel, maxLevel);
        }

        Seat BuildSeat(int index)
        {
            var root = new GameObject("Seat " + (index + 1)).transform;
            root.SetParent(m_Model, false);
            var renderers = new List<Renderer>();
            switch (m_Building.Need)
            {
                case Need.Rooms:
                    renderers.Add(Shape(PrimitiveMesh(PrimitiveType.Cube), root, k_Armchair, new Vector3(0f, Ground + 0.07f, 0f), new Vector3(0.24f, 0.14f, 0.22f)));
                    renderers.Add(Shape(PrimitiveMesh(PrimitiveType.Cube), root, k_Armchair, new Vector3(0f, Ground + 0.2f, 0.09f), new Vector3(0.24f, 0.24f, 0.06f)));
                    break;
                case Need.Drinks:
                    renderers.Add(Shape(PrimitiveMesh(PrimitiveType.Cylinder), root, k_Pole, new Vector3(0f, Ground + 0.15f, 0f), new Vector3(0.05f, 0.15f, 0.05f)));
                    renderers.Add(Shape(PrimitiveMesh(PrimitiveType.Cylinder), root, k_Stool, new Vector3(0f, Ground + 0.31f, 0f), new Vector3(0.22f, 0.03f, 0.22f)));
                    break;
                case Need.Food:
                    renderers.Add(Shape(PrimitiveMesh(PrimitiveType.Cylinder), root, k_White, new Vector3(0f, Ground + 0.24f, -0.06f), new Vector3(0.25f, 0.02f, 0.25f)));
                    renderers.Add(Shape(PrimitiveMesh(PrimitiveType.Cylinder), root, k_Pole, new Vector3(0f, Ground + 0.11f, -0.06f), new Vector3(0.05f, 0.11f, 0.05f)));
                    renderers.Add(Shape(PrimitiveMesh(PrimitiveType.Cube), root, k_Chair, new Vector3(0f, Ground + 0.06f, 0.16f), new Vector3(0.14f, 0.12f, 0.13f)));
                    break;
                case Need.Relax:
                    renderers.Add(Shape(PrimitiveMesh(PrimitiveType.Cube), root, k_Lounger, new Vector3(0f, Ground + 0.07f, -0.05f), new Vector3(0.2f, 0.06f, 0.32f)));
                    renderers.Add(Shape(PrimitiveMesh(PrimitiveType.Cube), root, k_Lounger, new Vector3(0f, Ground + 0.13f, 0.15f), new Vector3(0.2f, 0.05f, 0.18f),
                        new Vector3(-35f, 0f, 0f)));
                    break;
                default:
                    renderers.Add(Shape(PrimitiveMesh(PrimitiveType.Cube), root, k_DeckChair, new Vector3(0f, Ground + 0.13f, -0.04f), new Vector3(0.2f, 0.05f, 0.26f)));
                    renderers.Add(Shape(PrimitiveMesh(PrimitiveType.Cube), root, k_DeckChair, new Vector3(0f, Ground + 0.2f, 0.12f), new Vector3(0.2f, 0.05f, 0.16f),
                        new Vector3(-40f, 0f, 0f)));
                    break;
            }

            // The guest sits on the seat (behind the table at the Restaurant), facing the camera.
            var guestZ = m_Building.Need == Need.Food ? 0.16f : 0f;
            var color = k_Guests[index % k_Guests.Length];
            var guest = new[]
            {
                Shape(PrimitiveMesh(PrimitiveType.Capsule), root, color, new Vector3(0f, Ground + m_SeatTop + 0.11f, guestZ), new Vector3(0.15f, 0.12f, 0.15f)),
                Shape(PrimitiveMesh(PrimitiveType.Sphere), root, k_Wall, new Vector3(0f, Ground + m_SeatTop + 0.29f, guestZ), Vector3.one * 0.12f)
            };
            foreach (var part in guest) part.enabled = false;
            return new Seat { Root = root, Renderers = renderers.ToArray(), Guest = guest };
        }

        void Apply(bool force)
        {
            if (m_Building == null) return;
            var level = m_Building.Level;
            var capacity = m_Building.Queue != null ? m_Building.Queue.Capacity
                : WaveConfig.Current.Buildings.CapacityByLevel[Mathf.Clamp(level, 1, WaveConfig.Current.Buildings.CapacityByLevel.Count) - 1];
            var seats = BuildingModelRules.SeatCount(capacity);
            if (!force && level == m_Level && seats == m_SeatCount) return;

            if (capacity > BuildingModelRules.MaxSeats && !m_Warned)
            {
                m_Warned = true;
                Debug.LogWarning($"{name}: capacity {capacity}, the model shows only {BuildingModelRules.MaxSeats} seats.", this);
            }

            var top = 0f;
            foreach (var part in m_Parts)
            {
                var shown = BuildingModelRules.ShowsAt(level, part.MinLevel, part.MaxLevel);
                part.Renderer.enabled = shown;
                if (shown) top = Mathf.Max(top, part.Top);
            }

            for (var i = 0; i < m_Seats.Length; i++)
            {
                var seat = m_Seats[i];
                var shown = i < seats;
                foreach (var renderer in seat.Renderers) renderer.enabled = shown;
                foreach (var renderer in seat.Guest) renderer.enabled = false;
                if (shown)
                    seat.Root.localPosition = new Vector3(BuildingModelRules.SeatOffset(i, seats), 0f, m_RowZ);
            }

            if (seats != m_SeatCount)
                m_Occupied = new Unit[seats];
            else
                System.Array.Clear(m_Occupied, 0, m_Occupied.Length);
            m_Level = level;
            m_SeatCount = seats;
            Height = top - Ground;
            UpdateBounds(top);
        }

        void ShowGuests()
        {
            var queue = m_Building.Queue;
            if (queue == null) return;
            BuildingModelRules.AssignSeats(m_Occupied, queue.Seated);
            for (var i = 0; i < m_SeatCount; i++)
            {
                var occupied = m_Occupied[i] != null;
                var guest = m_Seats[i].Guest;
                if (guest[0].enabled == occupied) continue;
                foreach (var renderer in guest) renderer.enabled = occupied;
            }
        }

        // The cube's mesh becomes the model's bounding box (a bit wider, so the meter and the token stand clear of
        // the seats); its collider becomes the footprint, so a tap anywhere on the model hits the building.
        void UpdateBounds(float top)
        {
            var filter = GetComponent<MeshFilter>();
            if (filter != null)
            {
                if (m_Bounds == null)
                    m_Bounds = new Mesh { name = $"{name} bounds (graybox)" };
                var x = m_Half.x + BoundsMargin;
                var z = m_Half.y + BoundsMargin;
                m_Bounds.Clear();
                m_Bounds.SetVertices(new List<Vector3>
                {
                    new Vector3(-x, Ground, -z), new Vector3(x, Ground, -z), new Vector3(x, Ground, z), new Vector3(-x, Ground, z),
                    new Vector3(-x, top, -z), new Vector3(x, top, -z), new Vector3(x, top, z), new Vector3(-x, top, z)
                });
                m_Bounds.SetTriangles(new[]
                {
                    0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4,
                    1, 2, 6, 1, 6, 5, 2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7
                }, 0);
                m_Bounds.RecalculateBounds();
                filter.sharedMesh = m_Bounds;
            }

            var box = GetComponent<BoxCollider>();
            if (box != null)
            {
                box.center = new Vector3(0f, (Ground + top) * 0.5f, 0f);
                box.size = new Vector3(m_Half.x * 2f, top - Ground, m_Half.y * 2f);
            }
        }

        Renderer Box(Color color, Vector3 center, Vector3 size, int minLevel = 1, int maxLevel = AnyLevel, Vector3 euler = default) =>
            AddPart(Shape(PrimitiveMesh(PrimitiveType.Cube), m_Model, color, center, size, euler), minLevel, maxLevel, center.y + size.y * 0.5f);

        // Unity's cylinder is 2 units tall; the position is the bottom of the cylinder.
        Renderer Cylinder(Color color, Vector3 bottom, float diameter, float height, int minLevel = 1, int maxLevel = AnyLevel) =>
            AddPart(Shape(PrimitiveMesh(PrimitiveType.Cylinder), m_Model, color, bottom + Vector3.up * (height * 0.5f),
                new Vector3(diameter, height * 0.5f, diameter)), minLevel, maxLevel, bottom.y + height);

        // The pyramid mesh has its base at y = 0; the position is the middle of the base.
        Renderer Pyramid(Color color, Vector3 baseCenter, Vector3 size, int minLevel = 1, int maxLevel = AnyLevel) =>
            AddPart(Shape(PyramidMesh(), m_Model, color, baseCenter, size), minLevel, maxLevel, baseCenter.y + size.y);

        Renderer AddPart(Renderer renderer, int minLevel, int maxLevel, float top)
        {
            m_Parts.Add(new Part { Renderer = renderer, MinLevel = minLevel, MaxLevel = maxLevel, Top = top });
            return renderer;
        }

        static Renderer Shape(Mesh mesh, Transform parent, Color color, Vector3 position, Vector3 scale, Vector3 euler = default)
        {
            var part = new GameObject(mesh.name, typeof(MeshFilter), typeof(MeshRenderer));
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localRotation = Quaternion.Euler(euler);
            part.transform.localScale = scale;
            part.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = part.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = MaterialFor(color);
            return renderer;
        }

        static Mesh PrimitiveMesh(PrimitiveType type)
        {
            switch (type)
            {
                case PrimitiveType.Cylinder: return s_Cylinder != null ? s_Cylinder : s_Cylinder = Builtin(type);
                case PrimitiveType.Capsule: return s_Capsule != null ? s_Capsule : s_Capsule = Builtin(type);
                case PrimitiveType.Sphere: return s_Sphere != null ? s_Sphere : s_Sphere = Builtin(type);
                default: return s_Cube != null ? s_Cube : s_Cube = Builtin(type);
            }
        }

        // Primitive meshes and the default material come from a primitive that is thrown away (no collider left).
        static Mesh Builtin(PrimitiveType type)
        {
            var probe = GameObject.CreatePrimitive(type);
            probe.SetActive(false);
            var mesh = probe.GetComponent<MeshFilter>().sharedMesh;
            if (s_Template == null)
                s_Template = probe.GetComponent<Renderer>().sharedMaterial;
            Destroy(probe);
            return mesh;
        }

        static Mesh PyramidMesh()
        {
            if (s_Pyramid != null) return s_Pyramid;
            var apex = new Vector3(0f, 1f, 0f);
            var corners = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(-0.5f, 0f, 0.5f)
            };
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (var i = 0; i < 4; i++)
            {
                // Separate vertices per face, so every face has its own flat normal.
                vertices.Add(corners[i]);
                vertices.Add(apex);
                vertices.Add(corners[(i + 1) % 4]);
                triangles.AddRange(new[] { i * 3, i * 3 + 1, i * 3 + 2 });
            }
            var bottom = vertices.Count;
            vertices.AddRange(corners);
            triangles.AddRange(new[] { bottom, bottom + 2, bottom + 1, bottom, bottom + 3, bottom + 2 });

            s_Pyramid = new Mesh { name = "Pyramid (graybox)" };
            s_Pyramid.SetVertices(vertices);
            s_Pyramid.SetTriangles(triangles, 0);
            s_Pyramid.RecalculateNormals();
            s_Pyramid.RecalculateBounds();
            return s_Pyramid;
        }

        // One matte material per colour, shared by all models. WorkerMoveFeedback reads its colour as the base
        // colour and tints the renderers with a property block, so the material itself never changes.
        static Material MaterialFor(Color color)
        {
            if (s_Materials.TryGetValue(color, out var material) && material != null) return material;
            if (s_Template == null) PrimitiveMesh(PrimitiveType.Cube);
            material = new Material(s_Template) { name = $"Building {ColorUtility.ToHtmlStringRGB(color)} (graybox)", color = color };
            material.SetFloat("_Smoothness", 0.1f);
            material.SetFloat("_Glossiness", 0.1f);
            s_Materials[color] = material;
            return material;
        }
    }
}
