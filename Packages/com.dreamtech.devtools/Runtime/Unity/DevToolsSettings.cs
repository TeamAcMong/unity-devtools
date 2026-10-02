using System;
using System.Collections.Generic;
using UnityEngine;

namespace DreamTech.DevTools.Unity
{
	/// <summary>
	/// Project settings of the dev tools (Project Settings > DreamTech DevTools creates it at
	/// Assets/Resources/DevToolsSettings.asset). Without the asset every field keeps its default.
	/// This class is compiled in every build (so the asset never shows a missing script); nothing reads it in shipping builds.
	/// </summary>
	public sealed class DevToolsSettings : ScriptableObject
	{
		public const string ResourcePath = "DevToolsSettings";

		[Serializable]
		public sealed class Preset
		{
			public string Name = "";

			[TextArea(2, 8)]
			public string Script = "";
		}

		/// <summary>Look of the DEV pill. Values are serialized by number: only append.</summary>
		public enum PillStyleKind
		{
			/// <summary>Status dot, fps and a red badge with the error count.</summary>
			Compact = 0,

			/// <summary>Compact plus the pinned watches.</summary>
			Detailed = 1,
		}

		[Header("Runtime")]
		[Tooltip("Create the host and HUD automatically at startup (Editor, development builds, DREAMTECH_DEVTOOLS builds).")]
		public bool AutoStart = true;

		[Tooltip("Title of the HUD panel.")]
		public string Title = "DevTools";

		[Tooltip("Start with the HUD hidden (F2 / multi-finger tap shows it).")]
		public bool StartHidden;

		[Tooltip("Initial look of the DEV pill (Compact: fps + error badge; Detailed: also the pinned watches). A long-press on the pill toggles it and the choice is remembered on the device.")]
		public PillStyleKind PillStyle = PillStyleKind.Compact;

		[Tooltip("Keys that open / close the panel (read from IMGUI events: works with either input backend).")]
		public KeyCode[] OpenKeys = { KeyCode.F1, KeyCode.BackQuote };

		public KeyCode HideKey = KeyCode.F2;

		[Tooltip("Fingers of the tap that shows / hides the whole HUD on touch screens (0 = off).")]
		[Range(0, 5)]
		public int ToggleFingers = 3;

		[Tooltip("Write every command and its result to the Unity console.")]
		public bool LogCommands = true;

		[Tooltip("Script run at every start (console lines; wait / waitfor allowed).")]
		[TextArea(2, 8)]
		public string BootScript = "";

		[Tooltip("Ready-made scripts shown in the Scenarios tab (next to the presets modules register).")]
		public List<Preset> Presets = new List<Preset>();

		[Header("Save wipe")]
		[Tooltip("PlayerPrefs keys that survive 'Wipe save at next start' (the dev tools' own keys always survive).")]
		public List<string> PreservedPlayerPrefsKeys = new List<string>();

		[Tooltip("Folders under persistentDataPath the wipe keeps (relative names).")]
		public List<string> WipeKeepFolders = new List<string> { "DevShots" };

		[Header("Release safety")]
		[Tooltip("Fail non-development builds that compile the dev tools in through DREAMTECH_DEVTOOLS (otherwise only a warning is logged).")]
		public bool failReleaseBuildWithDefine;

		[Header("Editor")]
		[Tooltip("Scene the editor window's Play buttons open first (empty = the open scene).")]
		public string LaunchScene = "";

		[Tooltip("Player executable the editor window can start with a boot script (relative to the project folder).")]
		public string BuildExecutable = "";

		[Tooltip("Extra arguments for that player.")]
		public string BuildArguments = "";

		static DevToolsSettings _cached;

		/// <summary>The project's settings asset, or defaults.</summary>
		public static DevToolsSettings Current
		{
			get
			{
				if (_cached != null) return _cached;
				_cached = Resources.Load<DevToolsSettings>(ResourcePath);
				if (_cached == null)
				{
					_cached = CreateInstance<DevToolsSettings>();
					_cached.hideFlags = HideFlags.DontSave;
				}
				return _cached;
			}
		}

		/// <summary>Forget the cached instance (after the asset was created or edited).</summary>
		public static void Reload() => _cached = null;
	}
}
