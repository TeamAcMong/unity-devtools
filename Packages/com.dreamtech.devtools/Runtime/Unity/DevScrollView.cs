#if UNITY_EDITOR || DEVELOPMENT_BUILD || DREAMTECH_DEVTOOLS
using UnityEngine;
using UnityEngine.UIElements;

namespace DreamTech.DevTools.Unity
{
	/// <summary>
	/// A minimal one-axis scroll container for the HUD: a clipping viewport, a content element moved by translate, mouse-wheel
	/// scrolling and (vertical) a thin position thumb. It has NO touch handling of its own, on purpose: UI Toolkit's ScrollView
	/// starts its own touch scrolling after ~10 px, even under a pressed button, which on a phone turned taps into drags and,
	/// fighting the HUD's drag, made the content jump. Dragging is <see cref="DragScrollManipulator"/>'s job, alone.
	/// </summary>
	public sealed class DevScrollView : VisualElement
	{
		/// <summary>Logical pixels per wheel notch.</summary>
		const float WheelStep = 18f;
		const float MinimumThumbLength = 24f;

		readonly bool _horizontal;
		readonly VisualElement _viewport, _content, _thumb;
		Vector2 _offset;

		public DevScrollView(bool horizontal)
		{
			_horizontal = horizontal;
			AddToClassList("dt-scroll");
			AddToClassList(horizontal ? "dt-scroll--horizontal" : "dt-scroll--vertical");
			_viewport = new VisualElement { name = "dt-scroll-viewport" };
			_viewport.AddToClassList("dt-scroll__viewport");
			_content = new VisualElement { name = "dt-scroll-content" };
			_content.AddToClassList("dt-scroll__content");
			_viewport.hierarchy.Add(_content);
			hierarchy.Add(_viewport);
			if (!horizontal)
			{
				_thumb = new VisualElement { name = "dt-scroll-thumb", pickingMode = PickingMode.Ignore };
				_thumb.AddToClassList("dt-scroll__thumb");
				hierarchy.Add(_thumb);
			}
			// content or viewport resized (new page, rotation, panel size): keep the offset in range and the thumb right
			_viewport.RegisterCallback<GeometryChangedEvent>(_ => scrollOffset = _offset);
			_content.RegisterCallback<GeometryChangedEvent>(_ => scrollOffset = _offset);
			RegisterCallback<WheelEvent>(OnWheel);
		}

		/// <summary>Children added to the scroll view go into the moving content.</summary>
		public override VisualElement contentContainer => _content;

		/// <summary>The clipping area (its size is the visible part of the content).</summary>
		public VisualElement Viewport => _viewport;

		public bool Horizontal => _horizontal;

		/// <summary>Largest offset along the scroll axis: content length minus viewport length, never negative.</summary>
		public float HighValue
		{
			get
			{
				float content = _horizontal ? _content.layout.width : _content.layout.height;
				float viewport = _horizontal ? _viewport.layout.width : _viewport.layout.height;
				if (float.IsNaN(content) || float.IsNaN(viewport)) return 0f;
				return Mathf.Max(0f, content - viewport);
			}
		}

		/// <summary>Scroll position (only the scroll axis is used), clamped to [0, <see cref="HighValue"/>].</summary>
		public Vector2 scrollOffset
		{
			get => _offset;
			set
			{
				float along = Mathf.Clamp(_horizontal ? value.x : value.y, 0f, HighValue);
				_offset = _horizontal ? new Vector2(along, 0f) : new Vector2(0f, along);
				var current = _content.style.translate.value;
				if (!Mathf.Approximately(current.x.value, -_offset.x) || !Mathf.Approximately(current.y.value, -_offset.y)
				    || _content.style.translate.keyword != StyleKeyword.Undefined)
					_content.style.translate = new Translate(new Length(-_offset.x), new Length(-_offset.y), 0f);
				UpdateThumb();
			}
		}

		void OnWheel(WheelEvent e)
		{
			Vector3 delta = e.delta;
			float amount = _horizontal ? (Mathf.Abs(delta.x) > Mathf.Abs(delta.y) ? delta.x : delta.y) : delta.y;
			if (Mathf.Approximately(amount, 0f)) return;
			float before = _horizontal ? _offset.x : _offset.y;
			scrollOffset = _offset + (_horizontal ? new Vector2(amount * WheelStep, 0f) : new Vector2(0f, amount * WheelStep));
			float after = _horizontal ? _offset.x : _offset.y;
			if (!Mathf.Approximately(before, after)) e.StopPropagation(); // at an end the wheel goes on to the parent
		}

		void UpdateThumb()
		{
			if (_thumb == null) return;
			float viewport = _viewport.layout.height, content = _content.layout.height;
			bool scrollable = !float.IsNaN(viewport) && !float.IsNaN(content) && content > viewport + 0.5f;
			_thumb.style.display = scrollable ? DisplayStyle.Flex : DisplayStyle.None;
			if (!scrollable) return;
			float length = Mathf.Max(MinimumThumbLength, viewport * viewport / content);
			float high = HighValue;
			float top = high > 0f ? (viewport - length) * (_offset.y / high) : 0f;
			_thumb.style.height = length;
			_thumb.style.top = _viewport.layout.y + top;
		}
	}
}
#endif
