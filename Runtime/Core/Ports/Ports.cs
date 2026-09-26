using System;
using System.Collections.Generic;

// Standard ports. A game implements the ones it has (one adapter class may implement several) and passes the adapter to
// DevTools.Install: each implemented port gets the same commands and watches in every game, so QA learns them once.
// Ports only grow: new members go into new interfaces, existing signatures change only with a major version.
// Implementations run on the main thread; throwing an exception is fine (the command reports it as a failure).

namespace DreamTech.DevTools
{
	/// <summary>Currencies (coins, gems, energy...). Set should go through the game's wallet so its events and saving run.</summary>
	public interface ICurrencyPort
	{
		IReadOnlyList<string> CurrencyIds { get; }

		long GetBalance(string currencyId);

		void SetBalance(string currencyId, long amount);
	}

	/// <summary>Countable items: boosters, keys, lives, consumables, characters owned (0/1)...</summary>
	public interface IInventoryPort
	{
		IReadOnlyList<string> ItemIds { get; }

		long GetCount(string itemId);

		void SetCount(string itemId, long count);
	}

	/// <summary>Level / stage / wave / mission progression, whatever the genre calls it.</summary>
	public interface ILevelPort
	{
		/// <summary>1-based number of the level the player is on (or would start).</summary>
		int CurrentLevel { get; }

		/// <summary>A level is being played: Win / Lose / Restart are allowed.</summary>
		bool IsPlaying { get; }

		/// <summary>Free text for the watch (state machine state, wave, timer...).</summary>
		string StatusText { get; }

		/// <summary>Ends the running level as a victory through the normal result flow.</summary>
		void Win();

		/// <summary>Ends the running level as a defeat through the normal result flow (revive offers included).</summary>
		void Lose();

		void Restart();

		/// <summary>Makes <paramref name="level"/> the current level (starting it or not is up to the game).</summary>
		void JumpTo(int level);
	}

	/// <summary>
	/// A clock the game reads for day-based features. <see cref="DevClock"/> is the built-in one; install your own when the game
	/// has its own time source (server clock, live-ops clock) so time travel moves that one.
	/// </summary>
	public interface IClockPort
	{
		string Name { get; }

		DateTime Now { get; }

		TimeSpan Offset { get; set; }
	}

	/// <summary>Showing ads on demand. The outcome of ads the game shows by itself is forced with <see cref="DevAdOutcome"/>.</summary>
	public interface IAdsPort
	{
		string StatusText { get; }

		void ShowRewarded(Action<bool> completed);

		void ShowInterstitial(Action<bool> completed);
	}

	/// <summary>Remote config / tuning values.</summary>
	public interface IRemoteConfigPort
	{
		IReadOnlyList<string> Keys { get; }

		string GetValue(string key);

		/// <summary>Local override that wins over the remote value until cleared (null clears one key).</summary>
		void SetOverride(string key, string value);

		void ClearOverrides();
	}

	/// <summary>A/B tests.</summary>
	public interface IExperimentPort
	{
		IReadOnlyList<string> Experiments { get; }

		IReadOnlyList<string> GetGroups(string experiment);

		/// <summary>The group this player is in, or null.</summary>
		string GetCurrentGroup(string experiment);

		/// <summary>Puts the player in a group (the game decides whether it applies now or at the next start).</summary>
		void ForceGroup(string experiment, string group);

		void ClearForcedGroups();
	}

	/// <summary>Save data the Inspector can browse and edit (by reflection), and a way to write it to disk now.</summary>
	public interface ISaveSourcePort
	{
		/// <summary>Name → live save object (edited in place, so keep returning the instances the game uses).</summary>
		IEnumerable<KeyValuePair<string, object>> SaveObjects { get; }

		void SaveNow();
	}
}
