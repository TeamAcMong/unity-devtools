using System;
using System.Collections.Generic;
using System.Globalization;

namespace DreamTech.DevTools
{
	public enum DevParamKind
	{
		Int,
		Float,
		Text,
		Bool,
		Choice
	}

	/// <summary>
	/// One argument of a command. Choice parameters list their values (an enum's names, item ids, screen names...);
	/// <see cref="Options"/> runs each time a front end draws the field, so it can follow live game state.
	/// </summary>
	public sealed class DevParam
	{
		public string Name;
		public DevParamKind Kind;
		public string Default;
		public Func<IList<string>> Options;

		static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

		public static DevParam Int(string name, long def = 0) => new DevParam { Name = name, Kind = DevParamKind.Int, Default = def.ToString(Inv) };

		public static DevParam Float(string name, double def = 0) => new DevParam { Name = name, Kind = DevParamKind.Float, Default = def.ToString(Inv) };

		public static DevParam Text(string name, string def = "") => new DevParam { Name = name, Kind = DevParamKind.Text, Default = def ?? "" };

		public static DevParam Bool(string name, bool def = false) => new DevParam { Name = name, Kind = DevParamKind.Bool, Default = def ? "true" : "false" };

		public static DevParam Choice(string name, Func<IList<string>> options, string def = null) =>
			new DevParam { Name = name, Kind = DevParamKind.Choice, Options = options, Default = def ?? "" };

		public static DevParam Enum<T>(string name, T def) where T : struct, System.Enum =>
			Choice(name, () => System.Enum.GetNames(typeof(T)), def.ToString());
	}

	public enum DevCommandKind
	{
		Action,
		Toggle
	}

	/// <summary>
	/// A dev action. Every front end (in-game HUD, editor window, scripts) runs the same objects through
	/// <see cref="DevRegistry"/>, so a command added once is reachable everywhere.
	/// Id = "&lt;category&gt;.&lt;label&gt;" slugged, e.g. "economy.set-balance"; a console line is "&lt;id&gt; arg arg..."
	/// (quote arguments that contain spaces).
	/// </summary>
	public sealed class DevCommand
	{
		public string Id;
		public string Category;
		public string Label;
		public string Help;
		public DevCommandKind Kind;
		public DevParam[] Params = Array.Empty<DevParam>();
		public Func<DevArgs, DevResult> Run;

		/// <summary>Toggle commands: current state (Run receives "on" / "off" as argument 0).</summary>
		public Func<bool> State;

		/// <summary>Null when runnable, otherwise why the command cannot run now (e.g. "not in a level").</summary>
		public Func<string> Blocked;

		/// <summary>Listed in the HUD's Quick tab.</summary>
		public bool Quick;

		/// <summary>Front ends ask before running it (save wipe, reset...). Scripts do not ask.</summary>
		public bool Confirm;

		/// <summary>Who registered it; <see cref="DevRegistry.Remove(object)"/> drops everything of an owner.</summary>
		public object Owner;
	}

	public readonly struct DevResult
	{
		public readonly bool Ok;
		public readonly string Message;

		DevResult(bool ok, string message)
		{
			Ok = ok;
			Message = message ?? "";
		}

		public static DevResult Success(string message = "ok") => new DevResult(true, message);

		public static DevResult Fail(string message) => new DevResult(false, message);

		public override string ToString() => (Ok ? "" : "error: ") + Message;
	}

	/// <summary>A live value shown by the HUD's status strip and Watch tab and by the editor window.</summary>
	public sealed class DevWatch
	{
		public string Category;
		public string Label;
		public Func<string> Value;

		/// <summary>Also shown in the always-visible DEV pill.</summary>
		public bool Pinned;

		/// <summary>Seconds between evaluations while shown; raise it for expensive values.</summary>
		public float Interval = 0.25f;

		public object Owner;
	}

	/// <summary>A named script shown in the Scenarios tab and the editor window's preset list.</summary>
	public sealed class DevPreset
	{
		public string Name;
		public string Script;
		public object Owner;
	}

	public sealed class DevLogEntry
	{
		public double Time;
		public string Line;
		public bool Ok;
		public string Message;
	}

	/// <summary>Parsed arguments of one invocation, typed by the command's parameters (defaults fill missing ones).</summary>
	public sealed class DevArgs
	{
		static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
		readonly DevParam[] _params;
		readonly string[] _values;

		public DevArgs(DevParam[] ps, IList<string> values)
		{
			_params = ps ?? Array.Empty<DevParam>();
			_values = new string[Math.Max(_params.Length, values?.Count ?? 0)];
			for (int i = 0; i < _values.Length; i++)
				_values[i] = values != null && i < values.Count && values[i] != null ? values[i] : i < _params.Length ? _params[i].Default : null;
		}

		public int Count => _values.Length;

		public string this[int i] => i < _values.Length ? _values[i] : null;

		public string Str(int i) => this[i] ?? "";

		public long Long(int i)
		{
			string s = Str(i).Trim();
			if (long.TryParse(s, NumberStyles.Integer, Inv, out long v)) return v;
			if (double.TryParse(s, NumberStyles.Float, Inv, out double d) && d >= long.MinValue && d <= long.MaxValue) return (long)d;
			throw Bad(i, "an integer");
		}

		public int Int(int i)
		{
			long v = Long(i);
			if (v < int.MinValue || v > int.MaxValue) throw Bad(i, "a 32-bit integer");
			return (int)v;
		}

		public double Double(int i)
		{
			string s = Str(i).Trim();
			if (double.TryParse(s, NumberStyles.Float, Inv, out double v)) return v;
			throw Bad(i, "a number");
		}

		public float Float(int i) => (float)Double(i);

		public bool Bool(int i)
		{
			switch (Str(i).Trim().ToLowerInvariant())
			{
				case "1":
				case "true":
				case "on":
				case "yes":
					return true;
				case "0":
				case "false":
				case "off":
				case "no":
				case "":
					return false;
			}
			throw Bad(i, "on/off");
		}

		public T Enum<T>(int i) where T : struct, System.Enum
		{
			string s = Str(i).Trim();
			if (System.Enum.TryParse(s, true, out T v) && System.Enum.IsDefined(typeof(T), v)) return v;
			throw Bad(i, "one of " + string.Join("|", System.Enum.GetNames(typeof(T))));
		}

		FormatException Bad(int i, string what) =>
			new FormatException("argument " + (i + 1) + " (" + (i < _params.Length ? _params[i].Name : "?") + ") must be " + what + ", got '" + Str(i) + "'");
	}
}
