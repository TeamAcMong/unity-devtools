#if UNITY_EDITOR || DEVELOPMENT_BUILD || DREAMTECH_DEVTOOLS
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace DreamTech.DevTools.Unity
{
	/// <summary>
	/// Persistent object that owns the HUD, drives running scripts every frame, registers the [DevModule] modules of every
	/// assembly that references DreamTech.DevTools and runs boot scripts. The Unity console (every type) is recorded from
	/// SubsystemRegistration on, before the game logs anything, for the Logs page.
	/// Boot scripts, in this order: settings BootScript, <c>-devboot "..."</c> on the command line, PlayerPrefs BootAlways, and
	/// PlayerPrefs BootOnce (written by the editor window's Play buttons, deleted when read).
	/// </summary>
	[DefaultExecutionOrder(-10000)]
	public sealed class DevToolsHost : MonoBehaviour
	{
		public struct LogRecord
		{
			/// <summary>Unique for the session; stays with the entry when older ones are trimmed.</summary>
			public int Id;
			public float Time;
			public LogType Type;
			public string Message;
			public string Stack;
		}

		public const int MaxLogRecords = 1000;

		public static DevToolsHost Instance { get; private set; }

		static readonly List<LogRecord> _logs = new List<LogRecord>();
		public static IReadOnlyList<LogRecord> Logs => _logs;
		public static int ErrorCount { get; private set; }
		public static int WarningCount { get; private set; }
		/// <summary>Plain Debug.Log entries.</summary>
		public static int LogCount { get; private set; }
		/// <summary>Bumped on every recorded entry and on clear, so a view redraws only when the console changed.</summary>
		public static int LogVersion { get; private set; }
		static int _nextLogId;
		static bool _capturing;

		public static bool IsError(LogType type) => type == LogType.Error || type == LogType.Exception || type == LogType.Assert;

		public static void ClearLogs()
		{
			_logs.Clear();
			ErrorCount = WarningCount = LogCount = 0;
			LogVersion++;
		}

		/// <summary>
		/// Starts (or restarts, for a new play session) recording the console. Called by the bootstrap at SubsystemRegistration, so
		/// boot logs are kept even though the host is created after the first scene loads. Runs for the whole app, host or not.
		/// </summary>
		internal static void StartLogCapture()
		{
			Application.logMessageReceived -= OnLog;
			ClearLogs();
			Application.logMessageReceived += OnLog;
			_capturing = true;
		}

		/// <summary>Creates the host if it does not exist yet (the bootstrap does it after the first scene loads).</summary>
		public static DevToolsHost Ensure()
		{
			if (Instance != null) return Instance;
			var go = new GameObject("[DevTools]");
			DontDestroyOnLoad(go);
			var host = go.AddComponent<DevToolsHost>();
			go.AddComponent<DevToolsHud>();
			return host;
		}

		void Awake()
		{
			if (Instance != null && Instance != this)
			{
				Destroy(gameObject);
				return;
			}
			Instance = this;
			if (!_capturing) StartLogCapture();
			var r = DevTools.Registry;
			r.Clock = () => Time.realtimeSinceStartupAsDouble;
			r.CommandException += Debug.LogException;
			if (DevToolsSettings.Current.LogCommands) r.Executed += LogEntry;
			int n = r.RegisterModules(ModuleAssemblies());
			foreach (var p in DevToolsSettings.Current.Presets)
				if (p != null && !string.IsNullOrWhiteSpace(p.Name)) r.Preset(p.Name, p.Script);
			Debug.Log("[DevTools] " + n + " modules, " + r.Commands.Count + " commands, " + r.Watches.Count + " watches (F1 or the DEV pill)");
		}

		void Start()
		{
			var s = DevToolsSettings.Current;
			if (!string.IsNullOrWhiteSpace(s.BootScript)) DevTools.RunScript(s.BootScript, "boot (settings)");
			string arg = Arg("-devboot");
			if (!string.IsNullOrWhiteSpace(arg)) DevTools.RunScript(arg, "boot (-devboot)");
			string always = PlayerPrefs.GetString(DevToolsKeys.BootAlways, "");
			if (!string.IsNullOrWhiteSpace(always)) DevTools.RunScript(always, "boot (always)");
			string once = PlayerPrefs.GetString(DevToolsKeys.BootOnce, "");
			if (!string.IsNullOrWhiteSpace(once))
			{
				PlayerPrefs.DeleteKey(DevToolsKeys.BootOnce);
				PlayerPrefs.Save();
				DevTools.RunScript(once, "boot (once)");
			}
		}

		void OnDestroy()
		{
			if (Instance != this) return;
			var r = DevTools.Registry;
			r.CommandException -= Debug.LogException;
			r.Executed -= LogEntry;
			Instance = null;
		}

		void Update() => DevTools.Tick();

		static void LogEntry(DevLogEntry e) =>
			(e.Ok ? (Action<object>)Debug.Log : Debug.LogWarning)("[DevTools] " + e.Line + " -> " + (e.Ok ? "" : "error: ") + e.Message);

		static void OnLog(string message, string stack, LogType type)
		{
			if (IsError(type)) ErrorCount++;
			else if (type == LogType.Warning) WarningCount++;
			else LogCount++;
			_logs.Add(new LogRecord { Id = ++_nextLogId, Time = Time.realtimeSinceStartup, Type = type, Message = message ?? "", Stack = stack ?? "" });
			if (_logs.Count > MaxLogRecords) _logs.RemoveRange(0, _logs.Count - MaxLogRecords);
			LogVersion++;
		}

		/// <summary>DreamTech.DevTools itself and every loaded assembly that references it.</summary>
		static IEnumerable<Assembly> ModuleAssemblies()
		{
			var core = typeof(DevTools).Assembly;
			string coreName = core.GetName().Name;
			foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
			{
				if (asm == core) { yield return asm; continue; }
				AssemblyName[] refs;
				try { refs = asm.GetReferencedAssemblies(); }
				catch { continue; }
				if (refs.Any(a => a.Name == coreName)) yield return asm;
			}
		}

		public static string Arg(string name)
		{
			string[] args = Environment.GetCommandLineArgs();
			for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
			return null;
		}

		// ---- frame helpers for modules ---------------------------------------------------------------------------------

		/// <summary>Runs a coroutine on the host (modules that need frames: screenshots, frame stepping).</summary>
		public static Coroutine Run(IEnumerator routine) => Ensure().StartCoroutine(routine);
	}
}
#endif
