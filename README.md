# Apocaplayer

BepInEx 5 mod for **Apocalypter**: see your character - a full body in first person, a third-person camera on foot and in cars,
and a choice of **Female** (her own model) or **Male** (the game's own player man), with the same animations and features for both.

- **First person** – her body under the camera: legs, hips and torso when you look down, her shadow on the ground.
  The game's own first-person arms are kept (every reload, melee swing and item animation still works) but wear her
  skin and black gloves; the kick leg wears her boot.
- **Third person on foot** – the game's *Change Camera* key (the one that switches views in a car) also works on foot:
  a camera over her right shoulder; the mouse wheel zooms (saved separately on foot and in cars); holding Left Alt (`RebindObserving`) orbits the camera around her (the middle mouse button too with `EnableMMB` - off by default, the game rotates held items with it). Shots and picks still come from the first-person eye; the view is turned so the screen centre is exactly where they land, so aim with the crosshair.
  It works in cars too: dashboard clicks are blocked in third person so switches do not capture shooting or camera zoom.
  Use the vehicle hotkeys below; Exit (F) at the door still works, and you stay in third person when you get out.
  On death, animation stops and a jointed physics ragdoll falls from the current pose; the third-person camera follows the body.
  Her body is animated with the game's own humanoid raider clips (idle, run, rifle/pistol aim, melee swing) plus a
  procedural walk, strafe, crouch, prone and aim-pitch; the gun in her hand is the NPC model of the weapon you hold
  (Flexa's AKM, Sprokka's pipe pistol, Lugnut's shotgun …) at the spot the raiders hold it.
- **Cars** – she sits in the driver's seat instead of the game's seated man (car third-person camera), and in first
  person you see her legs and arms on the wheel.

The female model is **Player_female** (made from the Flexa model, rigged to Flexa's 22-bone mixamo skeleton).

Equipped gear appears on both character models: the worn backpack on the back at 0.8 scale, rotated 90 degrees clockwise around
the vertical axis and fitted close to the body; the first two shotguns/rifles/automatic rifles crossed close against the bare back,
or vertical beside an equipped backpack with muzzles down and magazines facing backward;
the first two pistols/SMGs at the right and left thighs; the first two machetes/knives/shivs at opposite belt sides;
and equipped binoculars behind the belt. Items are chosen in slot order, including slots 4–6 supplied by the optional inventory mod.
Drawing an item hides only its own mount and reserves that place until it is sheathed; a third item is never promoted to fill it.
Models are visual copies of the actual inventory items. Equipping, removing, and replacing gear updates them automatically.
`EquipmentInventory.cs` reads Apocapocket's logical inventory (also accepts the Apocainventory namespace with the same contract),
so drawing an extra-slot weapon into a temporary vanilla slot does not change the mounts. No inventory mod is required for slots 1–3.

## Install

Copy the `Apocaplayer` folder into `BepInEx\plugins\`:

```
BepInEx\plugins\Apocaplayer\Apocaplayer.dll
BepInEx\plugins\Apocaplayer\Models\Player_female.glb
BepInEx\plugins\Apocaplayer\Models\Player_female.png
BepInEx\plugins\Apocaplayer\Models\Player2_female_arms.png
BepInEx\plugins\Apocaplayer\Models\Player_male.glb
BepInEx\plugins\Apocaplayer\Models\Player_male.png
BepInEx\plugins\Apocaplayer\Models\player_character_2_UI.png
BepInEx\plugins\Apocaplayer\Models\apocaplayer_anims.bundle   (optional: the Mixamo animations, built with UnityAnims/)
BepInEx\plugins\Apocaplayer\icon.png                          (Apocasetter's Mods menu)
```

## Settings (`BepInEx\config\com.denis.apocalypter.apocaplayer.cfg`, also in the Apocasetter Mods menu)

| Section | Key | Default | |
|---|---|---|---|
| General | Enabled | true | Off = the game's own player (arms, driver, TAB picture) comes back at once |
| General | Character | Female | Female: her body, her arms and gloves in first person, her TAB picture. Male: the game's own man (with his hair, beard, bags) - in third person, first person and the driver's seat; the game's arms and TAB picture. Animations, weapon poses and everything else are the same |
| VEHICLE | VehicleHotkeyHint | true | Show hotkeys on the left while driving; hiding them keeps the controls working |
| VEHICLE | VehicleStatusHint | true | Top-right icons: key when ignition is off, (P) while handbrake is engaged, cassette with 0.0–1.0 volume while music is playing (including muted playback) |
| VEHICLE | IgnitionKey | E | Start / stop engine with the game's sounds; hint changes from "Ignition" to "Ignition Stop". Previously rebound General/IgnitionKey is migrated |
| VEHICLE | HeadlightsKey | X | Toggle headlights using the game's switch; replaces the vanilla headlight binding while driving |
| VEHICLE | CassetteKey | Z | Start / stop cassette. Starting at zero sets volume to 0.5; a nonzero volume is preserved |
| VEHICLE | VolumeDownKey | Minus | Reduce cassette volume by 0.1, clamped at 0; also accepts numpad minus |
| VEHICLE | VolumeUpKey | Equals | Increase cassette volume by 0.1, clamped at 1; + on the main keyboard (= physical key) or numpad plus |
| General | BodyFirstPerson | false | First person: see her body (legs and torso when you look down, her shadow, her body in the driver's seat); off = only the first-person arms |
| General | EnableMMB | false | Third person: the middle mouse button also orbits the camera around her (off by default: the game rotates a held item with it) |
| General | RebindObserving | LeftAlt | Third person: hold this key to orbit the camera around her, back behind her on release; None = no key |
| Debug | WeaponAdjustment | false | Third person: numpad 8/2 6/4 7/1 move the weapon in her hand, 5 move/rotate, 9/3 pick the animation, - / * delete/copy/paste; saved to `config/Apocaplayer/weapon-poses.txt` (overrides the built-in poses) |
| Debug | ToggleMiddleMouse | false | Third person: off = hold the observing key / middle mouse button to orbit around her, back behind her on release; on = a press turns orbiting on / off |
| Debug | VerboseLog | false | Detailed log |

Throws from the driver's seat (blast lance, quick grenade) work like guns there: her chest turns to the aim (a learned per-clip
correction puts her throwing hand on the aim line at the release), past 95° to a side she turns round and crouches; the throw clip and
its timing are the same as on foot, played once. A blast lance thrown from a car ignores that car's colliders (`Projectiles.cs`), so it
doesn't blow up on your own doors and windows.

Third person, right mouse button (Aim Down Sights) with a non-scoped gun: the view zooms in toward the crosshair (narrower field of view,
camera a little closer) and the crosshair stays - the game's own sights are on the first-person gun, which isn't drawn in third person.
Scoped guns keep the game's scope.

Transitions: a new animation always starts at once at its own time and speed, and the previous pose fades out under it - upper-body
clips (reload, aim, fire, throw, punches, swings) cross-fade in 0.08-0.15 s, switching between unarmed / rifle / pistol blends her
body over 0.2 s (the gun is in her hand at its correct grip from the first frame), getting into / out of the driver's seat eases over
0.3 s and turning off the seat in a car over 0.25 s. Nothing is delayed or made longer.
Aiming down sights in first person (right mouse button) moves the gun from the hip to the sights and back over 0.2 s
(easing out) instead of jumping there; hidden setting `[Camera] AimDownSightsTime`, 0 = the game's instant move.
In the driver's seat she sits 5 cm lower than the game's man (hands at the steering wheel). First person in a car with her body shown
(`BodyFirstPerson`), clicks on the ignition, lights and switches are cast from the eye the picture is drawn from, so the cassette player
no longer gets in the way.
Binoculars raised in third person show the game's own binocular view (full zoom, from her eyes, her body not in the way);
the camera goes back behind her when they are lowered.

Third person on foot, picking: when the game's eye ray misses (common behind the shoulder with small items), the item, part or switch nearest
the cursor ON SCREEN is picked - within 6 % of the screen height, visible from the camera, within the game's own reach from her eye
(`PickAssist.cs`, a Harmony postfix on PlayMaker's `ActionHelpers.DoMousePick`). Screen-space, so it works the same at every zoom.


Everything else is fixed: her body in first person, her arms, the driver and TAB picture, the third-person camera (Change Camera key, on
foot and in cars) and the camera offsets are always on, as `H(...)` entries in `Plugin.cs` (change one to `Config.Bind(...)` to expose
it). The mouse-wheel camera distances are remembered in `config/Apocaplayer/camera.cfg`.

## The male body

`Models/Player_male.glb` + `Models/Player_male.png` = the game's own player man (mesh `Player2` of the cars' `PlayerModel_Sit`, his
`PlayerHair.002`, `PlayerBeard.003`, `bag1`, `bag2`, `pouch1.001`; texture `Player2`), re-rigged onto Flexa's skeleton by
`tools/bake_male.py`: each of his bones follows the mixamo bone it maps to (the same pairs and swing as the car-seat retarget), the
rigid extras are baked in on his head / spine bone, and the beard (own texture `hair3`, not exported) takes one hair-coloured texel
of the `Player2` atlas. Inputs: `_export/Player2.json`, `_export/extras.json`, `_export/prefabs.json` (UnityPy dumps), `Player_female.glb`
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

`UnityAnims/` is a tiny Unity 2020.3.49f1 project: drop Mixamo FBX files in, run *Apocaplayer → Build animation bundle*, and it
writes `Models/apocaplayer_anims.bundle`. With at least `Idle` + `Walk` in it, her third-person body uses those clips (walk,
back, strafe, run, crouch, a rifle set, aim/fire/reload, pistol, melee, throw) instead of the raider clips and the procedural
walk, unmirrored, with the raider gun models moved to her right hand. See `UnityAnims/README.md` for the file names.

Blast lance: held in her right hand (its world model, placed like a machete); its throw plays `ThrowRight` (right-handed;
the grenade's `Throw` is the left-handed one) timed so her release comes when the game lets the lance go.
Melee weapons: a swing plays `Melee` timed to the first-person swing; swings chained straight after it (held button, fast clicks)
play `MeleeCombo`'s two blows by turns. Bare hands: `Punch1` / `Punch2` (or `Melee1` / `Melee2`) by turns. A strike follows the game's
`[Attack]` FSM (the hit lands the moment the swing starts): the clip's wind-up in 0.08 s, then the blow and the way back over the swing's
cycle (0.35 s hands, 0.4 s machete). The blow times are read off the clips (`Body.cs`, `Punches` / `Combo`).

## Build

Vehicle HUD assets live in `Models/Hud/`; `design/vehicle-hud.png` is the icon mockup and `tools/build_hud_icons.py` rebuilds the assets.
`Ragdoll.cs` uses eleven jointed physics proxies in world space so both character models and mirrored fallback animations can fall naturally.
`VehicleHud.cs` draws the optional status icons; `Car.cs` drives the game's switch FSMs and synchronizes hotkey volume with the dashboard knob.

`./build.sh` (mcs, against the game's own DLLs; `MANAGED=… BEPCORE=…` to override the paths) or `dotnet build`
with `Apocaplayer.csproj` (deploys DLL + Models to the game). `tools/` holds the Python scripts used to read the game
assets (UnityPy) and to bake/verify the arm texture and the car-seat retarget; they are not part of the build.
