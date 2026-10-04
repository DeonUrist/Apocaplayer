using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FemalePlayer
{
    // Optional humanoid clips from an AssetBundle (Models/femaleplayer_anims.bundle) built with Unity 2020.3.49f1 from Mixamo FBX files
    // (UnityAnims/ project in the repo). Clips are named after their FBX file: Idle, Walk, WalkBack, StrafeLeft, StrafeRight, Run,
    // CrouchIdle, CrouchWalk, the same with a "Rifle" prefix, RifleAim, RifleFire, RifleReload, PistolAim, PistolFire, PistolReload,
    // Melee, Throw. Humanoid clips retarget onto her (Flexa's avatar) through the Animator, whatever character they were made on.
    internal static class Anims
    {
        private static readonly Dictionary<string, AnimationClip> _clips = new Dictionary<string, AnimationClip>(StringComparer.OrdinalIgnoreCase);
        private static bool _tried;
        private static AssetBundle _bundle;

        public static bool Loaded { get { Load(); return _clips.Count > 0; } }

        public static AnimationClip Get(string name)
        {
            Load();
            AnimationClip c;
            return name != null && _clips.TryGetValue(name, out c) ? c : null;
        }

        // first of the names that the bundle has
        public static AnimationClip First(params string[] names)
        {
            foreach (var n in names) { var c = Get(n); if (c != null) return c; }
            return null;
        }

        private static void Load()
        {
            if (_tried) return;
            _tried = true;
            string path = Plugin.ModPath(Plugin.AnimBundleFile.Value);
            if (!File.Exists(path)) { Plugin.Log.LogInfo("No animation bundle (" + Path.GetFileName(path) + "): using the game's raider clips + procedural walk"); return; }
            try
            {
                _bundle = AssetBundle.LoadFromFile(path);
                if (_bundle == null) { Plugin.Log.LogError("Animation bundle " + path + " could not be opened (built with a different Unity version / platform?)"); return; }
                foreach (var c in _bundle.LoadAllAssets<AnimationClip>())
                {
                    if (c == null || c.name.StartsWith("__preview__")) continue;
                    c.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    _clips[c.name] = c;
                }
                var names = new List<string>(_clips.Keys); names.Sort();
                int nonHuman = 0; foreach (var c in _clips.Values) if (!c.humanMotion) nonHuman++;
                Plugin.Log.LogInfo("Animation bundle: " + _clips.Count + " clips (" + string.Join(", ", names.ToArray()) + ")"
                                   + (nonHuman > 0 ? "; " + nonHuman + " are NOT humanoid and won't fit her - set Animation Type = Humanoid in Unity" : ""));
            }
            catch (Exception e) { Plugin.Log.LogError("Animation bundle: " + e.Message); }
        }
    }
}
