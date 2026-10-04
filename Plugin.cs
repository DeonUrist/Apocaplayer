using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FemalePlayer
{
    // Plays the game as a woman: a female body under the first-person camera (legs and torso when you look down,
    // a shadow), her bare arms and gloves on every first-person weapon / item animation, an optional third-person
    // camera on foot (body animated with Flexa's own humanoid clips, the NPC gun model in her hand), and her in the
    // driver's seat of every car instead of the game's seated man.
    [BepInPlugin(GUID, NAME, VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public const string GUID = "com.denis.apocalypter.femaleplayer";
        public const string NAME = "FemalePlayer";
        public const string VERSION = "0.7.2";

        internal static ManualLogSource Log;
        internal static string Dir;

        // player-facing
        internal static ConfigEntry<bool> WeaponAdjust, Enabled, BodyFirstPerson, FemaleArms, ReplaceDriver, ThirdPersonOnFoot, MirrorBody, VerboseLog;
        // hidden (fixed values, never written to the file; change H( to Config.Bind( to expose one)
        internal static ConfigEntry<string> ModelFile, TextureFile, ArmsTextureFile, AnimBundleFile;
        internal static ConfigEntry<string> IdleClip, RunClip, RifleClip, PistolClip, MeleeClip;
        internal static ConfigEntry<float> BodyBack, BodyBackDown, RunClipSpeed, WalkStride, RunFrom, ThirdDistance, ThirdHeight, ThirdShoulder, AimPitchShare;
        internal static ConfigEntry<float> CarCameraForward, FirstPersonWeaponBack, FirstPersonChestLean;
        internal static ConfigEntry<float> ThirdCarDistance, ThirdCarHeight;
        internal static ConfigEntry<bool> OrbitMiddleMouse;
        internal static ConfigEntry<float> JumpClipStart;
        internal static ConfigEntry<KeyCode> IgnitionKey;
        internal static ConfigEntry<float> OrbitSpeed, HipsDrift, ClipWalkSpeed, ClipRunSpeed, ClipCrouchSpeed, ThighSwing, KneeBend, ArmSwing, HipBob, CrouchDrop;

        private static ConfigFile _hidden;
        private static ConfigEntry<T> H<T>(string section, string key, T value, string description) { return _hidden.Bind(section, key, value, description); }

        private static GameObject _runner;

        private void Awake()
        {
            Log = Logger;
            Dir = Path.GetDirectoryName(Info.Location);
            _hidden = new ConfigFile(Path.Combine(Path.Combine(Paths.ConfigPath, "FemalePlayer"), "hidden-settings.not-saved"), false) { SaveOnConfigSet = false };

            Config.Bind("General", "Apocasetter", true, "Show this mod in the Apocasetter Mods menu");
            Enabled = Config.Bind("General", "Enabled", true, "Play as a woman. Off = the game's own player (arms, driver) comes back at once.");
            BodyFirstPerson = Config.Bind("General", "BodyFirstPerson", true, "See her body in first person: legs and torso when you look down, and her shadow.");
            FemaleArms = Config.Bind("General", "FemaleArms", true, "Her bare arms and black gloves on the first-person weapon, tool and item animations (and the kick leg in her boot).");
            ReplaceDriver = Config.Bind("General", "ReplaceDriver", true, "She sits in the driver's seat instead of the game's driver model (seen from the car's third-person camera, and her arms on the wheel in first person).");
            ThirdPersonOnFoot = Config.Bind("General", "ThirdPersonOnFoot", true, "The game's Change Camera key switches to a camera behind her - on foot and in cars (it replaces the game's own car view, in which nothing in the car could be used). The mouse wheel zooms in / out (saved separately on foot and in cars). Shots and picks still go where the crosshair is; everything you can use in first person works.");
            MirrorBody = Config.Bind("General", "RightHanded", true, "The game's raider animations hold guns in the left hand. On = her body is mirrored so she holds the gun in her right hand.");
            WeaponAdjust = Config.Bind("Weapon grip", "WeaponAdjustment", false, "On: in third person set where the weapon sits in her hand for the animation she is in right now (walk, crouch, strafe, fire, reload ... to tune that one; each weapon and animation on its own) with the numpad - 8/2 up/down, 6/4 right/left, 7/1 forward/back; Numpad 5 switches between moving and turning it (8/2 muzzle up/down, 6/4 muzzle right/left, 7/1 roll); Numpad - deletes the animation's pose (it shows the weapon's Idle pose); Numpad / copies the shown pose and Numpad * pastes it. Poses are exact positions saved in config/FemalePlayer/weapon-poses.txt (and as C# in BuiltinPoses.generated.cs) - nothing else changes them.");
            CarCameraForward = Config.Bind("Camera", "CarCameraForward", 0.12f, new ConfigDescription("Driving in first person with her body shown: the view is drawn this far (m) in front of the game's eye so her head and chest don't block it (only the picture moves; 0 = off).", new AcceptableValueRange<float>(0f, 0.4f)));
            GunPose.Bind(Config);
            OrbitSpeed = H("Camera", "OrbitSpeed", 3f, "Third person: degrees per mouse step while the middle mouse button orbits the camera around her.");
            OrbitMiddleMouse = Config.Bind("Debug", "OrbitMiddleMouse", false, "Third person: clicking the middle mouse button turns orbiting the camera around her on / off (for looking at her from the front).");
            IgnitionKey = Config.Bind("Car", "IgnitionKey", KeyCode.E, "In the driver's seat: turns the key and starts the engine (the same as clicking the ignition twice). A hint shows on the left while the engine is off. None = off.");
            VerboseLog = Config.Bind("Debug", "VerboseLog", false, "Detailed log lines (what was found, which clips/props are used).");

            ModelFile = H("Model", "Model", "Models/Boss_lady.glb", "Body model (.glb/.gltf) relative to the mod folder, rigged to Flexa's skeleton (22 mixamorig bones).");
            TextureFile = H("Model", "Texture", "Models/Boss_lady.png", "Body texture relative to the mod folder.");
            ArmsTextureFile = H("Model", "ArmsTexture", "Models/Player2_female_arms.png", "Replacement for the game's Player2 texture atlas, used only on the first-person arms and kick leg.");

            AnimBundleFile = H("Model", "AnimationBundle", "Models/femaleplayer_anims.bundle", "AssetBundle with humanoid clips (Mixamo) built by the UnityAnims project; when it has Idle + Walk her locomotion comes from it.");
            JumpClipStart = H("Animation", "JumpClipStart", 0.2f, "Jump / RifleJump / PistolJump clips start this far in (share of the clip): Mixamo jumps crouch first, the game's jump leaves the ground at once.");
            HipsDrift = H("Animation", "HipsDrift", 0.08f, "Bundle clips: how far (m) her hips may move away from the player sideways/forward (stops clips with baked forward motion from walking ahead of you).");
            ClipWalkSpeed = H("Animation", "BundleWalkSpeed", 1.4f, "Ground speed (m/s) at which the bundle's walk clips play at normal speed.");
            ClipRunSpeed = H("Animation", "BundleRunSpeed", 3.8f, "Ground speed (m/s) at which the bundle's run clips play at normal speed.");
            ClipCrouchSpeed = H("Animation", "BundleCrouchSpeed", 1.0f, "Ground speed (m/s) at which the bundle's crouch walk plays at normal speed.");
            IdleClip = H("Animation", "IdleClip", "merchant_idle", "Humanoid clip for standing (the game has: merchant_idle, enemy_1_idle, zombie_idle).");
            RunClip = H("Animation", "RunClip", "enemy_1_run", "Humanoid clip for running.");
            RifleClip = H("Animation", "RifleClip", "enemy_machinegun", "Upper-body clip with a rifle / SMG / shotgun / crossbow drawn.");
            PistolClip = H("Animation", "PistolClip", "sprokka_shoot", "Upper-body clip with a pistol / revolver drawn.");
            MeleeClip = H("Animation", "MeleeClip", "enemy_1_attack", "Upper-body clip played once per melee swing.");
            RunClipSpeed = H("Animation", "RunClipSpeed", 4.5f, "Ground speed (m/s) at which the run clip plays at normal speed.");
            RunFrom = H("Animation", "RunFrom", 3.2f, "From this speed (m/s) the run clip takes over from the procedural walk.");
            WalkStride = H("Animation", "WalkStride", 1.25f, "Length of one full walk cycle (two steps), m.");
            ThighSwing = H("Animation", "ThighSwing", 26f, "Procedural walk: thigh swing, degrees.");
            KneeBend = H("Animation", "KneeBend", 38f, "Procedural walk: knee bend in the swing phase, degrees.");
            ArmSwing = H("Animation", "ArmSwing", 16f, "Procedural walk: arm swing without a weapon, degrees.");
            HipBob = H("Animation", "HipBob", 0.025f, "Procedural walk: hip bob, m.");
            CrouchDrop = H("Animation", "CrouchDrop", 0.42f, "How far the hips go down when crouched, m.");
            AimPitchShare = H("Animation", "AimPitch", 0.9f, "Share of the camera pitch the spine follows (aiming up/down).");

            BodyBack = Config.Bind("Camera", "FirstPersonBodyBack", 0.08f, new ConfigDescription("First person on foot: her head sits this far (m) behind the camera. More = less of her chest in the view when looking down; less = more of her body.", new AcceptableValueRange<float>(0f, 0.4f)));
            FirstPersonWeaponBack = Config.Bind("Camera", "FirstPersonWeaponBack", 0.08f, new ConfigDescription("First person with a weapon drawn: her body moves this much (m) further back, so the game's arms don't sink into her chest (same as moving the camera forward, but the arms stay undistorted).", new AcceptableValueRange<float>(0f, 0.4f)));
            FirstPersonChestLean = Config.Bind("Camera", "FirstPersonChestLean", 20f, new ConfigDescription("First person with a weapon drawn: her chest bends back by this many degrees (up to 40% more when looking down) to keep it out of the arms.", new AcceptableValueRange<float>(0f, 45f)));
            BodyBackDown = H("Camera", "BodyBackDown", 0.08f, "First person: extra distance behind the camera when looking straight down, m (blended in with the pitch).");
            ThirdDistance = Config.Bind("Camera", "ThirdPersonDistance", 2.4f, new ConfigDescription("Third person on foot: camera distance behind her, m (the mouse wheel changes it and it is saved here).", new AcceptableValueRange<float>(0.8f, 6f)));
            ThirdHeight = H("Camera", "ThirdHeight", 0.25f, "Third person on foot: camera height above the eyes, m.");
            ThirdCarDistance = Config.Bind("Camera", "ThirdPersonCarDistance", 5.5f, new ConfigDescription("Third person in a car: camera distance behind her, m (the mouse wheel changes it and it is saved here).", new AcceptableValueRange<float>(2.5f, 15f)));
            ThirdCarHeight = H("Camera", "ThirdCarHeight", 1.1f, "Third person in a car: camera height above her eyes, m.");
            ThirdShoulder = H("Camera", "ThirdShoulder", 0.45f, "Third person on foot: sideways offset (over the right shoulder), m.");

            try
            {
                var orphans = typeof(ConfigFile).GetProperty("OrphanedEntries", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                var dict = orphans != null ? orphans.GetValue(Config, null) as System.Collections.IDictionary : null;
                if (dict != null && dict.Count > 0) { int n = dict.Count; dict.Clear(); Config.Save(); Log.LogInfo("Config: " + n + " old setting(s) removed from the file"); }
            }
            catch (Exception e) { Log.LogWarning("Config cleanup: " + e.Message); }

            try
            {
                new HarmonyLib.Harmony(GUID).Patch(HarmonyLib.AccessTools.Method(typeof(HutongGames.PlayMaker.Actions.MouseLook), "OnUpdate"),
                    prefix: new HarmonyLib.HarmonyMethod(typeof(ThirdPerson), nameof(ThirdPerson.BeforeMouseLook)));
                new HarmonyLib.Harmony(GUID).Patch(HarmonyLib.AccessTools.Method(typeof(HutongGames.PlayMaker.Actions.GetButtonDown), "OnUpdate"),
                    prefix: new HarmonyLib.HarmonyMethod(typeof(ThirdPerson), nameof(ThirdPerson.BeforeGetButtonDown)));
            }
            catch (Exception e) { Log.LogError("Harmony patch failed, no camera orbit: " + e.Message); }

            SceneManager.sceneLoaded += (s, m) => { EnsureRunner(); Runner.OnSceneLoaded(); };
            EnsureRunner();
            Log.LogInfo(NAME + " " + VERSION + " loaded");
        }

        private static void EnsureRunner()
        {
            if (_runner != null) return;
            _runner = new GameObject("FemalePlayer.Runner") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(_runner);
            _runner.AddComponent<Runner>();
        }

        internal static string ModPath(string rel)
        {
            rel = (rel ?? "").Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            return Path.IsPathRooted(rel) ? rel : Path.Combine(Dir, rel);
        }

        internal static void Verbose(string s) { if (VerboseLog != null && VerboseLog.Value && Log != null) Log.LogInfo(s); }
        internal static void Warn(string s) { if (Log != null) Log.LogWarning(s); }
    }
}
