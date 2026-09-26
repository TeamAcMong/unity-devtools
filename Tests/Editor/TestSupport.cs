using System;
using System.Collections.Generic;
using System.Linq;

namespace DreamTech.DevTools.Tests
{
	/// <summary>A game adapter implementing every standard port, backed by plain fields.</summary>
	sealed class FakeGame : ICurrencyPort, IInventoryPort, ILevelPort, IRemoteConfigPort, IExperimentPort, ISaveSourcePort, IAdsPort
	{
		public readonly Dictionary<string, long> Wallet = new Dictionary<string, long> { { "coins", 100 }, { "gems", 5 } };
		public readonly Dictionary<string, long> Items = new Dictionary<string, long> { { "bomb", 1 }, { "hint", 0 } };
		public int Level = 3;
		public bool Playing = true;
		public int Wins, Losses, Restarts, Saves;
		public readonly Dictionary<string, string> Remote = new Dictionary<string, string> { { "lives_max", "5" }, { "ad_cd", "30" } };
		public readonly Dictionary<string, string> Overrides = new Dictionary<string, string>();
		public readonly Dictionary<string, string> Forced = new Dictionary<string, string>();
		public readonly PlayerSave Save = new PlayerSave();

		public IReadOnlyList<string> CurrencyIds => Wallet.Keys.ToList();
		public long GetBalance(string id) => Wallet[id];
		public void SetBalance(string id, long amount) => Wallet[id] = amount;

		public IReadOnlyList<string> ItemIds => Items.Keys.ToList();
		public long GetCount(string id) => Items[id];
		public void SetCount(string id, long count) => Items[id] = count;

		public int CurrentLevel => Level;
		public bool IsPlaying => Playing;
		public string StatusText => "wave 2";
		public void Win() { Wins++; Level++; Playing = false; }
		public void Lose() { Losses++; Playing = false; }
		public void Restart() { Restarts++; Playing = true; }
		public void JumpTo(int level) => Level = level;

		public IReadOnlyList<string> Keys => Remote.Keys.ToList();
		public string GetValue(string key) => Overrides.TryGetValue(key, out var v) ? v : Remote[key];
		public void SetOverride(string key, string value)
		{
			if (value == null) Overrides.Remove(key);
			else Overrides[key] = value;
		}
		public void ClearOverrides() => Overrides.Clear();

		public IReadOnlyList<string> Experiments => new[] { "shop_layout" };
		public IReadOnlyList<string> GetGroups(string experiment) => new[] { "a", "b" };
		public string GetCurrentGroup(string experiment) => Forced.TryGetValue(experiment, out var g) ? g : "a";
		public void ForceGroup(string experiment, string group) => Forced[experiment] = group;
		public void ClearForcedGroups() => Forced.Clear();

		public IEnumerable<KeyValuePair<string, object>> SaveObjects => new[] { new KeyValuePair<string, object>("PlayerSave", Save) };
		public void SaveNow() => Saves++;

		string IAdsPort.StatusText => "ready";
		public void ShowRewarded(Action<bool> completed) => completed(true);
		public void ShowInterstitial(Action<bool> completed) => completed(false);
	}

	public enum Difficulty
	{
		Easy,
		Hard
	}

	public sealed class PlayerSave
	{
		public int Coins = 10;
		public bool Tutorial = true;
		public Difficulty Difficulty = Difficulty.Easy;
		public List<int> Stars = new List<int> { 3, 2, 1 };
		public Dictionary<string, int> Flags = new Dictionary<string, int> { { "intro", 1 } };
		public Nested Stats = new Nested();
		public string ReadOnlyName => "fixed";
	}

	public sealed class Nested
	{
		public float Accuracy = 0.5f;
	}

	[DevModule(5000)]
	sealed class DiscoveredTestModule : IDevModule
	{
		public void Register(DevRegistry r) => r.Action("Test discovery", "Marked", () => DevResult.Success("marked"));
	}

	sealed class UnmarkedTestModule : IDevModule
	{
		public void Register(DevRegistry r) => r.Action("Test discovery", "Unmarked", () => DevResult.Success("unmarked"));
	}
}
