using System;
using Multikulti.Economy;
using Multikulti.Waves;
using UnityEngine;

namespace Multikulti.UI
{
    /// <summary>
    /// Graybox stand-in for a rewarded video (SCRUM-58) until a real ads SDK: a card "Rewarded video" counts down
    /// a few seconds and then reports the video as watched; Close ends it without a reward.
    ///
    /// The card is small and the game keeps running behind it (the service is never blocked, HR1). Unscaled time,
    /// so the length does not depend on the game speed. Registers itself as RewardedVideoService.Provider when no
    /// other provider did. Added to the WaveLoop object at runtime, so the scene does not change.
    /// </summary>
    public sealed class PlaceholderRewardedAd : MonoBehaviour, IRewardedAdProvider
    {
        const float ReferenceHeight = 720f;
        const float Width = 320f;

        [SerializeField, Min(0.1f)] float seconds = 5f;

        Action<bool> m_OnFinished;
        float m_Remaining;

        public bool IsReady => m_OnFinished == null;
        public bool IsShowing => m_OnFinished != null;
        public float Remaining => IsShowing ? m_Remaining : 0f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var loop = FindAnyObjectByType<WaveLoop>();
            if (loop != null && loop.GetComponent<PlaceholderRewardedAd>() == null)
                loop.gameObject.AddComponent<PlaceholderRewardedAd>();
        }

        void Start()
        {
            var service = GetComponent<RewardedVideoService>();
            if (service != null && service.Provider == null)
                service.Provider = this;
        }

        public void Show(Action<bool> onFinished)
        {
            if (IsShowing)
            {
                onFinished?.Invoke(false);
                return;
            }
            m_OnFinished = onFinished;
            m_Remaining = seconds;
        }

        /// <summary>Close: the video ends without a reward.</summary>
        public void Skip() => Finish(false);

        void Update()
        {
            if (!IsShowing) return;
            m_Remaining -= Time.unscaledDeltaTime;
            if (m_Remaining <= 0f)
                Finish(true);
        }

        void Finish(bool watchedToEnd)
        {
            var callback = m_OnFinished;
            m_OnFinished = null;
            callback?.Invoke(watchedToEnd);
        }

        void OnGUI()
        {
            if (!IsShowing) return;

            var scale = Screen.height / ReferenceHeight;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            var screenWidth = Screen.width / scale;
            var area = new Rect((screenWidth - Width) / 2f, ReferenceHeight - 150f, Width, 112f);
            GUI.Box(area, GUIContent.none);
            GUILayout.BeginArea(new Rect(area.x + 12f, area.y + 8f, area.width - 24f, area.height - 16f));
            GUILayout.Label("<b>Rewarded video</b>  (placeholder)", RichLabel(15));
            GUILayout.Label($"Your resort keeps running. Reward in {Mathf.CeilToInt(m_Remaining)} s.", RichLabel(12));
            if (GUILayout.Button("Close (no reward)", GUILayout.Height(28f)))
                Skip();
            GUILayout.EndArea();
            GUI.matrix = Matrix4x4.identity;
        }

        static GUIStyle RichLabel(int size) =>
            new GUIStyle(GUI.skin.label) { richText = true, fontSize = size, wordWrap = true };
    }
}
