using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Apocaplayer
{
    // Your character in Apocalypter (female or the game's man): a female body under the first-person camera (legs and torso when you look down,
    // a shadow), her bare arms and gloves on every first-person weapon / item animation, an optional third-person
    // camera on foot (body animated with Flexa's own humanoid clips, the NPC gun model in her hand), and her in the
    // driver's seat of every car instead of the game's seated man.
    [BepInPlugin(GUID, NAME, VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public const string GUID = "com.denis.apocalypter.apocaplayer";
        public const string NAME = "Apocaplayer";
        public const string VERSION = "1.0.9";

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
        internal static ConfigEntry<bool> ToggleMiddleMouse, EnableMMB;
        internal static ConfigEntry<float> JumpClipStart, StrikeWindup, PickAssistRadius, AdsTime, SeatDrop;
        internal static ConfigEntry<KeyCode> IgnitionKey, ObserveKey;
        internal enum Gender { Female, Male }
        internal static ConfigEntry<Gender> Character;
        public static bool Female { get { return Character == null || Character.Value == Gender.Female; } }
        // the body: hers (Player_female, made from Flexa) or the game's own man (Player2 + hair, beard, bags, re-rigged onto Flexa's skeleton by
        // tools/bake_male.py) - same skeleton, so every animation, weapon pose and feature is the same
        public static string BodyModelFile { get { return Female ? ModelFile.Value : "Models/Player_male.glb"; } }
        public static string BodyTextureFile { get { return Female ? TextureFile.Value : "Models/Player_male.png"; } }
        internal static ConfigEntry<float> OrbitSpeed, HipsDrift, ClipWalkSpeed, ClipRunSpeed, ClipCrouchSpeed, ThighSwing, KneeBend, ArmSwing, HipBob, CrouchDrop;

        private static ConfigFile _hidden;
        private static ConfigEntry<T> H<T>(string section, string key, T value, string description) { return _hidden.Bind(section, key, value, description); }

        private static GameObject _runner;

        private void Awake()
        {
            Log = Logger;
            Dir = Path.GetDirectoryName(Info.Location);
            _hidden = new ConfigFile(Path.Combine(Path.Combine(Paths.ConfigPath, "Apocaplayer"), "hidden-settings.not-saved"), false) { SaveOnConfigSet = false };

            // the config file: General (Enabled, Character, IgnitionKey, BodyFirstPerson, EnableMMB, RebindObserving) and Debug only - everything else is fixed (H) or kept in its own file
            Config.Bind("General", "Apocasetter", true, "Show this mod in the Apocasetter Mods menu");
            Enabled = Config.Bind("General", "Enabled", true, "Your character: body, third-person camera and the rest of this mod. Off = the game's own player (arms, driver, TAB picture) comes back at once.");
            Character = Config.Bind("General", "Character", Gender.Female, "Female: her body, arms and gloves in first person, her TAB-screen picture. Male: the game's own man (his body in third person, in first person and in the driver's seat; the game's arms and TAB picture). Everything else is the same.");
            Character.SettingChanged += (s, e) => Runner.CharacterChanged();
            IgnitionKey = Config.Bind("General", "IgnitionKey", KeyCode.E, "In the driver's seat: one press turns the key and starts the engine, another stops it. None = off.");
            BodyFirstPerson = Config.Bind("General", "BodyFirstPerson", false, "First person: see her body (legs and torso when you look down, her shadow, her body in the driver's seat). Off = only the first-person arms.");
            EnableMMB = Config.Bind("General", "EnableMMB", false, "Third person: the middle mouse button also orbits the camera around her. Off by default: the game uses the middle mouse button to rotate a held item.");
            ObserveKey = Config.Bind("General", "RebindObserving", KeyCode.LeftAlt, "Third person: hold this key to orbit the camera around her (observe her), back behind her on release. None = no key (only the middle mouse button, if EnableMMB).");
            WeaponAdjust = Config.Bind("Debug", "WeaponAdjustment", false, "Third person: numpad 8/2 6/4 7/1 move the weapon in her hand, 5 move/rotate, 9/3 pick the animation, - / * delete/copy/paste.\nSaved to config/Apocaplayer/weapon-poses.txt (overrides the built-in poses).");
            ToggleMiddleMouse = Config.Bind("Debug", "ToggleMiddleMouse", false, "Third person: off = hold the observing key / middle mouse button to orbit around her (back on release); on = a press turns orbiting on / off.");
            VerboseLog = Config.Bind("Debug", "VerboseLog", false, "Detailed log lines.");
            FemaleArms = H("General", "FemaleArms", true, "Her bare arms and black gloves on the first-person animations (and the kick leg).");
            ReplaceDriver = H("General", "ReplaceDriver", true, "She replaces the game's man in the driver's seat and on the TAB screen.");
            ThirdPersonOnFoot = H("General", "ThirdPersonOnFoot", true, "The Change Camera key switches to a camera behind her, on foot and in cars.");
            MirrorBody = H("General", "RightHanded", true, "Raider-clip mode only: mirror her body so the gun is in her right hand.");
            CarCameraForward = H("Camera", "CarCameraForward", 0.12f, "Driving in first person: the view is drawn this far (m) in front of the game's eye.");
            BodyBack = H("Camera", "FirstPersonBodyBack", 0.08f, "First person on foot: her head sits this far (m) behind the camera.");
            FirstPersonWeaponBack = H("Camera", "FirstPersonWeaponBack", 0.08f, "First person with a weapon drawn: her body moves this much (m) further back.");
            FirstPersonChestLean = H("Camera", "FirstPersonChestLean", 20f, "First person with a weapon drawn: her chest bends back by this many degrees.");
            // the mouse-wheel distances are remembered in their own file (not shown in the config / Apocasetter)
            string camPath = Path.Combine(Path.Combine(Paths.ConfigPath, "Apocaplayer"), "camera.cfg");
            bool camFresh = !File.Exists(camPath);
            var cam = new ConfigFile(camPath, true);
            ThirdDistance = cam.Bind("Camera", "ThirdPersonDistance", 2.4f, new ConfigDescription("Third person on foot: camera distance, m (mouse wheel).", new AcceptableValueRange<float>(0.8f, 8f)));
            ThirdCarDistance = cam.Bind("Camera", "ThirdPersonCarDistance", 5.5f, new ConfigDescription("Third person in a car: camera distance, m (mouse wheel).", new AcceptableValueRange<float>(2f, 15f)));
            GunPose.Bind(Config);
            OrbitSpeed = H("Camera", "OrbitSpeed", 3f, "Third person: degrees per mouse step while the middle mouse button orbits the camera around her.");

            ModelFile = H("Model", "Model", "Models/Player_female.glb", "Body model (.glb/.gltf) relative to the mod folder, rigged to Flexa's skeleton (22 mixamorig bones).");
            TextureFile = H("Model", "Texture", "Models/Player_female.png", "Body texture relative to the mod folder.");
            ArmsTextureFile = H("Model", "ArmsTexture", "Models/Player2_female_arms.png", "Replacement for the game's Player2 texture atlas, used only on the first-person arms and kick leg.");

            AnimBundleFile = H("Model", "AnimationBundle", "Models/apocaplayer_anims.bundle", "AssetBundle with humanoid clips (Mixamo) built by the UnityAnims project; when it has Idle + Walk her locomotion comes from it.");
            JumpClipStart = H("Animation", "JumpClipStart", 0.2f, "Jump / RifleJump / PistolJump clips start this far in (share of the clip): Mixamo jumps crouch first, the game's jump leaves the ground at once.");
            AdsTime = H("Camera", "AimDownSightsTime", 0.2f, "First person: seconds the gun takes to move between hip and sights (right mouse button) and back; 0 = the game's own (instant).");
            SeatDrop = H("Animation", "SeatDrop", 0.05f, "Driver's seat: her body sits this much (m) lower than the game's seated man (his pose put her a bit high for the steering wheel).");
            PickAssistRadius = H("Camera", "PickAssistRadius", 0.06f, "Third person: an item / part / switch this close to the cursor on screen (share of the screen height) is picked even if the eye ray misses it.");
            StrikeWindup = H("Animation", "StrikeWindup", 0.08f, "Melee / bare hands: seconds her wind-up takes before the blow lands (the game hits the moment the swing starts).");
            HipsDrift = H("Animation", "HipsDrift", 0.25f, "Bundle clips: how far (m) her hips may move away from the player sideways/forward (room for the idle sway; stops clips with baked forward motion from walking ahead of you).");
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

            BodyBackDown = H("Camera", "BodyBackDown", 0.08f, "First person: extra distance behind the camera when looking straight down, m (blended in with the pitch).");
            ThirdHeight = H("Camera", "ThirdHeight", 0.25f, "Third person on foot: camera height above the eyes, m.");
            ThirdCarHeight = H("Camera", "ThirdCarHeight", 1.1f, "Third person in a car: camera height above her eyes, m.");
            ThirdShoulder = H("Camera", "ThirdShoulder", 0.45f, "Third person on foot: sideways offset (over the right shoulder), m.");

            try
            {
                var orphans = typeof(ConfigFile).GetProperty("OrphanedEntries", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                var dict = orphans != null ? orphans.GetValue(Config, null) as System.Collections.IDictionary : null;
                if (dict != null && camFresh)   // the distances the wheel saved in the main file before 0.11.0 move to camera.cfg
                    foreach (System.Collections.DictionaryEntry de in dict)
                    {
                        var def = de.Key as ConfigDefinition; float f;
                        if (def == null || def.Section != "Camera" || !float.TryParse(de.Value as string, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f)) continue;
                        if (def.Key == "ThirdPersonDistance") ThirdDistance.Value = f;
                        if (def.Key == "ThirdPersonCarDistance") ThirdCarDistance.Value = f;
                    }
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
            try
            {
                new HarmonyLib.Harmony(GUID).Patch(HarmonyLib.AccessTools.Method(typeof(HutongGames.PlayMaker.Actions.ActivateGameObject), "DoActivateGameObject"),
                    prefix: new HarmonyLib.HarmonyMethod(typeof(ThirdPerson), nameof(ThirdPerson.BeforeActivateGameObject)));
            }
            catch (Exception e) { Log.LogError("Harmony patch failed, the crosshair hides when aiming in third person: " + e.Message); }
            try { Projectiles.Patch(new HarmonyLib.Harmony(GUID)); }
            catch (Exception e) { Log.LogError("Harmony patch failed, lances may hit your own car: " + e.Message); }
            try { PickAssist.Patch(new HarmonyLib.Harmony(GUID)); }
            catch (Exception e) { Log.LogError("Harmony patch failed, no third-person pick assist: " + e.Message); }
            try { AimTransition.Patch(new HarmonyLib.Harmony(GUID)); }
            catch (Exception e) { Log.LogError("Harmony patch failed, aiming down sights stays instant: " + e.Message); }

            SceneManager.sceneLoaded += (s, m) => { EnsureRunner(); Runner.OnSceneLoaded(); };
            EnsureRunner();
            Log.LogInfo(NAME + " " + VERSION + " loaded");
        }

        private static void EnsureRunner()
        {
            if (_runner != null) return;
            _runner = new GameObject("Apocaplayer.Runner") { hideFlags = HideFlags.HideAndDontSave };
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
