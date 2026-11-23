using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.WebGL;
using UnityEngine;

internal static class YVWebBuildTools
{
    [MenuItem("Tools/Yek Vajab Ja/Optimize Web Build For Disk Size")]
    private static void OptimizeWebBuildForDiskSize()
    {
        UserBuildSettings.codeOptimization = WasmCodeOptimization.DiskSize;
        AssetDatabase.SaveAssets();
        UnityEngine.Debug.Log("Yek Vajab Ja Web code optimization set to Disk Size.");
    }

    [MenuItem("Tools/Yek Vajab Ja/Build Web Optimized")]
    public static void BuildWebOptimized()
    {
        UserBuildSettings.codeOptimization = WasmCodeOptimization.DiskSize;
        PlayerSettings.WebGL.template = "PROJECT:YekVajabJa";
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
        PlayerSettings.WebGL.decompressionFallback = false;

        string[] scenes = System.Array.FindAll(
            System.Array.ConvertAll(EditorBuildSettings.scenes, scene => scene.path),
            scenePath => !string.IsNullOrEmpty(scenePath));
        if (scenes.Length == 0)
        {
            Debug.LogError("No enabled scenes are listed in Build Settings.");
            return;
        }

        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = "Build/Web-Optimized",
            target = BuildTarget.WebGL,
            options = BuildOptions.None
        });
        if (report.summary.result != BuildResult.Succeeded)
        {
            Debug.LogError($"Web build failed: {report.summary.result}");
            return;
        }

        Debug.Log($"Yek Vajab Ja Web build completed: {report.summary.totalSize} bytes.");
    }
}
