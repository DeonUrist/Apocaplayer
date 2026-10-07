using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Apocaplayer > Build animation bundle
// 1. every FBX in Assets/Mixamo is imported as Humanoid (avatar created from the file), in place (root motion baked into the pose),
//    looping unless it is a one-shot (Reload, Melee, Throw); its clip is named after the file (Walk.fbx -> "Walk"); standing clips keep their
//    sideways sway in the pose (feet stay planted), moving ones put it in the root (she walks on the spot)
// 2. (2.0) nothing is mirrored any more (a mirrored strafe put the gun in the other hand); the Throw is the only mirrored clip
// 3. the clips are copied to Assets/Clips/*.anim and packed into Build/apocaplayer_anims.bundle (Windows 64)
// 4. the bundle is copied into the game's BepInEx/plugins/Apocaplayer/Models folder when that folder exists
public static class ApocaplayerAnimBuilder
{
    const string Src = "Assets/Mixamo";
    const string ClipDir = "Assets/Clips";
    const string OutDir = "Build";
    const string BundleName = "apocaplayer_anims.bundle";
    const string GameModels = @"E:\SteamLibrary\steamapps\common\Apocalypter\BepInEx\plugins\Apocaplayer\Models";

    // 2.0: the Rifle pack is the lower body of EVERY weapon (8 directions × walk / run / sprint, crouch, turns, jump); the pistol and the bare
    // hands only need the clips that move the hands (the mod's UpperRig puts their upper body on the rifle legs)
    static readonly string[] Dirs = { "", "ForwardRight", "Right", "BackRight", "Back", "BackLeft", "Left", "ForwardLeft" };
    static IEnumerable<string> Loco(string tier, string strafe)
    {
        foreach (var d in Dirs) yield return (d == "Right" || d == "Left") ? strafe + d : tier + d;
    }
    static readonly string[] Known = BuildKnown();
    static string[] BuildKnown()
    {
        var l = new List<string> { "RifleIdle", "RifleAim", "RifleFire", "RifleCrouchIdle", "RifleCrouchAim", "RifleCrouchFire", "RifleReload",
            "RifleTurnLeft", "RifleTurnRight", "RifleCrouchTurnLeft", "RifleCrouchTurnRight", "RifleJumpUp", "RifleJumpLoop", "RifleJumpDown", "RifleJump",
            "RifleDeathFront", "RifleDeathBack", "RifleDeathRight", "RifleDeathHeadFront", "RifleDeathHeadBack", "RifleCrouchDeathHeadFront",
            "PistolIdle", "PistolRun", "PistolFire", "PistolReload", "PistolJump",
            "Idle", "Walk", "WalkBack", "Run", "CrouchIdle", "Kick", "Jump", "Melee", "MeleeCombo", "Punch", "Punch1", "Punch2", "Melee1", "Melee2", "Throw", "ThrowRight" };
        foreach (var n in Loco("Walk", "Strafe")) l.Add("Rifle" + n);
        foreach (var n in Loco("Run", "RunStrafe")) l.Add("Rifle" + n);
        foreach (var n in Loco("Sprint", "SprintStrafe")) l.Add("Rifle" + n);
        foreach (var n in Loco("CrouchWalk", "CrouchStrafe")) l.Add("Rifle" + n);
        return l.ToArray();
    }

    // naming: [Rifle|Pistol][Fire][Crouch]Action, e.g. RifleCrouchFire, PistolFireWalkBack. File names are normalised to that:
    // RunLeftStrafe -> RunStrafeLeft, wrong capitals (RIfleFireWalk) -> the known spelling, old CrouchRifleFire -> RifleCrouchFire
    static string Norm(string n)
    {
        n = n.Replace("LeftStrafe", "StrafeLeft").Replace("RightStrafe", "StrafeRight");
        if (n == "CrouchRifleFire") n = "RifleCrouchFire";
        if (n == "CrouchPistolFire") n = "PistolCrouchFire";
        foreach (var k in Known) if (string.Equals(k, n, System.StringComparison.OrdinalIgnoreCase)) return k;
        return n;
    }

    // the grenade is thrown with the LEFT hand (the right one holds the gun)
    const bool MirrorThrow = true;

    // clips that move her (walk, run, strafe, jump): forward/sideways motion goes to the (ignored) root, so she walks on the spot and the mod can
    // read the clip's walking speed. Everything else stands still (idles, fire, reload, melee, punches, kick ...): its sway is baked into the pose,
    // else the hips stay put and the feet slide under her (Idle sways 16 cm sideways)
    static bool Moves(string n) { return n.Contains("Walk") || n.Contains("Run") || n.Contains("Sprint") || n.Contains("Strafe") || n.Contains("Jump"); }

    static bool OneShot(string n) { return n.Contains("Reload") || n == "Melee" || n.StartsWith("Throw") || n == "Kick" || (n.Contains("Jump") && !n.Contains("JumpLoop")) || n.Contains("Turn") || n.Contains("Death"); }

    [MenuItem("Apocaplayer/Build animation bundle")]
    public static void Build()
    {
        var fbx = new List<string>();
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Src })) fbx.Add(AssetDatabase.GUIDToAssetPath(guid));
        if (fbx.Count == 0) { EditorUtility.DisplayDialog("Apocaplayer", "No FBX files in " + Src, "OK"); return; }
        var have = new HashSet<string>();
        foreach (var p in fbx) have.Add(Norm(Path.GetFileNameWithoutExtension(p)));
        var known = new HashSet<string>(Known);
        foreach (var n in have) if (!known.Contains(n)) Debug.LogWarning("Apocaplayer: " + n + ".fbx is not a name the mod uses (it is packed anyway)");

        // 1 + 2: importer settings
        foreach (var path in fbx)
        {
            string name = Norm(Path.GetFileNameWithoutExtension(path));
            var imp = (ModelImporter)AssetImporter.GetAtPath(path);
            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.importAnimation = true;
            var src = imp.defaultClipAnimations;
            if (src.Length == 0) { Debug.LogError("Apocaplayer: " + path + " has no animation"); continue; }
            var list = new List<ModelImporterClipAnimation>();
            var main = Setup(src[0], name);
            if (MirrorThrow && name == "Throw") { main.mirror = true; Debug.Log("Apocaplayer: Throw mirrored (thrown with the left hand)"); }
            list.Add(main);
            // the blast lance is thrown with the RIGHT hand (it is the drawn weapon): the same clip unmirrored
            if (MirrorThrow && name == "Throw" && !have.Contains("ThrowRight")) list.Add(Setup(src[0], "ThrowRight"));
            // (2.0) no mirrored strafes any more: a mirrored clip swaps the hands, the gun ends up in the left one - the rifle pack has both sides
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
                if (!clip.humanMotion) Debug.LogError("Apocaplayer: " + clip.name + " is not humanoid - check the FBX rig (Mixamo skeleton)");
            }
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory(OutDir);
        var build = new AssetBundleBuild { assetBundleName = BundleName, assetNames = anims.ToArray() };
        BuildPipeline.BuildAssetBundles(OutDir, new[] { build }, BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.StandaloneWindows64);
        string outFile = Path.Combine(OutDir, BundleName);

        // 4: into the game
        string copied = "";
        if (Directory.Exists(GameModels))
        {
            string dst = Path.Combine(GameModels, BundleName);
            File.Copy(outFile, dst, true);
            File.SetLastWriteTime(dst, System.DateTime.Now);   // File.Copy keeps the source's date - show when it was really copied
            copied = "\nCopied to " + GameModels;
        }
        var missing = new List<string>();
        foreach (var n in new[] { "Idle", "Walk" }) if (!have.Contains(n)) missing.Add(n);
        EditorUtility.DisplayDialog("Apocaplayer", anims.Count + " clips packed into " + Path.GetFullPath(outFile) + copied
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
            // rotation and height baked into the pose; forward/sideways motion of moving clips NOT baked (see Moves)
            // turns in place: the 90° turn stays root motion (ignored) - the body already faces the new way, only the feet shuffle
            lockRootRotation = !name.Contains("Turn"), keepOriginalOrientation = true,
            // jumps: the game lifts her body itself - their height goes to the (ignored) root so she doesn't rise twice
            lockRootHeightY = !name.Contains("Jump"), keepOriginalPositionY = true, heightFromFeet = false,
            // standing clips: XZ baked, measured from the clip's start (center of mass), so the sway stays and no fixed offset is added
            lockRootPositionXZ = !Moves(name), keepOriginalPositionXZ = Moves(name),
        };
        return a;
    }
}
