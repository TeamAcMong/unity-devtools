#if UNITY_EDITOR || DEVELOPMENT_BUILD || DREAMTECH_DEVTOOLS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace DreamTech.DevTools.Unity
{
	/// <summary>PlayerPrefs keys of the dev tools (they survive the save wipe).</summary>
	public static class DevToolsKeys
	{
		public const string BootOnce = "DreamTech.DevTools.BootOnce";
		public const string BootAlways = "DreamTech.DevTools.BootAlways";
		public const string WipePending = "DreamTech.DevTools.WipePending";
		public const string ClockOffset = "DreamTech.DevTools.ClockOffsetSeconds";
		public const string HudHidden = "DreamTech.DevTools.HudHidden";
		public const string HudFavorites = "DreamTech.DevTools.Favorites";
		public const string HudPill = "DreamTech.DevTools.Pill";
		public const string HudSize = "DreamTech.DevTools.Size";
		public const string HudDock = "DreamTech.DevTools.Dock";
		public const string HudTab = "DreamTech.DevTools.Tab";
		public const string HudScale = "DreamTech.DevTools.Scale";
		public const string HudAlpha = "DreamTech.DevTools.Alpha";
		public const string HudPillExpanded = "DreamTech.DevTools.PillExpanded";

		public static readonly string[] All =
		{
			BootOnce, BootAlways, ClockOffset, HudHidden, HudFavorites, HudPill, HudSize, HudDock, HudTab, HudScale, HudAlpha, HudPillExpanded,
		};
	}

	/// <summary>
	/// Starts the dev tools in the Editor, development builds and DREAMTECH_DEVTOOLS builds (this file compiles to nothing
	/// elsewhere): resets static state for a new play session, runs a pending save wipe and restores the clock offset before
	/// the game loads anything, then creates the host once the first scene is loaded.
	/// </summary>
	public static class DevToolsBootstrap
	{
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		static void ResetSession()
		{
			DevTools.Reset();
			DevToolsSettings.Reload();
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
		static void Activate()
		{
			DevTools.Activate();
			if (PlayerPrefs.GetInt(DevToolsKeys.WipePending, 0) == 1) WipeSave(DevToolsSettings.Current);
			if (long.TryParse(PlayerPrefs.GetString(DevToolsKeys.ClockOffset, ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out long seconds) && seconds != 0)
			{
				DevClock.RestoreOffset(TimeSpan.FromSeconds(seconds));
				Debug.LogWarning("[DevTools] game clock offset " + DevClock.Offset + " -> " + DevClock.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " (time travel; Time > Reset clock)");
			}
			DevClock.OffsetChanged += () =>
			{
				if (DevClock.Offset == TimeSpan.Zero) PlayerPrefs.DeleteKey(DevToolsKeys.ClockOffset);
				else PlayerPrefs.SetString(DevToolsKeys.ClockOffset, ((long)DevClock.Offset.TotalSeconds).ToString(CultureInfo.InvariantCulture));
				PlayerPrefs.Save();
			};
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		static void CreateHost()
		{
			if (DevToolsSettings.Current.AutoStart) DevToolsHost.Ensure();
		}

		/// <summary>
		/// Deletes every file under persistentDataPath (except the keep folders) and every PlayerPrefs key (except the dev
		/// tools' own and the preserved ones): the next start is a fresh install.
		/// </summary>
		public static int WipeSave(DevToolsSettings settings)
		{
			var keepStrings = new Dictionary<string, string>();
			var keepInts = new Dictionary<string, int>();
			var keepFloats = new Dictionary<string, float>();
			var keys = new List<string>(DevToolsKeys.All);
			keys.AddRange(settings.PreservedPlayerPrefsKeys);
			foreach (string k in keys)
			{
				if (string.IsNullOrEmpty(k) || !PlayerPrefs.HasKey(k)) continue;
				// PlayerPrefs has no type query: a key holds one of the three; the defaults tell which one is set
				string s = PlayerPrefs.GetString(k, "\u0001");
				if (s != "\u0001") keepStrings[k] = s;
				else if (PlayerPrefs.GetInt(k, int.MinValue) != int.MinValue) keepInts[k] = PlayerPrefs.GetInt(k);
				else keepFloats[k] = PlayerPrefs.GetFloat(k);
			}
			PlayerPrefs.DeleteAll();
			foreach (var kv in keepStrings) PlayerPrefs.SetString(kv.Key, kv.Value);
			foreach (var kv in keepInts) PlayerPrefs.SetInt(kv.Key, kv.Value);
			foreach (var kv in keepFloats) PlayerPrefs.SetFloat(kv.Key, kv.Value);
			PlayerPrefs.DeleteKey(DevToolsKeys.WipePending);
			PlayerPrefs.DeleteKey(DevToolsKeys.ClockOffset);
			PlayerPrefs.Save();

			int files = 0;
			var root = new DirectoryInfo(Application.persistentDataPath);
			if (root.Exists)
			{
				var keep = new HashSet<string>(settings.WipeKeepFolders, StringComparer.OrdinalIgnoreCase);
				foreach (var f in root.GetFiles())
				{
					try { f.Delete(); files++; }
					catch (Exception e) { Debug.LogWarning("[DevTools] wipe: " + e.Message); }
				}
				foreach (var d in root.GetDirectories())
				{
					if (keep.Contains(d.Name)) continue;
					try
					{
						files += d.GetFiles("*", SearchOption.AllDirectories).Length;
						d.Delete(true);
					}
					catch (Exception e) { Debug.LogWarning("[DevTools] wipe: " + e.Message); }
				}
			}
			DevClock.RestoreOffset(TimeSpan.Zero);
			Debug.LogWarning("[DevTools] save wiped before start: " + files + " file(s) under " + root.FullName + ", PlayerPrefs cleared (dev tools settings kept)");
			return files;
		}
	}
}
#endif
