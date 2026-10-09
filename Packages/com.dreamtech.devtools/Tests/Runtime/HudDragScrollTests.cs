using System.Collections;
using System.Reflection;
using DreamTech.DevTools.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace DreamTech.DevTools.Tests
{
	/// <summary>
	/// Synthetic pointer events through the HUD's UI Toolkit panel (mouse and touch typed): a drag scrolls and never presses what
	/// is under the finger, a tap still presses. Needs a graphics device (unity-run.py ... --graphics).
	/// </summary>
	[Category("DevTools.UI")]
	public sealed class HudDragScrollTests
	{
		const string TestCategory = "DragTest";
		object _owner;
		GameObject _eventSystem;
		int _ran;

		[UnitySetUp]
		public IEnumerator SetUp()
		{
			if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) Assert.Ignore("the HUD panel needs a graphics device: run tools/unity-run.py test playmode --graphics");
			DevToolsHost.Ensure();
			if (EventSystem.current == null) _eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
			_owner = new object();
			_ran = 0;
			var r = DevTools.Registry;
			for (int i = 0; i < 30; i++) r.Action(TestCategory, "Probe " + i, () => { _ran++; return DevResult.Success("ok"); });
			DevToolsHud.Scale = 2.5f; // few logical pixels: the tab strip surely overflows
			Assert.IsTrue(DevToolsHud.ShowTab(TestCategory));
			for (int i = 0; i < 12; i++) yield return null;
		}

		[TearDown]
		public void TearDown()
		{
			DevToolsHud.Scale = 1f;
			DevToolsHud.Open(false);
			if (_eventSystem != null) Object.Destroy(_eventSystem);
		}

		static VisualElement Root() => Object.FindFirstObjectByType<UIDocument>().rootVisualElement;

		/// <summary>Sends a pointer event of the given kind straight to an element, the way the panel's event handler would.</summary>
		static void Send<T>(VisualElement element, Vector2 panelPosition, string pointerType, int pointerId) where T : PointerEventBase<T>, new()
		{
			var e = PointerEventBase<T>.GetPooled();
			void Set(string property, object value)
			{
				var p = typeof(PointerEventBase<T>).GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				p?.GetSetMethod(true)?.Invoke(e, new[] { value });
			}
			Set("pointerId", pointerId);
			Set("pointerType", pointerType);
			Set("isPrimary", true); // a real finger is primary (UI Toolkit's ScrollView only touch-scrolled primary pointers)
			Set("position", (Vector3)panelPosition);
			Set("localPosition", (Vector3)element.WorldToLocal(panelPosition));
			Set("button", 0);
			Set("pressedButtons", typeof(T) == typeof(PointerUpEvent) ? 0 : 1);
			e.target = element;
			element.SendEvent(e);
			e.Dispose();
		}

		static void Drag(VisualElement start, Vector2 delta, string pointerType, int pointerId)
		{
			Vector2 from = start.worldBound.center;
			Send<PointerDownEvent>(start, from, pointerType, pointerId);
			for (int i = 1; i <= 8; i++) Send<PointerMoveEvent>(start, from + delta * (i / 8f), pointerType, pointerId);
			Send<PointerUpEvent>(start, from + delta, pointerType, pointerId);
		}

		static void Tap(VisualElement start, string pointerType, int pointerId)
		{
#if !UNITY_6000_0_OR_NEWER
			// 2022.3 buttons take a mouse press from the compatibility MouseDown / MouseUp events, which these synthetic pointer events do not produce
			if (pointerType == "mouse") Assert.Ignore("mouse taps are only checked on Unity 6");
#endif
			Vector2 at = start.worldBound.center;
			Send<PointerDownEvent>(start, at, pointerType, pointerId);
			Send<PointerUpEvent>(start, at, pointerType, pointerId);
		}

		static VisualElement OtherTab(VisualElement root)
		{
			var tabs = root.Q<DevScrollView>("dt-tabs");
			foreach (var child in tabs.Children())
				if (!child.ClassListContains("dt-tab--on") && tabs.worldBound.Contains(child.worldBound.center)) return child;
			return null;
		}

		static readonly object[] PointerKinds = { new object[] { "mouse", 0 }, new object[] { "touch", 1 } };

		[UnityTest]
		public IEnumerator DraggingTheTabStripScrollsAndDoesNotSwitchTab([ValueSource(nameof(PointerKinds))] object[] kind)
		{
			var root = Root();
			var tabs = root.Q<DevScrollView>("dt-tabs");
			Assert.Greater(tabs.HighValue, 10f, "the tab strip must overflow for this test");
			var tab = OtherTab(root);
			Assert.IsNotNull(tab);
			float before = tabs.scrollOffset.x;
			float direction = before < tabs.HighValue * 0.5f ? -1f : 1f; // drag towards the side that has room
			Drag(tab, new Vector2(80f * direction, 0f), (string)kind[0], (int)kind[1]);
			yield return null;
			Assert.Greater(Mathf.Abs(tabs.scrollOffset.x - before), 20f, "the drag must scroll the strip (" + kind[0] + ", from " + before + " of " + tabs.HighValue + ")");
			Assert.IsFalse(tab.ClassListContains("dt-tab--on"), "a drag must not select the tab under the finger");
		}

		[UnityTest]
		public IEnumerator TappingATabSwitchesIt([ValueSource(nameof(PointerKinds))] object[] kind)
		{
			var root = Root();
			var tab = OtherTab(root);
			Tap(tab, (string)kind[0], (int)kind[1]);
			yield return null;
			yield return null;
			Assert.IsTrue(tab.ClassListContains("dt-tab--on"), "a tap must select the tab (" + kind[0] + ")");
		}

		[UnityTest]
		public IEnumerator DraggingTheBodyScrollsAndDoesNotRunTheCommand([ValueSource(nameof(PointerKinds))] object[] kind)
		{
			var root = Root();
			var body = root.Q<DevScrollView>("dt-body");
			Assert.Greater(body.HighValue, 10f, "the body must overflow for this test");
			var run = root.Q<Button>(className: "dt-btn--primary");
			Assert.IsNotNull(run);
			Drag(run, new Vector2(0f, -80f), (string)kind[0], (int)kind[1]);
			yield return null;
			Assert.Greater(body.scrollOffset.y, 20f, "the drag must scroll the body");
			Assert.AreEqual(0, _ran, "a drag that started on Run must not run the command");
		}

		[UnityTest]
		public IEnumerator TappingRunRunsTheCommand([ValueSource(nameof(PointerKinds))] object[] kind)
		{
			var root = Root();
			var run = root.Q<Button>(className: "dt-btn--primary");
			Tap(run, (string)kind[0], (int)kind[1]);
			yield return null;
			Assert.AreEqual(1, _ran, "a tap on Run must run the command (" + kind[0] + ")");
		}

		/// <summary>The drag threshold the HUD uses on this screen (DragScrollManipulator is internal).</summary>
		static float Slop(VisualElement element)
		{
			var type = typeof(DevToolsHud).Assembly.GetType("DreamTech.DevTools.Unity.DragScrollManipulator");
			return (float)type.GetMethod("TouchSlop", BindingFlags.Static | BindingFlags.Public).Invoke(null, new object[] { element, 8f });
		}

		[UnityTest]
		public IEnumerator AWobblyTapOnRunStillRunsAndDoesNotScroll()
		{
			var root = Root();
			var body = root.Q<DevScrollView>("dt-body");
			var run = root.Q<Button>(className: "dt-btn--primary");
			float slop = Slop(body);
			float before = body.scrollOffset.y;
			Vector2 at = run.worldBound.center;
			// a finger lands and wobbles a little in both directions, staying under the slop
			Send<PointerDownEvent>(run, at, "touch", 1);
			Send<PointerMoveEvent>(run, at + new Vector2(slop * 0.4f, slop * 0.7f), "touch", 1);
			Send<PointerMoveEvent>(run, at + new Vector2(-slop * 0.3f, -slop * 0.6f), "touch", 1);
			Send<PointerUpEvent>(run, at + new Vector2(0f, slop * 0.5f), "touch", 1);
			yield return null;
			Assert.AreEqual(before, body.scrollOffset.y, 0.01f, "a wobble under the slop must not scroll");
			Assert.AreEqual(1, _ran, "a wobbly tap must still press Run (slop " + slop + ")");
		}

		static void SetSlopOverride(float logical) =>
			typeof(DevToolsHud).Assembly.GetType("DreamTech.DevTools.Unity.DragScrollManipulator")
				.GetField("SlopOverride", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, logical);

		// ---- phone-sized slop --------------------------------------------------------------------------------------------
		// On a phone the DPI slop is ~20 logical px. UI Toolkit's ScrollView started its own touch scrolling at ~10 px (even under
		// a pressed button), turning taps into drags and making the content jump. These reproduce that with a 40 px slop.

		[UnityTest]
		public IEnumerator PhoneSlop_AWobbleOnACardNeverMovesTheBody()
		{
			var root = Root();
			var body = root.Q<DevScrollView>("dt-body");
			var label = root.Q<Label>(className: "dt-card__label");
			SetSlopOverride(40f);
			try
			{
				float before = body.scrollOffset.y;
				Vector2 at = label.worldBound.center;
				Send<PointerDownEvent>(label, at, "touch", 1);
				for (int i = 1; i <= 10; i++)
				{
					Send<PointerMoveEvent>(label, at + new Vector2(0f, -2.5f * i), "touch", 1);
					yield return null;
				}
				Assert.AreEqual(before, body.scrollOffset.y, 0.5f, "25 px under a 40 px slop: nothing may scroll");
				Send<PointerUpEvent>(label, at + new Vector2(0f, -25f), "touch", 1);
				for (int i = 0; i < 10; i++) yield return null;
				Assert.AreEqual(before, body.scrollOffset.y, 0.5f, "nor after the release");
			}
			finally
			{
				SetSlopOverride(-1f);
			}
		}

		[UnityTest]
		public IEnumerator PhoneSlop_AWobblyTapOnRunRunsItAndNothingScrolls()
		{
			var root = Root();
			var body = root.Q<DevScrollView>("dt-body");
			var run = root.Q<Button>(className: "dt-btn--primary");
			SetSlopOverride(40f);
			try
			{
				float before = body.scrollOffset.y;
				Vector2 at = run.worldBound.center;
				Send<PointerDownEvent>(run, at, "touch", 1);
				for (int i = 1; i <= 6; i++)
				{
					Send<PointerMoveEvent>(run, at + new Vector2(1f, -2.5f * i), "touch", 1);
					yield return null;
				}
				Send<PointerUpEvent>(run, at + new Vector2(1f, -15f), "touch", 1);
				yield return null;
				Assert.AreEqual(before, body.scrollOffset.y, 0.5f, "a wobbly tap must not scroll");
				Assert.AreEqual(1, _ran, "a wobbly tap still presses Run");
			}
			finally
			{
				SetSlopOverride(-1f);
			}
		}

		[UnityTest]
		public IEnumerator PhoneSlop_AWobblyTapOnATabSelectsItAndTheStripStaysStill()
		{
			var root = Root();
			var tabs = root.Q<DevScrollView>("dt-tabs");
			var tab = OtherTab(root);
			SetSlopOverride(40f);
			try
			{
				float before = tabs.scrollOffset.x;
				Vector2 at = tab.worldBound.center;
				Send<PointerDownEvent>(tab, at, "touch", 1);
				for (int i = 1; i <= 10; i++)
				{
					Send<PointerMoveEvent>(tab, at + new Vector2(2.5f * i, 1f), "touch", 1);
					yield return null;
				}
				// measured before the release: selecting a tab then scrolls it to the middle on purpose
				Assert.AreEqual(before, tabs.scrollOffset.x, 0.5f, "the strip must not move while the finger wobbles under the slop");
				Send<PointerUpEvent>(tab, at + new Vector2(25f, 1f), "touch", 1);
				yield return null;
				yield return null;
				Assert.IsTrue(tab.ClassListContains("dt-tab--on"), "a wobbly tap still selects the tab");
			}
			finally
			{
				SetSlopOverride(-1f);
			}
		}

		[UnityTest]
		public IEnumerator PhoneSlop_ADragFollowsTheFingerPastTheSlopWithoutJumping()
		{
			var root = Root();
			var body = root.Q<DevScrollView>("dt-body");
			Assert.Greater(body.HighValue, 80f);
			var label = root.Q<Label>(className: "dt-card__label");
			SetSlopOverride(40f);
			try
			{
				float before = body.scrollOffset.y;
				Vector2 at = label.worldBound.center;
				Send<PointerDownEvent>(label, at, "touch", 1);
				for (int i = 1; i <= 20; i++)
				{
					// 41 px crosses the slop, then 30 px more: the body follows by exactly 30
					float travel = i <= 10 ? 4.1f * i : 41f + 3f * (i - 10);
					Send<PointerMoveEvent>(label, at + new Vector2(0f, -travel), "touch", 1);
					yield return null;
				}
				yield return new WaitForSecondsRealtime(0.2f); // the finger stopped: no fling
				Send<PointerUpEvent>(label, at + new Vector2(0f, -71f), "touch", 1);
				for (int i = 0; i < 20; i++) yield return null;
				Assert.AreEqual(30f, body.scrollOffset.y - before, 1.5f, "the body follows the finger past the slop, nothing else adds to it");
			}
			finally
			{
				SetSlopOverride(-1f);
			}
		}

		[UnityTest]
		public IEnumerator TheMouseWheelScrollsTheBody()
		{
			var root = Root();
			var body = root.Q<DevScrollView>("dt-body");
			float before = body.scrollOffset.y;
			var notches = new Event { type = EventType.ScrollWheel, delta = new Vector2(0f, 3f), mousePosition = body.worldBound.center };
			using (var wheel = WheelEvent.GetPooled(notches))
			{
				wheel.target = body;
				body.SendEvent(wheel);
			}
			yield return null;
			Assert.Greater(body.scrollOffset.y, before + 10f, "three notches down scroll the body");
		}

		[UnityTest]
		public IEnumerator ADragStartsScrollingFromWhereItCrossedTheSlop()
		{
			var root = Root();
			var body = root.Q<DevScrollView>("dt-body");
			Assert.Greater(body.HighValue, 80f, "the body must overflow for this test");
			var run = root.Q<Button>(className: "dt-btn--primary");
			float slop = Slop(body);
			float before = body.scrollOffset.y;
			Vector2 at = run.worldBound.center;
			Send<PointerDownEvent>(run, at, "touch", 1);
			Send<PointerMoveEvent>(run, at + new Vector2(0f, -(slop + 1f)), "touch", 1); // crosses the slop: the drag starts here
			Send<PointerMoveEvent>(run, at + new Vector2(0f, -(slop + 1f + 50f)), "touch", 1);
			Send<PointerUpEvent>(run, at + new Vector2(0f, -(slop + 1f + 50f)), "touch", 1);
			yield return null;
			Assert.AreEqual(50f, body.scrollOffset.y - before, 1f, "the content must follow the finger from the crossing point, not jump by the slop");
			Assert.AreEqual(0, _ran);
		}
	}
}
