using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class ClimbingBuilder
{
    const string Source = "Assets/Source/";
    const string Bundle = "apocaplayer_climbing.bundle";
    static readonly Dictionary<string, string> Bones = new Dictionary<string, string> {
        {"Hips", "pelvis"}, {"Spine", "spine_01"}, {"Chest", "spine_03"}, {"UpperChest", "spine_05"},
        {"Neck", "neck_01"}, {"Head", "head"},
        {"LeftShoulder", "clavicle_l"}, {"LeftUpperArm", "upperarm_l"}, {"LeftLowerArm", "lowerarm_l"}, {"LeftHand", "hand_l"},
        {"RightShoulder", "clavicle_r"}, {"RightUpperArm", "upperarm_r"}, {"RightLowerArm", "lowerarm_r"}, {"RightHand", "hand_r"},
        {"LeftUpperLeg", "thigh_l"}, {"LeftLowerLeg", "calf_l"}, {"LeftFoot", "foot_l"}, {"LeftToes", "ball_l"},
        {"RightUpperLeg", "thigh_r"}, {"RightLowerLeg", "calf_r"}, {"RightFoot", "foot_r"}, {"RightToes", "ball_r"}
    };

    [MenuItem("Apocaplayer/Build climbing bundle")]
    public static void Build()
    {
        Directory.CreateDirectory("Assets/Clips");
        AssetDatabase.Refresh();
        string avatarPath = Source + "UEFN_Avatar.fbx";
        var importer = (ModelImporter)AssetImporter.GetAtPath(avatarPath);
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        var description = importer.humanDescription;
        // The source has UE bone names; the destination is Mixamo. Humanoid
        // muscle clips retarget between these through the destination Avatar.
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(avatarPath);
        var names = new HashSet<string>(model.GetComponentsInChildren<Transform>(true).Select(t => t.name));
        var mapping = new List<HumanBone>();
        foreach (var pair in Bones)
        {
            string bone = pair.Value;
            if (!names.Contains("spine_05"))
            {
                if (pair.Key == "Chest") bone = "spine_02";
                if (pair.Key == "UpperChest") bone = "spine_03";
            }
            if (!names.Contains(bone))
            {
                if (pair.Key == "Chest" && names.Contains("spine_02")) bone = "spine_02";
                else if (pair.Key == "UpperChest") continue;
                else throw new Exception("Missing source bone " + bone + "; bones: " + string.Join(",", names));
            }
            mapping.Add(new HumanBone { humanName = pair.Key, boneName = bone, limit = new HumanLimit { useDefaultValues = true } });
        }
        description.human = mapping.ToArray();
        description.armStretch = .05f; description.legStretch = .05f;
        description.upperArmTwist = .5f; description.lowerArmTwist = .5f;
        description.upperLegTwist = .5f; description.lowerLegTwist = .5f;
        description.hasTranslationDoF = false;
        importer.humanDescription = description;
        importer.SaveAndReimport();
        var avatar = AssetDatabase.LoadAllAssetsAtPath(avatarPath).OfType<Avatar>().FirstOrDefault(a => a.isValid && a.isHuman);
        if (avatar == null) throw new Exception("UEFN Humanoid avatar could not be built");
        var paths = new List<string>();
        var report = new List<string> { "Source Avatar: valid Humanoid; destination: Apocaplayer Mixamo Avatar" };
        foreach (string name in new[] { "ClimbWaist", "ClimbHigh" })
        {
            string path = Source + name + ".fbx";
            var imp = (ModelImporter)AssetImporter.GetAtPath(path);
            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            imp.sourceAvatar = avatar;
            imp.importAnimation = true; imp.materialImportMode = ModelImporterMaterialImportMode.None;
            imp.animationCompression = ModelImporterAnimationCompression.Off;
            var clips = imp.defaultClipAnimations;
            if (clips.Length == 0) throw new Exception(name + " FBX contains no animation");
            var settings = clips[0];
            settings.name = name; settings.loopTime = false; settings.loopPose = false;
            settings.lockRootRotation = true; settings.keepOriginalOrientation = true;
            settings.lockRootHeightY = false; settings.keepOriginalPositionY = true; settings.heightFromFeet = false;
            // GAP clips begin behind their authored ledge (e.g. -1.18 m).
            // Extract travel relative to the initial body instead of retaining
            // that world-space origin in the retargeted pose.
            settings.lockRootPositionXZ = false; settings.keepOriginalPositionXZ = false;
            imp.clipAnimations = new[] { settings };
            imp.SaveAndReimport();
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => c.name == name);
            if (clip == null || !clip.humanMotion || clip.length < .2f) throw new Exception("Invalid Humanoid animation: " + name);
            string output = "Assets/Clips/" + name + ".anim";
            // Update in place so GUIDs and existing project references survive rebuilds.
            var copy = AssetDatabase.LoadAssetAtPath<AnimationClip>(output);
            if (copy == null) AssetDatabase.CreateAsset(UnityEngine.Object.Instantiate(clip), output);
            else EditorUtility.CopySerialized(clip, copy);
            copy = AssetDatabase.LoadAssetAtPath<AnimationClip>(output); copy.name = name;
            EditorUtility.SetDirty(copy);
            paths.Add(output);
            report.Add(name + ": Humanoid=" + clip.humanMotion + ", seconds=" + clip.length.ToString("F3") + ", curves=" + AnimationUtility.GetCurveBindings(clip).Length);
        }
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory("Build");
        var manifest = BuildPipeline.BuildAssetBundles("Build", new[] { new AssetBundleBuild { assetBundleName = Bundle, assetNames = paths.ToArray() } },
            BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);
        if (manifest == null) throw new Exception("Animation bundle build failed");
        string destination = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../Models"));
        Directory.CreateDirectory(destination);
        File.Copy("Build/" + Bundle, Path.Combine(destination, Bundle), true);
        File.WriteAllLines("Build/animation-validation.txt", report);
        Debug.Log(string.Join("\n", report));
        Debug.Log("APOCACLIMBER_BUNDLE_OK " + destination);
    }
}
