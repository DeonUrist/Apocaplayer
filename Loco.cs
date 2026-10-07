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
        // 0..33 the pack's aiming set; 34..40 (2.1) the RELAXED set: idle, walk F / R / B / L (relaxed walk, mirrored strafes, walk back), run, sprint
        // (2.1.1) the slots, their clips and every decision are in LocoPlan.cs (tested outside the game by tools/locotest)
        private const int B_IDLE = LocoPlan.B_IDLE, B_CIDLE = LocoPlan.B_CIDLE, B_RIDLE = LocoPlan.B_RIDLE, BN = LocoPlan.BN;
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
        private Transform _rigSpine, _rigSpineCopy, _rigLFoot, _rigRFoot;
        private AnimationPlayableOutput _rigOut;
        // ---- (2.1.1) each moving clip's phase: where in the clip her LEFT foot is highest above the right one (measured once per clip on the
        // upper rig's skeleton copy). Every moving clip - base and upper - is put at its own phase + the shared stride, so clips from different
        // sources (the pack, single downloads, a mirrored strafe, which leads with the other foot) step together.
        private readonly float[] _boff = new float[LocoPlan.BN];
        private readonly Dictionary<string, float> _phaseOf = new Dictionary<string, float>();
        private string _rigClip = "";
        private float _rigX = 1f, _rigW, _rigWant, _rigEff;   // cross-fade, applied weight (smoothed), wanted weight, the weight really applied this frame
        private bool _rigHold, _rigSync;
        private const float RigCross = 0.15f;

        // ---- actions
        private bool _actionIsTurn;
        private float _turnLastYaw, _turnDeg, _turnTotal = 90f, _turnMovedAt, _moveAng;
        private float[] _turnCurve;
        private string _turnClip;
        private bool _turnSettling;
        private bool _turnInit;
        private string _jumpPhase = "";
        private float _walkedAt = -10f, _stopAt = -10f;                       // rifle jump: "up" -> "loop" -> "down"

        // ---- weapon poses, per clip name
        private readonly Dictionary<string, float> _poseWn = new Dictionary<string, float>();
        private string _poseListFor = null;
        private bool _aimingNow, _shootingNow;

        private static bool Has(string clip) { return Anims.Get(clip) != null; }

        private void BuildMixamo(AnimationPlayableOutput output)
        {
            // the base: 34 slots of the rifle pack, with fallbacks for what the pack lacks
            _base = AnimationMixerPlayable.Create(_graph, BN);
            var missing = new List<string>();
            for (int i = 0; i < BN; i++)
            {
                bool rev;
                string n = LocoPlan.Resolve(i, Has, out rev) ?? "RifleIdle";
                if (n != LocoPlan.SlotName(i)) missing.Add(LocoPlan.SlotName(i) + "->" + n + (rev ? "(reversed)" : ""));
                _bname[i] = n; _breverse[i] = rev; _boff[i] = -2f;
                _bp[i] = AnimationClipPlayable.Create(_graph, Anims.Get(n));
                // foot IK only standing and walking (crouched too): the clips' foot goals come from X Bot and on Flexa's legs the solver twisted
                // the shins in the long running strides and the jumps (2.0.2)
                _bp[i].SetApplyFootIK(Plugin.FootIK.Value && (i < LocoPlan.B_RUN || (i >= B_CIDLE && i != LocoPlan.B_RRUN && i != LocoPlan.B_RSPRINT)));
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
                + "; relaxed: " + Have("Idle", "RifleWalkLow", "RifleRunLow", "WalkStrafeLeft", "WalkStrafeRight", "WalkBack", "RifleWalkToStop", "LeftTurn", "RightTurn")
                + "; actions: " + Have("Kick", "Jump", "PistolJump", "RifleJumpUp", "RifleJumpLoop", "RifleJumpDown", "RifleTurnLeft", "RifleTurnRight", "RifleCrouchTurnLeft", "RifleCrouchTurnRight"));
        }
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
                _rigOut = output;
                copy.TryGetValue("mixamorig:LeftFoot", out _rigLFoot); copy.TryGetValue("mixamorig:RightFoot", out _rigRFoot);
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

        private float PhaseOf(string clip)
        {
            float p;
            if (string.IsNullOrEmpty(clip)) return -1f;
            if (_phaseOf.TryGetValue(clip, out p)) return p;
            p = MeasurePhase(Anims.Get(clip));
            _phaseOf[clip] = p;
            Plugin.Verbose("Stride phase of " + clip + ": " + (p < 0f ? "none" : p.ToString("0.00")));
            return p;
        }
        // samples the clip on the skeleton copy (its own source for a moment) and takes the first harmonic of left-minus-right foot height
        private float MeasurePhase(AnimationClip c)
        {
            var d = SampleFeet(c, 24, false);
            return d == null ? -1f : LocoPlan.PhaseFromHeights(d);
        }
        // n samples over the clip on the skeleton copy: left minus right foot height
        private float[] SampleFeet(AnimationClip c, int n, bool lower)
        {
            if (c == null || !_rigGraph.IsValid() || !_rigOut.IsOutputValid() || _rigLFoot == null || _rigRFoot == null || _rigAnim == null) return null;
            AnimationClipPlayable tmp = default(AnimationClipPlayable);
            try
            {
                tmp = AnimationClipPlayable.Create(_rigGraph, c);
                tmp.SetApplyFootIK(false); tmp.SetSpeed(0);
                _rigOut.SetSourcePlayable(tmp);
                var d = new float[n];
                for (int k = 0; k < n; k++)
                {
                    tmp.SetTime(k * c.length / n);
                    _rigGraph.Evaluate(0f);
                    float l = _rigAnim.InverseTransformPoint(_rigLFoot.position).y, r = _rigAnim.InverseTransformPoint(_rigRFoot.position).y;
                    d[k] = lower ? Mathf.Min(l, r) : l - r;
                }
                return d;
            }
            catch (Exception e) { Plugin.Log.LogWarning("Sampling " + c.name + ": " + e.Message); return null; }
            finally
            {
                if (_rigOut.IsOutputValid()) _rigOut.SetSourcePlayable(_rigMix);
                if (tmp.IsValid()) tmp.Destroy();
            }
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
                    _rigCur.SetTime(LocoPlan.Time01(_stride + Plugin.UpperPhase.Value, PhaseOf(_rigClip), false) * cc.length);
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
        // relaxed: how much of the standing locomotion comes from the relaxed set (not aiming / striking), and the legs' turn toward the way she moves
        private float _relaxW, _hipTurn;
        private int _locoKind;
        private float YawIn(Transform a, Transform b)   // facing of the line a->b (pointing to her right) about her up axis, + = right
        {
            var s = _anim.InverseTransformDirection(b.position - a.position); s.y = 0f;
            var f = Vector3.Cross(s, Vector3.up);
            return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        }
        private void TwistSpine(float follow)
        {
            Transform lh, rh, la, ra;
            if (!Bones.TryGetValue("mixamorig:LeftUpLeg", out lh) || !Bones.TryGetValue("mixamorig:RightUpLeg", out rh)
                || !Bones.TryGetValue("mixamorig:LeftArm", out la) || !Bones.TryGetValue("mixamorig:RightArm", out ra)) return;
            float s0, s1, s2, nk;
            LocoPlan.Twist(YawIn(lh, rh), YawIn(la, ra), follow, out s0, out s1, out s2, out nk);
            Turn("mixamorig:Spine", Vector3.up, s0);
            Turn("mixamorig:Spine1", Vector3.up, s1);
            Turn("mixamorig:Spine2", Vector3.up, s2);
            Turn("mixamorig:Neck", Vector3.up, nk);
        }
        private float _actionFade = 6f;                          // how fast an action fades out (1/s)
        // a jump clip's take-off / apex / touch-down (x, y, z as shares of the clip; -1 = none)
        private static Vector3 JumpMarksOf(string clip)
        {
            float l, a, t;
            return LocoPlan.JumpMarks(clip, out l, out a, out t) ? new Vector3(l, a, t) : new Vector3(-1f, -1f, -1f);
        }
        private void DriveBase(float m, float r, float sprint, Vector3 local, float crouch, float speed, int previewSlot, float relax, out float hipTurn)
        {
            var w = _bw;
            hipTurn = 0f;
            if (previewSlot >= 0) { for (int i = 0; i < BN; i++) w[i] = 0f; w[previewSlot] = 1f; }
            else LocoPlan.Weights(m, r, sprint, local.x, local.z, crouch, relax, LocoPlan.CrouchMirror(_locoKind), Plugin.RunLegsTurn.Value, w, out hipTurn);
            // one stride for all moving clips: every moving slot is put at its own phase (left foot highest) + the shared stride each frame
            // (a blend tree's "sync"); the stride advances at the weighted rate of the clips in use
            _baseDom = -1; float best = 0.05f, rate = 0f, rateW = 0f;
            for (int i = 0; i < BN; i++)
            {
                _base.SetInputWeight(i, w[i]);
                if (!LocoPlan.Moving(i)) continue;
                if (w[i] > best) { best = w[i]; _baseDom = i; }
                float native = i == LocoPlan.B_RSPRINT || (i >= LocoPlan.B_SPRINT && i < B_CIDLE) ? Plugin.ClipSprintSpeed.Value
                             : i == LocoPlan.B_RRUN || (i >= LocoPlan.B_RUN && i < LocoPlan.B_SPRINT) ? Plugin.ClipRunSpeed.Value
                             : (i >= LocoPlan.B_CWALK && i < B_RIDLE) || i == LocoPlan.B_RCLEFT ? Plugin.ClipCrouchSpeed.Value : Plugin.ClipWalkSpeed.Value;
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
                if (!LocoPlan.Moving(i) || w[i] <= 0.001f) continue;
                if (_boff[i] < -1.5f) _boff[i] = PhaseOf(_bname[i]);
                var c = _bp[i].GetAnimationClip();
                float len = c != null ? c.length : 1f;
                _bp[i].SetTime(LocoPlan.Time01(_stride, _boff[i], _breverse[i]) * len);
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
                foreach (var n in new[] { "Idle", "RifleWalkLow", "RifleRunLow", "WalkStrafeLeft", "WalkStrafeRight", "WalkBack", "RifleWalkToStop", "LeftTurn", "RightTurn", "RifleAim", "RifleFire", "RifleCrouchAim", "RifleCrouchFire", "RifleReload", "RifleJumpUp", "RifleJumpLoop", "RifleJumpDown", "RifleTurnLeft", "RifleTurnRight", "RifleCrouchTurnLeft", "RifleCrouchTurnRight", "Kick" })
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
        // (2.1.5) standing still and turning: from the first degree the turn clip's feet are driven by the turn itself (LocoPlan.TurnCurve:
        // each degree she turns advances the clip by the share of it that turns one degree), so the feet step exactly as fast as the camera
        // turns and never slide; past the clip's end it starts over. When she stops turning the step in progress is finished (past its middle)
        // or faded out.
        private void TurnInPlace(float yawDeg, float m, float crouch, bool relaxed, float dt)
        {
            if (!_turnInit) { _turnInit = true; _turnLastYaw = yawDeg; }
            float d = Mathf.DeltaAngle(_turnLastYaw, yawDeg);
            _turnLastYaw = yawDeg;
            bool mine = _turnClip != null && _actionClip == _turnClip && Time.time < _actionUntil;
            if (Plugin.TurnClipAngle.Value <= 0f || m > 0.15f || (Time.time < _actionUntil && !mine)) { EndTurn(mine && m <= 0.15f); return; }
            if (Mathf.Abs(d) > Plugin.TurnStartRate.Value * Mathf.Max(dt, 1e-3f))
            {
                bool left = d < 0f;
                string clip = crouch > 0.5f ? (left ? "RifleCrouchTurnLeft" : "RifleCrouchTurnRight") : relaxed && Has(left ? "LeftTurn" : "RightTurn") ? (left ? "LeftTurn" : "RightTurn") : (left ? "RifleTurnLeft" : "RifleTurnRight");
                if (!Has(clip)) clip = left ? "RifleTurnLeft" : "RifleTurnRight";
                if (!Has(clip)) return;
                if (mine && _turnClip == clip && _turnSettling)
                {   // turning again while the last step settles: carry on from where the clip is
                    var sc = Anims.Get(clip);
                    _turnDeg = LocoPlan.TurnFrac(_turnCurve, Mathf.Clamp01((float)_action.GetTime() / sc.length)) * _turnTotal;
                }
                else if (!mine || _turnClip != clip)
                {
                    StartAction(clip);
                    _turnClip = clip; _turnDeg = 0f;
                    LocoPlan.TurnCurve(clip, out _turnTotal, out _turnCurve);
                }
                _turnDeg += Mathf.Abs(d);
                while (_turnDeg >= _turnTotal) _turnDeg -= _turnTotal;
                var c = Anims.Get(clip);
                _action.SetSpeed(0);
                _action.SetTime(LocoPlan.TurnTime(_turnCurve, _turnDeg / _turnTotal) * c.length);
                _actionUntil = Time.time + 0.4f;
                _actionIsTurn = true; _actionFade = 10f;
                _turnMovedAt = Time.time; _turnSettling = false;
            }
            else if (mine && !_turnSettling && Time.time - _turnMovedAt > 0.12f) EndTurn(true);
        }
        private void EndTurn(bool settle)
        {
            if (_turnClip == null) return;
            if (_actionClip == _turnClip && Time.time < _actionUntil && _action.IsValid())
            {
                var c = Anims.Get(_turnClip);
                float frac = _turnTotal > 0f ? _turnDeg / _turnTotal : 0f;
                if (settle && c != null && frac > 0.5f)
                {   // finish the step: the rest of the clip at its own speed
                    _action.SetSpeed(1);
                    _actionUntil = Time.time + Mathf.Max(0.05f, c.length - (float)_action.GetTime()) + 0.1f;
                }
                else _actionUntil = Mathf.Min(_actionUntil, Time.time + 0.15f);
                _actionFade = 10f;
            }
            _turnSettling = true;
            if (!settle) _turnClip = null;
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
            // aiming (right mouse button) / shooting / reloading
            bool aiming = live && gunKind && _previewName == null && Game.AimDownSights;
            bool shooting = live && Input.GetMouseButton(0);
            // relaxed legs (the low-ready walk / run, mirrored strafes, legs turned the way she goes) unless she aims, shoots a gun, swings or throws
            int lk = kind == Props.Kind.Rifle ? LocoPlan.K_RIFLE : kind == Props.Kind.Pistol ? LocoPlan.K_PISTOL : kind == Props.Kind.Melee ? LocoPlan.K_MELEE
                   : kind == Props.Kind.Throw ? LocoPlan.K_THROW : LocoPlan.K_NONE;
            bool relaxed = LocoPlan.Relaxed(lk, aiming, shooting && gunKind) && _previewName == null;
            _locoKind = lk;
            if (_previewName != null) relaxed = previewSlot >= B_RIDLE;
            _relaxW = _snap ? (relaxed ? 1f : 0f) : Mathf.MoveTowards(_relaxW, relaxed ? 1f : 0f, dt * 5f);
            float hipWant;
            // (2.1.5) the legs' direction turns toward the way she moves at DirBlendSpeed °/s (strafe left -> forward passes the diagonal)
            float wantAng = local.sqrMagnitude > 1e-4f ? Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg : _moveAng;
            _moveAng = (_snap || _speedSmooth < 0.15f) ? wantAng : LocoPlan.SlewAngle(_moveAng, wantAng, Plugin.DirBlendSpeed.Value * dt);
            var legDir = new Vector3(Mathf.Sin(_moveAng * Mathf.Deg2Rad), 0f, Mathf.Cos(_moveAng * Mathf.Deg2Rad)) * local.magnitude;
            DriveBase(m, _runW, _sprintW, legDir, crouch, _speedSmooth, previewSlot, _relaxW, out hipWant);
            _hipTurn = Mathf.Lerp(_hipTurn, hipWant, 1f - Mathf.Exp(-dt * 8f));
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
            // (2.1.2) the game's [Jump] FSM: Idle -> Jump (button) -> Idle on landing; Falling only when walking off an edge. Each jump clip's take-off,
            // apex and touch-down are measured once (JumpMarks); the clip starts just before its take-off, holds its apex while she is in the air,
            // and on landing goes to its touch-down - standing still it shows ~0.25 s of the landing, moving it fades straight back to the legs.
            string js = Game.JumpState;
            bool air = js == "Jump" || js == "Falling";
            bool jumpPress = live && js == "Idle" && Input.GetButtonDown("Jump");
            bool launched = js == "Falling" && _jumpState == "Idle" && Game.Velocity.y > 2f && Time.time > _jumpAt + 0.5f;
            if (jumpPress || launched)
            {
                _jumpAt = Time.time;
                string jc = JumpClip(kind);
                var jm = JumpMarksOf(jc);
                var jcl = Anims.Get(jc);
                float from = jm.x > 0f && jcl != null ? Mathf.Max(0f, jm.x - 0.1f / jcl.length) : jc == "RifleJumpUp" ? 0f : Plugin.JumpClipStart.Value;
                StartAction(jc, from);
                if (jc == "RifleJumpUp") _actionUntil = Time.time + 10f;     // held on until the loop / the landing takes over
                _actionIsTurn = false; _actionFade = 10f;
                _jumpPhase = jc == "RifleJumpUp" ? "up" : "single";
            }
            if (_jumpPhase != "" && _action.IsValid())
            {
                var ac = _action.GetAnimationClip();
                if (_jumpPhase == "up" && ac != null && _action.GetTime() >= ac.length - 0.05f && air && Has("RifleJumpLoop"))
                { StartAction("RifleJumpLoop"); _actionUntil = Time.time + 10f; _jumpPhase = "loop"; }
                else if (_jumpPhase == "single" && air && ac != null)
                {   // in the air: hold the clip's apex (the game's jump can be longer than the clip's)
                    var jm = JumpMarksOf(_actionClip);
                    if (jm.y > 0f && _action.GetTime() >= jm.y * ac.length) { _action.SetSpeed(0); _actionUntil = Mathf.Max(_actionUntil, Time.time + 0.5f); }
                }
                bool landed = (_jumpState == "Jump" || _jumpState == "Falling") && js == "Idle" && Time.time - _jumpAt > 0.2f;
                if (landed)
                {
                    bool still = m < 0.5f;
                    string land = _jumpPhase == "single" ? _actionClip : Has("RifleJumpDown") ? "RifleJumpDown" : null;
                    var lm = JumpMarksOf(land);
                    if (land != null && still)
                    {
                        float t = lm.z > 0f ? lm.z : 0f;
                        StartAction(land, t);
                        var lc = Anims.Get(land);
                        if (lc != null) _actionUntil = Time.time + Mathf.Min(0.25f, (1f - t) * lc.length) + 0.15f;
                    }
                    else _actionUntil = Mathf.Min(_actionUntil, Time.time + 0.15f);   // moving: back to the legs at once
                    _actionFade = 10f;
                    _jumpPhase = "";
                }
                else if (!air && Time.time - _jumpAt > 0.5f && _jumpPhase != "") { _jumpPhase = ""; _actionUntil = Mathf.Min(_actionUntil, Time.time + 0.15f); }
            }
            _jumpState = js;
            if (_previewName != null && (_previewName.Contains("Jump") || _previewName == "Kick" || _previewName.Contains("Turn")) && Time.time > _actionUntil - 0.2f)
            { StartAction(_previewName); _actionIsTurn = _previewName.Contains("Turn"); }
            if (_previewName == null) TurnInPlace(yaw.eulerAngles.y, m, crouch, relaxed, dt);
            // walking relaxed (straight forward) and letting go of the keys: RifleWalkToStop's last step settles the feet (the hands stay the rig's).
            // (2.1.2) it starts the moment her speed drops, at the point of its own walk cycle that matches her legs (LocoPlan.StopStart) -
            // starting it at a fixed point once she had nearly stopped popped a leg back by up to a metre. No match (right foot swinging): no clip.
            bool walkingNow = relaxed && crouch < 0.5f && m > 0.6f && _runW < 0.5f && local.z > 0.9f * local.magnitude;
            if (walkingNow) _walkedAt = Time.time;
            if (Plugin.WalkToStop.Value && _previewName == null && relaxed && crouch < 0.5f && speed < 0.25f && m > 0.3f && Time.time - _walkedAt < 0.3f
                && Time.time > _actionUntil && Has("RifleWalkToStop") && _stopAt < _walkedAt && _bw[LocoPlan.B_RWALK] > 0.3f)
            {
                _stopAt = Time.time;
                float from = LocoPlan.StopStart(_stride);
                var sc = Anims.Get("RifleWalkToStop");
                if (from >= 0f && sc != null)
                {
                    StartAction("RifleWalkToStop", from);
                    _actionUntil = Time.time + (LocoPlan.StopTo - from) * sc.length + 0.15f;
                    _actionIsTurn = true; _actionFade = 6f;
                }
            }
            if (_actionClip == "RifleWalkToStop" && Time.time < _actionUntil && (speed > 0.5f || crouch > 0.5f || !relaxed)) _actionUntil = Mathf.Min(_actionUntil, Time.time + 0.15f);
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
            if (_previewName != null && previewSlot >= 0 && _bname[previewSlot] == _previewName) up = null;   // a base clip: its own upper body
            else if (_previewName != null && !_previewName.Contains("Jump") && _previewName != "Kick" && !_previewName.Contains("Turn")) { up = _previewName; hold = false; }
            else up = LocoPlan.Upper(lk, relaxed, aiming, shooting, _reloading, crouch, m, _runW, legDir.z, _bw[LocoPlan.B_RCLEFT], Has, out hold, out sync);
            if (up != null) RigSet(up, hold, sync);
            _rigWant = up != null ? 1f : 0f;
            // relaxed running: the legs face the way she runs (hips about up); the chest is put back toward the camera by the rig (rifle, pistol,
            // bare hands all have one), else by a counter-turn of the spine
            if (Mathf.Abs(_hipTurn) > 0.2f && _actionW < 0.5f)
            {
                Turn("mixamorig:Hips", Vector3.up, _hipTurn);
                if (_rigWant < 0.5f) { Turn("mixamorig:Spine", Vector3.up, -_hipTurn * 0.5f); Turn("mixamorig:Spine1", Vector3.up, -_hipTurn * 0.5f); }
            }
            // full-body actions (kick, jumps) take the upper body with them; a turn in place keeps the hands
            float rigScale = (1f - _upperW) * (_actionIsTurn ? 1f : 1f - _actionW);
            RigApply(dt, rigScale);
            // bare hands crouch-walking: the chest follows the pelvis half-way and the twist is shared by the three spine bones (LocoPlan.Twist)
            float follow = LocoPlan.ChestFollowOf(lk, crouch, m) * _rigEff * (1f - _actionW);
            if (follow > 0.01f) TwistSpine(follow);
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
            // running: the pack's run / sprint cycles splay the thighs, and on her narrower hips that is a bow-legged, wide-footed stance -
            // each thigh is turned a little toward the middle about the hip (the Anim object's forward axis), the foot turned back so the sole stays flat
            float legsIn = Plugin.RunLegsIn.Value * m * _runW * (1f - crouch) * (1f - _actionW);
            if (Mathf.Abs(legsIn) > 0.05f)
            {
                Turn("mixamorig:LeftUpLeg", Vector3.forward, legsIn);     // +deg about forward swings a hanging leg toward +x (her right) = inward for the left leg
                Turn("mixamorig:LeftFoot", Vector3.forward, -legsIn);
                Turn("mixamorig:RightUpLeg", Vector3.forward, -legsIn);
                Turn("mixamorig:RightFoot", Vector3.forward, legsIn);
            }
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
