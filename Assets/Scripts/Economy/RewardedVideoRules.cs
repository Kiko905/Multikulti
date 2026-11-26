using System;

namespace Multikulti.Economy
{
    /// <summary>
    /// Rules of the rewarded video at zero Energy (SCRUM-58, build document §4.6 "Pri energy = 0: tlačidlo ponúkne
    /// Watch ad", §5.7 "Rewarded video: … Energy top-up (dobrovoľné, výhodné)"), plain C# so they can be tested
    /// without a scene.
    ///
    /// - The video is offered only instead of a Speed Up the player cannot afford (SpeedUpBlock.NotEnoughEnergy):
    ///   never as a gate, the Speed Up itself stays optional and the service never waits for it (HR1).
    /// - A video watched to the end gives energy_reward Energy (one Speed Up); a skipped or failed one gives nothing
    ///   and does not count, so the player can try again.
    /// - At most max_per_wave rewarded videos per wave, so Energy cannot be farmed; a new wave resets the count.
    /// - One video at a time.
    /// </summary>
    public sealed class RewardedVideoRules
    {
        public int EnergyReward { get; }
        public int MaxPerWave { get; }

        /// <summary>Videos watched to the end in this wave.</summary>
        public int WatchedThisWave { get; private set; }

        /// <summary>A video is playing now.</summary>
        public bool IsPlaying { get; private set; }

        public RewardedVideoRules(int energyReward, int maxPerWave)
        {
            if (energyReward < 1) throw new ArgumentOutOfRangeException(nameof(energyReward));
            if (maxPerWave < 1) throw new ArgumentOutOfRangeException(nameof(maxPerWave));
            EnergyReward = energyReward;
            MaxPerWave = maxPerWave;
        }

        /// <summary>The video is offered instead of a Speed Up blocked only by missing Energy.</summary>
        public bool CanOffer(SpeedUpBlock speedUpBlock, bool adReady) =>
            speedUpBlock == SpeedUpBlock.NotEnoughEnergy && adReady && !IsPlaying && WatchedThisWave < MaxPerWave;

        /// <summary>A video starts; the caller checks CanOffer first.</summary>
        public void Begin()
        {
            if (IsPlaying) throw new InvalidOperationException("A rewarded video is already playing.");
            if (WatchedThisWave >= MaxPerWave)
                throw new InvalidOperationException($"Already {WatchedThisWave} rewarded video(s) in this wave.");
            IsPlaying = true;
        }

        /// <summary>
        /// The video ended. Returns the Energy to credit: energy_reward when watched to the end, otherwise 0.
        /// A second report of the same video is ignored (returns 0).
        /// </summary>
        public int Finish(bool watchedToEnd)
        {
            if (!IsPlaying) return 0;
            IsPlaying = false;
            if (!watchedToEnd) return 0;
            WatchedThisWave++;
            return EnergyReward;
        }

        /// <summary>A new wave starts: the count starts again (a playing video finishes normally).</summary>
        public void ResetWave() => WatchedThisWave = 0;
    }
}
