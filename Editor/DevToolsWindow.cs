using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using DreamTech.DevTools.Unity;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace DreamTech.DevTools.Editor
{
	/// <summary>
	/// Editor front end (Tools > DreamTech > DevTools, Ctrl+Alt+D).
	/// Play mode: every command and watch by category, the console and the result log — the same registry as the HUD.
	/// Edit mode: play-test launcher (boot script and presets run once the game starts), Editor save wipe, the
	/// DREAMTECH_DEVTOOLS define for QA builds, starting a player build with a script, and the command catalog.
	/// </summary>
	public sealed class DevToolsWindow : EditorWindow
	{
		const string ScriptPref = "DreamTech.DevTools.Editor.Script", PresetsPref = "DreamTech.DevTools.Editor.Presets", CatPref = "DreamTech.DevTools.Editor.Category";
		public const string Define = "DREAMTECH_DEVTOOLS";
		const string WatchTab = "*watches";
		static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

		string _category, _search = "", _console = "", _script;
		Vector2 _catScroll, _mainScroll, _logScroll;
		readonly Dictionary<DevCommand, string[]> _values = new Dictionary<DevCommand, string[]>();
		double _nextRepaint;
		List<UserPreset> _presets;
		DevRegistry _catalog;

		[Serializable]
		class UserPreset
		{
			public string Name;
			public string Script;
		}

		[Serializable]
		class UserPresetList
		{
			public List<UserPreset> Items = new List<UserPreset>();
		}

		[MenuItem("Tools/DreamTech/DevTools %&d", priority = 1)]
		public static void Open() => GetWindow<DevToolsWindow>("DevTools");

		void OnEnable()
		{
			_script = EditorPrefs.GetString(ScriptPref, "");
			_category = EditorPrefs.GetString(CatPref, null);
			LoadPresets();
			EditorApplication.playModeStateChanged += OnPlayMode;
		}

		void OnDisable() => EditorApplication.playModeStateChanged -= OnPlayMode;

		void OnPlayMode(PlayModeStateChange s)
		{
			_values.Clear();
			Repaint();
		}

		void Update()
		{
			if (Application.isPlaying && EditorApplication.timeSinceStartup > _nextRepaint)
			{
				_nextRepaint = EditorApplication.timeSinceStartup + 0.25;
				Repaint();
			}
		}

		void OnGUI()
		{
			using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
			{
				GUILayout.Label(Application.isPlaying ? "Play mode · live commands" : "Edit mode · play-test setup", EditorStyles.miniLabel);
				GUILayout.FlexibleSpace();
				if (Application.isPlaying)
				{
					if (GUILayout.Button(DevToolsHud.IsOpen ? "Close HUD" : "Open HUD", EditorStyles.toolbarButton)) DevToolsHud.Open(!DevToolsHud.IsOpen);
					if (GUILayout.Button(DevToolsHud.IsHidden ? "Show HUD" : "Hide HUD", EditorStyles.toolbarButton)) DevToolsHud.SetHidden(!DevToolsHud.IsHidden);
				}
				if (GUILayout.Button("Settings", EditorStyles.toolbarButton)) SettingsService.OpenProjectSettings(DevToolsSettingsProvider.Path);
				_search = GUILayout.TextField(_search, EditorStyles.toolbarSearchField, GUILayout.Width(200));
			}
			if (Application.isPlaying) DrawPlayMode();
			else DrawEditMode();
		}

		// ---- play mode ---------------------------------------------------------------------------------------------------

		void DrawPlayMode()
		{
			var reg = DevTools.Registry;
			using (new EditorGUILayout.HorizontalScope())
			{
				using (var s = new EditorGUILayout.ScrollViewScope(_catScroll, GUILayout.Width(160)))
				{
					_catScroll = s.scrollPosition;
					foreach (string cat in reg.Categories.ToList())
					{
						bool sel = cat == _category && _search.Length == 0;
						if (GUILayout.Toggle(sel, cat, "Button") && !sel) Select(cat);
					}
					GUILayout.Space(8);
					if (reg.Presets.Count > 0 && GUILayout.Toggle(_category == "*presets" && _search.Length == 0, "Scenarios", "Button")) Select("*presets");
					if (GUILayout.Toggle(_category == WatchTab && _search.Length == 0, "All watches", "Button")) Select(WatchTab);
				}
				using (new EditorGUILayout.VerticalScope())
				{
					using (var s = new EditorGUILayout.ScrollViewScope(_mainScroll))
					{
						_mainScroll = s.scrollPosition;
						if (_search.Length > 0)
						{
							string cat = null;
							foreach (var c in reg.Search(_search).ToList())
							{
								if (c.Category != cat)
								{
									cat = c.Category;
									EditorGUILayout.LabelField(cat, EditorStyles.boldLabel);
								}
								DrawCommand(reg, c);
							}
						}
						else if (_category == WatchTab) DrawWatches(reg, null);
						else if (_category == "*presets")
						{
							foreach (var p in reg.Presets.ToList())
								if (GUILayout.Button(new GUIContent(p.Name, p.Script))) DevTools.RunScript(p.Script, p.Name);
						}
						else if (_category != null && reg.Categories.Contains(_category))
						{
							DrawWatches(reg, _category);
							foreach (var c in reg.InCategory(_category).ToList()) DrawCommand(reg, c);
						}
						else EditorGUILayout.HelpBox("Pick a category.", MessageType.Info);
					}
					DrawConsole(reg);
				}
			}
		}

		void Select(string cat)
		{
			_category = cat;
			_search = "";
			EditorPrefs.SetString(CatPref, cat);
			GUI.FocusControl(null);
		}

		static void DrawWatches(DevRegistry reg, string category)
		{
			var list = reg.Watches.Where(w => category == null || w.Category == category).ToList();
			if (list.Count == 0) return;
			using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
			{
				string cat = null;
				foreach (var w in list)
				{
					if (category == null && w.Category != cat)
					{
						cat = w.Category;
						EditorGUILayout.LabelField(cat, EditorStyles.miniBoldLabel);
					}
					string v;
					try { v = w.Value(); }
					catch (Exception e) { v = "<" + e.GetType().Name + ">"; }
					EditorGUILayout.LabelField(w.Label, v, EditorStyles.wordWrappedLabel);
				}
			}
		}

		void DrawCommand(DevRegistry reg, DevCommand c)
		{
			string blocked = reg.BlockedReason(c);
			using (new EditorGUILayout.HorizontalScope())
			{
				using (new EditorGUI.DisabledScope(blocked != null))
				{
					var label = new GUIContent(c.Label, (c.Help ?? "") + "\n" + reg.Usage(c));
					if (c.Kind == DevCommandKind.Toggle)
					{
						bool on = false;
						try { on = c.State(); }
						catch { }
						bool nv = EditorGUILayout.ToggleLeft(label, on);
						if (nv != on) reg.Execute(c, new[] { nv ? "on" : "off" });
					}
					else if (c.Params.Length == 0)
					{
						if (GUILayout.Button(label)) RunConfirm(reg, c, Array.Empty<string>());
					}
					else
					{
						GUILayout.Label(label, GUILayout.Width(170));
						var vals = Values(c);
						for (int i = 0; i < c.Params.Length; i++) DrawParam(c.Params[i], vals, i);
						if (GUILayout.Button("Run", GUILayout.Width(50))) RunConfirm(reg, c, vals);
					}
				}
				if (GUILayout.Button(new GUIContent("⧉", "Copy the console line"), EditorStyles.miniButton, GUILayout.Width(22)))
					EditorGUIUtility.systemCopyBuffer = DevRegistry.LineFor(c, c.Params.Length > 0 ? Values(c) : null);
			}
			if (blocked != null) EditorGUILayout.LabelField("   " + blocked, EditorStyles.miniLabel);
		}

		static void RunConfirm(DevRegistry reg, DevCommand c, string[] args)
		{
			if (c.Confirm && !EditorUtility.DisplayDialog("DevTools", "Run '" + c.Label + "'?", "Run", "Cancel")) return;
			reg.Execute(c, args);
		}

		string[] Values(DevCommand c)
		{
			if (!_values.TryGetValue(c, out var v) || v.Length != c.Params.Length) _values[c] = v = c.Params.Select(p => p.Default ?? "").ToArray();
			return v;
		}

		static void DrawParam(DevParam p, string[] vals, int i)
		{
			switch (p.Kind)
			{
				case DevParamKind.Bool:
					vals[i] = EditorGUILayout.ToggleLeft(p.Name, vals[i] == "true", GUILayout.Width(100)) ? "true" : "false";
					break;
				case DevParamKind.Choice:
				{
					IList<string> opts;
					try { opts = p.Options?.Invoke() ?? Array.Empty<string>(); }
					catch { opts = Array.Empty<string>(); }
					if (opts.Count == 0)
					{
						vals[i] = EditorGUILayout.TextField(vals[i]);
						break;
					}
					int idx = Mathf.Max(0, opts.IndexOf(vals[i]));
					idx = EditorGUILayout.Popup(idx, opts.ToArray());
					vals[i] = opts[idx];
					break;
				}
				case DevParamKind.Int:
				{
					long.TryParse(vals[i], NumberStyles.Integer, Inv, out long n);
					vals[i] = EditorGUILayout.LongField(n, GUILayout.MinWidth(50)).ToString(Inv);
					break;
				}
				case DevParamKind.Float:
				{
					double.TryParse(vals[i], NumberStyles.Float, Inv, out double f);
					vals[i] = EditorGUILayout.DoubleField(f, GUILayout.MinWidth(50)).ToString(Inv);
					break;
				}
				default:
					vals[i] = EditorGUILayout.TextField(vals[i] ?? "");
					break;
			}
		}

		void DrawConsole(DevRegistry reg)
		{
			using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Height(190)))
			{
				using (new EditorGUILayout.HorizontalScope())
				{
					GUI.SetNextControlName("dt.console");
					_console = EditorGUILayout.TextField(_console);
					bool enter = Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
					             && GUI.GetNameOfFocusedControl() == "dt.console";
					if ((GUILayout.Button("Run", GUILayout.Width(50)) || enter) && _console.Trim().Length > 0)
					{
						DevTools.Submit(_console.Trim(), "editor");
						_console = "";
						_logScroll.y = float.MaxValue;
						GUI.FocusControl("dt.console");
						if (enter) Event.current.Use();
					}
					if (DevTools.RunningScripts.Count > 0 && GUILayout.Button("Stop scripts", GUILayout.Width(90))) DevTools.CancelScripts();
					if (GUILayout.Button("Clear", GUILayout.Width(50))) reg.Log.Clear();
				}
				using (var s = new EditorGUILayout.ScrollViewScope(_logScroll))
				{
					_logScroll = s.scrollPosition;
					foreach (var e in reg.Log.ToList())
					{
						EditorGUILayout.LabelField("> " + e.Line, EditorStyles.miniBoldLabel);
						if (e.Message.Length == 0) continue;
						var st = new GUIStyle(EditorStyles.wordWrappedMiniLabel);
						if (!e.Ok) st.normal.textColor = new Color(1f, 0.45f, 0.4f);
						EditorGUILayout.SelectableLabel(e.Message, st, GUILayout.Height(st.CalcHeight(new GUIContent(e.Message), Mathf.Max(100, position.width - 200))));
					}
				}
			}
		}

		// ---- edit mode ---------------------------------------------------------------------------------------------------

		void DrawEditMode()
		{
			var settings = DevToolsSettings.Current;
			using (var s = new EditorGUILayout.ScrollViewScope(_mainScroll))
			{
				_mainScroll = s.scrollPosition;
				EditorGUILayout.LabelField("Play-test launcher", EditorStyles.boldLabel);
				EditorGUILayout.HelpBox("The boot script runs once the game has started: console lines, one per line or ';' separated, " +
				                        "plus 'wait <s>' and 'waitfor <condition> [timeout]'. Click a command in the catalog to append it.", MessageType.None);
				EditorGUI.BeginChangeCheck();
				_script = EditorGUILayout.TextArea(_script, GUILayout.MinHeight(80));
				if (EditorGUI.EndChangeCheck()) EditorPrefs.SetString(ScriptPref, _script);
				using (new EditorGUILayout.HorizontalScope())
				{
					if (GUILayout.Button("▶ Play with script", GUILayout.Height(26))) PlayWith(_script, settings);
					if (GUILayout.Button("▶ Clean save + script", GUILayout.Height(26))
					    && EditorUtility.DisplayDialog("DevTools", "Delete the Editor's save (persistentDataPath + PlayerPrefs, DevTools keys kept) and play?", "Wipe and play", "Cancel"))
					{
						DevToolsBootstrap.WipeSave(settings);
						PlayWith(_script, settings);
					}
					if (GUILayout.Button("▶ Play", GUILayout.Height(26))) PlayWith(null, settings);
				}
				string always = PlayerPrefs.GetString(DevToolsKeys.BootAlways, "");
				bool on = always.Length > 0;
				bool nv = EditorGUILayout.ToggleLeft(new GUIContent("Run this script on every play", "Stored in PlayerPrefs " + DevToolsKeys.BootAlways), on);
				if (nv != on || (nv && always != _script))
				{
					if (nv) PlayerPrefs.SetString(DevToolsKeys.BootAlways, _script);
					else PlayerPrefs.DeleteKey(DevToolsKeys.BootAlways);
					PlayerPrefs.Save();
				}

				GUILayout.Space(6);
				EditorGUILayout.LabelField("My presets (this machine)", EditorStyles.boldLabel);
				for (int i = 0; i < _presets.Count; i++)
				{
					var p = _presets[i];
					using (new EditorGUILayout.HorizontalScope())
					{
						string n = EditorGUILayout.TextField(p.Name, GUILayout.Width(200));
						if (n != p.Name)
						{
							p.Name = n;
							SavePresets();
						}
						if (GUILayout.Button("Load", GUILayout.Width(50)))
						{
							_script = p.Script;
							EditorPrefs.SetString(ScriptPref, _script);
							GUI.FocusControl(null);
						}
						if (GUILayout.Button("Play", GUILayout.Width(50))) PlayWith(p.Script, settings);
						if (GUILayout.Button("Overwrite", GUILayout.Width(75)))
						{
							p.Script = _script;
							SavePresets();
						}
						if (GUILayout.Button("X", GUILayout.Width(22)) && EditorUtility.DisplayDialog("DevTools", "Delete preset '" + p.Name + "'?", "Delete", "Cancel"))
						{
							_presets.RemoveAt(i);
							SavePresets();
							break;
						}
					}
				}
				if (GUILayout.Button("Save current script as a preset", GUILayout.Width(220)))
				{
					_presets.Add(new UserPreset { Name = "Preset " + (_presets.Count + 1), Script = _script });
					SavePresets();
				}
				if (settings.Presets.Count > 0)
				{
					EditorGUILayout.LabelField("Project presets (DevToolsSettings)", EditorStyles.miniBoldLabel);
					foreach (var p in settings.Presets)
						using (new EditorGUILayout.HorizontalScope())
						{
							GUILayout.Label(new GUIContent(p.Name, p.Script), GUILayout.Width(200));
							if (GUILayout.Button("Load", GUILayout.Width(50))) _script = p.Script;
							if (GUILayout.Button("Play", GUILayout.Width(50))) PlayWith(p.Script, settings);
						}
				}

				GUILayout.Space(10);
				EditorGUILayout.LabelField("Editor save", EditorStyles.boldLabel);
				EditorGUILayout.LabelField("persistentDataPath", Application.persistentDataPath, EditorStyles.wordWrappedMiniLabel);
				using (new EditorGUILayout.HorizontalScope())
				{
					if (GUILayout.Button("Open folder")) EditorUtility.RevealInFinder(Application.persistentDataPath + "/");
					if (GUILayout.Button("Wipe now") && EditorUtility.DisplayDialog("DevTools", "Delete persistentDataPath content and PlayerPrefs (DevTools keys and preserved keys kept)?", "Wipe", "Cancel"))
						DevToolsBootstrap.WipeSave(settings);
					if (DevClock.Offset != TimeSpan.Zero || PlayerPrefs.HasKey(DevToolsKeys.ClockOffset))
						if (GUILayout.Button("Reset clock offset"))
						{
							PlayerPrefs.DeleteKey(DevToolsKeys.ClockOffset);
							PlayerPrefs.Save();
						}
				}

				GUILayout.Space(10);
				EditorGUILayout.LabelField("Builds", EditorStyles.boldLabel);
				EditorGUILayout.HelpBox("Dev tools run in the Editor and in development builds. " + Define + " adds them to non-development builds (QA). " +
				                        "Remove it before shipping.", MessageType.None);
				bool has = HasDefine();
				bool want = EditorGUILayout.ToggleLeft(Define + " (Standalone, Android, iOS)", has);
				if (want != has) SetDefine(want);
				string exe = string.IsNullOrEmpty(settings.BuildExecutable) ? null : Path.GetFullPath(Path.Combine(Application.dataPath, "..", settings.BuildExecutable));
				using (new EditorGUI.DisabledScope(exe == null || !File.Exists(exe)))
					if (GUILayout.Button(exe == null ? "Run player with this script (set BuildExecutable in the settings)" : "Run " + Path.GetFileName(exe) + " with this script"))
						RunPlayer(exe, _script, settings.BuildArguments);

				GUILayout.Space(10);
				EditorGUILayout.LabelField("Command catalog", EditorStyles.boldLabel);
				EditorGUILayout.HelpBox("Commands of [DevModule] modules. Commands of adapters installed at runtime (DevTools.Install) appear in play mode.", MessageType.None);
				DrawCatalog();
			}
		}

		void DrawCatalog()
		{
			if (_catalog == null)
			{
				_catalog = new DevRegistry();
				var core = typeof(DevTools).Assembly;
				string coreName = core.GetName().Name;
				_catalog.RegisterModules(AppDomain.CurrentDomain.GetAssemblies().Where(a => a == core || a.GetReferencedAssemblies().Any(r => r.Name == coreName)));
			}
			string cat = null;
			foreach (var c in _catalog.Search(_search))
			{
				if (c.Category != cat)
				{
					cat = c.Category;
					EditorGUILayout.LabelField(cat, EditorStyles.miniBoldLabel);
				}
				using (new EditorGUILayout.HorizontalScope())
				{
					if (GUILayout.Button(_catalog.Usage(c), EditorStyles.linkLabel, GUILayout.Width(330)))
					{
						_script = (_script.Length > 0 && !_script.EndsWith("\n", StringComparison.Ordinal) ? _script + "\n" : _script)
						          + DevRegistry.LineFor(c, c.Params.Select(p => p.Default ?? "").ToArray());
						EditorPrefs.SetString(ScriptPref, _script);
						GUI.FocusControl(null);
					}
					GUILayout.Label(c.Help ?? c.Label, EditorStyles.wordWrappedMiniLabel);
				}
			}
			EditorGUILayout.LabelField("waitfor conditions: " + string.Join(", ", _catalog.ConditionNames) + " (+ those of installed adapters, e.g. playing)", EditorStyles.wordWrappedMiniLabel);
		}

		static void PlayWith(string script, DevToolsSettings settings)
		{
			if (!string.IsNullOrWhiteSpace(script)) PlayerPrefs.SetString(DevToolsKeys.BootOnce, script);
			else PlayerPrefs.DeleteKey(DevToolsKeys.BootOnce);
			PlayerPrefs.Save();
			if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
			if (!string.IsNullOrEmpty(settings.LaunchScene) && EditorSceneManager.GetActiveScene().path != settings.LaunchScene)
			{
				if (File.Exists(settings.LaunchScene)) EditorSceneManager.OpenScene(settings.LaunchScene);
				else Debug.LogWarning("[DevTools] LaunchScene not found: " + settings.LaunchScene);
			}
			EditorApplication.EnterPlaymode();
		}

		static void RunPlayer(string exe, string script, string extraArgs)
		{
			string flat = string.Join("; ", DevScriptRun.Split(script)).Replace("\"", "\\\"");
			string args = (extraArgs ?? "") + (flat.Length > 0 ? " -devboot \"" + flat + "\"" : "");
			Process.Start(new ProcessStartInfo(exe, args.Trim()) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) });
			Debug.Log("[DevTools] started " + exe + " " + args);
		}

		static readonly NamedBuildTarget[] Targets = { NamedBuildTarget.Standalone, NamedBuildTarget.Android, NamedBuildTarget.iOS };

		public static bool HasDefine() => HasDefine(NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget)));

		public static bool HasDefine(NamedBuildTarget target) => PlayerSettings.GetScriptingDefineSymbols(target).Split(';').Contains(Define);

		public static void SetDefine(bool on)
		{
			foreach (var t in Targets)
			{
				var list = PlayerSettings.GetScriptingDefineSymbols(t).Split(';').Where(s => s.Length > 0 && s != Define).ToList();
				if (on) list.Add(Define);
				PlayerSettings.SetScriptingDefineSymbols(t, string.Join(";", list));
			}
		}

		void LoadPresets()
		{
			_presets = null;
			string json = EditorPrefs.GetString(PresetsPref, "");
			if (json.Length > 0)
			{
				try { _presets = JsonUtility.FromJson<UserPresetList>(json).Items; }
				catch { }
			}
			if (_presets == null) _presets = new List<UserPreset>();
		}

		void SavePresets() => EditorPrefs.SetString(PresetsPref, JsonUtility.ToJson(new UserPresetList { Items = _presets }));
	}

	/// <summary>Project Settings > DreamTech DevTools: creates / edits Assets/Resources/DevToolsSettings.asset.</summary>
	static class DevToolsSettingsProvider
	{
		public const string Path = "Project/DreamTech DevTools";
		const string AssetPath = "Assets/Resources/" + DevToolsSettings.ResourcePath + ".asset";
		static UnityEditor.Editor _editor;

		[SettingsProvider]
		static SettingsProvider Create() => new SettingsProvider(Path, SettingsScope.Project)
		{
			keywords = new[] { "devtools", "debug", "cheat", "hud", "playtest" },
			guiHandler = _ =>
			{
				var asset = AssetDatabase.LoadAssetAtPath<DevToolsSettings>(AssetPath);
				if (asset == null)
				{
					EditorGUILayout.HelpBox("No settings asset: defaults apply. Create one to change the HUD, boot script, presets and wipe rules.", MessageType.Info);
					if (GUILayout.Button("Create " + AssetPath, GUILayout.Width(360)))
					{
						System.IO.Directory.CreateDirectory("Assets/Resources");
						asset = ScriptableObject.CreateInstance<DevToolsSettings>();
						AssetDatabase.CreateAsset(asset, AssetPath);
						AssetDatabase.SaveAssets();
						DevToolsSettings.Reload();
					}
				}
				if (asset != null)
				{
					UnityEditor.Editor.CreateCachedEditor(asset, null, ref _editor);
					EditorGUI.BeginChangeCheck();
					_editor.OnInspectorGUI();
					if (EditorGUI.EndChangeCheck()) DevToolsSettings.Reload();
				}
				GUILayout.Space(10);
				bool has = DevToolsWindow.HasDefine();
				bool want = EditorGUILayout.ToggleLeft(DevToolsWindow.Define + " (dev tools in non-development builds)", has);
				if (want != has) DevToolsWindow.SetDefine(want);
				if (GUILayout.Button("Open the DevTools window", GUILayout.Width(220))) DevToolsWindow.Open();
			},
		};
	}
}
