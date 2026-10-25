using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Multikulti.Editor.Naming
{
    /// <summary>
    /// SCRUM-91: fails the build when a banned term from the project glossary (Docs/Slovnik.md)
    /// appears in code, UI assets or the Localization tables.
    /// Run it by hand from the menu Multikulti → Check Banned Terms.
    /// </summary>
    public sealed class BannedTermsCheck : IPreprocessBuildWithReport
    {
        // Lowercase whole words. "idle" is never used, not even as a state name.
        static readonly string[] k_BannedTerms = { "idle" };

        static readonly string[] k_ScannedExtensions = { ".cs", ".prefab", ".unity", ".uxml", ".uss", ".json" };
        const string LocalizationFolder = "Assets/Localization";

        // Splits identifiers and text into words: "isIdle" → is, Idle; "WAVE_PAUSED" → WAVE, PAUSED.
        static readonly Regex k_WordPattern = new Regex(@"[A-Z]?[a-z]+|[A-Z]+(?![a-z])", RegexOptions.Compiled);

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var findings = FindBannedTerms();
            if (findings.Count == 0)
                return;

            throw new BuildFailedException(
                $"Banned terms found ({findings.Count}). See Docs/Slovnik.md for the allowed names:\n" +
                string.Join("\n", findings));
        }

        [MenuItem("Multikulti/Check Banned Terms")]
        static void CheckFromMenu()
        {
            var findings = FindBannedTerms();
            if (findings.Count == 0)
            {
                Debug.Log("Banned terms check: OK, nothing found.");
                return;
            }

            foreach (var finding in findings)
                Debug.LogError($"Banned term: {finding}");
        }

        /// <summary>Returns "path:line: text" for every line that contains a banned term.</summary>
        public static List<string> FindBannedTerms()
        {
            var findings = new List<string>();
            var ownScript = OwnScriptPath();

            foreach (var path in FilesToScan())
            {
                if (string.Equals(path, ownScript, StringComparison.OrdinalIgnoreCase))
                    continue;

                var lines = File.ReadAllLines(path);
                for (var i = 0; i < lines.Length; i++)
                {
                    if (ContainsBannedTerm(lines[i]))
                        findings.Add($"{path}:{i + 1}: {lines[i].Trim()}");
                }
            }

            return findings;
        }

        public static bool ContainsBannedTerm(string text)
        {
            foreach (Match word in k_WordPattern.Matches(text))
            {
                if (k_BannedTerms.Contains(word.Value.ToLowerInvariant()))
                    return true;
            }

            return false;
        }

        static IEnumerable<string> FilesToScan()
        {
            var assets = Directory.EnumerateFiles("Assets", "*", SearchOption.AllDirectories)
                .Where(path => k_ScannedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()));

            var localizationTables = Directory.Exists(LocalizationFolder)
                ? Directory.EnumerateFiles(LocalizationFolder, "*.asset", SearchOption.AllDirectories)
                : Enumerable.Empty<string>();

            return assets.Concat(localizationTables).Select(path => path.Replace('\\', '/')).Distinct();
        }

        static string OwnScriptPath()
        {
            var guid = AssetDatabase.FindAssets($"t:MonoScript {nameof(BannedTermsCheck)}").FirstOrDefault();
            return guid == null ? string.Empty : AssetDatabase.GUIDToAssetPath(guid);
        }
    }
}
