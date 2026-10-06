using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class BuildCameraShaders
{
    public static void Build()
    {
        var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/OcclusionComposite.shader");
        if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new Exception("Occlusion shader failed to compile");
        string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Build"));
        Directory.CreateDirectory(output);
        var map = new AssetBundleBuild { assetBundleName = "apocaplayer_camera.bundle", assetNames = new[] { "Assets/OcclusionComposite.shader" } };
        var built = BuildPipeline.BuildAssetBundles(output, new[] { map }, BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.StandaloneWindows64);
        if (built == null) throw new Exception("Camera bundle build failed");
        string destination = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Models/apocaplayer_camera.bundle"));
        File.Copy(Path.Combine(output, map.assetBundleName), destination, true);
        Debug.Log("Camera shader bundle built: " + destination);
    }
}
