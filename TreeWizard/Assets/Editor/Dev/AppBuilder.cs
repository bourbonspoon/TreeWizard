using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TreeWizardDev
{
    /// <summary>
    /// Builds the standalone designer app. Output goes to Builds/ (git-ignored).
    /// Check a build with: TreeWizard -smokeTest &lt;folder&gt; (see TreeWizardApp).
    /// </summary>
    public static class AppBuilder
    {
        const string Scene = "Assets/App/TreeWizardApp.unity";

        [MenuItem("TreeWizard/Dev/Build macOS App")]
        public static void BuildMac() => Build(BuildTarget.StandaloneOSX, "Builds/macOS/TreeWizard.app");

        [MenuItem("TreeWizard/Dev/Build Windows App")]
        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, "Builds/Windows/TreeWizard.exe");

        public static BuildReport Build(BuildTarget target, string path)
        {
            ConfigurePlayer();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                locationPathName = path,
                target = target,
                options = BuildOptions.None,
            });
            var summary = report.summary;
            Debug.Log($"[AppBuilder] {target}: {summary.result}, {summary.totalSize / (1024f * 1024f):F0} MB, " +
                      $"{summary.totalTime.TotalSeconds:F0}s, {summary.totalErrors} errors, {summary.totalWarnings} warnings -> {path}");
            return report;
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "TreeWizard";
            PlayerSettings.productName = "TreeWizard";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "com.treewizard.app");
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 1000;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
        }
    }
}
