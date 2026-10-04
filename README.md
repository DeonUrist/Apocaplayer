# FemalePlayer

BepInEx 5 mod for **Apocalypter**: play as a woman.

- **First person** – her body under the camera: legs, hips and torso when you look down, her shadow on the ground.
  The game's own first-person arms are kept (every reload, melee swing and item animation still works) but wear her
  skin and black gloves; the kick leg wears her boot.
- **Third person on foot** – the game's *Change Camera* key (the one that switches views in a car) also works on foot:
  a camera over her right shoulder; click the middle mouse button to orbit around her (click again to stop). Shots and picks still come from the first-person eye; the view is turned so the screen centre is exactly where they land, so aim with the crosshair.
  Getting into a car in third person switches the car to its own third-person view; **Use (F)** in the car's third-person view gets you out (the game only allows it in first person) and keeps third person on foot.
  Her body is animated with the game's own humanoid raider clips (idle, run, rifle/pistol aim, melee swing) plus a
  procedural walk, strafe, crouch, prone and aim-pitch; the gun in her hand is the NPC model of the weapon you hold
  (Flexa's AKM, Sprokka's pipe pistol, Lugnut's shotgun …) at the spot the raiders hold it.
- **Cars** – she sits in the driver's seat instead of the game's seated man (car third-person camera), and in first
  person you see her legs and arms on the wheel.

The default model is **Boss_lady** (made from the Flexa model, rigged to Flexa's 22-bone mixamo skeleton).

## Install

Copy the `FemalePlayer` folder into `BepInEx\plugins\`:

```
BepInEx\plugins\FemalePlayer\FemalePlayer.dll
BepInEx\plugins\FemalePlayer\Models\Boss_lady.glb
BepInEx\plugins\FemalePlayer\Models\Boss_lady.png
BepInEx\plugins\FemalePlayer\Models\Player2_female_arms.png
```

## Settings (`BepInEx\config\com.denis.apocalypter.femaleplayer.cfg`, also in the Apocasetter Mods menu)

| Section | Key | Default | |
|---|---|---|---|
| General | Enabled | true | Off = the game's own player (arms, driver) comes back at once |
| General | BodyFirstPerson | true | Her legs/torso and shadow in first person |
| General | FemaleArms | true | Her arms and gloves on the first-person animations |
| General | ReplaceDriver | true | She sits in the driver's seat |
| General | ThirdPersonOnFoot | true | Change Camera works on foot |
| General | RightHanded | true | Mirror her body so the raider gun animations hold the gun in her right hand |
| Weapon grip | WeaponAdjustment | false | In third person: numpad 8/2, 6/4, 7/1 move the weapon (Numpad 5: rotate instead) for the animation she is in - every clip has its own grip (StrafeLeft and the mirrored StrafeRight separately, every crouch and fire variant too); Numpad 9/3 preview the animations; Numpad - clears one; Numpad / copies a grip, Numpad * pastes it. Grips go to config/FemalePlayer/weapon-grips.cfg |
| Weapon grip | AutoGrip | true | With the Mixamo clips: the rifle's grip is taken once from the rifle idle pose (barrel from right hand to left hand) and then kept fixed in her hand |
| Weapon grip | akms, m16a1, … (one per weapon) | 0, 0, 0, 0, 0, 0 | Base grip in her right hand (hand axes, cm / degrees), used by every animation without its own grip |
| Camera | FirstPersonBodyBack | 0.08 | First person on foot: how far (m) her head sits behind the camera; looking down you see her body up to the collar |
| Camera | FirstPersonWeaponBack / FirstPersonChestLean | 0.08 / 20 | First person with a weapon drawn: her body moves back (m) and her chest bends back (degrees) so the game's arms don't sink into her chest |
| Camera | CarCameraForward | 0.12 | Driving in first person: the view is drawn this many metres further forward so her body doesn't block it (only the picture moves) |
| Debug | VerboseLog | false | Detailed log |

Fixed values (model/texture paths, clip names, walk/crouch amounts, camera offsets) are in `Plugin.cs` as `H(...)`
entries; change one to `Config.Bind(...)` to expose it.

## Using another model

Any `.glb` rigged to Flexa's skeleton works (same 22 `mixamorig:` bones, unmoved — the log reports the skeleton
offset; above 5 cm she will look deformed). Export from Blender with *Skinning* on, ≤ 4 weights per vertex. Point the
hidden `Model`/`Texture` entries at it. The arms texture is a separate atlas (see *How it works*); re-bake it with
`tools/bake.py` for a new outfit.

## How it works

- **Body** (`Body.cs`): a clone of Flexa's `Anim` object (Animator, avatar `enemy_1_IdleAvatar`, the skeleton, the
  skinned renderer), made while inactive and stripped of props, colliders and FSMs, with our mesh. A `PlayableGraph`
  drives the Animator: idle/run mixer + an upper-body layer (AvatarMask: body, head, arms) for the weapon clip.
  `LateUpdate` adds the procedural layers in the Anim object's own (unmirrored) space, so the mirror on the root is safe.
  Three mesh variants from one file (`Model.cs`): full, no head (car first person), no head + no arms (first person on foot);
  a second renderer draws the full body shadows-only in first person.
- **Arms** (`Arms.cs`): every first-person arm (`player_arm_left` / `player_arm_left.002`, 8 bones) and the kick leg use
  material `Player2` — the same atlas as the seated driver. Those renderers get a copy of the material with
  `Player2_female_arms.png`: the arm/leg UV islands re-painted from her texture (`tools/bake.py`: each texel of an arm
  triangle → its point on the game arm in bind pose → same place along her arm bone → nearest point on her mesh → her
  texel), everything else untouched.
- **Car** (`CarSeat.cs`): each car has a `PlayerModel_Sit` (27-bone `Player2` driver in a fixed driving pose, plus hair,
  beard, bags), toggled by the car's Camera FSM. Its renderers are disabled and its pose is copied onto her skeleton every
  frame: `her.rotation = its.rotation * Δ`, hips at `itsSpine1 * offset`. Δ comes from the two bind poses (both T-poses;
  Player2 mesh space → Flexa mesh space is `(x, y, z) → (x, z, -y)`) with a swing so every bone points along its partner
  (`tools/retarget.py` verifies it by skinning her into the seat offline).
- **Third person** (`ThirdPerson.cs`): `Camera.onPreCull` overrides the PlayerCamera's `worldToCameraMatrix` (sphere-cast
  against walls); the transform is never moved, so every game raycast is unchanged. First-person renderers under the
  PlayerCamera get `forceRenderingOff` while the view is behind her.
- **Weapon in hand** (`Props.cs`): NPC weapon props are found on the prefabs' `mixamorig:LeftHand` (guns) /
  `RightHand` (blades) and copied (render-only) onto her hand at the same local pose.

## What the game does not have (and how to add it)

The game ships only these humanoid clips: `merchant_idle`, `enemy_1_idle`, `enemy_1_run`, `enemy_1_attack`,
`enemy_machinegun`, `sprokka_shoot`, `spanna_two_handed_attack`, `zombie_idle/run/attack`. No walk, strafe, crouch, prone,
sit/drive or jump — those are procedural here (walk cycle, strafe twist, squat, lying flat) or copied (car seat).
Better animations need either an AssetBundle built with Unity 2020.3.49f1 (humanoid clips retarget to her automatically)
or a glTF animation sampler in the plugin (not written yet).

## Better animations (optional bundle)

`UnityAnims/` is a tiny Unity 2020.3.49f1 project: drop Mixamo FBX files in, run *FemalePlayer → Build animation bundle*, and it
writes `Models/femaleplayer_anims.bundle`. With at least `Idle` + `Walk` in it, her third-person body uses those clips (walk,
back, strafe, run, crouch, a rifle set, aim/fire/reload, pistol, melee, throw) instead of the raider clips and the procedural
walk, unmirrored, with the raider gun models moved to her right hand. See `UnityAnims/README.md` for the file names.

## Build

`./build.sh` (mcs, against the game's own DLLs; `MANAGED=… BEPCORE=…` to override the paths) or `dotnet build`
with `FemalePlayer.csproj` (deploys DLL + Models to the game). `tools/` holds the Python scripts used to read the game
assets (UnityPy) and to bake/verify the arm texture and the car-seat retarget; they are not part of the build.
