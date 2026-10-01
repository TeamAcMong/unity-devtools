using System;
using System.Collections.Generic;
using System.Linq;

namespace DreamTech.DevTools
{
	/// <summary>
	/// Entry point for game code. Install the game's adapters once, at boot:
	/// <code>
	/// DevTools.Install(new MyGameDevAdapter());   // implements ICurrencyPort, ILevelPort, ... → standard commands
	/// DevTools.Install(new MyGenreModule(board)); // IDevModule → the game's own commands
	/// </code>
	/// Outside the Editor / development builds / DREAMTECH_DEVTOOLS builds nothing activates the tools: Install still records
	/// the commands in memory (cheap, no reflection), but no HUD, window or boot script ever runs them.
	/// </summary>
	public static class DevTools
	{
		static DevRegistry _registry;
		static readonly List<DevScriptRun> _scripts = new List<DevScriptRun>();

		/// <summary>The shared registry (created on first use).</summary>
		public static DevRegistry Registry => _registry ??= new DevRegistry();

		/// <summary>Set by the Unity layer when dev tools are compiled in (Editor, development build, DREAMTECH_DEVTOOLS).</summary>
		public static bool IsActive { get; private set; }

		public static IReadOnlyList<DevScriptRun> RunningScripts => _scripts;

		public static void Activate() => IsActive = true;

		/// <summary>Drops every registration, script and override: a new play session (Editor without domain reload).</summary>
		public static void Reset()
		{
			_registry = null;
			_scripts.Clear();
			IsActive = false;
			DevClock.ResetState();
			DevAdOutcome.Reset();
		}

		// ---- install ---------------------------------------------------------------------------------------------------

		/// <summary>
		/// Installs an adapter: every standard port it implements (<see cref="ICurrencyPort"/>, <see cref="IInventoryPort"/>,
		/// <see cref="ILevelPort"/>, <see cref="IClockPort"/>, <see cref="IAdsPort"/>, <see cref="IRemoteConfigPort"/>,
		/// <see cref="IExperimentPort"/>, <see cref="ISaveSourcePort"/>) gets its commands, and an <see cref="IDevModule"/>
		/// registers its own. Installing the same adapter again replaces it. Dispose the handle to remove everything it added
		/// (e.g. when the scene that owns the adapter unloads).
		/// </summary>
		public static IDisposable Install(object adapter)
		{
			if (adapter == null) throw new ArgumentNullException(nameof(adapter));
			var r = Registry;
			r.Remove(adapter);
			r.RegisterModule(new StandardPortsModule(adapter), adapter);
			if (adapter is IDevModule m) r.RegisterModule(m, adapter);
			return new Handle(adapter);
		}

		/// <summary>Removes what an adapter / module installed.</summary>
		public static void Uninstall(object adapter) => _registry?.Remove(adapter);

		/// <summary>The standard ports an object implements (for diagnostics).</summary>
		public static IEnumerable<string> PortsOf(object adapter) =>
			adapter == null ? Enumerable.Empty<string>() : StandardPortsModule.PortTypes.Where(t => t.IsInstanceOfType(adapter)).Select(t => t.Name);

		sealed class Handle : IDisposable
		{
			object _adapter;

			public Handle(object adapter)
			{
				_adapter = adapter;
			}

			public void Dispose()
			{
				if (_adapter == null) return;
				Uninstall(_adapter);
				_adapter = null;
			}
		}

		// ---- running ---------------------------------------------------------------------------------------------------

		/// <summary>Runs one console line, or a script when the line holds several commands / wait / waitfor.</summary>
		public static DevResult Submit(string line, string source = "console")
		{
			if (DevScriptRun.LooksLikeScript(line))
			{
				RunScript(line, source);
				return DevResult.Success("script started");
			}
			return Registry.Execute(line);
		}

		/// <summary>Runs one console line and returns the result text (for external drivers such as test bots).</summary>
		public static string Execute(string line) => Registry.Execute(line).ToString();

		/// <summary>Starts a script; the Unity host advances it every frame.</summary>
		public static DevScriptRun RunScript(string script, string source = "script")
		{
			var run = new DevScriptRun(Registry, script, source);
			_scripts.Add(run);
			return run;
		}

		public static void CancelScripts()
		{
			foreach (var s in _scripts.ToArray()) s.Cancel();
			_scripts.Clear();
		}

		/// <summary>Advances running scripts (called by the Unity host every frame).</summary>
		public static void Tick()
		{
			if (_scripts.Count == 0) return;
			double now = Registry.Now;
			// Snapshot: a step may start or cancel scripts (tools.cancel-scripts clears the list) while we iterate.
			var snapshot = _scripts.ToArray();
			foreach (var s in snapshot)
				if (s.Finished || s.Tick(now))
					_scripts.Remove(s);
		}
	}
}
