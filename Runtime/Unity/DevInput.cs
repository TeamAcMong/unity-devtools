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

		/// <summary>A press (finger down / left mouse button down) that started this frame, in screen pixels (origin bottom-left).</summary>
		public static bool TryGetPressThisFrame(out Vector2 screenPosition)
		{
#if ENABLE_LEGACY_INPUT_MANAGER
			for (int i = 0; i < Input.touchCount; i++)
			{
				var touch = Input.GetTouch(i);
				if (touch.phase != TouchPhase.Began) continue;
				screenPosition = touch.position;
				return true;
			}
			if (Input.GetMouseButtonDown(0))
			{
				screenPosition = Input.mousePosition;
				return true;
			}
#elif DREAMTECH_DEVTOOLS_INPUTSYSTEM && ENABLE_INPUT_SYSTEM
			var pointer = UnityEngine.InputSystem.Pointer.current;
			if (pointer != null && pointer.press.wasPressedThisFrame)
			{
				screenPosition = pointer.position.ReadValue();
				return true;
			}
#endif
			screenPosition = default;
			return false;
		}
	}
}
#endif
