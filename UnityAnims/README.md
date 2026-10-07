# Apocaplayer animation bundle (Unity 2020.3.49f1)

Builds `apocaplayer_anims.bundle`: humanoid animation clips that the mod plays on her body. Mixamo clips are made for the
same `mixamorig` skeleton she has, and as Humanoid clips they fit her through the Animator.

## One-time setup
1. Install **Unity Hub**, then the editor **2020.3.49f1** (Hub → Installs → Install Editor → Archive → "download archive" page → 2020.x → 2020.3.49 → Unity Hub button). Exactly this version — the game is built with it, and bundles from other
   versions may not load. Only the *Windows Build Support (Mono)* module is needed (it comes with the editor).
2. Hub → Projects → Add → pick this `UnityAnims` folder. The first time it opens it creates its Library (a few minutes).

## Getting the clips (mixamo.com, free Adobe account)
- Upload `Models/Player_female.glb` converted to FBX (Blender: File → Export → FBX) as your character, or just use any Mixamo character (Y Bot) —
  either works, the clips are retargeted in the game.
- For each clip: **Download → Format: FBX for Unity (.fbx), Skin: With Skin, Frames per second: 30, Keyframe Reduction: none**.
  For walk/run/strafe/crouch-walk leave **In Place unticked**: the builder keeps her on the spot anyway, and the clip's real walking speed lets the mod play it in step with you (In Place clips use the BundleWalkSpeed/BundleRunSpeed guesses).
- Save it into `Assets/Mixamo/` named **exactly** as in the first column (the file name becomes the clip name the mod looks for). Naming: `[Rifle|Pistol][Crouch]Action[Direction]` - e.g. `RifleCrouchWalkBackLeft`, `RifleTurnRight`, `PistolFire`; the builder fixes wrong capitals and `RunLeftStrafe` word order.

| File name | What | Suggested Mixamo search |
|---|---|---|
| **The Rifle pack = the legs of every weapon** (2.0) | | Mixamo's *Pro Rifle Pack*, X Bot, "In Place" unticked |
| **RifleIdle.fbx**, RifleAim.fbx, RifleCrouchIdle.fbx, RifleCrouchAim.fbx | standing / aiming / crouched / crouched aiming (required: RifleIdle, RifleWalk) | "idle", "idle aiming", "idle crouching", "idle crouching aiming" |
| **RifleWalk.fbx**, RifleWalkBack.fbx, RifleStrafeLeft.fbx, RifleStrafeRight.fbx, RifleWalkForwardLeft.fbx, RifleWalkForwardRight.fbx, RifleWalkBackLeft.fbx, RifleWalkBackRight.fbx | walking, 8 directions | "walk forward / backward / left / right / forward left ..." |
| RifleRun.fbx, RifleRunBack.fbx, RifleRunStrafeLeft/Right.fbx, RifleRunForwardLeft/Right.fbx, RifleRunBackLeft/Right.fbx | running, 8 directions | "run forward ..." |
| RifleSprint.fbx, RifleSprintBack.fbx, RifleSprintStrafeLeft/Right.fbx, RifleSprintForwardLeft/Right.fbx, RifleSprintBackLeft/Right.fbx | sprinting, 8 directions (used above SprintFrom m/s, the game's run) | "sprint forward ..." |
| RifleCrouchWalk.fbx, RifleCrouchWalkBack.fbx, RifleCrouchStrafeLeft/Right.fbx, RifleCrouchWalkForwardLeft/Right.fbx, RifleCrouchWalkBackLeft/Right.fbx | crouched walking, 8 directions | "walk crouching forward ..." |
| RifleTurnLeft.fbx, RifleTurnRight.fbx, RifleCrouchTurnLeft.fbx, RifleCrouchTurnRight.fbx | turning in place (played when you turn a lot standing still) | "turn 90 left/right", "crouching turn 90 left/right" |
| RifleJumpUp.fbx, RifleJumpLoop.fbx, RifleJumpDown.fbx | the rifle jump: take-off, in the air, landing | "jump up", "jump loop", "jump down" |
| RifleFire.fbx, RifleCrouchFire.fbx, RifleReload.fbx | firing standing / crouched (else the aim clips), the reload | "Firing Rifle", "Crouch Rifle Fire", "Rifle Reload" |
| **Pistol: only the hands** - PistolIdle.fbx (gun lowered), PistolFire.fbx (aim = its first frame, shooting = playing), PistolRun.fbx (its upper body while running), PistolReload.fbx, PistolJump.fbx | the upper body over the rifle legs | "Pistol Idle", "Shooting", "Pistol Run", "Pistol Reload", "Pistol Jump" |
| **Bare hands / melee: only the hands** - Idle.fbx, Walk.fbx, WalkBack.fbx, Run.fbx, CrouchIdle.fbx | the upper body over the rifle legs (Walk / Run in step with them) | "Idle", "Walking", "Walking Backwards", "Running", "Crouching Idle" |
| Jump.fbx, Kick.fbx | jumping (bare hands), the Kick key | "Jump", "Front Kick" |
| Melee.fbx, MeleeCombo.fbx, Punch.fbx (Punch1/Punch2) | a swing, two chained blows, bare-hand punches (Punch = eight blows in one clip) | "Sword And Shield Slash", "Sword And Shield Attack", "Punching" |
| Throw.fbx | grenade (left hand: packed mirrored as "Throw") and blast lance (right hand: unmirrored as "ThrowRight") | "Throw" |

Everything except **RifleIdle** and **RifleWalk** is optional: a missing direction uses its neighbour, a missing tier the one below (sprint -> run -> walk),
a missing back clip the forward one reversed; what is missing of the uppers falls back to the rifle clips' own hands. Nothing is mirrored any more
(a mirrored strafe put the gun in the other hand). The first-person arms keep the game's own reload/shot animations.

## Build
Menu **Apocaplayer → Build animation bundle**. It sets every FBX to Humanoid / in place / looping (one-shots: Reload, Melee, Throw, Kick, jumps, turns),
packs `Build/apocaplayer_anims.bundle`, and copies it into
`E:\SteamLibrary\steamapps\common\Apocalypter\BepInEx\plugins\Apocaplayer\Models\` when that folder exists. Restart the game.
The log says `Animation bundle: N clips (...)` and `Animations from the bundle: unarmed ...; rifle ...`.

With bundle clips she is no longer mirrored (Mixamo holds guns right-handed), and the raider gun models are moved to her right hand.

Speeds (hidden settings in Plugin.cs): BundleWalkSpeed 1.4, BundleRunSpeed 3.8, BundleCrouchSpeed 1.0 m/s = the ground speed at which a clip plays at
normal speed (the game walks at 2 m/s and runs at 5).

Mixamo's terms allow using the animations in your own projects but not handing out the raw files; keep the FBX files out of git
(`.gitignore` does) and ship only the built bundle with the mod.
