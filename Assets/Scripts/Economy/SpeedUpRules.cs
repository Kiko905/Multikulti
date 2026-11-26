using System;
using System.Collections.Generic;
using Multikulti.Buildings;

namespace Multikulti.Economy
{
    /// <summary>Why the Energy boost Speed Up cannot be used on a building right now (SCRUM-57).</summary>
    public enum SpeedUpBlock
    {
        None,
        /// <summary>Speed Up works only while the wave runs (WaveActive).</summary>
        NotInWave,
        /// <summary>The building is not overloaded: its need is not BOTTLENECK and it has no Sudden rush.</summary>
        NotOverloaded,
        /// <summary>The boost already runs on this building.</summary>
        Active,
        /// <summary>The building already used max_per_building_per_wave boosts in this wave.</summary>
        UsedThisWave,
        /// <summary>The player has less Energy than energy_cost.</summary>
        NotEnoughEnergy
    }

    /// <summary>
    /// Rules of the Energy boost Speed Up (SCRUM-57), plain C# so they can be tested without a scene.
    /// A boost halves service_time of one building for duration_s: the building's BuildingQueue.SpeedMultiplier
    /// becomes 1 / service_time_multiplier, so it changes the speed of units already being served too (SCRUM-31).
    /// At most max_per_building_per_wave boosts per building in one wave.
    ///
    /// Time is the WaveLoop.WaveTime clock, so a pause (WavePaused) does not use up the boost.
    /// </summary>
    public sealed class SpeedUpRules
    {
        sealed class Entry
        {
            public int Uses;
            public float EndsAt = float.NegativeInfinity;
            public bool IsActive;
        }

        readonly Dictionary<BuildingQueue, Entry> m_Entries = new Dictionary<BuildingQueue, Entry>();
        readonly List<BuildingQueue> m_Ended = new List<BuildingQueue>();

        public int EnergyCost { get; }
        public float DurationSeconds { get; }
        public float ServiceTimeMultiplier { get; }
        public int MaxPerBuildingPerWave { get; }

        /// <summary>Factor for BuildingQueue.SpeedMultiplier while the boost runs (0.5 service time = 2× speed).</summary>
        public float SpeedFactor => 1f / ServiceTimeMultiplier;

        /// <summary>Raised when a boost ends, after the queue's speed is back to normal.</summary>
        public event Action<BuildingQueue> Ended;

        public SpeedUpRules(int energyCost, float durationSeconds, float serviceTimeMultiplier, int maxPerBuildingPerWave)
        {
            if (energyCost < 0) throw new ArgumentOutOfRangeException(nameof(energyCost));
            if (durationSeconds <= 0f) throw new ArgumentOutOfRangeException(nameof(durationSeconds));
            if (serviceTimeMultiplier <= 0f || serviceTimeMultiplier >= 1f)
                throw new ArgumentOutOfRangeException(nameof(serviceTimeMultiplier), "Must be between 0 and 1.");
            if (maxPerBuildingPerWave < 1) throw new ArgumentOutOfRangeException(nameof(maxPerBuildingPerWave));
            EnergyCost = energyCost;
            DurationSeconds = durationSeconds;
            ServiceTimeMultiplier = serviceTimeMultiplier;
            MaxPerBuildingPerWave = maxPerBuildingPerWave;
        }

        public bool IsActive(BuildingQueue queue) => m_Entries.TryGetValue(queue, out var e) && e.IsActive;

        public int UsesThisWave(BuildingQueue queue) => m_Entries.TryGetValue(queue, out var e) ? e.Uses : 0;

        /// <summary>Seconds left of a running boost; 0 when none runs.</summary>
        public float RemainingSeconds(BuildingQueue queue, float now) =>
            m_Entries.TryGetValue(queue, out var e) && e.IsActive ? Math.Max(0f, e.EndsAt - now) : 0f;

        /// <summary>Energy still missing for one boost; 0 when the player can afford it.</summary>
        public int MissingEnergy(int energy) => Math.Max(0, EnergyCost - energy);

        /// <summary>
        /// The first reason that blocks a boost, in the order the player should see it:
        /// wave, overload, a boost already running, the per-wave limit, and only then Energy.
        /// </summary>
        public SpeedUpBlock BlockOf(BuildingQueue queue, bool waveActive, bool overloaded, int energy)
        {
            if (queue == null) throw new ArgumentNullException(nameof(queue));
            if (!waveActive) return SpeedUpBlock.NotInWave;
            if (!overloaded) return SpeedUpBlock.NotOverloaded;
            if (IsActive(queue)) return SpeedUpBlock.Active;
            if (UsesThisWave(queue) >= MaxPerBuildingPerWave) return SpeedUpBlock.UsedThisWave;
            if (MissingEnergy(energy) > 0) return SpeedUpBlock.NotEnoughEnergy;
            return SpeedUpBlock.None;
        }

        /// <summary>
        /// The building is worth offering: overloaded and with a boost left in this wave.
        /// The offer shows even without enough Energy, with the missing amount (like SCRUM-55 for Coins).
        /// </summary>
        public bool IsOffered(BuildingQueue queue, bool waveActive, bool overloaded, int energy)
        {
            var block = BlockOf(queue, waveActive, overloaded, energy);
            return block == SpeedUpBlock.None || block == SpeedUpBlock.NotEnoughEnergy;
        }

        /// <summary>Starts the boost. The caller checks BlockOf and spends the Energy first.</summary>
        public void Start(BuildingQueue queue, float now)
        {
            if (queue == null) throw new ArgumentNullException(nameof(queue));
            if (IsActive(queue)) throw new InvalidOperationException($"Speed Up already runs on {queue.Need}.");
            if (UsesThisWave(queue) >= MaxPerBuildingPerWave)
                throw new InvalidOperationException($"{queue.Need} already used Speed Up in this wave.");

            if (!m_Entries.TryGetValue(queue, out var entry))
            {
                entry = new Entry();
                m_Entries.Add(queue, entry);
            }
            entry.Uses++;
            entry.IsActive = true;
            entry.EndsAt = now + DurationSeconds;
            queue.SpeedMultiplier = SpeedFactor;
        }

        /// <summary>Ends every boost whose time is over.</summary>
        public void Tick(float now)
        {
            foreach (var pair in m_Entries)
            {
                if (pair.Value.IsActive && now >= pair.Value.EndsAt)
                    m_Ended.Add(pair.Key);
            }
            foreach (var queue in m_Ended)
                End(queue);
            m_Ended.Clear();
        }

        /// <summary>Ends running boosts and forgets the uses; call it when a new wave starts.</summary>
        public void ResetWave()
        {
            EndAll();
            m_Entries.Clear();
        }

        /// <summary>Ends running boosts but keeps the uses of this wave, e.g. when the wave stops running.</summary>
        public void EndAll()
        {
            foreach (var pair in m_Entries)
            {
                if (pair.Value.IsActive) m_Ended.Add(pair.Key);
            }
            foreach (var queue in m_Ended)
                End(queue);
            m_Ended.Clear();
        }

        void End(BuildingQueue queue)
        {
            var entry = m_Entries[queue];
            entry.IsActive = false;
            queue.SpeedMultiplier = 1f;
            Ended?.Invoke(queue);
        }
    }
}
