using System;
using Multikulti.Buildings;
using Multikulti.Config;
using Multikulti.Waves;
using UnityEngine;

namespace Multikulti.Economy
{
    /// <summary>
    /// Rewarded video at zero Energy (SCRUM-58): where the Energy boost Speed Up is blocked only by missing Energy,
    /// the game offers a voluntary video instead; watched to the end it credits energy_reward Energy (default 3,
    /// one Speed Up) and the player can then use the boost. Values come from rewarded_video in wave_config.json,
    /// the rules from RewardedVideoRules.
    ///
    /// The video never blocks the service: the wave keeps running while it plays and nothing waits for it (HR1).
    /// The video itself comes from Provider (IRewardedAdProvider); the prototype has the graybox
    /// PlaceholderRewardedAd, a real ads SDK only needs an adapter.
    ///
    /// Added to the WaveLoop object at runtime, so the scene does not change.
    /// </summary>
    public sealed class RewardedVideoService : MonoBehaviour
    {
        WaveLoop m_Loop;
        SpeedUpService m_SpeedUp;
        ResortBalances m_Balances;
        RewardedVideoRules m_Rules;
        bool m_Subscribed;
        string m_For;

        /// <summary>Raised when a video starts.</summary>
        public event Action Started;
        /// <summary>Raised after the reward was credited, with the Energy amount.</summary>
        public event Action<int> Rewarded;
        /// <summary>Raised when a video ended without a reward (skipped or failed).</summary>
        public event Action Skipped;

        /// <summary>Where the videos come from; the placeholder registers itself when nothing else did.</summary>
        public IRewardedAdProvider Provider { get; set; }

        public RewardedVideoRules Rules => m_Rules;
        public bool IsPlaying => m_Rules != null && m_Rules.IsPlaying;
        public int EnergyReward => m_Rules != null ? m_Rules.EnergyReward : 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var loop = FindAnyObjectByType<WaveLoop>();
            if (loop != null && loop.GetComponent<RewardedVideoService>() == null)
                loop.gameObject.AddComponent<RewardedVideoService>();
        }

        void Awake()
        {
            m_Loop = GetComponent<WaveLoop>();
            m_Balances = FindAnyObjectByType<ResortBalances>();
            var settings = WaveConfig.Current.RewardedVideo;
            m_Rules = new RewardedVideoRules(Mathf.Max(1, settings.EnergyReward), Mathf.Max(1, settings.MaxPerWave));
            if (m_Balances == null)
                Debug.LogWarning("Rewarded video: no ResortBalances in the scene, the reward cannot be credited.", this);
        }

        // Start: SpeedUpService and WaveLoop.Machine are created in their Awake.
        void Start()
        {
            m_SpeedUp = GetComponent<SpeedUpService>();
            if (m_Loop == null || m_Loop.Machine == null) return;
            m_Loop.Machine.StateChanged += OnWaveStateChanged;
            m_Subscribed = true;
        }

        void OnDestroy()
        {
            if (m_Subscribed && m_Loop != null && m_Loop.Machine != null)
                m_Loop.Machine.StateChanged -= OnWaveStateChanged;
        }

        void OnWaveStateChanged(WaveState previous, WaveState next)
        {
            if (next == WaveState.WaveIncoming)
                m_Rules.ResetWave();
        }

        /// <summary>The video is offered at the building: its Speed Up is blocked only by missing Energy.</summary>
        public bool CanWatch(Building building) =>
            building != null && m_SpeedUp != null && Provider != null && m_Balances != null &&
            m_Rules.CanOffer(m_SpeedUp.BlockOf(building), Provider.IsReady);

        /// <summary>Starts the video for a Speed Up at the building; false (and nothing changes) when not offered.</summary>
        public bool TryWatch(Building building)
        {
            if (!CanWatch(building))
            {
                Debug.Log($"Wave {Wave}: rewarded video for {Name(building)} not offered.");
                return false;
            }

            m_Rules.Begin();
            m_For = building.name;
            Debug.Log($"Wave {Wave}: rewarded video started for Speed Up at {m_For} ({m_Balances.Energy} Energy)");
            Started?.Invoke();
            Provider.Show(OnFinished);
            return true;
        }

        void OnFinished(bool watchedToEnd)
        {
            var reward = m_Rules.Finish(watchedToEnd);
            if (reward > 0 && m_Balances != null)
            {
                m_Balances.EnergyWallet.Add(reward, "Rewarded video");
                Debug.Log($"Wave {Wave}: rewarded video watched, +{reward} Energy ({m_Balances.Energy} Energy) for Speed Up at {m_For}");
                Rewarded?.Invoke(reward);
            }
            else
            {
                Debug.Log($"Wave {Wave}: rewarded video ended without a reward");
                Skipped?.Invoke();
            }
        }

        int Wave => m_Loop != null ? m_Loop.Wave : 0;

        static string Name(Building building) => building != null ? building.name : "unknown building";
    }
}
