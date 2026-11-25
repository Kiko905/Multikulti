using Multikulti.Buildings;
using Multikulti.Waves;
using UnityEngine;

namespace Multikulti.UI
{
    /// <summary>
    /// Prompt of the mini-event Sudden rush (SCRUM-60, UI handoff D): "Sudden rush at Beach Bar!" with
    /// Move Worker here / Ignore. The wave keeps running while it is shown. It closes on a choice, when a Worker
    /// reaches the building another way, or when the wave leaves WaveActive.
    ///
    /// Move Worker here takes the Worker from the staffed building with the fewest pending units; the Worker goes
    /// to the only building without one, which is the building of the rush (WorkerRoster.ChooseTarget).
    /// Use Energy to serve faster comes with the Energy boost (SCRUM-57); until then the prompt does not offer it.
    ///
    /// Added to the WaveLoop object at runtime, so the scene does not change. Graybox IMGUI until the S2 art.
    /// </summary>
    public sealed class SuddenRushPrompt : MonoBehaviour
    {
        const float ReferenceHeight = 720f;
        const float Width = 340f;

        WaveLoop m_Loop;
        Building m_Target;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var loop = FindAnyObjectByType<WaveLoop>();
            if (loop != null && loop.GetComponent<SuddenRushPrompt>() == null)
                loop.gameObject.AddComponent<SuddenRushPrompt>();
        }

        void Awake()
        {
            m_Loop = GetComponent<WaveLoop>();
            if (m_Loop != null)
                m_Loop.SuddenRushStarted += OnRushStarted;
        }

        void OnDestroy()
        {
            if (m_Loop != null)
                m_Loop.SuddenRushStarted -= OnRushStarted;
        }

        void OnRushStarted(Building building) => m_Target = building;

        void Update()
        {
            if (m_Target == null) return;
            var state = m_Loop.State;
            if (state != WaveState.WaveActive && state != WaveState.WavePaused)
                Close("wave over");
            else if (m_Target.HasWorker)
                Close("Worker already there");
        }

        void Close(string reason)
        {
            Debug.Log($"Wave {m_Loop.Wave}: Sudden rush prompt closed ({reason})");
            m_Target = null;
        }

        void OnGUI()
        {
            if (m_Target == null || m_Loop.State != WaveState.WaveActive) return;

            var scale = Screen.height / ReferenceHeight;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            var screenWidth = Screen.width / scale;
            var area = new Rect((screenWidth - Width) / 2f, 70f, Width, 118f);
            GUI.Box(area, GUIContent.none);
            GUILayout.BeginArea(new Rect(area.x + 12f, area.y + 8f, area.width - 24f, area.height - 16f));
            GUILayout.Label($"<b>Sudden rush at {m_Target.name}!</b>", RichLabel(16));
            GUILayout.Label($"Extra guests want {m_Target.Need}. The wave keeps going.", RichLabel(12));
            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Move Worker here", GUILayout.Height(32f)))
                MoveWorkerHere();
            if (GUILayout.Button("Ignore", GUILayout.Height(32f), GUILayout.Width(90f)))
                Close("Ignore");
            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            GUI.matrix = Matrix4x4.identity;
        }

        void MoveWorkerHere()
        {
            var workers = m_Loop.Workers;
            Building source = null;
            if (workers != null)
            {
                foreach (var building in workers.Buildings)
                {
                    if (building == null || building == m_Target || !building.HasWorker || building.Queue == null) continue;
                    if (source == null || building.Queue.PendingCount < source.Queue.PendingCount)
                        source = building;
                }
            }

            if (source != null && workers.TryMoveFrom(source))
                Close($"Move Worker here, Worker from {source.name}");
            else
                Debug.LogWarning($"Wave {m_Loop.Wave}: Sudden rush, no Worker could be moved to {m_Target.name}.");
        }

        static GUIStyle RichLabel(int size) =>
            new GUIStyle(GUI.skin.label) { richText = true, fontSize = size, wordWrap = true };
    }
}
