using System;
using Multikulti.Buildings;
using Multikulti.Config;
using Multikulti.Island;
using Multikulti.Waves;
using UnityEngine;

namespace Multikulti.Unlocks
{
    public sealed class AreaUnlockService : MonoBehaviour
    {
        [SerializeField] WaveLoop waveLoop;
        [SerializeField] IslandMap map;

        AreaUnlock? m_Open;

        public AreaUnlocks Unlocks { get; private set; }

        public event Action<AreaUnlock> Requested;
        public event Action Closed;
        public event Action Changed;

        public WaveState WaveState => waveLoop != null && waveLoop.Machine != null ? waveLoop.State : WaveState.AwaitingWave;

        public int CompletedWaves => waveLoop != null && waveLoop.Machine != null
            ? AreaUnlocks.CompletedWaves(waveLoop.Wave, waveLoop.State)
            : 0;

        public IslandMap Map => map;

        public bool IsOpen => m_Open.HasValue;

        public AreaUnlock? Open => m_Open;

        void Awake()
        {
            if (waveLoop == null)
                waveLoop = FindAnyObjectByType<WaveLoop>();
            if (map == null)
                map = FindAnyObjectByType<IslandMap>();

            var config = WaveConfig.Current.Unlocks;
            Unlocks = new AreaUnlocks(new[]
            {
                new AreaUnlock("Pool", Need.Relax, config.PoolAfterWave, config.PoolPrice),
                new AreaUnlock("Bungalows", Need.OceanView, config.BungalowsAfterWave, config.BungalowsPrice)
            });
        }

        void Start()
        {
            if (waveLoop != null)
                waveLoop.Machine.StateChanged += OnStateChanged;
            if (map != null)
                map.Refreshed += SyncBought;
            SyncBought();
        }

        void OnDestroy()
        {
            if (waveLoop != null && waveLoop.Machine != null)
                waveLoop.Machine.StateChanged -= OnStateChanged;
            if (map != null)
                map.Refreshed -= SyncBought;
        }

        public AreaUnlockState StateOf(Need need) => Unlocks.StateOf(need, CompletedWaves);

        public bool TryOpen(Need need)
        {
            if (!Unlocks.CanOpen(need, CompletedWaves, WaveState))
                return false;

            var unlock = Unlocks.Get(need);
            m_Open = unlock;
            Debug.Log($"Area Unlock {unlock.Area} otvorený: {unlock.Price} Coins, dostupný od vlny {unlock.AfterWave}");
            Requested?.Invoke(unlock);
            return true;
        }

        public void Close()
        {
            if (!m_Open.HasValue) return;
            m_Open = null;
            Closed?.Invoke();
        }

        void OnStateChanged(WaveState from, WaveState to)
        {
            if (to != WaveState.AwaitingWave)
                Close();
            Changed?.Invoke();
        }

        void SyncBought()
        {
            var changed = false;
            foreach (var unlock in Unlocks.All)
                changed |= Unlocks.SetBought(unlock.Need, HasBuilding(unlock.Need));
            if (m_Open.HasValue && Unlocks.IsBought(m_Open.Value.Need))
                Close();
            if (changed)
                Changed?.Invoke();
        }

        bool HasBuilding(Need need)
        {
            if (map == null) return false;
            foreach (var building in map.Buildings)
                if (building != null && building.Need == need)
                    return true;
            return false;
        }
    }
}
