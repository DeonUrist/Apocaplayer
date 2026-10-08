using System;
using System.Collections.Generic;
using System.IO;

using BepInEx;
using BepInEx.Configuration;
using HutongGames.PlayMaker;
using UnityEngine;

namespace Apocaplayer
{
    internal sealed class ClimbingController
    {
        internal static ClimbingController Instance;
        const float FinishFraction = .65f;
        ConfigEntry<bool> enabledMod;
        ConfigEntry<bool> jumpTriggers;
        ConfigEntry<KeyCode> climbKey;
        int jumpAttemptFrame = -1;
        bool jumpConsumed;
        ConfigEntry<float> minHeight, maxHeight, reach, waistLimit, speed;
        AssetBundle bundle;
        AnimationClip waist, high;
        Rigidbody body;
        ClimbGeometry geometry;
        ClimbTarget target;
        PlayerTraversal animation;
        readonly List<PlayMakerFSM> suspended = new List<PlayMakerFSM>();
        readonly List<RendererState> hidden = new List<RendererState>();
        bool oldKinematic, oldGravity, climbing, highClimb;
        RigidbodyInterpolation oldInterpolation;
        float elapsed, duration, cooldown, playbackSpeed;
        Vector3 lastSafe, surfaceLocal;
        Quaternion surfaceRotation;

        struct RendererState { public Renderer Renderer; public bool Enabled; }

        internal ClimbingController(ConfigFile config)
        {
            Instance = this;
            enabledMod = config.Bind("climbing", "Enabled", Legacy<bool>("General", "Enabled", true), "Climb onto reachable solid ledges on foot.");
            jumpTriggers = config.Bind("climbing", "Jump triggers climbing", true, "The game's Jump key (Space by default) climbs when pushing forward into a reachable ledge. Otherwise it jumps normally.");
            climbKey = config.Bind("climbing", "alternative climb key", KeyCode.None, "Optional separate climb key. None = no alternative key. Useful when Jump triggers climbing is off.");
            minHeight = config.Bind("climbing", "MinimumHeight", Legacy<float>("Climbing", "MinimumHeight", .45f), new ConfigDescription("Minimum ledge height above the feet, metres.", new AcceptableValueRange<float>(.36f, 1f)));
            maxHeight = config.Bind("climbing", "MaximumHeight", Legacy<float>("Climbing", "MaximumHeight", 2.5f), new ConfigDescription("Maximum ledge height above the feet, metres.", new AcceptableValueRange<float>(1f, 2.6f)));
            reach = config.Bind("climbing", "Reach", Legacy<float>("Climbing", "Reach", .85f), new ConfigDescription("Maximum distance to the obstacle face, metres.", new AcceptableValueRange<float>(.35f, 1.2f)));
            waistLimit = config.Bind("climbing", "WaistHeightLimit", Legacy<float>("Climbing", "WaistHeightLimit", 1.25f), new ConfigDescription("Higher ledges use the high-climb animation.", new AcceptableValueRange<float>(.8f, 1.5f)));
            speed = config.Bind("climbing", "AnimationSpeed", Legacy<float>("Climbing", "AnimationSpeed", 1.5f), new ConfigDescription("Climb playback speed.", new AcceptableValueRange<float>(.6f, 2.5f)));
            Plugin.AutomaticStepUp = config.Bind("climbing", "AutomaticStepUp", Plugin.AutomaticStepUp.Value, "Automatically step onto low solid obstacles up to 35 cm while moving on foot. Off while jumping, prone or driving.");
            try
            {
                string path = Plugin.ModPath("Models/apocaplayer_climbing.bundle");
                bundle = AssetBundle.LoadFromFile(path);
                if (bundle != null) foreach (var clip in bundle.LoadAllAssets<AnimationClip>())
                {
                    if (clip.name == "ClimbWaist") waist = clip;
                    if (clip.name == "ClimbHigh") high = clip;
                }
                if (waist == null || high == null || !waist.humanMotion || !high.humanMotion)
                    throw new InvalidDataException("Missing Humanoid ClimbWaist/ClimbHigh clips");
                Plugin.Log.LogInfo("Integrated climbing ready; waist " + waist.length.ToString("0.00") + "s, high " + high.length.ToString("0.00") + "s.");
            }
            catch (Exception e) { Plugin.Log.LogError("Climbing animation bundle: " + e.Message); waist = high = null; }
        }

        static ConfigFile legacyConfig;
        static T Legacy<T>(string section, string key, T fallback)
        {
            try
            {
                if (legacyConfig == null)
                {
                    var path = Path.Combine(Paths.ConfigPath, "com.denis.apocalypter.apocaclimber.cfg");
                    if (!File.Exists(path)) return fallback;
                    legacyConfig = new ConfigFile(path, false) { SaveOnConfigSet = false };
                }
                return legacyConfig.Bind(section, key, fallback).Value;
            }
            catch { return fallback; }
        }

        internal bool TryJumpClimb()
        {
            if (!jumpTriggers.Value || !GameBindings.Down("Jump", KeyCode.Space)) return false;
            if (climbing) return true;
            // A failed early Update probe must not block the Jump FSM's later
            // probe: movement/grounding may have updated in between those calls.
            jumpAttemptFrame = Time.frameCount;
            jumpConsumed = TryStart(true);
            return jumpConsumed;
        }

        bool TryStart(bool pushing)
        {
            if (climbing || !Plugin.Enabled.Value || !enabledMod.Value || waist == null || high == null
                || Time.unscaledTime < cooldown || !ModAPI.PlayerCanTraverse || Cursor.visible) return false;
            if (pushing)
            {
                var vertical = Game.MovementFsm != null ? Game.MovementFsm.FsmVariables.FindFsmFloat("AxisVertical") : null;
                float input = vertical != null ? vertical.Value : 0;
                try { input = Mathf.Max(input, InsaneSystems.InputManager.InputController.GetKeyAxisActionValue("Vertical")); } catch { }
                if (input <= .1f) return false;
            }
            var player = ModAPI.PlayerRoot;
            var rb = player != null ? player.GetComponent<Rigidbody>() : null;
            if (rb == null) return false;
            if (body != rb) { body = rb; geometry = new ClimbGeometry(rb); }
            var camera = Game.PlayerCamera;
            var forward = camera != null ? camera.forward : player.transform.forward;
            if (!geometry.Find(forward, minHeight.Value, Mathf.Max(minHeight.Value, maxHeight.Value), reach.Value, out target))
            { Reject(geometry.Rejection); return false; }
            Begin();
            return climbing;
        }

        internal void Tick()
        {
            try
            {
                if (climbing)
                {
                    if (!Plugin.Enabled.Value || !enabledMod.Value || animation == null || !animation.Active || !body || body.gameObject != ModAPI.PlayerRoot
                        || target.Surface == null || !target.Surface.enabled || !target.Surface.gameObject.activeInHierarchy)
                    { Finish(false, "Player or surface unavailable"); return; }
                    if (Time.timeScale < .01f) return;
                    if (!ModAPI.PlayerCanTraverse) { Finish(false, "Player cannot continue climbing"); return; }
                    // Parked cars work. Cancel safely if somebody moves or rotates the
                    // support instead of dragging the character through new geometry.
                    if (!ClimbGeometry.Slow(target.Surface)
                        || Vector3.Distance(target.Surface.transform.TransformPoint(surfaceLocal), target.End) > .08f
                        || Quaternion.Angle(target.Surface.transform.rotation, surfaceRotation) > 3f)
                    { Finish(false, "Surface moved"); return; }
                    animation.Sample(Mathf.Min(elapsed, duration) * playbackSpeed, target.Facing);
                    return;
                }
                if (jumpTriggers.Value && GameBindings.Down("Jump", KeyCode.Space)) TryJumpClimb();
                if (!climbing && GameBindings.KeyDown(climbKey.Value)) TryStart(false);
            }
            catch (Exception e) { Finish(false, "Exception"); Plugin.Log.LogError(e); }
        }

        void Begin()
        {
            highClimb = target.Rise > waistLimit.Value;
            var clip = highClimb ? high : waist;
            animation = ModAPI.BeginPlayerTraversal(clip, target.Facing);
            if (animation == null) { Reject("Apocaplayer could not take control of the body animation"); return; }
            animation.StowWeapon = true;
            animation.FollowHeadInFirstPerson = true;
            oldKinematic = body.isKinematic; oldGravity = body.useGravity; oldInterpolation = body.interpolation;
            // Set climbing before any mutation so exceptions restore every saved state.
            climbing = true; elapsed = 0; playbackSpeed = speed.Value;
            // Both source clips finish their ascent before the final standing tail.
            // Give movement back at 65%; the pose blends out without holding physics.
            duration = clip.length * FinishFraction / playbackSpeed; lastSafe = body.position;
            surfaceLocal = target.Surface.transform.InverseTransformPoint(target.End);
            surfaceRotation = target.Surface.transform.rotation;
            foreach (var fsm in body.GetComponents<PlayMakerFSM>())
                if (fsm.enabled && (fsm.FsmName == "Movement" || fsm.FsmName == "Jump" || fsm.FsmName == "InCar")) Suspend(fsm);
            // Keep the camera active; suppress attacks and the game's separate first-person
            // weapon meshes while both hands are needed for climbing.
            var holder = animation.Camera;
            if (holder != null)
            {
                foreach (var fsm in holder.GetComponentsInChildren<PlayMakerFSM>(true))
                    if (fsm.enabled && (fsm.FsmName == "Attack" || fsm.FsmName == "Reload" || fsm.FsmName == "ReloadAnimation")) Suspend(fsm);
                foreach (var renderer in holder.GetComponentsInChildren<Renderer>(true))
                    if (renderer.transform.name != "" && (Under(renderer.transform, "WeaponsArm") || Under(renderer.transform, "QuickItems") || Under(renderer.transform, "ItemAnim")))
                    { hidden.Add(new RendererState { Renderer = renderer, Enabled = renderer.enabled }); renderer.enabled = false; }
            }
            body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero;
            body.useGravity = false; body.isKinematic = true; body.interpolation = RigidbodyInterpolation.None;
            animation.Sample(0, target.Facing);
            if (Plugin.VerboseLog.Value) Plugin.Log.LogInfo("Climb " + clip.name + " onto " + target.Surface.name + ", height " + target.Rise.ToString("0.00") + "m");
        }

        static bool Under(Transform t, string name)
        { for (; t != null; t = t.parent) if (t.name == name) return true; return false; }
        void Suspend(PlayMakerFSM fsm) { suspended.Add(fsm); fsm.enabled = false; }
        void Reject(string why) { if (Plugin.VerboseLog.Value) Plugin.Log.LogInfo("Climb rejected: " + why); }

        internal void FixedTick()
        {
            if (!climbing || body == null || Time.timeScale < .01f) return;
            try
            {
                float nextTime = Mathf.Min(duration, elapsed + Time.fixedDeltaTime);
                var next = ClimbPath.At(target, nextTime / duration, highClimb);
                if (!geometry.Clear(body.position, next)) { Finish(false, "Path became blocked"); return; }
                body.position = next; lastSafe = next; elapsed = nextTime;
                animation.Sample(elapsed * playbackSpeed, target.Facing);
                if (elapsed >= duration) Finish(true, "Complete");
            }
            catch (Exception e) { Finish(false, "Physics exception"); Plugin.Log.LogError(e); }
        }

        void Finish(bool completed, string why)
        {
            if (!climbing && animation == null) return;
            climbing = false;
            // Restore physics before restarting FSMs: their OnEnable can apply forces.
            if (body != null)
            {
                body.position = lastSafe;
                body.isKinematic = oldKinematic; body.useGravity = oldGravity; body.interpolation = oldInterpolation;
                if (!body.isKinematic) { body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            }
            if (animation != null) { animation.Dispose(); animation = null; }
            foreach (var state in hidden) if (state.Renderer != null) state.Renderer.enabled = state.Enabled;
            hidden.Clear();
            foreach (var fsm in suspended) if (fsm != null) fsm.enabled = true;
            suspended.Clear();
            cooldown = Time.unscaledTime + .15f;
            if (Plugin.VerboseLog != null && Plugin.VerboseLog.Value) Plugin.Log.LogInfo("Climb " + (completed ? "finished" : "cancelled") + ": " + why);
        }

        internal void Stop() { Finish(false, "Runner disabled"); }
        internal void Reset()
        { Stop(); body = null; geometry = null; cooldown = 0; jumpAttemptFrame = -1; jumpConsumed = false; }
        internal void Cleanup() { Finish(false, "Mod unloaded"); if (bundle != null) bundle.Unload(false); }
    }

    [DefaultExecutionOrder(-1000)]
    internal sealed class ClimbRunner : MonoBehaviour
    {
        // BepInEx's loader component can be destroyed when the game replaces its
        // bootstrap scene. Keep the controller on a persistent host like Apocaplayer.
        internal ClimbingController Controller;
        void Update() { Controller?.Tick(); }
        void FixedUpdate() { Controller?.FixedTick(); }
        void OnDisable() { Controller?.Stop(); }
        void OnDestroy() { Controller?.Cleanup(); }
    }
}
