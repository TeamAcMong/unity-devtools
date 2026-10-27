#if UNITY_EDITOR || DEVELOPMENT_BUILD || DREAMTECH_DEVTOOLS
using System;
using System.Collections.Generic;
using UnityEngine;

namespace DreamTech.DevTools.Unity
{
	/// <summary>Console entry types the Logs page shows; any combination.</summary>
	[Flags]
	public enum DevLogKinds
	{
		None = 0,
		Log = 1,
		Warning = 2,
		Error = 4,
		All = Log | Warning | Error,
	}

	/// <summary>Filtering and collapsing of the recorded console (<see cref="DevToolsHost.Logs"/>) for the Logs page. No UI, so it is tested on its own.</summary>
	public static class DevLogFilter
	{
		public struct Row
		{
			/// <summary>Index into the log list (its newest occurrence when collapsed).</summary>
			public int Index;
			public int Count;
		}

		/// <summary>Exceptions and asserts count as errors, as in the Unity console.</summary>
		public static DevLogKinds KindOf(LogType type) =>
			type == LogType.Log ? DevLogKinds.Log : type == LogType.Warning ? DevLogKinds.Warning : DevLogKinds.Error;

		/// <summary>
		/// Fills <paramref name="rows"/> newest first with the entries of the wanted kinds whose message contains
		/// <paramref name="search"/> (case-insensitive). Collapsed: one row per distinct type + message at its newest occurrence,
		/// Count = how many times it was logged. At most <paramref name="maxRows"/> rows; collapsed counts still cover every match.
		/// </summary>
		public static void Build(IReadOnlyList<DevToolsHost.LogRecord> logs, DevLogKinds kinds, string search, bool collapse, int maxRows, List<Row> rows)
		{
			rows.Clear();
			bool searching = !string.IsNullOrEmpty(search);
			var groups = collapse ? new Dictionary<(LogType, string), int>() : null;
			for (int i = logs.Count - 1; i >= 0; i--)
			{
				var entry = logs[i];
				if ((kinds & KindOf(entry.Type)) == 0) continue;
				string message = entry.Message ?? "";
				if (searching && message.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
				if (collapse)
				{
					var key = (entry.Type, message);
					if (groups.TryGetValue(key, out int at))
					{
						var row = rows[at];
						row.Count++;
						rows[at] = row;
						continue;
					}
					if (rows.Count >= maxRows) continue;
					groups[key] = rows.Count;
				}
				else if (rows.Count >= maxRows) break;
				rows.Add(new Row { Index = i, Count = 1 });
			}
		}
	}
}
#endif
