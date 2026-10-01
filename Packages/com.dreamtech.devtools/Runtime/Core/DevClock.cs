using System;

namespace DreamTech.DevTools
{
	/// <summary>
	/// Wall clock with a dev offset: read <see cref="Now"/> / <see cref="UtcNow"/> / <see cref="Today"/> wherever the game
	/// would read DateTime.Now, and day-based features (daily rewards, streaks, timed offers, seasons) become testable by
	/// time travel. Without an offset it returns exactly DateTime.Now, so shipping builds behave as before.
	/// The Unity layer persists the offset between runs (Editor / development builds only), because many day checks run at
	/// launch.
	/// </summary>
	public static class DevClock
	{
		static long _offsetTicks;

		/// <summary>Raised after the offset changed (persistence, games that cache "today").</summary>
		public static event Action OffsetChanged;

		public static TimeSpan Offset => new TimeSpan(_offsetTicks);

		public static DateTime Now => _offsetTicks == 0 || !DevTools.IsActive ? DateTime.Now : DateTime.Now.AddTicks(_offsetTicks);

		public static DateTime UtcNow => _offsetTicks == 0 || !DevTools.IsActive ? DateTime.UtcNow : DateTime.UtcNow.AddTicks(_offsetTicks);

		public static DateTime Today => Now.Date;

		/// <summary>Seconds since the Unix epoch (UTC), offset included.</summary>
		public static long UnixSeconds => (long)(UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;

		public static void SetOffset(TimeSpan offset)
		{
			if (!DevTools.IsActive || offset.Ticks == _offsetTicks) return;
			_offsetTicks = offset.Ticks;
			OffsetChanged?.Invoke();
		}

		/// <summary>Sets the offset without raising <see cref="OffsetChanged"/> (loading a persisted value).</summary>
		public static void RestoreOffset(TimeSpan offset) => _offsetTicks = offset.Ticks;

		internal static void ResetState()
		{
			_offsetTicks = 0;
			OffsetChanged = null;
		}

		/// <summary><see cref="IClockPort"/> view of this clock (the one the Time category drives by default).</summary>
		public static readonly IClockPort Port = new DevClockPort();

		sealed class DevClockPort : IClockPort
		{
			public string Name => "Game clock";

			public DateTime Now => DevClock.Now;

			public TimeSpan Offset
			{
				get => DevClock.Offset;
				set => SetOffset(value);
			}
		}
	}

	public enum DevAdKind
	{
		Rewarded,
		Interstitial,
		Banner
	}

	public enum DevAdMode
	{
		/// <summary>No override: the ad SDK decides.</summary>
		Normal,
		ForceSuccess,
		ForceFail,
		ForceNoFill
	}

	/// <summary>
	/// Forced ad outcomes for play tests. The game's ad wrapper asks before calling the SDK:
	/// <code>
	/// if (DevAdOutcome.TryIntercept(DevAdKind.Rewarded, out bool granted)) { onDone(granted); return; }
	/// bool ready = DevAdOutcome.IsReady(DevAdKind.Rewarded, sdk.IsRewardedReady);
	/// </code>
	/// With every mode Normal (the default, and always in shipping builds) nothing is intercepted.
	/// </summary>
	public static class DevAdOutcome
	{
		static readonly DevAdMode[] _modes = new DevAdMode[3];
		static readonly int[] _requests = new int[3];

		public static DevAdMode Get(DevAdKind kind) => _modes[(int)kind];

		public static void Set(DevAdKind kind, DevAdMode mode)
		{
			if (!DevTools.IsActive) return;
			_modes[(int)kind] = mode;
		}

		public static int Requests(DevAdKind kind) => _requests[(int)kind];

		/// <summary>
		/// True when a forced mode answers the request instead of the SDK: <paramref name="success"/> is the result to
		/// report (ForceSuccess → true; ForceFail / ForceNoFill → false). Counts every request either way.
		/// </summary>
		public static bool TryIntercept(DevAdKind kind, out bool success)
		{
			_requests[(int)kind]++;
			var m = _modes[(int)kind];
			success = m == DevAdMode.ForceSuccess;
			return m != DevAdMode.Normal;
		}

		/// <summary>Availability to show in UI: NoFill reports "not ready", ForceSuccess / ForceFail report "ready".</summary>
		public static bool IsReady(DevAdKind kind, bool sdkReady)
		{
			switch (_modes[(int)kind])
			{
				case DevAdMode.ForceNoFill: return false;
				case DevAdMode.ForceSuccess:
				case DevAdMode.ForceFail: return true;
				default: return sdkReady;
			}
		}

		internal static void Reset()
		{
			Array.Clear(_modes, 0, _modes.Length);
			Array.Clear(_requests, 0, _requests.Length);
		}
	}
}
