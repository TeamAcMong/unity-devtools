using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using DreamTech.DevTools.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace DreamTech.DevTools.Tests
{
	/// <summary>
	/// The DEV pill through synthetic pointer events: a tap opens the panel, a long-press toggles compact / expanded without
	/// opening it, a drag moves it without toggling, and the texts differ between the two modes. Needs a graphics device
	/// (unity-run.py ... --graphics).
	/// </summary>
	[Category("DevTools.UI")]
	public sealed class HudPillTests
	{
		GameObject _eventSystem;
		readonly object _owner = new object();

		sealed class WatchModule : IDevModule
		{
			public void Register(DevRegistry r) => r.Watch("PillTest", "Level", () => "12", pinned: true, interval: 0.05f);
		}

		[UnitySetUp]
		public IEnumerator SetUp()
		{
			if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) Assert.Ignore("the HUD panel needs a graphics device: run tools/unity-run.py test playmode --graphics");
			DevToolsHost.Ensure();
			if (EventSystem.current == null) _eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
			DevToolsHost.ClearLogs();
			DevTools.Registry.RegisterModule(new WatchModule(), _owner);
			DevToolsHud.SetHidden(false);
			DevToolsHud.PillExpanded = false;
			DevToolsHud.Open(false);
			for (int i = 0; i < 12; i++) yield return null;
		}

		[TearDown]
		public void TearDown()
		{
			DevTools.Registry.Remove(_owner);
			DevToolsHud.PillExpanded = false;
			DevToolsHud.Open(false);
			DevToolsHud.ShowQuickCard(false);
			PlayerPrefs.DeleteKey(DevToolsKeys.HudPill);
			DevToolsHost.ClearLogs();
			if (_eventSystem != null) Object.Destroy(_eventSystem);
		}

		static VisualElement Root() => Object.FindFirstObjectByType<UIDocument>().rootVisualElement;

		static VisualElement Pill() => Root().Q("dt-pill");

		static void Send<T>(VisualElement element, Vector2 panelPosition) where T : PointerEventBase<T>, new()
		{
			var e = PointerEventBase<T>.GetPooled();
			void Set(string property, object value)
			{
				var p = typeof(PointerEventBase<T>).GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				p?.GetSetMethod(true)?.Invoke(e, new[] { value });
			}
			Set("pointerId", 1);
			Set("pointerType", "touch");
			Set("position", (Vector3)panelPosition);
			Set("localPosition", (Vector3)element.WorldToLocal(panelPosition));
			Set("button", 0);
			Set("pressedButtons", typeof(T) == typeof(PointerUpEvent) ? 0 : 1);
			e.target = element;
			element.SendEvent(e);
			e.Dispose();
		}

		static IEnumerator Hold(VisualElement pill, float seconds)
		{
			Send<PointerDownEvent>(pill, pill.worldBound.center);
			yield return new WaitForSecondsRealtime(seconds);
			Send<PointerUpEvent>(pill, pill.worldBound.center);
			yield return null;
			yield return null;
		}

		static string TextOf(string name) => Pill().Q<Label>(name).text;

		static bool IsShown(string name) => Pill().Q(name).resolvedStyle.display != DisplayStyle.None;

		[UnityTest]
		public IEnumerator TapOpensTheQuickCardAndASecondTapClosesIt()
		{
			var pill = Pill();
			yield return Hold(pill, 0.05f);
			Assert.IsTrue(DevToolsHud.IsQuickCardOpen, "a short tap must open the quick card");
			Assert.IsFalse(DevToolsHud.IsOpen, "the full panel stays closed");
			Assert.IsFalse(DevToolsHud.PillExpanded);
			yield return Hold(pill, 0.05f);
			Assert.IsFalse(DevToolsHud.IsQuickCardOpen, "tapping the pill again closes the card");
		}

		[UnityTest]
		public IEnumerator LongPressTogglesExpandedAndDoesNotOpenThePanel()
		{
			var pill = Pill();
			yield return Hold(pill, 0.8f);
			Assert.IsTrue(DevToolsHud.PillExpanded, "holding the pill must expand it");
			Assert.IsFalse(DevToolsHud.IsOpen, "releasing a long-press must not open the panel");
			Assert.AreEqual(1, PlayerPrefs.GetInt(DevToolsKeys.HudPillExpanded, 0), "the choice is remembered");
			Assert.IsFalse(pill.ClassListContains("dt-pill--holding"), "the hold cue goes away on release");

			yield return Hold(pill, 0.8f);
			Assert.IsFalse(DevToolsHud.PillExpanded, "a second long-press goes back to compact");
			Assert.IsFalse(DevToolsHud.IsOpen);
		}

		[UnityTest]
		public IEnumerator HoldingShowsTheCueBeforeItToggles()
		{
			var pill = Pill();
			Send<PointerDownEvent>(pill, pill.worldBound.center);
			yield return new WaitForSecondsRealtime(0.3f);
			Assert.IsTrue(pill.ClassListContains("dt-pill--holding"), "the pill swells while held");
			Assert.IsFalse(DevToolsHud.PillExpanded, "but it has not toggled yet");
			Send<PointerUpEvent>(pill, pill.worldBound.center);
			yield return null;
			Assert.IsFalse(pill.ClassListContains("dt-pill--holding"));
		}

		[UnityTest]
		public IEnumerator DragMovesThePillAndDoesNotToggleOrOpen()
		{
			var pill = Pill();
			Vector2 before = pill.worldBound.position;
			Vector2 from = pill.worldBound.center;
			Send<PointerDownEvent>(pill, from);
			for (int i = 1; i <= 8; i++) Send<PointerMoveEvent>(pill, from + new Vector2(60f, 80f) * (i / 8f));
			yield return new WaitForSecondsRealtime(0.7f); // longer than a long-press: a drag must still not count as one
			Send<PointerUpEvent>(pill, from + new Vector2(60f, 80f));
			yield return null;
			yield return null;
			Vector2 after = pill.worldBound.position;
			Assert.Greater((after - before).magnitude, 30f, "the pill must follow the finger");
			Assert.IsFalse(DevToolsHud.PillExpanded, "a drag must not toggle the mode");
			Assert.IsFalse(DevToolsHud.IsOpen, "a drag must not open the panel");
			Assert.IsFalse(DevToolsHud.IsQuickCardOpen, "a drag must not open the quick card");
		}

		[UnityTest]
		public IEnumerator AfterADragThePillSlidesToTheNearerSideEdge()
		{
			var pill = Pill();
			Vector2 from = pill.worldBound.center;
			// drag it well into the right half: it must end on the right edge
			Vector2 to = new Vector2(Root().worldBound.width * 0.7f, from.y + 20f);
			Send<PointerDownEvent>(pill, from);
			for (int i = 1; i <= 8; i++) Send<PointerMoveEvent>(pill, Vector2.Lerp(from, to, i / 8f));
			Send<PointerUpEvent>(pill, to);
			yield return new WaitForSecondsRealtime(0.4f);
			float rightGap = Root().worldBound.xMax - pill.worldBound.xMax;
			Assert.Less(rightGap, Root().worldBound.width * 0.1f, "the pill must end at the right edge (gap " + rightGap + ")");
		}

		[UnityTest]
		public IEnumerator CompactShowsOnlyTheFpsExpandedAddsTheWatches()
		{
			yield return new WaitForSecondsRealtime(0.6f);
			Assert.IsTrue(Regex.IsMatch(TextOf("dt-pill-text"), @"^\d+$"), "compact pill text is just the fps number, got '" + TextOf("dt-pill-text") + "'");
			Assert.IsFalse(IsShown("dt-pill-detail"), "no watches in the compact pill");
			Assert.IsFalse(IsShown("dt-pill-badge"), "no badge without errors");
			float compactWidth = Pill().resolvedStyle.width;

			DevToolsHud.PillExpanded = true;
			yield return new WaitForSecondsRealtime(0.6f);
			Assert.IsTrue(IsShown("dt-pill-detail"));
			StringAssert.Contains("Level 12", TextOf("dt-pill-detail"));
			Assert.Greater(Pill().resolvedStyle.width, compactWidth + 20f, "the expanded pill is wider");
			// stays inside the screen after growing
			Rect safe = Root().worldBound;
			Assert.LessOrEqual(Pill().worldBound.xMax, safe.xMax + 0.5f, "the pill must stay inside the panel");
		}

		[UnityTest]
		public IEnumerator ErrorsShowARedBadgeCappedAt99Plus()
		{
			LogAssert.Expect(LogType.Error, "pill test error");
			Debug.LogError("pill test error");
			yield return new WaitForSecondsRealtime(0.6f);
			Assert.IsTrue(IsShown("dt-pill-badge"));
			Assert.AreEqual("1", TextOf("dt-pill-badge"));
			Assert.IsTrue(Pill().ClassListContains("dt-pill--error"));
			Assert.IsTrue(Regex.IsMatch(TextOf("dt-pill-text"), @"^\d+$"), "the error count lives in the badge, not in the text");

			for (int i = 0; i < 120; i++)
			{
				LogAssert.Expect(LogType.Error, "pill test error");
				Debug.LogError("pill test error");
			}
			yield return new WaitForSecondsRealtime(0.6f);
			Assert.AreEqual("99+", TextOf("dt-pill-badge"));
		}
	}
}
