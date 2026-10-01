using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DreamTech.DevTools.Editor
{
	/// <summary>
	/// Regenerates the PanelSettings asset the HUD loads (Runtime/Unity/Resources/DreamTechDevTools). It must be an asset: Unity 6
	/// gives the text engine its ICU data to PanelSettings assets only, so a PanelSettings made at runtime cannot lay out text.
	/// Package maintainers only: <c>python tools/unity-run.py method DreamTech.DevTools.Editor.HudAssetBuilder.Build</c>.
	/// </summary>
	public static class HudAssetBuilder
	{
		const string Folder = "Packages/com.dreamtech.devtools/Runtime/Unity/Resources/DreamTechDevTools";

		[MenuItem("Tools/DreamTech DevTools/Rebuild HUD PanelSettings (maintainers)")]
		public static void Build()
		{
			var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(Folder + "/DevToolsTheme.tss");
			if (theme == null) throw new FileNotFoundException("DevToolsTheme.tss not found (import the package first)");
			string path = Folder + "/DevToolsPanelSettings.asset";
			var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
			if (settings == null)
			{
				settings = ScriptableObject.CreateInstance<PanelSettings>();
				AssetDatabase.CreateAsset(settings, path);
			}
			settings.themeStyleSheet = theme;
			settings.scaleMode = PanelScaleMode.ConstantPixelSize;
			settings.scale = 1f;
			settings.sortingOrder = 32000f;
			settings.clearColor = false;
			EditorUtility.SetDirty(settings);
			AssetDatabase.SaveAssets();
			Debug.Log("[DevTools] HUD PanelSettings written to " + path);
		}
	}
}
