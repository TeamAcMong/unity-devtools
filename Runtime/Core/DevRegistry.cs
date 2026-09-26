using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;

namespace DreamTech.DevTools
{
	/// <summary>
	/// A group of commands, watches, conditions and presets. Mark a class with <see cref="DevModuleAttribute"/> (and give it
	/// a parameterless constructor) to have it registered at startup, or install an instance yourself with
	/// <see cref="DevTools.Install(IDevModule)"/> when it needs references from the game.
	/// </summary>
	public interface IDevModule
	{
		void Register(DevRegistry r);
	}

	/// <summary>Auto-registration of an <see cref="IDevModule"/>; lower orders register (and are listed) first.</summary>
	[AttributeUsage(AttributeTargets.Class, Inherited = false)]
	public sealed class DevModuleAttribute : Attribute
	{
		public readonly int Order;

		public DevModuleAttribute(int order = 1000)
		{
			Order = order;
		}
	}

	/// <summary>
	/// Registry and interpreter of dev commands, shared by every front end. Main thread only.
	/// Never throws from <see cref="Execute(DevCommand, IList{string})"/>: failures come back as <see cref="DevResult.Fail"/>.
	/// </summary>
	public sealed class DevRegistry
	{
		public const int MaxLog = 400;

		readonly List<DevCommand> _commands = new List<DevCommand>();
		readonly Dictionary<string, DevCommand> _byId = new Dictionary<string, DevCommand>(StringComparer.OrdinalIgnoreCase);
		readonly List<DevWatch> _watches = new List<DevWatch>();
		readonly List<DevPreset> _presets = new List<DevPreset>();
		readonly List<string> _categories = new List<string>();
		readonly Dictionary<string, KeyValuePair<object, Func<bool>>> _conditions = new Dictionary<string, KeyValuePair<object, Func<bool>>>(StringComparer.OrdinalIgnoreCase);
		readonly Dictionary<string, KeyValuePair<object, Func<object>>> _roots = new Dictionary<string, KeyValuePair<object, Func<object>>>(StringComparer.OrdinalIgnoreCase);
		readonly List<KeyValuePair<object, Func<IEnumerable<KeyValuePair<string, object>>>>> _rootProviders = new List<KeyValuePair<object, Func<IEnumerable<KeyValuePair<string, object>>>>>();
		readonly Stopwatch _stopwatch = Stopwatch.StartNew();
		object _owner;

		public readonly List<DevLogEntry> Log = new List<DevLogEntry>();

		/// <summary>Every executed line and every posted result (scripts, scheduled work).</summary>
		public event Action<DevLogEntry> Executed;

		/// <summary>Unexpected exceptions thrown by command code (the Unity layer forwards them to the console).</summary>
		public event Action<Exception> CommandException;

		/// <summary>Seconds, monotonic. The Unity host replaces it with unscaled real time.</summary>
		public Func<double> Clock;

		/// <summary>Bumped on every registration change, so front ends can rebuild cached lists.</summary>
		public int Version { get; private set; }

		public IReadOnlyList<DevCommand> Commands => _commands;
		public IReadOnlyList<DevWatch> Watches => _watches;
		public IReadOnlyList<DevPreset> Presets => _presets;

		/// <summary>Categories in registration order: the order front ends show them in.</summary>
		public IReadOnlyList<string> Categories => _categories;

		public IEnumerable<string> ConditionNames => _conditions.Keys.OrderBy(k => k, StringComparer.Ordinal);
		/// <summary>Names the Inspector can browse: fixed roots and those of root providers (save ports).</summary>
		public IEnumerable<string> RootNames => _roots.Keys.Concat(ProvidedRoots().Select(kv => kv.Key)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(k => k, StringComparer.Ordinal);

		public DevRegistry()
		{
			Clock = () => _stopwatch.Elapsed.TotalSeconds;
		}

		public double Now => Clock();

		// ---- modules -------------------------------------------------------------------------------------------------

		/// <summary>Registers every <see cref="DevModuleAttribute"/> module found in the given assemblies, by order.</summary>
		public int RegisterModules(IEnumerable<Assembly> assemblies)
		{
			var found = new List<KeyValuePair<int, Type>>();
			foreach (var asm in assemblies)
			{
				Type[] types;
				try { types = asm.GetTypes(); }
				catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
				catch { continue; }
				foreach (var t in types)
				{
					if (t.IsAbstract || t.IsInterface || !typeof(IDevModule).IsAssignableFrom(t)) continue;
					var attr = t.GetCustomAttribute<DevModuleAttribute>(false);
					if (attr != null) found.Add(new KeyValuePair<int, Type>(attr.Order, t));
				}
			}
			int n = 0;
			foreach (var kv in found.OrderBy(k => k.Key).ThenBy(k => k.Value.FullName, StringComparer.Ordinal))
			{
				try
				{
					var module = (IDevModule)Activator.CreateInstance(kv.Value, true);
					RegisterModule(module, module);
					n++;
				}
				catch (Exception e)
				{
					CommandException?.Invoke(new InvalidOperationException("DevTools module " + kv.Value.FullName + " failed to register", e));
				}
			}
			return n;
		}

		/// <summary>Registers a module; everything it adds belongs to <paramref name="owner"/>.</summary>
		public void RegisterModule(IDevModule module, object owner)
		{
			var prev = _owner;
			_owner = owner;
			try { module.Register(this); }
			finally { _owner = prev; }
		}

		/// <summary>Removes every command, watch, condition, inspect root and preset registered by an owner.</summary>
		public void Remove(object owner)
		{
			if (owner == null) return;
			foreach (var c in _commands.Where(c => c.Owner == owner).ToList())
			{
				_commands.Remove(c);
				_byId.Remove(c.Id);
			}
			_watches.RemoveAll(w => w.Owner == owner);
			_presets.RemoveAll(p => p.Owner == owner);
			foreach (var k in _conditions.Where(kv => kv.Value.Key == owner).Select(kv => kv.Key).ToList()) _conditions.Remove(k);
			foreach (var k in _roots.Where(kv => kv.Value.Key == owner).Select(kv => kv.Key).ToList()) _roots.Remove(k);
			_rootProviders.RemoveAll(p => p.Key == owner);
			_categories.RemoveAll(cat => !_commands.Any(c => c.Category == cat) && !_watches.Any(w => w.Category == cat));
			Version++;
		}

		// ---- registration --------------------------------------------------------------------------------------------

		public DevCommand Add(DevCommand c)
		{
			if (c == null) throw new ArgumentNullException(nameof(c));
			if (string.IsNullOrEmpty(c.Category)) c.Category = "Misc";
			if (string.IsNullOrEmpty(c.Id)) c.Id = Slug(c.Category) + "." + Slug(c.Label);
			if (c.Owner == null) c.Owner = _owner;
			if (_byId.TryGetValue(c.Id, out var existing))
			{
				// the newest registration wins (a game module overriding a standard command, a port installed again)
				_commands.Remove(existing);
			}
			AddCategory(c.Category);
			_commands.Add(c);
			_byId[c.Id] = c;
			Version++;
			return c;
		}

		/// <summary>Command without arguments.</summary>
		public DevCommand Action(string category, string label, Func<DevResult> run, string help = null, Func<string> blocked = null, string id = null) =>
			Add(new DevCommand { Category = category, Label = label, Help = help, Run = _ => run(), Blocked = blocked, Id = id });

		/// <summary>Command with typed parameters.</summary>
		public DevCommand Action(string category, string label, DevParam[] ps, Func<DevArgs, DevResult> run, string help = null, Func<string> blocked = null, string id = null) =>
			Add(new DevCommand { Category = category, Label = label, Help = help, Params = ps ?? Array.Empty<DevParam>(), Run = run, Blocked = blocked, Id = id });

		/// <summary>On/off switch. Console form: "&lt;id&gt; on|off" (no argument flips it).</summary>
		public DevCommand Toggle(string category, string label, Func<bool> state, Action<bool> set, string help = null, Func<string> blocked = null, string id = null) =>
			Add(new DevCommand
			{
				Category = category, Label = label, Help = help, Kind = DevCommandKind.Toggle, State = state, Blocked = blocked, Id = id,
				Params = new[] { DevParam.Text("on|off", "") },
				Run = a =>
				{
					bool v = string.IsNullOrEmpty(a.Str(0)) ? !state() : a.Bool(0);
					set(v);
					return DevResult.Success(label + ": " + (state() ? "ON" : "OFF"));
				},
			});

		public DevWatch Watch(string category, string label, Func<string> value, bool pinned = false, float interval = 0.25f)
		{
			var w = new DevWatch { Category = category, Label = label, Value = value, Pinned = pinned, Interval = interval, Owner = _owner };
			AddCategory(category);
			_watches.RemoveAll(x => x.Category == category && x.Label == label);
			_watches.Add(w);
			Version++;
			return w;
		}

		/// <summary>Named state for scripts: "waitfor &lt;name&gt; [timeout]".</summary>
		public void Condition(string name, Func<bool> test)
		{
			_conditions[name] = new KeyValuePair<object, Func<bool>>(_owner, test);
			Version++;
		}

		public bool TryCondition(string name, out Func<bool> test)
		{
			test = null;
			if (name == null || !_conditions.TryGetValue(name, out var kv)) return false;
			test = kv.Value;
			return true;
		}

		/// <summary>A ready-made script (Scenarios tab). Lines are console lines, wait and waitfor.</summary>
		public DevPreset Preset(string name, string script)
		{
			_presets.RemoveAll(p => p.Name == name);
			var p = new DevPreset { Name = name, Script = script, Owner = _owner };
			_presets.Add(p);
			Version++;
			return p;
		}

		/// <summary>An object the Inspector category can browse and edit by path ("Player.Stats.Hp").</summary>
		public void InspectRoot(string name, Func<object> root)
		{
			_roots[name] = new KeyValuePair<object, Func<object>>(_owner, root);
			Version++;
		}

		/// <summary>A changing set of named roots (e.g. the save objects of a save port), read each time it is needed.</summary>
		public void InspectRoots(Func<IEnumerable<KeyValuePair<string, object>>> provider)
		{
			_rootProviders.Add(new KeyValuePair<object, Func<IEnumerable<KeyValuePair<string, object>>>>(_owner, provider));
			Version++;
		}

		public bool TryRoot(string name, out object value)
		{
			value = null;
			if (name == null) return false;
			if (_roots.TryGetValue(name, out var kv))
			{
				value = kv.Value();
				return true;
			}
			foreach (var p in ProvidedRoots())
				if (string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase))
				{
					value = p.Value;
					return true;
				}
			return false;
		}

		IEnumerable<KeyValuePair<string, object>> ProvidedRoots()
		{
			foreach (var p in _rootProviders)
			{
				IEnumerable<KeyValuePair<string, object>> items;
				try { items = p.Value()?.ToList(); }
				catch { items = null; }
				if (items == null) continue;
				foreach (var kv in items)
					if (!string.IsNullOrEmpty(kv.Key) && kv.Value != null) yield return kv;
			}
		}

		void AddCategory(string category)
		{
			if (!_categories.Contains(category)) _categories.Add(category);
		}

		public static string Slug(string s)
		{
			var sb = new StringBuilder();
			foreach (char ch in s ?? "")
			{
				if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
				else if (sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
			}
			return sb.ToString().Trim('-');
		}

		// ---- lookup ----------------------------------------------------------------------------------------------------

		public DevCommand Find(string id) => id != null && _byId.TryGetValue(id.Trim(), out var c) ? c : null;

		public IEnumerable<DevCommand> InCategory(string category) => _commands.Where(c => c.Category == category);

		/// <summary>Commands whose id, label, category or help contain every word of the text.</summary>
		public IEnumerable<DevCommand> Search(string text)
		{
			if (string.IsNullOrWhiteSpace(text)) return _commands;
			string[] words = text.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
			return _commands.Where(c =>
			{
				string hay = (c.Id + " " + c.Label + " " + c.Category + " " + c.Help).ToLowerInvariant();
				return words.All(hay.Contains);
			});
		}

		/// <summary>Exact id, else a unique id prefix.</summary>
		public DevCommand Resolve(string id, out string error)
		{
			error = null;
			var c = Find(id);
			if (c != null) return c;
			var m = _commands.Where(x => x.Id.StartsWith(id ?? "", StringComparison.OrdinalIgnoreCase)).ToList();
			if (m.Count == 1) return m[0];
			error = m.Count > 1
				? "ambiguous '" + id + "': " + string.Join(", ", m.Take(8).Select(x => x.Id))
				: "unknown command '" + id + "' (try 'help')";
			return null;
		}

		public IEnumerable<string> Complete(string prefix) =>
			_commands.Select(c => c.Id).Where(id => id.StartsWith(prefix ?? "", StringComparison.OrdinalIgnoreCase)).OrderBy(s => s, StringComparer.Ordinal);

		public string Usage(DevCommand c) =>
			c.Id + string.Concat(c.Params.Select(p => " <" + p.Name + (string.IsNullOrEmpty(p.Default) ? "" : "=" + p.Default) + ">"));

		public string BlockedReason(DevCommand c)
		{
			if (c.Blocked == null) return null;
			try { return c.Blocked(); }
			catch (Exception e) { return e.GetType().Name + ": " + e.Message; }
		}

		// ---- execution -------------------------------------------------------------------------------------------------

		public DevResult Execute(DevCommand c, IList<string> args)
		{
			string line = c.Id + (args != null && args.Count > 0 ? " " + string.Join(" ", args.Select(Quote)) : "");
			DevResult res;
			string blocked = BlockedReason(c);
			if (!string.IsNullOrEmpty(blocked)) res = DevResult.Fail(blocked);
			else
			{
				try { res = c.Run(new DevArgs(c.Params, args)); }
				catch (FormatException e) { res = DevResult.Fail(e.Message); }
				catch (Exception e)
				{
					res = DevResult.Fail(e.GetType().Name + ": " + e.Message);
					CommandException?.Invoke(e);
				}
			}
			Record(line, res.Ok, res.Message);
			return res;
		}

		/// <summary>Runs one console line "&lt;id&gt; args...".</summary>
		public DevResult Execute(string line)
		{
			var tokens = Tokenize(line);
			if (tokens.Count == 0) return DevResult.Fail("empty command");
			var c = Resolve(tokens[0], out string err);
			if (c == null)
			{
				Record(line, false, err);
				return DevResult.Fail(err);
			}
			return Execute(c, tokens.Skip(1).ToList());
		}

		/// <summary>Result of work that finishes later (multi-frame automation, script steps).</summary>
		public void Post(string source, bool ok, string message) => Record(source, ok, message);

		void Record(string line, bool ok, string message)
		{
			var entry = new DevLogEntry { Time = Now, Line = line, Ok = ok, Message = message ?? "" };
			Log.Add(entry);
			if (Log.Count > MaxLog) Log.RemoveRange(0, Log.Count - MaxLog);
			Executed?.Invoke(entry);
		}

		/// <summary>Splits on spaces; double quotes group ("a b" is one token, "" an empty one).</summary>
		public static List<string> Tokenize(string line)
		{
			var list = new List<string>();
			if (line == null) return list;
			var sb = new StringBuilder();
			bool quoted = false, any = false;
			foreach (char ch in line)
			{
				if (ch == '"')
				{
					quoted = !quoted;
					any = true;
					continue;
				}
				if (char.IsWhiteSpace(ch) && !quoted)
				{
					if (any || sb.Length > 0) list.Add(sb.ToString());
					sb.Clear();
					any = false;
					continue;
				}
				sb.Append(ch);
			}
			if (any || sb.Length > 0) list.Add(sb.ToString());
			return list;
		}

		public static string Quote(string s) => s == null ? "\"\"" : s.Length == 0 || s.IndexOf(' ') >= 0 ? "\"" + s + "\"" : s;

		/// <summary>A console line that runs <paramref name="c"/> with these values.</summary>
		public static string LineFor(DevCommand c, IList<string> values) =>
			values == null || values.Count == 0 ? c.Id : c.Id + " " + string.Join(" ", values.Select(Quote));
	}
}
