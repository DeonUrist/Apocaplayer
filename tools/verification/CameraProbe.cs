using System;
using System.Collections;
using System.IO;
using BepInEx.Configuration;
using UnityEngine;

public partial class ProbeRunner
{
    GameObject Obstacle(string name, Vector3 position, Vector3 size, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = "Verifier." + name; go.layer=30;
        go.transform.position=position; go.transform.localScale=size;
        var material=new Material(Shader.Find("Standard")); material.color=color; go.GetComponent<Renderer>().sharedMaterial=material;
        return go;
    }
    Texture2D CameraImage(Camera camera,string file)
    {
        var target=new RenderTexture(1000,800,24); camera.targetTexture=target; camera.aspect=1.25f;
        camera.Render(); var previous=RenderTexture.active; RenderTexture.active=target;
        var image=new Texture2D(1000,800,TextureFormat.RGB24,false); image.ReadPixels(new Rect(0,0,1000,800),0,0); image.Apply();
        File.WriteAllBytes(Path.Combine(Output,file),image.EncodeToPNG()); RenderTexture.active=previous; camera.targetTexture=null; target.Release(); Destroy(target);
        return image;
    }
    float Difference(Texture2D a,Texture2D b,int x,int y)
    { var c=a.GetPixel(x,y);var d=b.GetPixel(x,y);return Mathf.Abs(c.r-d.r)+Mathf.Abs(c.g-d.g)+Mathf.Abs(c.b-d.b); }
    IEnumerator CameraExercise()
    {
        Time.timeScale=1; Call("Game","Reset");
        var toggle=(ConfigEntry<bool>)Get("Plugin","OcclusionPrototype");
        Check(toggle.Definition.Section=="General"&&toggle.Definition.Key=="3rd person camera culling","body configuration exposes 3rd person camera culling On/Off");
        string migration=Path.Combine(Output,"toggle-migration.cfg");
        File.WriteAllText(migration,"[CAMERA]\nOcclusionPrototype = false\n");
        var legacyConfig=new ConfigFile(migration,false){SaveOnConfigSet=false};
        var migrated=(ConfigEntry<bool>)Call("Plugin","BindCameraCulling",legacyConfig);
        Check(!migrated.Value&&!legacyConfig.ContainsKey(new ConfigDefinition("CAMERA","OcclusionPrototype")),"legacy Off toggle migrates into body configuration without duplicate setting");
        File.WriteAllText(migration,"[General]\n3rd person camera culling = false\n");
        var reloadConfig=new ConfigFile(migration,false){SaveOnConfigSet=false};
        Check(!((ConfigEntry<bool>)Call("Plugin","BindCameraCulling",reloadConfig)).Value,"saved body toggle remains Off after reload");
        Check((bool)T("OcclusionCutaway").GetProperty("Available",Flags).GetValue(null),"compiled camera shader loads and is supported in native Unity");
        var player=Node("Verifier.Player");player.layer=6;player.SetActive(false);player.transform.position=new Vector3(0,.85f,0);
        player.AddComponent<Rigidbody>().isKinematic=true;
        var incar=Simple(player,"InCar","OnFoot","OnFoot","InCar");player.SetActive(true);incar.Fsm.SetState("OnFoot");
        var eye=Node("Verifier.Camera");eye.transform.position=new Vector3(0,1.65f,0); var camera=eye.AddComponent<Camera>();
        camera.cullingMask=1<<30;camera.fieldOfView=60;camera.nearClipPlane=.05f;camera.farClipPlane=100;camera.allowHDR=false;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.06f,.09f,.14f);
        Set("Game","Player",player);Set("Game","PlayerCamera",eye.transform);Set("Game","Cam",camera);Set("Game","InCarFsm",incar);
        var body=Call("Body","Create");Check(body!=null,"native character created for camera prototype");Set("Runner","_body",body);
        var view=Enum.Parse(T("Body").GetNestedType("View",Flags),"ThirdPerson");
        Invoke(body,"LateFoot",view,.016f);Invoke(body,"SetMesh",false,false,false);Invoke(body,"SetVisible",true,false);yield return null;Invoke(body,"LateFoot",view,.016f);
        var root=(GameObject)body.GetType().GetField("Root",Flags).GetValue(body);
        foreach(var node in root.GetComponentsInChildren<Transform>(true))node.gameObject.layer=30;
        var light=Node("Verifier.CameraLight");var sun=light.AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.3f;light.transform.rotation=Quaternion.Euler(40,135,0);
        Obstacle("Ground",new Vector3(0,-.13f,0),new Vector3(20,.2f,20),new Color(.22f,.27f,.22f));
        Obstacle("FarMarker",new Vector3(0,1,4),new Vector3(2,2,.2f),Color.green);
        ((ConfigEntry<float>)Get("Plugin","ThirdDistance")).Value=3f;((ConfigEntry<float>)Get("Plugin","ThirdShoulder")).Value=0;
        ((ConfigEntry<bool>)Get("Plugin","OcclusionPrototype")).Value=true;
        Set("ThirdPerson","On",true);Call("ThirdPerson","Tick");Physics.SyncTransforms();
        var clear=CameraImage(camera,"camera-clear.png");Vector3 fixedPosition=(Vector3)Get("ThirdPerson","ViewPos");
        var unblocked=Get("OcclusionCutaway","_active");
        var terrainCount=(System.Collections.IDictionary)unblocked.GetType().GetField("_terrains",Flags).GetValue(unblocked);
        var rendererCount=(System.Collections.IDictionary)unblocked.GetType().GetField("_renderers",Flags).GetValue(unblocked);
        Check(terrainCount.Count==0&&rendererCount.Count==0,"ordinary ground under feet does not trigger cutaway or an extra scene render");
        Vector3 eyePosition=eye.transform.position;Quaternion eyeRotation=eye.transform.rotation;
        var wall=Obstacle("LargeWall",new Vector3(0,1.4f,-1.25f),new Vector3(9,4,.25f),new Color(.75f,.14f,.12f));
        var originalMaterial=wall.GetComponent<Renderer>().sharedMaterial;Physics.SyncTransforms();
        Set("ThirdPerson","On",false);eye.transform.position=fixedPosition;eye.transform.rotation=(Quaternion)Get("ThirdPerson","ViewRot");
        var blocked=CameraImage(camera,"camera-wall-opaque.png");eye.transform.SetPositionAndRotation(eyePosition,eyeRotation);
        Set("ThirdPerson","On",true);Call("ThirdPerson","Tick");for(int i=0;i<12;i++)yield return null;
        var cut=CameraImage(camera,"camera-wall-cutaway.png");
        var effect=Get("OcclusionCutaway","_active");
        report.Add("Cutaway strength="+effect.GetType().GetField("_strength",Flags).GetValue(effect)+" clear depth ready="+effect.GetType().GetField("_clearDepthReady",Flags).GetValue(effect));
        var shaderMaterial=(Material)effect.GetType().GetField("_composite",Flags).GetValue(effect);
        report.Add("Window endpoints="+shaderMaterial.GetVector("_Ends")+" radius="+shaderMaterial.GetFloat("_Radius")+" focus="+shaderMaterial.GetFloat("_FocusDepth"));
        Check(Vector3.Distance(fixedPosition,(Vector3)Get("ThirdPerson","ViewPos"))<.001f,"wall never changes chosen third-person camera position");
        Check(Difference(blocked,cut,500,400)>.08f,"occluding wall becomes semi-transparent over player");
        Check(Difference(blocked,cut,60,400)<.035f,"large wall remains opaque outside localized window");
        Check(wall.GetComponent<Renderer>().sharedMaterial==originalMaterial&&!wall.GetComponent<Renderer>().forceRenderingOff,"wall material and rendering state restored after clear pass");
        Check(eye.transform.position==eyePosition&&eye.transform.rotation==eyeRotation,"game's shooting-eye transform stays untouched");
        float wallRadius=shaderMaterial.GetFloat("_Radius");
        wall.name="Verifier.Roof";for(int i=0;i<16;i++)yield return null;
        var roofImage=CameraImage(camera,"camera-roof-expanded.png");
        Check(Mathf.Abs(shaderMaterial.GetFloat("_Radius")/wallRadius-3f)<.03f,"blocking roof expands transparent window radius threefold");
        Check(Difference(blocked,roofImage,260,400)>.05f,"expanded roof window reveals a region beyond normal outline");
        wall.name="Verifier.LargeWall";for(int i=0;i<32;i++)yield return null;CameraImage(camera,"camera-wall-normal-radius.png");
        Check(Mathf.Abs(shaderMaterial.GetFloat("_Radius")/wallRadius-1f)<.03f,"normal wall returns to original outline size");
        // Pressed against a wall in front: it must remain solid, including while
        // another wall behind the character is triggering the cutaway.
        wall.SetActive(false);
        var frontWall=Obstacle("FrontWall",new Vector3(0,1.4f,.22f),new Vector3(9,4,.2f),new Color(.1f,.2f,.75f));Physics.SyncTransforms();
        Set("ThirdPerson","On",false);eye.transform.position=fixedPosition;eye.transform.rotation=(Quaternion)Get("ThirdPerson","ViewRot");
        var frontOpaque=CameraImage(camera,"camera-front-wall-opaque.png");eye.transform.SetPositionAndRotation(eyePosition,eyeRotation);
        Set("ThirdPerson","On",true);Call("ThirdPerson","Tick");for(int i=0;i<12;i++)yield return null;
        var frontCut=CameraImage(camera,"camera-front-wall-protected.png");
        // A nearby aiming target changes convergence angle. Compare at the exact
        // rendered pose so character pixels moving are not mistaken for fading.
        Set("ThirdPerson","On",false);eye.transform.position=fixedPosition;eye.transform.rotation=(Quaternion)Get("ThirdPerson","ViewRot");
        Destroy(frontOpaque);frontOpaque=CameraImage(camera,"camera-front-wall-opaque.png");eye.transform.SetPositionAndRotation(eyePosition,eyeRotation);Set("ThirdPerson","On",true);
        Check(Difference(frontOpaque,frontCut,580,350)<.035f,"wall pressed against in front stays opaque");
        wall.SetActive(true);Physics.SyncTransforms();for(int i=0;i<12;i++)yield return null;
        var both=CameraImage(camera,"camera-rear-and-front-walls.png");
        Check(Difference(frontOpaque,both,580,350)<Difference(blocked,frontOpaque,580,350)*.35f,"rear cutaway still preserves separate front wall");
        Check(Difference(clear,both,580,350)>.08f,"protected front wall does not expose scenery beyond it");
        frontWall.SetActive(false);wall.SetActive(false);
        // One combined building renderer/collider owns both faces. Removing the
        // rear obstruction must not remove its forward wall from the clear pass.
        var part=GameObject.CreatePrimitive(PrimitiveType.Cube);var cubeMesh=part.GetComponent<MeshFilter>().sharedMesh;Destroy(part);
        var building=Node("Verifier.SharedBuilding");building.layer=30;
        var sharedMesh=new Mesh();sharedMesh.CombineMeshes(new[]{
            new CombineInstance{mesh=cubeMesh,transform=Matrix4x4.TRS(new Vector3(0,1.4f,-1.25f),Quaternion.identity,new Vector3(9,4,.25f))},
            new CombineInstance{mesh=cubeMesh,transform=Matrix4x4.TRS(new Vector3(0,1.4f,.22f),Quaternion.identity,new Vector3(9,4,.2f))}},true,true);
        building.AddComponent<MeshFilter>().sharedMesh=sharedMesh;building.AddComponent<MeshRenderer>().sharedMaterial=originalMaterial;building.AddComponent<MeshCollider>().sharedMesh=sharedMesh;
        Physics.SyncTransforms();for(int i=0;i<12;i++)yield return null;
        var shared=CameraImage(camera,"camera-shared-building-protected.png");
        Check(Difference(blocked,shared,580,350)<.035f,"shared building mesh retains its forward wall when rear section is faded");
        building.SetActive(false);wall.SetActive(true);
        // Start the camera inside a solid collider; overlap detection must still work.
        wall.transform.position=fixedPosition;wall.transform.localScale=new Vector3(9,5,2);Physics.SyncTransforms();
        for(int i=0;i<3;i++)yield return null;
        var inside=CameraImage(camera,"camera-inside-wall.png");
        Check(Vector3.Distance(fixedPosition,(Vector3)Get("ThirdPerson","ViewPos"))<.001f,"camera inside collider remains at chosen position");
        Check(!wall.GetComponent<Renderer>().forceRenderingOff,"camera-inside pass restores occluder renderer");
        wall.SetActive(false);
        var data=new TerrainData{heightmapResolution=33,size=new Vector3(12,3,12)};
        var heights=new float[33,33];for(int z=0;z<33;z++)for(int x=0;x<33;x++){float worldZ=-6+z*12f/32;heights[z,x]=.85f*Mathf.Exp(-Mathf.Pow((worldZ+1.4f)/.65f,2));}
        data.SetHeights(0,0,heights);data.terrainLayers=new[]{new TerrainLayer{diffuseTexture=Texture2D.whiteTexture,tileSize=new Vector2(2,2)}};
        var terrainGo=Terrain.CreateTerrainGameObject(data);terrainGo.name="Verifier.Terrain";terrainGo.layer=30;terrainGo.transform.position=new Vector3(-6,0,-6);var terrain=terrainGo.GetComponent<Terrain>();Physics.SyncTransforms();
        for(int i=0;i<12;i++)yield return null;
        var terrainImage=CameraImage(camera,"camera-terrain-cutaway.png");
        Check(Vector3.Distance(fixedPosition,(Vector3)Get("ThirdPerson","ViewPos"))<.001f,"terrain does not pull camera forward");
        Check(terrain.drawHeightmap&&terrain.drawTreesAndFoliage,"terrain heightmap and foliage state restored after render");
        terrainGo.SetActive(false);
        var vehicle=Node("Verifier.Vehicle");vehicle.AddComponent<Rigidbody>().isKinematic=true;var drive=Node("DriveTrigger",vehicle.transform);player.transform.SetParent(drive.transform,true);incar.Fsm.SetState("InCar");
        var rear=Obstacle("VehicleRear",new Vector3(0,1.2f,-1.0f),new Vector3(2.2f,2.4f,.25f),new Color(.16f,.30f,.60f));rear.transform.SetParent(vehicle.transform,true);
        Physics.SyncTransforms();for(int i=0;i<12;i++)yield return null;
        var carImage=CameraImage(camera,"camera-vehicle-cutaway.png");var carPosition=(Vector3)Get("ThirdPerson","ViewPos");
        Check(Mathf.Abs((float)effect.GetType().GetField("_windowScale",Flags).GetValue(effect)-3f)<.03f,"vehicle obstruction uses threefold outline size");
        rear.transform.position=new Vector3(0,1.4f,-3f);Physics.SyncTransforms();for(int i=0;i<3;i++)yield return null;
        var carMoved=CameraImage(camera,"camera-vehicle-obstacle-moved.png");
        Check(Vector3.Distance(carPosition,(Vector3)Get("ThirdPerson","ViewPos"))<.001f,"vehicle camera position is independent of vehicle geometry");
        Check(!rear.GetComponent<Renderer>().forceRenderingOff,"vehicle renderers restored after cutaway pass");
        ((ConfigEntry<bool>)Get("Plugin","OcclusionPrototype")).Value=false;Call("ThirdPerson","Tick");
        Check((object)Get("OcclusionCutaway","_active")==null,"prototype toggle releases active cutaway");
        Call("ThirdPerson","Off");Invoke(body,"Destroy");Set("Runner","_body",null);
        var expected=Matrix4x4.Scale(new Vector3(1,1,-1))*Matrix4x4.TRS(eye.transform.position,eye.transform.rotation,Vector3.one).inverse;
        Check(Vector3.Distance(camera.worldToCameraMatrix.MultiplyPoint3x4(player.transform.position),expected.MultiplyPoint3x4(player.transform.position))<.001f,"leaving third person restores game's original view matrix");
        Check(Vector4.Distance(camera.cullingMatrix.GetRow(2),(camera.projectionMatrix*camera.worldToCameraMatrix).GetRow(2))<.001f,"leaving third person restores camera culling matrix");
        foreach(var image in new[]{clear,blocked,cut,roofImage,frontOpaque,frontCut,both,shared,inside,terrainImage,carImage,carMoved})Destroy(image);
    }
}
