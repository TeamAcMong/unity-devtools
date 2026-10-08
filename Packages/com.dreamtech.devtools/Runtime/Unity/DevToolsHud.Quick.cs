#if UNITY_EDITOR || DEVELOPMENT_BUILD || DREAMTECH_DEVTOOLS
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace DreamTech.DevTools.Unity
{
	// The quick card: a small card beside the DEV pill with what a tester does every minute (level step / jump, win, lose,
	// restart, starred commands) and a button to the full panel. The full panel covers most of the game; this card does not.
	public sealed partial class DevToolsHud
	{
		const string LevelPreviousId = "level.previous", LevelNextId = "level.next", LevelJumpId = "level.jump-to";
		const string LevelWinId = "level.win", LevelLoseId = "level.lose", LevelRestartId = "level.restart";
		static readonly string[] QuickReservedIds = { LevelPreviousId, LevelNextId, LevelJumpId, LevelWinId, LevelLoseId, LevelRestartId };

		/// <summary>A button of the card bound to a command, re-enabled / disabled from the command's blocked reason.</summary>
		sealed class QuickButton
		{
			public DevCommand Command;
			public VisualElement Element;
			public Toggle Switch;
		}

		VisualElement _quick;
		Label _quickResult;
		LongField _quickLevelField;
		readonly List<QuickButton> _quickButtons = new List<QuickButton>();
		bool _quickOpen;
		int _quickVersion = -1;
		float _quickResultUntil;

		/// <summary>True while the quick card beside the DEV pill is shown.</summary>
		public static bool IsQuickCardOpen => _instance != null && _instance._quickOpen && !_instance._hidden;

		/// <summary>Opens / closes the quick card (the full panel closes it too).</summary>
		public static void ShowQuickCard(bool show)
		{
			if (_instance == null) return;
			if (show) _instance.OpenQuick();
			else _instance.CloseQuick();
		}

		void OpenQuick()
		{
			if (_root == null) return;
			if (_quick == null)
			{
				_quick = new VisualElement { name = "dt-quick" };
				_quick.AddToClassList("dt-quick");
				_quick.RegisterCallback<GeometryChangedEvent>(_ => PlaceQuick());
				_root.Add(_quick);
			}
			_hidden = false;
			_open = false;
			_quickOpen = true;
			if (_quickVersion != _reg.Version) BuildQuickCard();
			SyncQuickLevel();
			RefreshQuick();
			Show(_quick, true);
			PlaceQuick();
		}

		void CloseQuick()
		{
			_quickOpen = false;
			if (_quick != null) Show(_quick, false);
		}

		DevCommand QuickCommand(string id)
		{
			var c = _reg.Find(id);
			return c != null && c.Id == id ? c : null;
		}

		void BuildQuickCard()
		{
			_quickVersion = _reg.Version;
			_quick.Clear();
			_quickButtons.Clear();
			_quickLevelField = null;

			// level row: [<] [level] [>] [go]
			var previous = QuickCommand(LevelPreviousId);
			var next = QuickCommand(LevelNextId);
			var jump = QuickCommand(LevelJumpId);
			if (previous != null || next != null || jump != null)
			{
				var row = QuickRow("dt-quick__row");
				if (previous != null) AddQuickIcon(row, previous, DevIcon.Shape.ChevronLeft, "dt-btn--quick-step");
				if (jump != null)
				{
					_quickLevelField = new LongField();
					_quickLevelField.AddToClassList("dt-field");
					_quickLevelField.AddToClassList("dt-quick__level");
					// Enter jumps: typing a level and pressing Go is the most common thing a tester does
					_quickLevelField.RegisterCallback<KeyDownEvent>(e =>
					{
						if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter) return;
						JumpFromQuick(jump);
						e.StopPropagation();
					}, TrickleDown.TrickleDown);
					row.Add(_quickLevelField);
				}
				if (next != null) AddQuickIcon(row, next, DevIcon.Shape.ChevronRight, "dt-btn--quick-step");
				if (jump != null)
				{
					var go = MakeCommandButton(DevCommandStyle.Default, "Go", () => JumpFromQuick(jump));
					go.AddToClassList("dt-btn--quick-go");
					row.Add(go);
					_quickButtons.Add(new QuickButton { Command = jump, Element = go });
				}
			}

			// match row: win / lose / restart, icon + short text in their style colors
			var matchCommands = new[] { QuickCommand(LevelWinId), QuickCommand(LevelLoseId), QuickCommand(LevelRestartId) };
			if (Array.Exists(matchCommands, c => c != null))
			{
				var row = QuickRow("dt-quick__row");
				foreach (var c in matchCommands)
				{
					if (c == null) continue;
					var command = c;
					var button = MakeCommandButton(c.EffectiveStyle, c.Label, () => RunFromQuick(command));
					button.AddToClassList("dt-btn--quick-match");
					row.Add(button);
					_quickButtons.Add(new QuickButton { Command = c, Element = button });
				}
			}

			// shortcuts: starred + quick commands that need no arguments (those with arguments live in the panel)
			int shortcuts = 0;
			foreach (var c in _reg.Commands)
			{
				if (shortcuts >= Tuning.QuickMaxShortcuts) break;
				if (Array.IndexOf(QuickReservedIds, c.Id) >= 0 || !(c.Quick || _fav.Contains(c.Id))) continue;
				if (c.Kind == DevCommandKind.Action && c.Params.Length > 0) continue;
				if (shortcuts == 0) QuickCaption("SHORTCUTS");
				shortcuts++;
				var command = c;
				if (c.Kind == DevCommandKind.Toggle)
				{
					var row = QuickRow("dt-quick__toggle");
					var label = new Label(c.Label);
					label.AddToClassList("dt-quick__toggle-text");
					row.Add(label);
					var toggle = MakeSwitch(SafeState(c), on => Run(command, on ? "on" : "off"));
					row.Add(toggle);
					label.RegisterCallback<ClickEvent>(_ => toggle.value = !toggle.value);
					_quickButtons.Add(new QuickButton { Command = c, Element = row, Switch = toggle });
				}
				else
				{
					var button = MakeCommandButton(c.EffectiveStyle, c.Label, () => RunFromQuick(command));
					button.AddToClassList("dt-btn--quick-shortcut");
					_quick.Add(button);
					_quickButtons.Add(new QuickButton { Command = c, Element = button });
				}
			}

			_quickResult = new Label { pickingMode = PickingMode.Ignore };
			_quickResult.AddToClassList("dt-quick__result");
			Show(_quickResult, false);
			_quick.Add(_quickResult);

			// footer: the full panel, and close
			var footer = QuickRow("dt-quick__footer");
			var all = MakeIconButton(DevIcon.Shape.Expand, () =>
			{
				CloseQuick();
				Open(true);
			}, "dt-btn--quick-all");
			all.AddToClassList("dt-btn--with-icon");
			var allText = new Label("All tools") { pickingMode = PickingMode.Ignore };
			allText.AddToClassList("dt-btn__text");
			all.Add(allText);
			footer.Add(all);
			footer.Add(MakeIconButton(DevIcon.Shape.Close, CloseQuick));
			if (_quickButtons.Count == 0) QuickCaption("Star a command in the panel to keep it here.");
		}

		VisualElement QuickRow(string className)
		{
			var row = new VisualElement();
			row.AddToClassList(className);
			_quick.Add(row);
			return row;
		}

		void QuickCaption(string text)
		{
			var caption = new Label(text);
			caption.AddToClassList("dt-quick__caption");
			_quick.Add(caption);
		}

		void AddQuickIcon(VisualElement row, DevCommand c, DevIcon.Shape shape, string variant)
		{
			var button = MakeIconButton(shape, () => RunFromQuick(c), variant);
			row.Add(button);
			_quickButtons.Add(new QuickButton { Command = c, Element = button });
		}

		void RunFromQuick(DevCommand c)
		{
			Run(c);
			SyncQuickLevel();
		}

		void JumpFromQuick(DevCommand jump)
		{
			if (_quickLevelField == null) return;
			Run(jump, _quickLevelField.value.ToString(CultureInfo.InvariantCulture));
			SyncQuickLevel();
		}

		/// <summary>The level field shows the current level (the pinned "Level" watch) unless the tester is typing in it.</summary>
		void SyncQuickLevel()
		{
			if (_quickLevelField == null || IsTypingIn(_quickLevelField)) return;
			foreach (var w in _reg.Watches)
			{
				if (w.Category != "Level" || w.Label != "Level") continue;
				string text;
				try { text = w.Value(); }
				catch { return; }
				if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long level) && _quickLevelField.value != level)
					_quickLevelField.SetValueWithoutNotify(level);
				return;
			}
		}

		bool IsTypingIn(VisualElement element) =>
			_root?.panel?.focusController?.focusedElement is VisualElement focused && IsInsideOf(focused, element);

		static bool IsInsideOf(VisualElement element, VisualElement ancestor)
		{
			for (var e = element; e != null; e = e.parent)
				if (e == ancestor) return true;
			return false;
		}

		/// <summary>Result of a command run from the card (the panel's toast is not visible while only the card is).</summary>
		void ShowQuickResult(string text, bool ok)
		{
			if (_quickResult == null) return;
			_quickResult.text = text;
			_quickResult.EnableInClassList("dt-quick__result--error", !ok);
			Show(_quickResult, true);
			_quickResultUntil = Time.realtimeSinceStartup + (ok ? Tuning.ToastSecondsOk : Tuning.ToastSecondsError);
		}

		/// <summary>Periodic: blocked commands grey out, switches follow the game, the level field follows the level.</summary>
		void RefreshQuick()
		{
			if (_quickVersion != _reg.Version)
			{
				BuildQuickCard();
				PlaceQuick();
			}
			foreach (var button in _quickButtons)
			{
				bool runnable = _reg.BlockedReason(button.Command) == null;
				if (button.Element.enabledSelf != runnable) button.Element.SetEnabled(runnable);
				if (button.Switch != null && runnable) SetSwitch(button.Switch, SafeState(button.Command));
			}
			SyncQuickLevel();
		}

		/// <summary>Every frame while open: follow the pill (it can still be snapping) and time out the result line.</summary>
		void TickQuick()
		{
			PlaceQuick();
			if (_quickResultUntil > 0f && Time.realtimeSinceStartup >= _quickResultUntil)
			{
				_quickResultUntil = 0f;
				Show(_quickResult, false);
			}
		}

		/// <summary>
		/// Beside the pill, opening towards the middle of the screen (a pill on the left edge opens the card to its right),
		/// vertically centred on the pill and kept inside the safe area. When the side has no room it goes below / above.
		/// </summary>
		void PlaceQuick()
		{
			if (_quick == null || !_quickOpen || _pill == null) return;
			float width = _quick.resolvedStyle.width, height = _quick.resolvedStyle.height;
			if (float.IsNaN(width) || float.IsNaN(height)) return;
			Rect pill = _pillRect;
			Rect area = _safeLogical;
			bool pillOnLeft = pill.center.x < area.center.x;
			float x = pillOnLeft ? pill.xMax + Tuning.QuickGap : pill.xMin - Tuning.QuickGap - width;
			float y = pill.center.y - height * 0.5f;
			bool fitsBeside = pillOnLeft ? x + width <= area.xMax : x >= area.xMin;
			if (!fitsBeside)
			{
				x = pill.center.x - width * 0.5f;
				bool below = pill.center.y < area.center.y;
				y = below ? pill.yMax + Tuning.QuickGap : pill.yMin - Tuning.QuickGap - height;
			}
			x = Mathf.Clamp(x, area.xMin, Mathf.Max(area.xMin, area.xMax - width));
			y = Mathf.Clamp(y, area.yMin, Mathf.Max(area.yMin, area.yMax - height));
			SetTranslate(_quick, x, y);
		}
	}
}
#endif
