using DreamTech.DevTools.Unity;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DreamTech.DevTools.Editor
{
	/// <summary>
	/// Warns when a non-development build compiles the dev tools in through DREAMTECH_DEVTOOLS (a QA-only define that must not
	/// ship). Fails the build only when <see cref="DevToolsSettings.failReleaseBuildWithDefine"/> is on.
	/// </summary>
	public sealed class ReleaseBuildGuard : IPreprocessBuildWithReport
	{
		public int callbackOrder => 0;

		public void OnPreprocessBuild(BuildReport report)
		{
			if ((report.summary.options & BuildOptions.Development) != 0) return;
			var target = NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(report.summary.platform));
			if (!DevToolsWindow.HasDefine(target)) return;

			const string message = "[DevTools] " + DevToolsWindow.Define + " is defined for a non-development build: the dev tools (cheats, HUD) will ship. " +
			                       "Remove the define unless this is a QA build.";
			if (DevToolsSettings.Current.failReleaseBuildWithDefine) throw new BuildFailedException(message);
			Debug.LogWarning(message);
		}
	}
}
