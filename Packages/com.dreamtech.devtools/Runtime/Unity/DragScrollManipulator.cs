#if UNITY_EDITOR || DEVELOPMENT_BUILD || DREAMTECH_DEVTOOLS
using UnityEngine;
using UnityEngine.UIElements;

namespace DreamTech.DevTools.Unity
{
	/// <summary>
	/// Drag-to-scroll for a ScrollView with any pointer type (touch, mouse, pen). The built-in touch scrolling only handles
	/// pointers reported as touch, and a child Button captures the pointer on press, so without this a finger drag that starts on
	/// a tab or a Run button never scrolls. It listens in the trickle-down phase: once the pointer moved past
	/// <see cref="DragThreshold"/> along the scroll axis it captures the pointer on the ScrollView (the Button's Clickable loses
	/// the capture, so no click fires), sets the offset absolutely and stops the event so the built-in handling cannot add to it.
	/// A press that does not move is left alone, so taps, dropdowns and text fields behave normally. Inertia uses unscaled time.
	/// </summary>
	sealed class DragScrollManipulator : Manipulator
	{
		/// <summary>Minimum movement (logical panel pixels) along the axis before a press turns into a drag; see <see cref="TouchSlop"/>.</summary>
		public const float DragThreshold = 8f;
		/// <summary>Physical finger slop: a tap on a phone wobbles a few millimetres, which is many logical pixels on a dense screen.</summary>
		const float TouchSlopInches = 0.1f, FallbackDpi = 160f;
		const float InertiaStartSpeed = 900f, InertiaStopSpeed = 40f, InertiaDecayPerSecond = 5.5f, VelocitySmoothing = 0.35f;

		/// <summary>
		/// Drag threshold in logical panel pixels for this panel: the larger of <paramref name="minimumLogical"/> and ~2.5 mm of
		/// finger travel converted through the screen DPI and the panel scale.
		/// </summary>
		public static float TouchSlop(VisualElement element, float minimumLogical)
		{
			float panelWidth = element?.panel?.visualTree?.layout.width ?? 0f;
			if (!(panelWidth > 1f) || Screen.width <= 0) return minimumLogical;
			float physicalPerLogical = Screen.width / panelWidth;
			float dpi = Screen.dpi > 1f ? Screen.dpi : FallbackDpi;
			return Mathf.Max(minimumLogical, dpi * TouchSlopInches / physicalPerLogical);
		}

		readonly bool _horizontal;
		bool _pressed, _dragging;
		int _pointerId;
		Vector2 _startPosition, _startOffset, _lastPosition;
		float _lastTime, _velocity;
		IVisualElementScheduledItem _inertia;
		readonly System.Collections.Generic.List<VisualElement> _watched = new System.Collections.Generic.List<VisualElement>();

		public DragScrollManipulator(bool horizontal)
		{
			_horizontal = horizontal;
		}

		ScrollView Scroll => (ScrollView)target;

		protected override void RegisterCallbacksOnTarget()
		{
			target.RegisterCallback<PointerDownEvent>(OnDown, TrickleDown.TrickleDown);
		}

		protected override void UnregisterCallbacksFromTarget()
		{
			target.UnregisterCallback<PointerDownEvent>(OnDown, TrickleDown.TrickleDown);
			Unwatch();
			_inertia?.Pause();
		}

		float Axis(Vector2 v) => _horizontal ? v.x : v.y;

		float HighValue => _horizontal ? Scroll.horizontalScroller.highValue : Scroll.verticalScroller.highValue;

		void SetOffset(float value)
		{
			value = Mathf.Clamp(value, 0f, Mathf.Max(0f, HighValue));
			Scroll.scrollOffset = _horizontal ? new Vector2(value, Scroll.scrollOffset.y) : new Vector2(Scroll.scrollOffset.x, value);
		}

		void OnDown(PointerDownEvent e)
		{
			_inertia?.Pause();
			if (_pressed || e.button != 0 && e.pointerType != "touch") return;
			// a press inside a text field that already has focus is a text selection, not a scroll
			if (e.target is VisualElement hit && hit.panel?.focusController?.focusedElement is VisualElement focused && IsInside(hit, focused) && IsTextInput(focused)) return;
			_pressed = true;
			_dragging = false;
			_pointerId = e.pointerId;
			_startPosition = _lastPosition = e.position;
			_startOffset = Scroll.scrollOffset;
			_lastTime = Time.unscaledTime;
			_velocity = 0f;
			Watch(e.target as VisualElement);
		}

		/// <summary>
		/// A pressed Button captures the pointer, and a captured pointer's events go to the capturing element only (no trickle /
		/// bubble through the ScrollView). So the handlers sit on every element from the pressed one up to the ScrollView.
		/// </summary>
		void Watch(VisualElement pressed)
		{
			Unwatch();
			for (var element = pressed; element != null; element = element.parent)
			{
				_watched.Add(element);
				element.RegisterCallback<PointerMoveEvent>(OnMove);
				element.RegisterCallback<PointerUpEvent>(OnUp);
				element.RegisterCallback<PointerCancelEvent>(OnCancel);
				if (element == target) break;
			}
			if (!_watched.Contains(target))
			{
				_watched.Add(target);
				target.RegisterCallback<PointerMoveEvent>(OnMove);
				target.RegisterCallback<PointerUpEvent>(OnUp);
				target.RegisterCallback<PointerCancelEvent>(OnCancel);
			}
		}

		void Unwatch()
		{
			foreach (var element in _watched)
			{
				element.UnregisterCallback<PointerMoveEvent>(OnMove);
				element.UnregisterCallback<PointerUpEvent>(OnUp);
				element.UnregisterCallback<PointerCancelEvent>(OnCancel);
			}
			_watched.Clear();
		}

		void OnMove(PointerMoveEvent e)
		{
			if (!_pressed || e.pointerId != _pointerId) return;
			Vector2 position = e.position;
			if (!_dragging)
			{
				Vector2 travel = position - _startPosition;
				float along = Mathf.Abs(Axis(travel)), across = Mathf.Abs(_horizontal ? travel.y : travel.x);
				float slop = TouchSlop(target, DragThreshold);
				if (along < slop)
				{
					// moving mostly across the axis is not a scroll here: let go so the press stays a tap / belongs to the other scroller
					if (across >= slop) Reset();
					return;
				}
				_dragging = true;
				target.CapturePointer(_pointerId); // the pressed child loses the capture: its Clickable never fires
				// start scrolling from here, so the content does not jump by the slop distance
				_startPosition = _lastPosition = position;
				_startOffset = Scroll.scrollOffset;
				_lastTime = Time.unscaledTime;
				_velocity = 0f;
			}
			SetOffset(Axis(_startOffset) - Axis(position - _startPosition));
			float now = Time.unscaledTime;
			float dt = now - _lastTime;
			if (dt > 0.0001f)
			{
				float instant = -Axis(position - _lastPosition) / dt;
				_velocity = Mathf.Lerp(_velocity, instant, VelocitySmoothing);
				_lastTime = now;
			}
			_lastPosition = position;
			e.StopPropagation();
		}

		void OnUp(PointerUpEvent e)
		{
			if (!_pressed || e.pointerId != _pointerId) return;
			bool wasDragging = _dragging;
			Reset();
			if (!wasDragging) return;
			if (target.HasPointerCapture(_pointerId)) target.ReleasePointer(_pointerId);
			e.StopPropagation();
			// a finger that stopped before lifting must not fling; only a real flick does
			if (Time.unscaledTime - _lastTime < 0.05f && Mathf.Abs(_velocity) > InertiaStartSpeed) StartInertia();
		}

		void OnCancel(PointerCancelEvent e)
		{
			if (_pressed && e.pointerId == _pointerId) Reset();
		}

		void Reset()
		{
			_pressed = false;
			_dragging = false;
			Unwatch();
		}

		void StartInertia()
		{
			_inertia?.Pause();
			_inertia = target.schedule.Execute(() =>
			{
				float dt = Time.unscaledDeltaTime;
				float before = Axis(Scroll.scrollOffset);
				SetOffset(before + _velocity * dt);
				_velocity *= Mathf.Exp(-InertiaDecayPerSecond * dt);
				if (Mathf.Abs(_velocity) < InertiaStopSpeed || Mathf.Approximately(Axis(Scroll.scrollOffset), before)) _inertia.Pause();
			}).Every(16);
		}

		static bool IsInside(VisualElement element, VisualElement ancestor)
		{
			for (var e = element; e != null; e = e.parent)
				if (e == ancestor) return true;
			return false;
		}

		static bool IsTextInput(VisualElement element)
		{
			for (var e = element; e != null; e = e.parent)
				if (e.ClassListContains("unity-base-text-field__input")) return true;
			return false;
		}
	}
}
#endif
