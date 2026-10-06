using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using HutongGames.PlayMaker;
using UnityEngine;
using UnityEngine.SceneManagement;

[BepInPlugin("apocaplayer.native-verifier", "Apocaplayer Native Verifier", "1.0.0")]
[BepInDependency(Apocaplayer.Plugin.GUID)]
public class Probe : BaseUnityPlugin
{
    static bool Running;
    public static KeyCode TestKey;
    public static bool KeyDown(ref bool __result, KeyCode key) { __result = key != KeyCode.None && key == TestKey; return false; }
    void Awake()
    {
        Logger.LogInfo("Verifier args: " + string.Join(" | ", Environment.GetCommandLineArgs()));
        var arg = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("-apocaplayer-report="));
        if (arg == null) return;
        Running = true;
        Application.runInBackground = true;
        var h = new Harmony("apocaplayer.native-verifier"); h.PatchAll(typeof(Probe).Assembly);
        h.Patch(typeof(Apocaplayer.Plugin).Assembly.GetType("Apocaplayer.Car").GetMethod("Pressed", BindingFlags.NonPublic | BindingFlags.Static), prefix: new HarmonyMethod(typeof(Probe), nameof(KeyDown)));
        var go = new GameObject("Verifier.Host") { hideFlags = HideFlags.HideAndDontSave }; DontDestroyOnLoad(go);
        var runner = go.AddComponent<ProbeRunner>();
        runner.Output = arg.Substring(arg.IndexOf('=') + 1);
        runner.StartCoroutine(runner.Run());
    }
    [HarmonyPatch(typeof(PlayMakerFSM), "OnEnable")]
    static class NoSavesOrWorld
    {
        static bool Prefix(PlayMakerFSM __instance)
        {
            if (!Running) return true;
            bool test = false;
            for (var t = __instance.transform; t != null; t = t.parent) if (t.name.StartsWith("Verifier.")) test = true;
            return test && new[] { "Health", "InCar", "Start", "OnOff", "Music", "Play", "Stop", "Volume", "LightOn", "LightOff", "Handbrake" }.Contains(__instance.FsmName);
        }
    }
}

public class ProbeRunner : MonoBehaviour
{
    public string Output;
    static Assembly Mod = typeof(Apocaplayer.Plugin).Assembly;
    const BindingFlags Flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    List<string> report = new List<string>();
    int checks;
    bool draw;
    void OnDestroy() { if (!string.IsNullOrEmpty(Output)) File.AppendAllText(Path.Combine(Output, "progress.txt"), "Runner destroyed\n"); }
    static Type T(string type) => Mod.GetType("Apocaplayer." + type);
    static object Call(string type, string method, params object[] args) => T(type).GetMethod(method, Flags).Invoke(null, args);
    static object Get(string type, string field) => T(type).GetField(field, Flags).GetValue(null);
    static void Set(string type, string field, object value) => T(type).GetField(field, Flags).SetValue(null, value);
    static object Invoke(object o, string method, params object[] args) => o.GetType().GetMethod(method, Flags).Invoke(o, args);
    void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; report.Add("PASS " + label); }
    static GameObject Node(string name, Transform parent = null) { var go = new GameObject(name); if (parent != null) go.transform.SetParent(parent, false); return go; }
    static PlayMakerFSM Simple(GameObject go, string name, string initial, params string[] states)
    {
        var f = go.AddComponent<PlayMakerFSM>();
        var data = new Fsm { Name = name, StartState = initial };
        if (name == "Health") data.Variables.FloatVariables = new[] { new FsmFloat("Health") { Value = 100f } };
        data.States = states.Select(n => new FsmState(data) { Name = n, Actions = new FsmStateAction[0] }).ToArray();
        f.Fsm = data;
        return f;
    }
    void DisableRunner()
    {
        foreach (var r in Resources.FindObjectsOfTypeAll<MonoBehaviour>()) if (r != null && r.GetType() == T("Runner")) r.enabled = false;
    }
    public IEnumerator Run()
    {
        Directory.CreateDirectory(Output); Application.runInBackground = true;
        File.WriteAllText(Path.Combine(Output, "progress.txt"), "Runner started\n");
        yield return new WaitForSecondsRealtime(1f);
        DisableRunner();
        File.AppendAllText(Path.Combine(Output, "progress.txt"), "Loading game scene\n");
        yield return SceneManager.LoadSceneAsync(1);
        File.AppendAllText(Path.Combine(Output, "progress.txt"), "Game scene loaded\n");
        DisableRunner();
        yield return new WaitForSecondsRealtime(1f);
        var exercise = Exercise();
        while (true)
        {
            bool more;
            try { more = exercise.MoveNext(); }
            catch (Exception e) { report.Add("FAIL " + e); break; }
            if (!more) break;
            yield return exercise.Current;
        }
        report.Add("Checks=" + checks);
        File.WriteAllLines(Path.Combine(Output, "report.txt"), report);
        Application.Quit();
    }
    IEnumerator Exercise()
    {
        Time.timeScale = 1f;
        Call("Game", "Reset");
        var cfg = ((BaseUnityPlugin)BepInEx.Bootstrap.Chainloader.PluginInfos[Apocaplayer.Plugin.GUID].Instance).Config;
        var vehicleEntries = cfg.Keys.Where(k => k.Section == "VEHICLE").ToArray();
        Check(vehicleEntries.Length == 7 && vehicleEntries[0].Key == "VehicleHotkeyHint", "VEHICLE contains both toggles before five bindings");
        Check(!cfg.Keys.Any(k => k.Section == "General" && k.Key == "IgnitionKey"), "legacy ignition entry removed");
        Check(((ConfigEntry<KeyCode>)Get("Plugin", "IgnitionKey")).Value == KeyCode.R, "legacy ignition rebind migrated");
        var car = Node("Verifier.Car");
        var drive = Node("DriveTrigger", car.transform);
        var player = Node("Verifier.Player", car.transform); player.SetActive(false);
        player.transform.localPosition = new Vector3(0, 2, 0);
        player.AddComponent<Rigidbody>().isKinematic = true;
        player.AddComponent<CapsuleCollider>();
        var inCar = Simple(player, "InCar", "InCar", "InCar", "OnFoot");
        var health = Simple(player, "Health", "playerHealth", "playerHealth", "playerDeath", "backToMenu");
        player.SetActive(true);
        health.FsmVariables.FloatVariables = new[] { new FsmFloat("Health") { Value = 100f } };
        health.FsmVariables.Init();
        health.Fsm.SetState("playerHealth"); inCar.Fsm.SetState("InCar");
        health.FsmVariables.GetFsmFloat("Health").Value = 100f;
        var camGo = Node("Verifier.Camera"); camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>(); camGo.transform.position = new Vector3(0,2,-5);
        Set("Game", "Player", player); Set("Game", "InCarFsm", inCar);
        Set("Game", "PlayerCamera", camGo.transform); Set("Game", "Cam", cam);
        report.Add("Health setup: " + health.FsmName + " initialized=" + health.Fsm.Initialized + " state=" + health.ActiveStateName + " health=" + health.FsmVariables.GetFsmFloat("Health").Value);
        report.Add("Player FSMs: " + string.Join(",", player.GetComponents<PlayMakerFSM>().Select(f => f.FsmName + "/" + f.ActiveStateName)));
        Set("Game", "_healthFsm", null);
        Check(!(bool)T("Game").GetProperty("Dead").GetValue(null), "alive health detected");
        health.Fsm.Variables.GetFsmFloat("Health").Value = 0.2f;
        Check((bool)T("Game").GetProperty("Dead").GetValue(null), "game's sub-0.4 death threshold detected");
        health.Fsm.Variables.GetFsmFloat("Health").Value = 100f;
        health.Fsm.SetState("playerDeath");
        Check((bool)T("Game").GetProperty("Dead").GetValue(null), "latched playerDeath detected");
        health.Fsm.SetState("playerHealth");
        health.FsmVariables.GetFsmFloat("Health").Value = 100f;

        var switchPrefab = Resources.FindObjectsOfTypeAll<GameObject>().First(g => g.name == "switch_lights" && !g.scene.IsValid());
        var panel = Node("switch_panel", car.transform);
        var lights = Instantiate(switchPrefab, panel.transform); lights.name = "switch_lights";
        var radioPrefab = Resources.FindObjectsOfTypeAll<GameObject>().First(g => g.name == "radio_car" && !g.scene.IsValid());
        var hinge = Node("hinge_radio", car.transform);
        var radio = Instantiate(radioPrefab, hinge.transform); radio.name = "radio_car";
        // Match an inserted cassette: the game normally activates these controls on attachment.
        radio.transform.Find("PlayStop").gameObject.SetActive(true);
        radio.transform.Find("Volume").gameObject.SetActive(true);
        var audio = radio.GetComponent<AudioSource>();
        audio.clip = AudioClip.Create("Verifier.Cassette", 441000, 1, 44100, false); audio.loop = true; audio.Play(); audio.volume = 0;
        var startGo = Node("START", car.transform); startGo.SetActive(false);
        var start = Simple(startGo, "Start", "off", "off", "Ignition", "off2", "Start", "off3", "Stop"); startGo.SetActive(true);
        Call("Car", "Tick");
        Check(Get("Car", "_play") != null && Get("Car", "_volume") != null, "native cassette controls discovered");
        var lightOff = (PlayMakerFSM)Get("Car", "_lightOff"); var lightOn = (PlayMakerFSM)Get("Car", "_lightOn");
        Check(lightOff.enabled && !lightOn.enabled, "native headlights initially off");
        Probe.TestKey = KeyCode.X; Call("Car", "Tick"); Probe.TestKey = KeyCode.None;
        yield return null;
        Check(lightOn.enabled && !lightOff.enabled, "X invokes native headlight-on switch and enables off action");
        Probe.TestKey = KeyCode.X; Call("Car", "Tick"); Probe.TestKey = KeyCode.None;
        yield return null;
        Check(lightOff.enabled && !lightOn.enabled, "X invokes native headlight-off switch");
        // Native Music initialization may clear an empty tape slot on its first frame.
        audio.clip = AudioClip.Create("Verifier.LoadedTape", 441000, 1, 44100, false); audio.Play(); audio.volume = 0;
        ((PlayMakerFSM)Get("Car", "_radioOnOff")).SendEvent("Deactivate");
        var playControl = (PlayMakerFSM)Get("Car", "_play");
        playControl.gameObject.SetActive(true);
        ((PlayMakerFSM)Get("Car", "_volume")).gameObject.SetActive(true);
        playControl.Fsm.Start();
        ((PlayMakerFSM)Get("Car", "_volume")).Fsm.Start();
        report.Add("Play setup: enabled=" + playControl.enabled + " active=" + playControl.gameObject.activeInHierarchy + " init=" + playControl.Fsm.Initialized + " state=" + playControl.ActiveStateName);
        Probe.TestKey = KeyCode.Z; Call("Car", "Tick"); Probe.TestKey = KeyCode.None;
        yield return null;
        report.Add("Cassette start: volume=" + audio.volume + " pitch=" + audio.pitch + " clip=" + (audio.clip != null) + " state=" + ((PlayMakerFSM)Get("Car", "_radioOnOff")).ActiveStateName);
        report.Add("Play after key: " + playControl.ActiveStateName + " enabled=" + playControl.enabled + " transitions=" + string.Join(",", playControl.Fsm.GlobalTransitions.Select(t => t.FsmEvent.Name)));
        Check(Mathf.Abs(audio.volume - .5f) < .001f && audio.pitch == 1f, "Z starts native cassette at 0.5 from zero");
        Probe.TestKey = KeyCode.Z; Call("Car", "Tick"); Probe.TestKey = KeyCode.None;
        yield return null;
        Check(audio.pitch == 0f && Mathf.Abs(audio.volume - .5f) < .001f, "Z stops music and retains volume");
        Call("Car", "SetVolume", .8f);
        Probe.TestKey = KeyCode.Z; Call("Car", "Tick"); Probe.TestKey = KeyCode.None;
        yield return null;
        Check(audio.pitch == 1f && Mathf.Abs(audio.volume - .8f) < .001f, "Z preserves manually selected nonzero volume");
        Probe.TestKey = KeyCode.Equals; Call("Car", "Tick"); Probe.TestKey = KeyCode.None;
        Check(Mathf.Abs(audio.volume - .9f) < .001f, "plus raises volume by 0.1");
        var volume = (PlayMakerFSM)Get("Car", "_volume");
        Check(Mathf.Abs(volume.FsmVariables.GetFsmFloat("audioVolume").Value - .9f) < .001f, "volume hotkey updates native knob variable");
        Call("Car", "SetVolume", 1.2f); Check(audio.volume == 1f, "volume clamps at one");
        Call("Car", "SetVolume", -.2f); Check(audio.volume == 0f, "volume clamps at zero");
        Probe.TestKey = KeyCode.Z; Call("Car", "Tick"); Probe.TestKey = KeyCode.None; yield return null;
        Probe.TestKey = KeyCode.Z; Call("Car", "Tick"); Probe.TestKey = KeyCode.None; yield return null;
        Check(Mathf.Abs(audio.volume - .5f) < .001f, "restart at manually muted zero restores 0.5");
        ((ConfigEntry<bool>)Get("Plugin", "VehicleHotkeyHint")).Value = false; Call("Car", "Tick");
        Check((string)Get("Car", "_hint") == "", "hotkey hint toggle hides text");
        Probe.TestKey = KeyCode.Minus; Call("Car", "Tick"); Probe.TestKey = KeyCode.None;
        Check(Mathf.Abs(audio.volume - .4f) < .001f, "controls work while hints are hidden");
        ((ConfigEntry<bool>)Get("Plugin", "VehicleHotkeyHint")).Value = true;
        start.Fsm.SetState("off3"); Call("Car", "Tick");
        Check(((string)Get("Car", "_hint")).Contains("R - Ignition Stop"), "running ignition hint uses migrated binding");
        Probe.TestKey = KeyCode.R; Call("Car", "Tick"); Probe.TestKey = KeyCode.None;
        Check(start.ActiveStateName == "Stop", "running ignition key invokes Stop");
        start.Fsm.SetState("off"); Probe.TestKey = KeyCode.R; Call("Car", "Tick"); Probe.TestKey = KeyCode.None;
        Check(start.ActiveStateName == "Ignition", "ignition key turns native key state");
        start.Fsm.SetState("off2"); Call("Car", "Tick");
        Check(start.ActiveStateName == "Start", "next ignition phase cranks engine");
        Set("ThirdPerson", "On", true);
        var dashboard = Node("dashboard-test", car.transform); dashboard.AddComponent<BoxCollider>();
        Check((bool)Call("PickAssist", "BlockedVehiclePart", dashboard.transform), "dashboard is blocked in third-person vehicle picks");
        RaycastHit picked;
        var dashboardCollider = dashboard.GetComponent<BoxCollider>();
        dashboardCollider.Raycast(new Ray(dashboard.transform.position - Vector3.forward * 2, Vector3.forward), out picked, 5);
        HutongGames.PlayMaker.ActionHelpers.mousePickInfo = picked;
        Call("PickAssist", "AfterDoMousePick", 5f, ~0);
        Check(HutongGames.PlayMaker.ActionHelpers.mousePickInfo.collider == null, "dashboard ray hit removed from shared mouse pick");
        Check(!(bool)Call("PickAssist", "BlockedVehiclePart", drive.transform), "DriveTrigger remains available for exit");
        var input = Node("INPUT", drive.transform); input.SetActive(false);
        var fInput = Simple(input, "INPUT_Headlight", "idle", "idle"); input.SetActive(true);
        var action = new HutongGames.PlayMaker.Actions.GetButtonDown(); action.Init(fInput.Fsm.States[0]);
        Check(!(bool)Call("ThirdPerson", "BeforeGetButtonDown", action), "vanilla headlights input suppressed to avoid double toggle");
        Time.timeScale = 0;
        Probe.TestKey = KeyCode.Minus; Call("Car", "Tick"); Probe.TestKey = KeyCode.None;
        Check(Mathf.Abs(audio.volume - .4f) < .001f, "paused vehicle ignores hotkeys"); Time.timeScale = 1;
        start.Fsm.SetState("off"); Call("Car", "Tick");
        draw = true; yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(Path.Combine(Output, "vehicle-hud.png")); yield return null; draw = false;

        inCar.Fsm.SetState("OnFoot"); player.transform.SetParent(null, true);
        var ground = GameObject.CreatePrimitive(PrimitiveType.Cube); ground.name = "Verifier.Ground"; ground.transform.position = new Vector3(0,-.1f,0); ground.transform.localScale = new Vector3(20,.2f,20);
        var body = Call("Body", "Create"); Check(body != null, "body built from native Flexa skeleton and mod model");
        var view = Enum.Parse(T("Body").GetNestedType("View", Flags), "ThirdPerson");
        Invoke(body, "LateFoot", view, .016f); yield return null; Invoke(body, "LateFoot", view, .016f);
        Invoke(body, "LateDead", new Vector3(2,0,0), null);
        var root = (GameObject)body.GetType().GetField("Root", Flags).GetValue(body);
        Check(!root.GetComponentInChildren<Animator>().enabled, "death disables animator");
        var rag = body.GetType().GetField("_ragdoll", Flags).GetValue(body);
        Vector3 before = (Vector3)rag.GetType().GetProperty("Focus").GetValue(rag);
        var physical = GameObject.Find("ApocaplayerRagdoll").GetComponentsInChildren<Rigidbody>();
        Check(physical.Length == 11 && physical.All(r => !r.isKinematic), "eleven dynamic physics bodies created");
        Check(GameObject.Find("ApocaplayerRagdoll").GetComponentsInChildren<CharacterJoint>().Length == 10, "ten joints connect limbs and torso");
        for (int i = 0; i < 75; i++) { yield return new WaitForFixedUpdate(); Invoke(body, "LateDead", Vector3.zero, null); }
        Vector3 after = (Vector3)rag.GetType().GetProperty("Focus").GetValue(rag);
        Check(after.y < before.y - .25f && after.y > -.2f && physical.All(r => r.position.magnitude < 20), "ragdoll falls under gravity and settles on ground without exploding");
        Check(ReferenceEquals(rag, body.GetType().GetField("_ragdoll", Flags).GetValue(body)), "death does not recreate physics every frame");
        Invoke(body, "Destroy"); yield return null;
        Check(GameObject.Find("ApocaplayerRagdoll") == null, "ragdoll cleanup removes physics objects");
    }
    void OnGUI() { if (draw) Call("Car", "OnGUI"); }
}
