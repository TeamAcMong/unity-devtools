#if UNITY_EDITOR || DEVELOPMENT_BUILD || DREAMTECH_DEVTOOLS
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace DreamTech.DevTools.Unity
{
	/// <summary>
	/// Clean screen for videos and screenshots: hide the game's UI (all of it, one canvas, or groups the game names) and the
	/// DEV pill. Canvases are hidden by <c>Canvas.enabled</c>, never <c>SetActive</c>: the UI keeps its Update / tweens and
	/// comes back exactly as it was. Showing again only restores what this module hid, never what the game hid on its own.
	/// Games add named groups with <see cref="DevCreative.AddGroup"/>.
	/// </summary>
	[DevModule(850)]
	sealed class CreativeModule : IDevModule
	{
		public void Register(DevRegistry r)
		{
			r.Toggle(DevCreative.Category, "Game UI", () => !DevCreative.AllHidden, on => DevCreative.SetAllHidden(!on),
				"Every canvas and UI Toolkit document of the game (the dev tools excepted). Off = hidden.").With(quick: true);
			r.Action(DevCreative.Category, "Show / hide one canvas", new[] { DevParam.Choice("canvas", DevCreative.CanvasNames) },
				a => DevCreative.ToggleCanvas(a.Str(0)), "Root canvases of the loaded scenes, by name.");
			r.Toggle(DevCreative.Category, "DEV pill", () => !DevToolsHud.IsHidden, on => DevToolsHud.SetHidden(!on),
				"Hide the dev tools themselves. Bring them back with quick taps in the top-left corner or a multi-finger tap.");
			r.Action(DevCreative.Category, "Show everything again", () =>
			{
				DevCreative.RestoreAll();
				return DevResult.Success("restored what the dev tools had hidden");
			});
		}
	}

	/// <summary>Hide / show of game UI for clean captures; also the API a game uses to name its own UI groups.</summary>
	public static class DevCreative
	{
		public const string Category = "Creative";

		/// <summary>What this module disabled, so "show" restores exactly that (and nothing the game hid itself).</summary>
		static readonly HashSet<Behaviour> HiddenBehaviours = new HashSet<Behaviour>();
		static readonly HashSet<UIDocument> HiddenDocuments = new HashSet<UIDocument>();
		static readonly Dictionary<string, HashSet<Behaviour>> HiddenByGroup = new Dictionary<string, HashSet<Behaviour>>();

		public static bool AllHidden { get; private set; }

		/// <summary>
		/// Adds a "Show &lt;name&gt;" switch to the Creative tab. <paramref name="targets"/> is asked each time the switch is used,
		/// so it can follow scene changes (return the Canvas / Behaviour components of the group: top HUD, booster bar...).
		/// Returns the handle that removes the switch.
		/// </summary>
		public static IDisposable AddGroup(string name, Func<IEnumerable<Behaviour>> targets, string help = null)
		{
			var owner = new object();
			var command = DevTools.Registry.Toggle(Category, "Show " + name, () => !HiddenByGroup.ContainsKey(name), on =>
			{
				if (on) ShowGroup(name);
				else HideGroup(name, targets);
			}, help);
			command.Owner = owner;
			return new Removal(owner, name);
		}

		sealed class Removal : IDisposable
		{
			readonly object _owner;
			readonly string _name;

			public Removal(object owner, string name)
			{
				_owner = owner;
				_name = name;
			}

			public void Dispose()
			{
				ShowGroup(_name);
				DevTools.Registry.Remove(_owner);
			}
		}

		static void HideGroup(string name, Func<IEnumerable<Behaviour>> targets)
		{
			var hidden = new HashSet<Behaviour>();
			IEnumerable<Behaviour> found;
			try { found = targets?.Invoke() ?? Enumerable.Empty<Behaviour>(); }
			catch (Exception e)
			{
				Debug.LogWarning("[DevTools] creative group '" + name + "': " + e.Message);
				return;
			}
			foreach (var behaviour in found)
			{
				if (behaviour == null || !behaviour.enabled) continue;
				behaviour.enabled = false;
				hidden.Add(behaviour);
			}
			HiddenByGroup[name] = hidden;
		}

		static void ShowGroup(string name)
		{
			if (!HiddenByGroup.TryGetValue(name, out var hidden)) return;
			foreach (var behaviour in hidden)
				if (behaviour != null) behaviour.enabled = true;
			HiddenByGroup.Remove(name);
		}

		/// <summary>Root canvases of the loaded scenes (the dev tools draw with UI Toolkit, so none of them is ours).</summary>
		static IEnumerable<Canvas> RootCanvases() => FindActive<Canvas>().Where(c => c.isRootCanvas);

		static IEnumerable<UIDocument> GameDocuments() => FindActive<UIDocument>().Where(d => d.GetComponentInParent<DevToolsHud>() == null);

		static T[] FindActive<T>() where T : UnityEngine.Object
		{
#if UNITY_6000_5_OR_NEWER
			return UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Exclude);
#elif UNITY_2023_1_OR_NEWER
			return UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
			return UnityEngine.Object.FindObjectsOfType<T>(false);
#endif
		}

		public static IList<string> CanvasNames() =>
			RootCanvases().Select(c => c.name).Concat(HiddenBehaviours.OfType<Canvas>().Where(c => c != null).Select(c => c.name))
				.Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

		public static void SetAllHidden(bool hidden)
		{
			if (hidden == AllHidden) return;
			if (!hidden)
			{
				RestoreAll();
				return;
			}
			foreach (var canvas in RootCanvases())
			{
				if (!canvas.enabled) continue;
				canvas.enabled = false;
				HiddenBehaviours.Add(canvas);
			}
			foreach (var document in GameDocuments())
			{
				var root = document.rootVisualElement;
				if (root == null || root.resolvedStyle.display == DisplayStyle.None) continue;
				root.style.display = DisplayStyle.None;
				HiddenDocuments.Add(document);
			}
			AllHidden = true;
		}

		public static DevResult ToggleCanvas(string name)
		{
			var hiddenCanvas = HiddenBehaviours.OfType<Canvas>().FirstOrDefault(c => c != null && c.name == name);
			if (hiddenCanvas != null)
			{
				hiddenCanvas.enabled = true;
				HiddenBehaviours.Remove(hiddenCanvas);
				return DevResult.Success("shown " + name);
			}
			var canvas = RootCanvases().FirstOrDefault(c => c.name == name);
			if (canvas == null) return DevResult.Fail("no root canvas named '" + name + "'");
			if (!canvas.enabled) return DevResult.Fail("'" + name + "' is hidden by the game itself");
			canvas.enabled = false;
			HiddenBehaviours.Add(canvas);
			return DevResult.Success("hidden " + name);
		}

		/// <summary>Shows everything this module hid: canvases, UI Toolkit documents and every named group.</summary>
		public static void RestoreAll()
		{
			foreach (var behaviour in HiddenBehaviours)
				if (behaviour != null) behaviour.enabled = true;
			HiddenBehaviours.Clear();
			foreach (var document in HiddenDocuments)
				if (document != null && document.rootVisualElement != null) document.rootVisualElement.style.display = StyleKeyword.Null;
			HiddenDocuments.Clear();
			foreach (string name in HiddenByGroup.Keys.ToList()) ShowGroup(name);
			AllHidden = false;
		}
	}
}
#endif
