using System;
using System.Collections.Generic;
using Multikulti.Buildings;
using Multikulti.Config;

namespace Multikulti.UI
{
    /// <summary>One row of the Wave Needs Panel (SCRUM-29): one active need of the running wave.</summary>
    public readonly struct NeedProgress : IEquatable<NeedProgress>
    {
        public NeedProgress(Need need, int served, int total, bool hasWorker, bool hasBuilding)
        {
            Need = need;
            Served = served;
            Total = total;
            HasWorker = hasWorker;
            HasBuilding = hasBuilding;
        }

        public Need Need { get; }

        /// <summary>Units of this need served since Start Wave.</summary>
        public int Served { get; }

        /// <summary>
        /// Units of this need in the wave: demand_total from wave_config.json,
        /// or more if more units arrived (Sudden rush, debug keys).
        /// </summary>
        public int Total { get; }

        /// <summary>A Worker stands on a building that serves this need.</summary>
        public bool HasWorker { get; }

        /// <summary>The scene has a building for this need.</summary>
        public bool HasBuilding { get; }

        public bool Equals(NeedProgress other) =>
            Need == other.Need && Served == other.Served && Total == other.Total &&
            HasWorker == other.HasWorker && HasBuilding == other.HasBuilding;

        public override bool Equals(object obj) => obj is NeedProgress other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Need, Served, Total, HasWorker, HasBuilding);
    }

    /// <summary>
    /// Served and total units of every need in the current wave (SCRUM-29).
    /// BuildingQueue counts units for the whole game, so StartWave remembers the counts at Start Wave
    /// and Collect reports only what happened since then.
    /// </summary>
    public sealed class WaveNeedsProgress
    {
        /// <summary>
        /// All needs in panel order. The needs that join in later waves (Relax, Ocean View) come last,
        /// so the rows above them keep their place when they appear (SCRUM-79).
        /// </summary>
        public static readonly IReadOnlyList<Need> Needs = (Need[])Enum.GetValues(typeof(Need));

        readonly Dictionary<Building, Counts> m_AtWaveStart = new Dictionary<Building, Counts>();

        /// <summary>Call on Start Wave (AwaitingWave → WaveIncoming), before any unit of the new wave arrives.</summary>
        public void StartWave(IReadOnlyList<Building> buildings)
        {
            m_AtWaveStart.Clear();
            foreach (var building in buildings)
            {
                if (building != null && building.Queue != null)
                    m_AtWaveStart[building] = new Counts(building.Queue.ArrivedCount, building.Queue.ServedCount);
            }
        }

        /// <summary>
        /// Fills rows with the active needs of the wave, in the order of Needs.
        /// A need is active when the wave has demand for it or when units of it arrived anyway.
        /// A building added after Start Wave counts from zero.
        /// </summary>
        public void Collect(WaveDefinition wave, IReadOnlyList<Building> buildings, List<NeedProgress> rows)
        {
            rows.Clear();
            foreach (var need in Needs)
            {
                var demand = wave != null ? Demand(wave, need) : 0;
                var arrived = 0;
                var served = 0;
                var hasWorker = false;
                var hasBuilding = false;

                foreach (var building in buildings)
                {
                    if (building == null || building.Need != need || building.Queue == null) continue;
                    m_AtWaveStart.TryGetValue(building, out var atStart);
                    arrived += building.Queue.ArrivedCount - atStart.Arrived;
                    served += building.Queue.ServedCount - atStart.Served;
                    hasWorker |= building.HasWorker;
                    hasBuilding = true;
                }

                if (demand == 0 && arrived == 0) continue;
                var total = Math.Max(demand, Math.Max(arrived, served));
                rows.Add(new NeedProgress(need, served, total, hasWorker, hasBuilding));
            }
        }

        /// <summary>demand_total of a need in a scripted wave; 0 means the need is not in that wave.</summary>
        public static int Demand(WaveDefinition wave, Need need) => need switch
        {
            Need.Rooms => wave.Rooms,
            Need.Drinks => wave.Drinks,
            Need.Food => wave.Food,
            Need.Relax => wave.Relax,
            Need.OceanView => wave.OceanView,
            _ => 0
        };

        readonly struct Counts
        {
            public Counts(int arrived, int served)
            {
                Arrived = arrived;
                Served = served;
            }

            public int Arrived { get; }
            public int Served { get; }
        }
    }
}
