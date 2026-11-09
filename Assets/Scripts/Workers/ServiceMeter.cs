using System;
using System.Collections.Generic;
using Multikulti.Buildings;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Multikulti.Workers
{
    /// <summary>
    /// Graybox service meter in front of a building (SCRUM-41): shows how fast the building serves.
    ///
    /// - One bar per seat fills with the service progress of the unit on that seat. With a Worker the bars
    ///   fill about three times faster (utilization 1.0 against 0.33), so the speed-up after a move is seen
    ///   as motion, not only as a number.
    /// - Speed pips under the bars: all lit at full speed, one lit without a Worker. They show the speed
    ///   also when no unit is seated (on Home, before the wave).
    ///
    /// A unit keeps its bar until it is served, so the bars do not jump when another unit finishes.
    /// The meter faces the camera; WorkerMoveFeedback places it and owns its material. Colours go through
    /// a MaterialPropertyBlock, so all meters share one material.
    /// </summary>
    public sealed class ServiceMeter
    {
        public const int PipCount = 3;

        const float Padding = 0.06f;
        const float PipHeight = 0.12f;
        const float Depth = 0.02f;

        static readonly int k_BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int k_ColorId = Shader.PropertyToID("_Color");
        static readonly Color k_PanelColor = new Color(0.07f, 0.08f, 0.1f);
        static readonly Color k_WellColor = new Color(0.22f, 0.24f, 0.28f);

        readonly Material m_Material;
        readonly MaterialPropertyBlock m_Block;
        readonly float m_Width;
        readonly float m_BarHeight;
        readonly Renderer[] m_Pips = new Renderer[PipCount];
        readonly List<Transform> m_Wells = new List<Transform>();
        readonly List<Transform> m_Fills = new List<Transform>();
        readonly List<Renderer> m_FillRenderers = new List<Renderer>();
        Unit[] m_Slots = Array.Empty<Unit>();

        public Transform Root { get; }

        /// <summary>Lit speed pips after the last Show.</summary>
        public int LitPips { get; private set; }

        /// <summary>Bars (seats) of the meter: one per seat of the building.</summary>
        public int SeatCount => m_Slots.Length;

        /// <summary>Colour of the lit pips and of the bars after the last Show.</summary>
        public Color Color { get; private set; }

        float Height => Padding * 3f + PipHeight + m_BarHeight;

        float BarBottom => Padding * 2f + PipHeight;

        /// <param name="material">Shared material of all parts (matte, see WorkerMoveFeedback); colours come from the block.</param>
        public ServiceMeter(string name, Transform parent, Material material, MaterialPropertyBlock block, float width, float barHeight)
        {
            m_Material = material;
            m_Block = block;
            m_Width = width;
            m_BarHeight = barHeight;
            Root = new GameObject(name).transform;
            Root.SetParent(parent, false);

            var panel = CreatePart("Panel", k_PanelColor);
            panel.localPosition = new Vector3(0f, Height * 0.5f, 0f);
            panel.localScale = new Vector3(m_Width, Height, Depth);

            var pipWidth = (m_Width - Padding * (PipCount + 1)) / PipCount;
            for (var p = 0; p < PipCount; p++)
            {
                var pip = CreatePart($"Speed Pip {p}", k_WellColor);
                pip.localPosition = new Vector3(SlotX(p, pipWidth), Padding + PipHeight * 0.5f, -Depth);
                pip.localScale = new Vector3(pipWidth, PipHeight, Depth);
                m_Pips[p] = pip.GetComponent<Renderer>();
            }
        }

        /// <summary>Service progress (0–1) shown on a bar; 0 for a free seat.</summary>
        public float Fill(int seat) => m_Slots[seat] != null ? Mathf.Clamp01(m_Slots[seat].ServiceProgress) : 0f;

        /// <summary>Shows the seats of the queue and the speed of the building.</summary>
        public void Show(BuildingQueue queue, int litPips, Color color)
        {
            if (queue.Capacity != m_Slots.Length)
                BuildSeats(queue.Capacity);
            AssignSlots(queue.Seated);

            Color = color;
            LitPips = Mathf.Clamp(litPips, 0, PipCount);
            for (var p = 0; p < PipCount; p++)
                SetColor(m_Pips[p], p < LitPips ? color : k_WellColor);

            for (var seat = 0; seat < m_Slots.Length; seat++)
            {
                var height = m_BarHeight * Fill(seat);
                var renderer = m_FillRenderers[seat];
                renderer.enabled = height > 0.001f;
                if (!renderer.enabled) continue;
                var fill = m_Fills[seat];
                var position = fill.localPosition;
                fill.localPosition = new Vector3(position.x, BarBottom + height * 0.5f, position.z);
                fill.localScale = new Vector3(fill.localScale.x, height, Depth);
                SetColor(renderer, color);
            }
        }

        // A seated unit keeps its bar; a new unit takes the first free bar.
        void AssignSlots(IReadOnlyList<Unit> seated)
        {
            for (var s = 0; s < m_Slots.Length; s++)
                if (m_Slots[s] != null && !Contains(seated, m_Slots[s]))
                    m_Slots[s] = null;

            for (var u = 0; u < seated.Count; u++)
            {
                var unit = seated[u];
                if (Array.IndexOf(m_Slots, unit) >= 0) continue;
                var free = Array.IndexOf(m_Slots, null);
                if (free >= 0)
                    m_Slots[free] = unit;
            }
        }

        static bool Contains(IReadOnlyList<Unit> units, Unit unit)
        {
            for (var i = 0; i < units.Count; i++)
                if (units[i] == unit)
                    return true;
            return false;
        }

        // Capacity only grows (BuildingQueue.SetCapacity); the bars are laid out again for the new count.
        void BuildSeats(int capacity)
        {
            foreach (var well in m_Wells)
                Object.Destroy(well.gameObject);
            foreach (var fill in m_Fills)
                Object.Destroy(fill.gameObject);
            m_Wells.Clear();
            m_Fills.Clear();
            m_FillRenderers.Clear();

            var slots = new Unit[capacity];
            Array.Copy(m_Slots, slots, Mathf.Min(m_Slots.Length, capacity));
            m_Slots = slots;

            var barWidth = (m_Width - Padding * (capacity + 1)) / capacity;
            for (var seat = 0; seat < capacity; seat++)
            {
                var x = SlotX(seat, barWidth);
                var well = CreatePart($"Seat {seat}", k_WellColor);
                well.localPosition = new Vector3(x, BarBottom + m_BarHeight * 0.5f, -Depth);
                well.localScale = new Vector3(barWidth, m_BarHeight, Depth);
                m_Wells.Add(well);

                // In front of the well, so the fill is not hidden by it.
                var fill = CreatePart($"Seat {seat} Progress", k_WellColor);
                fill.localPosition = new Vector3(x, BarBottom, -Depth * 2f);
                fill.localScale = new Vector3(barWidth, 0f, Depth);
                m_Fills.Add(fill);
                m_FillRenderers.Add(fill.GetComponent<Renderer>());
            }
        }

        float SlotX(int index, float slotWidth) =>
            -m_Width * 0.5f + Padding + slotWidth * 0.5f + index * (slotWidth + Padding);

        Transform CreatePart(string name, Color color)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            part.transform.SetParent(Root, false);
            // Taps are meant for the buildings (WorkerTapInput raycasts).
            Object.Destroy(part.GetComponent<Collider>());
            var renderer = part.GetComponent<Renderer>();
            if (m_Material != null)
                renderer.sharedMaterial = m_Material;
            SetColor(renderer, color);
            return part.transform;
        }

        void SetColor(Renderer renderer, Color color)
        {
            renderer.GetPropertyBlock(m_Block);
            m_Block.SetColor(k_BaseColorId, color);
            m_Block.SetColor(k_ColorId, color);
            renderer.SetPropertyBlock(m_Block);
        }
    }
}
