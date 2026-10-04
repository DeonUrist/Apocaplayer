# FemalePlayer animation bundle (Unity 2020.3.49f1)

Builds `femaleplayer_anims.bundle`: humanoid animation clips that the mod plays on her body. Mixamo clips are made for the
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
- Save it into `Assets/Mixamo/` named **exactly** as in the first column (the file name becomes the clip name the mod looks for).

| File name | What | Suggested Mixamo search |
|---|---|---|
| **Idle.fbx** | standing, unarmed (required) | "Idle", "Breathing Idle", "Happy Idle" |
| **Walk.fbx** | walking forward (required) | "Walking", "Female Walk", "Catwalk Walk" |
| WalkBack.fbx | walking backwards | "Walking Backwards" |
| StrafeLeft.fbx / StrafeRight.fbx | side steps (one is enough: the other is mirrored) | "Left Strafe Walking" |
| Run.fbx | running | "Running", "Fast Run" |
| CrouchIdle.fbx / CrouchWalk.fbx | crouched | "Crouching Idle", "Crouched Walking" |
| CrouchWalkBack.fbx, CrouchStrafeLeft.fbx | crouched backwards / sideways (one strafe side is enough) | "Crouched Walking Backwards", "Crouched Sneaking Left" |
| RunStrafeLeft.fbx | running sideways (one side is enough; also RifleRunStrafeLeft.fbx) | "Left Strafe" (run), "Rifle Run Left" |
| RifleIdle.fbx, RifleWalk.fbx, RifleWalkBack.fbx, RifleStrafeLeft.fbx, RifleRun.fbx, RifleCrouchIdle.fbx, RifleCrouchWalk.fbx | the same set holding a rifle (used with rifles, shotguns, crossbow) | "Rifle Idle", "Rifle Walk", "Walking Backwards Rifle", "Strafe Left Rifle", "Rifle Run", "Crouch Rifle Idle", "Crouch Walk Rifle" (Mixamo's *Pro Rifle Pack* has all of them) |
| RifleCrouchWalkBack.fbx, RifleCrouchStrafeLeft.fbx | crouched with a rifle, backwards / sideways | "Crouch Walk Back Rifle", "Crouch Walk Left Rifle" |
| RifleFireWalk.fbx, RifleFireWalkBack.fbx, RifleFireStrafeLeft.fbx, RifleFireCrouchWalk.fbx, RifleFireCrouchWalkBack.fbx, RifleFireCrouchStrafeLeft.fbx | walking / crouching while firing a rifle (whole body; used while you hold fire and move) | "Walking Firing Rifle", "Walk Backward Firing Rifle", "Strafe Left Firing Rifle", "Crouch Walk Firing Rifle" |
| PistolIdle.fbx, PistolWalk.fbx, PistolWalkBack.fbx, PistolStrafeLeft.fbx, PistolRun.fbx, PistolRunStrafeLeft.fbx, PistolCrouchIdle.fbx, PistolCrouchWalk.fbx, PistolCrouchWalkBack.fbx, PistolCrouchStrafeLeft.fbx | the same set holding a pistol / revolver / SMG (one strafe side is enough; a missing back clip = the forward one reversed) | "Pistol Idle", "Pistol Walk", "Pistol Walk Backward", "Pistol Strafe", "Pistol Run", "Pistol Kneeling Idle" / "Crouch Pistol" (Mixamo's *Pistol Pack*) |
| PistolFireWalk.fbx, PistolFireWalkBack.fbx, PistolFireStrafeLeft.fbx, PistolFireCrouchWalk.fbx, PistolFireCrouchWalkBack.fbx, PistolFireCrouchStrafeLeft.fbx | walking / crouching while firing a pistol (whole body) | "Pistol Walk Shooting" … |
| CrouchPistolFire.fbx, PistolJump.fbx | firing while crouched (upper body) / jumping with a pistol | "Kneeling Pistol Shoot", "Pistol Jump" |
| CrouchRifleFire.fbx | firing while crouched (upper body, looped while you hold the button) | "Crouch Rifle Fire" |
| Kick.fbx | a front kick, played when you use the game's Kick key | "Front Kick", "Mma Kick" |
| Jump.fbx / RifleJump.fbx | jumping, unarmed / with a rifle (jump on the spot; the game lifts her) | "Jump", "Rifle Jump" |
| RifleAim.fbx | rifle raised to the shoulder (upper body) | "Rifle Aiming Idle" |
| RifleFire.fbx | firing a rifle, looped while you hold the mouse button | "Firing Rifle" |
| RifleReload.fbx | rifle reload, played when you press Reload | "Reloading", "Rifle Reload" |
| PistolAim.fbx / PistolFire.fbx / PistolReload.fbx | pistol / revolver | "Pistol Idle", "Shooting", "Pistol Reload" |
| Melee.fbx | a swing (knife, machete, wrench) | "Sword And Shield Slash", "Stabbing", "Punching" |
| MeleeCombo.fbx | two blows in a row (melee weapon swings chained one after the other) | "Sword And Shield Attack" |
| Punch1.fbx, Punch2.fbx (or Melee1/Melee2) | bare-hands punches, played by turns | "Punching", "Jab" |
| Throw.fbx | grenade (left hand: packed mirrored as "Throw") and blast lance (right hand: packed unmirrored as "ThrowRight") | "Throw", "Throw Grenade" |

Everything except **Idle** and **Walk** is optional: whatever is missing falls back to the game's raider clips or the procedural motion.
Only the third-person body uses these. The first-person arms keep the game's own reload/shot animations.

## Build
Menu **FemalePlayer → Build animation bundle**. It sets every FBX to Humanoid / in place / looping (one-shots: Reload, Melee, Throw),
mirrors a missing strafe side, packs `Build/femaleplayer_anims.bundle`, and copies it into
`E:\SteamLibrary\steamapps\common\Apocalypter\BepInEx\plugins\FemalePlayer\Models\` when that folder exists. Restart the game.
The log says `Animation bundle: N clips (...)` and `Animations from the bundle: unarmed ...; rifle ...`.

With bundle clips she is no longer mirrored (Mixamo holds guns right-handed), and the raider gun models are moved to her right hand.

Speeds (hidden settings in Plugin.cs): BundleWalkSpeed 1.4, BundleRunSpeed 3.8, BundleCrouchSpeed 1.0 m/s = the ground speed at which a clip plays at
normal speed (the game walks at 2 m/s and runs at 5).

Mixamo's terms allow using the animations in your own projects but not handing out the raw files; keep the FBX files out of git
(`.gitignore` does) and ship only the built bundle with the mod.
