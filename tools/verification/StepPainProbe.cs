using System;
using System.Collections;
using System.Linq;
using BepInEx.Configuration;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

public partial class ProbeRunner
{
    GameObject stepPlayer, stepObstacle, stepFloor, stepCeiling;
    Rigidbody stepBody;
    PlayMakerFSM stepMove, stepCar;
    float stepMaxY, stepMaxZ;
    int stepEvents;
    void StepFixture(float height,bool ceiling=false,bool trigger=false)
    {
        Call("AutoStepUp","Reset");Call("Game","Reset");
        stepPlayer=Node("Verifier.StepPlayer");stepPlayer.layer=6;stepPlayer.SetActive(false);stepPlayer.transform.position=new Vector3(0,.86f,0);
        stepBody=stepPlayer.AddComponent<Rigidbody>();stepBody.constraints=RigidbodyConstraints.FreezeRotation;stepBody.sleepThreshold=0;
        var capsule=stepPlayer.AddComponent<CapsuleCollider>();capsule.height=1.7f;capsule.radius=.17f;
        var secondary=stepPlayer.AddComponent<CapsuleCollider>();secondary.height=1.55f;secondary.radius=.2f;secondary.center=new Vector3(0,.07f,0);
        stepCar=Simple(stepPlayer,"InCar","OnFoot","OnFoot","InCar");
        stepMove=stepPlayer.AddComponent<PlayMakerFSM>();var data=new Fsm{Name="Movement",StartState="Standing"};
        data.Variables.FloatVariables=new[]{new FsmFloat("standingHeight"){Value=1.7f},new FsmFloat("AxisHorizontal"){Value=0},new FsmFloat("AxisVertical"){Value=2}};
        data.States=new[]{new FsmState(data){Name="Standing",Actions=new FsmStateAction[0]},new FsmState(data){Name="Crawling",Actions=new FsmStateAction[0]}};stepMove.Fsm=data;
        stepPlayer.SetActive(true);stepCar.Fsm.SetState("OnFoot");stepMove.Fsm.SetState("Standing");
        Set("Game","Player",stepPlayer);Set("Game","InCarFsm",stepCar);Set("Game","MovementFsm",stepMove);Set("Game","PlayerCamera",stepPlayer.transform);
        stepFloor=Obstacle("StepGround",new Vector3(0,-.1f,1),new Vector3(10,.2f,10),Color.gray);
        stepObstacle=Obstacle("LowStep",new Vector3(0,height*.5f,1),new Vector3(2,height,.8f),Color.green);stepObstacle.GetComponent<Collider>().isTrigger=trigger;
        if(ceiling)stepCeiling=Obstacle("LowCeiling",new Vector3(0,1.9f,1),new Vector3(3,.15f,2),Color.red);
        stepMaxY=stepBody.position.y;stepMaxZ=0;stepEvents=0;Physics.SyncTransforms();
    }
    IEnumerator WalkStep(int frames,bool move=true)
    {
        float last=0;
        for(int i=0;i<frames;i++)
        {
            foreach(var terrain in UnityEngine.Object.FindObjectsOfType<TerrainCollider>())terrain.enabled=false;
            var velocity=stepBody.velocity;velocity.z=move?2:0;velocity.x=0;stepBody.velocity=velocity;
            stepMove.FsmVariables.FindFsmFloat("AxisVertical").Value=move?2:0;
            Call("AutoStepUp","Tick");float next=(float)Get("AutoStepUp","_nextStep");if(next>last+.001f){stepEvents++;last=next;}
            yield return new WaitForFixedUpdate();
            stepMaxY=Mathf.Max(stepMaxY,stepBody.position.y);stepMaxZ=Mathf.Max(stepMaxZ,stepBody.position.z);
        }
        report.Add("WALK steps="+stepEvents+" maxY="+stepMaxY+" maxZ="+stepMaxZ);
    }
    void ClearStepFixture()
    { Call("AutoStepUp","Reset");Destroy(stepPlayer);Destroy(stepObstacle);Destroy(stepFloor);if(stepCeiling!=null)Destroy(stepCeiling);stepCeiling=null; }
    IEnumerator StepPainExercise()
    {
        Time.timeScale=1;
        // Isolated fixtures supply their own ground; stock terrain at the world
        // origin otherwise becomes an unintended low ceiling for the bus.
        foreach(var collider in UnityEngine.Object.FindObjectsOfType<Collider>())collider.enabled=false;
        StepFixture(.25f);yield return WalkStep(45);
        var tap=Get("AutoStepUp","_contacts");
        report.Add("CONTACT ground="+tap.GetType().GetField("GroundAt",Flags).GetValue(tap)+" low="+tap.GetType().GetField("LowAt",Flags).GetValue(tap)+" normal="+tap.GetType().GetField("Normal",Flags).GetValue(tap)+" now="+Time.fixedTime+" velocity="+stepBody.velocity);
        Check(stepEvents>0&&stepMaxY>1.03f&&stepMaxZ>1.1f,"grounded player steps onto 25 cm obstacle and keeps moving");
        Check(stepPlayer.GetComponents<CapsuleCollider>()[0].height==1.7f&&stepPlayer.GetComponents<CapsuleCollider>()[1].height==1.55f,"step-up preserves both vanilla body capsule sizes");
        ClearStepFixture();yield return null;
        StepFixture(.6f);yield return WalkStep(40);Check(stepEvents==0&&stepMaxZ<.6f,"tall wall does not become climbable");ClearStepFixture();yield return null;
        StepFixture(.25f,true);yield return WalkStep(40);Check(stepEvents==0,"low overhead clearance prevents step-up");ClearStepFixture();yield return null;
        StepFixture(.25f,false,true);yield return WalkStep(35);Check(stepEvents==0,"trigger volumes do not trigger step-up");ClearStepFixture();yield return null;
        StepFixture(.25f);yield return WalkStep(25,false);Check(stepEvents==0,"no movement input produces no step-up");ClearStepFixture();yield return null;
        StepFixture(.25f);stepCar.Fsm.SetState("InCar");yield return WalkStep(30);Check(stepEvents==0,"automatic step-up disabled while driving");ClearStepFixture();yield return null;
        StepFixture(.25f);stepMove.FsmVariables.FindFsmFloat("standingHeight").Value=.56f;stepMove.Fsm.SetState("Crawling");yield return WalkStep(30);Check(stepEvents==0,"prone player does not auto-step");ClearStepFixture();yield return null;
        StepFixture(.25f);((ConfigEntry<bool>)Get("Plugin","AutomaticStepUp")).Value=false;yield return WalkStep(30);Check(stepEvents==0,"AutomaticStepUp Off prevents stepping");((ConfigEntry<bool>)Get("Plugin","AutomaticStepUp")).Value=true;ClearStepFixture();yield return null;
        StepFixture(.25f);Destroy(stepObstacle);stepObstacle=null;stepFloor.transform.position=new Vector3(0,-.65f,1);
        var asset=Resources.FindObjectsOfTypeAll<GameObject>().First(g=>!g.scene.IsValid()&&g.transform.parent==null&&g.name=="Rustliner"&&g.GetComponent<Rigidbody>()!=null);
        var host=Node("Verifier.StepBus");host.SetActive(false);var bus=Instantiate(asset,host.transform);bus.name="Rustliner";bus.transform.localPosition=Vector3.zero;bus.transform.localRotation=Quaternion.identity;
        foreach(var script in bus.GetComponentsInChildren<MonoBehaviour>(true))DestroyImmediate(script);
        foreach(var rb in bus.GetComponentsInChildren<Rigidbody>(true)){rb.isKinematic=true;rb.useGravity=false;}host.SetActive(true);
        stepBody.position=new Vector3(2.05f,.315f,1.85f);stepPlayer.transform.rotation=Quaternion.Euler(0,-90,0);Physics.SyncTransforms();yield return null;
        int busSteps=0;float lastBus=0,minX=stepBody.position.x,maxY=stepBody.position.y;
        for(int i=0;i<60;i++)
        {
            foreach(var terrain in UnityEngine.Object.FindObjectsOfType<TerrainCollider>())terrain.enabled=false;
            var velocity=stepBody.velocity;velocity.x=-2;velocity.z=0;stepBody.velocity=velocity;
            Call("RustlinerDoors","ApplyForPlayer",bus.transform,stepPlayer);Call("AutoStepUp","Tick");float next=(float)Get("AutoStepUp","_nextStep");if(next>lastBus+.001f){busSteps++;lastBus=next;}
            yield return new WaitForFixedUpdate();minX=Mathf.Min(minX,stepBody.position.x);maxY=Mathf.Max(maxY,stepBody.position.y);
        }
        report.Add("RUSTLINER steps="+busSteps+" minX="+minX+" maxY="+maxY);
        var busTap=Get("AutoStepUp","_contacts");report.Add("BUS CONTACT ground="+busTap.GetType().GetField("GroundAt",Flags).GetValue(busTap)+" low="+busTap.GetType().GetField("LowAt",Flags).GetValue(busTap)+" normal="+busTap.GetType().GetField("Normal",Flags).GetValue(busTap)+" now="+Time.fixedTime+" forward="+stepPlayer.transform.forward+" reject="+Get("AutoStepUp","LastReject"));
        report.Add("BUS direct="+Call("AutoStepUp","TryStep",Vector3.left)+" reject="+Get("AutoStepUp","LastReject"));
        foreach(var hit in (RaycastHit[])Get("AutoStepUp","Hits"))if(hit.collider!=null)report.Add("BUS HIT "+hit.collider.name+" center="+hit.collider.bounds.center+" size="+hit.collider.bounds.size+" distance="+hit.distance+" normal="+hit.normal+" ignored="+Physics.GetIgnoreCollision(stepPlayer.GetComponents<CapsuleCollider>()[0],hit.collider));
        Check(minX<1.1f&&maxY>.45f,"player crosses vanilla Rustliner side steps without manual jump");
        ClearStepFixture();Call("RustlinerDoors","Reset");Destroy(host);yield return null;
        yield return PainExercise();
    }
    IEnumerator PainExercise()
    {
        var player=Node("Verifier.PainPlayer");Set("Game","Player",player);
        var character=(ConfigEntryBase)Get("Plugin","Character");character.BoxedValue=Enum.Parse(character.SettingType,"Female");
        var loaded=(AudioClip[])Get("FemalePain","Clips");
        Check(loaded.All(c=>c!=null)&&loaded[0].channels==2&&loaded[1].channels==1,"female WAV clips load with original stereo/mono channels");
        Check(Mathf.Abs(loaded[0].length-.756f)<.01f&&Mathf.Abs(loaded[1].length-.820f)<.01f,"female pain durations match the supplied WAVs");
        foreach(string fsmName in new[]{"DamageEffectSound","DamageEffectSound_InCar"})
        {
            player.SetActive(false);var f=Simple(player,fsmName,"off","off","damage");player.SetActive(true);
            var vanilla=new[]{new FsmObject{ObjectType=typeof(AudioClip),Value=AudioClip.Create("human_hurt",4410,1,44100,false)},new FsmObject{ObjectType=typeof(AudioClip),Value=AudioClip.Create("human_hurt_2",4410,1,44100,false)}};
            var action=new PlayRandomSound{audioClips=vanilla,weights=new FsmFloat[]{1f,1f},volume=1f,noRepeat=false,gameObject=new FsmOwnerDefault{OwnerOption=OwnerDefaultOption.UseOwner},position=new FsmVector3{UseVariable=true}};action.Init(f.Fsm.States[1]);
            object[] args={action,null};T("FemalePain").GetMethod("Before",Flags).Invoke(null,args);
            Check(action.audioClips[0].Value==loaded[0]&&action.audioClips[1].Value==loaded[1],"female "+fsmName+" swaps the two hurt choices");
            T("FemalePain").GetMethod("Finally",Flags).Invoke(null,new object[]{action,args[1],null});Check(ReferenceEquals(action.audioClips,vanilla),"pain action restores original clip array");
            character.BoxedValue=Enum.Parse(character.SettingType,"Male");object[] maleArgs={action,null};T("FemalePain").GetMethod("Before",Flags).Invoke(null,maleArgs);
            Check(ReferenceEquals(action.audioClips,vanilla)&&maleArgs[1]==null,"male pain keeps vanilla choices");
            character.BoxedValue=Enum.Parse(character.SettingType,"Female");
            action.OnEnter();Check(ReferenceEquals(action.audioClips,vanilla),"Harmony playback callback finishes and restores clips");
            Check(UnityEngine.Object.FindObjectsOfType<AudioSource>().Any(s=>s.clip==loaded[0]||s.clip==loaded[1]),"actual female pain playback uses supplied clip");
        }
        var head=Node("head",player.transform);var npc=Node("Verifier.PainNpc");
        foreach(var owner in new[]{head,npc})
        {
            owner.SetActive(false);var f=Simple(owner,"DamageEffectSound","off","off","damage");owner.SetActive(true);
            var vanilla=new[]{new FsmObject{ObjectType=typeof(AudioClip),Value=AudioClip.Create("human_hurt",4410,1,44100,false)}};
            var action=new PlayRandomSound{audioClips=vanilla};action.Init(f.Fsm.States[1]);object[] args={action,null};
            T("FemalePain").GetMethod("Before",Flags).Invoke(null,args);
            Check(owner==head?action.audioClips[0].Value==loaded[0]:ReferenceEquals(action.audioClips,vanilla),owner==head?"player head-hit pain uses female sound":"NPC pain is unaffected by female selection");
            T("FemalePain").GetMethod("Finally",Flags).Invoke(null,new object[]{action,args[1],null});
            ((ConfigEntry<bool>)Get("Plugin","Enabled")).Value=false;object[] disabled={action,null};T("FemalePain").GetMethod("Before",Flags).Invoke(null,disabled);
            Check(ReferenceEquals(action.audioClips,vanilla)&&disabled[1]==null,"disabled mod retains vanilla pain");((ConfigEntry<bool>)Get("Plugin","Enabled")).Value=true;
        }
        Destroy(player);Destroy(npc);yield return null;
    }
}
