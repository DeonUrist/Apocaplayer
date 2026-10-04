# FemalePlayer

BepInEx 5 mod for **Apocalypter**: play as a woman.

- **First person** – her body under the camera: legs, hips and torso when you look down, her shadow on the ground.
  The game's own first-person arms are kept (every reload, melee swing and item animation still works) but wear her
  skin and black gloves; the kick leg wears her boot.
- **Third person on foot** – the game's *Change Camera* key (the one that switches views in a car) also works on foot:
  a camera over her right shoulder; the mouse wheel zooms (saved separately on foot and in cars); the middle mouse button orbits around her. Shots and picks still come from the first-person eye; the view is turned so the screen centre is exactly where they land, so aim with the crosshair.
  It works in cars too (instead of the game's own car view, where nothing in the car can be used): ignition, cassette player and Exit (F) at the door work as in first person, and you stay in third person when you get out.
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
| General | Enabled | true | Off = the game's own player (arms, driver, TAB picture) comes back at once |
| General | Character | Female | Female: her body, her arms and gloves in first person, her TAB picture. Male: the game's own man (with his hair, beard, bags) - in third person, first person and the driver's seat; the game's arms and TAB picture. Animations, weapon poses and everything else are the same |
| General | IgnitionKey | E | In the driver's seat: one press turns the key and starts the engine, another stops it (the game's own stop and its sounds); "E - Start / Ignition" shows on the left, in the game's hint font, while the engine is off |
| Debug | WeaponAdjustment | false | Third person: numpad 8/2 6/4 7/1 move the weapon in her hand, 5 move/rotate, 9/3 pick the animation, - / * delete/copy/paste; saved to `config/FemalePlayer/weapon-poses.txt` (overrides the built-in poses) |
| Debug | ToggleMiddleMouse | false | Third person: off = hold the middle mouse button to orbit around her, back behind her on release; on = a click turns orbiting on / off |
| Debug | VerboseLog | false | Detailed log |

Everything else is fixed: her body in first person, her arms, the driver and TAB picture, the third-person camera (Change Camera key, on
foot and in cars) and the camera offsets are always on, as `H(...)` entries in `Plugin.cs` (change one to `Config.Bind(...)` to expose
it). The mouse-wheel camera distances are remembered in `config/FemalePlayer/camera.cfg`.

## The male body

`Models/Player_male.glb` + `Models/Player_male.png` = the game's own player man (mesh `Player2` of the cars' `PlayerModel_Sit`, his
`PlayerHair.002`, `PlayerBeard.003`, `bag1`, `bag2`, `pouch1.001`; texture `Player2`), re-rigged onto Flexa's skeleton by
`tools/bake_male.py`: each of his bones follows the mixamo bone it maps to (the same pairs and swing as the car-seat retarget), the
rigid extras are baked in on his head / spine bone, and the beard (own texture `hair3`, not exported) takes one hair-coloured texel
of the `Player2` atlas. Inputs: `_export/Player2.json`, `_export/extras.json`, `_export/prefabs.json` (UnityPy dumps), `Boss_lady.glb`
(for the skeleton nodes).

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

Blast lance: held in her right hand (its world model, placed like a machete); its throw plays `ThrowRight` (right-handed;
the grenade's `Throw` is the left-handed one) timed so her release comes when the game lets the lance go.
Melee weapons: a swing plays `Melee` timed to the first-person swing; swings chained straight after it (held button, fast clicks)
play `MeleeCombo`'s two blows by turns. Bare hands: `Punch1` / `Punch2` (or `Melee1` / `Melee2`) by turns. A strike follows the game's
`[Attack]` FSM (the hit lands the moment the swing starts): the clip's wind-up in 0.08 s, then the blow and the way back over the swing's
cycle (0.35 s hands, 0.4 s machete). The blow times are read off the clips (`Body.cs`, `Punches` / `Combo`).

## Build

`./build.sh` (mcs, against the game's own DLLs; `MANAGED=… BEPCORE=…` to override the paths) or `dotnet build`
with `FemalePlayer.csproj` (deploys DLL + Models to the game). `tools/` holds the Python scripts used to read the game
assets (UnityPy) and to bake/verify the arm texture and the car-seat retarget; they are not part of the build.
