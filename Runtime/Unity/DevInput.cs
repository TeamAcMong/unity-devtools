#if UNITY_EDITOR || DEVELOPMENT_BUILD || DREAMTECH_DEVTOOLS
using UnityEngine;

namespace DreamTech.DevTools.Unity
{
	/// <summary>
	/// Touch count from whichever input backend the project uses (legacy Input Manager, or the Input System package when
	/// the legacy one is disabled). Keys are read from IMGUI events instead, which work with both.
	/// </summary>
	static class DevInput
	{
		public static int TouchCount
		{
			get
			{
#if ENABLE_LEGACY_INPUT_MANAGER
				return Input.touchCount;
#elif DREAMTECH_DEVTOOLS_INPUTSYSTEM && ENABLE_INPUT_SYSTEM
				var ts = UnityEngine.InputSystem.Touchscreen.current;
				if (ts == null) return 0;
				int n = 0;
				foreach (var t in ts.touches)
					if (t.press.isPressed) n++;
				return n;
#else
				return 0;
#endif
			}
		}
	}
}
#endif
