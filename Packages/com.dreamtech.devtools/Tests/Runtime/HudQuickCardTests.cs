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
	/// The quick card beside the DEV pill (level step / jump, styled win / lose / restart, the way to the full panel) and the
	/// Creative module's hide / restore of game canvases. Needs a graphics device (unity-run.py ... --graphics).
	/// </summary>
	[Category("DevTools.UI")]
	public sealed class HudQuickCardTests
	{
		sealed class FakeLevel : ILevelPort
		{
			public int Level = 7, Wins, Loses, Restarts;
			public int CurrentLevel => Level;
			public bool IsPlaying => true;
			public string StatusText => "";
			public void Win() => Wins++;
			public void Lose() => Loses++;
			public void Restart() => Restarts++;
			public void JumpTo(int level) => Level = level;
		}

		GameObject _eventSystem;
		FakeLevel _level;
		System.IDisposable _handle;

		[UnitySetUp]
		public IEnumerator SetUp()
		{
			if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) Assert.Ignore("the HUD panel needs a graphics device: run tools/unity-run.py test playmode --graphics");
			DevToolsHost.Ensure();
			if (EventSystem.current == null) _eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
			_level = new FakeLevel();
			_handle = DevTools.Install(_level);
			DevToolsHud.SetHidden(false);
			DevToolsHud.Open(false);
			DevToolsHud.ShowQuickCard(true);
			for (int i = 0; i < 12; i++) yield return null;
		}

		[TearDown]
		public void TearDown()
		{
			DevToolsHud.ShowQuickCard(false);
			DevToolsHud.Open(false);
			DevCreative.RestoreAll();
			_handle?.Dispose();
			if (_eventSystem != null) Object.Destroy(_eventSystem);
		}

		static VisualElement Card() => Object.FindFirstObjectByType<UIDocument>().rootVisualElement.Q("dt-quick");

		static Button ButtonWithText(string text)
		{
			foreach (var button in Card().Query<Button>().ToList())
			{
				var label = button.Q<Label>(className: "dt-btn__text");
				if (label != null && label.text == text) return button;
			}
			return null;
		}

		/// <summary>A touch tap straight on the button (touch taps press buttons on both 2022.3 and Unity 6).</summary>
		static void Tap(VisualElement element)
		{
			Vector2 at = element.worldBound.center;
			foreach (var type in new[] { typeof(PointerDownEvent), typeof(PointerUpEvent) })
			{
				var method = typeof(HudQuickCardTests).GetMethod(nameof(Send), BindingFlags.Static | BindingFlags.NonPublic).MakeGenericMethod(type);
				method.Invoke(null, new object[] { element, at });
			}
		}

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
			Set("isPrimary", true);
			Set("position", (Vector3)panelPosition);
			Set("localPosition", (Vector3)element.WorldToLocal(panelPosition));
			Set("button", 0);
			Set("pressedButtons", typeof(T) == typeof(PointerUpEvent) ? 0 : 1);
			e.target = element;
			element.SendEvent(e);
			e.Dispose();
		}

		[UnityTest]
		public IEnumerator TheCardShowsTheLevelAndStyledMatchButtons()
		{
			Assert.IsTrue(DevToolsHud.IsQuickCardOpen);
			var field = LevelField();
			Assert.IsNotNull(field, "the level field");
			Assert.AreEqual("7", field.value, "it shows the current level");
			Assert.IsTrue(ButtonWithText("Win").ClassListContains("dt-btn--positive"), "Win is green");
			Assert.IsTrue(ButtonWithText("Lose").ClassListContains("dt-btn--danger"), "Lose is red");
			Assert.IsTrue(ButtonWithText("Restart").ClassListContains("dt-btn--warning"), "Restart is orange");
			yield return null;
		}

		[UnityTest]
		public IEnumerator WinAndNextRunFromTheCard()
		{
			Tap(ButtonWithText("Win"));
			yield return null;
			Assert.AreEqual(1, _level.Wins, "Win on the card wins");
			var next = Card().Query<Button>(className: "dt-btn--quick-step").Last();
			Tap(next);
			yield return null;
			Assert.AreEqual(8, _level.Level, "the right chevron goes to the next level");
			yield return new WaitForSecondsRealtime(0.3f);
			Assert.AreEqual("8", LevelField().value, "the field follows the level");
		}

		static TextField LevelField() => Card().Q<TextField>("dt-quick-level");

		/// <summary>
		/// The phone bug: the card re-synced the field to the current level every 0.2 s because "is the tester typing" was read
		/// from UI focus, which an OS keyboard does not hold. Once touched, the field must keep what is typed until Go.
		/// </summary>
		[UnityTest]
		public IEnumerator ATypedLevelSurvivesTheLevelSyncAndGoJumpsToIt()
		{
			var field = LevelField();
			Tap(field);
			field.value = "42";
			field.Blur(); // what a phone does: the OS keyboard closes, the field has no UI focus any more
			yield return new WaitForSecondsRealtime(0.8f); // several refresh ticks
			Assert.AreEqual("42", field.value, "the typed level must not be put back to the current one");
			Tap(ButtonWithText("Go"));
			yield return null;
			Assert.AreEqual(42, _level.Level, "Go jumps to the typed level");
			yield return new WaitForSecondsRealtime(0.4f);
			Assert.AreEqual("42", LevelField().value, "after the jump the field follows the level again");
		}

		[UnityTest]
		public IEnumerator GoWithSomethingThatIsNotALevelDoesNotJump()
		{
			var field = LevelField();
			Tap(field);
			field.value = "abc";
			Tap(ButtonWithText("Go"));
			yield return null;
			Assert.AreEqual(7, _level.Level, "no jump");
			var result = Card().Q<Label>(className: "dt-quick__result");
			Assert.IsTrue(result.ClassListContains("dt-quick__result--error"), "the card says why");
		}

		[UnityTest]
		public IEnumerator AllToolsOpensTheFullPanelAndClosesTheCard()
		{
			Tap(ButtonWithText("All tools"));
			yield return null;
			yield return null;
			Assert.IsTrue(DevToolsHud.IsOpen, "the full panel opens");
			Assert.IsFalse(DevToolsHud.IsQuickCardOpen, "the card closes");
		}

		[UnityTest]
		public IEnumerator TheCardStaysInsideTheScreen()
		{
			yield return null;
			var root = Object.FindFirstObjectByType<UIDocument>().rootVisualElement;
			Rect card = Card().worldBound, screen = root.worldBound;
			Assert.GreaterOrEqual(card.xMin, screen.xMin - 0.5f);
			Assert.LessOrEqual(card.xMax, screen.xMax + 0.5f);
			Assert.GreaterOrEqual(card.yMin, screen.yMin - 0.5f);
			Assert.LessOrEqual(card.yMax, screen.yMax + 0.5f);
			Assert.IsTrue(DevToolsHud.IsPointerOverHud(new Vector2(
				card.center.x * Screen.width / screen.width, Screen.height - card.center.y * Screen.height / screen.height)), "taps on the card belong to the HUD");
		}

		[UnityTest]
		public IEnumerator CreativeHidesGameCanvasesAndRestoresOnlyThose()
		{
			var shown = new GameObject("Game HUD", typeof(Canvas)).GetComponent<Canvas>();
			var hiddenByGame = new GameObject("Game popup", typeof(Canvas)).GetComponent<Canvas>();
			hiddenByGame.enabled = false;
			yield return null;
			try
			{
				Assert.IsTrue(DevTools.Registry.Execute("creative.game-ui off").Ok);
				Assert.IsFalse(shown.enabled, "the game's canvas is hidden");
				Assert.IsTrue(DevTools.Registry.Execute("creative.game-ui on").Ok);
				Assert.IsTrue(shown.enabled, "and shown again");
				Assert.IsFalse(hiddenByGame.enabled, "a canvas the game had hidden stays hidden");
			}
			finally
			{
				Object.Destroy(shown.gameObject);
				Object.Destroy(hiddenByGame.gameObject);
			}
		}
	}
}
