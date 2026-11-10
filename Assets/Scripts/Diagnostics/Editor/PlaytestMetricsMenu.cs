using UnityEditor;
using UnityEngine;

namespace Multikulti.Diagnostics.EditorTools
{
    /// <summary>Helpers for the playtest metrics (SCRUM-93).</summary>
    static class PlaytestMetricsMenu
    {
        [MenuItem("Multikulti/Playtest/Open metrics folder")]
        static void OpenFolder()
        {
            System.IO.Directory.CreateDirectory(PlaytestMetrics.Folder);
            EditorUtility.RevealInFinder(PlaytestMetrics.Folder);
        }

        /// <summary>
        /// Writes one made-up wave so the whole path (game → file → tools/playtest_report.py) can be checked
        /// before the real wave loop exists. Use in Play mode.
        /// </summary>
        [MenuItem("Multikulti/Playtest/Write sample wave")]
        static void WriteSampleWave()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("Playtest metrics: start Play mode first.");
                return;
            }

            PlaytestMetrics.WorkerMoved(1, false, "BeachBar", "Restaurant", "OK");
            PlaytestMetrics.WaveStarted(1);
            PlaytestMetrics.BottleneckShown(1, "Drinks");
            PlaytestMetrics.WorkerMoved(1, true, "Reception", "BeachBar", "BOTTLENECK");
            PlaytestMetrics.WaveCompleted(1, "4★", 0.86f, 41.5f, 131.5f);
            PlaytestMetrics.UpgradeOffered(1, "BeachBar");
            PlaytestMetrics.UpgradeBought(1, "BeachBar", 2);

            Debug.Log($"Playtest metrics: sample wave written to {PlaytestMetrics.CurrentFile}");
        }
    }
}
