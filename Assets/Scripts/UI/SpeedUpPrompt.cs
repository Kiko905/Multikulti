using Multikulti.Buildings;
using Multikulti.Economy;
using Multikulti.Waves;
using UnityEngine;

namespace Multikulti.UI
{
    /// <summary>
    /// Offer of the Energy boost Speed Up (SCRUM-57): for every overloaded building a row
    /// "Beach Bar is overloaded" with a Speed Up button and its Energy price. Without enough Energy the button is
    /// disabled and the row shows how much Energy is missing. A running boost shows its remaining seconds.
    /// Without enough Energy the button offers a voluntary rewarded video instead, when one is available (SCRUM-58).
    ///
    /// Shown only in WaveActive. Added to the WaveLoop object at runtime, so the scene does not change.
    /// Graybox IMGUI until the S2 art.
    /// </summary>
    public sealed class SpeedUpPrompt : MonoBehaviour
    {
        const float ReferenceHeight = 720f;
        const float Width = 300f;
        const float RowHeight = 34f;

        WaveLoop m_Loop;
        SpeedUpService m_Service;
        RewardedVideoService m_Video;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var loop = FindAnyObjectByType<WaveLoop>();
            if (loop != null && loop.GetComponent<SpeedUpPrompt>() == null)
                loop.gameObject.AddComponent<SpeedUpPrompt>();
        }

        void Awake() => m_Loop = GetComponent<WaveLoop>();

        void Start()
        {
            m_Service = GetComponent<SpeedUpService>();
            m_Video = GetComponent<RewardedVideoService>();
        }

        void OnGUI()
        {
            if (m_Loop == null || m_Service == null || m_Loop.Machine == null || m_Loop.Workers == null ||
                m_Loop.State != WaveState.WaveActive)
                return;

            var offers = m_Service.Offers();
            var running = 0;
            foreach (var building in m_Loop.Workers.Buildings)
            {
                if (m_Service.IsActive(building)) running++;
            }
            if (offers.Count == 0 && running == 0) return;

            var scale = Screen.height / ReferenceHeight;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            var screenWidth = Screen.width / scale;
            var rows = offers.Count + running;
            var area = new Rect(screenWidth - Width - 12f, 200f, Width, 34f + rows * RowHeight);
            GUI.Box(area, GUIContent.none);
            GUILayout.BeginArea(new Rect(area.x + 10f, area.y + 6f, area.width - 20f, area.height - 12f));
            GUILayout.Label($"<b>Energy boost</b>  ({m_Service.Energy} Energy)", RichLabel(13));

            foreach (var building in m_Loop.Workers.Buildings)
            {
                if (m_Service.IsActive(building))
                    GUILayout.Label($"{building.name}: Speed Up {Mathf.CeilToInt(m_Service.RemainingSeconds(building))} s",
                        RichLabel(12), GUILayout.Height(RowHeight - 4f));
            }

            foreach (var building in offers)
                DrawOffer(building);

            GUILayout.EndArea();
            GUI.matrix = Matrix4x4.identity;
        }

        void DrawOffer(Building building)
        {
            var block = m_Service.BlockOf(building);
            // SCRUM-58: without enough Energy the button offers a voluntary rewarded video instead of the boost.
            var video = block == SpeedUpBlock.NotEnoughEnergy ? m_Video : null;
            var offerVideo = video != null && video.CanWatch(building);
            var playing = video != null && video.IsPlaying;

            GUILayout.BeginHorizontal(GUILayout.Height(RowHeight - 4f));
            var text = block != SpeedUpBlock.NotEnoughEnergy ? $"{building.name} is overloaded"
                : offerVideo ? $"{building.name} is overloaded\n<size=10>Out of Energy: watch a video for +{video.EnergyReward}</size>"
                : playing ? $"{building.name} is overloaded\n<size=10>Video playing, Energy comes after it</size>"
                : $"{building.name} is overloaded\n<size=10>Missing: {m_Service.MissingEnergy} Energy</size>";
            GUILayout.Label(text, RichLabel(12));

            if (offerVideo)
            {
                if (GUILayout.Button($"Watch ad (+{video.EnergyReward})", GUILayout.Width(110f), GUILayout.Height(28f)))
                    video.TryWatch(building);
            }
            else
            {
                GUI.enabled = block == SpeedUpBlock.None;
                if (GUILayout.Button($"Speed Up ({m_Service.Rules.EnergyCost})", GUILayout.Width(110f), GUILayout.Height(28f)))
                    m_Service.TrySpeedUp(building);
                GUI.enabled = true;
            }
            GUILayout.EndHorizontal();
        }

        static GUIStyle RichLabel(int size) =>
            new GUIStyle(GUI.skin.label) { richText = true, fontSize = size, wordWrap = true };
    }
}
