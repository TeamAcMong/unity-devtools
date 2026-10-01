#if UNITY_EDITOR || DEVELOPMENT_BUILD || DREAMTECH_DEVTOOLS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DreamTech.DevTools.Unity
{
	/// <summary>
	/// In-game panel (IMGUI: mouse, keyboard and touch; scales with the screen; no asset needed).
	/// <list type="bullet">
	/// <item>DEV pill: drag to move, tap to open; shows fps, pinned watches and the error count of the console.</item>
	/// <item>Tabs: ★ Quick (quick + starred commands, search), one per category, Scenarios (presets), Watch, Console, Log.</item>
	/// <item>Open keys (default F1 and `), hide key (F2) and a multi-finger tap (default 3) from <see cref="DevToolsSettings"/>.</item>
	/// </list>
	/// While it is visible an invisible uGUI graphic on top of every canvas covers the panel / pill, so a tap on the HUD never
	/// reaches the game's buttons, and games that check EventSystem.IsPointerOverGameObject before handling world input are
	/// blocked too. Games with their own raw input can ask <see cref="IsPointerOverHud"/>.
	/// </summary>
	public sealed class DevToolsHud : MonoBehaviour
	{
		const string TabQuick = "★ Quick", TabScenarios = "Scenarios", TabWatch = "Watch", TabConsole = "Console", TabLog = "Log";
		static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
		static readonly float[] Sizes = { 0.42f, 0.62f, 0.9f };

		static DevToolsHud _instance;

		DevRegistry _reg;
		DevToolsSettings _settings;
		bool _open, _hidden;
		string _tab = TabQuick;
		string _search = "";
		Vector2 _scroll, _logScroll, _pickScroll;
		Vector2 _tabScroll;
		Rect _tabStripRect;
		bool _tabDragArmed, _tabDragging;
		float _tabDragStartX, _tabDragScrollStartX;
		int _size = 1;
		bool _dockBottom;
		float _userScale = 1f, _alpha = 0.92f;
		Vector2 _pill = new Vector2(0f, 0.55f);
		readonly HashSet<string> _fav = new HashSet<string>();
		readonly Dictionary<DevCommand, string[]> _values = new Dictionary<DevCommand, string[]>();
		string _toast;
		bool _toastOk;
		float _toastUntil;
		DevCommand _confirm;
		float _confirmUntil;
		DevCommand _pickCmd;
		int _pickParam;
		string _pickFilter = "";
		string _consoleLine = "";
		readonly List<string> _history = new List<string>();
		int _historyPos = -1;
		int _lastTouchCount;

		bool _dragArmed, _dragging;
		Vector2 _dragStart, _pillDragOffset;
		float _dragScrollStart;
		Rect _scrollRect;

		readonly Dictionary<DevWatch, string> _watchCache = new Dictionary<DevWatch, string>();
		readonly Dictionary<DevWatch, float> _watchDue = new Dictionary<DevWatch, float>();
		float _fps, _fpsAcc;
		GUIContent _pillContent;
		float _pillBuiltAt = -1f;
		int _pillErrors = -1;
		readonly Dictionary<RectTransform, Canvas> _canvasOf = new Dictionary<RectTransform, Canvas>();
		int _fpsFrames;

		float _scale;
		Rect _panelRect, _pillRect, _screen;
		RectTransform _blockPanel, _blockPill;

		GUIStyle _box, _btn, _btnOn, _btnOff, _tabBtn, _tabBtnOn, _label, _small, _field, _title, _okText, _errText, _catHeader, _pillStyle, _pillErr;
		int _stylesFor = -1;
		readonly List<Texture2D> _textures = new List<Texture2D>();

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
			_instance._tab = t;
			_instance._scroll = Vector2.zero;
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
				_instance._stylesFor = -1;
			}
		}

		public static float Opacity
		{
			get => _instance != null ? _instance._alpha : 1f;
			set
			{
				if (_instance == null) return;
				_instance._alpha = Mathf.Clamp(value, 0.3f, 1f);
				PlayerPrefs.SetFloat(DevToolsKeys.HudAlpha, _instance._alpha);
				_instance._stylesFor = -1;
			}
		}

		/// <summary>True when a screen point (pixels, origin bottom-left) is over the visible HUD.</summary>
		public static bool IsPointerOverHud(Vector2 screen)
		{
			var h = _instance;
			if (h == null || h._hidden || SuppressDrawing || h._scale <= 0) return false;
			var v = new Vector2(screen.x / h._scale, (Screen.height - screen.y) / h._scale);
			return h._open ? h._panelRect.Contains(v) : h._pillRect.Contains(v);
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
			_alpha = Mathf.Clamp(PlayerPrefs.GetFloat(DevToolsKeys.HudAlpha, 0.92f), 0.3f, 1f);
			_hidden = PlayerPrefs.GetInt(DevToolsKeys.HudHidden, _settings.StartHidden ? 1 : 0) == 1;
			var parts = PlayerPrefs.GetString(DevToolsKeys.HudPill, "").Split(',');
			if (parts.Length == 2 && float.TryParse(parts[0], NumberStyles.Float, Inv, out float px) && float.TryParse(parts[1], NumberStyles.Float, Inv, out float py))
				_pill = new Vector2(Mathf.Clamp01(px), Mathf.Clamp01(py));
			_reg.Executed += OnExecuted;
			BuildBlocker();
		}

		void OnDestroy()
		{
			if (_reg != null) _reg.Executed -= OnExecuted;
			foreach (var t in _textures) if (t != null) Destroy(t);
			if (_instance == this) _instance = null;
		}

		void OnExecuted(DevLogEntry e)
		{
			_toast = e.Message.Length > 0 ? e.Message : e.Line;
			_toastOk = e.Ok;
			_toastUntil = Time.realtimeSinceStartup + (e.Ok ? 3f : 6f);
		}

		void Update()
		{
			_fpsAcc += Time.unscaledDeltaTime;
			_fpsFrames++;
			if (_fpsAcc >= 0.5f)
			{
				_fps = _fpsFrames / _fpsAcc;
				_fpsAcc = 0;
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
			UpdateBlocker();
		}

		// ---- input blocker -------------------------------------------------------------------------------------------

		void BuildBlocker()
		{
			var go = new GameObject("[DevTools Blocker]", typeof(Canvas), typeof(GraphicRaycaster));
			go.transform.SetParent(transform, false);
			var canvas = go.GetComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = short.MaxValue;
			_blockPanel = NewBlock(go.transform, "panel");
			_blockPill = NewBlock(go.transform, "pill");
		}

		static RectTransform NewBlock(Transform parent, string name)
		{
			var go = new GameObject(name, typeof(RectTransform), typeof(Image));
			go.transform.SetParent(parent, false);
			var img = go.GetComponent<Image>();
			img.color = new Color(0, 0, 0, 0);
			img.raycastTarget = true;
			// a transparent mesh culled by its CanvasRenderer gets no depth, and GraphicRaycaster skips depth -1 graphics
			go.GetComponent<CanvasRenderer>().cullTransparentMesh = false;
			var rt = (RectTransform)go.transform;
			rt.anchorMin = rt.anchorMax = Vector2.zero;
			rt.pivot = Vector2.zero;
			go.SetActive(false);
			return rt;
		}

		void UpdateBlocker()
		{
			if (_blockPanel == null) return;
			bool visible = !_hidden && !SuppressDrawing;
			Place(_blockPanel, visible && _open ? _panelRect : Rect.zero);
			Place(_blockPill, visible && !_open ? _pillRect : Rect.zero);
		}

		void Place(RectTransform rt, Rect r)
		{
			bool on = r.width > 0 && _scale > 0;
			if (rt.gameObject.activeSelf != on) rt.gameObject.SetActive(on);
			if (!on) return;
			if (!_canvasOf.TryGetValue(rt, out var canvas) || canvas == null)
				_canvasOf[rt] = canvas = rt.GetComponentInParent<Canvas>();
			float k = canvas != null && canvas.scaleFactor > 0 ? 1f / canvas.scaleFactor : 1f;
			rt.anchoredPosition = new Vector2(r.x * _scale, Screen.height - r.yMax * _scale) * k;
			rt.sizeDelta = new Vector2(r.width * _scale, r.height * _scale) * k;
		}

		// ---- IMGUI ---------------------------------------------------------------------------------------------------

		void OnGUI()
		{
			HandleKeys(Event.current);
			if (_hidden || SuppressDrawing) return;
			float baseScale = Mathf.Min(Screen.width / 540f, Screen.height / 960f);
			if (Screen.width > Screen.height) baseScale = Mathf.Min(Screen.width / 960f, Screen.height / 540f);
			_scale = Mathf.Max(0.5f, baseScale * _userScale);
			int key = Mathf.RoundToInt(_alpha * 100);
			if (_box == null || _stylesFor != key) BuildStyles(key);
			GUI.depth = -1000;
			var prev = GUI.matrix;
			GUI.matrix = Matrix4x4.Scale(new Vector3(_scale, _scale, 1f));
			Rect safe = Screen.safeArea;
			_screen = new Rect(safe.x / _scale, (Screen.height - safe.yMax) / _scale, safe.width / _scale, safe.height / _scale);
			if (_open) DrawPanel();
			else DrawPill();
			GUI.matrix = prev;
		}

		void HandleKeys(Event e)
		{
			if (e.type != EventType.KeyDown || e.keyCode == KeyCode.None) return;
			bool typing = GUIUtility.keyboardControl != 0;
			if (e.keyCode == _settings.HideKey && !typing)
			{
				SetHidden(!_hidden);
				e.Use();
				return;
			}
			if (_settings.OpenKeys != null && Array.IndexOf(_settings.OpenKeys, e.keyCode) >= 0 && (!typing || e.keyCode >= KeyCode.F1 && e.keyCode <= KeyCode.F15))
			{
				if (_hidden) SetHidden(false);
				_open = !_open;
				e.Use();
			}
		}

		void DrawPill()
		{
			int errors = DevToolsHost.ErrorCount;
			var style = errors > 0 ? _pillErr : _pillStyle;
			// Rebuilt a few times per second (or when the error count changes), not on every IMGUI event.
			if (_pillContent == null || errors != _pillErrors || Time.unscaledTime - _pillBuiltAt > 0.25f)
			{
				var sb = new System.Text.StringBuilder("DEV ").Append(_fps.ToString("0", Inv));
				foreach (var w in _reg.Watches)
					if (w.Pinned && _watchCache.TryGetValue(w, out string v))
						sb.Append("  ").Append(w.Label).Append(' ').Append(v);
				if (errors > 0) sb.Insert(0, "! " + errors + " err  ");
				_pillContent = new GUIContent(sb.ToString());
				_pillErrors = errors;
				_pillBuiltAt = Time.unscaledTime;
			}
			var content = _pillContent;
			var size = style.CalcSize(content);
			size.x = Mathf.Min(size.x, _screen.width);
			_pillRect = new Rect(_screen.x + _pill.x * (_screen.width - size.x), _screen.y + _pill.y * (_screen.height - size.y), size.x, size.y);

			var e = Event.current;
			int id = GUIUtility.GetControlID(FocusType.Passive);
			switch (e.GetTypeForControl(id))
			{
				case EventType.MouseDown:
					if (_pillRect.Contains(e.mousePosition))
					{
						GUIUtility.hotControl = id;
						_dragging = false;
						_dragStart = e.mousePosition;
						_pillDragOffset = e.mousePosition - _pillRect.position;
						e.Use();
					}
					break;
				case EventType.MouseDrag:
					if (GUIUtility.hotControl == id)
					{
						if (!_dragging && (e.mousePosition - _dragStart).magnitude > 6f) _dragging = true;
						if (_dragging)
						{
							var p = e.mousePosition - _pillDragOffset;
							_pill = new Vector2(Mathf.Clamp01((p.x - _screen.x) / Mathf.Max(1, _screen.width - size.x)),
								Mathf.Clamp01((p.y - _screen.y) / Mathf.Max(1, _screen.height - size.y)));
						}
						e.Use();
					}
					break;
				case EventType.MouseUp:
					if (GUIUtility.hotControl == id)
					{
						GUIUtility.hotControl = 0;
						if (_dragging) PlayerPrefs.SetString(DevToolsKeys.HudPill, _pill.x.ToString("0.###", Inv) + "," + _pill.y.ToString("0.###", Inv));
						else _open = true;
						_dragging = false;
						e.Use();
					}
					break;
				case EventType.Repaint:
					style.Draw(_pillRect, content, false, false, false, false);
					break;
			}
		}

		List<string> Tabs()
		{
			var tabs = new List<string> { TabQuick };
			tabs.AddRange(_reg.Categories);
			if (_reg.Presets.Count > 0) tabs.Add(TabScenarios);
			tabs.Add(TabWatch);
			tabs.Add(TabConsole);
			tabs.Add(TabLog);
			return tabs;
		}

		void DrawPanel()
		{
			float h = Mathf.Max(240f, _screen.height * Sizes[_size]);
			float y = _dockBottom ? _screen.yMax - h - 4 : _screen.y + 4;
			_panelRect = new Rect(_screen.x + 4, y, _screen.width - 8, h);
			GUI.Box(_panelRect, GUIContent.none, _box);
			GUILayout.BeginArea(new Rect(_panelRect.x + 6, _panelRect.y + 6, _panelRect.width - 12, _panelRect.height - 12));

			GUILayout.BeginHorizontal();
			GUILayout.Label(string.IsNullOrEmpty(_settings.Title) ? "DevTools" : _settings.Title, _title);
			GUILayout.FlexibleSpace();
			GUILayout.Label(_fps.ToString("0", Inv) + " fps" + (DevToolsHost.ErrorCount > 0 ? "  " + DevToolsHost.ErrorCount + " err" : ""), DevToolsHost.ErrorCount > 0 ? _errText : _small, GUILayout.ExpandWidth(false));
			if (GUILayout.Button(_dockBottom ? "^" : "v", _btn, GUILayout.Width(34)))
			{
				_dockBottom = !_dockBottom;
				PlayerPrefs.SetInt(DevToolsKeys.HudDock, _dockBottom ? 1 : 0);
			}
			if (GUILayout.Button(Sizes[_size] < 0.5f ? "S" : Sizes[_size] < 0.8f ? "M" : "L", _btn, GUILayout.Width(34)))
			{
				_size = (_size + 1) % Sizes.Length;
				PlayerPrefs.SetInt(DevToolsKeys.HudSize, _size);
			}
			if (GUILayout.Button("X", _btn, GUILayout.Width(34))) _open = false;
			GUILayout.EndHorizontal();

			var tabs = Tabs();
			if (!tabs.Contains(_tab)) _tab = TabQuick;
			// One-row tab strip that scrolls sideways: finger/mouse drag, mouse wheel, or the ‹ › buttons.
			GUILayout.BeginHorizontal();
			if (GUILayout.Button("‹", _tabBtn, GUILayout.Width(28))) _tabScroll.x = Mathf.Max(0, _tabScroll.x - _tabStripRect.width * 0.6f);
			_tabScroll = GUILayout.BeginScrollView(_tabScroll, false, false, GUIStyle.none, GUIStyle.none, GUILayout.Height(_tabBtn.fixedHeight + 6));
			GUILayout.BeginHorizontal();
			foreach (string t in tabs)
				if (GUILayout.Button(t, t == _tab ? _tabBtnOn : _tabBtn))
				{
					_tab = t;
					_scroll = Vector2.zero;
					_pickCmd = null;
					PlayerPrefs.SetString(DevToolsKeys.HudTab, t);
				}
			GUILayout.EndHorizontal();
			GUILayout.EndScrollView();
			_tabScroll = DragTabStrip(_tabScroll);
			if (GUILayout.Button("›", _tabBtn, GUILayout.Width(28))) _tabScroll.x += _tabStripRect.width * 0.6f;
			GUILayout.EndHorizontal();

			if (_pickCmd != null) DrawPicker();
			else if (_tab == TabConsole) DrawConsole();
			else if (_tab == TabLog) DrawLog();
			else
			{
				if (_tab == TabQuick || _tab == TabWatch)
				{
					GUILayout.BeginHorizontal();
					GUILayout.Label("Search", _small, GUILayout.ExpandWidth(false));
					_search = GUILayout.TextField(_search, _field);
					if (_search.Length > 0 && GUILayout.Button("X", _btn, GUILayout.Width(34)))
					{
						_search = "";
						GUI.FocusControl(null);
					}
					GUILayout.EndHorizontal();
				}
				_scroll = BeginDragScroll(_scroll);
				if (_tab == TabWatch) DrawWatches(null, false);
				else if (_tab == TabQuick) DrawQuick();
				else if (_tab == TabScenarios) DrawScenarios();
				else
				{
					DrawWatches(_tab, false);
					foreach (var c in _reg.InCategory(_tab).ToList()) DrawCommand(c, false);
				}
				_scroll = EndDragScroll(_scroll);
			}

			if (_confirm != null && Time.realtimeSinceStartup < _confirmUntil)
			{
				GUILayout.BeginHorizontal();
				GUILayout.Label("Run '" + _confirm.Label + "'?", _errText);
				if (GUILayout.Button("Yes", _btnOff, GUILayout.Width(70)))
				{
					var c = _confirm;
					_confirm = null;
					Run(c);
				}
				if (GUILayout.Button("No", _btn, GUILayout.Width(70))) _confirm = null;
				GUILayout.EndHorizontal();
			}
			else if (_toast != null && Time.realtimeSinceStartup < _toastUntil)
				GUILayout.Label(_toast.Length > 300 ? _toast.Substring(0, 300) + "..." : _toast, _toastOk ? _okText : _errText);
			GUILayout.EndArea();
		}

		void DrawQuick()
		{
			bool searching = _search.Length > 0;
			var list = searching ? _reg.Search(_search).ToList() : _reg.Commands.Where(c => c.Quick || _fav.Contains(c.Id)).ToList();
			if (!searching)
			{
				DrawWatches(null, true);
				if (list.Count == 0) GUILayout.Label("Star (☆) a command in any tab to keep it here.", _small);
			}
			string cat = null;
			var categoryIndex = new Dictionary<string, int>();
			foreach (var category in _reg.Categories)
				if (!categoryIndex.ContainsKey(category)) categoryIndex[category] = categoryIndex.Count;
			foreach (var c in list.OrderBy(c => categoryIndex.TryGetValue(c.Category, out int index) ? index : -1))
			{
				if (c.Category != cat)
				{
					cat = c.Category;
					GUILayout.Label(cat, _catHeader);
				}
				DrawCommand(c, searching);
			}
		}

		void DrawScenarios()
		{
			foreach (var p in _reg.Presets.ToList())
			{
				GUILayout.BeginHorizontal();
				if (GUILayout.Button(p.Name, _btn)) DevTools.RunScript(p.Script, p.Name);
				GUILayout.EndHorizontal();
				GUILayout.Label("   " + string.Join("; ", DevScriptRun.Split(p.Script)), _small);
			}
			if (DevTools.RunningScripts.Count > 0 && GUILayout.Button("Cancel running scripts (" + DevTools.RunningScripts.Count + ")", _btnOff)) DevTools.CancelScripts();
		}

		void DrawWatches(string category, bool pinnedOnly)
		{
			string cat = null;
			foreach (var w in _reg.Watches)
			{
				if (category != null && w.Category != category) continue;
				if (pinnedOnly && !w.Pinned) continue;
				if (category == null && !pinnedOnly && _search.Length > 0 && (w.Label + " " + w.Category).IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0) continue;
				if (category == null && !pinnedOnly && w.Category != cat)
				{
					cat = w.Category;
					GUILayout.Label(cat, _catHeader);
				}
				GUILayout.BeginHorizontal();
				GUILayout.Label(w.Label, _small, GUILayout.Width(Mathf.Min(170f, _panelRect.width * 0.36f)));
				GUILayout.Label(_watchCache.TryGetValue(w, out string v) ? v : "...", _label);
				GUILayout.EndHorizontal();
			}
		}

		void DrawCommand(DevCommand c, bool showHelp)
		{
			string blocked = _reg.BlockedReason(c);
			bool prevEnabled = GUI.enabled;
			GUILayout.BeginHorizontal();
			if (GUILayout.Button(_fav.Contains(c.Id) ? "★" : "☆", _btn, GUILayout.Width(30)))
			{
				if (!_fav.Remove(c.Id)) _fav.Add(c.Id);
				PlayerPrefs.SetString(DevToolsKeys.HudFavorites, string.Join("|", _fav));
			}
			GUI.enabled = prevEnabled && blocked == null;
			var label = new GUIContent(c.Label, c.Help);
			if (c.Kind == DevCommandKind.Toggle)
			{
				bool on = false;
				try { on = c.State(); }
				catch { }
				if (GUILayout.Button(label, _btn)) Run(c, on ? "off" : "on");
				if (GUILayout.Button(on ? "ON" : "OFF", on ? _btnOn : _btnOff, GUILayout.Width(56))) Run(c, on ? "off" : "on");
			}
			else if (c.Params.Length == 0)
			{
				if (GUILayout.Button(label, _btn)) Ask(c);
			}
			else
			{
				GUILayout.Label(c.Label, _label, GUILayout.Width(Mathf.Min(150f, _panelRect.width * 0.3f)));
				var vals = Values(c);
				for (int i = 0; i < c.Params.Length; i++) DrawParam(c, i, vals);
				if (GUILayout.Button("Run", _btnOn, GUILayout.Width(52))) Ask(c);
			}
			GUI.enabled = prevEnabled;
			GUILayout.EndHorizontal();
			if (blocked != null) GUILayout.Label("   " + blocked, _small);
			else if (showHelp && !string.IsNullOrEmpty(c.Help)) GUILayout.Label("   " + c.Help, _small);
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

		void DrawParam(DevCommand c, int i, string[] vals)
		{
			var p = c.Params[i];
			switch (p.Kind)
			{
				case DevParamKind.Bool:
				{
					bool on = vals[i] == "true";
					if (GUILayout.Button(p.Name + (on ? ": on" : ": off"), on ? _btnOn : _btn, GUILayout.ExpandWidth(false))) vals[i] = on ? "false" : "true";
					break;
				}
				case DevParamKind.Choice:
				{
					string shown = string.IsNullOrEmpty(vals[i]) ? "<" + p.Name + ">" : vals[i];
					if (GUILayout.Button(shown + " v", _btn, GUILayout.MinWidth(80)))
					{
						_pickCmd = c;
						_pickParam = i;
						_pickFilter = "";
						_pickScroll = Vector2.zero;
					}
					break;
				}
				case DevParamKind.Int:
					if (GUILayout.Button("-", _btn, GUILayout.Width(28))) vals[i] = Step(vals[i], -1);
					vals[i] = GUILayout.TextField(vals[i] ?? "", _field, GUILayout.MinWidth(54));
					if (GUILayout.Button("+", _btn, GUILayout.Width(28))) vals[i] = Step(vals[i], +1);
					break;
				default:
					vals[i] = GUILayout.TextField(vals[i] ?? "", _field, GUILayout.MinWidth(60));
					break;
			}
		}

		static string Step(string v, int d) => (long.TryParse(v, NumberStyles.Integer, Inv, out long n) ? n + d : d).ToString(Inv);

		void DrawPicker()
		{
			var p = _pickCmd.Params[_pickParam];
			GUILayout.BeginHorizontal();
			GUILayout.Label(_pickCmd.Label + " · " + p.Name, _catHeader);
			GUILayout.FlexibleSpace();
			bool cancel = GUILayout.Button("Cancel", _btn, GUILayout.Width(80));
			GUILayout.EndHorizontal();
			GUILayout.BeginHorizontal();
			GUILayout.Label("Filter", _small, GUILayout.ExpandWidth(false));
			_pickFilter = GUILayout.TextField(_pickFilter, _field);
			GUILayout.EndHorizontal();
			IList<string> options;
			try { options = p.Options?.Invoke() ?? Array.Empty<string>(); }
			catch (Exception e) { options = new[] { "<" + e.Message + ">" }; }
			string picked = null;
			_pickScroll = BeginDragScroll(_pickScroll);
			int shown = 0;
			foreach (string o in options)
			{
				if (_pickFilter.Length > 0 && o.IndexOf(_pickFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
				if (++shown > 500) break;
				if (GUILayout.Button(o, _btn)) picked = o;
			}
			if (shown == 0) GUILayout.Label("(no values)", _small);
			_pickScroll = EndDragScroll(_pickScroll);
			if (picked != null)
			{
				Values(_pickCmd)[_pickParam] = picked;
				_pickCmd = null;
			}
			else if (cancel) _pickCmd = null;
		}

		void DrawConsole()
		{
			var e = Event.current;
			if (e.type == EventType.KeyDown && GUI.GetNameOfFocusedControl() == "dt.console")
			{
				if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
				{
					SubmitConsole();
					e.Use();
				}
				else if (e.keyCode == KeyCode.UpArrow && _history.Count > 0)
				{
					_historyPos = Mathf.Clamp(_historyPos < 0 ? _history.Count - 1 : _historyPos - 1, 0, _history.Count - 1);
					_consoleLine = _history[_historyPos];
					e.Use();
				}
				else if (e.keyCode == KeyCode.DownArrow && _historyPos >= 0)
				{
					_historyPos++;
					if (_historyPos >= _history.Count)
					{
						_historyPos = -1;
						_consoleLine = "";
					}
					else _consoleLine = _history[_historyPos];
					e.Use();
				}
				else if (e.keyCode == KeyCode.Tab)
				{
					string first = DevRegistry.Tokenize(_consoleLine).FirstOrDefault() ?? "";
					var m = _reg.Complete(first).ToList();
					if (m.Count == 1) _consoleLine = m[0] + " ";
					else if (m.Count > 1) _consoleLine = CommonPrefix(m);
					e.Use();
				}
			}
			GUILayout.BeginHorizontal();
			GUI.SetNextControlName("dt.console");
			_consoleLine = GUILayout.TextField(_consoleLine, _field);
			if (GUILayout.Button("Run", _btnOn, GUILayout.Width(56))) SubmitConsole();
			GUILayout.EndHorizontal();
			GUILayout.Label("help [text] · wait / waitfor for scripts · Tab completes · Up/Down history", _small);
			var tokens = DevRegistry.Tokenize(_consoleLine);
			string head = tokens.FirstOrDefault() ?? "";
			var exact = _reg.Find(head);
			if (exact != null) GUILayout.Label(_reg.Usage(exact) + (string.IsNullOrEmpty(exact.Help) ? "" : "  — " + exact.Help), _small);
			else if (head.Length > 0)
			{
				GUILayout.BeginHorizontal();
				foreach (string s in _reg.Complete(head).Take(4))
					if (GUILayout.Button(s, _btn, GUILayout.ExpandWidth(false))) _consoleLine = s + " ";
				GUILayout.EndHorizontal();
			}
			DrawLog();
		}

		void SubmitConsole()
		{
			string line = _consoleLine.Trim();
			if (line.Length == 0) return;
			_history.Remove(line);
			_history.Add(line);
			_historyPos = -1;
			_consoleLine = "";
			DevTools.Submit(line);
			_logScroll.y = float.MaxValue;
		}

		static string CommonPrefix(List<string> s)
		{
			string p = s[0];
			foreach (string x in s)
				while (p.Length > 0 && !x.StartsWith(p, StringComparison.OrdinalIgnoreCase)) p = p.Substring(0, p.Length - 1);
			return p;
		}

		void DrawLog()
		{
			_logScroll = BeginDragScroll(_logScroll);
			foreach (var e in _reg.Log)
			{
				GUILayout.Label("> " + e.Line, _small);
				if (e.Message.Length > 0) GUILayout.Label(e.Message, e.Ok ? _label : _errText);
			}
			_logScroll = EndDragScroll(_logScroll);
			if (GUILayout.Button("Clear log", _btn, GUILayout.ExpandWidth(false))) _reg.Log.Clear();
		}

		// ---- scrolling that also works by dragging with a finger -----------------------------------------------------

		Vector2 BeginDragScroll(Vector2 scroll) => GUILayout.BeginScrollView(scroll, false, false);

		/// <summary>Horizontal drag + mouse wheel for the tab strip; call right after its EndScrollView.</summary>
		Vector2 DragTabStrip(Vector2 scroll)
		{
			var e = Event.current;
			if (e.type == EventType.Repaint) _tabStripRect = GUILayoutUtility.GetLastRect();
			switch (e.type)
			{
				case EventType.ScrollWheel:
					if (_tabStripRect.Contains(e.mousePosition))
					{
						scroll.x = Mathf.Max(0, scroll.x + (Mathf.Abs(e.delta.x) > Mathf.Abs(e.delta.y) ? e.delta.x : e.delta.y) * 20f);
						e.Use();
					}
					break;
				case EventType.MouseDown:
					if (_tabStripRect.Contains(e.mousePosition))
					{
						_tabDragArmed = true;
						_tabDragging = false;
						_tabDragStartX = e.mousePosition.x;
						_tabDragScrollStartX = scroll.x;
					}
					break;
				case EventType.MouseDrag:
					if (_tabDragArmed)
					{
						float dx = e.mousePosition.x - _tabDragStartX;
						if (!_tabDragging && Mathf.Abs(dx) > 8f)
						{
							_tabDragging = true;
							GUIUtility.hotControl = 0; // cancels the tab under the finger
						}
						if (_tabDragging)
						{
							scroll.x = Mathf.Max(0, _tabDragScrollStartX - dx);
							e.Use();
						}
					}
					break;
				case EventType.MouseUp:
					if (_tabDragging) e.Use();
					_tabDragArmed = false;
					_tabDragging = false;
					break;
			}
			return scroll;
		}

		Vector2 EndDragScroll(Vector2 scroll)
		{
			GUILayout.EndScrollView();
			var e = Event.current;
			if (e.type == EventType.Repaint) _scrollRect = GUILayoutUtility.GetLastRect();
			switch (e.type)
			{
				case EventType.MouseDown:
					if (_scrollRect.Contains(e.mousePosition))
					{
						_dragArmed = true;
						_dragging = false;
						_dragStart = e.mousePosition;
						_dragScrollStart = scroll.y;
					}
					break;
				case EventType.MouseDrag:
					if (_dragArmed)
					{
						float dy = e.mousePosition.y - _dragStart.y;
						if (!_dragging && Mathf.Abs(dy) > 8f)
						{
							_dragging = true;
							GUIUtility.hotControl = 0; // cancels the button under the finger
						}
						if (_dragging)
						{
							scroll.y = Mathf.Max(0, _dragScrollStart - dy);
							e.Use();
						}
					}
					break;
				case EventType.MouseUp:
					if (_dragging) e.Use();
					_dragArmed = false;
					_dragging = false;
					break;
			}
			return scroll;
		}

		// ---- running -------------------------------------------------------------------------------------------------

		void Ask(DevCommand c)
		{
			if (c.Confirm)
			{
				_confirm = c;
				_confirmUntil = Time.realtimeSinceStartup + 5f;
			}
			else Run(c);
		}

		void Run(DevCommand c, params string[] args)
		{
			GUI.FocusControl(null);
			if (args.Length == 0 && c.Params.Length > 0) args = Values(c);
			_reg.Execute(c, args);
		}

		// ---- styles --------------------------------------------------------------------------------------------------

		Texture2D Tex(Color c)
		{
			var t = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
			t.SetPixel(0, 0, c);
			t.Apply();
			_textures.Add(t);
			return t;
		}

		void BuildStyles(int key)
		{
			foreach (var t in _textures) if (t != null) Destroy(t);
			_textures.Clear();
			_stylesFor = key;
			var bg = Tex(new Color(0.08f, 0.09f, 0.11f, _alpha));
			var btn = Tex(new Color(0.22f, 0.24f, 0.28f, 1f));
			var hover = Tex(new Color(0.30f, 0.33f, 0.38f, 1f));
			var on = Tex(new Color(0.16f, 0.52f, 0.30f, 1f));
			var off = Tex(new Color(0.55f, 0.20f, 0.20f, 1f));
			var tab = Tex(new Color(0.16f, 0.17f, 0.20f, 1f));
			var tabOn = Tex(new Color(0.20f, 0.42f, 0.75f, 1f));
			var field = Tex(new Color(0.03f, 0.03f, 0.04f, 1f));
			var pill = Tex(new Color(0.08f, 0.09f, 0.11f, 0.8f));
			var pillErr = Tex(new Color(0.45f, 0.08f, 0.08f, 0.9f));

			_box = new GUIStyle { normal = { background = bg } };
			_btn = Button(btn, hover, tabOn);
			_btnOn = Button(on, on, tabOn);
			_btnOff = Button(off, off, tabOn);
			_tabBtn = Button(tab, hover, tabOn);
			_tabBtnOn = Button(tabOn, tabOn, tabOn);
			foreach (var s in new[] { _tabBtn, _tabBtnOn })
			{
				s.fixedHeight = 30;
				s.stretchWidth = false;
				s.padding = new RectOffset(10, 10, 4, 4);
			}
			_label = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true, normal = { textColor = new Color(0.92f, 0.93f, 0.95f) } };
			_small = new GUIStyle(_label) { fontSize = 12, normal = { textColor = new Color(0.62f, 0.66f, 0.72f) } };
			_title = new GUIStyle(_label) { fontSize = 16, fontStyle = FontStyle.Bold, wordWrap = false };
			_catHeader = new GUIStyle(_label) { fontSize = 13, fontStyle = FontStyle.Bold, margin = new RectOffset(4, 4, 8, 2), normal = { textColor = new Color(0.55f, 0.75f, 1f) } };
			_okText = new GUIStyle(_label) { normal = { textColor = new Color(0.55f, 0.92f, 0.6f) } };
			_errText = new GUIStyle(_label) { normal = { textColor = new Color(1f, 0.55f, 0.5f) } };
			_field = new GUIStyle(GUI.skin.textField) { fontSize = 14, fixedHeight = 30, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(6, 6, 4, 4) };
			_field.normal.background = _field.focused.background = _field.hover.background = _field.active.background = field;
			_field.normal.textColor = _field.focused.textColor = _field.hover.textColor = Color.white;
			_pillStyle = new GUIStyle(_label) { fontSize = 13, wordWrap = false, padding = new RectOffset(10, 10, 6, 6), normal = { background = pill, textColor = new Color(1f, 0.85f, 0.3f) } };
			_pillErr = new GUIStyle(_pillStyle) { normal = { background = pillErr, textColor = Color.white } };
		}

		GUIStyle Button(Texture2D bg, Texture2D hover, Texture2D active)
		{
			var s = new GUIStyle(GUI.skin.button)
			{
				fontSize = 14, fixedHeight = 32, alignment = TextAnchor.MiddleCenter, wordWrap = false,
				margin = new RectOffset(2, 2, 2, 2), padding = new RectOffset(6, 6, 4, 4),
			};
			s.normal.background = bg;
			s.hover.background = hover;
			s.active.background = active;
			s.focused.background = bg;
			s.normal.textColor = s.hover.textColor = s.active.textColor = s.focused.textColor = Color.white;
			return s;
		}
	}
}
#endif
