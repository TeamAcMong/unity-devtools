using System;
using System.Collections.Generic;
using System.Globalization;

namespace DreamTech.DevTools
{
	/// <summary>
	/// A dev script: console lines separated by ';' or new lines, plus two built-ins:
	/// <c>wait &lt;seconds&gt;</c> and <c>waitfor &lt;condition&gt; [timeout=60]</c>. Lines starting with # or // are comments.
	/// It runs one command per <see cref="Tick"/> (one per frame under the Unity host) so the game reacts between lines.
	/// </summary>
	public sealed class DevScriptRun
	{
		enum Kind
		{
			Command,
			Wait,
			WaitFor
		}

		struct Step
		{
			public Kind Kind;
			public string Line;
			public double Seconds;
			public string Condition;
		}

		static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
		readonly DevRegistry _registry;
		readonly List<Step> _steps = new List<Step>();
		int _index;
		double _until = -1, _started;

		public readonly string Source;
		public bool Finished { get; private set; }
		public bool Cancelled { get; private set; }
		public int StepCount => _steps.Count;
		public int StepIndex => _index;

		public DevScriptRun(DevRegistry registry, string script, string source = "script")
		{
			_registry = registry ?? throw new ArgumentNullException(nameof(registry));
			Source = string.IsNullOrEmpty(source) ? "script" : source;
			foreach (string raw in Split(script))
			{
				var t = DevRegistry.Tokenize(raw);
				if (t.Count == 0) continue;
				if (t[0] == "wait")
					_steps.Add(new Step { Kind = Kind.Wait, Line = raw, Seconds = t.Count > 1 && double.TryParse(t[1], NumberStyles.Float, Inv, out double s) ? Math.Max(0, s) : 1 });
				else if (t[0] == "waitfor")
					_steps.Add(new Step
					{
						Kind = Kind.WaitFor, Line = raw, Condition = t.Count > 1 ? t[1] : "",
						Seconds = t.Count > 2 && double.TryParse(t[2], NumberStyles.Float, Inv, out double to) ? Math.Max(0, to) : 60,
					});
				else _steps.Add(new Step { Kind = Kind.Command, Line = raw });
			}
		}

		/// <summary>Non-empty, non-comment lines of a script.</summary>
		public static IEnumerable<string> Split(string script)
		{
			if (string.IsNullOrEmpty(script)) yield break;
			foreach (string raw in script.Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
			{
				string line = raw.Trim();
				if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith("//", StringComparison.Ordinal)) continue;
				yield return line;
			}
		}

		/// <summary>True when a console line should run as a script rather than a single command.</summary>
		public static bool LooksLikeScript(string line)
		{
			if (string.IsNullOrEmpty(line)) return false;
			if (line.IndexOf(';') >= 0 || line.IndexOf('\n') >= 0) return true;
			var t = DevRegistry.Tokenize(line);
			return t.Count > 0 && (t[0] == "wait" || t[0] == "waitfor");
		}

		public void Cancel()
		{
			if (Finished) return;
			Cancelled = Finished = true;
			_registry.Post(Source, false, "cancelled at step " + (_index + 1) + "/" + _steps.Count);
		}

		/// <summary>Advances the script; returns true once it has finished.</summary>
		public bool Tick(double now)
		{
			while (!Finished && _index < _steps.Count)
			{
				var s = _steps[_index];
				switch (s.Kind)
				{
					case Kind.Wait:
						if (_until < 0) _until = now + s.Seconds;
						if (now < _until) return false;
						_until = -1;
						_index++;
						continue;

					case Kind.WaitFor:
					{
						if (!_registry.TryCondition(s.Condition, out var test))
						{
							_registry.Post(Source + ": " + s.Line, false, "unknown condition '" + s.Condition + "'; known: " + string.Join(", ", _registry.ConditionNames));
							_index++;
							continue;
						}
						if (_until < 0)
						{
							_until = now + s.Seconds;
							_started = now;
						}
						bool ok;
						try { ok = test(); }
						catch { ok = false; }
						if (ok || now >= _until)
						{
							_registry.Post(Source + ": " + s.Line, ok, ok ? "reached after " + (now - _started).ToString("0.0", Inv) + " s" : "TIMEOUT after " + s.Seconds.ToString("0.#", Inv) + " s");
							_until = -1;
							_index++;
							if (ok) continue;
						}
						return false;
					}

					default:
						_registry.Execute(s.Line);
						_index++;
						return _index >= _steps.Count && Complete();
				}
			}
			return Complete();
		}

		bool Complete()
		{
			Finished = true;
			return true;
		}
	}
}
