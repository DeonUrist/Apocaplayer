using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Apocaplayer;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using HutongGames.PlayMaker;
using UnityEngine;
using UnityEngine.SceneManagement;

[BepInPlugin("apocaplayer.climbing-verifier", "Apocaplayer Climbing Verifier", "1.0.0")]
[BepInDependency(Apocaplayer.Plugin.GUID)]
public sealed class Probe : BaseUnityPlugin
{
    internal static bool Running;
    internal static bool InjectKeys;
    internal static readonly HashSet<KeyCode> Keys = new HashSet<KeyCode>();
    void Awake()
    {
        string arg = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("-apocaplayer-climbing-report="));
        if (arg == null) return;
        Running = true;
        new Harmony("apocaplayer.climbing-verifier").PatchAll();
        var host = new GameObject("Verifier.Host") { hideFlags = HideFlags.HideAndDontSave }; DontDestroyOnLoad(host);
        var runner = host.AddComponent<Checks>();
        runner.Output = arg.Substring(arg.IndexOf('=') + 1);
        runner.StartCoroutine(runner.Run());
    }
    [HarmonyPatch(typeof(PlayMakerFSM), "OnEnable")]
    static class NoWorldOrSaves
    {
        static bool Prefix(PlayMakerFSM __instance)
        {
            if (!Running) return true;
            for (var t = __instance.transform; t != null; t = t.parent)
                if (t.name.StartsWith("Verifier.")) return true;
            return false;
        }
    }
    [HarmonyPatch]
    static class Keyboard
    {
        static MethodBase TargetMethod() => typeof(Apocaplayer.Plugin).Assembly.GetType("Apocaplayer.GameBindings")
            .GetMethod("KeyDown", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        static bool Prefix(KeyCode key, ref bool __result)
        { if (!InjectKeys) return true; __result = key != KeyCode.None && Keys.Contains(key); return false; }
    }
}

public sealed class Checks : MonoBehaviour
{
    public string Output;
    const BindingFlags Flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static Assembly PlayerAssembly = typeof(Apocaplayer.Plugin).Assembly;
    static Assembly ClimbAssembly = PlayerAssembly;
    static Type CT => PT("ClimbingController");
    readonly List<string> report = new List<string>();
    readonly List<GameObject> fixtures = new List<GameObject>();
    Rigidbody rb;
    PlayMakerFSM movement, jump, car;
    GameObject player, ledge;
    object playerBody, geometry;
    object plugin;
    int checks;
    int poseSamples;
    static Type PT(string type) { return PlayerAssembly.GetType("Apocaplayer." + type); }
    static object Get(Type t, object obj, string name) { return t.GetField(name, Flags).GetValue(obj); }
    static void Set(Type t, object obj, string name, object value) { t.GetField(name, Flags).SetValue(obj, value); }
    static object Call(Type t, object obj, string method, params object[] args) { return t.GetMethod(method, Flags).Invoke(obj, args); }
    void Check(bool pass, string description) { report.Add((pass ? "PASS " : "FAIL ") + description); checks++; if (!pass) throw new Exception(description); }
    void DisableRunners()
    {
        foreach (var r in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            if (r != null && (r.GetType() == PT("Runner") || r.GetType() == PT("ModApiRunner") || r.GetType() == PT("ClimbRunner"))) r.enabled = false;
    }
    public IEnumerator Run()
    {
        Directory.CreateDirectory(Output); File.WriteAllText(Path.Combine(Output, "progress.txt"), "Started\n");
        Application.runInBackground = true; Time.timeScale = 1;
        yield return null;
        plugin = Get(CT, null, "Instance");
        DisableRunners();
        File.AppendAllText(Path.Combine(Output, "progress.txt"), "Loading fixture scene\n");
        yield return SceneManager.LoadSceneAsync(1);
        DisableRunners();
        yield return null;
        var nativePlayer = GameObject.Find("Player");
        if (nativePlayer != null) foreach (var capsule in nativePlayer.GetComponents<CapsuleCollider>())
            report.Add("NATIVE CAPSULE radius=" + capsule.radius + " height=" + capsule.height + " center=" + capsule.center + " enabled=" + capsule.enabled + " trigger=" + capsule.isTrigger + " scale=" + capsule.transform.lossyScale);
        foreach (var c in UnityEngine.Object.FindObjectsOfType<Collider>()) c.enabled = false;
        try { Exercise(); }
        catch (Exception e) { report.Add("FAIL " + e); }
        report.Add("Checks=" + checks);
        File.WriteAllLines(Path.Combine(Output, "report.txt"), report.Select(line => line.TrimEnd()));
        Application.Quit();
    }

    GameObject Node(string name)
    { var go = new GameObject("Verifier." + name); fixtures.Add(go); return go; }
    GameObject Box(string name, Vector3 position, Vector3 size)
    { var go = Node(name); go.transform.position = position; go.AddComponent<BoxCollider>().size = size; return go; }
    PlayMakerFSM Fsm(string name, string state)
    {
        var fsm = player.AddComponent<PlayMakerFSM>();
        var data = new Fsm { Name = name, StartState = state };
        data.Variables.FloatVariables = new[] { new FsmFloat("standingHeight") { Value = 1.7f }, new FsmFloat("Health") { Value = 100 },
            new FsmFloat("AxisHorizontal"), new FsmFloat("AxisVertical") };
        data.States = new[] { new FsmState(data) { Name = state, Actions = new FsmStateAction[0] } };
        fsm.Fsm = data;
        fsm.enabled = false; fsm.enabled = true;
        if (player.activeInHierarchy) fsm.Fsm.SetState(state);
        return fsm;
    }
    void Fixture(float height)
    {
        Time.timeScale = 1;
        Call(CT, plugin, "Reset");
        if (playerBody != null) { Call(PT("Body"), playerBody, "Destroy"); playerBody = null; }
        Set(PT("Runner"), null, "_body", null);
        Call(PT("Game"), null, "Reset");
        foreach (var go in fixtures) if (go != null) UnityEngine.Object.DestroyImmediate(go);
        fixtures.Clear();
        player = Node("Player"); player.SetActive(false);
        player.transform.position = new Vector3(10000, .86f, 10000);
        rb = player.AddComponent<Rigidbody>(); rb.useGravity = false; rb.constraints = RigidbodyConstraints.FreezeRotation;
        var capsule = player.AddComponent<CapsuleCollider>(); capsule.height = 1.7f; capsule.radius = .17f;
        var secondary = player.AddComponent<CapsuleCollider>(); secondary.height = 1.55f; secondary.radius = .2f; secondary.center = new Vector3(0, .07f, 0);
        movement = Fsm("Movement", "Standing"); jump = Fsm("Jump", "Idle"); car = Fsm("InCar", "OnFoot");
        var health = Fsm("Health", "playerHealth");
        player.SetActive(true);
        foreach (var fsm in player.GetComponents<PlayMakerFSM>()) fsm.Fsm.SetState(fsm.Fsm.StartState);
        var camera = Node("Camera"); camera.transform.position = player.transform.position + Vector3.up * .7f;
        var lens = camera.AddComponent<Camera>();
        Set(PT("Game"), null, "Player", player); Set(PT("Game"), null, "InCarFsm", car);
        Set(PT("Game"), null, "MovementFsm", movement); Set(PT("Game"), null, "PlayerCamera", camera.transform);
        Set(PT("Game"), null, "Cam", lens);
        Set(PT("Game"), null, "_healthFsm", health);
        Box("Floor", new Vector3(10000, -.1f, 10000), new Vector3(10, .2f, 10));
        ledge = Box("Ledge", new Vector3(10000, height * .5f, 10001.05f), new Vector3(2, height, 1));
        Physics.SyncTransforms();
        geometry = Activator.CreateInstance(ClimbAssembly.GetType("Apocaplayer.ClimbGeometry"), new object[] { rb });
    }
    bool Find(out object target)
    {
        var args = new object[] { Vector3.forward, .45f, 2.5f, .85f, null };
        bool result = (bool)Call(geometry.GetType(), geometry, "Find", args);
        target = args[4];
        report.Add("DETECT " + result + " " + geometry.GetType().GetProperty("Rejection").GetValue(geometry));
        return result;
    }
    void BuildBody()
    {
        playerBody = Call(PT("Body"), null, "Create");
        Check(playerBody != null, "Actual Apocaplayer character created from the game's Mixamo avatar");
        Set(PT("Runner"), null, "_body", playerBody);
        report.Add("API ready=" + PT("Game").GetProperty("Ready").GetValue(null) + " dead=" + PT("Game").GetProperty("Dead").GetValue(null)
            + " paused=" + PT("Game").GetProperty("Paused").GetValue(null) + " inCar=" + PT("Game").GetProperty("InCar").GetValue(null)
            + " height=" + PT("Game").GetProperty("StandingHeight").GetValue(null));
        Check(ModAPI.PlayerCanTraverse, "Player traversal API ready");
    }
    void Begin(object target)
    {
        var t = CT;
        Set(t, plugin, "body", rb); Set(t, plugin, "geometry", geometry); Set(t, plugin, "target", target);
        Call(t, plugin, "Begin");
        Check((bool)Get(t, plugin, "climbing"), "Climb controller started");
    }
    void Animate()
    {
        var view = Enum.Parse(PT("Body").GetNestedType("View", Flags), "ThirdPerson");
        Call(PT("Body"), playerBody, "LateFoot", view, .02f);
        var bones = (Dictionary<string, Transform>)Get(PT("Body"), playerBody, "Bones");
        var visualRoot = ((GameObject)Get(PT("Body"), playerBody, "Root")).transform;
        var hipsAtRoot = visualRoot.InverseTransformPoint(bones["mixamorig:Hips"].position);
        if (ModAPI.PlayerTraversalActive && new Vector2(hipsAtRoot.x, hipsAtRoot.z).magnitude > .5f)
            throw new Exception("Visible climb pose separated from player origin: " + hipsAtRoot.ToString("F4"));
        if (poseSamples++ % 20 == 0 && poseSamples < 301)
        {
            var root = ((GameObject)Get(PT("Body"), playerBody, "Root")).transform;
            report.Add("POSE seconds=" + Get(CT, plugin, "elapsed")
                + " hips=" + root.InverseTransformPoint(bones["mixamorig:Hips"].position).ToString("F4")
                + " anim=" + ((Transform)Get(PT("Body"), playerBody, "_anim")).localPosition.ToString("F4"));
        }
        foreach (var bone in bones.Values)
            if (float.IsNaN(bone.position.x) || float.IsInfinity(bone.position.y)) throw new Exception("Non-finite retargeted pose");
    }
    void Exercise()
    {
        var defaults = UnityEngine.Resources.Load<InsaneSystems.InputManager.InputStorage>("InputStorage");
        foreach (var key in defaults.Keys) report.Add("GAME ACTION " + key.Name + " = " + key.Key + " / " + key.AlternativeKey);
        Check(Get(CT, plugin, "waist") is AnimationClip, "Runtime loaded waist clip from separate bundle");
        Check(Get(CT, plugin, "high") is AnimationClip, "Runtime loaded high clip from separate bundle");
        object target;
        Fixture(1f); Check(Find(out target), "Waist-height collider detected");
        BuildBody(); jump.enabled = false;
        rb.useGravity = true; rb.interpolation = RigidbodyInterpolation.Interpolate;
        Begin(target); Check(rb.isKinematic && !rb.useGravity && !movement.enabled && !car.enabled, "Climbing suspends physics and competing movement FSMs");
        var runner = Resources.FindObjectsOfTypeAll<MonoBehaviour>().First(r => r != null && r.GetType() == PT("Runner"));
        Call(PT("Runner"), runner, "LateUpdate");
        Check(((SkinnedMeshRenderer)Get(PT("Body"), playerBody, "_fpArms")).enabled,
            "First-person view displays the character's own climbing hands");
        for (int i = 0; i < 10; i++) { Call(CT, plugin, "FixedTick"); Physics.SyncTransforms(); Animate(); }
        CheckClimbCamera();
        Check(!jump.enabled, "Previously disabled jump remains disabled");
        var end = (Vector3)Get(target.GetType(), target, "End");
        for (int i = 0; i < 120 && (bool)Get(CT, plugin, "climbing"); i++)
        { Call(CT, plugin, "FixedTick"); Physics.SyncTransforms(); Animate(); }
        Check(Vector3.Distance(rb.position, end) < .001f, "Waist climb places actual player capsule on the top");
        Check((float)Get(CT, plugin, "elapsed") <= 1.1f,
            "Waist climb returns movement within 1.1 seconds at the new 1.5x speed");
        Check(true, "Waist climb's visible body stays within 50 cm of its origin throughout the clip");
        Check(!rb.isKinematic && rb.useGravity && rb.interpolation == RigidbodyInterpolation.Interpolate && movement.enabled && car.enabled && !jump.enabled,
            "Completion restores original physics and FSM enabled states");
        Check(!ModAPI.PlayerTraversalActive, "Completion releases animation lease");
        var camera = (Camera)Get(PT("Game"), null, "Cam");
        Call(PT("ThirdPerson"), null, "Tick");
        Call(PT("ThirdPerson"), null, "PreCullInner", camera);
        Check(Vector3.Distance(camera.worldToCameraMatrix.inverse.MultiplyPoint3x4(Vector3.zero), camera.transform.position) < .01f,
            "Completion restores the ordinary first-person camera");
        Call(PT("Runner"), runner, "LateUpdate");
        Check(!((SkinnedMeshRenderer)Get(PT("Body"), playerBody, "_fpArms")).enabled,
            "Completion restores the normal first-person arms preference");

        Fixture(2.3f); Check(Find(out target), "High ledge detected"); BuildBody(); Begin(target);
        for (int i = 0; i < 180 && (bool)Get(CT, plugin, "climbing"); i++)
        { Call(CT, plugin, "FixedTick"); Physics.SyncTransforms(); Animate(); }
        end = (Vector3)Get(target.GetType(), target, "End");
        Check(Vector3.Distance(rb.position, end) < .001f, "High climb completes with retargeted Mixamo poses");
        Check((float)Get(CT, plugin, "elapsed") <= 1.85f,
            "High climb returns movement within 1.85 seconds without the standing tail");
        Check(true, "High climb's visible body stays within 50 cm of its origin throughout the clip");

        Fixture(2.8f); Check(!Find(out target), "Out-of-reach high ledge rejected");
        Fixture(1.5f); ledge.transform.position = new Vector3(10000, 1.2f, 10001.05f);
        ledge.GetComponent<BoxCollider>().size = new Vector3(2, .6f, 1); Physics.SyncTransforms();
        Check(Find(out target), "Forward probes detect raised car bodywork above the lowest ray");
        Fixture(1f); ledge.transform.position += Vector3.forward * .45f; Physics.SyncTransforms();
        Check(Find(out target), "Forward check includes a ledge slightly beyond the old probe reach");
        Fixture(1f); ledge.GetComponent<Collider>().isTrigger = true; Physics.SyncTransforms(); Check(!Find(out target), "Trigger obstacle rejected");
        Fixture(1f); Box("Ceiling", new Vector3(10000, 2.2f, 10000.5f), new Vector3(3, .1f, 3)); Physics.SyncTransforms(); Check(!Find(out target), "Low ceiling rejects climb");
        Fixture(1f); var moving = ledge.AddComponent<Rigidbody>(); moving.useGravity = false; moving.velocity = Vector3.right * 2; Physics.SyncTransforms(); Check(!Find(out target), "Fast moving car rejected");
        Fixture(1f); player.transform.position += Vector3.up * .5f; Physics.SyncTransforms(); Check(!Find(out target), "Airborne player rejected");
        Fixture(1f); ledge.GetComponent<BoxCollider>().size = new Vector3(2, 1, .08f); ledge.transform.position = new Vector3(10000, .5f, 10000.6f); Physics.SyncTransforms(); Check(!Find(out target), "Thin fence without landing rejected");

        Fixture(1f); Check(Find(out target), "Cancellation fixture detected"); BuildBody(); Begin(target);
        for (int i = 0; i < 15; i++) { Call(CT, plugin, "FixedTick"); Physics.SyncTransforms(); Animate(); }
        var safe = rb.position;
        report.Add("BLOCKER rb=" + rb.position + " transform=" + player.transform.position);
        Box("NewBlocker", rb.position + Vector3.up * .9f, new Vector3(1, .1f, 1)); Physics.SyncTransforms();
        for (int i = 0; i < 20 && (bool)Get(CT, plugin, "climbing"); i++)
        { Call(CT, plugin, "FixedTick"); Physics.SyncTransforms(); }
        Check(!(bool)Get(CT, plugin, "climbing") && !rb.isKinematic && movement.enabled && car.enabled && !ModAPI.PlayerTraversalActive,
            "Dynamic obstruction cancels climb and restores control");
        Check(Vector3.Distance(rb.position, (Vector3)Get(CT, plugin, "lastSafe")) < .001f
            && rb.position.y <= safe.y + .04f, "Cancellation keeps last checked safe position");

        Fixture(1f); Check(Find(out target), "Moving-support fixture detected"); BuildBody(); Begin(target);
        ledge.transform.position += Vector3.right * .2f; Physics.SyncTransforms();
        Call(CT, plugin, "Tick");
        Check(!(bool)Get(CT, plugin, "climbing") && movement.enabled && !ModAPI.PlayerTraversalActive, "Support transform movement safely cancels climb");

        Fixture(1f); Check(Find(out target), "Pause fixture detected"); BuildBody(); Begin(target);
        Time.timeScale = 0; Call(CT, plugin, "Tick"); Animate();
        Check(ModAPI.PlayerTraversalActive && rb.isKinematic, "Pause preserves traversal lease");
        Time.timeScale = 1; Call(CT, plugin, "Finish", false, "Test cleanup");
        Check(!ModAPI.PlayerTraversalActive && movement.enabled, "Cleanup releases animation and restores movement");
        CheckWeaponStow();
        CheckSettings();
        CheckJumpControls();
        CheckVehicleBindings();
    }

    void CheckClimbCamera()
    {
        var camera = (Camera)Get(PT("Game"), null, "Cam");
        Vector3 nativePosition = camera.transform.position;
        Quaternion nativeRotation = camera.transform.rotation;
        Call(PT("ThirdPerson"), null, "Tick");
        Call(PT("ThirdPerson"), null, "PreCullInner", camera);
        var bones = (Dictionary<string, Transform>)Get(PT("Body"), playerBody, "Bones");
        var eye = camera.worldToCameraMatrix.inverse.MultiplyPoint3x4(Vector3.zero);
        Check(Vector3.Distance(eye, bones["mixamorig:Head"].position + new Vector3(0, .075f, .06f)) < .01f,
            "First-person view follows the animated head during the climb");
        Check(camera.transform.position == nativePosition && Quaternion.Angle(camera.transform.rotation, nativeRotation) < .001f,
            "Head-follow keeps mouse look and the native camera transform unchanged");
        Call(PT("ThirdPerson"), null, "EndOfFrame");
        Check(Vector3.Distance(camera.worldToCameraMatrix.inverse.MultiplyPoint3x4(Vector3.zero), nativePosition) < .01f,
            "Head-follow camera matrix resets after rendering");
    }

    GameObject Child(string name, Transform parent)
    {
        var go = new GameObject(name); fixtures.Add(go);
        go.transform.SetParent(parent, false); return go;
    }

    void CheckWeaponStow()
    {
        object target;
        Fixture(1f); Check(Find(out target), "Armed climb fixture detected"); BuildBody();
        var camera = (Transform)Get(PT("Game"), null, "PlayerCamera");
        var use = Child("HandItemUse", camera);
        var weapons = Child("Weapons", use.transform); weapons.SetActive(false);
        var selected = weapons.AddComponent<PlayMakerFSM>();
        var data = new Fsm { Name = "Weapons", StartState = "Slot 1" };
        data.States = new[] { new FsmState(data) { Name = "Slot 1", Actions = new FsmStateAction[0] } };
        selected.Fsm = data; weapons.SetActive(true); selected.Fsm.SetState("Slot 1");
        var slot = Child("Slot 1", weapons.transform);
        var item = GameObject.CreatePrimitive(PrimitiveType.Cube); fixtures.Add(item); item.name = "akms";
        item.transform.SetParent(slot.transform, false); item.transform.localScale = new Vector3(.1f, .7f, .12f);
        item.GetComponent<Collider>().enabled = false; item.GetComponent<Renderer>().enabled = false;
        var arms = Child("WeaponsArm", camera); var parent = Child("Parent", arms.transform);
        var handModel = GameObject.CreatePrimitive(PrimitiveType.Cube); fixtures.Add(handModel); handModel.name = "akms";
        handModel.transform.SetParent(parent.transform, false); handModel.GetComponent<Collider>().enabled = false;
        var handRenderer = handModel.GetComponent<Renderer>();
        Set(PT("Game"), null, "WeaponsParent", parent.transform);
        Call(PT("Body"), playerBody, "SetVisible", true, false);
        Call(PT("Body"), playerBody, "LateEquipment");
        var equipment = Get(PT("Body"), playerBody, "_equipment");
        var mounts = (Array)Get(PT("Equipment"), equipment, "_mounts");
        var mount = mounts.GetValue(1);
        var renderers = (Renderer[])Get(mount.GetType(), mount, "Renderers");
        Check(renderers.Length > 0 && renderers.All(r => !r.enabled), "Drawn rifle is absent from the back before climbing");
        Begin(target); Animate(); Call(PT("Body"), playerBody, "LateEquipment");
        Check(renderers.All(r => r.enabled) && !handRenderer.enabled,
            "Climbing puts the drawn rifle on its back mount and hides the hand model");
        Check(selected.ActiveStateName == "Slot 1" && item.transform.parent == slot.transform,
            "Temporary stowing preserves the selected inventory slot and original item");
        for (int i = 0; i < 100 && (bool)Get(CT, plugin, "climbing"); i++)
        { Call(CT, plugin, "FixedTick"); Physics.SyncTransforms(); Animate(); }
        Call(PT("Body"), playerBody, "LateEquipment");
        Check(renderers.All(r => !r.enabled) && handRenderer.enabled,
            "Climb completion restores the weapon to the hands and removes its back copy");
        Begin(target); Animate(); Call(PT("Body"), playerBody, "LateEquipment");
        Call(CT, plugin, "Finish", false, "Weapon cancellation check");
        Call(PT("Body"), playerBody, "LateEquipment");
        Check(renderers.All(r => !r.enabled) && handRenderer.enabled,
            "Climb cancellation also restores the weapon");
    }

    void CheckSettings()
    {
        var cfg = BepInEx.Bootstrap.Chainloader.PluginInfos[Apocaplayer.Plugin.GUID].Instance.Config;
        var sections = cfg.Keys.Select(k => k.Section).Distinct().ToArray();
        Check(sections.Length >= 2 && sections[sections.Length - 2] == "climbing" && sections[sections.Length - 1] == "Debug",
            "Apocasetter entry order puts climbing second-last and Debug last");
        var keys = cfg.Keys.Where(k => k.Section == "climbing").Select(k => k.Key).ToArray();
        int jumpIndex = Array.IndexOf(keys, "Jump triggers climbing");
        Check(jumpIndex >= 0 && keys[jumpIndex + 1] == "alternative climb key", "Alternative climb key appears directly below the jump toggle");
        Check(((ConfigEntry<bool>)Get(CT, plugin, "jumpTriggers")).Value && ((ConfigEntry<KeyCode>)Get(CT, plugin, "climbKey")).Value == KeyCode.None,
            "Jump climbing defaults on and alternative key defaults to None");
        cfg.Save();
        var savedSections = System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(cfg.ConfigFilePath), @"(?m)^\[([^\]\r\n]+)\]")
            .Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value).ToArray();
        Check(savedSections[savedSections.Length - 2] == "climbing" && savedSections[savedSections.Length - 1] == "Debug",
            "Saved config also places climbing before the final Debug section");
        Check(!cfg.ContainsKey(new ConfigDefinition("VEHICLE", "HeadlightsKey")), "Native headlights binding removes the duplicate mod setting");
        Check(((ConfigEntry<KeyCode>)Get(PT("Plugin"), null, "IgnitionKey")).Value == KeyCode.R,
            "Custom ignition fallback survives the settings migration");
    }

    HutongGames.PlayMaker.Actions.GetButtonDown Button(string name, PlayMakerFSM fsm)
    {
        var action = new HutongGames.PlayMaker.Actions.GetButtonDown { buttonName = new FsmString(name) };
        action.Init(fsm.Fsm.States[0]);
        action.buttonName = name;
        return action;
    }
    bool NativeJump()
    {
        var button = Button("Jump", jump);
        report.Add("JUMP button=" + button.buttonName.Value + " toggle=" + ((ConfigEntry<bool>)Get(CT, plugin, "jumpTriggers")).Value
            + " name=" + button.Fsm.Name + " owner=" + button.Fsm.GameObjectName + " player=" + player.name
            + " input=" + Call(PT("GameBindings"), null, "Down", "Jump", KeyCode.Space)
            + " cursor=" + Cursor.visible + " forward=" + movement.FsmVariables.FindFsmFloat("AxisVertical").Value);
        bool result = (bool)Call(PT("ThirdPerson"), null, "BeforeGetButtonDown", button);
        report.Add("JUMP result=" + result + " active=" + ModAPI.PlayerTraversalActive + " attempt=" + Get(CT, plugin, "jumpAttemptFrame")
            + " consumed=" + Get(CT, plugin, "jumpConsumed"));
        return result;
    }
    void ReadyInputFixture(float height, bool pushing)
    {
        Fixture(height); BuildBody(); Cursor.visible = false;
        movement.FsmVariables.FindFsmFloat("AxisVertical").Value = pushing ? 2 : 0;
    }
    void CheckJumpControls()
    {
        var toggle = (ConfigEntry<bool>)Get(CT, plugin, "jumpTriggers");
        var alternative = (ConfigEntry<KeyCode>)Get(CT, plugin, "climbKey");
        var enabled = (ConfigEntry<bool>)Get(CT, plugin, "enabledMod");
        var action = InsaneSystems.InputManager.InputStorage.Singleton.GetKeyByName("Jump");
        var original = action.Key;
        action.UpdateKey(KeyCode.Space, false);
        Probe.InjectKeys = true; Probe.Keys.Clear(); Probe.Keys.Add(KeyCode.Space);
        toggle.Value = true; alternative.Value = KeyCode.None;
        foreach (float gap in new[] { .21f, .20f, .195f, .19f, .18f })
        {
            ReadyInputFixture(1, true);
            player.transform.position = new Vector3(10000, .86f, 10000.55f - gap); Physics.SyncTransforms();
            bool normalJump = NativeJump();
            report.Add("CONTACT GAP=" + gap + " reject=" + Get(CT, plugin, "geometry")?.GetType().GetProperty("Rejection").GetValue(Get(CT, plugin, "geometry")));
            Check(!normalJump && ModAPI.PlayerTraversalActive, "Climbing succeeds under wall contact pressure at gap " + gap);
            var finish = (Vector3)Get(Get(CT, plugin, "target").GetType(), Get(CT, plugin, "target"), "End");
            for (int i = 0; i < 100 && (bool)Get(CT, plugin, "climbing"); i++)
            { Call(CT, plugin, "FixedTick"); Physics.SyncTransforms(); }
            Check(!ModAPI.PlayerTraversalActive && Vector3.Distance(rb.position, finish) < .005f,
                "Full climb escapes wall contact and reaches the roof at gap " + gap);
        }
        ReadyInputFixture(1, true);
        player.transform.position = new Vector3(10000, .86f, 10000.37f); Physics.SyncTransforms();
        var contactGeometry = Activator.CreateInstance(ClimbAssembly.GetType("Apocaplayer.ClimbGeometry"), new object[] { rb });
        Check(!(bool)Call(contactGeometry.GetType(), contactGeometry, "Clear", rb.position, rb.position + Vector3.forward * .03f),
            "Contact tolerance does not allow moving deeper into the wall");
        ReadyInputFixture(1, true);
        player.transform.position = new Vector3(10000, .86f, 10000.42f); Physics.SyncTransforms();
        Check(NativeJump() && !ModAPI.PlayerTraversalActive, "Deep wall penetration still rejects climbing");
        ReadyInputFixture(1, true);
        player.transform.position = new Vector3(10000, .86f, 10000.37f);
        Box("TouchingCeiling", new Vector3(10000, 1.75f, 10000.4f), new Vector3(3, .12f, 3)); Physics.SyncTransforms();
        Check(NativeJump() && !ModAPI.PlayerTraversalActive, "Wall contact tolerance still rejects a blocked ceiling");
        ReadyInputFixture(1, true);
        Check(!NativeJump() && ModAPI.PlayerTraversalActive && !jump.enabled, "Space pushing into a reachable wall starts climbing and consumes native Jump");
        ReadyInputFixture(1, true);
        player.transform.position = new Vector3(10000, .86f, 10000.345f); Physics.SyncTransforms();
        Check(!NativeJump() && ModAPI.PlayerTraversalActive, "Space climbs while the capsules are flush against the wall");
        ReadyInputFixture(1, false);
        Check(NativeJump(), "An early probe without forward input leaves Jump available");
        movement.FsmVariables.FindFsmFloat("AxisVertical").Value = 2;
        Check(!NativeJump() && ModAPI.PlayerTraversalActive, "Jump FSM retries after movement updates within the same frame");
        ReadyInputFixture(.2f, true);
        Check(NativeJump() && !ModAPI.PlayerTraversalActive && jump.enabled, "Space keeps the normal jump when no reachable ledge exists");
        ReadyInputFixture(1, false);
        Check(NativeJump() && !ModAPI.PlayerTraversalActive, "Space near a ledge without pushing forward still jumps normally");
        toggle.Value = false;
        ReadyInputFixture(1, true);
        Check(NativeJump() && !ModAPI.PlayerTraversalActive, "Turning off Jump triggers climbing restores ordinary Space jumps at walls");
        Probe.Keys.Clear(); Probe.Keys.Add(KeyCode.G); alternative.Value = KeyCode.G;
        ReadyInputFixture(1, false); Call(CT, plugin, "Tick");
        Check(ModAPI.PlayerTraversalActive, "A configured alternative key climbs independently of the disabled jump toggle");
        alternative.Value = KeyCode.None; toggle.Value = true;
        action.UpdateKey(KeyCode.J, false); Probe.Keys.Clear(); Probe.Keys.Add(KeyCode.J);
        ReadyInputFixture(1, true);
        Check(!NativeJump() && ModAPI.PlayerTraversalActive, "Jump climbing follows a game Controls rebind to J");
        action.UpdateKey(KeyCode.Space, false); Probe.Keys.Clear(); Probe.Keys.Add(KeyCode.Space);
        enabled.Value = false; ReadyInputFixture(1, true);
        Check(NativeJump() && !ModAPI.PlayerTraversalActive, "Disabling climbing leaves the native jump untouched");
        enabled.Value = true; action.UpdateKey(original, false);
        Probe.InjectKeys = false; Probe.Keys.Clear();
        Call(CT, plugin, "Reset");
    }

    void CheckVehicleBindings()
    {
        Probe.InjectKeys = true; Probe.Keys.Clear();
        var binding = PT("GameBindings");
        var storage = InsaneSystems.InputManager.InputStorage.Singleton;
        var light = storage.GetKeyByName("Headlight");
        var old = light.Key; var oldAlt = light.AlternativeKey;
        light.UpdateKey(KeyCode.F10, false); light.UpdateKey(KeyCode.F11, true);
        Check((string)Call(binding, null, "Label", "Headlight", KeyCode.X) == "F10 / F11", "Headlight hint displays both current game bindings");
        Probe.Keys.Add(KeyCode.F11);
        Check((bool)Call(PT("Car"), null, "PressedAction", "Headlight", KeyCode.X), "Alternative game headlight binding triggers the mod's light control");
        Probe.Keys.Clear(); Probe.Keys.Add(KeyCode.X);
        Check(!(bool)Call(PT("Car"), null, "PressedAction", "Headlight", KeyCode.X), "Legacy X light binding is ignored when the game has its own action");
        ReadyInputFixture(1, true);
        car.Fsm.States = new[] { car.Fsm.States[0], new FsmState(car.Fsm) { Name = "InCar", Actions = new FsmStateAction[0] } };
        car.Fsm.SetState("InCar");
        var nativeLights = Fsm("INPUT_Headlight", "on");
        Check(!(bool)Call(PT("ThirdPerson"), null, "BeforeGetButtonDown", Button("Headlight", nativeLights)), "Native and mod light handlers cannot toggle headlights twice");
        var brake = Fsm("Handbrake", "HandbrakeOn");
        var lights = Fsm("LightOn", "on");
        Set(PT("Car"), null, "_handbrake", brake); Set(PT("Car"), null, "_lightOn", lights);
        Call(PT("Car"), null, "UpdateHints");
        var hints = (string)Get(PT("Car"), null, "_hint");
        report.Add("VEHICLE HINTS " + hints.Replace('\n', '|'));
        Check(hints.Contains("F10 / F11 - Headlights") && hints.Contains("Space - Handbrake Release"), "Vehicle hints use the game's light and handbrake keys");
        var inputBrake = Fsm("INPUT_Handbrake", "on");
        Check((bool)Call(PT("ThirdPerson"), null, "BeforeGetButtonDown", Button("Handbrake", inputBrake)), "Native handbrake input remains enabled");
        light.UpdateKey(old, false); light.UpdateKey(oldAlt, true);
        Probe.InjectKeys = false; Probe.Keys.Clear();
    }
}
