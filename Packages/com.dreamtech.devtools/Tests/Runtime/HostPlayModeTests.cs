using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DreamTech.DevTools.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace DreamTech.DevTools.Tests
{
	/// <summary>The host boots by itself in play mode and drives the registry; these run with -nographics.</summary>
	public sealed class HostPlayModeTests
	{
		[UnityTest]
		public IEnumerator HostStartsWithBuiltInModules()
		{
			yield return null;
			Assert.IsNotNull(DevToolsHost.Ensure());
			var r = DevTools.Registry;
			Assert.IsTrue(DevTools.IsActive);
			foreach (string id in new[] { "help", "time.advance-days", "engine.time-scale", "logs.show-recent", "hud.show-panel", "data.wipe-save-at-next-start", "inspector.show" })
				Assert.IsNotNull(r.Find(id), id);
		}

		[UnityTest]
		public IEnumerator ScriptsAdvanceOverFrames()
		{
			DevToolsHost.Ensure();
			int n = 0;
			bool gate = false;
			var owner = new object();
			DevTools.Registry.RegisterModule(new Inline(r =>
			{
				r.Action("PM", "Inc", () => DevResult.Success((++n).ToString()));
				r.Condition("pm-gate", () => gate);
			}), owner);
			var run = DevTools.RunScript("pm.inc; waitfor pm-gate 5; pm.inc");
			yield return null;
			yield return null;
			Assert.AreEqual(1, n);
			gate = true;
			for (int i = 0; i < 5 && !run.Finished; i++) yield return null;
			Assert.IsTrue(run.Finished);
			Assert.AreEqual(2, n);
			DevTools.Registry.Remove(owner);
		}

		[UnityTest]
		public IEnumerator TimeScaleCommandChangesTheEngine()
		{
			DevToolsHost.Ensure();
			yield return null;
			Assert.IsTrue(DevTools.Registry.Execute("engine.time-scale 3").Ok);
			Assert.AreEqual(3f, Time.timeScale, 0.001f);
			DevTools.Registry.Execute("engine.time-scale 1");
			Assert.AreEqual(1f, Time.timeScale, 0.001f);
		}

		[UnityTest]
		public IEnumerator ConsoleErrorsAreCounted()
		{
			DevToolsHost.Ensure();
			DevToolsHost.ClearLogs();
			LogAssert.ignoreFailingMessages = true;
			Debug.LogError("devtools test error");
			yield return null;
			LogAssert.ignoreFailingMessages = false;
			Assert.AreEqual(1, DevToolsHost.ErrorCount);
			StringAssert.Contains("devtools test error", DevTools.Registry.Execute("logs.show-recent 1").Message);
		}

		sealed class Inline : IDevModule
		{
			readonly System.Action<DevRegistry> _f;
			public Inline(System.Action<DevRegistry> f) => _f = f;
			public void Register(DevRegistry r) => _f(r);
		}
	}

	/// <summary>
	/// Needs a real UI Toolkit panel, so it needs a graphics device (unity-run.py ... --graphics, or the Test Runner window); the batch
	/// check of the HUD is the player smoke test (tools/player-smoke.py on a development build of the demo).
	/// </summary>
	[Category("DevTools.UI")]
	public sealed class HudPlayModeTests
	{
		[UnityTest]
		public IEnumerator OpenPanelBlocksUiRaycastsUnderIt()
		{
			if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) Assert.Ignore("the HUD panel needs a graphics device: run tools/unity-run.py test playmode --graphics");
			DevToolsHost.Ensure();
			var es = EventSystem.current != null ? null : new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
			Assert.IsTrue(DevToolsHud.ShowTab("quick"));
			for (int i = 0; i < 5; i++) yield return null;
			var point = new Vector2(Screen.width * 0.5f, Screen.height * 0.75f); // panel docks at the top by default
			Assert.IsTrue(DevToolsHud.IsPointerOverHud(point));
			var hits = new List<RaycastResult>();
			EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
			Assert.IsTrue(hits.Count > 0 && !(hits[0].module is UnityEngine.UI.GraphicRaycaster), "topmost hit must be the HUD panel, got: " + (hits.Count > 0 ? hits[0].gameObject.name + " via " + hits[0].module : "none"));
			DevToolsHud.Open(false);
			yield return null;
			yield return null;
			hits.Clear();
			EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
			Assert.IsFalse(hits.Any(h => !(h.module is UnityEngine.UI.GraphicRaycaster)), "closed panel must not take the tap");
			if (es != null) Object.Destroy(es);
		}
	}
}
