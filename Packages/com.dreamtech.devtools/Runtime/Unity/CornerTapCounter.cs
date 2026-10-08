#if UNITY_EDITOR || DEVELOPMENT_BUILD || DREAMTECH_DEVTOOLS
using UnityEngine;

namespace DreamTech.DevTools.Unity
{
	/// <summary>Counts quick taps in a screen corner: a tap later than the allowed gap after the previous one starts over.</summary>
	public sealed class CornerTapCounter
	{
		int _count;
		float _lastTap = float.NegativeInfinity;

		/// <summary>True when a screen point (pixels, origin bottom-left) is inside the top-left square of the given share of the short side.</summary>
		public static bool IsInCorner(Vector2 screenPosition, int screenWidth, int screenHeight, float regionShare)
		{
			float side = Mathf.Min(screenWidth, screenHeight) * regionShare;
			return screenPosition.x <= side && screenPosition.y >= screenHeight - side;
		}

		/// <summary>Records a tap at <paramref name="time"/> and returns how many taps are in the current run.</summary>
		public int Register(float time, float maximumGapSeconds)
		{
			_count = time - _lastTap <= maximumGapSeconds ? _count + 1 : 1;
			_lastTap = time;
			return _count;
		}

		public void Reset()
		{
			_count = 0;
			_lastTap = float.NegativeInfinity;
		}
	}
}
#endif
