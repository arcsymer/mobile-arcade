// Builds the game entirely from code: constructs the scene (camera + Game object),
// configures player/WebGL settings, and builds. No hand-authored scene/prefab YAML.
// Invoked in batchmode, e.g.:
//   Unity -batchmode -projectPath . -buildTarget WebGL -executeMethod BuildTool.BuildWebGL -logFile -
using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BuildTool
{
    const string ScenePath = "Assets/Main.unity";

    static void BuildScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.transform.position = new Vector3(0, 0, -10);
        camGo.AddComponent<AudioListener>();

        var g = new GameObject("Game");
        g.AddComponent<Game>();

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
    }

    static void ConfigurePlayer()
    {
        PlayerSettings.productName = "StarDodge";
        PlayerSettings.companyName = "arcsymer";
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
        PlayerSettings.runInBackground = true;
    }

    public static void BuildWebGL()
    {
        try
        {
            BuildScene();
            ConfigurePlayer();
            // Disabled compression = plain files that load on any static host (GitHub Pages)
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.dataCaching = false;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.None;
            PlayerSettings.WebGL.linkerTarget = WebGLLinkerTarget.Wasm;

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/WebGL",
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            });
            Report("WebGL", report);
        }
        catch (Exception e) { Debug.LogError("BUILD EXCEPTION: " + e); EditorApplication.Exit(2); }
    }

    public static void BuildWindows()
    {
        try
        {
            BuildScene();
            ConfigurePlayer();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/Windows/StarDodge.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            Report("Windows", report);
        }
        catch (Exception e) { Debug.LogError("BUILD EXCEPTION: " + e); EditorApplication.Exit(2); }
    }

    static void Report(string what, BuildReport report)
    {
        var s = report.summary;
        Debug.Log($"=== BUILD {what}: {s.result} | size={s.totalSize} bytes | errors={s.totalErrors} | time={s.totalTime} ===");
        EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
    }
}
