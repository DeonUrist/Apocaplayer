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
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

[BepInPlugin("apocaplayer.jump-verifier", "Apocaplayer Jump Verifier", "1.0.0")]
[BepInDependency(Apocaplayer.Plugin.GUID)]
public class JumpProbe : BaseUnityPlugin
{
    static bool running;
    void Awake()
    {
        var arg = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("-jump-report="));
        if (arg == null) return;
        running = true;
        new Harmony("apocaplayer.jump-verifier").PatchAll();
        var go = new GameObject("Verifier.Host") { hideFlags = HideFlags.HideAndDontSave }; DontDestroyOnLoad(go);
        var checks=go.AddComponent<JumpChecks>(); checks.Output = arg.Substring(arg.IndexOf('=') + 1);
        checks.StartCoroutine(checks.Run());
    }
    [HarmonyPatch(typeof(PlayMakerFSM), "OnEnable")]
    class NoWorldOrSaves
    {
        static bool Prefix(PlayMakerFSM __instance)
        {
            if(!running) return true;
            for(var t=__instance.transform;t!=null;t=t.parent) if(t.name.StartsWith("Verifier.")) return true;
            return false;
        }
    }
}
public class JumpChecks : MonoBehaviour
{
    public string Output;
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static Type T(string name) => typeof(Apocaplayer.Plugin).Assembly.GetType("Apocaplayer." + name);
    static object Get(object o, string name) => o.GetType().GetField(name,F).GetValue(o);
    static object Call(object o, string name, params object[] args) => o.GetType().GetMethod(name,F).Invoke(o,args);
    static void Set(string type,string name,object value) => T(type).GetField(name,F).SetValue(null,value);
    readonly List<string> report = new List<string>();
    int checks;
    bool visual => Environment.GetCommandLineArgs().Contains("-jump-visual");
    void Check(bool pass,string message) { checks++; if(!pass) throw new Exception(message); }
    void Field(object o,string name,object value) => o.GetType().GetField(name,F).SetValue(o,value);
    void DisableRunners()
    { foreach(var r in Resources.FindObjectsOfTypeAll<MonoBehaviour>()) if(r != null && r.GetType().Assembly == typeof(Apocaplayer.Plugin).Assembly && r.GetType() != typeof(Apocaplayer.Plugin)) r.enabled=false; }
    public IEnumerator Run()
    {
        Directory.CreateDirectory(Output); File.WriteAllText(Path.Combine(Output,"progress.txt"),"Started\n");
        Application.runInBackground=true; Time.timeScale=1;
        DisableRunners();
        yield return null;
        File.AppendAllText(Path.Combine(Output,"progress.txt"),"Loading scene\n");
        yield return SceneManager.LoadSceneAsync(1);
        DisableRunners(); yield return null;
        File.AppendAllText(Path.Combine(Output,"progress.txt"),"Sampling\n");
        try { Exercise(); } catch(Exception e) { report.Add("FAIL " + e); }
        Directory.CreateDirectory(Output); File.WriteAllLines(Path.Combine(Output,"report.txt"),report);
        Application.Quit();
        yield break;
    }
    void Exercise()
    {
        Time.timeScale=1;
        report.Add("MODEL " + T("Plugin").GetProperty("BodyModelFile",F).GetValue(null));
        var player=new GameObject("Verifier.Player"); player.transform.position=new Vector3(0,.85f,0);
        player.AddComponent<Rigidbody>().isKinematic=true;
        var camGo=new GameObject("Verifier.Camera"); camGo.transform.position=new Vector3(0,1.55f,0);
        var cam=camGo.AddComponent<Camera>(); cam.nearClipPlane=.03f; cam.fieldOfView=70;
        var weapons=new GameObject("Verifier.Weapons"); var gun=new GameObject("akms"); gun.transform.SetParent(weapons.transform);
        Set("Game","Player",player); Set("Game","PlayerCamera",camGo.transform); Set("Game","Cam",cam); Set("Game","WeaponsParent",weapons.transform);
        var first=Enum.Parse(T("Body").GetNestedType("View",F),"FirstPerson");
        ((ConfigEntry<bool>)T("Plugin").GetField("BodyFirstPerson",F).GetValue(null)).Value=true;
        var strength=(ConfigEntry<float>)T("Plugin").GetField("FirstPersonJumpKneeCorrection",F).GetValue(null);
        var body=T("Body").GetMethod("Create",F).Invoke(null,null);
        if(body==null) throw new Exception("Body unavailable");
        var root=(GameObject)Get(body,"Root"); var bones=(Dictionary<string,Transform>)Get(body,"Bones");
        if(visual)
        {
            Call(body,"SetMesh",true,false,false); Call(body,"SetVisible",true,true);
            foreach(var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer=30;
            cam.cullingMask=1<<30; cam.clearFlags=CameraClearFlags.SolidColor; cam.backgroundColor=new Color(.16f,.2f,.25f);
            var light=new GameObject("Verifier.Light").AddComponent<Light>(); light.type=LightType.Directional; light.intensity=1.3f;
            light.transform.rotation=Quaternion.Euler(45,-30,0); light.cullingMask=1<<30;
        }
        var graph=(PlayableGraph)Get(body,"_graph"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        var output=(AnimationPlayableOutput)graph.GetOutput(0);
        foreach(var clipName in new[]{"Jump","PistolJump","RifleJumpUp","RifleJumpLoop","RifleJumpDown","Run","RifleRunLow","Kick"})
        {
            var clip=(AnimationClip)T("Anims").GetMethod("Get",F).Invoke(null,new object[]{clipName});
            if(clip==null) { report.Add("SKIP Missing comparison clip " + clipName); continue; }
            var p=AnimationClipPlayable.Create(graph,clip); p.SetApplyFootIK(false); p.SetSpeed(0); output.SetSourcePlayable(p);
            foreach(var armed in new[]{false,true})
            foreach(float pitch in new[]{0f,30f,60f,85f})
            foreach(float heading in new[]{0f,117f})
            {
                gun.SetActive(armed); var yaw=Quaternion.Euler(0,heading,0); camGo.transform.rotation=yaw*Quaternion.Euler(pitch,0,0);
                float maxY=-100, maxZ=-100, minDistance=100, maxLift=-100; int at=0;
                Field(body,"_actionClip",clipName); Field(body,"_actionW",1f);
                for(int i=0;i<=60;i++)
                {
                    root.transform.SetPositionAndRotation(Vector3.zero,yaw);
                    p.SetTime(clip.length*i/60.0); graph.Evaluate(0);
                    Field(body,"_fpWeaponW",armed?1f:0f);
                    Call(body,"FitFirstPerson",first,yaw,pitch);
                    bool capture=visual && clipName=="RifleJumpLoop" && i==57 && !armed && heading==0 && (pitch==30 || pitch==60 || pitch==85);
                    if(capture) Render(cam,"before-"+pitch+".png");
                    var left=bones["mixamorig:LeftUpLeg"]; var right=bones["mixamorig:RightUpLeg"];
                    var beforeL=left.localRotation; var beforeR=right.localRotation;
                    Call(body,"FitFirstPersonJump",false,.1f);
                    Check(Quaternion.Angle(beforeL,left.localRotation)<.01f && Quaternion.Angle(beforeR,right.localRotation)<.01f,"Third person retains exact jump pose");
                    strength.Value=0; Call(body,"FitFirstPersonJump",true,.1f);
                    Check(Quaternion.Angle(beforeL,left.localRotation)<.01f && Quaternion.Angle(beforeR,right.localRotation)<.01f,"Zero strength retains original pose");
                    strength.Value=1;
                    var calfL=bones["mixamorig:LeftLeg"].localRotation; var calfR=bones["mixamorig:RightLeg"].localRotation;
                    float lengthL=Vector3.Distance(left.position,bones["mixamorig:LeftLeg"].position);
                    Call(body,"FitFirstPersonJump",true,.1f);
                    if(capture) Render(cam,"after-"+pitch+".png");
                    Check(Quaternion.Angle(calfL,bones["mixamorig:LeftLeg"].localRotation)<.01f && Quaternion.Angle(calfR,bones["mixamorig:RightLeg"].localRotation)<.01f,"Knee bend preserved");
                    Check(Mathf.Abs(lengthL-Vector3.Distance(left.position,bones["mixamorig:LeftLeg"].position))<.0001f,"Femur length preserved");
                    if(!clipName.Contains("Jump")) Check(Quaternion.Angle(beforeL,left.localRotation)<.01f && Quaternion.Angle(beforeR,right.localRotation)<.01f,"Run and kick unchanged");
                    foreach(var side in new[]{"Left","Right"})
                    {
                        var knee=Quaternion.Inverse(yaw)*(bones["mixamorig:"+side+"Leg"].position-camGo.transform.position);
                        var thigh=Quaternion.Inverse(yaw)*(bones["mixamorig:"+side+"Leg"].position-bones["mixamorig:"+side+"UpLeg"].position);
                        float lift=Mathf.Atan2(thigh.z,-thigh.y)*Mathf.Rad2Deg;
                        Check(!float.IsNaN(lift),"Finite pose");
                        if(clipName.Contains("Jump")) Check(lift<=30.01f,"Jump knee stays below hip: " + clipName + " " + lift);
                        maxLift=Mathf.Max(maxLift,lift);
                        if(knee.y>maxY) {maxY=knee.y; at=i;}
                        maxZ=Mathf.Max(maxZ,knee.z); minDistance=Mathf.Min(minDistance,knee.magnitude);
                    }
                }
                report.Add($"POSE {clipName} armed={armed} pitch={pitch} yaw={heading} maxY={maxY:F4} maxZ={maxZ:F4} distance={minDistance:F4} lift={maxLift:F1} frame={at}");
            }
            if(clipName.Contains("Jump")) Transitions(body,bones,root,camGo.transform,first,p,clip,strength);
            graph.DestroyPlayable(p);
        }
        output.SetSourcePlayable((AnimationLayerMixerPlayable)Get(body,"_layers"));
        Integration(body,player,graph,bones);
        Call(body,"Destroy"); Set("Runner","_body",null);
        report.Add("PASS " + checks + " native pose checks; all jumps, run/kick, camera pitch/yaw, empty/armed, third person, zero strength, knee bend and femur length.");
    }
    PlayMakerFSM Fsm(GameObject player,string name,string initial,params string[] states)
    {
        var fsm=player.AddComponent<PlayMakerFSM>(); var data=new Fsm { Name=name,StartState=initial };
        data.Variables.FloatVariables=new[]{new FsmFloat("standingHeight"){Value=1.7f},new FsmFloat("Health"){Value=100f}};
        data.States=states.Select(s=>new FsmState(data){Name=s,Actions=new FsmStateAction[0]}).ToArray();
        fsm.Fsm=data; fsm.enabled=false; fsm.enabled=true; fsm.Fsm.SetState(initial); return fsm;
    }
    void Integration(object body,GameObject player,PlayableGraph graph,Dictionary<string,Transform> bones)
    {
        Set("Game","InCarFsm",Fsm(player,"InCar","OnFoot","OnFoot","InCar"));
        Set("Game","MovementFsm",Fsm(player,"Movement","Standing","Standing"));
        Set("Game","_healthFsm",Fsm(player,"Health","playerHealth","playerHealth"));
        Set("Game","_jumpFsm",Fsm(player,"Jump","Jump","Idle","Jump","Falling"));
        Set("Runner","_body",body); Set("ThirdPerson","On",false);
        Call(body,"StartAction","RifleJumpLoop",.95f); Field(body,"_actionW",1f); Field(body,"_actionUntil",Time.time+10f);
        Field(body,"_jumpPhase","loop"); Field(body,"_jumpState","Jump");
        var layers=(AnimationLayerMixerPlayable)Get(body,"_layers"); layers.SetInputWeight((int)Get(body,"_lAction"),1f);
        var action=(AnimationClipPlayable)Get(body,"_action"); action.SetSpeed(0); graph.Evaluate(0);
        var runner=Resources.FindObjectsOfTypeAll<MonoBehaviour>().First(r=>r!=null&&r.GetType()==T("Runner"));
        Field(body,"_fpLeftJump",0f); Field(body,"_fpRightJump",0f);
        Call(runner,"LateUpdate");
        Check((float)Get(body,"_fpLeftJump")>0f || (float)Get(body,"_fpRightJump")>0f,"Actual Runner applies first-person jump correction");
        Time.timeScale=0; graph.Evaluate(0); Call(runner,"LateUpdate");
        Check(!float.IsNaN(bones["mixamorig:LeftUpLeg"].rotation.x),"Paused pose remains finite");
        Time.timeScale=1; Set("ThirdPerson","On",true); graph.Evaluate(0); Call(runner,"LateUpdate");
        Check((float)Get(body,"_fpLeftJump")==0f && (float)Get(body,"_fpRightJump")==0f,"Switching to third person resets correction in Runner");
        Set("ThirdPerson","On",false);
        report.Add("PASS Runner integration, pause and first/third-person transition");
    }
    void Render(Camera cam,string file)
    {
        var rt=new RenderTexture(960,640,24); cam.targetTexture=rt; cam.Render();
        var previous=RenderTexture.active; RenderTexture.active=rt;
        var texture=new Texture2D(960,640,TextureFormat.RGB24,false); texture.ReadPixels(new Rect(0,0,960,640),0,0); texture.Apply();
        File.WriteAllBytes(Path.Combine(Output,file),texture.EncodeToPNG());
        RenderTexture.active=previous; cam.targetTexture=null; rt.Release(); Destroy(rt); Destroy(texture);
    }
    void Transitions(object body,Dictionary<string,Transform> bones,GameObject root,Transform cam,object first,
        AnimationClipPlayable p,AnimationClip clip,ConfigEntry<float> strength)
    {
        foreach(float fps in new[]{30f,60f,144f})
        {
            Call(body,"FitFirstPersonJump",false,0f); cam.rotation=Quaternion.identity;
            int frames=Mathf.CeilToInt(clip.length*fps);
            float previous=0;
            for(int i=0;i<=frames;i++)
            {
                root.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                p.SetTime(clip.length*i/(double)frames); ((PlayableGraph)Get(body,"_graph")).Evaluate(0);
                Call(body,"FitFirstPerson",first,Quaternion.identity,0f);
                Call(body,"FitFirstPersonJump",true,1f/fps);
                float correction=(float)Get(body,"_fpLeftJump");
                Check(Mathf.Abs(correction-previous)<=900f/fps+.01f,"Correction transitions at " + fps + " fps");
                previous=correction;
            }
            Field(body,"_actionW",0f);
            for(int i=0;i<Mathf.CeilToInt(.15f*fps);i++)
            { ((PlayableGraph)Get(body,"_graph")).Evaluate(0); Call(body,"FitFirstPersonJump",true,1f/fps); }
            Check((float)Get(body,"_fpLeftJump")==0f && (float)Get(body,"_fpRightJump")==0f,"Landing releases correction at " + fps + " fps");
            Field(body,"_actionW",1f);
        }
        report.Add("PASS Smooth correction and landing release for " + clip.name + " at 30/60/144 fps");
    }
}
