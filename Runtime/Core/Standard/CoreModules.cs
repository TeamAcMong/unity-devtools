using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DreamTech.DevTools
{
	/// <summary>Console helpers: help, scripts, presets.</summary>
	[DevModule(0)]
	sealed class ToolsModule : IDevModule
	{
		public const string Cat = "Tools";

		public void Register(DevRegistry r)
		{
			r.Action(Cat, "Help", new[] { DevParam.Text("filter", "") }, a =>
			{
				var sb = new StringBuilder();
				foreach (var c in r.Search(a.Str(0)))
					sb.Append(r.Usage(c)).Append(string.IsNullOrEmpty(c.Help) ? "" : "  — " + c.Help).Append('\n');
				sb.Append("script built-ins: wait <seconds>; waitfor <").Append(string.Join("|", r.ConditionNames)).Append("> [timeout]");
				return DevResult.Success(sb.ToString());
			}, "Commands with their syntax (optionally filtered).", id: "help");
			r.Action(Cat, "Run script", new[] { DevParam.Text("script", "") }, a =>
			{
				DevTools.RunScript(a.Str(0), "script");
				return DevResult.Success("started");
			}, "Console lines separated by ';' (wait / waitfor allowed).", id: "script");
			r.Action(Cat, "Run preset", new[] { DevParam.Choice("preset", () => r.Presets.Select(p => p.Name).ToList()) }, a =>
			{
				var p = r.Presets.FirstOrDefault(x => string.Equals(x.Name, a.Str(0), StringComparison.OrdinalIgnoreCase));
				if (p == null) return DevResult.Fail("unknown preset '" + a.Str(0) + "'");
				DevTools.RunScript(p.Script, p.Name);
				return DevResult.Success("preset '" + p.Name + "' started");
			}, id: "preset");
			r.Action(Cat, "Cancel scripts", () =>
			{
				int n = DevTools.RunningScripts.Count;
				DevTools.CancelScripts();
				return DevResult.Success(n + " script(s) cancelled");
			});
			r.Watch(Cat, "Running scripts", () => DevTools.RunningScripts.Count == 0 ? "none"
				: string.Join(", ", DevTools.RunningScripts.Select(s => s.Source + " " + (s.StepIndex + 1) + "/" + s.StepCount)));
		}
	}

	/// <summary>Time travel on <see cref="DevClock"/>.</summary>
	[DevModule(100)]
	sealed class TimeModule : IDevModule
	{
		public void Register(DevRegistry r) => ClockCommands.Register(r, DevClock.Port, "Time", "time");
	}

	/// <summary>Forced outcomes of the ads the game shows (see <see cref="DevAdOutcome"/>).</summary>
	[DevModule(110)]
	sealed class AdOutcomeModule : IDevModule
	{
		const string Cat = StandardPortsModule.Ads;

		static IList<string> Modes() => Enum.GetNames(typeof(DevAdMode));

		public void Register(DevRegistry r)
		{
			r.Watch(Cat, "Outcome", () => string.Join(", ", Kinds().Select(k => k + " " + DevAdOutcome.Get(k))));
			r.Watch(Cat, "Requests", () => string.Join(", ", Kinds().Select(k => k + " " + DevAdOutcome.Requests(k))));
			foreach (var kind in Kinds())
			{
				var k = kind;
				var c = r.Action(Cat, k + " outcome", new[] { DevParam.Choice("mode", Modes, DevAdMode.Normal.ToString()) }, a =>
				{
					DevAdOutcome.Set(k, a.Enum<DevAdMode>(0));
					return DevResult.Success(k + " ads: " + DevAdOutcome.Get(k));
				}, "Only for ad calls the game routes through DevAdOutcome. Normal = the SDK decides.");
				if (k == DevAdKind.Rewarded) c.Quick = true;
			}
			r.Action(Cat, "All ads normal", () =>
			{
				foreach (var k in Kinds()) DevAdOutcome.Set(k, DevAdMode.Normal);
				return DevResult.Success("no ad override");
			});
		}

		static IEnumerable<DevAdKind> Kinds() => (DevAdKind[])Enum.GetValues(typeof(DevAdKind));
	}

	/// <summary>Browse and edit registered objects (save data, singletons) by path.</summary>
	[DevModule(900)]
	sealed class InspectorModule : IDevModule
	{
		const string Cat = "Inspector";

		public void Register(DevRegistry r)
		{
			IList<string> Roots() => r.RootNames.ToList();
			r.Action(Cat, "List roots", () => DevResult.Success(string.Join("\n", Roots()).Length == 0 ? "no roots: install an ISaveSourcePort or call DevRegistry.InspectRoot" : string.Join("\n", Roots())));
			r.Action(Cat, "Show", new[] { DevParam.Choice("root", Roots), DevParam.Text("path", "") }, a =>
			{
				if (!r.TryRoot(a.Str(0), out object root)) return DevResult.Fail("unknown root '" + a.Str(0) + "'");
				if (!ObjectInspector.TryResolve(root, a.Str(1), out object v, out _, out string err)) return DevResult.Fail(err);
				return DevResult.Success(ObjectInspector.Dump(v));
			}, "Members of an object; path goes deeper: Stats.Levels[3], Flags[\"tutorial\"].");
			r.Action(Cat, "Set", new[] { DevParam.Text("root.path", ""), DevParam.Text("value", "") }, a =>
			{
				string full = a.Str(0);
				int dot = full.IndexOfAny(new[] { '.', '[' });
				string rootName = dot < 0 ? full : full.Substring(0, dot), path = dot < 0 ? "" : full.Substring(dot).TrimStart('.');
				if (!r.TryRoot(rootName, out object root)) return DevResult.Fail("unknown root '" + rootName + "'");
				if (!ObjectInspector.TrySet(root, path, a.Str(1), out string msg)) return DevResult.Fail(msg);
				return DevResult.Success(msg + " (in memory: use Save now to write it; screens that cached the value refresh when reopened)");
			}, "Writes a number / bool / text / enum in place, e.g. PlayerSave.Coins 500.");
			r.Action(Cat, "Find", new[] { DevParam.Text("text", "") }, a =>
			{
				var lines = new List<string>();
				foreach (string name in Roots())
					if (r.TryRoot(name, out object root)) lines.AddRange(ObjectInspector.Find(name, root, a.Str(0)));
				return lines.Count == 0 ? DevResult.Fail("no member contains '" + a.Str(0) + "'") : DevResult.Success(string.Join("\n", lines));
			}, "Member names containing the text, across every root (first level).");
		}
	}
}
