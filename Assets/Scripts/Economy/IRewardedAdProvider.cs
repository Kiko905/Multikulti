using System;

namespace Multikulti.Economy
{
    /// <summary>
    /// Source of rewarded videos (SCRUM-58). The prototype uses the graybox PlaceholderRewardedAd; a real ads SDK
    /// gets an adapter with this interface and registers it in RewardedVideoService.Provider.
    /// </summary>
    public interface IRewardedAdProvider
    {
        /// <summary>A video can be shown now (loaded, nothing else playing).</summary>
        bool IsReady { get; }

        /// <summary>
        /// Shows a video and reports once: true when it was watched to the end (the reward is earned), false when it
        /// was skipped or failed. It must not pause the game: the wave and the service keep running (HR1).
        /// </summary>
        void Show(Action<bool> onFinished);
    }
}
