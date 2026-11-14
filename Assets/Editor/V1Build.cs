using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class V1Build {
	[MenuItem("Our Final Dawn/Build Windows V1")]
	public static void Build() {
		BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
			scenes = new[] { "Assets/Scenes/Debug-Scene.unity" },
			locationPathName = "Builds/V1/Our Final Dawn.exe",
			target = BuildTarget.StandaloneWindows64,
			options = BuildOptions.Development
		});
		Debug.Log($"[V1Build] {report.summary.result}, errors={report.summary.totalErrors}");
		if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
	}
}
