using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DreamTech.DevTools
{
	/// <summary>The standard commands and watches of every port an adapter implements (installed by DevTools.Install).</summary>
	sealed class StandardPortsModule : IDevModule
	{
		public static readonly Type[] PortTypes =
		{
			typeof(ICurrencyPort), typeof(IInventoryPort), typeof(ILevelPort), typeof(IClockPort), typeof(IAdsPort),
			typeof(IRemoteConfigPort), typeof(IExperimentPort), typeof(ISaveSourcePort),
		};

		public const string Economy = "Economy", Level = "Level", Ads = "Ads", Config = "Remote config", Experiments = "Experiments", Save = "Save";
		static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
		readonly object _adapter;

		public StandardPortsModule(object adapter)
		{
			_adapter = adapter;
		}

		public void Register(DevRegistry r)
		{
			if (_adapter is ICurrencyPort currency) RegisterCurrency(r, currency);
			if (_adapter is IInventoryPort inventory) RegisterInventory(r, inventory);
			if (_adapter is ILevelPort level) RegisterLevel(r, level);
			if (_adapter is IClockPort clock) ClockCommands.Register(r, clock, "Time: " + clock.Name, clock.Name);
			if (_adapter is IAdsPort ads) RegisterAds(r, ads);
			if (_adapter is IRemoteConfigPort config) RegisterConfig(r, config);
			if (_adapter is IExperimentPort experiments) RegisterExperiments(r, experiments);
			if (_adapter is ISaveSourcePort save) RegisterSave(r, save);
		}

		static IList<string> Safe(Func<IReadOnlyList<string>> f)
		{
			try { return f()?.ToList() ?? new List<string>(); }
			catch { return new List<string>(); }
		}

		// ---- currency / inventory ----------------------------------------------------------------------------------------

		static void RegisterCurrency(DevRegistry r, ICurrencyPort p)
		{
			var ids = Safe(() => p.CurrencyIds);
			for (int i = 0; i < ids.Count; i++)
			{
				string id = ids[i];
				r.Watch(Economy, id, () => p.GetBalance(id).ToString("N0", Inv), pinned: i == 0);
			}
			IList<string> Ids() => Safe(() => p.CurrencyIds);
			string first = ids.FirstOrDefault() ?? "";
			r.Action(Economy, "Set balance", new[] { DevParam.Choice("currency", Ids, first), DevParam.Int("amount", 99999) }, a =>
			{
				string id = Known(Ids(), a.Str(0), "currency");
				p.SetBalance(id, Math.Max(0, a.Long(1)));
				return DevResult.Success(id + " = " + p.GetBalance(id));
			}, "Absolute balance, through the game's wallet.").Quick = true;
			r.Action(Economy, "Add balance", new[] { DevParam.Choice("currency", Ids, first), DevParam.Int("amount", 1000) }, a =>
			{
				string id = Known(Ids(), a.Str(0), "currency");
				p.SetBalance(id, Math.Max(0, p.GetBalance(id) + a.Long(1)));
				return DevResult.Success(id + " = " + p.GetBalance(id));
			}, "Negative amounts remove (not below 0).");
			r.Action(Economy, "Set all balances", new[] { DevParam.Int("amount", 0) }, a =>
			{
				foreach (string id in Ids()) p.SetBalance(id, Math.Max(0, a.Long(0)));
				return DevResult.Success(string.Join(", ", Ids().Select(id => id + " " + p.GetBalance(id))));
			}, "0 = broke player (purchase / ad offers), a big number = rich player.");
		}

		static void RegisterInventory(DevRegistry r, IInventoryPort p)
		{
			IList<string> Ids() => Safe(() => p.ItemIds);
			r.Watch(Economy, "Items", () => string.Join("  ", Ids().Select(id => id + " " + p.GetCount(id))));
			string first = Ids().FirstOrDefault() ?? "";
			r.Action(Economy, "Set item", new[] { DevParam.Choice("item", Ids, first), DevParam.Int("count", 99) }, a =>
			{
				string id = Known(Ids(), a.Str(0), "item");
				p.SetCount(id, Math.Max(0, a.Long(1)));
				return DevResult.Success(id + " = " + p.GetCount(id));
			}).Quick = true;
			r.Action(Economy, "Add item", new[] { DevParam.Choice("item", Ids, first), DevParam.Int("count", 5) }, a =>
			{
				string id = Known(Ids(), a.Str(0), "item");
				p.SetCount(id, Math.Max(0, p.GetCount(id) + a.Long(1)));
				return DevResult.Success(id + " = " + p.GetCount(id));
			});
			r.Action(Economy, "Set all items", new[] { DevParam.Int("count", 99) }, a =>
			{
				foreach (string id in Ids()) p.SetCount(id, Math.Max(0, a.Long(0)));
				return DevResult.Success(string.Join(", ", Ids().Select(id => id + " " + p.GetCount(id))));
			});
		}

		static string Known(IList<string> ids, string id, string what)
		{
			var hit = ids.FirstOrDefault(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
			if (hit == null) throw new FormatException("unknown " + what + " '" + id + "'; known: " + string.Join(", ", ids));
			return hit;
		}

		// ---- level ------------------------------------------------------------------------------------------------------

		static void RegisterLevel(DevRegistry r, ILevelPort p)
		{
			Func<string> needPlaying = () => p.IsPlaying ? null : "needs a running level";
			r.Watch(Level, "Level", () => p.CurrentLevel.ToString(Inv), pinned: true);
			r.Watch(Level, "Status", () => (p.IsPlaying ? "playing" : "not playing") + (string.IsNullOrEmpty(p.StatusText) ? "" : " · " + p.StatusText));
			r.Condition("playing", () => p.IsPlaying);
			r.Condition("not-playing", () => !p.IsPlaying);

			r.Action(Level, "Win", () =>
			{
				int level = p.CurrentLevel;
				p.Win();
				return DevResult.Success("win requested on level " + level);
			}, "Victory through the normal result flow.", needPlaying).With(quick: true, style: DevCommandStyle.Positive);
			r.Action(Level, "Lose", () =>
			{
				int level = p.CurrentLevel;
				p.Lose();
				return DevResult.Success("defeat requested on level " + level);
			}, "Defeat through the normal flow (revive offers included).", needPlaying).With(quick: true, style: DevCommandStyle.Danger);
			r.Action(Level, "Restart", () =>
			{
				p.Restart();
				return DevResult.Success("restarted level " + p.CurrentLevel);
			}, blocked: needPlaying).With(style: DevCommandStyle.Warning);
			r.Action(Level, "Jump to", new[] { DevParam.Int("level", 10) }, a =>
			{
				if (a.Int(0) < 1) return DevResult.Fail("level must be >= 1");
				p.JumpTo(a.Int(0));
				return DevResult.Success("level " + p.CurrentLevel);
			}).Quick = true;
			r.Action(Level, "Next", () =>
			{
				p.JumpTo(p.CurrentLevel + 1);
				return DevResult.Success("level " + p.CurrentLevel);
			});
			r.Action(Level, "Previous", () =>
			{
				p.JumpTo(Math.Max(1, p.CurrentLevel - 1));
				return DevResult.Success("level " + p.CurrentLevel);
			});
		}

		// ---- ads / config / experiments / save --------------------------------------------------------------------------

		static void RegisterAds(DevRegistry r, IAdsPort p)
		{
			r.Watch(Ads, "Status", () => p.StatusText ?? "");
			r.Action(Ads, "Show rewarded", () =>
			{
				p.ShowRewarded(ok => r.Post("ads.show-rewarded", ok, ok ? "rewarded: completed" : "rewarded: failed / skipped"));
				return DevResult.Success("requested (result in the log)");
			});
			r.Action(Ads, "Show interstitial", () =>
			{
				p.ShowInterstitial(ok => r.Post("ads.show-interstitial", ok, ok ? "interstitial: shown" : "interstitial: not shown"));
				return DevResult.Success("requested (result in the log)");
			});
		}

		static void RegisterConfig(DevRegistry r, IRemoteConfigPort p)
		{
			IList<string> Keys() => Safe(() => p.Keys);
			r.Watch(Config, "Keys", () => Keys().Count.ToString(Inv));
			r.Action(Config, "Show values", new[] { DevParam.Text("filter", "") }, a =>
			{
				var sb = new StringBuilder();
				foreach (string k in Keys().Where(k => k.IndexOf(a.Str(0), StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(k => k, StringComparer.Ordinal))
					sb.Append(k).Append(" = ").Append(p.GetValue(k)).Append('\n');
				return sb.Length == 0 ? DevResult.Fail("no key contains '" + a.Str(0) + "'") : DevResult.Success(sb.ToString());
			});
			r.Action(Config, "Override", new[] { DevParam.Choice("key", Keys), DevParam.Text("value", "") }, a =>
			{
				p.SetOverride(a.Str(0), a.Str(1));
				return DevResult.Success(a.Str(0) + " = " + p.GetValue(a.Str(0)));
			}, "Local value that wins over remote config until cleared. Code that cached the value may need a restart.").Quick = true;
			r.Action(Config, "Clear override", new[] { DevParam.Choice("key", Keys) }, a =>
			{
				p.SetOverride(a.Str(0), null);
				return DevResult.Success(a.Str(0) + " = " + p.GetValue(a.Str(0)));
			});
			r.Action(Config, "Clear all overrides", () =>
			{
				p.ClearOverrides();
				return DevResult.Success("remote values restored");
			});
		}

		static void RegisterExperiments(DevRegistry r, IExperimentPort p)
		{
			IList<string> Pairs()
			{
				var list = new List<string>();
				foreach (string e in Safe(() => p.Experiments))
					foreach (string g in Safe(() => p.GetGroups(e)))
						list.Add(e + " = " + g);
				return list;
			}
			r.Watch(Experiments, "Groups", () => string.Join(", ", Safe(() => p.Experiments).Select(e => e + " " + (p.GetCurrentGroup(e) ?? "-"))));
			r.Action(Experiments, "Force group", new[] { DevParam.Choice("experiment = group", Pairs) }, a =>
			{
				string v = a.Str(0);
				int eq = v.IndexOf('=');
				if (eq <= 0) return DevResult.Fail("expected 'experiment = group'");
				string e = v.Substring(0, eq).Trim(), g = v.Substring(eq + 1).Trim();
				p.ForceGroup(e, g);
				return DevResult.Success(e + " -> " + g + " (current: " + (p.GetCurrentGroup(e) ?? "-") + ")");
			}, "Whether it applies now or at the next start is up to the game.").Quick = true;
			r.Action(Experiments, "Clear forced groups", () =>
			{
				p.ClearForcedGroups();
				return DevResult.Success("normal assignment");
			});
		}

		static void RegisterSave(DevRegistry r, ISaveSourcePort p)
		{
			r.InspectRoots(() => p.SaveObjects);
			r.Watch(Save, "Save objects", () => string.Join(", ", p.SaveObjects.Select(kv => kv.Key)));
			r.Action(Save, "Save now", () =>
			{
				p.SaveNow();
				return DevResult.Success("saved");
			}, "Writes the game's save to disk now (normally on pause / quit).").Quick = true;
		}
	}

	/// <summary>Time-travel commands over any <see cref="IClockPort"/> (the built-in DevClock, or a game's own clock).</summary>
	static class ClockCommands
	{
		static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

		public static void Register(DevRegistry r, IClockPort clock, string category, string idPrefix)
		{
			string slug = DevRegistry.Slug(idPrefix);
			r.Watch(category, clock.Name, () => clock.Now.ToString("yyyy-MM-dd HH:mm:ss ddd", Inv) + (clock.Offset == TimeSpan.Zero ? "" : "  (offset " + Format(clock.Offset) + ")"));
			r.Action(category, "Advance days", new[] { DevParam.Int("days", 1) }, a => Shift(clock, TimeSpan.FromDays(a.Int(0))),
				"Moves the clock forward (negative = back). Day checks that run at launch need a restart.", id: slug + ".advance-days").Quick = true;
			r.Action(category, "Advance hours", new[] { DevParam.Int("hours", 1) }, a => Shift(clock, TimeSpan.FromHours(a.Int(0))), id: slug + ".advance-hours");
			r.Action(category, "Advance minutes", new[] { DevParam.Int("minutes", 10) }, a => Shift(clock, TimeSpan.FromMinutes(a.Int(0))), id: slug + ".advance-minutes");
			r.Action(category, "Just before midnight", new[] { DevParam.Int("seconds", 10) }, a =>
			{
				var now = clock.Now;
				return Shift(clock, now.Date.AddDays(1).AddSeconds(-Math.Max(1, a.Int(0))) - now);
			}, "N seconds before 00:00: watch the live day change.", id: slug + ".just-before-midnight");
			r.Action(category, "Reset clock", () =>
			{
				clock.Offset = TimeSpan.Zero;
				return DevResult.Success(clock.Name + " = real time");
			}, "Data written 'in the future' keeps its day keys: wipe the save if a feature looks stuck.", id: slug + ".reset");
		}

		static DevResult Shift(IClockPort clock, TimeSpan by)
		{
			clock.Offset += by;
			return DevResult.Success(clock.Name + " " + clock.Now.ToString("yyyy-MM-dd HH:mm", Inv) + " (offset " + Format(clock.Offset) + ")");
		}

		public static string Format(TimeSpan t) =>
			(t < TimeSpan.Zero ? "-" : "+") + (int)Math.Abs(t.TotalDays) + "d " + Math.Abs(t.Hours) + "h " + Math.Abs(t.Minutes) + "m";
	}
}
