#!/bin/sh
# Builds Apocaplayer.dll against the game's own libraries. Usage: ./build.sh [out.dll]
M=${MANAGED:-/e/SteamLibrary/steamapps/common/Apocalypter/Apocalypter_Data/Managed}; B=${BEPCORE:-/e/SteamLibrary/steamapps/common/Apocalypter/BepInEx/core}
mcs -nostdlib -noconfig -target:library -langversion:latest -optimize+ -out:${1:-Apocaplayer.dll} \
  -r:$M/mscorlib.dll -r:$M/System.dll -r:$M/System.Core.dll -r:$M/netstandard.dll \
  -r:$B/BepInEx.dll -r:$B/0Harmony.dll \
  -r:$M/UnityEngine.dll -r:$M/UnityEngine.CoreModule.dll -r:$M/UnityEngine.PhysicsModule.dll -r:$M/UnityEngine.TerrainModule.dll -r:$M/UnityEngine.AudioModule.dll -r:$M/UnityEngine.ImageConversionModule.dll \
  -r:$M/UnityEngine.AnimationModule.dll -r:$M/UnityEngine.AssetBundleModule.dll -r:$M/UnityEngine.DirectorModule.dll -r:$M/UnityEngine.InputLegacyModule.dll -r:$M/UnityEngine.IMGUIModule.dll -r:$M/UnityEngine.TextRenderingModule.dll -r:$M/UnityEngine.UI.dll -r:$M/UnityEngine.UIModule.dll \
  -r:$M/PlayMaker.dll -r:$M/Assembly-CSharp.dll \
  Plugin.cs Runner.cs Game.cs Model.cs Body.cs Loco.cs LocoPlan.cs CharPlan.cs ModAPI.cs ModCharacter.cs AimLift.cs Ragdoll.cs Equipment.cs EquipmentInventory.cs RustlinerDoors.cs AutoStepUp.cs FemalePain.cs CarSeat.cs Arms.cs Props.cs ThirdPerson.cs OcclusionCutaway.cs Car.cs VehicleHud.cs Perf.cs Cave.cs InventoryModel.cs PickAssist.cs Projectiles.cs AimTransition.cs Anims.cs GunPose.cs BuiltinPoses.cs Gltf.cs Json.cs Bindposes.cs
