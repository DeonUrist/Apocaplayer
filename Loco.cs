using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Apocaplayer
{
    // ---------------------------------------------------------------- Mixamo mode, 2.0: ONE lower body, the Rifle pack
    // Every kind of weapon walks on the Rifle pack's legs: idle, 8 directions × walk / run / sprint, crouch idle, 8 crouched walks, turns in
    // place and the jump (up / loop / down), blended by the body's real direction and speed (the base layer, below). What the hands do comes
    // from an UPPER source: with a rifle the pack's own clips (nothing to override) until she aims, fires or reloads (RifleAim / RifleFire /
    // RifleCrouchAim / RifleCrouchFire / RifleReload); with a pistol always PistolIdle (lowered), PistolRun (running), PistolFire (aiming:
    // its first frame; shooting: playing), PistolReload; bare hands / melee / throw: Idle, Walk / WalkBack, Run, CrouchIdle. An avatar mask can't
    // put a standing clip's upper body on crouched legs (the Body part carries the hips), so the upper source is a second, invisible copy of her
    // skeleton (the UpperRig) that plays the clip on its own graph; her spine takes the copy's orientation relative to the character, every bone
    // above it the copy's local rotation. Walking uppers are kept in step with the legs.
    // Weapon poses (GunPose) are per CLIP NAME: whatever clip moves her hands this frame, weighted - so every rifle clip and every pistol clip
    // can be tuned in WeaponAdjustment (numpad 9/3 cycles them all).
    internal sealed partial class Body
    {
        // ---- base layer slots
        private const int B_IDLE = 0, B_WALK = 1, B_RUN = 9, B_SPRINT = 17, B_CIDLE = 25, B_CWALK = 26, BN = 34;
        private static readonly string[] DirWord = { "", "ForwardRight", "Right", "BackRight", "Back", "BackLeft", "Left", "ForwardLeft" };   // 0 = forward, clockwise
        private static string DirName(string tier, string strafe, int d)
        {
            if (d == 2 || d == 6) return strafe + DirWord[d];           // StrafeRight / RunStrafeLeft / CrouchStrafeLeft ...
            return tier + DirWord[d];                                   // Walk, WalkForwardRight, WalkBackRight, WalkBack ...
        }
        private static string SlotName(int i)
        {
            if (i == B_IDLE) return "RifleIdle";
            if (i == B_CIDLE) return "RifleCrouchIdle";
            if (i < B_RUN) return "Rifle" + DirName("Walk", "Strafe", i - B_WALK);
            if (i < B_SPRINT) return "Rifle" + DirName("Run", "RunStrafe", i - B_RUN);
            if (i < B_CIDLE) return "Rifle" + DirName("Sprint", "SprintStrafe", i - B_SPRINT);
            return "Rifle" + DirName("CrouchWalk", "CrouchStrafe", i - B_CWALK);
        }
        private AnimationMixerPlayable _base;
        private readonly AnimationClipPlayable[] _bp = new AnimationClipPlayable[BN];
        private readonly string[] _bname = new string[BN];     // the clip really in the slot (fallbacks)
        private readonly bool[] _breverse = new bool[BN];
        private readonly float[] _bw = new float[BN];
        private bool _noCrouchClips, _hasSprint;
        private int _baseDom = -1;                            // the moving slot with the most weight (the uppers walk in step with it)
        private float _sprintW, _stride;
        private string _previewName;                          // WeaponAdjustment: the selected clip, played standing still

        // ---- the upper source (UpperRig)
        private PlayableGraph _rigGraph;
        private Transform _rigAnim;
        private AnimationMixerPlayable _rigMix;
        private AnimationClipPlayable _rigCur, _rigOld;
        private readonly List<KeyValuePair<Transform, Transform>> _rigPairs = new List<KeyValuePair<Transform, Transform>>();
        private Transform _rigSpine, _rigSpineCopy;
        private string _rigClip = "";
        private float _rigX = 1f, _rigW, _rigWant, _rigEff;   // cross-fade, applied weight (smoothed), wanted weight, the weight really applied this frame
        private bool _rigHold, _rigSync;
        private const float RigCross = 0.15f;

        // ---- actions
        private bool _actionIsTurn;
        private float _turnAcc, _turnLastYaw, _turnDecayAt;
        private bool _turnInit;
        private string _jumpPhase = "";                       // rifle jump: "up" -> "loop" -> "down"

        // ---- weapon poses, per clip name
        private readonly Dictionary<string, float> _poseWn = new Dictionary<string, float>();
        private string _poseListFor = null;
        private bool _aimingNow, _shootingNow;

        private static bool Has(string clip) { return Anims.Get(clip) != null; }

        private void BuildMixamo(AnimationPlayableOutput output)
        {
            // the base: 34 slots of the rifle pack, with fallbacks for what the pack lacks
            var names = new string[BN];
            for (int i = 0; i < BN; i++) names[i] = SlotName(i);
            _base = AnimationMixerPlayable.Create(_graph, BN);
            var missing = new List<string>();
            for (int i = 0; i < BN; i++)
            {
                string n = names[i]; bool rev = false;
                if (!Has(n))
                {
                    missing.Add(n);
                    int d = i >= B_CWALK ? i - B_CWALK : i >= B_SPRINT ? i - B_SPRINT : i >= B_RUN ? i - B_RUN : i >= B_WALK ? i - B_WALK : -1;
                    if (i == B_CIDLE) n = "RifleIdle";
                    else if (i >= B_CWALK) n = Pick(names[B_CWALK + Cardinal(d)], names[B_WALK + d], names[B_WALK + Cardinal(d)]);
                    else if (i >= B_SPRINT) n = Pick(names[B_SPRINT + Cardinal(d)], names[B_RUN + d], names[B_RUN + Cardinal(d)], names[B_WALK + d], names[B_WALK + Cardinal(d)]);
                    else if (i >= B_RUN) n = Pick(names[B_RUN + Cardinal(d)], names[B_WALK + d], names[B_WALK + Cardinal(d)]);
                    else if (i >= B_WALK) n = Pick(names[B_WALK + Cardinal(d)]);
                    if (n == null || !Has(n))
                    {   // backward without any back clip: the forward one reversed
                        if (d >= 3 && d <= 5 && i < B_CIDLE) { n = names[i >= B_SPRINT ? B_SPRINT : i >= B_RUN ? B_RUN : B_WALK]; if (!Has(n)) n = "RifleWalk"; rev = true; }
                        else if (d >= 3 && d <= 5) { n = "RifleCrouchWalk"; if (!Has(n)) n = "RifleWalk"; rev = true; }
                        else n = i >= B_CWALK ? (Has("RifleCrouchWalk") ? "RifleCrouchWalk" : "RifleWalk") : "RifleWalk";
                    }
                }
                _bname[i] = n; _breverse[i] = rev;
                _bp[i] = AnimationClipPlayable.Create(_graph, Anims.Get(n));
                // foot IK only standing and walking (crouched too): the clips' foot goals come from X Bot and on Flexa's legs the solver twisted
                // the shins in the long running strides and the jumps (2.0.2)
                _bp[i].SetApplyFootIK(Plugin.FootIK.Value && (i < B_RUN || i >= B_CIDLE));
                _graph.Connect(_bp[i], 0, _base, i);
                _base.SetInputWeight(i, i == B_IDLE ? 1f : 0f);
            }
            _noCrouchClips = !Has("RifleCrouchIdle") || !Has("RifleCrouchWalk");
            _hasSprint = Has("RifleSprint");
            _fallback = Anims.Get("RifleIdle");
            _layers = AnimationLayerMixerPlayable.Create(_graph, 3);
            _graph.Connect(_base, 0, _layers, 0);
            _layers.SetInputWeight(0, 1f);
            MakeUpper(_fallback);                                   // layer 1: strikes, punches, throws (upper-body mask)
            _layers.SetLayerMaskFromAvatarMask(1, UpperMask());
            _action = AnimationClipPlayable.Create(_graph, _fallback);   // layer 2: whole-body one-shots (Kick, jumps, turns)
            _graph.Connect(_action, 0, _layers, 2);
            _layers.SetInputWeight(2, 0f);
            output.SetSourcePlayable(_layers);
            _graph.Play();
            Plugin.Log.LogInfo("Animations from the bundle (rifle pack legs for every weapon): " + (BN - missing.Count) + "/" + BN + " locomotion clips"
                + (missing.Count > 0 ? ", missing (stand-ins used): " + string.Join(", ", missing.ToArray()) : "")
                + "; uppers: " + Have("RifleAim", "RifleFire", "RifleCrouchAim", "RifleCrouchFire", "RifleReload", "PistolIdle", "PistolRun", "PistolFire", "PistolReload", "Idle", "Walk", "WalkBack", "Run", "CrouchIdle")
                + "; actions: " + Have("Kick", "Jump", "PistolJump", "RifleJumpUp", "RifleJumpLoop", "RifleJumpDown", "RifleTurnLeft", "RifleTurnRight", "RifleCrouchTurnLeft", "RifleCrouchTurnRight"));
        }
        private static int Cardinal(int d) { return d == 1 || d == 7 ? 0 : d == 3 || d == 5 ? 4 : d; }   // the diagonal's forward / back
        private static string Pick(params string[] names) { foreach (var n in names) if (n != null && Has(n)) return n; return null; }
        private static string Have(params string[] names)
        {
            var l = new List<string>();
            foreach (var n in names) l.Add(n + (Has(n) ? "" : "(missing)"));
            return string.Join(", ", l.ToArray());
        }

        // the invisible skeleton copy that plays the upper-body clip (built right after the graph; src = her Anim object)
        private void BuildUpperRig(GameObject src)
        {
            try
            {
                var go = UnityEngine.Object.Instantiate(src, Root.transform, false);
                go.name = "UpperRig";
                go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true)) UnityEngine.Object.DestroyImmediate(r);
                foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.DestroyImmediate(mb);
                _rigAnim = go.transform;
                var copy = new Dictionary<string, Transform>();
                foreach (var t in go.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith("mixamorig:")) copy[t.name] = t;
                var an = go.GetComponent<Animator>();
                an.applyRootMotion = false; an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                if (!Bones.TryGetValue("mixamorig:Spine", out _rigSpine) || !copy.TryGetValue("mixamorig:Spine", out _rigSpineCopy)) { UnityEngine.Object.Destroy(go); return; }
                foreach (var kv in Bones)
                {
                    Transform c;
                    if (kv.Value == _rigSpine || !kv.Value.IsChildOf(_rigSpine) || !copy.TryGetValue(kv.Key, out c)) continue;
                    _rigPairs.Add(new KeyValuePair<Transform, Transform>(kv.Value, c));
                }
                _rigGraph = PlayableGraph.Create("ApocaplayerUpperRig");
                _rigGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var output = AnimationPlayableOutput.Create(_rigGraph, "upper", an);
                _rigMix = AnimationMixerPlayable.Create(_rigGraph, 2);
                _rigCur = AnimationClipPlayable.Create(_rigGraph, _fallback);
                _rigCur.SetApplyFootIK(false);
                _rigGraph.Connect(_rigCur, 0, _rigMix, 0);
                _rigMix.SetInputWeight(0, 1f); _rigMix.SetInputWeight(1, 0f);
                output.SetSourcePlayable(_rigMix);
                _rigGraph.Play();
                Plugin.Verbose("UpperRig: " + _rigPairs.Count + " bones above the spine");
            }
            catch (Exception e) { Plugin.Log.LogError("UpperRig: " + e.Message); _rigPairs.Clear(); }
        }

        // the upper source's clip: a new clip starts at once, the old pose fades out under it (as SetUpper)
        private void RigSet(string clip, bool hold, bool sync)
        {
            if (!_rigGraph.IsValid()) return;
            if (clip != _rigClip)
            {
                _rigClip = clip;
                if (_rigOld.IsValid()) { _rigGraph.Disconnect(_rigMix, 1); _rigOld.Destroy(); }
                _rigGraph.Disconnect(_rigMix, 0);
                if (_rigCur.IsValid() && _rigW > 0.01f && !_snap) { _rigOld = _rigCur; _rigOld.SetSpeed(0); _rigGraph.Connect(_rigOld, 0, _rigMix, 1); _rigX = 0f; }
                else { if (_rigCur.IsValid()) _rigCur.Destroy(); _rigX = 1f; }
                _rigCur = AnimationClipPlayable.Create(_rigGraph, Anims.Get(clip) ?? _fallback);
                _rigCur.SetApplyFootIK(false);
                _rigGraph.Connect(_rigCur, 0, _rigMix, 0);
                _rigCur.SetTime(0);
                _rigMix.SetInputWeight(0, _rigX); _rigMix.SetInputWeight(1, 1f - _rigX);
            }
            _rigHold = hold; _rigSync = sync;
        }

        // every frame after the Animator: evaluate the upper source and write it over her upper body with weight w
        private void RigApply(float dt, float w)
        {
            _rigEff = 0f;
            if (!_rigGraph.IsValid() || _rigSpine == null || !_rigCur.IsValid()) return;
            _rigW = _snap ? _rigWant : Mathf.MoveTowards(_rigW, _rigWant, dt / RigCross);
            float eff = _rigW * w;
            if (eff < 0.001f) return;
            var cc = _rigCur.GetAnimationClip();
            if (_rigHold) { _rigCur.SetSpeed(0); _rigCur.SetTime(0); }
            else
            {
                _rigCur.SetSpeed(1);
                // in step with the legs: the same normalized time as the base's main moving clip
                if (_rigSync && _baseDom >= 0 && cc != null && cc.length > 0.01f)
                {
                    double f = _stride + Plugin.UpperPhase.Value; f -= Math.Floor(f);
                    _rigCur.SetTime(f * cc.length);
                }
                else if (cc != null && !cc.isLooping && _rigCur.GetTime() > cc.length) { if (_previewName != null) _rigCur.SetTime(0); else _rigCur.SetTime(cc.length - 0.001); }
            }
            if (_rigOld.IsValid())
            {
                _rigX = _snap ? 1f : Mathf.MoveTowards(_rigX, 1f, dt / RigCross);
                _rigMix.SetInputWeight(0, _rigX); _rigMix.SetInputWeight(1, 1f - _rigX);
                if (_rigX >= 1f) { _rigGraph.Disconnect(_rigMix, 1); _rigOld.Destroy(); }
            }
            _rigGraph.Evaluate(dt);
            var target = _anim.rotation * (Quaternion.Inverse(_rigAnim.rotation) * _rigSpineCopy.rotation);
            _rigSpine.rotation = Quaternion.Slerp(_rigSpine.rotation, target, eff);
            for (int i = 0; i < _rigPairs.Count; i++)
            {
                var pr = _rigPairs[i];
                pr.Key.localRotation = Quaternion.Slerp(pr.Key.localRotation, pr.Value.localRotation, eff);
            }
            _rigEff = eff;
        }

        private static float ClipSpeed(float speed, float native) { return Mathf.Clamp(speed / Mathf.Max(0.2f, native), 0.5f, 2f); }
        private static float Native(AnimationClipPlayable p, float fallback)
        {
            var c = p.GetAnimationClip();
            if (c == null) return fallback;
            float v = new Vector2(c.averageSpeed.x, c.averageSpeed.z).magnitude;
            return v > 0.3f ? v : fallback;
        }

        // the base layer: idle / 8 directions × walk, run, sprint / crouch idle / 8 crouched walks, by the body's direction and speed
        private void DriveBase(float m, float r, float sprint, Vector3 local, float crouch, float speed, int previewSlot)
        {
            var w = _bw;
            for (int i = 0; i < BN; i++) w[i] = 0f;
            if (previewSlot >= 0) w[previewSlot] = 1f;
            else
            {
                float ang = local.sqrMagnitude > 1e-4f ? Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg : 0f;
                if (ang < 0f) ang += 360f;
                float bin = ang / 45f; int i0 = Mathf.FloorToInt(bin) % 8, i1 = (i0 + 1) % 8; float f = bin - Mathf.Floor(bin);
                float st = 1f - crouch;
                w[B_IDLE] = (1f - m) * st;
                w[B_CIDLE] = (1f - m) * crouch;
                float walk = m * (1f - r) * st, run = m * r * (1f - sprint) * st, spr = m * r * sprint * st, cw = m * crouch;
                w[B_WALK + i0] += walk * (1f - f); w[B_WALK + i1] += walk * f;
                w[B_RUN + i0] += run * (1f - f); w[B_RUN + i1] += run * f;
                w[B_SPRINT + i0] += spr * (1f - f); w[B_SPRINT + i1] += spr * f;
                w[B_CWALK + i0] += cw * (1f - f); w[B_CWALK + i1] += cw * f;
            }
            // one stride for all moving clips: blending two directions / two tiers that are at different points of their stride (run at 0.3,
            // sprint at 0.8 ...) mangles the legs - so every moving slot is put at the same normalized time each frame (a blend tree's "sync"),
            // and the shared phase advances at the weighted rate of the clips in use
            _baseDom = -1; float best = 0.05f, rate = 0f, rateW = 0f;
            for (int i = 0; i < BN; i++)
            {
                _base.SetInputWeight(i, w[i]);
                if (i == B_IDLE || i == B_CIDLE) continue;
                if (w[i] > best) { best = w[i]; _baseDom = i; }
                float native = i >= B_SPRINT && i < B_CIDLE ? Plugin.ClipSprintSpeed.Value : i >= B_RUN && i < B_SPRINT ? Plugin.ClipRunSpeed.Value : i >= B_CWALK ? Plugin.ClipCrouchSpeed.Value : Plugin.ClipWalkSpeed.Value;
                float sp = previewSlot >= 0 ? 1f : ClipSpeed(speed, Native(_bp[i], native));
                var c = _bp[i].GetAnimationClip();
                float len = c != null && c.length > 0.05f ? c.length : 1f;
                if (w[i] > 0.001f) { rate += w[i] * sp / len; rateW += w[i]; }
                _bp[i].SetSpeed(0);
            }
            if (rateW > 0.001f) _stride += Time.deltaTime * rate / rateW;
            _stride -= Mathf.Floor(_stride);
            for (int i = 0; i < BN; i++)
            {
                if (i == B_IDLE || i == B_CIDLE || w[i] <= 0.001f) continue;
                var c = _bp[i].GetAnimationClip();
                float len = c != null ? c.length : 1f;
                _bp[i].SetTime((_breverse[i] ? 1f - _stride : _stride) * len);
            }
        }

        // the clips the drawn weapon can show, for WeaponAdjustment (numpad 9/3) - every one with its own pose entry
        private void SetPoseList(Props.Kind kind, string weapon)
        {
            string key = kind + "|" + weapon;
            if (key == _poseListFor) return;
            _poseListFor = key;
            var l = new List<string>();
            string idle = "Idle";
            if (kind == Props.Kind.Rifle)
            {
                idle = "RifleIdle";
                for (int i = 0; i < BN; i++) if (!l.Contains(_bname[i])) l.Add(_bname[i]);
                foreach (var n in new[] { "RifleAim", "RifleFire", "RifleCrouchAim", "RifleCrouchFire", "RifleReload", "RifleJumpUp", "RifleJumpLoop", "RifleJumpDown", "RifleTurnLeft", "RifleTurnRight", "RifleCrouchTurnLeft", "RifleCrouchTurnRight", "Kick" })
                    if (Has(n) && !l.Contains(n)) l.Add(n);
            }
            else if (kind == Props.Kind.Pistol)
            {
                idle = "PistolIdle";
                foreach (var n in new[] { "PistolIdle", "PistolRun", "PistolFire", "PistolReload", "PistolJump", "Kick" }) if (Has(n)) l.Add(n);
            }
            else
            {
                foreach (var n in new[] { "Idle", "Walk", "WalkBack", "Run", "CrouchIdle" }) if (Has(n)) l.Add(n);
                if (kind == Props.Kind.Throw) { foreach (var n in new[] { "Throw", "ThrowRight" }) if (Has(n)) l.Add(n); }
                else foreach (var n in new[] { "Melee", "MeleeCombo" }) if (Has(n)) l.Add(n);
                foreach (var n in new[] { "Kick", "Jump" }) if (Has(n)) l.Add(n);
            }
            GunPose.SetPoseList(l, idle);
        }

        // which clips move her hands this frame, and how much (for the weapon pose blend)
        private void PoseWeights()
        {
            _poseWn.Clear();
            string pv = _previewName;
            if (pv != null) { _poseWn[pv] = 1f; return; }
            if (_inCar) return;
            float wAct = _actionW, wUp = _upperW, wRig = _rigEff;
            float wBase = (1f - wAct) * (1f - wUp) * (1f - wRig);
            if (wBase > 0.001f) for (int i = 0; i < BN; i++) if (_bw[i] > 0.001f) Add(_bname[i], _bw[i] * wBase);
            if (wRig > 0.001f && !string.IsNullOrEmpty(_rigClip)) Add(_rigClip, wRig * (1f - wAct) * (1f - wUp));
            if (wUp > 0.001f && !string.IsNullOrEmpty(_upperClip)) Add(_upperClip, wUp * (1f - wAct));
            if (wAct > 0.001f && !string.IsNullOrEmpty(_actionClip)) Add(_actionClip, wAct);
        }
        private void Add(string n, float w) { float v; _poseWn.TryGetValue(n, out v); _poseWn[n] = v + w; }
        private string DominantPose()
        {
            if (_previewName != null) return _previewName;
            string best = null; float bw = -1f;
            foreach (var kv in _poseWn) if (kv.Value > bw) { bw = kv.Value; best = kv.Key; }
            return best ?? "Idle";
        }

        private string JumpClip(Props.Kind kind)
        {
            if (kind == Props.Kind.Rifle && Has("RifleJumpUp")) return "RifleJumpUp";
            if (kind == Props.Kind.Rifle && Has("RifleJump")) return "RifleJump";
            if (kind == Props.Kind.Pistol && Has("PistolJump")) return "PistolJump";
            return "Jump";
        }

        // standing still and turned a lot: a turn-in-place clip (the rifle pack's, full body; the upper source stays on it for pistols / bare hands)
        private void TurnInPlace(float yawDeg, float m, float crouch)
        {
            if (!_turnInit) { _turnInit = true; _turnLastYaw = yawDeg; }
            float d = Mathf.DeltaAngle(_turnLastYaw, yawDeg);
            _turnLastYaw = yawDeg;
            if (Plugin.TurnClipAngle.Value <= 0f) return;
            if (m > 0.15f || Time.time < _actionUntil) { _turnAcc = 0f; return; }
            if (Mathf.Abs(d) > 0.05f) { _turnAcc += d; _turnDecayAt = Time.time + 0.4f; }
            else if (Time.time > _turnDecayAt) _turnAcc = Mathf.MoveTowards(_turnAcc, 0f, Time.deltaTime * 120f);
            if (Mathf.Abs(_turnAcc) < Plugin.TurnClipAngle.Value) return;
            bool left = _turnAcc < 0f;
            string clip = crouch > 0.5f ? (left ? "RifleCrouchTurnLeft" : "RifleCrouchTurnRight") : (left ? "RifleTurnLeft" : "RifleTurnRight");
            if (!Has(clip)) clip = left ? "RifleTurnLeft" : "RifleTurnRight";
            _turnAcc = 0f;
            if (!Has(clip)) return;
            StartAction(clip);
            _actionIsTurn = true;
        }

        private void LateMixamo(View view, float dt, Quaternion yaw, float camPitch)
        {
            Vector3 v = Game.Velocity; v.y = 0f;
            var local = Quaternion.Inverse(yaw) * v;
            float speed = v.magnitude;
            _speedSmooth = Mathf.Lerp(_speedSmooth, speed, 1f - Mathf.Exp(-dt * 10f));
            float m = Mathf.Clamp01(_speedSmooth / 0.4f);
            float r = Mathf.Clamp01((_speedSmooth - Plugin.RunFrom.Value) / 0.8f);
            if (_crouch > 0.5f) r = 0f;
            _runW = Mathf.MoveTowards(_runW, r, dt * 4f);
            float sprint = _hasSprint ? Mathf.Clamp01((_speedSmooth - Plugin.SprintFrom.Value) / 0.6f) : 0f;
            _sprintW = Mathf.MoveTowards(_sprintW, sprint, dt * 4f);
            float crouch = _noCrouchClips ? 0f : _crouch;

            string weapon = Game.DrawnWeapon;
            var kind = Props.KindOf(weapon);
            bool gunKind = kind == Props.Kind.Rifle || kind == Props.Kind.Pistol;
            if (weapon != _lastWeapon)
            {
                _lastWeapon = weapon; _weaponSince = Time.time;
                _reloading = false; _reloadFresh = false; _upperClip = "";
                GunPose.Flush();
                Plugin.Verbose("Weapon: " + (weapon == "" ? "none" : weapon + " (" + kind + ")"));
            }
            SetPoseList(kind, weapon);
            bool live = !Game.Paused;

            // WeaponAdjustment, Numpad 9/3: the selected clip plays standing still so its weapon pose can be tuned
            _previewName = GunPose.Preview;
            int previewSlot = -1;
            if (_previewName != null)
            {
                for (int i = 0; i < BN; i++) if (_bname[i] == _previewName) { previewSlot = i; break; }
                m = 0f; _runW = 0f; _sprintW = 0f; _speedSmooth = 0f;
                if (previewSlot < 0 && _previewName.Contains("Crouch")) { crouch = 1f; previewSlot = B_CIDLE; }
                else if (previewSlot < 0) { crouch = 0f; previewSlot = B_IDLE; }
            }
            DriveBase(m, _runW, _sprintW, local, crouch, _speedSmooth, previewSlot);

            // aiming (right mouse button) / shooting / reloading
            bool aiming = live && gunKind && _previewName == null && Game.AimDownSights;
            bool shooting = live && Input.GetMouseButton(0);
            bool click = live && Input.GetMouseButtonDown(0);
            bool reloadingNow = gunKind && ReloadNow(weapon);
            bool pvReload = _previewName != null && _previewName.EndsWith("Reload");
            if (pvReload) reloadingNow = true;
            if (reloadingNow && !_reloading) { _reloadStart = Time.time; }
            if (pvReload) { var rc = Anims.Get(_previewName); if (rc != null && Time.time - _reloadStart > rc.length) _reloadStart = Time.time; }
            _reloading = reloadingNow;
            _reloadUntil = _reloading ? Time.time + 0.05f : 0f;
            _aimingNow = aiming; _shootingNow = shooting && gunKind;
            if (_snap) { _upperClip = ""; Plugin.Verbose("View switched: animation state re-synced"); }

            // kick (the game's Kick button) and jump: whole-body one-shots on the action layer
            string ks = Game.KickState;
            if (ks == "fire" && _kickState != "fire") { StartAction("Kick"); _actionIsTurn = false; }
            _kickState = ks;
            string js = Game.JumpState;
            bool jumpPress = live && js == "Idle" && Input.GetButtonDown("Jump");
            bool launched = js == "Falling" && _jumpState == "Idle" && Game.Velocity.y > 2f && Time.time > _jumpAt + 0.5f;
            if (jumpPress || launched)
            {
                _jumpAt = Time.time;
                string jc = JumpClip(kind);
                StartAction(jc, jc == "RifleJumpUp" ? 0f : Plugin.JumpClipStart.Value);
                _actionIsTurn = false;
                _jumpPhase = jc == "RifleJumpUp" ? "up" : "";
            }
            // the rifle jump in three clips: up, then the loop while in the air, down when the ground is back
            if (_jumpPhase == "up" && Time.time >= _actionUntil - 0.1f && js == "Falling" && Has("RifleJumpLoop")) { StartAction("RifleJumpLoop"); _actionUntil = Time.time + 10f; _jumpPhase = "loop"; }
            if (_jumpPhase != "" && _jumpState == "Falling" && js == "Idle" && Time.time - _jumpAt > 0.25f)
            {
                if (Has("RifleJumpDown")) StartAction("RifleJumpDown"); else _actionUntil = Mathf.Min(_actionUntil, Time.time + 0.3f);
                _jumpPhase = "";
            }
            else if (_jumpPhase == "" && _jumpState == "Falling" && js == "Idle" && _actionClip != null && _actionClip.EndsWith("Jump") && Time.time - _jumpAt > 0.25f && Time.time < _actionUntil)
                _actionUntil = Mathf.Min(_actionUntil, Time.time + 0.3f);
            _jumpState = js;
            if (_previewName != null && (_previewName.Contains("Jump") || _previewName == "Kick" || _previewName.Contains("Turn")) && Time.time > _actionUntil - 0.2f)
            { StartAction(_previewName); _actionIsTurn = _previewName.Contains("Turn"); }
            if (_previewName == null) TurnInPlace(yaw.eulerAngles.y, m, crouch);
            UpdateAction(dt);

            // melee weapons and bare hands: one strike per game swing; throws
            if (kind == Props.Kind.Melee || kind == Props.Kind.None && weapon != "") WatchAttack(kind, weapon, click);
            else { _atkFor = ""; _striking = false; _strikeW = 0f; _meleeStart = 0f; }
            if (kind == Props.Kind.Throw) WatchThrow(weapon, click); else _throwFor = "";
            string gs = Game.GrenadeState;
            if (gs == "fire" && _grenadeState != "fire") StartThrow(false, true);
            _grenadeState = gs;

            // layer 1 (upper-body mask): throws and strikes only
            if (Time.time < _throwUntil && Anims.Get(_throwClip) != null) PlayThrow();
            else if (kind == Props.Kind.Melee || kind == Props.Kind.None && weapon != "") PlayStrike(dt);
            else SetUpper("", 0f, 0f);

            // the upper source: what the hands do
            string up = null; bool hold = false, sync = false;
            bool moving = m > 0.5f, running = _runW > 0.5f;
            if (_previewName != null && previewSlot >= 0 && _bname[previewSlot] == _previewName) up = null;   // a base clip: its own upper body
            else if (_previewName != null && !_previewName.Contains("Jump") && _previewName != "Kick" && !_previewName.Contains("Turn")) { up = _previewName; hold = false; }
            else if (kind == Props.Kind.Rifle)
            {
                if (_reloading && Has("RifleReload")) up = "RifleReload";
                else if (aiming || shooting)
                {
                    bool cr = crouch > 0.5f;
                    string aim = cr && Has("RifleCrouchAim") ? "RifleCrouchAim" : Has("RifleAim") ? "RifleAim" : null;
                    string fire = cr && Has("RifleCrouchFire") ? "RifleCrouchFire" : Has("RifleFire") ? "RifleFire" : null;
                    if (shooting && fire != null) up = fire;
                    else if (aim != null) up = aim;
                    else if (fire != null) { up = fire; hold = true; }
                }
            }
            else if (kind == Props.Kind.Pistol)
            {
                if (_reloading && Has("PistolReload")) up = "PistolReload";
                else if ((aiming || shooting) && Has("PistolFire")) { up = "PistolFire"; hold = !shooting; }
                else if (moving && running && Has("PistolRun")) { up = "PistolRun"; sync = true; }
                else up = Has("PistolIdle") ? "PistolIdle" : null;
            }
            else
            {
                if (crouch > 0.5f && Has("CrouchIdle")) up = "CrouchIdle";
                else if (moving && running && Has("Run")) { up = "Run"; sync = true; }
                else if (moving && Has("Walk")) { up = local.z < -0.1f && Has("WalkBack") ? "WalkBack" : "Walk"; sync = true; }
                else up = Has("Idle") ? "Idle" : null;
            }
            if (up != null) RigSet(up, hold, sync);
            _rigWant = up != null ? 1f : 0f;
            // full-body actions (kick, jumps) take the upper body with them; a turn in place keeps the hands
            float rigScale = (1f - _upperW) * (_actionIsTurn ? 1f : 1f - _actionW);
            RigApply(dt, rigScale);
            UpdateProp(weapon); ShowProp(view == View.ThirdPerson);

            // keep her over the player: a clip whose forward motion was baked into the pose walks the hips away from the root - pin them
            Transform hips;
            if (Bones.TryGetValue("mixamorig:Hips", out hips))
            {
                var hp = hips.localPosition;
                var xz = Vector2.ClampMagnitude(new Vector2(hp.x, hp.z), Plugin.HipsDrift.Value);
                if (xz.x != hp.x || xz.y != hp.z) hips.localPosition = new Vector3(xz.x, hp.y, xz.y);
            }
            if (_noCrouchClips && _crouch > 0.001f) Crouch(_crouch);
            float pitch = view == View.FirstPerson ? 0f : camPitch * Plugin.AimPitchShare.Value * (1f - _prone);
            if (Mathf.Abs(pitch) > 0.5f)
            {
                Turn("mixamorig:Spine", Vector3.right, pitch * 0.3f);
                Turn("mixamorig:Spine1", Vector3.right, pitch * 0.3f);
                Turn("mixamorig:Spine2", Vector3.right, pitch * 0.3f);
                Turn("mixamorig:Neck", Vector3.right, pitch * 0.1f);
            }
            ThrowAim();
            PunchAim();
            PoseProp(kind, yaw);
            LanceAlign(kind);
        }
    }
}
