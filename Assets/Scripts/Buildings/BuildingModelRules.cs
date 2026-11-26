using System;
using System.Collections.Generic;
using UnityEngine;

namespace Multikulti.Buildings
{
    /// <summary>
    /// Layout rules of the graybox building models (SCRUM-101), plain C# so they can be tested without a scene.
    ///
    /// - Every building shows its seats as a row of objects in front of it, on the side that faces the camera:
    ///   one object per seat (capacity 3 → 5 → 7, build document §4.8), centred, so the row grows on both sides
    ///   with every upgrade and the count can be read from the building itself (§4.5, AC2d).
    /// - The structure stands behind the row and grows with the level (one part more per level).
    /// - A building keeps the same footprint on every level, so its neighbours, its service meter and its Worker
    ///   token never have to move.
    /// </summary>
    public static class BuildingModelRules
    {
        /// <summary>Most seats a model shows; capacity_by_level ends at 7.</summary>
        public const int MaxSeats = 7;

        /// <summary>Distance between the centres of two seats in a row.</summary>
        public const float SeatSpacing = 0.34f;

        /// <summary>Seats shown for a capacity.</summary>
        public static int SeatCount(int capacity) => Mathf.Clamp(capacity, 0, MaxSeats);

        /// <summary>Offset of a seat from the middle of its row: the row is centred whatever its length.</summary>
        public static float SeatOffset(int index, int count, float spacing = SeatSpacing)
        {
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (index < 0 || index >= count) throw new ArgumentOutOfRangeException(nameof(index));
            return (index - (count - 1) * 0.5f) * spacing;
        }

        /// <summary>
        /// Half size of the building's ground footprint (x, z). Pool and Bungalows fit their construction sites
        /// (2.6 × 2.6 and 3 × 2.2), the others the 5-unit spacing of the first row.
        /// </summary>
        public static Vector2 HalfFootprint(Need need) => need == Need.OceanView ? new Vector2(1.4f, 1.0f) : new Vector2(1.2f, 1.2f);

        /// <summary>A part made for levels minLevel..maxLevel is shown on this level.</summary>
        public static bool ShowsAt(int level, int minLevel, int maxLevel) => level >= minLevel && level <= maxLevel;

        /// <summary>
        /// Keeps every seated unit on its seat until it is served, so guests do not jump to another seat when
        /// someone else leaves: a unit no longer seated frees its seat, a newly seated unit takes the first free one.
        /// </summary>
        public static void AssignSeats<T>(T[] seats, IReadOnlyList<T> seated) where T : class
        {
            for (var s = 0; s < seats.Length; s++)
            {
                if (seats[s] != null && !Contains(seated, seats[s]))
                    seats[s] = null;
            }

            foreach (var unit in seated)
            {
                if (Array.IndexOf(seats, unit) >= 0) continue;
                var free = Array.IndexOf(seats, null);
                if (free < 0) return;
                seats[free] = unit;
            }
        }

        static bool Contains<T>(IReadOnlyList<T> list, T item) where T : class
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], item)) return true;
            }
            return false;
        }
    }
}
