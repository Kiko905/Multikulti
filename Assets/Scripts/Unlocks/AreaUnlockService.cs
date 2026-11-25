using System;
using Multikulti.Buildings;
using Multikulti.Config;
using Multikulti.Economy;
using Multikulti.Island;
using Multikulti.Waves;
using Multikulti.Workers;
using UnityEngine;

namespace Multikulti.Unlocks
{
    public sealed class AreaUnlockService : MonoBehaviour
    {
        [SerializeField] WaveLoop waveLoop;
        [SerializeField] IslandMap map;
        [SerializeField] ResortBalances balances;
        [SerializeField] WorkerManager workers;
        [SerializeField] Building buildingTemplate;

        AreaUnlock? m_Open;
        PurchaseRules m_Rules;

        public AreaUnlocks Unlocks { get; private set; }

        public event Action<AreaUnlock> Requested;
        public event Action Closed;
        public event Action Changed;
        public event Action<AreaUnlock> Bought;

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
            if (balances == null)
                balances = FindAnyObjectByType<ResortBalances>();
            if (workers == null)
                workers = FindAnyObjectByType<WorkerManager>();
            m_Rules = new PurchaseRules(() => balances.CoinWallet, () => WaveState);

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

        /// <summary>SCRUM-34: the need's building is not bought yet, so the need brings no demand.</summary>
        public bool IsNeedLocked(Need need) => Unlocks != null && Unlocks.IsNeedLocked(need);

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

        public PurchaseOffer OfferFor(Need need)
        {
            var unlock = Unlocks.Get(need);
            var block = StateOf(need) switch
            {
                AreaUnlockState.Bought => PurchaseBlock.AlreadyOwned,
                AreaUnlockState.Locked => PurchaseBlock.NotAvailableYet,
                _ => PurchaseBlock.None
            };
            return new PurchaseOffer($"area unlock {unlock.Area}", unlock.Price, block);
        }

        public PurchaseBlock BlockOf(Need need) => m_Rules.BlockOf(OfferFor(need));

        public bool CanBuy(Need need) => m_Rules.CanBuy(OfferFor(need));

        public int MissingCoins(Need need) => m_Rules.MissingCoins(OfferFor(need));

        public bool TryBuy(Need need)
        {
            var unlock = Unlocks.Get(need);
            if (!m_Rules.TryBuy(OfferFor(need), () => Unlocks.SetBought(need, true)))
                return false;

            Debug.Log($"Area Unlock {unlock.Area} kúpený za {unlock.Price} Coins ({need} sa odomkne od ďalšej vlny)");
            Close();
            Build(unlock);
            Bought?.Invoke(unlock);
            Changed?.Invoke();
            return true;
        }

        void Build(AreaUnlock unlock)
        {
            if (HasBuilding(unlock.Need)) return;

            var template = buildingTemplate != null ? buildingTemplate
                : workers != null && workers.Buildings.Count > 0 ? workers.Buildings[0]
                : null;
            if (template == null || workers == null || map == null || !map.TryGetSite(unlock.Need, out var site))
            {
                Debug.LogWarning($"Area Unlock {unlock.Area}: budovu sa nepodarilo postaviť (chýba vzor budovy, WorkerManager alebo stavenisko).", this);
                return;
            }

            var position = new Vector3(site.x, template.transform.position.y, site.z);
            var building = template.CloneFor(unlock.Need, position, unlock.Area);
            workers.AddBuilding(building);
            map.Refresh();
            Debug.Log($"Area Unlock {unlock.Area}: postavená budova {unlock.Need} a pridaný Worker (budov {workers.Roster.BuildingCount}, Workerov {workers.Roster.WorkerCount})");
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
            {
                if (HasBuilding(unlock.Need))
                    changed |= Unlocks.SetBought(unlock.Need, true);
            }
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
