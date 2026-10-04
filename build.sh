#!/bin/sh
# Builds FemalePlayer.dll against the game's own libraries. Usage: ./build.sh [out.dll]
M=${MANAGED:-/e/SteamLibrary/steamapps/common/Apocalypter/Apocalypter_Data/Managed}; B=${BEPCORE:-/e/SteamLibrary/steamapps/common/Apocalypter/BepInEx/core}
mcs -nostdlib -noconfig -target:library -langversion:latest -optimize+ -out:${1:-FemalePlayer.dll} \
  -r:$M/mscorlib.dll -r:$M/System.dll -r:$M/System.Core.dll -r:$M/netstandard.dll \
  -r:$B/BepInEx.dll -r:$B/0Harmony.dll \
  -r:$M/UnityEngine.dll -r:$M/UnityEngine.CoreModule.dll -r:$M/UnityEngine.PhysicsModule.dll -r:$M/UnityEngine.ImageConversionModule.dll \
  -r:$M/UnityEngine.AnimationModule.dll -r:$M/UnityEngine.AssetBundleModule.dll -r:$M/UnityEngine.DirectorModule.dll -r:$M/UnityEngine.InputLegacyModule.dll -r:$M/UnityEngine.IMGUIModule.dll -r:$M/UnityEngine.TextRenderingModule.dll -r:$M/UnityEngine.UI.dll -r:$M/UnityEngine.UIModule.dll \
  -r:$M/PlayMaker.dll -r:$M/Assembly-CSharp.dll \
  Plugin.cs Runner.cs Game.cs Model.cs Body.cs CarSeat.cs Arms.cs Props.cs ThirdPerson.cs Car.cs Anims.cs GunPose.cs Gltf.cs Json.cs Bindposes.cs
