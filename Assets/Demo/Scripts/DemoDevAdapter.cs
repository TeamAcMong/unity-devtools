using System;
using System.Collections.Generic;
using System.Linq;

namespace DreamTech.DevTools.Demo
{
	/// <summary>
	/// Everything the demo exposes to DevTools, in one class: the standard ports (so the demo gets the same Economy / Level /
	/// Remote config / Experiments / Save / Ads commands as any game) plus an <see cref="IDevModule"/> for what is specific
	/// to this game (daily gift, boosters in a level, presets). A real game writes one of these per project.
	/// </summary>
	public sealed class DemoDevAdapter : ICurrencyPort, IInventoryPort, ILevelPort, IRemoteConfigPort, IExperimentPort, ISaveSourcePort, IAdsPort, IDevModule
	{
		readonly DemoGame _game;

		public DemoDevAdapter(DemoGame game)
		{
			_game = game;
		}

		// ---- ICurrencyPort / IInventoryPort ------------------------------------------------------------------------------

		public IReadOnlyList<string> CurrencyIds { get; } = new[] { "coins", "gems" };

		public long GetBalance(string id) => id == "coins" ? _game.Data.Coins : _game.Data.Gems;

		public void SetBalance(string id, long amount)
		{
			if (id == "coins") _game.Data.Coins = amount;
			else _game.Data.Gems = amount;
			_game.SaveNow();
		}

		public IReadOnlyList<string> ItemIds { get; } = new[] { "freeze", "double" };

		public long GetCount(string id) => id == "freeze" ? _game.Data.Freeze : _game.Data.Double;

		public void SetCount(string id, long count)
		{
			if (id == "freeze") _game.Data.Freeze = count;
			else _game.Data.Double = count;
			_game.SaveNow();
		}

		// ---- ILevelPort -------------------------------------------------------------------------------------------------

		public int CurrentLevel => _game.Data.Level;

		public bool IsPlaying => _game.Playing;

		public string StatusText => _game.Playing ? _game.Taps + "/" + _game.TapsNeeded + " taps, " + _game.TimeLeft.ToString("0.0") + " s" : _game.AwaitingRevive ? "revive offer" : "menu";

		public void Win() => _game.Win();

		public void Lose() => _game.Lose();

		public void Restart() => _game.StartLevel();

		public void JumpTo(int level)
		{
			_game.Data.Level = level;
			_game.SaveNow();
		}

		// ---- IRemoteConfigPort / IExperimentPort ------------------------------------------------------------------------

		public IReadOnlyList<string> Keys => _game.RemoteDefaults.Keys.ToList();

		public string GetValue(string key) => _game.Remote(key);

		public void SetOverride(string key, string value)
		{
			if (value == null) _game.RemoteOverrides.Remove(key);
			else _game.RemoteOverrides[key] = value;
		}

		public void ClearOverrides() => _game.RemoteOverrides.Clear();

		public IReadOnlyList<string> Experiments { get; } = new[] { "tap_button_color" };

		public IReadOnlyList<string> GetGroups(string experiment) => DemoGame.ButtonGroups;

		public string GetCurrentGroup(string experiment) => _game.ButtonGroup;

		public void ForceGroup(string experiment, string group)
		{
			_game.Data.ForcedGroup = group;
			_game.SaveNow(); // the button colour is chosen when the UI is built: applies at the next start
		}

		public void ClearForcedGroups()
		{
			_game.Data.ForcedGroup = "";
			_game.SaveNow();
		}

		// ---- ISaveSourcePort / IAdsPort ---------------------------------------------------------------------------------

		public IEnumerable<KeyValuePair<string, object>> SaveObjects
		{
			get { yield return new KeyValuePair<string, object>("DemoSave", _game.Data); }
		}

		public void SaveNow() => _game.SaveNow();

		string IAdsPort.StatusText => "rewarded " + (DemoAds.RewardedReady ? "ready" : "no fill");

		public void ShowRewarded(Action<bool> completed) => DemoAds.ShowRewarded(completed);

		public void ShowInterstitial(Action<bool> completed) => DemoAds.ShowInterstitial(completed);

		// ---- IDevModule: what only this game has ------------------------------------------------------------------------

		public void Register(DevRegistry r)
		{
			const string Cat = "Demo";
			r.Action(Cat, "Start level", () =>
			{
				_game.StartLevel();
				return DevResult.Success("level " + _game.Data.Level + " started");
			}, blocked: () => _game.Playing ? "already playing" : null);
			r.Action(Cat, "Revive with ad", () =>
			{
				_game.ReviveWithAd();
				return DevResult.Success(_game.LastResult);
			}, "Presses the revive button (rewarded ad; outcome from Ads > Rewarded outcome).", () => _game.AwaitingRevive ? null : "no revive offer on screen");
			r.Watch(Cat, "Daily gift", () => _game.GiftAvailable ? "available" : "claimed today, streak " + _game.Data.GiftStreak);
			r.Action(Cat, "Claim daily gift", () => _game.ClaimGift() ? DevResult.Success("gems " + _game.Data.Gems + ", streak " + _game.Data.GiftStreak) : DevResult.Fail("already claimed today"));
			r.Action(Cat, "Reset daily gift", () =>
			{
				_game.Data.LastGiftDay = "";
				_game.SaveNow();
				return DevResult.Success("gift available again");
			});
			r.Action(Cat, "Set gift streak", new[] { DevParam.Int("days", 6) }, a =>
			{
				_game.Data.GiftStreak = a.Int(0);
				_game.Data.LastGiftDay = DevClock.Today.AddDays(-1).ToString("yyyy-MM-dd");
				_game.SaveNow();
				return DevResult.Success("streak " + a.Int(0) + ", claimed yesterday: today's claim continues it");
			}, "Edge case: the 7-day cap of the gift multiplier.");
			r.Action(Cat, "Use booster", new[] { DevParam.Choice("booster", () => ItemIds.ToList(), "freeze") }, a =>
				_game.UseBooster(a.Str(0)) ? DevResult.Success(a.Str(0) + " used") : DevResult.Fail("not usable now (no level / none left / already active)"),
				blocked: () => _game.Playing ? null : "needs a running level");
			r.Action(Cat, "Tap", new[] { DevParam.Int("times", 1) }, a =>
			{
				for (int i = 0; i < a.Int(0); i++) _game.Tap();
				return DevResult.Success(_game.Taps + " / " + _game.TapsNeeded);
			}, blocked: () => _game.Playing ? null : "needs a running level");

			r.Condition("revive-offer", () => _game.AwaitingRevive);
			r.Preset("Rich player", "economy.set-all-balances 99999\neconomy.set-all-items 99");
			r.Preset("Broke player", "economy.set-all-balances 0\neconomy.set-all-items 0");
			r.Preset("Lose, then fail the revive ad", "demo.start-level\nwait 0.5\nlevel.lose\nwaitfor revive-offer 5\nads.rewarded-outcome ForceFail\ndemo.revive-with-ad\nads.rewarded-outcome Normal");
			r.Preset("Gift tomorrow", "demo.claim-daily-gift\ntime.advance-days 1\ndemo.claim-daily-gift\ntime.reset");
		}
	}
}
