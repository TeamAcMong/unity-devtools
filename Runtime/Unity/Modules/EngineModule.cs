#if UNITY_EDITOR || DEVELOPMENT_BUILD || DREAMTECH_DEVTOOLS
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

namespace DreamTech.DevTools.Unity
{
	/// <summary>Genre-agnostic engine controls: game speed, frames, quality, scenes, audio, screenshots, memory, hierarchy.</summary>
	[DevModule(800)]
	sealed class EngineModule : IDevModule
	{
		const string Cat = "Engine";
		static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
		static float _scaleBeforePause = 1f;
		static bool _paused;

		public void Register(DevRegistry r)
		{
			r.Watch(Cat, "Frame", () => (1f / Mathf.Max(0.0001f, Time.smoothDeltaTime)).ToString("0", Inv) + " fps, " + (Time.smoothDeltaTime * 1000f).ToString("0.0", Inv) + " ms");
			r.Watch(Cat, "Time scale", () => Time.timeScale.ToString("0.##", Inv) + (_paused ? " (paused)" : ""));
			r.Watch(Cat, "Target fps / vSync", () => Application.targetFrameRate + " / " + QualitySettings.vSyncCount);
			r.Watch(Cat, "Memory", () => "mono " + (Profiler.GetMonoUsedSizeLong() >> 20) + " MB, allocated " + (Profiler.GetTotalAllocatedMemoryLong() >> 20) + " MB, reserved " + (Profiler.GetTotalReservedMemoryLong() >> 20) + " MB", interval: 1f);
			r.Watch(Cat, "Screen", () => Screen.width + "x" + Screen.height + " @" + Screen.dpi.ToString("0", Inv) + " dpi, safe " + Screen.safeArea);
			r.Watch(Cat, "Scenes", () => string.Join(", ", Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i).name)));
			r.Watch(Cat, "Device", () => SystemInfo.deviceModel + ", " + SystemInfo.operatingSystem + ", " + SystemInfo.systemMemorySize + " MB RAM, " + SystemInfo.graphicsDeviceName, interval: 5f);
			r.Watch(Cat, "Build", () => Application.productName + " " + Application.version + " (Unity " + Application.unityVersion + ", " + Application.platform + ")", interval: 5f);

			r.Action(Cat, "Time scale", new[] { DevParam.Float("scale", 1) }, a => SetScale(Mathf.Clamp(a.Float(0), 0f, 100f)),
				"Game speed. Everything on scaled time follows (animations, tweens, timers of the game).").Quick = true;
			r.Action(Cat, "Speed x0.25", () => SetScale(0.25f));
			r.Action(Cat, "Speed x1", () => SetScale(1f));
			r.Action(Cat, "Speed x4", () => SetScale(4f));
			r.Action(Cat, "Speed x10", () => SetScale(10f));
			r.Toggle(Cat, "Pause", () => _paused, on =>
			{
				if (on && !_paused)
				{
					_scaleBeforePause = Time.timeScale;
					Time.timeScale = 0f;
				}
				else if (!on && _paused) Time.timeScale = _scaleBeforePause;
				_paused = on;
			}, "timeScale 0; Step frames advances while paused.");
			r.Action(Cat, "Step frames", new[] { DevParam.Int("frames", 1) }, a =>
			{
				if (!_paused) return DevResult.Fail("pause first");
				DevToolsHost.Run(Step(Mathf.Max(1, a.Int(0))));
				return DevResult.Success("stepping " + a.Int(0) + " frame(s)");
			});
			r.Action(Cat, "Target fps", new[] { DevParam.Int("fps", 60) }, a =>
			{
				QualitySettings.vSyncCount = 0;
				Application.targetFrameRate = a.Int(0) > 0 ? a.Int(0) : -1;
				return DevResult.Success("targetFrameRate " + Application.targetFrameRate + ", vSync off");
			}, "30 reproduces low-end phones; 0 = uncapped.");
			r.Action(Cat, "Quality level", new[] { DevParam.Choice("level", () => QualitySettings.names, QualitySettings.names.FirstOrDefault()) }, a =>
			{
				int i = Array.IndexOf(QualitySettings.names, a.Str(0));
				if (i < 0) return DevResult.Fail("unknown quality level");
				QualitySettings.SetQualityLevel(i, true);
				return DevResult.Success("quality " + a.Str(0));
			});
			r.Toggle(Cat, "Mute audio", () => AudioListener.pause, v => AudioListener.pause = v);
			r.Action(Cat, "Master volume", new[] { DevParam.Float("volume", 1) }, a =>
			{
				AudioListener.volume = Mathf.Clamp01(a.Float(0));
				return DevResult.Success("AudioListener.volume " + AudioListener.volume.ToString("0.##", Inv));
			});

			r.Action(Cat, "Load scene", new[] { DevParam.Choice("scene", BuildScenes, BuildScenes().FirstOrDefault()) }, a =>
			{
				SceneManager.LoadScene(a.Str(0));
				return DevResult.Success("loading " + a.Str(0));
			}, "Scenes of the build settings.").Confirm = true;
			r.Action(Cat, "Reload active scene", () =>
			{
				string s = SceneManager.GetActiveScene().name;
				SceneManager.LoadScene(s);
				return DevResult.Success("reloading " + s);
			}).Confirm = true;

			r.Action(Cat, "Screenshot", new[] { DevParam.Bool("withHud", false) }, a =>
			{
				string dir = Path.Combine(Application.persistentDataPath, "DevShots");
				Directory.CreateDirectory(dir);
				string file = Path.Combine(dir, "shot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", Inv) + ".png");
				DevToolsHost.Run(Capture(file, a.Bool(0)));
				return DevResult.Success(file);
			}, "PNG into persistentDataPath/DevShots (without the HUD unless asked).").Quick = true;
			r.Action(Cat, "GC + unload unused", () =>
			{
				long before = Profiler.GetMonoUsedSizeLong();
				GC.Collect();
				Resources.UnloadUnusedAssets();
				return DevResult.Success("mono " + (before >> 20) + " -> " + (Profiler.GetMonoUsedSizeLong() >> 20) + " MB");
			});
			r.Action(Cat, "Log hierarchy", new[] { DevParam.Int("depth", 3) }, a =>
			{
				int depth = a.Int(0);
				var sb = new StringBuilder();
				void Walk(Transform t, int d)
				{
					if (d > depth) return;
					sb.Append(' ', d * 2).Append(t.name).Append(t.gameObject.activeInHierarchy ? "" : " (off)").Append('\n');
					foreach (Transform c in t) Walk(c, d + 1);
				}
				foreach (var root in AllTransforms(true).Where(t => t.parent == null)) Walk(root, 0);
				Debug.Log("[DevTools] hierarchy\n" + sb);
				return DevResult.Success("written to the console (" + sb.Length + " chars)");
			});
			r.Action(Cat, "Find object", new[] { DevParam.Text("name", "") }, a =>
			{
				var hits = AllTransforms(false).Where(t => t.name.IndexOf(a.Str(0), StringComparison.OrdinalIgnoreCase) >= 0).Take(20).Select(PathOf).ToList();
				return hits.Count == 0 ? DevResult.Fail("no active object named like '" + a.Str(0) + "'") : DevResult.Success(string.Join("\n", hits));
			}, "Active objects whose name contains the text (20 max), with their paths.");
			r.Action(Cat, "UI at point", new[] { DevParam.Float("x", 0.5), DevParam.Float("y", 0.5) }, a =>
			{
				if (EventSystem.current == null) return DevResult.Fail("no EventSystem");
				var hits = new List<RaycastResult>();
				EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = new Vector2(a.Float(0) * Screen.width, a.Float(1) * Screen.height) }, hits);
				return DevResult.Success(hits.Count == 0 ? "nothing: a tap there reaches the world" : string.Join("\n", hits.Take(8).Select(h => PathOf(h.gameObject.transform) + " (order " + h.sortingOrder + ")")));
			}, "uGUI objects under a point (0..1, origin bottom-left), topmost first: what receives or blocks a tap there.");
			r.Action(Cat, "Quit", () =>
			{
#if UNITY_EDITOR
				UnityEditor.EditorApplication.isPlaying = false;
#else
				Application.Quit();
#endif
				return DevResult.Success("quitting");
			}).Confirm = true;
		}

		static DevResult SetScale(float s)
		{
			Time.timeScale = s;
			_paused = s == 0f;
			return DevResult.Success("timeScale " + s.ToString("0.##", Inv));
		}

		static IEnumerator Step(int frames)
		{
			for (int i = 0; i < frames && _paused; i++)
			{
				Time.timeScale = _scaleBeforePause > 0 ? _scaleBeforePause : 1f;
				yield return null;
				Time.timeScale = 0f;
			}
		}

		static IEnumerator Capture(string file, bool withHud)
		{
			if (!withHud) DevToolsHud.SuppressDrawing = true;
			yield return new WaitForEndOfFrame();
			ScreenCapture.CaptureScreenshot(file);
			yield return null;
			yield return null;
			DevToolsHud.SuppressDrawing = false;
		}

		static IList<string> BuildScenes()
		{
			var list = new List<string>();
			for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
				list.Add(Path.GetFileNameWithoutExtension(SceneUtility.GetScenePathByBuildIndex(i)));
			return list;
		}

		static IEnumerable<Transform> AllTransforms(bool includeInactive)
		{
#if UNITY_6000_5_OR_NEWER
			return UnityEngine.Object.FindObjectsByType<Transform>(includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude);
#elif UNITY_2023_1_OR_NEWER
			return UnityEngine.Object.FindObjectsByType<Transform>(includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
			return UnityEngine.Object.FindObjectsOfType<Transform>(includeInactive);
#endif
		}

		static string PathOf(Transform t)
		{
			var sb = new StringBuilder(t.name);
			for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
			return sb.ToString();
		}
	}

	/// <summary>Warnings and errors of the Unity console, readable on a device.</summary>
	[DevModule(810)]
	sealed class LogsModule : IDevModule
	{
		const string Cat = "Logs";

		public void Register(DevRegistry r)
		{
			r.Watch(Cat, "Errors / warnings", () => DevToolsHost.ErrorCount + " / " + DevToolsHost.WarningCount);
			r.Watch(Cat, "Last error", () =>
			{
				for (int i = DevToolsHost.Logs.Count - 1; i >= 0; i--)
					if (DevToolsHost.Logs[i].Type != LogType.Warning) return FirstLine(DevToolsHost.Logs[i].Message);
				return "none";
			});
			r.Action(Cat, "Show recent", new[] { DevParam.Int("count", 10), DevParam.Bool("warnings", false), DevParam.Bool("stack", false) }, a =>
			{
				var items = DevToolsHost.Logs.Where(l => a.Bool(1) || l.Type != LogType.Warning).Reverse().Take(Math.Max(1, a.Int(0))).ToList();
				if (items.Count == 0) return DevResult.Success("nothing logged");
				var sb = new StringBuilder();
				foreach (var l in items)
				{
					sb.Append('[').Append(l.Type).Append(" @").Append(l.Time.ToString("0.0", CultureInfo.InvariantCulture)).Append("s] ").Append(l.Message).Append('\n');
					if (a.Bool(2) && !string.IsNullOrEmpty(l.Stack)) sb.Append(string.Join("\n", l.Stack.Split('\n').Take(6))).Append('\n');
				}
				return DevResult.Success(sb.ToString());
			}, "Newest first. Errors / exceptions / asserts, warnings on demand.").Quick = true;
			r.Action(Cat, "Clear", () =>
			{
				DevToolsHost.ClearLogs();
				return DevResult.Success("cleared");
			});
		}

		static string FirstLine(string s)
		{
			int i = s.IndexOf('\n');
			return i < 0 ? s : s.Substring(0, i);
		}
	}

	/// <summary>The HUD's own controls (useful from scripts, e.g. to film a tab).</summary>
	[DevModule(820)]
	sealed class HudModule : IDevModule
	{
		const string Cat = "HUD";

		public void Register(DevRegistry r)
		{
			r.Action(Cat, "Show panel", new[] { DevParam.Choice("tab", DevToolsHud.TabNames, "quick") }, a =>
				DevToolsHud.ShowTab(a.Str(0)) ? DevResult.Success("panel on " + a.Str(0)) : DevResult.Fail("unknown tab '" + a.Str(0) + "'"));
			r.Action(Cat, "Close panel", () =>
			{
				DevToolsHud.Open(false);
				return DevResult.Success("closed");
			});
			r.Action(Cat, "Hide HUD", () =>
			{
				DevToolsHud.SetHidden(true);
				return DevResult.Success("hidden: the hide key or a multi-finger tap shows it again");
			}, "Also hides the DEV pill (clean screenshots, video).");
			r.Action(Cat, "Scale", new[] { DevParam.Float("scale", 1) }, a =>
			{
				DevToolsHud.Scale = a.Float(0);
				return DevResult.Success("HUD scale " + DevToolsHud.Scale.ToString("0.##", CultureInfo.InvariantCulture));
			}, "0.5 .. 2.5");
			r.Action(Cat, "Opacity", new[] { DevParam.Float("alpha", 0.92) }, a =>
			{
				DevToolsHud.Opacity = a.Float(0);
				return DevResult.Success("HUD opacity " + DevToolsHud.Opacity.ToString("0.##", CultureInfo.InvariantCulture));
			}, "0.3 .. 1: see the game through the panel.");
		}
	}

	/// <summary>PlayerPrefs, the data folder and the save wipe.</summary>
	[DevModule(830)]
	sealed class DataModule : IDevModule
	{
		const string Cat = "Data";

		public void Register(DevRegistry r)
		{
			r.Watch(Cat, "persistentDataPath", () => Application.persistentDataPath, interval: 5f);
			r.Watch(Cat, "Files", () =>
			{
				var d = new DirectoryInfo(Application.persistentDataPath);
				if (!d.Exists) return "none";
				var files = d.GetFiles("*", SearchOption.AllDirectories);
				return files.Length + " file(s), " + (files.Sum(f => f.Length) / 1024) + " KB";
			}, interval: 2f);
			r.Watch(Cat, "Wipe pending", () => PlayerPrefs.GetInt(DevToolsKeys.WipePending, 0) == 1 ? "YES (next start)" : "no");

			r.Action(Cat, "PlayerPrefs get", new[] { DevParam.Text("key", "") }, a =>
			{
				string k = a.Str(0);
				if (!PlayerPrefs.HasKey(k)) return DevResult.Fail("no key '" + k + "'");
				string s = PlayerPrefs.GetString(k, "\u0001");
				if (s != "\u0001") return DevResult.Success(k + " = \"" + s + "\" (string)");
				int i = PlayerPrefs.GetInt(k, int.MinValue);
				if (i != int.MinValue) return DevResult.Success(k + " = " + i + " (int)");
				return DevResult.Success(k + " = " + PlayerPrefs.GetFloat(k).ToString(CultureInfo.InvariantCulture) + " (float)");
			});
			r.Action(Cat, "PlayerPrefs set", new[] { DevParam.Text("key", ""), DevParam.Choice("type", () => new[] { "int", "float", "string" }, "int"), DevParam.Text("value", "") }, a =>
			{
				string k = a.Str(0);
				switch (a.Str(1))
				{
					case "int": PlayerPrefs.SetInt(k, a.Int(2)); break;
					case "float": PlayerPrefs.SetFloat(k, a.Float(2)); break;
					default: PlayerPrefs.SetString(k, a.Str(2)); break;
				}
				PlayerPrefs.Save();
				return DevResult.Success(k + " set (the game reads it when it next loads it)");
			});
			r.Action(Cat, "PlayerPrefs delete", new[] { DevParam.Text("key", "") }, a =>
			{
				PlayerPrefs.DeleteKey(a.Str(0));
				PlayerPrefs.Save();
				return DevResult.Success("deleted " + a.Str(0));
			});
			r.Action(Cat, "Open data folder", () =>
			{
				Application.OpenURL("file://" + Application.persistentDataPath);
				return DevResult.Success(Application.persistentDataPath);
			}, "Desktop / Editor only.");
			var wipe = r.Action(Cat, "Wipe save at next start", () =>
			{
				PlayerPrefs.SetInt(DevToolsKeys.WipePending, 1);
				PlayerPrefs.Save();
				return DevResult.Success("armed: restart the game (Editor: stop, then play) to begin as a new player");
			}, "Deletes persistentDataPath and PlayerPrefs before the game loads anything at the next start (DevTools settings and the keys in DevToolsSettings survive).");
			wipe.Confirm = true;
			r.Action(Cat, "Cancel pending wipe", () =>
			{
				PlayerPrefs.DeleteKey(DevToolsKeys.WipePending);
				PlayerPrefs.Save();
				return DevResult.Success("wipe cancelled");
			});
		}
	}
}
#endif
