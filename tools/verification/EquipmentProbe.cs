using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using HutongGames.PlayMaker;
using UnityEngine;

// Same reflection contract as the installed inventory, with controllable custody
// transitions and no game saves. Exercises optional Apocainventory namespace too.
namespace Apocainventory
{
    internal sealed class Slot { public GameObject Content; }
    internal sealed class Persistence { public bool Loading = false, Normalised; }
    internal sealed class Borrow { public int Logical, Host; }
    internal sealed class Runner
    {
        public static Runner Instance;
        public readonly Slot[] Slots = Enumerable.Range(0, 6).Select(i => new Slot()).ToArray();
        public object CurrentOp;
        public readonly Persistence Save = new Persistence();
        public Borrow Borrow;
        public PlayMakerFSM Weapons;
        public int _handOrigin = -1;
        public string _handName = null;
        public bool Ready => true;
        bool IsDrawn(int slot) => Borrow != null ? slot == Borrow.Logical && Weapons.ActiveStateName == "Slot " + (Borrow.Host + 1)
            : slot < 3 && Weapons.ActiveStateName == "Slot " + (slot + 1);
    }
}

public partial class ProbeRunner
{
    object EquipmentOf(object body) => body.GetType().GetField("_equipment", Flags).GetValue(body);
    object MountOf(object gear, int index) => ((Array)gear.GetType().GetField("_mounts", Flags).GetValue(gear)).GetValue(index);
    GameObject MountedItem(object gear, int index) => (GameObject)MountOf(gear, index).GetType().GetField("Item", Flags).GetValue(MountOf(gear, index));
    GameObject Visual(object gear, int index) => (GameObject)MountOf(gear, index).GetType().GetField("Visual", Flags).GetValue(MountOf(gear, index));
    bool Showing(object gear, int index) => Visual(gear, index) != null && Visual(gear, index).GetComponentsInChildren<Renderer>(true).Any(r => r.enabled);
    void GearTick(object body) { Invoke(body, "LateEquipment"); }
    GameObject InventoryItem(string name, Transform holder)
    {
        var source = Resources.FindObjectsOfTypeAll<GameObject>().FirstOrDefault(g => !g.scene.IsValid() && g.transform.parent == null
            && g.GetComponent<Rigidbody>() != null && g.GetComponentInChildren<MeshRenderer>(true) != null && g.name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (source == null)
            source = Resources.FindObjectsOfTypeAll<GameObject>().FirstOrDefault(g => !g.scene.IsValid() && g.transform.parent == null
                && g.GetComponent<Rigidbody>() != null && g.GetComponentInChildren<MeshRenderer>(true) != null
                && (g.name.StartsWith(name, StringComparison.OrdinalIgnoreCase) || g.name.StartsWith("9mm_"+name,StringComparison.OrdinalIgnoreCase)
                    || name=="borz_smg" && (g.name.StartsWith("borz",StringComparison.OrdinalIgnoreCase)||g.name.StartsWith("9mm_borz",StringComparison.OrdinalIgnoreCase))));
        if(source!=null) report.Add("Model " + name + " = " + source.name);
        Check(source != null, "native world model exists: " + name);
        var item = (GameObject)Call("Props", "CopyForMount", source, holder); item.name = name + "(Clone)" + checks;
        item.AddComponent<Rigidbody>().isKinematic = true;
        foreach (var r in item.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        return item;
    }
    void RenderGear(object body, string file, Vector3 direction)
    {
        var root = (GameObject)body.GetType().GetField("Root", Flags).GetValue(body);
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 30;
        var go = Node("Verifier.RenderCamera"); var camera = go.AddComponent<Camera>(); camera.enabled = false;
        camera.cullingMask = 1 << 30; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.10f,.12f,.14f);
        var focus = root.transform.position + Vector3.up * .9f;
        go.transform.position = focus + direction.normalized * 3f; go.transform.LookAt(focus);
        camera.orthographic = true; camera.orthographicSize = 1.05f;
        var rt = new RenderTexture(900, 1000, 24); camera.targetTexture = rt; camera.Render();
        var previous = RenderTexture.active; RenderTexture.active = rt;
        var texture = new Texture2D(900,1000,TextureFormat.RGB24,false); texture.ReadPixels(new Rect(0,0,900,1000),0,0); texture.Apply();
        File.WriteAllBytes(Path.Combine(Output,file),texture.EncodeToPNG()); RenderTexture.active = previous;
        camera.targetTexture = null; rt.Release(); Destroy(rt); Destroy(texture); Destroy(go);
    }
    IEnumerator EquipmentExercise()
    {
        Time.timeScale = 1; Call("Game", "Reset");
        var player = Node("Verifier.Player"); player.SetActive(false); player.transform.position = new Vector3(0,.85f,0);
        player.AddComponent<Rigidbody>().isKinematic = true;
        var inCar = Simple(player,"InCar","OnFoot","OnFoot","InCar"); player.SetActive(true); inCar.Fsm.SetState("OnFoot");
        var camGo = Node("Verifier.Camera"); camGo.transform.position = new Vector3(0,1.6f,0); var cam = camGo.AddComponent<Camera>();
        Set("Game","Player",player); Set("Game","PlayerCamera",camGo.transform); Set("Game","Cam",cam); Set("Game","InCarFsm",inCar);
        var use = Node("HandItemUse",camGo.transform); var weapons = Node("Weapons",use.transform); weapons.SetActive(false);
        var fsm = Simple(weapons,"Weapons","off","off","Slot 1","Slot 2","Slot 3"); weapons.SetActive(true); fsm.Fsm.SetState("off");
        var holders = new Transform[6]; for(int i=0;i<6;i++) holders[i]=Node("Slot "+(i+1),weapons.transform).transform;
        var quick = Node("QuickItems",camGo.transform); var bag = Node("Backpack_Item",quick.transform); var bino = Node("Binocular_Item",quick.transform);
        var anim = Node("ItemAnim",camGo.transform); var binoAnim = Node("Binocular Anim",anim.transform); binoAnim.SetActive(false);
        var binoFsm = Simple(binoAnim,"Animation","off","off","animOn","on","animOff"); binoAnim.SetActive(true); binoFsm.Fsm.SetState("off");
        var run = new Apocainventory.Runner { Weapons=fsm }; Apocainventory.Runner.Instance=run;
        string[] names = { "akms", "m16a1", "rochester_m24", "22_pipe_pistol", "borz_smg", "folk_17" };
        for(int i=0;i<6;i++) run.Slots[i].Content=InventoryItem(names[i],holders[i]);
        var worn=InventoryItem("backpack",bag.transform); var binocular=InventoryItem("binocular",bino.transform);
        var body=Call("Body","Create"); Check(body!=null,"native character created for equipment");
        var view=Enum.Parse(T("Body").GetNestedType("View",Flags),"ThirdPerson");
        Invoke(body,"LateFoot",view,.016f); Invoke(body,"SetMesh",false,false,false); Invoke(body,"SetVisible",true,false); yield return null;
        Invoke(body,"LateFoot",view,.016f); GearTick(body); var gear=EquipmentOf(body);
        Check(MountedItem(gear,0)==worn,"worn backpack copied onto back");
        Check(MountedItem(gear,1)==run.Slots[0].Content && MountedItem(gear,2)==run.Slots[1].Content,"first two long guns reserved in numerical slot order");
        Check(MountedItem(gear,3)==run.Slots[3].Content && MountedItem(gear,4)==run.Slots[4].Content,"slots 4-6 participate: first pistol and SMG selected");
        Check(MountedItem(gear,7)==binocular,"quick-slot binocular attached behind belt");
        Check(Visual(gear,0).GetComponentsInChildren<Collider>(true).Length==0 && Visual(gear,1).GetComponentsInChildren<PlayMakerFSM>(true).Length==0,"copies have no gameplay colliders or FSMs");
        Check(run.Slots.All(s=>s.Content.GetComponentsInChildren<Renderer>(true).All(r=>!r.enabled)),"original inventory renderers stay hidden");
        var first=Visual(gear,1); fsm.Fsm.SetState("Slot 1"); GearTick(body);
        Check(!Showing(gear,1)&&Showing(gear,2),"drawing first rifle hides only its own back mount");
        Check(MountedItem(gear,1)==run.Slots[0].Content && !new[]{MountedItem(gear,1),MountedItem(gear,2)}.Contains(run.Slots[2].Content),"third rifle never fills drawn rifle's reserved place");
        fsm.Fsm.SetState("Slot 3"); GearTick(body); Check(Showing(gear,1)&&Showing(gear,2),"drawing ignored third rifle leaves first two visible");
        fsm.Fsm.SetState("off"); GearTick(body); Check(Visual(gear,1)==first&&Showing(gear,1),"sheathing restores same visual without rebuilding");
        var parked=Node("Staging",camGo.transform); run.Slots[0].Content.transform.SetParent(parked.transform,false);
        run.Slots[3].Content.transform.SetParent(holders[0],false); run.Borrow=new Apocainventory.Borrow{Logical=3,Host=0}; fsm.Fsm.SetState("Slot 1"); GearTick(body);
        Check(Showing(gear,1)&&!Showing(gear,3)&&Showing(gear,4),"borrowed slot 4 pistol hides holster without hiding parked slot 1 rifle");
        Check(MountedItem(gear,1)==run.Slots[0].Content && MountedItem(gear,3)==run.Slots[3].Content,"borrowed physical host does not reorder logical equipment");
        run.Borrow=null; fsm.Fsm.SetState("off"); run.Slots[0].Content.transform.SetParent(holders[0],false); run.Slots[3].Content.transform.SetParent(holders[3],false); GearTick(body);
        Check(Showing(gear,3),"sheathed extra-slot pistol returns to its original holster");
        run.Save.Normalised=true; var old=run.Slots[0].Content; run.Slots[0].Content=null; GearTick(body);
        Check(MountedItem(gear,1)==old,"save normalization preserves reserved equipment"); run.Slots[0].Content=old; run.Save.Normalised=false;
        run.CurrentOp=new object(); run.Slots[0].Content=null; GearTick(body); Check(MountedItem(gear,1)==old,"inventory transfer preserves assignments until completion"); run.Slots[0].Content=old; run.CurrentOp=null;
        binoFsm.Fsm.SetState("animOn"); GearTick(body); Check(!Showing(gear,7),"raising binoculars removes belt copy");
        binoFsm.Fsm.SetState("animOff"); GearTick(body); Check(!Showing(gear,7),"binocular remains hidden while lowering");
        binoFsm.Fsm.SetState("off"); GearTick(body); Check(Showing(gear,7),"lowered binocular returns to belt");
        var light=Node("Verifier.Light"); var sun=light.AddComponent<Light>(); sun.type=LightType.Directional; sun.intensity=1.7f; light.transform.rotation=Quaternion.Euler(35,145,0);
        RenderGear(body,"female-back-guns.png",new Vector3(1,.25f,-2)); RenderGear(body,"female-front-guns.png",new Vector3(-1,.2f,2));
        RenderGear(body,"female-side-guns.png",new Vector3(2,.1f,-.1f));
        // Two belt blades alongside two long guns and two sidearms.
        run.Slots[2].Content=run.Slots[3].Content; run.Slots[3].Content=run.Slots[4].Content;
        run.Slots[4].Content=InventoryItem("machete",holders[4]); run.Slots[5].Content=InventoryItem("shiv",holders[5]); GearTick(body);
        Check(MountedItem(gear,5)==run.Slots[4].Content && MountedItem(gear,6)==run.Slots[5].Content,"first two blades attach at opposite belt sides");
        var root=(GameObject)body.GetType().GetField("Root",Flags).GetValue(body);
        Check(root.transform.InverseTransformPoint(Visual(gear,3).transform.position).x>0 && root.transform.InverseTransformPoint(Visual(gear,4).transform.position).x<0,"first and second sidearms occupy right and left thighs");
        RenderGear(body,"female-back-all.png",new Vector3(1,.25f,-2)); RenderGear(body,"female-front-all.png",new Vector3(-1,.2f,2));
        Check(Call("Equipment","Classify","folk17").ToString()=="Sidearm","Folk pistol ID without underscore is recognized");
        var originalFirst=run.Slots[0].Content;
        var knife=InventoryItem("old_knife",holders[0]); run.Slots[0].Content=knife; GearTick(body);
        Check(MountedItem(gear,5)==knife && MountedItem(gear,6)==run.Slots[4].Content,"first two blades follow slot order and ignore third");
        fsm.Fsm.SetState("Slot 1"); GearTick(body);
        Check(!Showing(gear,5)&&Showing(gear,6)&&MountedItem(gear,6)!=run.Slots[5].Content,"drawing first blade never promotes third blade");
        fsm.Fsm.SetState("off"); run.Slots[0].Content=originalFirst; knife.transform.SetParent(null); GearTick(body);
        run.Slots[1].Content=InventoryItem("akms",holders[1]); GearTick(body); fsm.Fsm.SetState("Slot 1"); GearTick(body);
        Check(!Showing(gear,1)&&Showing(gear,2)&&MountedItem(gear,1)!=MountedItem(gear,2),"duplicate rifle types hide only the drawn object identity");
        fsm.Fsm.SetState("off"); GearTick(body);
        run.Save.Loading=true; var previous=run.Slots[0].Content; run.Slots[0].Content=null; GearTick(body);
        Check(MountedItem(gear,1)==previous,"load in progress preserves gear until logical slots restore"); run.Slots[0].Content=previous; run.Save.Loading=false; GearTick(body);
        Invoke(body,"SetVisible",false,false); Check(!Showing(gear,0)&&!Showing(gear,3),"hidden character hides equipment"); Invoke(body,"SetVisible",true,false);
        var gunBefore=Visual(gear,1); var packedRotation=gunBefore.transform.localRotation;
        worn.transform.SetParent(null); GearTick(body); Check(MountedItem(gear,0)==null,"removing backpack removes its back model");
        Check(Visual(gear,1)==gunBefore && Quaternion.Angle(packedRotation,gunBefore.transform.localRotation)>25,"removing backpack changes gun layout without recreating reserved gun");
        RenderGear(body,"female-back-no-pack.png",new Vector3(.25f,.1f,-2));
        worn.transform.SetParent(bag.transform); GearTick(body);
        Check(Quaternion.Angle(packedRotation,gunBefore.transform.localRotation)<.1f,"re-equipping backpack restores vertical side-gun layout");
        Destroy(worn); yield return null; GearTick(body);
        Check(MountedItem(gear,0)==null&&!Showing(gear,0),"destroyed worn item cannot leave a ghost equipment model");
        worn=InventoryItem("backpack",bag.transform); GearTick(body);
        Invoke(body,"Destroy"); yield return null; Check(GameObject.Find("Apocaplayer.Equipment.0")==null,"destroying body cleans up attachments");
        var character=(ConfigEntryBase)Get("Plugin","Character"); character.BoxedValue=Enum.Parse(character.SettingType,"Male");
        body=Call("Body","Create"); Invoke(body,"LateFoot",view,.016f); Invoke(body,"SetMesh",false,false,false); Invoke(body,"SetVisible",true,false); yield return null; Invoke(body,"LateFoot",view,.016f); GearTick(body);
        Check(Showing(EquipmentOf(body),0)&&Showing(EquipmentOf(body),6),"male character shares backpack and belt mounts");
        RenderGear(body,"male-back-all.png",new Vector3(1,.25f,-2)); RenderGear(body,"male-front-all.png",new Vector3(-1,.2f,2));
        Invoke(body,"Destroy"); Apocainventory.Runner.Instance=null;
    }
}
