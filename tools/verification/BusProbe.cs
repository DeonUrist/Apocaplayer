using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public partial class ProbeRunner
{
    string BusPath(Transform node)
    { string name=node.name;for(var p=node.parent;p!=null;p=p.parent)name=p.name+"/"+name;return name; }
    IEnumerator BusExercise()
    {
        var assets=Resources.FindObjectsOfTypeAll<GameObject>();
        var candidates=assets.Where(g=>!g.scene.IsValid() && (g.name.IndexOf("rust",StringComparison.OrdinalIgnoreCase)>=0||g.name.IndexOf("bus",StringComparison.OrdinalIgnoreCase)>=0)).ToArray();
        foreach(var go in candidates.Where(g=>g.transform.parent==null&&!g.name.StartsWith("bush",StringComparison.OrdinalIgnoreCase)))report.Add("ASSET "+BusPath(go.transform));
        var player=GameObject.Find("Player");
        if(player!=null)foreach(var c in player.GetComponentsInChildren<Collider>(true))report.Add("PLAYER "+ColliderDescription(c));
        foreach(var go in assets.Where(g=>g.scene.IsValid() && g.name=="PlayerCameraHolder"))foreach(var c in go.GetComponentsInChildren<Collider>(true))report.Add("CAMERA "+ColliderDescription(c));
        var root=candidates.FirstOrDefault(g=>g.transform.parent==null&&g.GetComponent<Rigidbody>()!=null&&g.name.IndexOf("rustliner",StringComparison.OrdinalIgnoreCase)>=0)
            ??candidates.FirstOrDefault(g=>g.transform.parent==null&&g.GetComponent<Rigidbody>()!=null);
        Check(root!=null,"Rustliner prefab located in vanilla assets");
        var host=Node("Verifier.BusAsset");host.SetActive(false);var copy=Instantiate(root,host.transform);copy.name=root.name;
        foreach(var behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true))DestroyImmediate(behaviour);
        foreach(var body in copy.GetComponentsInChildren<Rigidbody>(true)){body.isKinematic=true;body.useGravity=false;}
        copy.transform.localPosition=Vector3.zero;copy.transform.localRotation=Quaternion.identity;host.SetActive(true);Physics.SyncTransforms();
        foreach(var c in copy.GetComponentsInChildren<Collider>(true))report.Add("BUS "+ColliderDescription(c));
        foreach(var t in copy.GetComponentsInChildren<Transform>(true))if(t.name.IndexOf("door",StringComparison.OrdinalIgnoreCase)>=0||t.name.IndexOf("exit",StringComparison.OrdinalIgnoreCase)>=0)
            report.Add("DOOR "+BusPath(t)+" pos="+t.position.ToString("F3")+" rot="+t.eulerAngles.ToString("F2"));
        foreach(var t in copy.GetComponentsInChildren<Transform>(true))
        {
            if(t.name.IndexOf("hinge_door",StringComparison.OrdinalIgnoreCase)<0)continue;
            var direction=(new Vector3(t.position.x,0,t.position.z)).normalized;
            if(direction.sqrMagnitude<.1f)continue;
            var hits=Physics.RaycastAll(t.position+direction*2,-direction,4,~0,QueryTriggerInteraction.Ignore).Where(h=>h.collider.transform.IsChildOf(copy.transform)).OrderBy(h=>h.distance).ToArray();
            foreach(var hit in hits)report.Add("DOOR RAY "+t.name+" "+BusPath(hit.collider.transform)+" point="+hit.point.ToString("F3")+" normal="+hit.normal.ToString("F3"));
        }
        var solids=copy.GetComponentsInChildren<Collider>(true).Where(c=>c.enabled&&!c.isTrigger&&c.gameObject.activeInHierarchy).ToArray();
        foreach(var doorway in new[]{"side","rear"})
        {
            var point=doorway=="side"?new Vector3(1.2f,0,1.86f):new Vector3(0,0,-4.1f);
            foreach(var hit in Physics.RaycastAll(point+Vector3.up*3,Vector3.down,5,~0,QueryTriggerInteraction.Ignore).Where(h=>solids.Contains(h.collider)).OrderByDescending(h=>h.point.y))
                report.Add("HEIGHT "+doorway+" y="+hit.point.y.ToString("F4")+" normal="+hit.normal.ToString("F3")+" "+ColliderDescription(hit.collider));
        }
        var probe=Node("Verifier.ClearanceCapsule");probe.layer=2;probe.transform.position=new Vector3(100,100,100);var capsule=probe.AddComponent<CapsuleCollider>();capsule.radius=.17f;capsule.direction=1;
        Physics.SyncTransforms();
        var sanity=solids.First(c=>c is BoxCollider&&c.bounds.size.sqrMagnitude>1);
        Vector3 sanityDirection;float sanityDistance;
        Check(Physics.ComputePenetration(capsule,sanity.bounds.center,Quaternion.identity,sanity,sanity.transform.position,sanity.transform.rotation,out sanityDirection,out sanityDistance),"native capsule penetration query detects overlap with bus collider");
        foreach(string door in new[]{"side","rear"})
        {
            int examples=0;
            for(float lane=door=="side"?1.55f:-.4f;lane<=(door=="side"?2.2f:.4f);lane+=.05f)
            for(float feet=-.185f;feet<=.16f;feet+=.05f)
            {
                var xz=door=="side"?new Vector3(1.2f,0,lane):new Vector3(lane,0,-4.2f);
                var standing=new List<string>();var crouching=new List<string>();
                foreach(float height in new[]{1.7f,1f})
                {
                    capsule.height=height;var position=xz+Vector3.up*(feet+height*.5f);
                    foreach(var c in solids)
                    {
                        Vector3 direction;float overlap;
                        if(Physics.ComputePenetration(capsule,position,Quaternion.identity,c,c.transform.position,c.transform.rotation,out direction,out overlap)&&overlap>.012f)
                            (height>1.5f?standing:crouching).Add(ColliderDescription(c)+" penetration="+overlap.ToString("F4"));
                    }
                }
                if(standing.Count>0&&crouching.Count==0&&examples++<5)report.Add("STANDING_ONLY "+door+" lane="+lane.ToString("F2")+" feet="+feet.ToString("F3")+" "+string.Join(" | ",standing));
                if((door=="side"&&Mathf.Abs(lane-1.85f)<.03f||door=="rear"&&Mathf.Abs(lane)<.03f)&&feet<-.17f)
                    report.Add("CENTER_CLEARANCE "+door+" standingHits="+standing.Count+" crouchingHits="+crouching.Count+" "+string.Join(" | ",standing));
            }
            report.Add("STANDING_ONLY_COUNT "+door+" "+examples);
        }
        var geometry=solids.OfType<BoxCollider>().ToDictionary(c=>c,c=>new[]{c.center,c.size});
        var enabledStates=solids.ToDictionary(c=>c,c=>c.enabled);
        int sideProblems=0,rearProblems=0;
        foreach(string door in new[]{"side","rear"})
        {
            for(float lane=door=="side"?1.55f:-.4f;lane<=(door=="side"?2.2f:.4f);lane+=.05f)
            for(float feet=-.185f;feet<=.16f;feet+=.05f)
            {
                capsule.height=1.7f;var position=(door=="side"?new Vector3(1.2f,0,lane):new Vector3(lane,0,-4.2f))+Vector3.up*(feet+.85f);
                var blocking=new List<Collider>();bool crouchedClear=true;
                foreach(var c in solids)
                {
                    Vector3 direction;float overlap;
                    if(Physics.ComputePenetration(capsule,position,Quaternion.identity,c,c.transform.position,c.transform.rotation,out direction,out overlap)&&overlap>.012f)blocking.Add(c);
                }
                capsule.height=1f;
                foreach(var c in solids){Vector3 direction;float overlap;if(Physics.ComputePenetration(capsule,position-Vector3.up*.35f,Quaternion.identity,c,c.transform.position,c.transform.rotation,out direction,out overlap)&&overlap>.012f)crouchedClear=false;}
                capsule.height=1.7f;if(blocking.Count==0||!crouchedClear)continue;
                probe.transform.position=position;Physics.SyncTransforms();Call("RustlinerDoors","ApplyForPlayer",copy.transform,probe);
                bool fixedHere=blocking.All(c=>Physics.GetIgnoreCollision(capsule,c));
                if(!fixedHere)report.Add("UNFIXED "+door+" lane="+lane+" feet="+feet+" "+string.Join(" | ",blocking.Where(c=>!Physics.GetIgnoreCollision(capsule,c)).Select(ColliderDescription)));
                if(!fixedHere){if(door=="side")sideProblems++;else rearProblems++;}
            }
        }
        Check(sideProblems==0,"all diagnosed standing-only side-door collisions bypassed locally");
        Check(rearProblems==0,"all diagnosed standing-only rear-door collisions bypassed locally");
        probe.transform.position=new Vector3(1.2f,.915f,1.85f);Call("RustlinerDoors","ApplyForPlayer",copy.transform,probe);
        var ignored=solids.Where(c=>Physics.GetIgnoreCollision(capsule,c)).ToArray();
        Check(ignored.Length==3,"side doorway bypasses only three identified upper-frame colliders");
        var secondary=probe.AddComponent<CapsuleCollider>();secondary.height=1.55f;secondary.radius=.2f;secondary.center=new Vector3(0,.07f,0);
        Call("RustlinerDoors","ApplyForPlayer",copy.transform,probe);Check(ignored.All(c=>Physics.GetIgnoreCollision(secondary,c)),"secondary vanilla body capsule receives same local clearance");
        var panel=GameObject.CreatePrimitive(PrimitiveType.Cube);panel.name="RustlinerDoor_R";panel.transform.SetParent(copy.transform,false);panel.transform.localPosition=new Vector3(1.25f,.75f,1.85f);panel.transform.localScale=new Vector3(.12f,1.5f,.65f);
        Call("RustlinerDoors","ApplyForPlayer",copy.transform,probe);Check(!Physics.GetIgnoreCollision(capsule,panel.GetComponent<Collider>()),"fitted closed door panel still collides with player");
        Check(solids.Where(c=>!ignored.Contains(c)).All(c=>!Physics.GetIgnoreCollision(capsule,c)),"floor, walls and unrelated bus colliders retain player collision");
        var objectCollider=Node("Verifier.OtherObject").AddComponent<SphereCollider>();Check(ignored.All(c=>!Physics.GetIgnoreCollision(objectCollider,c)),"door frame collision remains active for other objects and projectiles");
        Check(geometry.All(kv=>kv.Key.center==kv.Value[0]&&kv.Key.size==kv.Value[1])&&enabledStates.All(kv=>kv.Key.enabled==kv.Value),"bus collider dimensions and enabled state remain untouched");
        Check(capsule.height==1.7f&&capsule.radius==.17f&&secondary.height==1.55f&&secondary.radius==.2f,"standing player body hitboxes remain unchanged");
        probe.transform.position=new Vector3(0,.915f,-2);Call("RustlinerDoors","ApplyForPlayer",copy.transform,probe);
        Check(solids.All(c=>!Physics.GetIgnoreCollision(capsule,c)&&!Physics.GetIgnoreCollision(secondary,c)),"leaving door region restores upper-frame collision");
        probe.transform.position=new Vector3(0,.915f,-4.2f);Call("RustlinerDoors","ApplyForPlayer",copy.transform,probe);
        Check(solids.Count(c=>Physics.GetIgnoreCollision(capsule,c))==1,"rear door bypasses only its identified header");
        Call("RustlinerDoors","Restore");Check(solids.All(c=>!Physics.GetIgnoreCollision(capsule,c)),"disabling mod restores every modified collision pair");
        var frame=ignored[0];Physics.IgnoreCollision(capsule,frame,true);
        probe.transform.position=new Vector3(1.2f,.915f,1.85f);Call("RustlinerDoors","ApplyForPlayer",copy.transform,probe);Call("RustlinerDoors","Restore");
        Check(Physics.GetIgnoreCollision(capsule,frame),"pre-existing collision exclusions from other mods are preserved");Physics.IgnoreCollision(capsule,frame,false);
        copy.name="Poloska";Call("RustlinerDoors","Reset");Call("RustlinerDoors","ApplyForPlayer",copy.transform,probe);
        Check(solids.All(c=>!Physics.GetIgnoreCollision(capsule,c)),"other vehicle types receive no clearance modification");copy.name="Rustliner";
        File.WriteAllLines(Path.Combine(Output,"bus-colliders.txt"),report);
        yield return null;
    }
    string ColliderDescription(Collider c)
    {
        string text=BusPath(c.transform)+" "+c.GetType().Name+" enabled="+c.enabled+" trigger="+c.isTrigger+" layer="+c.gameObject.layer+" pos="+c.transform.position.ToString("F3")+" bounds="+c.bounds.center.ToString("F3")+" size="+c.bounds.size.ToString("F3");
        if(c is CapsuleCollider){var k=(CapsuleCollider)c;text+=" height="+k.height+" radius="+k.radius+" center="+k.center.ToString("F3");}
        if(c is SphereCollider){var k=(SphereCollider)c;text+=" radius="+k.radius+" center="+k.center.ToString("F3");}
        if(c is MeshCollider){var k=(MeshCollider)c;text+=" convex="+k.convex+" mesh="+(k.sharedMesh!=null?k.sharedMesh.name:"null");}
        return text;
    }
}
