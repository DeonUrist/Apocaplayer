using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Apocaplayer
{
    public static partial class ModAPI
    {
        // One animated humanoid (ModAPI.Attach). The decisions are CharPlan's (the player's own logic, tested offline); this class applies them
        // like the player's Body does: a PlayableGraph on the character's Animator (layer 0 the 42 locomotion slots, 1 the whole-body actions,
        // 2 throws / strikes on the upper body), an invisible copy of its skeleton (the UpperRig) that plays the hands' clip and is written over its
        // spine and everything above, then the procedural touches (hips turn, chest follow, aim pitch, aim lift) and the weapon in the right hand.
        public sealed class Character
        {
            // ---------------------------------------------------------------- inputs (set them in Update; read in LateUpdate)
            public Animator Animator { get { return _a; } }
            public bool Valid { get { return !_disposed && _a != null; } }
            // the planar velocity: by default the character's Rigidbody's (else its movement since the last frame); ManualVelocity = use Velocity
            public bool ManualVelocity;
            public Vector3 Velocity;
            public bool Crouched, Aiming, Firing, Airborne;
            public float AimPitch;                                 // degrees, + = looking down (the spine bends like the player's aim pitch)
            public bool TurnInPlace { get { return _p.TurnInPlace; } set { _p.TurnInPlace = value; } }
            public bool WalkToStop { get { return _p.WalkToStop; } set { _p.WalkToStop = value; } }
            // ---------------------------------------------------------------- state (read)
            public string WeaponKey { get { return _wKey ?? ""; } }
            public string WeaponKind { get { return _kind.ToString(); } }
            public bool Reloading { get { return _p.Reloading; } }
            public bool Pumping { get { return _p.Pumping; } }
            public string HandsClip { get { return _p.RigEff > 0.01f ? _p.RigClip : ""; } }       // what moves the hands now (UpperRig)
            public string ActionClip { get { return _p.ActionW > 0.01f ? _p.ActionClip : ""; } }   // the whole-body action (kick, jump, turn)
            public string UpperClip { get { return _p.UpperW > 0.01f ? _p.UpperClip : ""; } }     // a throw / strike
            // the fastest the body may move without its feet sliding (twice the clips' native speed: crouched / sprinting)
            public float SpeedCap { get { return 2f * (Crouched ? Plugin.ClipCrouchSpeed.Value : Anims.Get("RifleSprint") != null ? Plugin.ClipSprintSpeed.Value : Plugin.ClipRunSpeed.Value); } }

            // ---------------------------------------------------------------- commands
            public void SetWeapon(Transform model) { SetWeapon(model, null); }
            // the weapon model it holds (null = bare hands); weaponKey null = from the model's name. The model is moved to the right hand and
            // posed per clip from the player's weapon-pose table; SetWeapon(null) / Suspended / Dispose put it back where it was.
            public void SetWeapon(Transform model, string weaponKey)
            {
                string key = weaponKey ?? (model != null ? Props.Norm(model.name) : "");
                if (model == _w && key == (_wKey ?? "")) return;
                ReleaseWeapon();
                _w = model; _wKey = key;
                _kind = model != null || key != "" ? Props.KindOf(key) : Props.Kind.None;
                _p.Kind = _kind == Props.Kind.Rifle ? LocoPlan.K_RIFLE : _kind == Props.Kind.Pistol ? LocoPlan.K_PISTOL : _kind == Props.Kind.Melee ? LocoPlan.K_MELEE
                        : _kind == Props.Kind.Throw ? LocoPlan.K_THROW : LocoPlan.K_NONE;
                if (!Suspended) GrabWeapon();
            }
            public void Shoot() { _p.SetNow(Time.time); _p.Shoot(0.25f); }
            public void Shoot(float seconds) { _p.SetNow(Time.time); _p.Shoot(seconds); }
            public void Reload(float seconds) { _p.SetNow(Time.time); _p.Reload(seconds, 0); }
            // rounds > 0: one round at a time (the reload clip's round part, rounds times over the seconds)
            public void Reload(float seconds, int rounds) { _p.SetNow(Time.time); _p.Reload(seconds, rounds); }
            public void CancelReload() { _p.SetNow(Time.time); _p.CancelReload(); }
            public void Pump() { _p.SetNow(Time.time); _p.Pump(); }                       // ShotgunPump on the hands (rifle kind; not while reloading)
            public void Jump() { _p.SetNow(Time.time); _p.Jump(); }                       // the take-off; keep Airborne true while in the air
            public void Kick() { _p.SetNow(Time.time); _p.Kick(); }
            public void Throw() { _p.SetNow(Time.time); _p.Throw(false); }                // the quick grenade, left hand
            public void Throw(bool rightHand) { _p.SetNow(Time.time); _p.Throw(rightHand); }
            public void Strike() { _p.SetNow(Time.time); _p.Strike(0.5f); }
            public void Strike(float seconds) { _p.SetNow(Time.time); _p.Strike(seconds); }   // a punch (bare hands) / melee swing taking that long
            // the Animator's own controller shows (the graph is taken down, the weapon put back); false = the player's clips again
            public bool Suspended
            {
                get { return _suspended; }
                set
                {
                    if (value == _suspended || _disposed) return;
                    _suspended = value;
                    if (value) { ReleaseWeapon(); DestroyGraph(); }
                    else { BuildGraph(); GrabWeapon(); _p.Snap(); }
                }
            }
            public void Dispose()
            {
                if (_disposed) return;
                ReleaseWeapon();
                DestroyGraph();
                try { if (_rg.IsValid()) _rg.Destroy(); } catch (Exception) { }
                if (_rigGo != null) UnityEngine.Object.Destroy(_rigGo);
                _disposed = true;
                ModAPI.Forget(this);
            }

            // ---------------------------------------------------------------- internals
            private readonly Animator _a;
            private readonly Transform _root;
            private readonly Rigidbody _rb;
            private readonly CharPlan _p = new CharPlan();
            private bool _disposed, _suspended, _rootMotionWas;
            private AnimatorCullingMode _cullingWas; private AnimatorUpdateMode _updateWas;
            private readonly Dictionary<HumanBodyBones, Transform> _b = new Dictionary<HumanBodyBones, Transform>();
            private AnimationClip _fallback;
            // main graph
            private PlayableGraph _g;
            private AnimationMixerPlayable _base;
            private readonly AnimationClipPlayable[] _bp = new AnimationClipPlayable[LocoPlan.BN];
            private AnimationLayerMixerPlayable _layers;
            private AnimationClipPlayable _act; private string _actName = "";
            private AnimationMixerPlayable _upMix; private AnimationClipPlayable _up0, _up1; private string _up0n = "", _up1n;
            // the UpperRig
            private PlayableGraph _rg; private GameObject _rigGo; private Animator _rigA; private AnimationPlayableOutput _rigOut;
            private AnimationMixerPlayable _rigMix; private AnimationClipPlayable _r0, _r1; private string _r0n = "", _r1n;
            private Transform _spine, _spineC, _rigLFoot, _rigRFoot;
            private readonly List<KeyValuePair<Transform, Transform>> _pairs = new List<KeyValuePair<Transform, Transform>>();
            // weapon
            private Transform _w, _wParent; private string _wKey = ""; private Props.Kind _kind = Props.Kind.None;
            private Vector3 _wPos, _wScale, _defPos; private Quaternion _wRot, _defRot; private bool _wMoved;
            private Vector3 _lastPos; private bool _hasLast;

            internal Character(Animator a)
            {
                _a = a; _root = a.transform;
                _rb = a.GetComponentInParent<Rigidbody>();
                foreach (HumanBodyBones hb in Enum.GetValues(typeof(HumanBodyBones)))
                {
                    if (hb == HumanBodyBones.LastBone) continue;
                    var t = a.GetBoneTransform(hb);
                    if (t != null) _b[hb] = t;
                }
                if (!_b.ContainsKey(HumanBodyBones.Hips) || !_b.ContainsKey(HumanBodyBones.Spine) || !_b.ContainsKey(HumanBodyBones.RightHand)) { _disposed = true; return; }
                _fallback = Anims.Get("RifleIdle");
                _p.Has = n => n != null && Anims.Get(n) != null;
                _p.Length = n => { var c = Anims.Get(n); return c != null ? c.length : 1f; };
                _p.Looping = n => { var c = Anims.Get(n); return c != null && c.isLooping; };
                _p.PhaseOf = PhaseOf;
                _p.NativeOf = NativeOf;
                ReadSettings();
                _p.Build();
                BuildRig();
                BuildGraph();
            }

            private void ReadSettings()
            {
                _p.RunFrom = Plugin.RunFrom.Value; _p.SprintFrom = Plugin.SprintFrom.Value; _p.DirBlendSpeed = Plugin.DirBlendSpeed.Value; _p.RunLegsTurn = Plugin.RunLegsTurn.Value;
                _p.UpperPhase = Plugin.UpperPhase.Value; _p.PumpSpeed = Plugin.PumpClipSpeed.Value; _p.StrikeWindup = Plugin.StrikeWindup.Value;
                _p.WalkNative = Plugin.ClipWalkSpeed.Value; _p.RunNative = Plugin.ClipRunSpeed.Value; _p.SprintNative = Plugin.ClipSprintSpeed.Value; _p.CrouchNative = Plugin.ClipCrouchSpeed.Value;
                _p.JumpClipStart = Plugin.JumpClipStart.Value; _p.TurnStartRate = Plugin.TurnStartRate.Value;
                _p.TurnInPlace = Plugin.TurnClipAngle.Value > 0f; _p.WalkToStop = Plugin.WalkToStop.Value;
            }

            // ---- the main graph (rebuilt when un-suspended)
            private void BuildGraph()
            {
                if (_g.IsValid() || _a == null) return;
                _g = PlayableGraph.Create("Apocaplayer.ModAPI." + _a.gameObject.name);
                _g.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
                var output = AnimationPlayableOutput.Create(_g, "body", _a);
                _base = AnimationMixerPlayable.Create(_g, LocoPlan.BN);
                for (int i = 0; i < LocoPlan.BN; i++)
                {
                    _bp[i] = AnimationClipPlayable.Create(_g, Anims.Get(_p.SlotClip[i]) ?? _fallback);
                    _bp[i].SetApplyFootIK(Plugin.FootIK.Value && (i < LocoPlan.B_RUN || (i >= LocoPlan.B_CIDLE && i != LocoPlan.B_RRUN && i != LocoPlan.B_RSPRINT)));
                    _bp[i].SetSpeed(0);
                    _g.Connect(_bp[i], 0, _base, i);
                    _base.SetInputWeight(i, i == LocoPlan.B_IDLE ? 1f : 0f);
                }
                _layers = AnimationLayerMixerPlayable.Create(_g, 3);
                _g.Connect(_base, 0, _layers, 0); _layers.SetInputWeight(0, 1f);
                _act = Clip(_g, _fallback); _actName = "";
                _g.Connect(_act, 0, _layers, 1); _layers.SetInputWeight(1, 0f);
                _upMix = AnimationMixerPlayable.Create(_g, 2);
                _up0 = Clip(_g, _fallback); _up0n = ""; _up1n = null;
                _g.Connect(_up0, 0, _upMix, 0); _upMix.SetInputWeight(0, 1f); _upMix.SetInputWeight(1, 0f);
                _g.Connect(_upMix, 0, _layers, 2); _layers.SetInputWeight(2, 0f);
                _layers.SetLayerMaskFromAvatarMask(2u, UpperMask());
                output.SetSourcePlayable(_layers);
                _rootMotionWas = _a.applyRootMotion; _a.applyRootMotion = false;
                // always evaluated while the player's clips drive it: the procedural turns after the Animator are applied to its pose of this
                // frame - a culled Animator would leave last frame's bones and they would add up
                _cullingWas = _a.cullingMode; _a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                _updateWas = _a.updateMode; _a.updateMode = AnimatorUpdateMode.Normal;      // every frame, not only on physics steps (same reason)
                _g.Play();
                _p.Snap();
            }
            private void DestroyGraph()
            {
                try { if (_g.IsValid()) { _g.Destroy(); if (_a != null) { _a.applyRootMotion = _rootMotionWas; _a.cullingMode = _cullingWas; _a.updateMode = _updateWas; } } } catch (Exception) { }
            }
            private static AnimationClipPlayable Clip(PlayableGraph g, AnimationClip c)
            {
                var p = AnimationClipPlayable.Create(g, c);
                p.SetApplyFootIK(false); p.SetSpeed(0);
                return p;
            }
            private static AvatarMask _mask;
            private static AvatarMask UpperMask()
            {
                if (_mask != null) return _mask;
                _mask = new AvatarMask { hideFlags = HideFlags.DontUnloadUnusedAsset };
                foreach (AvatarMaskBodyPart part in Enum.GetValues(typeof(AvatarMaskBodyPart)))
                {
                    if (part == AvatarMaskBodyPart.LastBodyPart) continue;
                    bool upper = part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.Head || part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm
                              || part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers || part == AvatarMaskBodyPart.LeftHandIK || part == AvatarMaskBodyPart.RightHandIK;
                    _mask.SetHumanoidBodyPartActive(part, upper);
                }
                return _mask;
            }

            // ---- the UpperRig: an invisible, stripped copy of the character's Animator object (bones + Animator only), on its own manual graph
            private static GameObject _rigRoot;
            private void BuildRig()
            {
                try
                {
                    if (_rigRoot == null) { _rigRoot = new GameObject("Apocaplayer.ModAPI.Rigs") { hideFlags = HideFlags.HideAndDontSave }; UnityEngine.Object.DontDestroyOnLoad(_rigRoot); }
                    var holder = new GameObject("Apocaplayer.ModAPI.RigHolder");
                    holder.SetActive(false);                       // nothing in the copy wakes up before it is stripped
                    var go = UnityEngine.Object.Instantiate(_a.gameObject, holder.transform, false);
                    go.name = "UpperRig." + _a.gameObject.name;
                    var keep = go.GetComponent<Animator>();
                    for (int pass = 0; pass < 3; pass++)
                        foreach (var c in go.GetComponentsInChildren<Component>(true))
                        {
                            if (c == null || c is Transform || c == keep) continue;
                            try { UnityEngine.Object.DestroyImmediate(c); } catch (Exception) { }
                        }
                    // nothing in the game may take the copy for the character: no tag, no layer, hidden
                    foreach (var t in go.GetComponentsInChildren<Transform>(true))
                    {
                        t.gameObject.layer = 0;
                        try { t.gameObject.tag = "Untagged"; } catch (Exception) { }
                        t.gameObject.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
                    }
                    go.transform.SetParent(_rigRoot.transform, false);
                    go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity; go.transform.localScale = _root.lossyScale;
                    go.SetActive(true);
                    UnityEngine.Object.Destroy(holder);
                    _rigGo = go; _rigA = keep;
                    if (_rigA == null) { UnityEngine.Object.Destroy(go); _rigGo = null; return; }
                    _rigA.runtimeAnimatorController = null;
                    _rigA.applyRootMotion = false; _rigA.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    _rigA.enabled = true;
                    _spine = _b[HumanBodyBones.Spine]; _spineC = _rigA.GetBoneTransform(HumanBodyBones.Spine);
                    _rigLFoot = _rigA.GetBoneTransform(HumanBodyBones.LeftFoot); _rigRFoot = _rigA.GetBoneTransform(HumanBodyBones.RightFoot);
                    if (_spineC == null) { UnityEngine.Object.Destroy(go); _rigGo = null; return; }
                    // the skeleton above the spine: every bone its skinned meshes use (Spine1 / Spine2 too when the avatar maps only some of them) and every
                    // humanoid bone, found in the copy by its path - props under the hands (the gun moved to the right hand ...) are not bones and are left alone
                    var bones = new HashSet<Transform>();
                    foreach (var smr in _a.GetComponentsInChildren<SkinnedMeshRenderer>(true)) foreach (var t in smr.bones) if (t != null) bones.Add(t);
                    foreach (var kv in _b) bones.Add(kv.Value);
                    foreach (var t in bones)
                    {
                        if (t == _spine || !t.IsChildOf(_spine)) continue;
                        var c = go.transform.Find(PathOf(t, _root));
                        if (c != null) _pairs.Add(new KeyValuePair<Transform, Transform>(t, c));
                    }
                    _rg = PlayableGraph.Create("Apocaplayer.ModAPI.UpperRig." + _a.gameObject.name);
                    _rg.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    _rigOut = AnimationPlayableOutput.Create(_rg, "upper", _rigA);
                    _rigMix = AnimationMixerPlayable.Create(_rg, 2);
                    _r0 = Clip(_rg, _fallback); _r0n = ""; _r1n = null;
                    _rg.Connect(_r0, 0, _rigMix, 0); _rigMix.SetInputWeight(0, 1f); _rigMix.SetInputWeight(1, 0f);
                    _rigOut.SetSourcePlayable(_rigMix);
                    _rg.Play();
                }
                catch (Exception e) { Plugin.Log.LogWarning("ModAPI: no upper rig for " + _a.gameObject.name + " (" + e.Message + ") - the legs' clips move its hands"); _pairs.Clear(); _rigGo = null; }
            }

            private static string PathOf(Transform t, Transform root)
            {
                var sb = new System.Text.StringBuilder(t.name);
                for (var x = t.parent; x != null && x != root; x = x.parent) sb.Insert(0, x.name + "/");
                return sb.ToString();
            }

            // ---- per clip, measured once on a rig (shared by every character): the stride phase, the native ground speed
            private static readonly Dictionary<string, float> _phases = new Dictionary<string, float>(), _natives = new Dictionary<string, float>();
            private float PhaseOf(string clip)
            {
                float p;
                if (string.IsNullOrEmpty(clip)) return -1f;
                if (_phases.TryGetValue(clip, out p)) return p;
                if (!_rg.IsValid()) return -1f;                      // no rig to measure on: not cached, another character may measure it
                p = Measure(Anims.Get(clip));
                _phases[clip] = p;
                return p;
            }
            private float Measure(AnimationClip c)
            {
                if (c == null || !_rigOut.IsOutputValid() || _rigLFoot == null || _rigRFoot == null) return -1f;
                var tmp = default(AnimationClipPlayable);
                try
                {
                    tmp = AnimationClipPlayable.Create(_rg, c);
                    tmp.SetApplyFootIK(false); tmp.SetSpeed(0);
                    _rigOut.SetSourcePlayable(tmp);
                    var d = new float[24];
                    var rt = _rigA.transform;
                    for (int k = 0; k < d.Length; k++)
                    {
                        tmp.SetTime(k * c.length / d.Length);
                        _rg.Evaluate(0f);
                        d[k] = rt.InverseTransformPoint(_rigLFoot.position).y - rt.InverseTransformPoint(_rigRFoot.position).y;
                    }
                    return LocoPlan.PhaseFromHeights(d);
                }
                catch (Exception) { return -1f; }
                finally
                {
                    if (_rigOut.IsOutputValid()) _rigOut.SetSourcePlayable(_rigMix);
                    if (tmp.IsValid()) tmp.Destroy();
                }
            }
            private static float NativeOf(string clip, float fallback)
            {
                float v;
                if (clip == null) return fallback;
                if (!_natives.TryGetValue(clip, out v))
                {
                    var c = Anims.Get(clip);
                    v = c != null ? new Vector2(c.averageSpeed.x, c.averageSpeed.z).magnitude : 0f;
                    _natives[clip] = v;
                }
                return v > 0.3f ? v : fallback;
            }

            // ---- the weapon in the right hand
            private void GrabWeapon()
            {
                if (_w == null || _wMoved || _kind == Props.Kind.None) return;
                Transform hand; if (!_b.TryGetValue(HumanBodyBones.RightHand, out hand)) return;
                Transform left; _b.TryGetValue(HumanBodyBones.LeftHand, out left);
                _wParent = _w.parent; _wPos = _w.localPosition; _wRot = _w.localRotation; _wScale = _w.localScale;
                // without an entry in the pose table: its own grip (a left-hand gun mirrored into the right hand, as the player's props)
                if (left != null && _wParent == left) Props.MirrorToRight(_wPos, _wRot, out _defPos, out _defRot);
                else if (_wParent == hand) { _defPos = _wPos; _defRot = _wRot; }
                else { _defPos = hand.InverseTransformPoint(_w.position); _defRot = Quaternion.Inverse(hand.rotation) * _w.rotation; }
                _w.SetParent(hand, false);
                _w.localScale = _wScale;
                _wMoved = true;
                PoseWeapon();
            }
            private void ReleaseWeapon()
            {
                if (!_wMoved) return;
                _wMoved = false;
                if (_w != null) { _w.SetParent(_wParent, false); _w.localPosition = _wPos; _w.localRotation = _wRot; _w.localScale = _wScale; }
            }
            private void PoseWeapon()
            {
                if (!_wMoved || _w == null) return;
                Vector3 p; Quaternion r;
                if (_p.PoseW.Count == 0) { var d = new Dictionary<string, float>(); d[IdleOf(_kind)] = 1f; GunPose.BlendFor(_wKey, d, IdleOf(_kind), _defPos, _defRot, out p, out r); }
                else GunPose.BlendFor(_wKey, _p.PoseW, IdleOf(_kind), _defPos, _defRot, out p, out r);
                _w.localPosition = p; _w.localRotation = r;
            }

            // ---------------------------------------------------------------- every frame (LateUpdate, after the Animator)
            private int _errors;
            internal void Tick()
            {
                if (_disposed) return;
                if (_a == null) { Dispose(); return; }
                if (_suspended) { _hasLast = false; return; }
                if (!_a.isActiveAndEnabled) { _hasLast = false; return; }
                try { TickInner(); }
                catch (Exception e)
                {
                    if (++_errors <= 3) Plugin.Log.LogError("ModAPI " + _a.gameObject.name + ": " + e);
                    if (_errors >= 3) { Plugin.Log.LogWarning("ModAPI: " + _a.gameObject.name + " suspended after errors"); Suspended = true; }
                }
            }

            private void TickInner()
            {
                if (!_g.IsValid()) BuildGraph();
                float dt = Time.deltaTime, now = Time.time;
                if (dt <= 0f) return;                                            // paused: the pose stays (nothing evaluated, nothing added)
                if (_w == null && _wMoved) _wMoved = false;                      // the weapon model was destroyed
                // velocity in its own frame
                Vector3 fwd = _root.forward; fwd.y = 0f;
                if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
                var yaw = Quaternion.LookRotation(fwd.normalized, Vector3.up);
                Vector3 v;
                if (ManualVelocity) v = Velocity;
                else if (_rb != null && !_rb.isKinematic) v = _rb.velocity;
                else v = _hasLast && dt > 1e-4f ? (_root.position - _lastPos) / dt : Vector3.zero;
                _lastPos = _root.position; _hasLast = true;
                v.y = 0f;
                var local = Quaternion.Inverse(yaw) * v;
                _p.Lx = local.x; _p.Lz = local.z; _p.Yaw = yaw.eulerAngles.y;
                _p.Crouch = Crouched; _p.Aim = Aiming; _p.Fire = Firing; _p.Airborne = Airborne;
                _p.Step(now, dt);
                if (Plugin.VerboseLog.Value && _p.Log != "") Plugin.Log.LogInfo("ModAPI " + _a.gameObject.name + ": " + _p.Log);

                // ---- next frame's graph (the Animator evaluates it before the next LateUpdate)
                for (int i = 0; i < LocoPlan.BN; i++)
                {
                    _base.SetInputWeight(i, _p.W[i]);
                    if (_p.W[i] > 0.0001f) _bp[i].SetTime(_p.SlotTime[i]);
                }
                if (_p.ActionClip != "" && _p.ActionClip != _actName)
                {
                    _g.Disconnect(_layers, 1); _act.Destroy();
                    _act = Clip(_g, Anims.Get(_p.ActionClip) ?? _fallback); _actName = _p.ActionClip;
                    _g.Connect(_act, 0, _layers, 1);
                }
                _layers.SetInputWeight(1, _p.ActionW);
                _act.SetTime(_p.ActionTime);
                Sync2(_g, _upMix, ref _up0, ref _up0n, ref _up1, ref _up1n, _p.UpperClip, _p.UpperOld);
                _layers.SetInputWeight(2, _p.UpperW);
                _up0.SetTime(_p.UpperTime); if (_up1.IsValid()) _up1.SetTime(_p.UpperOldTime);
                _upMix.SetInputWeight(0, _up1.IsValid() ? _p.UpperX : 1f); _upMix.SetInputWeight(1, _up1.IsValid() ? 1f - _p.UpperX : 0f);

                // ---- this frame's pose (the Animator evaluated it): the hands from the rig, then the procedural touches
                if (Mathf.Abs(_p.HipTurn) > 0.2f && _p.ActionW < 0.5f)
                {
                    Turn(HumanBodyBones.Hips, Vector3.up, _p.HipTurn);
                    if (_p.RigWant < 0.5f) { Turn(HumanBodyBones.Spine, Vector3.up, -_p.HipTurn * 0.5f); Turn(HumanBodyBones.Chest, Vector3.up, -_p.HipTurn * 0.5f); }
                }
                RigApply();
                if (_p.ChestFollow > 0.01f) TwistSpine(_p.ChestFollow);
                // the hips stay over the character (clips with forward motion baked into the pose)
                Transform hips;
                if (_b.TryGetValue(HumanBodyBones.Hips, out hips))
                {
                    var hl = _root.InverseTransformPoint(hips.position);
                    var xz = Vector2.ClampMagnitude(new Vector2(hl.x, hl.z), Plugin.HipsDrift.Value);
                    if (xz.x != hl.x || xz.y != hl.z) hips.position = _root.TransformPoint(new Vector3(xz.x, hl.y, xz.y));
                }
                float pitch = AimPitch * Plugin.AimPitchShare.Value;
                if (Mathf.Abs(pitch) > 0.5f)
                {
                    Turn(HumanBodyBones.Spine, Vector3.right, pitch * 0.3f);
                    Turn(HumanBodyBones.Chest, Vector3.right, pitch * 0.3f);
                    Turn(HumanBodyBones.UpperChest, Vector3.right, pitch * 0.3f);
                    Turn(HumanBodyBones.Neck, Vector3.right, pitch * 0.1f);
                }
                AimLiftApply();
                PoseWeapon();
            }

            // the two-input mixer to show 'cur' (input 0) with 'old' fading out under it (input 1)
            private AnimationClipPlayable Make(PlayableGraph g, string name) { return Clip(g, Anims.Get(name) ?? _fallback); }
            private void Sync2(PlayableGraph g, AnimationMixerPlayable mix, ref AnimationClipPlayable c0, ref string n0, ref AnimationClipPlayable c1, ref string n1, string cur, string old)
            {
                if (string.IsNullOrEmpty(cur)) cur = n0;                   // nothing new: keep the last one (fading out by the layer weight)
                if (cur != n0)
                {
                    if (old != null && old == n0)
                    {   // the shown clip becomes the one fading out
                        if (c1.IsValid()) { g.Disconnect(mix, 1); c1.Destroy(); }
                        g.Disconnect(mix, 0);
                        c1 = c0; n1 = n0; g.Connect(c1, 0, mix, 1);
                    }
                    else { g.Disconnect(mix, 0); if (c0.IsValid()) c0.Destroy(); }
                    c0 = Make(g, cur); n0 = cur; g.Connect(c0, 0, mix, 0);
                }
                if (old != n1)
                {
                    if (c1.IsValid()) { g.Disconnect(mix, 1); c1.Destroy(); }
                    c1 = default(AnimationClipPlayable); n1 = null;
                    if (old != null && old != cur) { c1 = Make(g, old); n1 = old; g.Connect(c1, 0, mix, 1); }
                }
            }

            private void RigApply()
            {
                float eff = _p.RigEff;
                if (!_rg.IsValid() || _spineC == null || eff < 0.001f || string.IsNullOrEmpty(_p.RigClip)) return;
                Sync2(_rg, _rigMix, ref _r0, ref _r0n, ref _r1, ref _r1n, _p.RigClip, _p.RigOld);
                _r0.SetTime(_p.RigTime); if (_r1.IsValid()) _r1.SetTime(_p.RigOldTime);
                _rigMix.SetInputWeight(0, _r1.IsValid() ? _p.RigX : 1f); _rigMix.SetInputWeight(1, _r1.IsValid() ? 1f - _p.RigX : 0f);
                _rg.Evaluate(0f);
                var target = _root.rotation * (Quaternion.Inverse(_rigA.transform.rotation) * _spineC.rotation);
                _spine.rotation = Quaternion.Slerp(_spine.rotation, target, eff);
                for (int i = 0; i < _pairs.Count; i++)
                {
                    var pr = _pairs[i];
                    pr.Key.localRotation = Quaternion.Slerp(pr.Key.localRotation, pr.Value.localRotation, eff);
                }
            }

            private void AimLiftApply()
            {
                if (_p.AimLiftW < 0.001f || _p.AimLiftClip == null || string.IsNullOrEmpty(_wKey)) return;
                var v = AimLift.Get(_wKey, _p.AimLiftClip, _p.AimCrouched);
                float w = _p.AimLiftW;
                var d = (_root.up * v[0] + _root.forward * v[2]) * (0.01f * w);
                if (d.sqrMagnitude > 1e-8f)
                {
                    LiftArm(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, d);
                    LiftArm(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, d);
                }
                if (Mathf.Abs(v[1]) > 0.01f) Turn(HumanBodyBones.Head, Vector3.right, v[1] * w);
            }
            private void LiftArm(HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones hand, Vector3 d)
            {
                Transform s, e, h;
                if (!_b.TryGetValue(upper, out s) || !_b.TryGetValue(lower, out e) || !_b.TryGetValue(hand, out h)) return;
                var hr = h.rotation;
                var t = h.position + d;
                var ne = AimLift.Elbow(D3(s.position), D3(e.position), D3(h.position), D3(t));
                var E = new Vector3((float)ne[0], (float)ne[1], (float)ne[2]);
                s.rotation = Quaternion.FromToRotation(e.position - s.position, E - s.position) * s.rotation;
                e.rotation = Quaternion.FromToRotation(h.position - e.position, t - e.position) * e.rotation;
                h.rotation = hr;
            }
            private static double[] D3(Vector3 p) { return new double[] { p.x, p.y, p.z }; }

            private float YawIn(Transform a, Transform b)
            {
                var s = _root.InverseTransformDirection(b.position - a.position); s.y = 0f;
                var f = Vector3.Cross(s, Vector3.up);
                return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            }
            private void TwistSpine(float follow)
            {
                Transform lh, rh, la, ra;
                if (!_b.TryGetValue(HumanBodyBones.LeftUpperLeg, out lh) || !_b.TryGetValue(HumanBodyBones.RightUpperLeg, out rh)
                    || !_b.TryGetValue(HumanBodyBones.LeftUpperArm, out la) || !_b.TryGetValue(HumanBodyBones.RightUpperArm, out ra)) return;
                float s0, s1, s2, nk;
                LocoPlan.Twist(YawIn(lh, rh), YawIn(la, ra), follow, out s0, out s1, out s2, out nk);
                Turn(HumanBodyBones.Spine, Vector3.up, s0);
                Turn(HumanBodyBones.Chest, Vector3.up, s1);
                Turn(HumanBodyBones.UpperChest, Vector3.up, s2);
                Turn(HumanBodyBones.Neck, Vector3.up, nk);
            }
            // a bone turned by deg about an axis given in the character's own space (Body.Turn)
            private void Turn(HumanBodyBones bone, Vector3 axis, float deg)
            {
                Transform t;
                if (!_b.TryGetValue(bone, out t)) return;
                var q = Quaternion.identity;
                for (var x = t; x != null && x != _root; x = x.parent) q = x.localRotation * q;
                t.localRotation = t.localRotation * Quaternion.Inverse(q) * Quaternion.AngleAxis(deg, axis) * q;
            }
        }
    }
}
