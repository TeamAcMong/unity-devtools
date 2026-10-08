using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DreamTech.DevTools.Demo.EditorTools
{
	/// <summary>
	/// Windows development build of the demo (the dev tools are in it because it is a development build), for the player
	/// smoke test. Batch mode: -executeMethod DreamTech.DevTools.Demo.EditorTools.DemoBuild.BuildWindows [-out dir]
	/// </summary>
	public static class DemoBuild
	{
		[MenuItem("Tools/DreamTech/DevTools Demo/Build Windows player")]
		public static void BuildWindows()
		{
			string outDir = Arg("-out") ?? "Builds/Demo";
			if (!File.Exists(DemoSceneBuilder.ScenePath)) DemoSceneBuilder.Build();
			PlayerSettings.productName = "DevToolsDemo";
			PlayerSettings.defaultScreenWidth = 540;
			PlayerSettings.defaultScreenHeight = 960;
			PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
			// the smoke player is launched while someone works in another window: without this it pauses unfocused and times out
			bool runInBackground = PlayerSettings.runInBackground;
			PlayerSettings.runInBackground = true;
			BuildReport report;
			try
			{
				report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
				{
					scenes = new[] { DemoSceneBuilder.ScenePath },
					locationPathName = Path.Combine(outDir, "DevToolsDemo.exe"),
					target = BuildTarget.StandaloneWindows64,
					options = BuildOptions.Development,
				});
			}
			finally
			{
				PlayerSettings.runInBackground = runInBackground;
			}
			Debug.Log("[DevTools Demo] build " + report.summary.result + " -> " + report.summary.outputPath);
			if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 3);
		}

		static string Arg(string name)
		{
			var args = System.Environment.GetCommandLineArgs();
			for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
			return null;
		}
	}
}
