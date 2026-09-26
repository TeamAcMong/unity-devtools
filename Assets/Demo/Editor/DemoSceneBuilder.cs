using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DreamTech.DevTools.Demo.EditorTools
{
	/// <summary>
	/// Builds Assets/Demo/DevToolsDemo.unity (a generated file: change this builder, not the scene) and puts it first in the
	/// build settings. Batch mode: -executeMethod DreamTech.DevTools.Demo.EditorTools.DemoSceneBuilder.BuildFromCommandLine
	/// </summary>
	public static class DemoSceneBuilder
	{
		public const string ScenePath = "Assets/Demo/DevToolsDemo.unity";

		[MenuItem("Tools/DreamTech/DevTools Demo/Build demo scene")]
		public static void Build()
		{
			var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
			var cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
			cam.tag = "MainCamera";
			cam.clearFlags = CameraClearFlags.SolidColor;
			cam.backgroundColor = new Color(0.12f, 0.14f, 0.2f);
			cam.orthographic = true;
			new GameObject("DemoGame", typeof(DemoGame));
			EditorSceneManager.SaveScene(scene, ScenePath);
			var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
			scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
			EditorBuildSettings.scenes = scenes.ToArray();
			AssetDatabase.SaveAssets();
			Debug.Log("[DevTools Demo] scene built: " + ScenePath);
		}

		public static void BuildFromCommandLine()
		{
			Build();
			EditorApplication.Exit(0);
		}
	}
}
