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
		VisualElement _root, _panelElement, _pill, _pillRing, _searchSlot, _searchBox, _consoleRow;
		Label _title, _stats, _toast, _pillText, _pillDetail, _pillBadge, _searchPlaceholder;
		Button _dockButton, _sizeButton, _closeButton, _searchClear;
		DevIcon _dockIcon, _pillIcon;
		TextField _searchField;
		DevScrollView _tabs, _body;
		readonly Dictionary<string, Button> _tabButtons = new Dictionary<string, Button>();
		readonly List<string> _tabsCache = new List<string>();
		string _tabsKey = "";
		bool _revealTab, _contentDirty = true, _logDirty, _wasOpen;
		float _toastUntil, _nextRefresh;
		bool _pillPressed, _pillDragging, _pillLongPressed;
		float _pillPressedAt, _pillSnapFrom, _pillSnapTarget, _pillSnapStart = -1f;
		string _pillTextShown = "", _pillDetailShown = "", _pillBadgeShown = "";
		readonly StringBuilder _pillBuilder = new StringBuilder(64);
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

		/// <summary>Icons of the built-in categories and pages; games add theirs with <see cref="SetCategoryIcon"/>.</summary>
		static readonly Dictionary<string, DevIcon.Shape> CategoryIcons = new Dictionary<string, DevIcon.Shape>(StringComparer.OrdinalIgnoreCase)
		{
			{ TabQuick, DevIcon.Shape.StarFilled },
			{ "Level", DevIcon.Shape.Flag },
			{ "Economy", DevIcon.Shape.Coin },
			{ "Ads", DevIcon.Shape.Play },
			{ "Remote config", DevIcon.Shape.Sliders },
			{ "Experiments", DevIcon.Shape.ListLines },
			{ "Save", DevIcon.Shape.Database },
			{ "Data", DevIcon.Shape.Database },
			{ "Time", DevIcon.Shape.Clock },
			{ "Tools", DevIcon.Shape.Terminal },
			{ "Engine", DevIcon.Shape.Gear },
			{ "Logs", DevIcon.Shape.ListLines },
			{ "HUD", DevIcon.Shape.Sliders },
			{ "Creative", DevIcon.Shape.Image },
			{ "Inspector", DevIcon.Shape.Search },
			{ "Info", DevIcon.Shape.Info },
			{ TabScenarios, DevIcon.Shape.Play },
			{ TabWatch, DevIcon.Shape.Eye },
			{ TabConsole, DevIcon.Shape.Terminal },
			{ TabLog, DevIcon.Shape.ListLines },
		};

		/// <summary>
		/// Icon of a game's own category (tab and section header). <paramref name="icon"/> is one of: star, flag, coin, play,
		/// sliders, list, database, clock, terminal, gear, image, search, info, eye, check, reload, trophy, heart, gift, user,
		/// cart. Returns false for an unknown name.
		/// </summary>
		public static bool SetCategoryIcon(string category, string icon)
		{
			string name = (icon ?? "").Replace("-", "").Replace(" ", "");
			if (string.Equals(name, "star", StringComparison.OrdinalIgnoreCase)) name = nameof(DevIcon.Shape.StarFilled);
			if (string.Equals(name, "list", StringComparison.OrdinalIgnoreCase)) name = nameof(DevIcon.Shape.ListLines);
			if (string.IsNullOrEmpty(category) || !Enum.TryParse(name, true, out DevIcon.Shape shape)) return false;
			CategoryIcons[category] = shape;
			if (_instance != null)
			{
				_instance._tabsKey = "";
				_instance._contentDirty = true;
			}
			return true;
		}

		static bool TryIconOf(string category, out DevIcon.Shape shape) => CategoryIcons.TryGetValue(category ?? "", out shape);

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
			_searchSlot = _root.Q("dt-search-slot");
			_toast = _root.Q<Label>("dt-toast");
			_pillText = _root.Q<Label>("dt-pill-text");
			// our own scroll containers, not UI Toolkit's ScrollView (its built-in touch scrolling fights the drag: see DevScrollView)
			_tabs = new DevScrollView(true) { name = "dt-tabs" };
			_tabs.AddToClassList("dt-tabs");
			_root.Q("dt-tabs-wrap").Add(_tabs);
			_tabs.AddManipulator(new DragScrollManipulator(_tabs));
			_body = new DevScrollView(false) { name = "dt-body" };
			_body.AddToClassList("dt-body");
			_root.Q("dt-body-wrap").Add(_body);
			_body.AddManipulator(new DragScrollManipulator(_body));

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
			// compact = a round ball (AssistiveTouch-like): dark body, accent ring, sliders icon; expanded = a pill with fps + watches
			_pillRing = new VisualElement { pickingMode = PickingMode.Ignore };
			_pillRing.AddToClassList("dt-pill__ring");
			_pill.Insert(0, _pillRing);
			_pillIcon = new DevIcon(DevIcon.Shape.Sliders);
			_pillIcon.AddToClassList("dt-pill__icon");
			_pill.Insert(1, _pillIcon);
			_pillText.pickingMode = PickingMode.Ignore;
			_pillDetail = new Label { name = "dt-pill-detail", pickingMode = PickingMode.Ignore };
			_pillDetail.AddToClassList("dt-pill__detail");
			_pill.Add(_pillDetail);
			_pillBadge = new Label { name = "dt-pill-badge", pickingMode = PickingMode.Ignore };
			_pillBadge.AddToClassList("dt-pill__badge");
			_pill.Add(_pillBadge);
			Show(_pillBadge, false);
			ApplyPillMode();
			// the pill grows and shrinks with its content: keep it inside the safe area
			_pill.RegisterCallback<GeometryChangedEvent>(_ => PlacePill());
			_pill.RegisterCallback<PointerDownEvent>(e =>
			{
				_pillPressed = true;
				_pillDragging = false;
				_pillLongPressed = false;
				_pillPressedAt = Time.unscaledTime;
				_pillPressPosition = e.position;
				_pillGrab = (Vector2)e.position - _pillRect.position;
				_pill.AddToClassList("dt-pill--pressed"); // sinks a little under the finger
				_pill.CapturePointer(e.pointerId);
				e.StopPropagation();
			});
			_pill.RegisterCallback<PointerMoveEvent>(e =>
			{
				if (!_pillPressed || !_pill.HasPointerCapture(e.pointerId)) return;
				if (!_pillDragging && !_pillLongPressed && ((Vector2)e.position - _pillPressPosition).magnitude > DragScrollManipulator.TouchSlop(_pill, Tuning.PillDragThreshold)) _pillDragging = true;
				if (!_pillDragging) return;
				_pill.RemoveFromClassList("dt-pill--holding");
				_pill.RemoveFromClassList("dt-pill--pressed");
				_pillSnapStart = -1f;
				CloseQuick(); // the card is anchored to the pill: it would hang in the air while the pill moves
				Vector2 topLeft = (Vector2)e.position - _pillGrab;
				float margin = Tuning.PillEdgeMargin; // same mapping as PlacePill
				float freeX = Mathf.Max(1f, _safeLogical.width - _pill.resolvedStyle.width - margin * 2f);
				float freeY = Mathf.Max(1f, _safeLogical.height - _pill.resolvedStyle.height - margin * 2f);
				_pillPosition = new Vector2(Mathf.Clamp01((topLeft.x - _safeLogical.x - margin) / freeX), Mathf.Clamp01((topLeft.y - _safeLogical.y - margin) / freeY));
				e.StopPropagation();
			});
			_pill.RegisterCallback<PointerUpEvent>(e =>
			{
				if (!_pillPressed) return;
				_pillPressed = false;
				if (_pill.HasPointerCapture(e.pointerId)) _pill.ReleasePointer(e.pointerId);
				_pill.RemoveFromClassList("dt-pill--holding");
				_pill.RemoveFromClassList("dt-pill--pressed");
				if (_pillDragging)
				{
					if (_settings.PillSnapToEdge) StartPillSnap();
					SavePillPosition();
				}
				else if (!_pillLongPressed) TapPill(); // a long-press already did its job: releasing must not also open anything
				_pillDragging = false;
				_pillLongPressed = false;
				e.StopPropagation();
			});
			_pill.RegisterCallback<PointerCaptureOutEvent>(_ =>
			{
				_pillPressed = false;
				_pillDragging = false;
				_pillLongPressed = false;
				_pill.RemoveFromClassList("dt-pill--holding");
				_pill.RemoveFromClassList("dt-pill--pressed");
			});
		}

		void TapPill()
		{
			if (_settings.PillTap == DevToolsSettings.PillTapKind.Panel) Open(true);
			else if (_quickOpen) CloseQuick();
			else OpenQuick();
		}

		void SavePillPosition() =>
			PlayerPrefs.SetString(DevToolsKeys.HudPill, (_pillSnapStart >= 0f ? _pillSnapTarget : _pillPosition.x).ToString("0.###", Inv) + "," + _pillPosition.y.ToString("0.###", Inv));

		/// <summary>Like AssistiveTouch: after a drag the pill slides to the nearer side edge, out of the play area.</summary>
		void StartPillSnap()
		{
			_pillSnapFrom = _pillPosition.x;
			_pillSnapTarget = _pillPosition.x < 0.5f ? 0f : 1f;
			_pillSnapStart = Time.unscaledTime;
		}

		/// <summary>Every frame while a snap runs: ease-out on unscaled time (the game may be paused or slowed).</summary>
		void TickPillSnap()
		{
			if (_pillSnapStart < 0f) return;
			float t = Mathf.Clamp01((Time.unscaledTime - _pillSnapStart) / Tuning.PillSnapSeconds);
			float eased = 1f - (1f - t) * (1f - t);
			_pillPosition.x = Mathf.Lerp(_pillSnapFrom, _pillSnapTarget, eased);
			if (t >= 1f) _pillSnapStart = -1f;
		}

		/// <summary>Called every frame: the long-press fires on the clock, not on a pointer event (a held finger sends none).</summary>
		void TickPillHold()
		{
			if (!_pillPressed || _pillDragging || _pillLongPressed || _open) return;
			float held = Time.unscaledTime - _pillPressedAt;
			if (held >= Tuning.PillLongPressSeconds)
			{
				_pillLongPressed = true;
				SetPillExpanded(!_pillExpanded);
				_pill.RemoveFromClassList("dt-pill--holding");
				_pill.RemoveFromClassList("dt-pill--pressed");
			}
			else if (held >= Tuning.PillHoldCueSeconds) _pill.AddToClassList("dt-pill--holding");
		}

		void SetPillExpanded(bool expanded)
		{
			if (_pillExpanded == expanded) return;
			_pillExpanded = expanded;
			PlayerPrefs.SetInt(DevToolsKeys.HudPillExpanded, expanded ? 1 : 0);
			ApplyPillMode();
			_nextRefresh = 0f; // refill the texts now
		}

		/// <summary>Ball (compact: icon only, the error badge on its corner) or pill (expanded: icon, fps, pinned watches, badge).</summary>
		void ApplyPillMode()
		{
			if (_pill == null) return;
			_pill.EnableInClassList("dt-pill--ball", !_pillExpanded);
			Show(_pillRing, !_pillExpanded);
			Show(_pillText, _pillExpanded);
			if (!_pillExpanded) Show(_pillDetail, false);
		}

		/// <summary>Pill position from its stored fraction of the free safe-area space (so a width change cannot push it off screen).</summary>
		void PlacePill()
		{
			if (_pill == null || _open) return;
			float width = float.IsNaN(_pill.resolvedStyle.width) ? 100f : _pill.resolvedStyle.width;
			float height = float.IsNaN(_pill.resolvedStyle.height) ? 34f : _pill.resolvedStyle.height;
			// a small gap from the screen edges, so the ball reads as floating rather than glued on
			float margin = Tuning.PillEdgeMargin;
			float x = _safeLogical.x + margin + _pillPosition.x * Mathf.Max(0f, _safeLogical.width - width - margin * 2f);
			float y = _safeLogical.y + margin + _pillPosition.y * Mathf.Max(0f, _safeLogical.height - height - margin * 2f);
			_pillRect = new Rect(x, y, width, height);
			// moved by translate, not left / top: translate does not take part in layout, so neither a layout loop (an absolute
			// element held by its left edge near the right edge shrinks, moves, shrinks...) nor a squeezed pill can happen
			SetTranslate(_pill, x, y);
		}

		/// <summary>The pill's rectangle in panel coordinates (its layout stays at 0,0; the position is a translate).</summary>
		Rect _pillRect;

		static void SetTranslate(VisualElement element, float x, float y)
		{
			var current = element.style.translate.value;
			if (Mathf.Approximately(current.x.value, x) && Mathf.Approximately(current.y.value, y) && element.style.translate.keyword == StyleKeyword.Undefined) return;
			element.style.translate = new Translate(new Length(x), new Length(y), 0f);
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
			if (panelOn && _quickOpen) CloseQuick();
			Show(_panelElement, panelOn);
			Show(_pill, !panelOn);
			TickPillSnap();
			ApplyLayout();
			TickPillHold();
			if (_quickOpen) TickQuick();

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
				else if (_quickOpen) RefreshQuick();
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

			PlacePill();
		}

		void RefreshStats()
		{
			int errors = DevToolsHost.ErrorCount;
			string fps = _fps.ToString("0", Inv) + " fps";
			_stats.text = errors > 0 ? errors + " err  ·  " + fps : fps;
			_stats.EnableInClassList("dt-chip--error", errors > 0);

			if (!_open) RefreshPill(errors);
		}

		/// <summary>Compact: fps + error badge. Expanded: fps + pinned watches + badge. Texts are only reassigned when they change.</summary>
		void RefreshPill(int errors)
		{
			_pillBuilder.Clear().Append(_fps.ToString("0", Inv));
			SetPillLabel(_pillText, _pillBuilder, ref _pillTextShown);
			if (_pillExpanded)
			{
				_pillBuilder.Clear();
				foreach (var w in _reg.Watches)
				{
					if (!w.Pinned || !_watchCache.TryGetValue(w, out string v)) continue;
					if (_pillBuilder.Length > 0) _pillBuilder.Append("   ");
					_pillBuilder.Append(w.Label).Append(' ').Append(v);
				}
				SetPillLabel(_pillDetail, _pillBuilder, ref _pillDetailShown);
				Show(_pillDetail, _pillBuilder.Length > 0);
			}
			if (errors > 0)
			{
				_pillBuilder.Clear();
				if (errors > Tuning.PillBadgeMaxErrors) _pillBuilder.Append(Tuning.PillBadgeMaxErrors).Append('+');
				else _pillBuilder.Append(errors);
				SetPillLabel(_pillBadge, _pillBuilder, ref _pillBadgeShown);
			}
			Show(_pillBadge, errors > 0);
			_pill.EnableInClassList("dt-pill--error", errors > 0);
		}

		static void SetPillLabel(Label label, StringBuilder text, ref string shown)
		{
			string built = text.ToString();
			if (built == shown) return;
			shown = built;
			label.text = built;
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
					if (TryIconOf(tab, out var icon)) button.Add(new DevIcon(icon));
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
					float viewport = _tabs.Viewport.resolvedStyle.width;
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
