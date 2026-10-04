using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// FemalePlayer > Build animation bundle
// 1. every FBX in Assets/Mixamo is imported as Humanoid (avatar created from the file), in place (root motion baked into the pose),
//    looping unless it is a one-shot (Reload, Melee, Throw); its clip is named after the file (Walk.fbx -> "Walk")
// 2. a missing StrafeRight / RifleStrafeRight is made by mirroring the left one (and the other way round)
// 3. the clips are copied to Assets/Clips/*.anim and packed into Build/femaleplayer_anims.bundle (Windows 64)
// 4. the bundle is copied into the game's BepInEx/plugins/FemalePlayer/Models folder when that folder exists
public static class FemalePlayerAnimBuilder
{
    const string Src = "Assets/Mixamo";
    const string ClipDir = "Assets/Clips";
    const string OutDir = "Build";
    const string BundleName = "femaleplayer_anims.bundle";
    const string GameModels = @"E:\SteamLibrary\steamapps\common\Apocalypter\BepInEx\plugins\FemalePlayer\Models";

    static readonly string[] Known =
    {
        "Idle", "Walk", "WalkBack", "StrafeLeft", "StrafeRight", "Run", "RunStrafeLeft", "RunStrafeRight",
        "CrouchIdle", "CrouchWalk", "CrouchWalkBack", "CrouchStrafeLeft", "CrouchStrafeRight",
        "RifleIdle", "RifleWalk", "RifleWalkBack", "RifleStrafeLeft", "RifleStrafeRight", "RifleRun", "RifleRunStrafeLeft", "RifleRunStrafeRight",
        "RifleCrouchIdle", "RifleCrouchWalk", "RifleCrouchWalkBack", "RifleCrouchStrafeLeft", "RifleCrouchStrafeRight",
        "RifleFireWalk", "RifleFireWalkBack", "RifleFireStrafeLeft", "RifleFireStrafeRight",
        "RifleFireCrouchWalk", "RifleFireCrouchWalkBack", "RifleFireCrouchStrafeLeft", "RifleFireCrouchStrafeRight",
        "PistolIdle", "PistolWalk", "PistolWalkBack", "PistolStrafeLeft", "PistolStrafeRight", "PistolRun", "PistolRunStrafeLeft", "PistolRunStrafeRight",
        "PistolCrouchIdle", "PistolCrouchWalk", "PistolCrouchWalkBack", "PistolCrouchStrafeLeft", "PistolCrouchStrafeRight",
        "PistolFireWalk", "PistolFireWalkBack", "PistolFireStrafeLeft", "PistolFireStrafeRight",
        "PistolFireCrouchWalk", "PistolFireCrouchWalkBack", "PistolFireCrouchStrafeLeft", "PistolFireCrouchStrafeRight",
        "CrouchPistolFire", "PistolCrouchFire", "PistolJump",
        "Kick", "Jump", "RifleJump", "RifleAim", "RifleFire", "CrouchRifleFire", "RifleCrouchFire", "RifleReload", "PistolAim", "PistolFire", "PistolReload", "Melee", "Throw",
        "MeleeCombo", "Punch1", "Punch2", "Melee1", "Melee2",
    };

    // RunLeftStrafe -> RunStrafeLeft (either word order works for file names)
    static string Norm(string n) { return n.Replace("LeftStrafe", "StrafeLeft").Replace("RightStrafe", "StrafeRight"); }

    // the grenade is thrown with the LEFT hand (the right one holds the gun)
    const bool MirrorThrow = true;

    static bool OneShot(string n) { return n.Contains("Reload") || n == "Melee" || n == "Throw" || n == "Kick" || n.Contains("Jump"); }

    [MenuItem("FemalePlayer/Build animation bundle")]
    public static void Build()
    {
        var fbx = new List<string>();
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Src })) fbx.Add(AssetDatabase.GUIDToAssetPath(guid));
        if (fbx.Count == 0) { EditorUtility.DisplayDialog("FemalePlayer", "No FBX files in " + Src, "OK"); return; }
        var have = new HashSet<string>();
        foreach (var p in fbx) have.Add(Norm(Path.GetFileNameWithoutExtension(p)));
        var known = new HashSet<string>(Known);
        foreach (var n in have) if (!known.Contains(n)) Debug.LogWarning("FemalePlayer: " + n + ".fbx is not a name the mod uses (it is packed anyway)");

        // 1 + 2: importer settings
        foreach (var path in fbx)
        {
            string name = Norm(Path.GetFileNameWithoutExtension(path));
            var imp = (ModelImporter)AssetImporter.GetAtPath(path);
            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.importAnimation = true;
            var src = imp.defaultClipAnimations;
            if (src.Length == 0) { Debug.LogError("FemalePlayer: " + path + " has no animation"); continue; }
            var list = new List<ModelImporterClipAnimation>();
            var main = Setup(src[0], name);
            if (MirrorThrow && name == "Throw") { main.mirror = true; Debug.Log("FemalePlayer: Throw mirrored (thrown with the left hand)"); }
            list.Add(main);
            string mirrorName = null;
            if (name.EndsWith("StrafeLeft")) mirrorName = name.Replace("StrafeLeft", "StrafeRight");
            else if (name.EndsWith("StrafeRight")) mirrorName = name.Replace("StrafeRight", "StrafeLeft");
            if (mirrorName != null && !have.Contains(mirrorName))
            {
                var m = Setup(src[0], mirrorName);
                m.mirror = true;
                list.Add(m);
                Debug.Log("FemalePlayer: " + mirrorName + " = mirrored " + name);
            }
            imp.clipAnimations = list.ToArray();
            imp.SaveAndReimport();
        }

        // 3: standalone .anim copies -> bundle
        if (!AssetDatabase.IsValidFolder(ClipDir)) AssetDatabase.CreateFolder("Assets", "Clips");
        var anims = new List<string>();
        foreach (var path in fbx)
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                var clip = o as AnimationClip;
                if (clip == null || clip.name.StartsWith("__preview__")) continue;
                var copy = new AnimationClip();
                EditorUtility.CopySerialized(clip, copy);
                string dst = ClipDir + "/" + clip.name + ".anim";
                AssetDatabase.DeleteAsset(dst);
                AssetDatabase.CreateAsset(copy, dst);
                anims.Add(dst);
                if (!clip.humanMotion) Debug.LogError("FemalePlayer: " + clip.name + " is not humanoid - check the FBX rig (Mixamo skeleton)");
            }
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory(OutDir);
        var build = new AssetBundleBuild { assetBundleName = BundleName, assetNames = anims.ToArray() };
        BuildPipeline.BuildAssetBundles(OutDir, new[] { build }, BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);
        string outFile = Path.Combine(OutDir, BundleName);

        // 4: into the game
        string copied = "";
        if (Directory.Exists(GameModels)) { File.Copy(outFile, Path.Combine(GameModels, BundleName), true); copied = "\nCopied to " + GameModels; }
        var missing = new List<string>();
        foreach (var n in new[] { "Idle", "Walk" }) if (!have.Contains(n)) missing.Add(n);
        EditorUtility.DisplayDialog("FemalePlayer", anims.Count + " clips packed into " + Path.GetFullPath(outFile) + copied
            + (missing.Count > 0 ? "\n\nMISSING (the mod ignores the bundle without them): " + string.Join(", ", missing.ToArray()) : ""), "OK");
    }

    static ModelImporterClipAnimation Setup(ModelImporterClipAnimation c, string name)
    {
        var a = new ModelImporterClipAnimation
        {
            name = name,
            takeName = c.takeName,
            firstFrame = c.firstFrame,
            lastFrame = c.lastFrame,
            loopTime = !OneShot(name),
            loopPose = false,
            // rotation and height baked into the pose; forward/sideways motion NOT baked: it goes to the root, which the mod
            // ignores - so she walks on the spot whether or not the clip was downloaded "In Place", and the mod can read the
            // clip's own walking speed (averageSpeed) to play it in step with the player
            lockRootRotation = true, keepOriginalOrientation = true,
            // jumps: the game lifts her body itself - their height goes to the (ignored) root so she doesn't rise twice
            lockRootHeightY = !name.Contains("Jump"), keepOriginalPositionY = true, heightFromFeet = false,
            lockRootPositionXZ = false, keepOriginalPositionXZ = true,
        };
        return a;
    }
}
