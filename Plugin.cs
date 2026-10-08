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
        public const string VERSION = "2.2.9";

        internal static ManualLogSource Log;
        internal static string Dir;

        // player-facing
        internal static ConfigEntry<bool> WeaponAdjust, Enabled, BodyFirstPerson, FemaleArms, EmptyHandArms, ReplaceDriver, ThirdPersonOnFoot, MirrorBody, VerboseLog;
        // hidden (fixed values, never written to the file; change H( to Config.Bind( to expose one)
        internal static ConfigEntry<string> ModelFile, TextureFile, ArmsTextureFile, AnimBundleFile;
        internal static ConfigEntry<string> IdleClip, RunClip, RifleClip, PistolClip, MeleeClip;
        internal static ConfigEntry<float> BodyBack, BodyBackDown, RunClipSpeed, WalkStride, RunFrom, ThirdDistance, ThirdHeight, ThirdShoulder, AimPitchShare;
        internal static ConfigEntry<float> CarCameraForward, FirstPersonWeaponBack, FirstPersonChestLean;
        internal static ConfigEntry<float> FpCamX, FpCamY, FpCamZ, FpCarCamX, FpCarCamY, FpCarCamZ;
        // the driver's-seat view offset Denis set in game (1.5.3: X -0.03, Y 0.02, Z 0.02) is the built-in base; the [Debug] values add to it
        internal static readonly Vector3 CarViewBase = new Vector3(-0.03f, 0.02f, 0.02f);
        internal static ConfigEntry<float> ThirdCarDistance, ThirdCarHeight;
        internal static ConfigEntry<bool> ToggleMiddleMouse, EnableMMB, VerboseModApi;
        internal static ConfigEntry<float> JumpClipStart, StrikeWindup, PickAssistRadius, AdsTime, SeatDrop;
        internal static ConfigEntry<KeyCode> IgnitionKey, ObserveKey, HeadlightsKey, CassetteKey, VolumeDownKey, VolumeUpKey;
        internal static ConfigEntry<bool> VehicleHotkeyHint, VehicleStatusHint;
        internal static ConfigEntry<bool> OcclusionPrototype, OcclusionInVehicle;
        internal static ConfigEntry<float> CullingDarkness;
        internal static ConfigEntry<bool> AutomaticStepUp;
        internal static ConfigEntry<float> OcclusionOpacity, OcclusionRadius;
        internal enum Gender { Female, Male, Max }
        internal static ConfigEntry<Gender> Character;
        public static bool Female { get { return Character == null || Character.Value == Gender.Female; } }
        // the body: hers (Player_female, made from Flexa) or the game's own man (Player2 + hair, beard, bags, re-rigged onto Flexa's skeleton by
        // tools/bake_male.py) - same skeleton, so every animation, weapon pose and feature is the same
        // Max: Denis's low-poly Max (the game man's mesh reshaped, his own UVs on Player_max.png), same 22-bone skeleton
        public static bool IsMax { get { return Character != null && Character.Value == Gender.Max; } }
        public static string BodyModelFile { get { return Female ? ModelFile.Value : IsMax ? "Models/Player_max.glb" : "Models/Player_male.glb"; } }
        public static string BodyTextureFile { get { return Female ? TextureFile.Value : IsMax ? "Models/Player_max.png" : "Models/Player_male.png"; } }
        internal static ConfigEntry<bool> CrouchRemap;
        internal static ConfigEntry<float> CrouchArmsRest, CrouchArmsPhase, CrouchPistolLean, CrouchPistolArms;   // (1.7.x, unused since 2.0)
        internal static ConfigEntry<float> UpperPhase, ClipSprintSpeed, SprintFrom, TurnClipAngle, TurnStartRate, DirBlendSpeed, PumpClipSpeed;
        internal static ConfigEntry<bool> FootIK, WalkToStop;
        internal static ConfigEntry<float> RunLegsIn, RunLegsTurn;
        internal static ConfigEntry<bool> DynamicCrosshair;
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
            Character = Config.Bind("General", "Character", Gender.Female, "Female: her body, arms and gloves in first person, her TAB-screen picture. Male: the game's own man (his body in third person, in first person and in the driver's seat; the game's arms and TAB picture). Max: a road warrior in a leather jacket (his body, his arms in first person - armoured left sleeve, bare right forearm, black gloves - and his own TAB picture). Everything else is the same.");
            Character.SettingChanged += (s, e) => Runner.CharacterChanged();
            // Preserve a previously rebound ignition key when moving it into VEHICLE.
            var oldIgnition = Config.Bind("General", "IgnitionKey", KeyCode.E, "Legacy ignition binding.");
            var previousIgnitionKey = oldIgnition.Value;
            Config.Remove(oldIgnition.Definition);
            VehicleHotkeyHint = Config.Bind("VEHICLE", "VehicleHotkeyHint", true, "Show vehicle hotkeys on the left while driving. Hiding hints keeps the keys working.");
            VehicleStatusHint = Config.Bind("VEHICLE", "VehicleStatusHint", true, "Show ignition off, handbrake engaged and playing cassette with volume in the top right while driving.");
            bool ignitionAlreadyMigrated = Config.ContainsKey(new ConfigDefinition("VEHICLE", "IgnitionKey"));
            IgnitionKey = Config.Bind("VEHICLE", "IgnitionKey", KeyCode.E, "Start / stop ignition in the driver's seat. None = no hotkey.");
            if (!ignitionAlreadyMigrated) IgnitionKey.Value = previousIgnitionKey;
            HeadlightsKey = Config.Bind("VEHICLE", "HeadlightsKey", KeyCode.X, "Switch headlights on / off in the driver's seat. None = no hotkey.");
            CassetteKey = Config.Bind("VEHICLE", "CassetteKey", KeyCode.Z, "Start / stop the cassette player. Starting at zero volume sets 0.5; a nonzero volume is kept. None = no hotkey.");
            VolumeDownKey = Config.Bind("VEHICLE", "VolumeDownKey", KeyCode.Minus, "Reduce cassette volume by 0.1. None = no hotkey.");
            VolumeUpKey = Config.Bind("VEHICLE", "VolumeUpKey", KeyCode.Equals, "Increase cassette volume by 0.1 (+ on the main keyboard). None = no hotkey.");
            EmptyHandArms = Config.Bind("General", "FirstPersonArms", false, "First person with nothing in hand: show the body's own arms (idle/walk swing, hands on the wheel in a car). Off = no body arms in first person (they can get in the way when crouching or driving); the game's weapon and item arms always show.");
            BodyFirstPerson = Config.Bind("General", "BodyFirstPerson", false, "First person: see her body (legs and torso when you look down, her shadow, her body in the driver's seat). Off = only the first-person arms.");
            AutomaticStepUp = Config.Bind("General", "AutomaticStepUp", true, "Automatically step onto low solid obstacles up to 35 cm while moving on foot. Requires ground contact, a walkable top and clearance for the entire body. Off while jumping, prone or driving.");
            OcclusionPrototype = BindCameraCulling(Config);
            OcclusionInVehicle = Config.Bind("General", "3rd person camera culling in vehicles", false, "Third person while driving: also use the camera culling (needs 3rd person camera culling on). Off: in a vehicle the camera moves in front of what blocks it instead.");
            CullingDarkness = Config.Bind("General", "3rd person camera culling darkness", .12f, new ConfigDescription("The camera culling switches itself off where the picture is darker than this (caves, unlit rooms, night: the cut-away views light those up in patches) and the camera moves in front of what blocks it instead. 0 = never; higher = it switches off sooner. VerboseLog shows the measured brightness.", new AcceptableValueRange<float>(0f, .5f)));
            EnableMMB = Config.Bind("General", "EnableMMB", false, "Third person: the middle mouse button also orbits the camera around her. Off by default: the game uses the middle mouse button to rotate a held item.");
            ObserveKey = Config.Bind("General", "RebindObserving", KeyCode.LeftAlt, "Third person: hold this key to orbit the camera around her (observe her), back behind her on release. None = no key (only the middle mouse button, if EnableMMB).");
            DynamicCrosshair = Config.Bind("CAMERA", "DynamicCrosshair", true, "Third person: the crosshair sits where your shots, melee hits and pickups really land (the eye ray from her head): at the centre for far targets, moving left toward her as the target gets closer, on her head when you look straight down. Off: the camera turns toward the aim point instead (old behaviour).");
            OcclusionOpacity = Config.Bind("CAMERA", "OcclusionOpacity", .20f, new ConfigDescription("Opacity of blocking geometry inside the cutaway window: 0 = clear, 0.2 = faintly visible.", new AcceptableValueRange<float>(0f, .9f)));
            OcclusionRadius = Config.Bind("CAMERA", "OcclusionRadius", .55f, new ConfigDescription("Width around the character cleared by the cutaway, in metres. The rest of a large object remains visible.", new AcceptableValueRange<float>(.2f, 1.5f)));
            WeaponAdjust = Config.Bind("Debug", "WeaponAdjustment", false, "Third person: numpad 8/2 6/4 7/1 move the weapon in her hand, 5 move/rotate, 9/3 pick the animation, - / * delete/copy/paste.\nSaved to config/Apocaplayer/weapon-poses.txt (overrides the built-in poses).\nAiming (or an aim clip picked with 9/3): Page Up/Down lift her hands, Home/End tilt her head, Insert/Delete push the hands forward - saved to config/Apocaplayer/aim-lift.txt.");
            ToggleMiddleMouse = Config.Bind("Debug", "ToggleMiddleMouse", false, "Third person: on = a press of the observing key (RebindObserving) / middle mouse button locks the camera orbiting around her (her rotation stays) until the next press - handy with WeaponAdjustment; off = hold it to orbit, back behind her on release.");
            // (2.2.8) visible again, for slow-machine reports: off = nothing measured and nothing extra logged
            VerboseLog = Config.Bind("Debug", "VerboseLog", false, "Diagnostics for performance reports, written to BepInEx\\LogOutput.log: the PC (GPU, VRAM, CPU, RAM, quality settings, other mods) once, then every 10 s the FPS, the slowest frame and the stutters, how many ms of each frame Apocaplayer takes (and where), garbage collections, the camera cutaway's work and which cameras render; plus what the mod finds and decides (weapons, cars, seats...). Off = none of it is measured or written.");
            VerboseModApi = H("Debug", "VerboseModApi", false, "Every decision of every ModAPI character (very chatty).");
            // first-person view vs her body (with BodyFirstPerson), on foot and driving: the game's camera stays where it is (aiming, clicks, weapons
            // unchanged) - her body is moved the opposite way. (2.1.11) sliders in CAMERA, next to each other; the old [Debug] values move over
            FpCamX = CameraSlider("FirstPersonBodyX", "FirstPersonCameraX"); FpCamY = CameraSlider("FirstPersonBodyY", "FirstPersonCameraY"); FpCamZ = CameraSlider("FirstPersonBodyZ", "FirstPersonCameraZ");
            FpCarCamX = CameraSlider("FirstPersonDrivingX", "FirstPersonDrivingCameraX"); FpCarCamY = CameraSlider("FirstPersonDrivingY", "FirstPersonDrivingCameraY"); FpCarCamZ = CameraSlider("FirstPersonDrivingZ", "FirstPersonDrivingCameraZ");
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
            AimLift.Load(System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "Apocaplayer"));
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
            CrouchRemap = H("Animation", "CrouchRifleLegs", true, "Bundle clips, crouched unarmed / with a pistol: the legs (and hips/spine) from the Rifle crouch clips, the arms and head from CrouchWalk (unarmed) or PistolFire (pistol).");
            CrouchArmsRest = H("Animation", "CrouchArmsRestFrame", 0.25f, "Unarmed crouch standing still: the CrouchWalk frame (0..1 of the clip) whose arms are held.");
            CrouchArmsPhase = H("Animation", "CrouchArmsPhase", 0f, "Unarmed crouch walking: CrouchWalk arms' phase against the rifle crouch legs (0..1; 0.5 = the other step).");
            CrouchPistolLean = H("Animation", "CrouchPistolLean", 3f, "Crouched with a pistol: how far (degrees) the torso (hips to head) leans forward; the rifle crouch's deeper lean is straightened to this.");
            CrouchPistolArms = H("Animation", "CrouchPistolArms", 0f, "Crouched with a pistol: the arms (shoulders to hands) are raised to this many degrees below level before the aim pitch is added.");
            UpperPhase = H("Animation", "UpperPhase", 0f, "2.0: walking / running upper-body clips (Walk, Run, PistolRun) play in step with the rifle legs; this shifts them (0..1 of a stride, 0.5 = the other foot).");
            ClipSprintSpeed = H("Animation", "BundleSprintSpeed", 5.5f, "Ground speed (m/s) at which the rifle pack's sprint clips play at normal speed (when the clip has no root motion of its own).");
            SprintFrom = H("Animation", "SprintFrom", 4.3f, "Ground speed (m/s) above which the rifle pack's sprint clips replace its run clips (the game runs at 5).");
            TurnClipAngle = H("Animation", "TurnClipAngle", 70f, "0 = no turn-in-place clips. (2.1.5: any other value = on; the clips start with the first degree, see TurnStartRate.)");
            TurnStartRate = H("Animation", "TurnStartRate", 8f, "Standing still: turning faster than this (degrees per second) steps her feet with the turn-in-place clips, driven by the turn itself.");
            PumpClipSpeed = H("Animation", "PumpClipSpeed", 2f, "ShotgunPump (cocking the pump shotgun / bolt rifle) plays this fast (x normal; its rack is ~2 s at 1x).");
            DirBlendSpeed = H("Animation", "DirBlendSpeed", 360f, "How fast (degrees per second) the legs' walking direction turns when the movement keys change (strafe left -> forward passes the diagonal).");
            FootIK = H("Animation", "FootIK", true, "Unity's humanoid foot IK on the bundle's idle and walking clips (standing and crouched). Never on the run / sprint / jump clips: their foot goals were made on another skeleton and the solver twisted the shins.");
            RunLegsIn = H("Animation", "RunLegsIn", 10f, "Running (bundle animations): each thigh turned this many degrees toward the middle, the feet kept flat - the rifle pack's run and sprint cycles stand wide on her hips (10 = Denis's setting).");
            WalkToStop = H("Animation", "WalkToStop", true, "Relaxed walking forward and stopping: the RifleWalkToStop clip's last step settles her feet.");
            RunLegsTurn = H("Animation", "RunLegsTurn", 75f, "Relaxed running (not aiming): the legs turn up to this many degrees toward the way she runs; the chest keeps facing the camera.");
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
            try { FemalePain.Patch(new HarmonyLib.Harmony(GUID)); }
            catch (Exception e) { Log.LogError("Female pain sounds: " + e.Message); }

            SceneManager.sceneLoaded += (s, m) => { EnsureRunner(); Runner.OnSceneLoaded(); ModAPI.OnSceneLoaded(); };
            EnsureRunner();
            Log.LogInfo(NAME + " " + VERSION + " loaded");
        }

        // a first-person offset slider (m) in CAMERA without a description; its old [Debug] value is carried over once
        private ConfigEntry<float> CameraSlider(string key, string oldKey)
        {
            bool fresh = !Config.ContainsKey(new ConfigDefinition("CAMERA", key));
            var old = Config.Bind("Debug", oldKey, 0f, "");
            float v = old.Value; Config.Remove(old.Definition);
            var e = Config.Bind("CAMERA", key, 0f, new ConfigDescription("", new AcceptableValueRange<float>(-0.3f, 0.3f)));
            if (fresh && Mathf.Abs(v) > 1e-5f) e.Value = Mathf.Clamp(v, -0.3f, 0.3f);
            return e;
        }

        private static void EnsureRunner()
        {
            if (_runner != null) return;
            _runner = new GameObject("Apocaplayer.Runner") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(_runner);
            _runner.AddComponent<Runner>();
            _runner.AddComponent<ModApiRunner>();          // (2.2.0) characters animated through ModAPI, after the Animators
        }

        internal static string ModPath(string rel)
        {
            rel = (rel ?? "").Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            return Path.IsPathRooted(rel) ? rel : Path.Combine(Dir, rel);
        }

        internal static ConfigEntry<bool> BindCameraCulling(ConfigFile config)
        {
            var definition = new ConfigDefinition("General", "3rd person camera culling");
            var property = typeof(ConfigFile).GetProperty("OrphanedEntries", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            var orphans = property != null ? property.GetValue(config, null) as System.Collections.IDictionary : null;
            bool alreadySaved = config.ContainsKey(definition) || orphans != null && orphans.Contains(definition);
            var legacy = config.Bind("CAMERA", "OcclusionPrototype", true, "Legacy camera culling toggle.");
            bool oldValue = legacy.Value; config.Remove(legacy.Definition);
            var entry = config.Bind("General", "3rd person camera culling", true, "On: keep third-person camera distance and make blocking geometry semi-transparent between camera and body. Roofs and vehicles get a larger window. Off: restore collision-based camera movement.");
            if (!alreadySaved) entry.Value = oldValue;
            return entry;
        }

        internal static void Verbose(string s) { if (VerboseLog != null && VerboseLog.Value && Log != null) Log.LogInfo(s); }
        internal static void Warn(string s) { if (Log != null) Log.LogWarning(s); }
    }
}
