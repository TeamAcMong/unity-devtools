#if UNITY_EDITOR || DEVELOPMENT_BUILD || DREAMTECH_DEVTOOLS
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace DreamTech.DevTools.Unity
{
	// The Logs page: the recorded Unity console (every type), filtered by type and text, collapsible, a tap shows the stack.
	// The toolbar sits in the search slot so it stays put while the list scrolls; the Logs commands follow the list.
	public sealed partial class DevToolsHud
	{
		DevLogKinds _logKinds = DevLogKinds.All;
		string _logSearch = "";
		bool _logCollapse;
		string _logExpanded; // key of the open row: its id, or its type + message when collapsed
		int _logViewVersion = -1;
		readonly List<DevLogFilter.Row> _logRows = new List<DevLogFilter.Row>();
		VisualElement _logToolbar, _logList;
		TextField _logSearchField;
		Label _logSearchPlaceholder;
		Button _logChipErrors, _logChipWarnings, _logChipLogs, _logChipCollapse;

		bool IsLogsTab => _tab == LogsModule.Category;

		VisualElement BuildLogToolbar()
		{
			if (_logToolbar != null)
			{
				RefreshLogChips();
				return _logToolbar;
			}
			_logToolbar = new VisualElement();
			_logToolbar.AddToClassList("dt-logbar");

			var box = new VisualElement();
			box.AddToClassList("dt-searchbox");
			box.Add(new DevIcon(DevIcon.Shape.Search));
			_logSearchField = new TextField();
			_logSearchField.AddToClassList("dt-field");
			_logSearchField.AddToClassList("dt-searchbox__field");
			box.Add(_logSearchField);
			_logSearchPlaceholder = new Label("Filter logs...") { pickingMode = PickingMode.Ignore };
			_logSearchPlaceholder.AddToClassList("dt-searchbox__placeholder");
			box.Add(_logSearchPlaceholder);
			_logSearchField.RegisterValueChangedCallback(e =>
			{
				_logSearch = e.newValue ?? "";
				Show(_logSearchPlaceholder, _logSearch.Length == 0);
				RedrawLogs();
			});
			_logSearchField.RegisterCallback<FocusInEvent>(_ => box.AddToClassList("dt-searchbox--focus"));
			_logSearchField.RegisterCallback<FocusOutEvent>(_ => box.RemoveFromClassList("dt-searchbox--focus"));
			_logToolbar.Add(box);

			var chips = new VisualElement();
			chips.AddToClassList("dt-logbar__chips");
			_logChipErrors = MakeLogChip("dt-logchip--error", () => ToggleLogKind(DevLogKinds.Error));
			_logChipWarnings = MakeLogChip("dt-logchip--warning", () => ToggleLogKind(DevLogKinds.Warning));
			_logChipLogs = MakeLogChip("dt-logchip--log", () => ToggleLogKind(DevLogKinds.Log));
			_logChipCollapse = MakeLogChip(null, () =>
			{
				_logCollapse = !_logCollapse;
				_logExpanded = null;
				RefreshLogChips();
				RedrawLogs();
			});
			_logChipCollapse.text = "Collapse";
			chips.Add(_logChipErrors);
			chips.Add(_logChipWarnings);
			chips.Add(_logChipLogs);
			chips.Add(_logChipCollapse);
			var spacer = new VisualElement();
			spacer.AddToClassList("dt-spacer");
			chips.Add(spacer);
			var copy = MakeButton("Copy", "dt-btn--ghost", CopyLogs);
			var clear = MakeButton("Clear", "dt-btn--ghost", () =>
			{
				DevToolsHost.ClearLogs();
				_logExpanded = null;
				RefreshLogChips();
				RedrawLogs();
			});
			copy.AddToClassList("dt-btn--small");
			clear.AddToClassList("dt-btn--small");
			chips.Add(copy);
			chips.Add(clear);
			_logToolbar.Add(chips);

			_logSearchField.SetValueWithoutNotify(_logSearch);
			Show(_logSearchPlaceholder, _logSearch.Length == 0);
			RefreshLogChips();
			return _logToolbar;
		}

		static Button MakeLogChip(string variant, System.Action onClick)
		{
			var chip = new Button(onClick);
			chip.AddToClassList("dt-logchip");
			if (variant != null) chip.AddToClassList(variant);
			return chip;
		}

		void ToggleLogKind(DevLogKinds kind)
		{
			_logKinds ^= kind;
			RefreshLogChips();
			RedrawLogs();
		}

		void RefreshLogChips()
		{
			if (_logToolbar == null) return;
			SetChip(_logChipErrors, "Errors " + DevToolsHost.ErrorCount, (_logKinds & DevLogKinds.Error) != 0);
			SetChip(_logChipWarnings, "Warnings " + DevToolsHost.WarningCount, (_logKinds & DevLogKinds.Warning) != 0);
			SetChip(_logChipLogs, "Logs " + DevToolsHost.LogCount, (_logKinds & DevLogKinds.Log) != 0);
			_logChipCollapse.EnableInClassList("dt-logchip--on", _logCollapse);
		}

		static void SetChip(Button chip, string text, bool on)
		{
			if (chip.text != text) chip.text = text;
			chip.EnableInClassList("dt-logchip--on", on);
		}

		/// <summary>The list host on the Logs page; filled by <see cref="RefreshLogList"/>.</summary>
		void BuildLogList()
		{
			_logList = new VisualElement();
			_logList.AddToClassList("dt-loglist");
			_body.Add(_logList);
			RedrawLogs();
		}

		void RedrawLogs()
		{
			_logViewVersion = -1;
			RefreshLogList();
		}

		/// <summary>Rebuilds the rows only when the console changed since the last draw (called on the periodic refresh).</summary>
		void RefreshLogList()
		{
			if (_logList == null || _logViewVersion == DevToolsHost.LogVersion) return;
			_logViewVersion = DevToolsHost.LogVersion;
			var logs = DevToolsHost.Logs;
			DevLogFilter.Build(logs, _logKinds, _logSearch, _logCollapse, Tuning.MaxConsoleRows, _logRows);
			_logList.Clear();
			if (_logRows.Count == 0)
			{
				var empty = new Label(logs.Count == 0 ? "Nothing logged yet." : "Nothing matches the filters.");
				empty.AddToClassList("dt-empty");
				_logList.Add(empty);
				return;
			}
			foreach (var row in _logRows) _logList.Add(BuildLogRow(logs[row.Index], row.Count));
			if (_logRows.Count >= Tuning.MaxConsoleRows)
			{
				var more = new Label("Newest " + Tuning.MaxConsoleRows + " shown. Filter or search to narrow it down.");
				more.AddToClassList("dt-hint");
				_logList.Add(more);
			}
		}

		VisualElement BuildLogRow(DevToolsHost.LogRecord entry, int count)
		{
			string key = _logCollapse ? (int)entry.Type + ":" + entry.Message : entry.Id.ToString(Inv);
			bool open = key == _logExpanded;
			var row = new VisualElement();
			row.AddToClassList("dt-logrow");
			var kind = DevLogFilter.KindOf(entry.Type);
			row.AddToClassList(kind == DevLogKinds.Error ? "dt-logrow--error" : kind == DevLogKinds.Warning ? "dt-logrow--warning" : "dt-logrow--log");

			var head = new VisualElement();
			head.AddToClassList("dt-logrow__head");
			var time = new Label(entry.Time.ToString("0.0", Inv) + "s" + (entry.Type == LogType.Exception ? "  exception" : entry.Type == LogType.Assert ? "  assert" : ""));
			time.AddToClassList("dt-logrow__time");
			head.Add(time);
			if (count > 1)
			{
				var badge = new Label(count.ToString(Inv));
				badge.AddToClassList("dt-logrow__count");
				head.Add(badge);
			}
			row.Add(head);

			var message = new Label(open ? entry.Message : Clip(FirstLineOf(entry.Message), Tuning.ConsoleRowCharacters));
			message.AddToClassList("dt-logrow__message");
			row.Add(message);

			if (open)
			{
				var stack = new Label(string.IsNullOrEmpty(entry.Stack) ? "No stack trace recorded for this type (Player Settings > Stack Trace)." : entry.Stack.TrimEnd());
				stack.AddToClassList("dt-logrow__stack");
				row.Add(stack);
				var copy = MakeButton("Copy", "dt-btn--ghost", () =>
				{
					GUIUtility.systemCopyBuffer = Describe(entry, count);
					ShowToast("copied", true);
				});
				copy.AddToClassList("dt-btn--small");
				copy.AddToClassList("dt-logrow__copy");
				copy.RegisterCallback<ClickEvent>(e => e.StopPropagation()); // copying must not also fold the row
				row.Add(copy);
			}

			row.RegisterCallback<ClickEvent>(_ =>
			{
				_logExpanded = open ? null : key;
				RedrawLogs();
			});
			return row;
		}

		/// <summary>Copies what the list shows (filters applied, newest first), stacks included.</summary>
		void CopyLogs()
		{
			var logs = DevToolsHost.Logs;
			DevLogFilter.Build(logs, _logKinds, _logSearch, _logCollapse, DevToolsHost.MaxLogRecords, _logRows);
			var sb = new StringBuilder();
			foreach (var row in _logRows) sb.Append(Describe(logs[row.Index], row.Count)).Append('\n');
			GUIUtility.systemCopyBuffer = sb.ToString();
			ShowToast("copied " + _logRows.Count + " log entr" + (_logRows.Count == 1 ? "y" : "ies"), true);
			_logViewVersion = -1; // _logRows now holds the uncapped list: redraw from scratch next time
		}

		static string Describe(DevToolsHost.LogRecord entry, int count)
		{
			var sb = new StringBuilder();
			sb.Append('[').Append(entry.Type).Append(" @").Append(entry.Time.ToString("0.0", Inv)).Append("s");
			if (count > 1) sb.Append(" x").Append(count);
			sb.Append("] ").Append(entry.Message);
			if (!string.IsNullOrEmpty(entry.Stack)) sb.Append('\n').Append(entry.Stack.TrimEnd());
			return sb.ToString();
		}

		static string FirstLineOf(string text)
		{
			int i = text.IndexOf('\n');
			return i < 0 ? text : text.Substring(0, i);
		}

		static string Clip(string text, int max) => text.Length > max ? text.Substring(0, max - 3) + "..." : text;
	}
}
#endif
