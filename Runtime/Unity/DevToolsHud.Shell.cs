#if UNITY_EDITOR || DEVELOPMENT_BUILD || DREAMTECH_DEVTOOLS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace DreamTech.DevTools.Unity
{
	// The shell: UIDocument + PanelSettings, header, tab strip, search slot, toast and the DEV pill.
	public sealed partial class DevToolsHud
	{
		UIDocument _document;
		PanelSettings _panelSettings;
		VisualElement _root, _panelElement, _pill, _pillDot, _searchSlot, _searchBox, _consoleRow;
		Label _title, _stats, _toast, _pillText, _searchPlaceholder;
		Button _dockButton, _sizeButton, _closeButton, _searchClear;
		DevIcon _dockIcon;
		TextField _searchField;
		ScrollView _tabs, _body;
		readonly Dictionary<string, Button> _tabButtons = new Dictionary<string, Button>();
		readonly List<string> _tabsCache = new List<string>();
		string _tabsKey = "";
		bool _revealTab, _contentDirty = true, _logDirty, _wasOpen;
		float _toastUntil, _nextRefresh;
		bool _pillPressed, _pillDragging;
		Vector2 _pillPressPosition, _pillGrab;
		Rect _safeLogical;

		/// <summary>Tab names; rebuilt when the registry changes (categories come and go with modules).</summary>
		List<string> Tabs()
		{
			if (_tabsCache.Count > 0 && _tabsCacheVersion == _reg.Version) return _tabsCache;
			_tabsCacheVersion = _reg.Version;
			_tabsCache.Clear();
			_tabsCache.Add(TabQuick);
			for (int i = 0; i < _reg.Categories.Count; i++) _tabsCache.Add(_reg.Categories[i]);
			if (_reg.Presets.Count > 0) _tabsCache.Add(TabScenarios);
			_tabsCache.Add(TabWatch);
			_tabsCache.Add(TabConsole);
			_tabsCache.Add(TabLog);
			return _tabsCache;
		}

		int _tabsCacheVersion = -1;

		static string TabText(string tab) => tab == TabQuick ? "Quick" : tab;

		// ---- construction --------------------------------------------------------------------------------------------

		void BuildUi()
		{
			// Resources inside the package: works for a git-installed (read-only) package, in the Editor and in player builds.
			var source = Resources.Load<PanelSettings>(ResourceFolder + "DevToolsPanelSettings");
			var tree = Resources.Load<VisualTreeAsset>(ResourceFolder + "DevToolsHud");
			if (source == null || tree == null)
			{
				Debug.LogError("[DevTools] HUD resources not found under Resources/" + ResourceFolder + " (package damaged?)");
				enabled = false;
				return;
			}
			// An instance: the scale is per HUD. It must come from an asset (Unity 6 hands the text engine's ICU data to PanelSettings assets only).
			_panelSettings = Instantiate(source);
			_panelSettings.hideFlags = HideFlags.DontSave;
			_panelSettings.sortingOrder = Tuning.PanelSortingOrder;

			var go = new GameObject("[DevTools UI]");
			go.transform.SetParent(transform, false);
			go.SetActive(false);
			_document = go.AddComponent<UIDocument>();
			_document.panelSettings = _panelSettings;
			go.SetActive(true);

			var documentRoot = _document.rootVisualElement;
			tree.CloneTree(documentRoot);
			_root = documentRoot.Q("dt-root");
			_panelElement = _root.Q("dt-panel");
			_pill = _root.Q("dt-pill");
			_title = _root.Q<Label>("dt-title");
			_stats = _root.Q<Label>("dt-stats");
			_dockButton = _root.Q<Button>("dt-dock");
			_sizeButton = _root.Q<Button>("dt-size");
			_closeButton = _root.Q<Button>("dt-close");
			_tabs = _root.Q<ScrollView>("dt-tabs");
			_searchSlot = _root.Q("dt-search-slot");
			_body = _root.Q<ScrollView>("dt-body");
			_toast = _root.Q<Label>("dt-toast");
			_pillText = _root.Q<Label>("dt-pill-text");
			// the built-in ScrollView drag only handles pointers typed as touch, and a pressed child button swallows the drag
			_tabs.touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped;
			_body.touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped;
			_tabs.AddManipulator(new DragScrollManipulator(true));
			_body.AddManipulator(new DragScrollManipulator(false));

			_title.text = string.IsNullOrEmpty(_settings.Title) ? "DevTools" : _settings.Title;
			BuildHeader();
			BuildPill();
			BuildSearchBox();
			_toast.AddToClassList("dt-toast--hidden");
		}

		void BuildHeader()
		{
			_dockIcon = new DevIcon(DevIcon.Shape.ChevronDown);
			_dockButton.Add(_dockIcon);
			_dockButton.clicked += () =>
			{
				_dockBottom = !_dockBottom;
				PlayerPrefs.SetInt(DevToolsKeys.HudDock, _dockBottom ? 1 : 0);
				_layoutKey = null;
			};
			_sizeButton.AddToClassList("dt-btn--size");
			_sizeButton.clicked += () =>
			{
				_size = (_size + 1) % Sizes.Length;
				PlayerPrefs.SetInt(DevToolsKeys.HudSize, _size);
				_layoutKey = null;
			};
			_closeButton.Add(new DevIcon(DevIcon.Shape.Close));
			_closeButton.clicked += () => Open(false);
		}

		void BuildPill()
		{
			_pillDot = new VisualElement { pickingMode = PickingMode.Ignore };
			_pillDot.AddToClassList("dt-pill__dot");
			_pill.Insert(0, _pillDot);
			_pillText.pickingMode = PickingMode.Ignore;
			_pill.RegisterCallback<PointerDownEvent>(e =>
			{
				_pillPressed = true;
				_pillDragging = false;
				_pillPressPosition = e.position;
				_pillGrab = (Vector2)e.position - new Vector2(_pill.resolvedStyle.left, _pill.resolvedStyle.top);
				_pill.CapturePointer(e.pointerId);
				e.StopPropagation();
			});
			_pill.RegisterCallback<PointerMoveEvent>(e =>
			{
				if (!_pillPressed || !_pill.HasPointerCapture(e.pointerId)) return;
				if (!_pillDragging && ((Vector2)e.position - _pillPressPosition).magnitude > Tuning.PillDragThreshold) _pillDragging = true;
				if (!_pillDragging) return;
				Vector2 topLeft = (Vector2)e.position - _pillGrab;
				float freeX = Mathf.Max(1f, _safeLogical.width - _pill.resolvedStyle.width);
				float freeY = Mathf.Max(1f, _safeLogical.height - _pill.resolvedStyle.height);
				_pillPosition = new Vector2(Mathf.Clamp01((topLeft.x - _safeLogical.x) / freeX), Mathf.Clamp01((topLeft.y - _safeLogical.y) / freeY));
				e.StopPropagation();
			});
			_pill.RegisterCallback<PointerUpEvent>(e =>
			{
				if (!_pillPressed) return;
				_pillPressed = false;
				if (_pill.HasPointerCapture(e.pointerId)) _pill.ReleasePointer(e.pointerId);
				if (_pillDragging) PlayerPrefs.SetString(DevToolsKeys.HudPill, _pillPosition.x.ToString("0.###", Inv) + "," + _pillPosition.y.ToString("0.###", Inv));
				else Open(true);
				_pillDragging = false;
				e.StopPropagation();
			});
			_pill.RegisterCallback<PointerCaptureOutEvent>(_ =>
			{
				_pillPressed = false;
				_pillDragging = false;
			});
		}

		/// <summary>Search box: icon, field, placeholder and a clear button, all in one rounded container.</summary>
		void BuildSearchBox()
		{
			_searchBox = new VisualElement();
			_searchBox.AddToClassList("dt-searchbox");
			_searchBox.Add(new DevIcon(DevIcon.Shape.Search));
			_searchField = new TextField();
			_searchField.AddToClassList("dt-field");
			_searchField.AddToClassList("dt-searchbox__field");
			_searchBox.Add(_searchField);
			_searchPlaceholder = new Label("Search commands...") { pickingMode = PickingMode.Ignore };
			_searchPlaceholder.AddToClassList("dt-searchbox__placeholder");
			_searchBox.Add(_searchPlaceholder);
			_searchClear = new Button(() =>
			{
				_searchField.value = "";
				_searchField.Blur();
			});
			_searchClear.AddToClassList("dt-btn");
			_searchClear.AddToClassList("dt-btn--icon");
			_searchClear.Add(new DevIcon(DevIcon.Shape.Close));
			_searchBox.Add(_searchClear);
			_searchField.RegisterValueChangedCallback(e =>
			{
				_search = e.newValue ?? "";
				_contentDirty = true;
				RefreshSearchChrome();
			});
			_searchField.RegisterCallback<FocusInEvent>(_ =>
			{
				_searchBox.AddToClassList("dt-searchbox--focus");
				RefreshSearchChrome();
			});
			_searchField.RegisterCallback<FocusOutEvent>(_ =>
			{
				_searchBox.RemoveFromClassList("dt-searchbox--focus");
				RefreshSearchChrome();
			});
			RefreshSearchChrome();
		}

		void RefreshSearchChrome()
		{
			bool hasText = _search.Length > 0;
			bool focused = _searchBox.ClassListContains("dt-searchbox--focus");
			_searchPlaceholder.style.display = hasText ? DisplayStyle.None : DisplayStyle.Flex;
			_searchPlaceholder.text = _tab == TabWatch ? "Filter values..." : "Search commands...";
			_searchClear.style.display = hasText ? DisplayStyle.Flex : DisplayStyle.None;
			_ = focused;
		}

		// ---- per-frame -----------------------------------------------------------------------------------------------

		static void Show(VisualElement element, bool on)
		{
			var wanted = on ? DisplayStyle.Flex : DisplayStyle.None;
			if (element.style.display != wanted) element.style.display = wanted;
		}

		void UpdateUi()
		{
			if (_root == null) return;
			bool visible = !_hidden && !SuppressDrawing;
			Show(_root, visible);
			if (!visible) return;
			bool panelOn = _open;
			Show(_panelElement, panelOn);
			Show(_pill, !panelOn);
			ApplyLayout();

			if (panelOn)
			{
				if (!_wasOpen)
				{
					_revealTab = true;
					_contentDirty = true;
				}
				EnsureTabs();
				if (_contentDirty || _reg.Version != _contentVersion) RebuildContent();
				else if (_logDirty) RefreshLogHost();
				_logDirty = false;
				TickCards();
			}
			_wasOpen = panelOn;

			if (Time.unscaledTime >= _nextRefresh)
			{
				_nextRefresh = Time.unscaledTime + Tuning.RefreshSeconds;
				RefreshStats();
				if (panelOn) RefreshDynamic();
			}
			if (_toastUntil > 0f && Time.realtimeSinceStartup >= _toastUntil)
			{
				_toastUntil = 0f;
				_toast.AddToClassList("dt-toast--hidden");
			}
		}

		/// <summary>Scale, safe area, docking and size. Recomputed only when one of the inputs changes.</summary>
		void ApplyLayout()
		{
			float baseScale = Screen.width > Screen.height
				? Mathf.Min(Screen.width / 960f, Screen.height / 540f)
				: Mathf.Min(Screen.width / 540f, Screen.height / 960f);
			float scale = Mathf.Max(0.5f, baseScale * _userScale);
			Rect safe = Screen.safeArea;
			string key = string.Concat(scale.ToString("0.###", Inv), "|", Screen.width, "x", Screen.height, "|", safe.ToString(), "|", _size, _dockBottom ? "b" : "t");
			if (key != _layoutKey)
			{
				_layoutKey = key;
				_scale = scale;
				_panelSettings.scale = scale;
				_safeLogical = new Rect(safe.x / scale, (Screen.height - safe.yMax) / scale, safe.width / scale, safe.height / scale);
				float fullWidth = Screen.width / scale, fullHeight = Screen.height / scale;
				_root.style.paddingLeft = _safeLogical.x;
				_root.style.paddingTop = _safeLogical.y;
				_root.style.paddingRight = fullWidth - _safeLogical.xMax;
				_root.style.paddingBottom = fullHeight - _safeLogical.yMax;
				_root.style.justifyContent = _dockBottom ? Justify.FlexEnd : Justify.FlexStart;
				float height = Mathf.Min(Mathf.Max(Tuning.MinimumPanelHeight, _safeLogical.height * (Screen.width > Screen.height ? SizesLandscape : Sizes)[_size]), _safeLogical.height - Tuning.PanelMargin * 2f);
				_panelElement.style.height = height;
				_root.EnableInClassList("dt-compact", Screen.width > Screen.height);
				_dockIcon.Kind = _dockBottom ? DevIcon.Shape.ChevronUp : DevIcon.Shape.ChevronDown;
				_sizeButton.text = Sizes[_size] < 0.5f ? "S" : Sizes[_size] < 0.8f ? "M" : "L";
			}
			_panelElement.style.opacity = Mathf.Max(_alpha, Tuning.MinimumPanelAlpha);

			if (!_open)
			{
				float width = float.IsNaN(_pill.resolvedStyle.width) ? 100f : _pill.resolvedStyle.width;
				float height = float.IsNaN(_pill.resolvedStyle.height) ? 40f : _pill.resolvedStyle.height;
				_pill.style.left = _safeLogical.x + _pillPosition.x * Mathf.Max(0f, _safeLogical.width - width);
				_pill.style.top = _safeLogical.y + _pillPosition.y * Mathf.Max(0f, _safeLogical.height - height);
			}
		}

		void RefreshStats()
		{
			int errors = DevToolsHost.ErrorCount;
			string fps = _fps.ToString("0", Inv) + " fps";
			_stats.text = errors > 0 ? errors + " err  ·  " + fps : fps;
			_stats.EnableInClassList("dt-chip--error", errors > 0);

			if (!_open)
			{
				var sb = new StringBuilder("DEV ").Append(_fps.ToString("0", Inv));
				foreach (var w in _reg.Watches)
					if (w.Pinned && _watchCache.TryGetValue(w, out string v))
						sb.Append("   ").Append(w.Label).Append(' ').Append(v);
				if (errors > 0) sb.Append("   ").Append(errors).Append(" err");
				_pillText.text = sb.ToString();
				_pill.EnableInClassList("dt-pill--error", errors > 0);
			}
		}

		// ---- tabs ----------------------------------------------------------------------------------------------------

		void EnsureTabs()
		{
			var tabs = Tabs();
			if (!tabs.Contains(_tab)) _tab = TabQuick;
			string key = string.Join("|", tabs);
			if (key != _tabsKey)
			{
				_tabsKey = key;
				_tabs.Clear();
				_tabButtons.Clear();
				foreach (string t in tabs)
				{
					string tab = t;
					var button = new Button(() => SelectTab(tab, true));
					button.AddToClassList("dt-tab");
					if (tab == TabQuick) button.Add(new DevIcon(DevIcon.Shape.StarFilled));
					button.Add(new Label(TabText(tab)) { pickingMode = PickingMode.Ignore });
					button.Q<Label>().AddToClassList("dt-tab__text");
					_tabs.Add(button);
					_tabButtons[tab] = button;
				}
				_revealTab = true;
			}
			foreach (var pair in _tabButtons) pair.Value.EnableInClassList("dt-tab--on", pair.Key == _tab);
			if (_revealTab && _tabButtons.TryGetValue(_tab, out var selected))
			{
				_revealTab = false;
				_tabs.schedule.Execute(() =>
				{
					float viewport = _tabs.contentViewport.resolvedStyle.width;
					if (float.IsNaN(viewport) || viewport < 1f) return;
					float target = selected.layout.center.x - viewport * 0.5f;
					_tabs.scrollOffset = new Vector2(Mathf.Max(0f, target), 0f);
				}).StartingIn(40);
			}
		}

		void SelectTab(string tab, bool persist)
		{
			_tab = tab;
			_contentDirty = true;
			_revealTab = true;
			_pendingScrollTop = true;
			if (persist) PlayerPrefs.SetString(DevToolsKeys.HudTab, tab);
		}

		// ---- toast ---------------------------------------------------------------------------------------------------

		void ShowToast(string text, bool ok)
		{
			if (_toast == null) return;
			_toast.text = text;
			_toast.RemoveFromClassList("dt-toast--hidden");
			_toast.EnableInClassList("dt-toast--error", !ok);
			_toastUntil = Time.realtimeSinceStartup + (ok ? Tuning.ToastSecondsOk : Tuning.ToastSecondsError);
		}
	}
}
#endif
