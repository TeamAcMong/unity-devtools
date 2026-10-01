#if UNITY_EDITOR || DEVELOPMENT_BUILD || DREAMTECH_DEVTOOLS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace DreamTech.DevTools.Unity
{
	/// <summary>
	/// In-game panel built with UI Toolkit (mouse, keyboard and touch; scales with the screen). The package ships its own
	/// UXML / USS / theme under Resources/DreamTechDevTools and creates the PanelSettings at runtime, so no project asset is needed.
	/// <list type="bullet">
	/// <item>DEV pill: drag to move, tap to open; shows fps, pinned watches and the error count of the console.</item>
	/// <item>Tabs: Quick (quick + starred commands, search), one per category, Scenarios (presets), Watch, Console, Log.</item>
	/// <item>Open keys (default F1 and `), hide key (F2) and a multi-finger tap (default 3) from <see cref="DevToolsSettings"/>.</item>
	/// </list>
	/// The panel sorts above every game canvas and takes part in the EventSystem raycasts, so a tap on the HUD never reaches the
	/// game's buttons and <c>EventSystem.IsPointerOverGameObject</c> is true over it. Games with their own raw input can ask
	/// <see cref="IsPointerOverHud"/>.
	/// </summary>
	public sealed partial class DevToolsHud : MonoBehaviour
	{
		const string TabQuick = "★ Quick", TabScenarios = "Scenarios", TabWatch = "Watch", TabConsole = "Console", TabLog = "Log";
		const string ResourceFolder = "DreamTechDevTools/";
		static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
		static readonly float[] Sizes = { 0.42f, 0.62f, 0.9f };
		static readonly float[] SizesLandscape = { 0.6f, 0.8f, 0.96f }; // a wide screen is short: the panel needs a larger share of it

		/// <summary>Sizes and timings that are behaviour, not look (the look lives in the USS theme).</summary>
		static class Tuning
		{
			public const float MinimumPanelHeight = 240f;
			public const float PanelMargin = 6f;
			public const float DefaultPanelAlpha = 1f;
			public const float MinimumPanelAlpha = 0.9f;
			public const float PanelSortingOrder = 32000f;
			public const float FlashSeconds = 0.7f;
			public const float ToastSecondsOk = 3f;
			public const float ToastSecondsError = 6f;
			public const float ConfirmSeconds = 5f;
			public const float RefreshSeconds = 0.2f;
			public const float PillDragThreshold = 6f;
			public const int ToastMaxCharacters = 110;
			public const int PathTokenMinLength = 12;
			public const int MaxLogRows = 150;
			public const int MaxDropdownOptions = 500;
		}

		static DevToolsHud _instance;

		DevRegistry _reg;
		DevToolsSettings _settings;
		bool _open, _hidden;
		string _tab = TabQuick;
		string _search = "";
		int _size = 1;
		bool _dockBottom;
		float _userScale = 1f, _alpha = Tuning.DefaultPanelAlpha;
		Vector2 _pillPosition = new Vector2(0f, 0.55f);
		readonly HashSet<string> _fav = new HashSet<string>();
		readonly Dictionary<DevCommand, string[]> _values = new Dictionary<DevCommand, string[]>();
		DevCommand _commandBeingRun;
		string _consoleLine = "";
		readonly List<string> _history = new List<string>();
		int _historyPosition = -1;
		int _lastTouchCount;

		readonly Dictionary<DevWatch, string> _watchCache = new Dictionary<DevWatch, string>();
		readonly Dictionary<DevWatch, float> _watchDue = new Dictionary<DevWatch, float>();
		float _fps, _fpsAccumulator;
		int _fpsFrames;

		float _scale;
		string _layoutKey;

		// ---- public API ----------------------------------------------------------------------------------------------

		public static bool IsOpen => _instance != null && _instance._open && !_instance._hidden;

		public static bool IsHidden => _instance != null && _instance._hidden;

		/// <summary>Set by screenshot helpers: the HUD draws nothing (and blocks nothing) while true.</summary>
		public static bool SuppressDrawing;

		public static void Open(bool open = true)
		{
			if (_instance == null) return;
			if (open) _instance._hidden = false;
			_instance._open = open;
			if (open) _instance._revealTab = true;
		}

		/// <summary>Opens the panel on a tab: a category name or slug, or quick / scenarios / watch / console / log.</summary>
		public static bool ShowTab(string tab)
		{
			if (_instance == null) return false;
			string t = _instance.Tabs().FirstOrDefault(n => string.Equals(n, tab, StringComparison.OrdinalIgnoreCase)
			                                                || string.Equals(DevRegistry.Slug(n), DevRegistry.Slug(tab), StringComparison.OrdinalIgnoreCase));
			if (t == null) return false;
			_instance._hidden = false;
			_instance._open = true;
			_instance.SelectTab(t, false);
			return true;
		}

		public static IList<string> TabNames() => _instance != null ? _instance.Tabs().Select(DevRegistry.Slug).ToList() : new List<string>();

		public static void SetHidden(bool hidden)
		{
			if (_instance == null) return;
			_instance._hidden = hidden;
			if (hidden) _instance._open = false;
			PlayerPrefs.SetInt(DevToolsKeys.HudHidden, hidden ? 1 : 0);
		}

		public static float Scale
		{
			get => _instance != null ? _instance._userScale : 1f;
			set
			{
				if (_instance == null) return;
				_instance._userScale = Mathf.Clamp(value, 0.5f, 2.5f);
				PlayerPrefs.SetFloat(DevToolsKeys.HudScale, _instance._userScale);
			}
		}

		/// <summary>User opacity of the panel. The panel is never drawn below <c>Tuning.MinimumPanelAlpha</c>, so game text cannot bleed through.</summary>
		public static float Opacity
		{
			get => _instance != null ? _instance._alpha : 1f;
			set
			{
				if (_instance == null) return;
				_instance._alpha = Mathf.Clamp(value, 0.3f, 1f);
				PlayerPrefs.SetFloat(DevToolsKeys.HudAlpha, _instance._alpha);
			}
		}

		/// <summary>True when a screen point (pixels, origin bottom-left) is over the visible HUD.</summary>
		public static bool IsPointerOverHud(Vector2 screen)
		{
			var h = _instance;
			if (h == null || h._hidden || SuppressDrawing || h._root == null || h._root.panel == null) return false;
			Vector2 p = RuntimePanelUtils.ScreenToPanel(h._root.panel, new Vector2(screen.x, Screen.height - screen.y));
			var element = h._open ? h._panelElement : h._pill;
			return element != null && element.resolvedStyle.display != DisplayStyle.None && element.worldBound.Contains(p);
		}

		// ---- lifecycle -----------------------------------------------------------------------------------------------

		void Awake()
		{
			_instance = this;
			_reg = DevTools.Registry;
			_settings = DevToolsSettings.Current;
			foreach (string id in PlayerPrefs.GetString(DevToolsKeys.HudFavorites, "").Split('|'))
				if (id.Length > 0) _fav.Add(id);
			_size = Mathf.Clamp(PlayerPrefs.GetInt(DevToolsKeys.HudSize, 1), 0, Sizes.Length - 1);
			_dockBottom = PlayerPrefs.GetInt(DevToolsKeys.HudDock, 0) == 1;
			_tab = PlayerPrefs.GetString(DevToolsKeys.HudTab, TabQuick);
			_userScale = Mathf.Clamp(PlayerPrefs.GetFloat(DevToolsKeys.HudScale, 1f), 0.5f, 2.5f);
			_alpha = Mathf.Clamp(PlayerPrefs.GetFloat(DevToolsKeys.HudAlpha, Tuning.DefaultPanelAlpha), 0.3f, 1f);
			_hidden = PlayerPrefs.GetInt(DevToolsKeys.HudHidden, _settings.StartHidden ? 1 : 0) == 1;
			var parts = PlayerPrefs.GetString(DevToolsKeys.HudPill, "").Split(',');
			if (parts.Length == 2 && float.TryParse(parts[0], NumberStyles.Float, Inv, out float px) && float.TryParse(parts[1], NumberStyles.Float, Inv, out float py))
				_pillPosition = new Vector2(Mathf.Clamp01(px), Mathf.Clamp01(py));
			_reg.Executed += OnExecuted;
			BuildUi();
		}

		void OnDestroy()
		{
			if (_reg != null) _reg.Executed -= OnExecuted;
			if (_panelSettings != null) Destroy(_panelSettings);
			if (_instance == this) _instance = null;
		}

		void OnExecuted(DevLogEntry e)
		{
			string full = e.Message.Length > 0 ? e.Message : e.Line;
			ShowToast(ShortenForToast(full), e.Ok);
			if (_commandBeingRun != null) Flash(_commandBeingRun, e.Ok);
			_logDirty = true;
		}

		/// <summary>Toast text: long file paths become their file name and the whole thing is capped; the full text stays in the Log tab.</summary>
		static string ShortenForToast(string text)
		{
			var sb = new StringBuilder(text.Length);
			int start = 0;
			while (start < text.Length)
			{
				int end = text.IndexOf(' ', start);
				if (end < 0) end = text.Length;
				int length = end - start;
				int cut = -1;
				if (length >= Tuning.PathTokenMinLength)
					cut = Math.Max(text.LastIndexOf('/', end - 1, length), text.LastIndexOf('\\', end - 1, length));
				if (cut >= start) sb.Append(text, cut + 1, end - cut - 1);
				else sb.Append(text, start, length);
				if (end < text.Length) sb.Append(' ');
				start = end + 1;
			}
			text = sb.ToString().Replace('\n', ' ');
			return text.Length > Tuning.ToastMaxCharacters ? text.Substring(0, Tuning.ToastMaxCharacters - 3) + "..." : text;
		}

		void Update()
		{
			_fpsAccumulator += Time.unscaledDeltaTime;
			_fpsFrames++;
			if (_fpsAccumulator >= 0.5f)
			{
				_fps = _fpsFrames / _fpsAccumulator;
				_fpsAccumulator = 0;
				_fpsFrames = 0;
			}

			int fingers = _settings.ToggleFingers;
			int touches = DevInput.TouchCount;
			if (fingers > 0 && touches == fingers && _lastTouchCount < fingers) SetHidden(!_hidden);
			_lastTouchCount = touches;

			// only what is on screen: pinned watches, plus the open tab's (all of them on the Watch tab), each at its own rate
			float now = Time.realtimeSinceStartup;
			foreach (var w in _reg.Watches)
			{
				bool shown = w.Pinned || (_open && !_hidden && (_tab == TabWatch || _tab == w.Category));
				if (!shown || (_watchDue.TryGetValue(w, out float due) && now < due)) continue;
				_watchDue[w] = now + Mathf.Max(0.05f, w.Interval);
				string v;
				try { v = w.Value(); }
				catch (Exception e) { v = "<" + e.GetType().Name + ">"; }
				_watchCache[w] = v;
			}
			UpdateUi();
		}

		// ---- keyboard (IMGUI events, so it works with either input backend) -------------------------------------------

		void OnGUI()
		{
			var e = Event.current;
			if (e.type != EventType.KeyDown || e.keyCode == KeyCode.None) return;
			bool typing = IsTyping();
			if (e.keyCode == _settings.HideKey && !typing)
			{
				SetHidden(!_hidden);
				e.Use();
				return;
			}
			if (_settings.OpenKeys != null && Array.IndexOf(_settings.OpenKeys, e.keyCode) >= 0 && (!typing || e.keyCode >= KeyCode.F1 && e.keyCode <= KeyCode.F15))
			{
				if (_hidden) SetHidden(false);
				Open(!_open);
				e.Use();
			}
		}

		bool IsTyping()
		{
			if (_root == null || _root.panel == null) return false;
			return _root.panel.focusController?.focusedElement is VisualElement focused && IsTextInput(focused);
		}

		static bool IsTextInput(VisualElement element)
		{
			for (var e = element; e != null; e = e.parent)
				if (e.ClassListContains("unity-base-text-field__input")) return true;
			return false;
		}

		// ---- running -------------------------------------------------------------------------------------------------

		void Run(DevCommand c, params string[] args)
		{
			_root?.panel?.focusController?.focusedElement?.Blur();
			if (args.Length == 0 && c.Params.Length > 0) args = Values(c);
			_commandBeingRun = c; // OnExecuted (synchronous) uses it to flash this card
			try { _reg.Execute(c, args); }
			finally { _commandBeingRun = null; }
		}

		string[] Values(DevCommand c)
		{
			if (!_values.TryGetValue(c, out var v) || v.Length != c.Params.Length)
			{
				v = c.Params.Select(p => p.Default ?? "").ToArray();
				_values[c] = v;
			}
			return v;
		}

		static string Step(string v, long d) => (long.TryParse(v, NumberStyles.Integer, Inv, out long n) ? n + d : d).ToString(Inv);

		static string CommonPrefix(List<string> s)
		{
			string p = s[0];
			foreach (string x in s)
				while (p.Length > 0 && !x.StartsWith(p, StringComparison.OrdinalIgnoreCase)) p = p.Substring(0, p.Length - 1);
			return p;
		}
	}
}
#endif
