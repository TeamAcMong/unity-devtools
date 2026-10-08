#if UNITY_EDITOR || DEVELOPMENT_BUILD || DREAMTECH_DEVTOOLS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace DreamTech.DevTools.Unity
{
	// The pages inside the panel: command cards, watches, scenarios, console and log.
	public sealed partial class DevToolsHud
	{
		/// <summary>The elements of one command card that change after it was built.</summary>
		sealed class CommandCard
		{
			public DevCommand Command;
			public VisualElement Root, ParamsBox, ConfirmRow, Flash;
			public Button Run;
			public Toggle Switch;
			public Label BlockedChip;
			public DevIcon Star;
			public float FlashStart = -1f;
			public bool FlashOk;
		}

		sealed class WatchRow
		{
			public DevWatch Watch;
			public Label Value;
		}

		readonly List<CommandCard> _cards = new List<CommandCard>();
		readonly List<WatchRow> _watchRows = new List<WatchRow>();
		readonly List<DevCommand> _rowCommands = new List<DevCommand>();
		readonly Dictionary<string, int> _categoryOrder = new Dictionary<string, int>();
		VisualElement _logHost;
		Label _consoleHint;
		TextField _consoleField;
		CommandCard _confirmCard;
		float _confirmUntil;
		int _contentVersion = -1, _runningScripts;
		bool _pendingScrollTop;

		// ---- page switch ---------------------------------------------------------------------------------------------

		void RebuildContent()
		{
			_contentDirty = false;
			_contentVersion = _reg.Version;
			_cards.Clear();
			_watchRows.Clear();
			_logHost = null;
			_consoleHint = null;
			_confirmCard = null;
			_body.Clear();
			_runningScripts = DevTools.RunningScripts.Count;
			BuildSearchSlot();

			if (_tab == TabConsole) BuildConsole();
			else if (_tab == TabLog) BuildLog();
			else if (_tab == TabScenarios) BuildScenarios();
			else if (_tab == TabWatch) BuildWatchPage();
			else if (_tab == TabQuick) BuildQuick();
			else BuildCategory(_tab);

			if (_pendingScrollTop)
			{
				_pendingScrollTop = false;
				_body.scrollOffset = Vector2.zero;
			}
			RefreshDynamic();
		}

		void BuildSearchSlot()
		{
			_searchSlot.Clear();
			bool search = _tab == TabQuick || _tab == TabWatch;
			if (search)
			{
				_searchSlot.Add(_searchBox);
				if (_searchField.value != _search) _searchField.SetValueWithoutNotify(_search);
				RefreshSearchChrome();
			}
			else if (_tab == TabConsole) _searchSlot.Add(BuildConsoleRow());
			Show(_searchSlot, search || _tab == TabConsole);
		}

		// ---- Quick / category ----------------------------------------------------------------------------------------

		void BuildQuick()
		{
			bool searching = _search.Length > 0;
			RefreshRowCommands();
			if (!searching)
			{
				var pinned = _reg.Watches.Where(w => w.Pinned).ToList();
				if (pinned.Count > 0)
				{
					AddSection("LIVE");
					AddWatchGroup(pinned);
				}
				if (_rowCommands.Count == 0) AddEmpty("Tap the star on any command to keep it here.");
			}
			else if (_rowCommands.Count == 0) AddEmpty("Nothing matches \"" + _search + "\".");
			string category = null;
			foreach (var c in _rowCommands)
			{
				if (c.Category != category)
				{
					category = c.Category;
					AddSection(category.ToUpperInvariant(), category);
				}
				AddCard(c, searching);
			}
		}

		void BuildCategory(string category)
		{
			var watches = _reg.Watches.Where(w => w.Category == category).ToList();
			var commands = _reg.Commands.Where(c => c.Category == category).ToList();
			if (watches.Count > 0)
			{
				if (commands.Count > 0) AddSection("LIVE VALUES");
				AddWatchGroup(watches);
			}
			if (watches.Count > 0 && commands.Count > 0) AddSection("ACTIONS");
			foreach (var c in commands) AddCard(c, true);
			if (watches.Count == 0 && commands.Count == 0) AddEmpty("Nothing registered in this category.");
		}

		/// <summary>Quick tab rows: the quick + starred commands, or the search results, grouped by category in registration order.</summary>
		void RefreshRowCommands()
		{
			_rowCommands.Clear();
			if (_search.Length > 0) _rowCommands.AddRange(_reg.Search(_search));
			else
				foreach (var c in _reg.Commands)
					if (c.Quick || _fav.Contains(c.Id)) _rowCommands.Add(c);
			_categoryOrder.Clear();
			for (int i = 0; i < _reg.Categories.Count; i++)
				if (!_categoryOrder.ContainsKey(_reg.Categories[i])) _categoryOrder[_reg.Categories[i]] = _categoryOrder.Count;
			var sorted = _rowCommands.OrderBy(c => _categoryOrder.TryGetValue(c.Category, out int index) ? index : -1).ToList(); // LINQ OrderBy is stable
			_rowCommands.Clear();
			_rowCommands.AddRange(sorted);
		}

		// ---- Watch ---------------------------------------------------------------------------------------------------

		void BuildWatchPage()
		{
			bool any = false;
			string category = null;
			var group = new List<DevWatch>();
			void Flush()
			{
				if (group.Count == 0) return;
				AddSection(category.ToUpperInvariant());
				AddWatchGroup(group);
				group.Clear();
				any = true;
			}
			foreach (var w in _reg.Watches)
			{
				if (_search.Length > 0 && (w.Label + " " + w.Category).IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0) continue;
				if (w.Category != category)
				{
					Flush();
					category = w.Category;
				}
				group.Add(w);
			}
			Flush();
			if (!any) AddEmpty(_search.Length > 0 ? "Nothing matches \"" + _search + "\"." : "No watches registered.");
		}

		void AddWatchGroup(List<DevWatch> watches)
		{
			var group = new VisualElement();
			group.AddToClassList("dt-group");
			for (int i = 0; i < watches.Count; i++)
			{
				var row = new VisualElement();
				row.AddToClassList("dt-watch");
				if (i == 0) row.AddToClassList("dt-watch--first");
				var key = new Label(watches[i].Label);
				key.AddToClassList("dt-watch__key");
				var value = new Label("...");
				value.AddToClassList("dt-watch__value");
				row.Add(key);
				row.Add(value);
				group.Add(row);
				_watchRows.Add(new WatchRow { Watch = watches[i], Value = value });
			}
			_body.Add(group);
		}

		// ---- Scenarios -----------------------------------------------------------------------------------------------

		void BuildScenarios()
		{
			AddSection("SCENARIOS");
			foreach (var preset in _reg.Presets)
			{
				var p = preset;
				var card = new VisualElement();
				card.AddToClassList("dt-card");
				var top = new VisualElement();
				top.AddToClassList("dt-card__top");
				var text = new VisualElement();
				text.AddToClassList("dt-card__text");
				var label = new Label(p.Name);
				label.AddToClassList("dt-card__label");
				var hint = new Label(string.Join("; ", DevScriptRun.Split(p.Script)));
				hint.AddToClassList("dt-card__hint");
				text.Add(label);
				text.Add(hint);
				top.Add(text);
				var actions = new VisualElement();
				actions.AddToClassList("dt-card__actions");
				actions.Add(MakeButton("Run", "dt-btn--primary", () => DevTools.RunScript(p.Script, p.Name)));
				top.Add(actions);
				card.Add(top);
				_body.Add(card);
			}
			if (DevTools.RunningScripts.Count > 0)
			{
				var cancel = MakeButton("Cancel running scripts (" + DevTools.RunningScripts.Count + ")", "dt-btn--danger", () =>
				{
					DevTools.CancelScripts();
					_contentDirty = true;
				});
				cancel.style.marginTop = 4;
				_body.Add(cancel);
			}
		}

		// ---- Console / Log -------------------------------------------------------------------------------------------

		VisualElement BuildConsoleRow()
		{
			if (_consoleRow != null) return _consoleRow;
			_consoleRow = new VisualElement();
			_consoleRow.AddToClassList("dt-console-row");
			var box = new VisualElement();
			box.AddToClassList("dt-searchbox");
			_consoleField = new TextField();
			_consoleField.AddToClassList("dt-field");
			_consoleField.AddToClassList("dt-searchbox__field");
			_consoleField.style.marginLeft = 0;
			_consoleField.SetValueWithoutNotify(_consoleLine);
			var placeholder = new Label("command id and arguments, or help") { pickingMode = PickingMode.Ignore };
			placeholder.AddToClassList("dt-searchbox__placeholder");
			placeholder.style.left = 14;
			box.Add(_consoleField);
			box.Add(placeholder);
			_consoleField.RegisterValueChangedCallback(e =>
			{
				_consoleLine = e.newValue ?? "";
				placeholder.style.display = _consoleLine.Length > 0 ? DisplayStyle.None : DisplayStyle.Flex;
			});
			placeholder.style.display = _consoleLine.Length > 0 ? DisplayStyle.None : DisplayStyle.Flex;
			_consoleField.RegisterCallback<FocusInEvent>(_ => box.AddToClassList("dt-searchbox--focus"));
			_consoleField.RegisterCallback<FocusOutEvent>(_ => box.RemoveFromClassList("dt-searchbox--focus"));
			_consoleField.RegisterCallback<KeyDownEvent>(OnConsoleKey, TrickleDown.TrickleDown);
			_consoleRow.Add(box);
			_consoleRow.Add(MakeButton("Run", "dt-btn--primary", SubmitConsole));
			return _consoleRow;
		}

		void OnConsoleKey(KeyDownEvent e)
		{
			if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
			{
				SubmitConsole();
				e.StopPropagation();
			}
			else if (e.keyCode == KeyCode.UpArrow && _history.Count > 0)
			{
				_historyPosition = Mathf.Clamp(_historyPosition < 0 ? _history.Count - 1 : _historyPosition - 1, 0, _history.Count - 1);
				_consoleField.value = _history[_historyPosition];
				e.StopPropagation();
			}
			else if (e.keyCode == KeyCode.DownArrow && _historyPosition >= 0)
			{
				_historyPosition++;
				if (_historyPosition >= _history.Count)
				{
					_historyPosition = -1;
					_consoleField.value = "";
				}
				else _consoleField.value = _history[_historyPosition];
				e.StopPropagation();
			}
			else if (e.keyCode == KeyCode.Tab)
			{
				string first = DevRegistry.Tokenize(_consoleLine).FirstOrDefault() ?? "";
				var matches = _reg.Complete(first).ToList();
				if (matches.Count == 1) _consoleField.value = matches[0] + " ";
				else if (matches.Count > 1) _consoleField.value = CommonPrefix(matches);
				e.StopPropagation();
#if UNITY_6000_0_OR_NEWER
				_root.panel.focusController.IgnoreEvent(e); // keeps Tab from moving the focus away
#else
				e.PreventDefault();
#endif
			}
		}

		void SubmitConsole()
		{
			string line = _consoleLine.Trim();
			if (line.Length == 0) return;
			_history.Remove(line);
			_history.Add(line);
			_historyPosition = -1;
			_consoleField.value = "";
			DevTools.Submit(line);
		}

		void BuildConsole()
		{
			_consoleHint = new Label();
			_consoleHint.AddToClassList("dt-hint");
			_body.Add(_consoleHint);
			BuildLogHost();
		}

		void BuildLog()
		{
			var row = new VisualElement();
			row.style.flexDirection = FlexDirection.Row;
			row.style.justifyContent = Justify.SpaceBetween;
			row.style.alignItems = Align.Center;
			var title = new Label("RECENT COMMANDS");
			title.AddToClassList("dt-section__text");
			row.Add(title);
			row.Add(MakeButton("Clear", "dt-btn--ghost", () =>
			{
				_reg.Log.Clear();
				_logDirty = true;
			}));
			row.style.marginBottom = 8;
			_body.Add(row);
			BuildLogHost();
		}

		void BuildLogHost()
		{
			_logHost = new VisualElement();
			_body.Add(_logHost);
			RefreshLogHost();
		}

		void RefreshLogHost()
		{
			if (_logHost == null) return;
			_logHost.Clear();
			if (_reg.Log.Count == 0)
			{
				var empty = new Label("Nothing yet. Run a command and it shows up here.");
				empty.AddToClassList("dt-empty");
				_logHost.Add(empty);
				return;
			}
			int shown = 0;
			for (int i = _reg.Log.Count - 1; i >= 0 && shown < Tuning.MaxLogRows; i--, shown++)
			{
				var entry = _reg.Log[i];
				var item = new VisualElement();
				item.AddToClassList("dt-log");
				item.EnableInClassList("dt-log--error", !entry.Ok);
				var line = new Label("> " + entry.Line);
				line.AddToClassList("dt-log__line");
				item.Add(line);
				if (entry.Message.Length > 0)
				{
					var message = new Label(entry.Message);
					message.AddToClassList("dt-log__message");
					item.Add(message);
				}
				_logHost.Add(item);
			}
		}

		// ---- small builders ------------------------------------------------------------------------------------------

		/// <summary>Section header; <paramref name="category"/> (when the section is a category) adds its icon.</summary>
		void AddSection(string text, string category = null)
		{
			var section = new VisualElement();
			section.AddToClassList("dt-section");
			if (category != null && TryIconOf(category, out var icon))
			{
				var glyph = new DevIcon(icon);
				glyph.AddToClassList("dt-section__icon");
				section.Add(glyph);
			}
			var label = new Label(text);
			label.AddToClassList("dt-section__text");
			var line = new VisualElement();
			line.AddToClassList("dt-section__line");
			section.Add(label);
			section.Add(line);
			_body.Add(section);
		}

		void AddEmpty(string text)
		{
			var empty = new Label(text);
			empty.AddToClassList("dt-empty");
			_body.Add(empty);
		}

		static Button MakeButton(string text, string variant, Action onClick)
		{
			var button = new Button(onClick) { text = text };
			button.AddToClassList("dt-btn");
			if (variant != null) button.AddToClassList(variant);
			return button;
		}

		/// <summary>USS modifier of a command button: the style decides the color, so Win reads green and Lose red at a glance.</summary>
		static string StyleClass(DevCommandStyle style)
		{
			switch (style)
			{
				case DevCommandStyle.Positive: return "dt-btn--positive";
				case DevCommandStyle.Danger: return "dt-btn--danger";
				case DevCommandStyle.Warning: return "dt-btn--warning";
				default: return "dt-btn--primary";
			}
		}

		/// <summary>Icon of a styled command (none for the neutral style: "Run" alone says it).</summary>
		static DevIcon.Shape? StyleIcon(DevCommandStyle style)
		{
			switch (style)
			{
				case DevCommandStyle.Positive: return DevIcon.Shape.Check;
				case DevCommandStyle.Danger: return DevIcon.Shape.Close;
				case DevCommandStyle.Warning: return DevIcon.Shape.Reload;
				default: return null;
			}
		}

		/// <summary>A command's action button: style color, optional style icon, then the text (icon and text are children, so they line up).</summary>
		static Button MakeCommandButton(DevCommandStyle style, string text, Action onClick)
		{
			var button = new Button(onClick);
			button.AddToClassList("dt-btn");
			button.AddToClassList(StyleClass(style));
			button.AddToClassList("dt-btn--with-icon"); // a row centred on both axes, with or without the icon
			var icon = StyleIcon(style);
			if (icon.HasValue) button.Add(new DevIcon(icon.Value));
			if (!string.IsNullOrEmpty(text))
			{
				var label = new Label(text) { pickingMode = PickingMode.Ignore };
				label.AddToClassList("dt-btn__text");
				button.Add(label);
			}
			return button;
		}

		static Button MakeIconButton(DevIcon.Shape shape, Action onClick, string variant = "dt-btn--icon")
		{
			var button = new Button(onClick);
			button.AddToClassList("dt-btn");
			button.AddToClassList(variant);
			button.Add(new DevIcon(shape));
			return button;
		}

		/// <summary>An on/off switch (a real Toggle, so it keeps its value / events); the track and knob are USS-styled children.</summary>
		static Toggle MakeSwitch(bool initial, Action<bool> onChanged)
		{
			var toggle = new Toggle();
			toggle.AddToClassList("dt-switch");
			var track = new VisualElement { pickingMode = PickingMode.Ignore };
			track.AddToClassList("dt-switch__track");
			var knob = new VisualElement { pickingMode = PickingMode.Ignore };
			knob.AddToClassList("dt-switch__knob");
			track.Add(knob);
			toggle.Add(track);
			toggle.SetValueWithoutNotify(initial);
			toggle.EnableInClassList("dt-on", initial);
			toggle.RegisterValueChangedCallback(e =>
			{
				toggle.EnableInClassList("dt-on", e.newValue);
				onChanged?.Invoke(e.newValue);
			});
			return toggle;
		}

		static void SetSwitch(Toggle toggle, bool on)
		{
			if (toggle.value != on) toggle.SetValueWithoutNotify(on);
			toggle.EnableInClassList("dt-on", on);
		}

		// ---- command cards -------------------------------------------------------------------------------------------

		void AddCard(DevCommand c, bool showHelp)
		{
			var card = new CommandCard { Command = c };
			var root = new VisualElement();
			root.AddToClassList("dt-card");
			card.Root = root;

			var top = new VisualElement();
			top.AddToClassList("dt-card__top");
			var text = new VisualElement();
			text.AddToClassList("dt-card__text");
			var label = new Label(c.Label);
			label.AddToClassList("dt-card__label");
			text.Add(label);
			if (showHelp && !string.IsNullOrEmpty(c.Help))
			{
				var hint = new Label(c.Help);
				hint.AddToClassList("dt-card__hint");
				text.Add(hint);
			}
			top.Add(text);

			var actions = new VisualElement();
			actions.AddToClassList("dt-card__actions");
			card.BlockedChip = new Label();
			card.BlockedChip.AddToClassList("dt-card__blocked");
			actions.Add(card.BlockedChip);
			string id = c.Id;
			var star = MakeIconButton(_fav.Contains(id) ? DevIcon.Shape.StarFilled : DevIcon.Shape.Star, null);
			card.Star = star.Q<DevIcon>();
			card.Star.EnableInClassList("dt-icon--star-on", _fav.Contains(id));
			star.clicked += () =>
			{
				if (!_fav.Remove(id)) _fav.Add(id);
				PlayerPrefs.SetString(DevToolsKeys.HudFavorites, string.Join("|", _fav));
				bool on = _fav.Contains(id);
				card.Star.Kind = on ? DevIcon.Shape.StarFilled : DevIcon.Shape.Star;
				card.Star.EnableInClassList("dt-icon--star-on", on);
				if (_tab == TabQuick && _search.Length == 0 && !c.Quick) _contentDirty = true;
			};
			actions.Add(star);

			if (c.Kind == DevCommandKind.Toggle)
			{
				card.Switch = MakeSwitch(SafeState(c), on =>
				{
					Run(c, on ? "on" : "off");
					RefreshCard(card);
				});
				text.RegisterCallback<ClickEvent>(_ => card.Switch.value = !card.Switch.value);
				actions.Add(card.Switch);
			}
			else
			{
				card.Run = MakeCommandButton(c.EffectiveStyle, "Run", () => Ask(card));
				actions.Add(card.Run);
			}
			top.Add(actions);
			root.Add(top);

			if (c.Kind == DevCommandKind.Action && c.Params.Length > 0)
			{
				card.ParamsBox = new VisualElement();
				card.ParamsBox.AddToClassList("dt-card__params");
				var values = Values(c);
				for (int i = 0; i < c.Params.Length; i++) card.ParamsBox.Add(BuildParam(c, i, values));
				root.Add(card.ParamsBox);
			}

			card.Flash = new VisualElement { pickingMode = PickingMode.Ignore };
			card.Flash.AddToClassList("dt-flash");
			root.Add(card.Flash);
			_cards.Add(card);
			_body.Add(root);
		}

		bool SafeState(DevCommand c)
		{
			try { return c.State != null && c.State(); }
			catch { return false; }
		}

		static List<string> OptionsOf(DevParam p)
		{
			IList<string> options;
			try { options = p.Options?.Invoke() ?? Array.Empty<string>(); }
			catch (Exception e) { options = new[] { "<" + e.Message + ">" }; }
			return options.Take(Tuning.MaxDropdownOptions).ToList();
		}

		VisualElement BuildParam(DevCommand c, int i, string[] values)
		{
			var p = c.Params[i];
			var block = new VisualElement();
			block.AddToClassList("dt-param");
			var caption = new Label(p.Name.ToUpperInvariant());
			caption.AddToClassList("dt-param__caption");

			switch (p.Kind)
			{
				case DevParamKind.Bool:
				{
					block.AddToClassList("dt-param--inline");
					caption.text = p.Name;
					block.Add(caption);
					block.Add(MakeSwitch(values[i] == "true", on => values[i] = on ? "true" : "false"));
					break;
				}
				case DevParamKind.Choice:
				{
					block.Add(caption);
					var options = OptionsOf(p);
					string current = values[i];
					if (!string.IsNullOrEmpty(current) && !options.Contains(current)) options.Insert(0, current);
					var dropdown = new DropdownField { choices = options };
					dropdown.AddToClassList("dt-field");
					// nothing chosen yet (empty default): show what to do, keep the value empty so Run behaves as it always did
					dropdown.SetValueWithoutNotify(string.IsNullOrEmpty(current) ? "Choose " + p.Name + "..." : current);
					dropdown.EnableInClassList("dt-field--empty", string.IsNullOrEmpty(current));
					dropdown.RegisterValueChangedCallback(e =>
					{
						values[i] = e.newValue;
						dropdown.RemoveFromClassList("dt-field--empty");
					});
					// options follow live game state: refresh them right before the popup opens
					dropdown.RegisterCallback<PointerDownEvent>(_ =>
					{
						var fresh = OptionsOf(p);
						if (!string.IsNullOrEmpty(values[i]) && !fresh.Contains(values[i])) fresh.Insert(0, values[i]);
						dropdown.choices = fresh;
					}, TrickleDown.TrickleDown);
					dropdown.Q(className: "unity-base-popup-field__input")?.Add(new DevIcon(DevIcon.Shape.ChevronDown));
					block.Add(dropdown);
					break;
				}
				case DevParamKind.Int:
				{
					block.Add(caption);
					var row = new VisualElement();
					row.AddToClassList("dt-param__row");
					var field = new LongField();
					field.AddToClassList("dt-field");
					field.SetValueWithoutNotify(long.TryParse(values[i], NumberStyles.Integer, Inv, out long n) ? n : 0);
					field.RegisterValueChangedCallback(e => values[i] = e.newValue.ToString(Inv));
					row.Add(MakeIconButton(DevIcon.Shape.Minus, () => field.value -= 1, "dt-btn--stepper"));
					row.Add(field);
					row.Add(MakeIconButton(DevIcon.Shape.Plus, () => field.value += 1, "dt-btn--stepper"));
					block.Add(row);
					break;
				}
				case DevParamKind.Float:
				{
					block.Add(caption);
					var row = new VisualElement();
					row.AddToClassList("dt-param__row");
					var field = new DoubleField { formatString = "0.####" };
					field.AddToClassList("dt-field");
					field.SetValueWithoutNotify(double.TryParse(values[i], NumberStyles.Float, Inv, out double d) ? d : 0);
					field.RegisterValueChangedCallback(e => values[i] = e.newValue.ToString("R", Inv));
					row.Add(MakeIconButton(DevIcon.Shape.Minus, () => field.value -= StepOf(field.value), "dt-btn--stepper"));
					row.Add(field);
					row.Add(MakeIconButton(DevIcon.Shape.Plus, () => field.value += StepOf(field.value), "dt-btn--stepper"));
					block.Add(row);
					break;
				}
				default:
				{
					block.Add(caption);
					var field = new TextField();
					field.AddToClassList("dt-field");
					field.SetValueWithoutNotify(values[i] ?? "");
					field.RegisterValueChangedCallback(e => values[i] = e.newValue ?? "");
					block.Add(field);
					break;
				}
			}
			return block;
		}

		static double StepOf(double value) => Math.Abs(value) < 2.0 ? 0.1 : 1.0;

		// ---- ask / confirm / flash -----------------------------------------------------------------------------------

		void Ask(CommandCard card)
		{
			if (!card.Command.Confirm)
			{
				Run(card.Command);
				return;
			}
			HideConfirm();
			if (card.ConfirmRow == null)
			{
				var row = new VisualElement();
				row.AddToClassList("dt-card__confirm");
				var label = new Label("Run '" + card.Command.Label + "'?");
				label.AddToClassList("dt-card__confirm-text");
				row.Add(label);
				row.Add(MakeButton("Cancel", "dt-btn--ghost", HideConfirm));
				row.Add(MakeButton("Run", "dt-btn--danger", () =>
				{
					HideConfirm();
					Run(card.Command);
				}));
				card.ConfirmRow = row;
				card.Root.Insert(card.Root.IndexOf(card.Flash), row);
			}
			Show(card.ConfirmRow, true);
			_confirmCard = card;
			_confirmUntil = Time.unscaledTime + Tuning.ConfirmSeconds;
		}

		void HideConfirm()
		{
			if (_confirmCard?.ConfirmRow != null) Show(_confirmCard.ConfirmRow, false);
			_confirmCard = null;
		}

		void Flash(DevCommand c, bool ok)
		{
			foreach (var card in _cards)
			{
				if (card.Command != c) continue;
				card.FlashStart = Time.unscaledTime;
				card.FlashOk = ok;
				card.Flash.EnableInClassList("dt-flash--ok", ok);
				card.Flash.EnableInClassList("dt-flash--error", !ok);
			}
		}

		/// <summary>Every frame: fades the result flash (unscaled time) and times out the inline confirmation.</summary>
		void TickCards()
		{
			float now = Time.unscaledTime;
			foreach (var card in _cards)
			{
				if (card.FlashStart < 0f) continue;
				float age = now - card.FlashStart;
				if (age >= Tuning.FlashSeconds)
				{
					card.FlashStart = -1f;
					card.Flash.style.opacity = 0f;
				}
				else card.Flash.style.opacity = 1f - age / Tuning.FlashSeconds;
			}
			if (_confirmCard != null && now >= _confirmUntil) HideConfirm();
		}

		// ---- periodic refresh of what the game changes (blocked reasons, toggle states, watch values) -----------------

		void RefreshDynamic()
		{
			foreach (var card in _cards) RefreshCard(card);
			foreach (var row in _watchRows)
			{
				string text = _watchCache.TryGetValue(row.Watch, out string v) ? v : "...";
				if (row.Value.text != text) row.Value.text = text;
			}
			if (_consoleHint != null) _consoleHint.text = ConsoleHintText();
			if (DevTools.RunningScripts.Count != _runningScripts && _tab == TabScenarios) _contentDirty = true;
		}

		void RefreshCard(CommandCard card)
		{
			var c = card.Command;
			string blocked = _reg.BlockedReason(c);
			bool isBlocked = blocked != null;
			card.Root.EnableInClassList("dt-card--blocked", isBlocked);
			Show(card.BlockedChip, isBlocked);
			if (isBlocked && card.BlockedChip.text != blocked) card.BlockedChip.text = blocked;
			if (card.Run != null) Show(card.Run, !isBlocked);
			if (card.Switch != null)
			{
				Show(card.Switch, !isBlocked);
				if (!isBlocked) SetSwitch(card.Switch, SafeState(c));
			}
			if (card.ParamsBox != null) Show(card.ParamsBox, !isBlocked);
			if (isBlocked && card.ConfirmRow != null) Show(card.ConfirmRow, false);
		}

		string ConsoleHintText()
		{
			var tokens = DevRegistry.Tokenize(_consoleLine);
			string head = tokens.FirstOrDefault() ?? "";
			var exact = _reg.Find(head);
			if (exact != null) return _reg.Usage(exact) + (string.IsNullOrEmpty(exact.Help) ? "" : "  -  " + exact.Help);
			if (head.Length > 0)
			{
				var matches = _reg.Complete(head).Take(4).ToList();
				if (matches.Count > 0) return "Tab completes: " + string.Join(", ", matches);
			}
			return "help [text]  ·  wait / waitfor for scripts  ·  Tab completes  ·  Up / Down history";
		}
	}
}
#endif
