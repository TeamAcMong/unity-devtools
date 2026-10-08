#if UNITY_EDITOR || DEVELOPMENT_BUILD || DREAMTECH_DEVTOOLS
using System;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DreamTech.DevTools.Unity
{
	/// <summary>
	/// What a bug report needs about the environment (build, device, screen, session, paths), readable at a glance and
	/// copyable in one tap so a tester pastes it into the ticket instead of typing it.
	/// </summary>
	[DevModule(900)]
	sealed class InfoModule : IDevModule
	{
		const string Category = "Info";
		static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

		public void Register(DevRegistry r)
		{
			r.Watch(Category, "Build", Build, interval: 5f);
			r.Watch(Category, "Device", Device, interval: 5f);
			r.Watch(Category, "Screen", ScreenText, interval: 1f);
			r.Watch(Category, "Session", Session, interval: 1f);
			r.Watch(Category, "Scenes", Scenes, interval: 1f);
			r.Watch(Category, "Data path", () => Application.persistentDataPath, interval: 5f);

			r.Action(Category, "Copy report", () =>
			{
				string report = Report(r);
				GUIUtility.systemCopyBuffer = report;
				return DevResult.Success("report copied (" + report.Split('\n').Length + " lines): paste it into the ticket");
			}, "Build, device, screen, session, paths and every live value, as text on the clipboard.").With(quick: true);
			r.Action(Category, "Log report", () =>
			{
				Debug.Log("[DevTools] report\n" + Report(r));
				return DevResult.Success("report written to the log");
			}, "Same report in the Unity log (for log-upload tools).");
		}

		static string Build() =>
			Application.productName + " " + Application.version + " (" + Application.identifier + "), Unity " + Application.unityVersion
			+ ", " + Application.platform + (Debug.isDebugBuild ? ", development" : "");

		static string Device() =>
			SystemInfo.deviceModel + ", " + SystemInfo.operatingSystem + ", " + SystemInfo.systemMemorySize + " MB RAM, "
			+ SystemInfo.graphicsDeviceName + " (" + SystemInfo.graphicsDeviceType + ")";

		static string ScreenText() =>
			Screen.width + "x" + Screen.height + " @" + Screen.dpi.ToString("0", Inv) + " dpi, " + Screen.orientation + ", safe " + Screen.safeArea;

		static string Session() =>
			"up " + TimeSpan.FromSeconds(Time.realtimeSinceStartup).ToString(@"hh\:mm\:ss", Inv) + ", frame " + Time.frameCount
			+ ", " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv) + (DevClock.Offset == TimeSpan.Zero ? "" : " (clock offset " + DevClock.Offset + ")");

		static string Scenes() =>
			string.Join(", ", Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i).name));

		/// <summary>One "label: value" line per item, grouped by category: the environment first, then every live value of the game.</summary>
		static string Report(DevRegistry r)
		{
			var text = new StringBuilder(1024);
			text.Append("Build: ").Append(Build()).Append('\n');
			text.Append("Device: ").Append(Device()).Append('\n');
			text.Append("Screen: ").Append(ScreenText()).Append('\n');
			text.Append("Session: ").Append(Session()).Append('\n');
			text.Append("Scenes: ").Append(Scenes()).Append('\n');
			text.Append("Data path: ").Append(Application.persistentDataPath).Append('\n');
			string category = null;
			foreach (var watch in r.Watches)
			{
				if (watch.Category == Category) continue;
				if (watch.Category != category)
				{
					category = watch.Category;
					text.Append('[').Append(category).Append("]\n");
				}
				string value;
				try { value = watch.Value(); }
				catch (Exception e) { value = "<" + e.GetType().Name + ">"; }
				text.Append("  ").Append(watch.Label).Append(": ").Append(value).Append('\n');
			}
			return text.ToString().TrimEnd('\n');
		}
	}
}
#endif
