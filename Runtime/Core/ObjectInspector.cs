using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace DreamTech.DevTools
{
	/// <summary>
	/// Reads and writes public fields / properties of live objects by path: <c>Root.Member.List[2].Dict["key"].Field</c>.
	/// Only numbers, bools, text and enums can be written. Value-type elements of lists are written back; members of a
	/// struct nested inside another struct are not.
	/// </summary>
	public static class ObjectInspector
	{
		static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

		/// <summary>Resolves <paramref name="path"/> below <paramref name="root"/>; <paramref name="setter"/> assigns the last hop.</summary>
		public static bool TryResolve(object root, string path, out object value, out Action<object> setter, out string error)
		{
			value = root;
			setter = null;
			error = null;
			foreach (string part in Split(path))
			{
				object owner = value;
				if (owner == null)
				{
					error = "null before '" + part + "'";
					return false;
				}
				if (part.StartsWith("[", StringComparison.Ordinal))
				{
					string key = part.Substring(1, part.Length - 2).Trim().Trim('"');
					if (owner is IList list && int.TryParse(key, NumberStyles.Integer, Inv, out int idx))
					{
						if (idx < 0 || idx >= list.Count)
						{
							error = "index " + idx + " out of range (count " + list.Count + ")";
							return false;
						}
						value = list[idx];
						int ci = idx;
						setter = v => list[ci] = v;
						continue;
					}
					if (owner is IDictionary dict)
					{
						object k = key;
						var args = owner.GetType().IsGenericType ? owner.GetType().GetGenericArguments() : null;
						if (args != null && args.Length == 2)
						{
							try { k = Convert(key, args[0]); }
							catch (Exception e)
							{
								error = "key '" + key + "': " + e.Message;
								return false;
							}
						}
						if (!dict.Contains(k))
						{
							error = "no key " + key;
							return false;
						}
						value = dict[k];
						object ck = k;
						setter = v => dict[ck] = v;
						continue;
					}
					error = part + ": " + owner.GetType().Name + " is not a list or dictionary";
					return false;
				}
				var mem = Members(owner.GetType()).FirstOrDefault(x => string.Equals(x.Name, part, StringComparison.OrdinalIgnoreCase));
				if (mem == null)
				{
					error = owner.GetType().Name + " has no public member '" + part + "'";
					return false;
				}
				value = Get(mem, owner);
				var m = mem;
				setter = v => Set(m, owner, v);
			}
			return true;
		}

		/// <summary>Converts <paramref name="raw"/> to the type of the current value and assigns it.</summary>
		public static bool TrySet(object root, string path, string raw, out string message)
		{
			if (!TryResolve(root, path, out object cur, out var setter, out message)) return false;
			if (setter == null)
			{
				message = "give a member path below the root";
				return false;
			}
			var type = cur?.GetType() ?? typeof(string);
			object v;
			try { v = Convert(raw, type); }
			catch (Exception e)
			{
				message = "cannot convert '" + raw + "' to " + type.Name + ": " + e.Message;
				return false;
			}
			try { setter(v); }
			catch (Exception e)
			{
				message = "set failed: " + (e is TargetInvocationException t && t.InnerException != null ? t.InnerException.Message : e.Message);
				return false;
			}
			message = path + ": " + Short(cur) + " -> " + Short(v);
			return true;
		}

		public static object Convert(string raw, Type t)
		{
			raw = raw ?? "";
			if (t == typeof(string)) return raw;
			if (t.IsEnum) return Enum.Parse(t, raw, true);
			if (t == typeof(bool))
			{
				switch (raw.Trim().ToLowerInvariant())
				{
					case "1": case "true": case "on": case "yes": return true;
					case "0": case "false": case "off": case "no": return false;
				}
				throw new FormatException("expected true/false");
			}
			if (t.IsPrimitive || t == typeof(decimal)) return System.Convert.ChangeType(raw.Trim(), t, Inv);
			throw new NotSupportedException("only numbers, bools, text and enums can be set (" + t.Name + ")");
		}

		public static IEnumerable<MemberInfo> Members(Type t)
		{
			foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public)) yield return f;
			foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public))
				if (p.GetIndexParameters().Length == 0 && p.CanRead) yield return p;
		}

		static object Get(MemberInfo m, object o)
		{
			try { return m is FieldInfo f ? f.GetValue(o) : ((PropertyInfo)m).GetValue(o); }
			catch (Exception e) { return "<" + (e is TargetInvocationException t && t.InnerException != null ? t.InnerException.GetType().Name : e.GetType().Name) + ">"; }
		}

		static void Set(MemberInfo m, object o, object v)
		{
			if (m is FieldInfo f)
			{
				if (f.IsInitOnly || f.IsLiteral) throw new InvalidOperationException(f.Name + " is read-only");
				f.SetValue(o, v);
				return;
			}
			var p = (PropertyInfo)m;
			var set = p.GetSetMethod(true);
			if (set == null) throw new InvalidOperationException(p.Name + " is read-only");
			set.Invoke(o, new[] { v });
		}

		/// <summary>One line per member (or element), values shortened.</summary>
		public static string Dump(object o, int maxItems = 60)
		{
			if (o == null) return "null";
			var t = o.GetType();
			if (IsLeaf(t)) return Short(o);
			var sb = new StringBuilder(t.Name).Append('\n');
			if (o is IDictionary dict)
			{
				int n = 0;
				foreach (DictionaryEntry e in dict)
				{
					if (n++ >= maxItems)
					{
						sb.Append("  ... ").Append(dict.Count).Append(" entries\n");
						break;
					}
					sb.Append("  [").Append(e.Key).Append("] = ").Append(Short(e.Value)).Append('\n');
				}
				return sb.ToString();
			}
			if (o is IList list)
			{
				for (int i = 0; i < list.Count && i < maxItems; i++) sb.Append("  [").Append(i).Append("] = ").Append(Short(list[i])).Append('\n');
				if (list.Count > maxItems) sb.Append("  ... ").Append(list.Count).Append(" items\n");
				return sb.ToString();
			}
			foreach (var m in Members(t)) sb.Append("  ").Append(m.Name).Append(" = ").Append(Short(Get(m, o))).Append('\n');
			return sb.ToString();
		}

		/// <summary>"Root.Path = value" for every member (depth 1) whose name contains the text.</summary>
		public static IEnumerable<string> Find(string rootName, object root, string text)
		{
			if (root == null) yield break;
			foreach (var m in Members(root.GetType()))
				if (m.Name.IndexOf(text ?? "", StringComparison.OrdinalIgnoreCase) >= 0)
					yield return rootName + "." + m.Name + " = " + Short(Get(m, root));
		}

		static bool IsLeaf(Type t) => t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime) || t == typeof(TimeSpan);

		public static string Short(object v)
		{
			if (v == null) return "null";
			if (v is string s) return "\"" + (s.Length > 80 ? s.Substring(0, 80) + "..." : s) + "\"";
			if (v is IDictionary d) return v.GetType().Name + "{" + d.Count + "}";
			if (v is ICollection c)
			{
				var items = c.Cast<object>().Take(8).Select(x => x == null ? "null" : IsLeaf(x.GetType()) ? (x is IFormattable f ? f.ToString(null, Inv) : x.ToString()) : x.GetType().Name);
				return "[" + string.Join(", ", items) + (c.Count > 8 ? ", ... (" + c.Count + ")" : "") + "]";
			}
			if (v is IFormattable fm) return fm.ToString(null, Inv);
			return IsLeaf(v.GetType()) ? v.ToString() : "{" + v.GetType().Name + "}";
		}

		/// <summary>Splits "A.B[2].C[\"k.x\"]" into A, B, [2], C, ["k.x"].</summary>
		public static List<string> Split(string path)
		{
			var parts = new List<string>();
			var sb = new StringBuilder();
			bool quoted = false;
			foreach (char ch in (path ?? "").Trim())
			{
				if (ch == '"') quoted = !quoted;
				if (!quoted && (ch == '.' || ch == '['))
				{
					if (sb.Length > 0) parts.Add(sb.ToString());
					sb.Clear();
					if (ch == '[') sb.Append('[');
					continue;
				}
				sb.Append(ch);
				if (!quoted && ch == ']')
				{
					parts.Add(sb.ToString());
					sb.Clear();
				}
			}
			if (sb.Length > 0) parts.Add(sb.ToString());
			return parts;
		}
	}
}
