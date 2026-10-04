using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace FemalePlayer
{
    // Her body: a clone of Flexa's "Anim" object (Animator with the humanoid avatar enemy_1_IdleAvatar + the 22 mixamorig bones +
    // the skinned mesh), stripped of props/colliders/FSMs, with our mesh and texture. Animated by a PlayableGraph with the game's own
    // humanoid clips (idle, run, rifle / pistol aim, melee swing), plus procedural layers in LateUpdate: walk cycle, strafe twist,
    // aim pitch, crouch, prone. In a car the Animator is off and the pose is copied from the car's seated driver (CarSeat).
    internal sealed class Body
    {
        public GameObject Root;          // mirror lives here (scale x -1)
        private Transform _anim;         // clone of Flexa/Anim
        private Animator _animator;
        private SkinnedMeshRenderer _smr, _shadow;
        private Material _mat;
        public readonly Dictionary<string, Transform> Bones = new Dictionary<string, Transform>();
        private readonly Dictionary<string, Quaternion> _bindLocal = new Dictionary<string, Quaternion>();

        private PlayableGraph _graph;
        private AnimationMixerPlayable _loco;
        private AnimationLayerMixerPlayable _layers;
        private AnimationClipPlayable _upper;
        private string _upperClip = "";
        private AnimationClip _fallback;
        private bool _mixamo;                       // locomotion from the animation bundle (Mixamo clips) instead of the game's clips + procedural walk
        private LocoSet _unarmed, _rifle, _fire;   // _fire: RifleFire* clips = walking/crouching while shooting (full body)
        private float _fireW;
        private AnimationMixerPlayable _sets;
        private float _armW, _reloadUntil, _throwUntil, _reloadStart;
        private bool _reloading;
        private string _kickState = "", _jumpState = "";
        private AnimationClipPlayable _action;
        private string _actionClip = "";
        private float _actionUntil, _actionW;

        private void StartAction(string name)
        {
            var c = Anims.Get(name);
            if (c == null || !_action.IsValid()) return;
            if (_actionClip != name)
            {
                _actionClip = name;
                _graph.Disconnect(_layers, 2);
                _action.Destroy();
                _action = AnimationClipPlayable.Create(_graph, c);
                _action.SetApplyFootIK(true);
                _graph.Connect(_action, 0, _layers, 2);
            }
            _action.SetTime(0); _action.SetSpeed(1);
            _actionUntil = Time.time + c.length;
        }

        private void UpdateAction(float dt)
        {
            if (!_action.IsValid()) return;
            bool on = Time.time < _actionUntil - 0.15f;
            _actionW = Mathf.MoveTowards(_actionW, on ? 1f : 0f, dt * (on ? 10f : 6f));
            _layers.SetInputWeight(2, _actionW);
        }
        private string _grenadeState = "";

        private void StartThrow()
        {
            var c = Anims.Get("Throw");
            if (c == null) return;
            _throwUntil = Time.time + Mathf.Clamp(c.length, 0.5f, 2f);
            _upperClip = "";   // restart the clip
        }
        private readonly Dictionary<string, AnimationClip> _clips = new Dictionary<string, AnimationClip>();

        private float _phase, _speedSmooth, _strafeSmooth, _crouch, _prone, _runW;
        private float _legThigh, _legShin;
        private bool _inCar, _snap;
        private View _lastView = View.FirstPerson;
        private GameObject _prop; private string _propFor = ""; private Vector3 _propBarrel, _propPos; private Quaternion _propRot; private Transform _propHand; private bool _gripDone; private float _propSince;

        public enum View { FirstPerson, ThirdPerson }

        // ---------------------------------------------------------------- build
        public static Body Create()
        {
            var flexa = FlexaPrefab();
            if (flexa == null) return null;
            var animSrc = flexa.transform.Find("Anim");
            if (animSrc == null) { Plugin.Log.LogError("Flexa prefab has no Anim child"); return null; }

            var b = new Body();
            b.Root = new GameObject("FemalePlayerBody");
            b.Root.SetActive(false);   // nothing in the clone wakes up until it is stripped
            var clone = UnityEngine.Object.Instantiate(animSrc.gameObject, b.Root.transform, false);
            clone.name = "Anim";
            clone.transform.localPosition = Vector3.zero;
            clone.transform.localRotation = Quaternion.identity;
            b._anim = clone.transform;

            // props (beard, headband, bags, guns ...) out, then every component that isn't the skeleton or its renderer
            var props = new List<GameObject>();
            foreach (var mf in clone.GetComponentsInChildren<MeshFilter>(true)) props.Add(mf.gameObject);
            foreach (var p in props) if (p != null) UnityEngine.Object.DestroyImmediate(p);
            for (int pass = 0; pass < 2; pass++)
                foreach (var c in clone.GetComponentsInChildren<Component>(true))
                {
                    if (c == null || c is Transform || c is Animator || c is SkinnedMeshRenderer) continue;
                    try { UnityEngine.Object.DestroyImmediate(c); } catch (Exception) { }
                }
            var smrs = clone.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var s in smrs) if (s.bones.Length >= 20) { b._smr = s; break; }
            foreach (var s in smrs) if (s != b._smr) UnityEngine.Object.DestroyImmediate(s.gameObject);
            if (b._smr == null) { Plugin.Log.LogError("Flexa's body renderer not found"); UnityEngine.Object.Destroy(b.Root); return null; }
            foreach (var t in clone.GetComponentsInChildren<Transform>(true)) { t.gameObject.layer = 0; if (t.name.StartsWith("mixamorig:")) b.Bones[t.name] = t; }

            if (!Model.Build(b._smr)) { UnityEngine.Object.Destroy(b.Root); return null; }
            b._mat = new Material(b._smr.sharedMaterial) { name = "FemalePlayer body" };
            var tex = Model.Body();
            if (tex != null) b._mat.mainTexture = tex;
            b._smr.sharedMesh = Model.NoArms;
            b._smr.sharedMaterial = b._mat;
            b._smr.updateWhenOffscreen = true;
            b._smr.localBounds = new Bounds(Vector3.zero, Vector3.one * 2.5f);
            b._smr.gameObject.SetActive(true);

            var sh = new GameObject("FemalePlayerShadow");
            sh.transform.SetParent(b._smr.transform.parent, false);
            b._shadow = sh.AddComponent<SkinnedMeshRenderer>();
            b._shadow.bones = b._smr.bones;
            b._shadow.rootBone = b._smr.rootBone;
            b._shadow.sharedMesh = Model.Full;
            b._shadow.sharedMaterial = b._mat;
            b._shadow.updateWhenOffscreen = true;
            b._shadow.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;

            // bind-pose local rotations (for the car pose's unmapped bones)
            var bindWorld = new Dictionary<string, Matrix4x4>();
            foreach (var kv in Bindposes.Human)
            {
                var m = Matrix4x4.identity;
                for (int r = 0; r < 3; r++) for (int c = 0; c < 4; c++) m[r, c] = kv.Value[r * 4 + c];
                bindWorld[kv.Key] = m.inverse;
            }
            foreach (var kv in b.Bones)
            {
                var par = kv.Value.parent;
                if (par == null || !bindWorld.ContainsKey(kv.Key) || !bindWorld.ContainsKey(par.name)) continue;
                var local = bindWorld[par.name].inverse * bindWorld[kv.Key];
                b._bindLocal[kv.Key] = local.rotation;
            }

            b._animator = clone.GetComponent<Animator>();
            b._animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            b._animator.applyRootMotion = false;
            b.Root.SetActive(true);
            b.BuildGraph(flexa);
            b.MeasureLegs();
            Plugin.Log.LogInfo("Body built from Flexa's skeleton (" + b.Bones.Count + " bones)");
            return b;
        }

        private static GameObject _flexa;
        internal static GameObject FlexaPrefab()
        {
            if (_flexa != null) return _flexa;
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                if (go != null && go.name == "Flexa" && !go.scene.IsValid() && go.transform.parent == null && go.GetComponent<Rigidbody>() != null)
                { _flexa = go; break; }
            return _flexa;
        }

        private AnimationClip Clip(string name)
        {
            AnimationClip c;
            if (string.IsNullOrEmpty(name)) return null;
            if (_clips.TryGetValue(name, out c)) return c;
            c = Anims.Get(name);
            if (c != null) { _clips[name] = c; return c; }
            foreach (var a in Resources.FindObjectsOfTypeAll<AnimationClip>())
                if (a != null && a.name == name && a.humanMotion) { c = a; break; }
            if (c == null)
                foreach (var a in Resources.FindObjectsOfTypeAll<AnimationClip>())
                    if (a != null && a.name == name) { c = a; break; }
            if (c == null) Plugin.Verbose("Animation clip " + name + " not found");
            _clips[name] = c;
            return c;
        }

        private void BuildGraph(GameObject flexa)
        {
            _graph = PlayableGraph.Create("FemalePlayer");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var output = AnimationPlayableOutput.Create(_graph, "body", _animator);
            _mixamo = Anims.Loaded && Anims.Get("Idle") != null && Anims.Get("Walk") != null;
            if (_mixamo) { BuildMixamo(output); return; }
            _loco = AnimationMixerPlayable.Create(_graph, 2);
            var idle = Clip(Plugin.IdleClip.Value);
            var run = Clip(Plugin.RunClip.Value);
            if (idle == null) idle = Clip("enemy_1_idle");
            if (run == null) run = idle;
            _fallback = idle;
            var pIdle = AnimationClipPlayable.Create(_graph, idle);
            var pRun = AnimationClipPlayable.Create(_graph, run);
            pIdle.SetApplyFootIK(false); pRun.SetApplyFootIK(false);
            _graph.Connect(pIdle, 0, _loco, 0);
            _graph.Connect(pRun, 0, _loco, 1);
            _loco.SetInputWeight(0, 1f); _loco.SetInputWeight(1, 0f);

            _layers = AnimationLayerMixerPlayable.Create(_graph, 2);
            _graph.Connect(_loco, 0, _layers, 0);
            _layers.SetInputWeight(0, 1f);
            _upper = AnimationClipPlayable.Create(_graph, idle);
            _graph.Connect(_upper, 0, _layers, 1);
            _layers.SetInputWeight(1, 0f);
            var mask = new AvatarMask();
            foreach (AvatarMaskBodyPart part in Enum.GetValues(typeof(AvatarMaskBodyPart)))
            {
                if (part == AvatarMaskBodyPart.LastBodyPart) continue;
                bool upper = part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.Head || part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm
                          || part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers || part == AvatarMaskBodyPart.LeftHandIK || part == AvatarMaskBodyPart.RightHandIK;
                mask.SetHumanoidBodyPartActive(part, upper);
            }
            _layers.SetLayerMaskFromAvatarMask(1, mask);
            output.SetSourcePlayable(_layers);
            _graph.Play();
            Plugin.Verbose("Animation graph: idle " + (idle != null ? idle.name : "-") + ", run " + (run != null ? run.name : "-"));
        }

        // melee swing in third person timed to the first-person one: the drawn weapon's arm Animator switches to its swing state on the click;
        // its length (s) = how long her swing lasts, and her clip is sped up to fit it (the raider/Mixamo swings are much slower than the game's)
        private Animator _meleeFp;
        private int _meleeHash0;
        private bool _meleeMeasuring;
        private float _meleeStart, _meleeLen = 0.5f, _meleeSpeed = 1f;
        private void StartMelee(string weapon)
        {
            _meleeStart = Time.time; _upperClip = ""; _meleeLen = 0.5f; _meleeMeasuring = false; _meleeFp = null;
            var w = Game.WeaponsParent != null && !string.IsNullOrEmpty(weapon) ? Game.WeaponsParent.Find(weapon) : null;
            if (w != null) _meleeFp = w.GetComponentInChildren<Animator>();
            if (_meleeFp != null && _meleeFp.isActiveAndEnabled) { _meleeHash0 = _meleeFp.GetCurrentAnimatorStateInfo(0).fullPathHash; _meleeMeasuring = true; }
        }

        private bool UpdateMelee(string clipName)
        {
            if (_meleeStart <= 0f) return false;
            if (_meleeMeasuring && _meleeFp != null && Time.time - _meleeStart < 0.3f)
            {
                var st = _meleeFp.IsInTransition(0) ? _meleeFp.GetNextAnimatorStateInfo(0) : _meleeFp.GetCurrentAnimatorStateInfo(0);
                if (st.fullPathHash != _meleeHash0 && st.length > 0.05f)
                {
                    float sp = Mathf.Abs(_meleeFp.speed * st.speedMultiplier);
                    _meleeLen = Mathf.Clamp(st.length / Mathf.Max(0.1f, sp), 0.2f, 2f);
                    _meleeMeasuring = false;
                    Plugin.Verbose("Melee: first-person swing " + _meleeLen.ToString("0.00") + " s");
                }
            }
            else _meleeMeasuring = false;
            var c = Clip(clipName);
            _meleeSpeed = c != null ? Mathf.Clamp(c.length / _meleeLen, 0.5f, 5f) : 1f;
            if (Time.time - _meleeStart < _meleeLen) return true;
            _meleeStart = 0f;
            return false;
        }

        private void SetUpper(string clipName, float weight, float speed)
        {
            if (string.IsNullOrEmpty(clipName)) { _layers.SetInputWeight(1, 0f); return; }   // keep the last clip connected, just off
            if (clipName != _upperClip)
            {
                _upperClip = clipName;
                _graph.Disconnect(_layers, 1);
                if (_upper.IsValid()) _upper.Destroy();
                var c = Clip(clipName) ?? _fallback;
                _upper = AnimationClipPlayable.Create(_graph, c);
                _upper.SetApplyFootIK(false);
                _graph.Connect(_upper, 0, _layers, 1);
                _upper.SetTime(0);
            }
            _layers.SetInputWeight(1, weight);
            if (_upper.IsValid()) _upper.SetSpeed(speed);
        }

        private void MeasureLegs()
        {
            Transform up, leg, foot;
            if (Bones.TryGetValue("mixamorig:LeftUpLeg", out up) && Bones.TryGetValue("mixamorig:LeftLeg", out leg) && Bones.TryGetValue("mixamorig:LeftFoot", out foot))
            { _legThigh = leg.localPosition.magnitude; _legShin = foot.localPosition.magnitude; }
            if (_legThigh < 0.1f) _legThigh = 0.44f;
            if (_legShin < 0.1f) _legShin = 0.40f;
        }

        public void Destroy()
        {
            try { if (_graph.IsValid()) _graph.Destroy(); } catch (Exception) { }
            if (Root != null) UnityEngine.Object.Destroy(Root);
            Root = null;
        }

        public bool Alive { get { return Root != null && _anim != null; } }

        // ---------------------------------------------------------------- per frame
        public void SetVisible(bool body, bool shadow)
        {
            if (_smr.enabled != body) _smr.enabled = body;
            if (_shadow.enabled != shadow) _shadow.enabled = shadow;
        }

        // on foot: place, animate (graph is already evaluated by the Animator this frame), then the procedural layers
        public void LateFoot(View view, float dt)
        {
            if (_inCar) { _inCar = false; _animator.enabled = true; }
            var player = Game.Player.transform;
            Vector3 fwd = Game.PlayerCamera.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = player.forward;
            var yaw = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            float standH = Game.StandingHeight;
            string mstate = Game.MoveState;
            bool crawl = mstate.StartsWith("Crawl") || standH < 0.75f;
            bool crouch = !crawl && (mstate.StartsWith("Crouch") || standH < 1.4f);
            _crouch = Mathf.MoveTowards(_crouch, crouch ? 1f : 0f, dt * 4f);
            _prone = Mathf.MoveTowards(_prone, crawl ? 1f : 0f, dt * 3f);

            // feet at the bottom of the capsule (the Movement FSM keeps the capsule centred on the Player, height = standingHeight)
            Vector3 feet = player.position - Vector3.up * (standH * 0.5f);
            Vector3 pos = feet;
            float camPitch = Mathf.DeltaAngle(0f, Game.PlayerCamera.eulerAngles.x);   // + = looking down
            var rot = yaw;
            if (_prone > 0f)
            {
                // lying face down, head toward the camera
                rot = yaw * Quaternion.Euler(82f * _prone, 0f, 0f);
                pos = feet - yaw * Vector3.forward * (1.45f * _prone) + Vector3.up * (0.12f * _prone);
            }
            Root.transform.SetPositionAndRotation(pos, rot);
            bool mirror = Plugin.MirrorBody.Value && !_mixamo;   // Mixamo clips hold guns right-handed already
            Root.transform.localScale = new Vector3(mirror ? -1f : 1f, 1f, 1f);
            _anim.localPosition = Vector3.zero; _anim.localRotation = Quaternion.identity;

            if (view != _lastView) { _lastView = view; _snap = true; }
            if (_mixamo) { LateMixamo(view, dt, yaw, camPitch); _snap = false; FitFirstPerson(view, yaw, camPitch); return; }

            // locomotion
            Vector3 v = Game.Velocity; v.y = 0f;
            var local = Quaternion.Inverse(yaw) * v;
            float speed = v.magnitude;
            _speedSmooth = Mathf.Lerp(_speedSmooth, speed, 1f - Mathf.Exp(-dt * 10f));
            float runW = Mathf.Clamp01((_speedSmooth - Plugin.RunFrom.Value) / 0.8f);
            if (_crouch > 0.5f || _prone > 0.5f) runW = 0f;
            _runW = Mathf.MoveTowards(_runW, runW, dt * 4f);
            _loco.SetInputWeight(0, 1f - _runW);
            _loco.SetInputWeight(1, _runW);
            var run = (AnimationClipPlayable)_loco.GetInput(1);
            if (run.IsValid()) run.SetSpeed(Mathf.Max(0.6f, _speedSmooth / Mathf.Max(0.5f, Plugin.RunClipSpeed.Value)));

            // weapon -> upper-body clip
            string weapon = Game.DrawnWeapon;
            var kind = Props.KindOf(weapon);
            bool fire = !Game.Paused && Input.GetMouseButton(0);
            if (kind == Props.Kind.Melee || kind == Props.Kind.None || kind == Props.Kind.Throw)
            {
                if (kind == Props.Kind.Melee && !Game.Paused && Input.GetMouseButtonDown(0)) StartMelee(weapon);
                if (UpdateMelee(Plugin.MeleeClip.Value)) SetUpper(Plugin.MeleeClip.Value, 1f, _meleeSpeed);
                else SetUpper("", 0f, 0f);
            }
            else
            {
                string clip = kind == Props.Kind.Pistol ? Plugin.PistolClip.Value : Plugin.RifleClip.Value;
                SetUpper(clip, 1f, fire ? 1f : 0f);
                if (!fire && _upper.IsValid()) _upper.SetTime(0);
            }
            UpdateProp(weapon); ShowProp(view == View.ThirdPerson);

            // ---- procedural, in the (unmirrored) space of the Anim object ----
            float walkW = (1f - _runW) * Mathf.Clamp01(_speedSmooth / 0.6f) * (1f - _prone);
            if (walkW > 0.001f)
            {
                float dir = local.z < -0.2f ? -1f : 1f;
                _phase += dt * (_speedSmooth / Mathf.Max(0.3f, Plugin.WalkStride.Value)) * Mathf.PI * 2f * dir;
                if (_phase > Mathf.PI * 2f) _phase -= Mathf.PI * 2f; else if (_phase < 0f) _phase += Mathf.PI * 2f;
            }
            // strafe: legs turn toward the movement direction, the chest keeps facing the camera
            float strafe = 0f;
            if (speed > 0.3f) { strafe = Mathf.Atan2(local.x, Mathf.Abs(local.z)) * Mathf.Rad2Deg; strafe = Mathf.Clamp(strafe, -60f, 60f); }
            _strafeSmooth = Mathf.Lerp(_strafeSmooth, strafe * (1f - _prone), 1f - Mathf.Exp(-dt * 8f));
            float ysign = mirror ? -1f : 1f;   // a turn about the up axis flips in the mirrored body
            if (Mathf.Abs(_strafeSmooth) > 0.5f)
            {
                Turn("mixamorig:Hips", Vector3.up, _strafeSmooth * ysign);
                Turn("mixamorig:Spine", Vector3.up, -_strafeSmooth * 0.5f * ysign);
                Turn("mixamorig:Spine1", Vector3.up, -_strafeSmooth * 0.5f * ysign);
            }
            if (walkW > 0.001f) Walk(walkW, kind == Props.Kind.None);
            if (_crouch > 0.001f) Crouch(_crouch);

            // aim pitch: the spine follows the camera (looking down = positive)
            // (not in first person: bending the torso toward the camera put her chest in front of the lens)
            float pitch = view == View.FirstPerson ? 0f : camPitch * Plugin.AimPitchShare.Value * (1f - _prone);
            if (Mathf.Abs(pitch) > 0.5f)
            {
                Turn("mixamorig:Spine", Vector3.right, pitch * 0.3f);
                Turn("mixamorig:Spine1", Vector3.right, pitch * 0.3f);
                Turn("mixamorig:Spine2", Vector3.right, pitch * 0.3f);
                Turn("mixamorig:Neck", Vector3.right, pitch * 0.1f);
            }
            PoseProp(kind, yaw);
            FitFirstPerson(view, yaw, camPitch);
        }

        // first person: after the pose is final, slide her (horizontally) so her head is right under the camera, a little behind it -
        // looking down you see her whole body up to the collar, wherever the animation puts her head (crouch lean, walk bob)
        private void FitFirstPerson(View view, Quaternion yaw, float camPitch)
        {
            if (view != View.FirstPerson || _prone > 0.01f || Game.PlayerCamera == null) return;
            Transform head;
            if (!Bones.TryGetValue("mixamorig:Head", out head)) return;
            Vector3 want = Game.PlayerCamera.position - yaw * Vector3.forward * (Plugin.BodyBack.Value + Plugin.BodyBackDown.Value * Mathf.Clamp01(camPitch / 70f));
            // a weapon drawn: the game's first-person arms hang right in front of the camera, where her chest is - lean her back and bend the
            // chest back (more the further you look down) so the arms don't sink into it. Moving HER back = the camera forward, without
            // touching the camera (which would also push the game's arms closer and distort them).
            _fpWeaponW = Mathf.MoveTowards(_fpWeaponW, Game.DrawnWeapon != "" ? 1f : 0f, Time.deltaTime * 4f);
            want -= yaw * Vector3.forward * (Plugin.FirstPersonWeaponBack.Value * _fpWeaponW);
            Vector3 d = want - head.position; d.y = 0f;
            Root.transform.position += d;
            float lean = Plugin.FirstPersonChestLean.Value * _fpWeaponW * (0.6f + 0.4f * Mathf.Clamp01(camPitch / 60f));
            if (lean > 0.1f)
            {
                Turn("mixamorig:Spine1", Vector3.right, -lean * 0.4f);
                Turn("mixamorig:Spine2", Vector3.right, -lean * 0.6f);
            }
        }
        private float _fpWeaponW;

        // in a car: no Animator, the pose comes from the car's seated driver
        public void LateCar(Transform player)
        {
            if (!_inCar) { _inCar = true; _animator.enabled = false; _crouch = _prone = 0f; UpdateProp(""); }
            Root.transform.localScale = Vector3.one;
            Root.transform.SetPositionAndRotation(player.position, player.rotation);
            CarSeat.Pose(Bones, _bindLocal);
        }

        // first person: no head; her own arms only in a car with nothing drawn (hands on the wheel) - with a gun the game draws its arms
        public void SetMesh(bool firstPerson, bool inCar, bool gameArms = false)
        {
            var want = !firstPerson ? Model.Full : inCar && !gameArms ? Model.NoHead : Model.NoArms;
            if (_smr.sharedMesh != want) _smr.sharedMesh = want;
            var mode = firstPerson ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
            if (_smr.shadowCastingMode != mode) _smr.shadowCastingMode = mode;
        }

        // ---------------------------------------------------------------- Mixamo mode (animation bundle)
        private sealed class LocoSet
        {
            public AnimationMixerPlayable Mix;
            public readonly AnimationClipPlayable[] P = new AnimationClipPlayable[N];
            public readonly bool[] Reverse = new bool[N];   // a missing "back" clip = the forward one played backwards
            public bool StrafeIsWalk, CrouchStrafeMissing, CrouchMissing;
            public string Info;
        }
        private const int S_IDLE = 0, S_FWD = 1, S_BACK = 2, S_LEFT = 3, S_RIGHT = 4, S_RUN = 5, S_RUNL = 6, S_RUNR = 7,
                          S_CIDLE = 8, S_CWALK = 9, S_CBACK = 10, S_CLEFT = 11, S_CRIGHT = 12, N = 13;
        private static readonly string[] SlotNames = { "Idle", "Walk", "WalkBack", "StrafeLeft", "StrafeRight", "Run", "RunStrafeLeft", "RunStrafeRight",
                                                       "CrouchIdle", "CrouchWalk", "CrouchWalkBack", "CrouchStrafeLeft", "CrouchStrafeRight" };

        // the set's own clip ("Rifle" + name), else the unarmed one
        // (the "RifleFire" set falls back to the "Rifle" one, then to the unarmed one)
        private static AnimationClip G(string pre, string name)
        {
            return Anims.Get(pre + name) ?? (pre.Length > 5 && pre.StartsWith("Rifle") ? Anims.Get("Rifle" + name) : null) ?? Anims.Get(name);
        }

        // one locomotion set (unarmed: no prefix, rifle: "Rifle"); null when the set has no idle clip
        private LocoSet MakeSet(string pre, AnimationClip idleOverride = null, AnimationClip crouchIdleOverride = null)
        {
            var idle = idleOverride ?? Anims.Get(pre + "Idle");
            var walk = G(pre, "Walk");
            if (idle == null || walk == null) return null;
            var s = new LocoSet();
            var c = new AnimationClip[N];
            for (int i = 0; i < N; i++) c[i] = G(pre, SlotNames[i]);
            c[S_IDLE] = idle;
            if (crouchIdleOverride != null) c[S_CIDLE] = crouchIdleOverride;
            s.StrafeIsWalk = c[S_LEFT] == null || c[S_RIGHT] == null;
            s.CrouchMissing = c[S_CIDLE] == null || c[S_CWALK] == null;
            s.CrouchStrafeMissing = c[S_CLEFT] == null || c[S_CRIGHT] == null;
            // backwards: the set's own back clip, else ITS OWN forward clip played in reverse (RifleFireWalk reversed beats the non-firing RifleWalkBack)
            if (pre != "" && Anims.Get(pre + "WalkBack") == null && Anims.Get(pre + "Walk") != null) c[S_BACK] = null;
            if (pre != "" && Anims.Get(pre + "CrouchWalkBack") == null && Anims.Get(pre + "CrouchWalk") != null) c[S_CBACK] = null;
            if (c[S_BACK] == null) { c[S_BACK] = walk; s.Reverse[S_BACK] = true; }
            if (c[S_LEFT] == null) c[S_LEFT] = walk;
            if (c[S_RIGHT] == null) c[S_RIGHT] = walk;
            if (c[S_RUN] == null) c[S_RUN] = walk;
            if (c[S_RUNL] == null) c[S_RUNL] = s.StrafeIsWalk ? c[S_RUN] : c[S_LEFT];
            if (c[S_RUNR] == null) c[S_RUNR] = s.StrafeIsWalk ? c[S_RUN] : c[S_RIGHT];
            if (c[S_CIDLE] == null) c[S_CIDLE] = idle;
            if (c[S_CWALK] == null) c[S_CWALK] = walk;
            if (c[S_CBACK] == null) { c[S_CBACK] = c[S_CWALK]; s.Reverse[S_CBACK] = true; }
            if (c[S_CLEFT] == null) c[S_CLEFT] = c[S_CWALK];
            if (c[S_CRIGHT] == null) c[S_CRIGHT] = c[S_CWALK];
            s.Mix = AnimationMixerPlayable.Create(_graph, N);
            var n = new List<string>();
            for (int i = 0; i < N; i++)
            {
                s.P[i] = AnimationClipPlayable.Create(_graph, c[i]);
                s.P[i].SetApplyFootIK(true);
                _graph.Connect(s.P[i], 0, s.Mix, i);
                s.Mix.SetInputWeight(i, i == 0 ? 1f : 0f);
                n.Add(SlotNames[i] + "=" + c[i].name + (s.Reverse[i] ? "(reversed)" : ""));
            }
            s.Info = string.Join(", ", n.ToArray());
            return s;
        }

        private void BuildMixamo(AnimationPlayableOutput output)
        {
            _unarmed = MakeSet("");
            _rifle = MakeSet("Rifle");
            var crouchFire = Anims.First("RifleFireCrouchIdle", "CrouchRifleFire", "RifleCrouchFire");
            if (_rifle != null && (Anims.Get("RifleFireWalk") != null || Anims.Get("RifleFireCrouchWalk") != null))
                _fire = MakeSet("RifleFire", Anims.First("RifleFire", "RifleFireIdle", "RifleIdle"), crouchFire);
            _sets = AnimationMixerPlayable.Create(_graph, 3);
            _graph.Connect(_unarmed.Mix, 0, _sets, 0);
            _sets.SetInputWeight(0, 1f);
            if (_rifle != null) { _graph.Connect(_rifle.Mix, 0, _sets, 1); _sets.SetInputWeight(1, 0f); }
            if (_fire != null) { _graph.Connect(_fire.Mix, 0, _sets, 2); _sets.SetInputWeight(2, 0f); }
            _fallback = Anims.Get("Idle");
            _layers = AnimationLayerMixerPlayable.Create(_graph, 3);
            _graph.Connect(_sets, 0, _layers, 0);
            _layers.SetInputWeight(0, 1f);
            _upper = AnimationClipPlayable.Create(_graph, _fallback);
            _graph.Connect(_upper, 0, _layers, 1);
            _layers.SetInputWeight(1, 0f);
            _layers.SetLayerMaskFromAvatarMask(1, UpperMask());
            _action = AnimationClipPlayable.Create(_graph, _fallback);   // layer 2: whole-body one-shots (Kick, Jump, RifleJump)
            _graph.Connect(_action, 0, _layers, 2);
            _layers.SetInputWeight(2, 0f);
            output.SetSourcePlayable(_layers);
            _graph.Play();
            Plugin.Log.LogInfo("Animations from the bundle: unarmed " + _unarmed.Info + (_rifle != null ? "; rifle " + _rifle.Info : "; no rifle set (RifleIdle) - rifle aim on the upper body only")
                + (_fire != null ? "; firing " + _fire.Info : ""));
        }

        private static AvatarMask UpperMask()
        {
            var mask = new AvatarMask();
            foreach (AvatarMaskBodyPart part in Enum.GetValues(typeof(AvatarMaskBodyPart)))
            {
                if (part == AvatarMaskBodyPart.LastBodyPart) continue;
                bool upper = part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.Head || part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm
                          || part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers || part == AvatarMaskBodyPart.LeftHandIK || part == AvatarMaskBodyPart.RightHandIK;
                mask.SetHumanoidBodyPartActive(part, upper);
            }
            return mask;
        }

        private static float ClipSpeed(float speed, float native) { return Mathf.Clamp(speed / Mathf.Max(0.2f, native), 0.5f, 2f); }

        // the clip's own ground speed when it was exported with root motion (not "In Place"), else the configured one
        private static float Native(AnimationClipPlayable p, float fallback)
        {
            var c = p.GetAnimationClip();
            if (c == null) return fallback;
            float v = new Vector2(c.averageSpeed.x, c.averageSpeed.z).magnitude;
            return v > 0.3f ? v : fallback;
        }

        private readonly float[] _slotW = new float[N];   // the locomotion slots' blend weights (the same in every set) - used for the per-animation grips
        private void Drive(LocoSet s, float m, float r, float wF, float wB, float wL, float wR, float c, float speed)
        {
            if (s == null) return;
            float st = 1f - c;
            var w = _slotW;
            w[S_IDLE] = (1f - m) * st;
            w[S_FWD] = m * wF * (1f - r) * st;   w[S_RUN] = m * wF * r * st;
            w[S_BACK] = m * wB * st;
            w[S_LEFT] = m * wL * (1f - r) * st;  w[S_RUNL] = m * wL * r * st;
            w[S_RIGHT] = m * wR * (1f - r) * st; w[S_RUNR] = m * wR * r * st;
            w[S_CIDLE] = (1f - m) * c;
            w[S_CWALK] = m * wF * c; w[S_CBACK] = m * wB * c; w[S_CLEFT] = m * wL * c; w[S_CRIGHT] = m * wR * c;
            for (int i = 0; i < N; i++)
            {
                s.Mix.SetInputWeight(i, w[i]);
                if (i == S_IDLE || i == S_CIDLE) continue;
                float native = i == S_RUN || i == S_RUNL || i == S_RUNR ? Plugin.ClipRunSpeed.Value
                             : i >= S_CWALK ? Plugin.ClipCrouchSpeed.Value : Plugin.ClipWalkSpeed.Value;
                float sp = ClipSpeed(speed, Native(s.P[i], native));
                s.P[i].SetSpeed(s.Reverse[i] ? -sp : sp);
            }
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
            float ax = Mathf.Abs(local.x), az = Mathf.Abs(local.z), sum = ax + az;
            float wF = 1f, wB = 0f, wL = 0f, wR = 0f;
            if (sum > 0.05f) { wF = Mathf.Max(0f, local.z) / sum; wB = Mathf.Max(0f, -local.z) / sum; wL = Mathf.Max(0f, -local.x) / sum; wR = Mathf.Max(0f, local.x) / sum; }

            // WeaponAdjustment pose preview (Numpad 9/3): play that pose standing still so the grip can be tuned in it
            int pv = GunPose.Preview;
            bool pvFire = GunPose.IsFire(pv);
            if (pv >= 0 && pv != GunPose.P_RELOAD)
            {
                int sl = pv % GunPose.SLOTS;
                bool crouched = sl >= S_CIDLE, run = sl == S_RUN || sl == S_RUNL || sl == S_RUNR, moving = sl != S_IDLE && sl != S_CIDLE;
                _crouch = crouched ? 1f : 0f;
                m = moving ? 1f : 0f;
                wF = sl == S_FWD || sl == S_RUN || sl == S_CWALK ? 1f : 0f;
                wB = sl == S_BACK || sl == S_CBACK ? 1f : 0f;
                wL = sl == S_LEFT || sl == S_RUNL || sl == S_CLEFT ? 1f : 0f;
                wR = sl == S_RIGHT || sl == S_RUNR || sl == S_CRIGHT ? 1f : 0f;
                if (!moving) wF = 1f;
                _runW = run ? 1f : 0f;
                _speedSmooth = run ? Plugin.ClipRunSpeed.Value : crouched && moving ? Plugin.ClipCrouchSpeed.Value : moving ? Plugin.ClipWalkSpeed.Value : 0f;
            }
            else if (pv == GunPose.P_RELOAD) { _crouch = 0f; m = 0f; wF = 1f; wB = wL = wR = 0f; _runW = 0f; _speedSmooth = 0f; }

            string weapon = Game.DrawnWeapon;
            var kind = Props.KindOf(weapon);
            bool rifleSet = _rifle != null && kind == Props.Kind.Rifle;
            _armW = Mathf.MoveTowards(_armW, rifleSet ? 1f : 0f, dt * 5f);
            bool firing = !Game.Paused && (pvFire || Input.GetMouseButton(0)) && rifleSet && _fire != null && !_reloading && Time.time >= _throwUntil;
            _fireW = _snap ? (firing ? 1f : 0f) : Mathf.MoveTowards(_fireW, firing ? 1f : 0f, dt * 10f);
            if (_snap) { _armW = rifleSet ? 1f : 0f; _upperClip = ""; Plugin.Verbose("View switched: animation state re-synced"); }
            if (_rifle != null)
            {
                _sets.SetInputWeight(0, 1f - _armW);
                _sets.SetInputWeight(1, _armW * (1f - _fireW));
                if (_fire != null) _sets.SetInputWeight(2, _armW * _fireW);
            }
            var set = firing ? _fire : rifleSet ? _rifle : _unarmed;
            Drive(_fire, m, _runW, wF, wB, wL, wR, set.CrouchMissing ? 0f : _crouch, _speedSmooth);
            Drive(_unarmed, m, _runW, wF, wB, wL, wR, set.CrouchMissing ? 0f : _crouch, _speedSmooth);
            Drive(_rifle, m, _runW, wF, wB, wL, wR, set.CrouchMissing ? 0f : _crouch, _speedSmooth);

            // upper body: fire / reload / melee / throw / aim
            bool live = !Game.Paused;
            bool fire = live && (pvFire || Input.GetMouseButton(0));
            bool click = live && Input.GetMouseButtonDown(0);
            // reload: only while the game really reloads (the weapon's Reload FSM went past checkAmmo), not on every R press
            bool reloadingNow = (kind == Props.Kind.Rifle || kind == Props.Kind.Pistol) && Game.IsReloading(weapon);
            if (pv == GunPose.P_RELOAD) reloadingNow = true;   // preview: over and over
            if (reloadingNow && !_reloading) { _upperClip = ""; _reloadStart = Time.time; }
            if (pv == GunPose.P_RELOAD)
            {
                var rc = Clip(kind == Props.Kind.Pistol ? "PistolReload" : "RifleReload");
                if (rc != null && Time.time - _reloadStart > rc.length) { _upperClip = ""; _reloadStart = Time.time; }
            }
            _reloading = reloadingNow;
            _reloadUntil = _reloading ? Time.time + 0.05f : 0f;

            // kick (the game's Kick button: kick [Attack] FSM goes on -> fire) and jump (Player [Jump] FSM Idle -> Jump): whole-body one-shots
            string ks = Game.KickState;
            if (ks == "fire" && _kickState != "fire") StartAction("Kick");
            _kickState = ks;
            string js = Game.JumpState;
            if (js == "Jump" && _jumpState != "Jump") StartAction(kind == Props.Kind.Rifle && Anims.Get("RifleJump") != null ? "RifleJump" : "Jump");
            _jumpState = js;
            UpdateAction(dt);

            string meleeClip = Anims.Get("Melee") != null ? "Melee" : Plugin.MeleeClip.Value;
            if (kind == Props.Kind.Melee && click) StartMelee(weapon);
            if (kind == Props.Kind.Throw && click) StartThrow();
            string gs = Game.GrenadeState;   // the quick grenade (Throw Grenade key) is not a drawn weapon: watch its Attack FSM
            if (gs == "fire" && _grenadeState != "fire") StartThrow();
            _grenadeState = gs;

            if (Time.time < _throwUntil && Anims.Get("Throw") != null)
                SetUpper("Throw", 1f, 1f);
            else if (_reloading && kind != Props.Kind.None && kind != Props.Kind.Melee)
                SetUpper(kind == Props.Kind.Pistol ? "PistolReload" : "RifleReload", 1f, 1f);
            else if (kind == Props.Kind.Melee)
            {
                if (UpdateMelee(meleeClip)) SetUpper(meleeClip, 1f, _meleeSpeed); else SetUpper("", 0f, 0f);
            }
            else if (kind == Props.Kind.Throw)
            {
                if (Time.time < _throwUntil && Anims.Get("Throw") != null) SetUpper("Throw", 1f, 1f); else SetUpper("", 0f, 0f);
            }
            else if (kind == Props.Kind.Pistol)
            {
                string aim = Anims.Get("PistolAim") != null ? "PistolAim" : Plugin.PistolClip.Value;
                SetUpper(fire && Anims.Get("PistolFire") != null ? "PistolFire" : aim, 1f, 1f);
            }
            else if (kind == Props.Kind.Rifle)
            {
                string crouchFire = Anims.Get("CrouchRifleFire") != null ? "CrouchRifleFire" : Anims.Get("RifleCrouchFire") != null ? "RifleCrouchFire" : null;
                if (fire && _fire != null) SetUpper("", 0f, 0f);   // the full-body RifleFire* set does it
                else if (fire && _crouch > 0.5f && crouchFire != null) SetUpper(crouchFire, 1f, 1f);
                else if (fire && Anims.Get("RifleFire") != null) SetUpper("RifleFire", 1f, 1f);
                else if (Anims.Get("RifleAim") != null) SetUpper("RifleAim", 1f, 1f);
                else if (_rifle == null) { SetUpper(Plugin.RifleClip.Value, 1f, fire ? 1f : 0f); if (!fire) _upper.SetTime(0); }
                else SetUpper("", 0f, 0f);
            }
            else SetUpper("", 0f, 0f);
            UpdateProp(weapon); ShowProp(view == View.ThirdPerson);

            // keep her over the player: a clip whose forward motion was baked into the pose (not exported "In Place") walks
            // the hips away from the body root for a whole cycle and snaps back - pin them horizontally
            Transform hips;
            if (Bones.TryGetValue("mixamorig:Hips", out hips))
            {
                var hp = hips.localPosition;
                var xz = Vector2.ClampMagnitude(new Vector2(hp.x, hp.z), Plugin.HipsDrift.Value);
                if (xz.x != hp.x || xz.y != hp.z) hips.localPosition = new Vector3(xz.x, hp.y, xz.y);
            }

            // what the bundle doesn't have is still procedural
            if (_crouch > 0.5f && !set.CrouchMissing ? set.CrouchStrafeMissing : set.StrafeIsWalk)
            {
                float strafe = speed > 0.3f ? Mathf.Clamp(Mathf.Atan2(local.x, Mathf.Abs(local.z)) * Mathf.Rad2Deg, -60f, 60f) : 0f;
                _strafeSmooth = Mathf.Lerp(_strafeSmooth, strafe * (1f - _prone), 1f - Mathf.Exp(-dt * 8f));
                if (Mathf.Abs(_strafeSmooth) > 0.5f)
                {
                    Turn("mixamorig:Hips", Vector3.up, _strafeSmooth);
                    Turn("mixamorig:Spine", Vector3.up, -_strafeSmooth * 0.5f);
                    Turn("mixamorig:Spine1", Vector3.up, -_strafeSmooth * 0.5f);
                }
            }
            if (set.CrouchMissing && _crouch > 0.001f) Crouch(_crouch);
            float pitch = view == View.FirstPerson ? 0f : camPitch * Plugin.AimPitchShare.Value * (1f - _prone);
            if (Mathf.Abs(pitch) > 0.5f)
            {
                Turn("mixamorig:Spine", Vector3.right, pitch * 0.3f);
                Turn("mixamorig:Spine1", Vector3.right, pitch * 0.3f);
                Turn("mixamorig:Spine2", Vector3.right, pitch * 0.3f);
                Turn("mixamorig:Neck", Vector3.right, pitch * 0.1f);
            }
            PoseProp(kind, yaw);
        }

        // Rifles: the raider prop's pose was made for Flexa's left-handed raider clip; in Mixamo clips the hands hold it differently.
        // Point the barrel from her right hand toward her left (support) hand, turning about the right hand, so it lies along both hands.
        // every frame: the prop's hand pose, then (rifles, bundle clips) the barrel along both hands, then the player's own offset for this weapon
        // from the [Gun position] settings (her right/up/forward in cm, pitch/yaw/roll in degrees, about the hand holding it)
        // every frame: the prop's grip in her right hand (raider pose, optionally turned toward the left hand) + the player's grip offset for
        // this weapon in the HAND's axes - so it stays glued to the hand in every animation. WeaponAdjustment edits that offset with the numpad
        // (moves/turns along her body axes, converted to the hand's axes).
        // how much she is in each grip animation (GunPose.Poses): reload, else the locomotion slots split into firing / not firing
        private readonly float[] _poseW = new float[GunPose.Poses.Length];
        private float _reloadW;
        private void PoseWeights()
        {
            _reloadW = Mathf.MoveTowards(_reloadW, _reloading ? 1f : 0f, Time.deltaTime * 8f);
            int pv = GunPose.Preview;
            if (pv >= 0) { for (int i = 0; i < _poseW.Length; i++) _poseW[i] = i == pv ? 1f : 0f; return; }
            if (!_mixamo)
            {   // game clips: no slots - idle / walk / run, crouched or not
                float mv = Mathf.Clamp01(_speedSmooth / 0.4f), c = _crouch;
                for (int i = 0; i < N; i++) _slotW[i] = 0f;
                _slotW[S_IDLE] = (1f - mv) * (1f - c); _slotW[S_FWD] = mv * (1f - _runW) * (1f - c); _slotW[S_RUN] = mv * _runW * (1f - c);
                _slotW[S_CIDLE] = (1f - mv) * c; _slotW[S_CWALK] = mv * c;
            }
            float R = _reloadW, F = _mixamo && _fire != null ? _fireW : 0f;
            for (int i = 0; i < N; i++)
            {
                _poseW[i] = (1f - R) * (1f - F) * _slotW[i];
                _poseW[GunPose.FIRE0 + i] = (1f - R) * F * _slotW[i];
            }
            _poseW[GunPose.P_RELOAD] = R;
        }

        private int DominantPose()
        {
            int pv = GunPose.Preview;
            if (pv >= 0) return pv;
            int best = 0;
            for (int i = 1; i < _poseW.Length; i++) if (_poseW[i] > _poseW[best]) best = i;
            return best;
        }

        // every frame: the prop's grip in her right hand (raider pose, AutoGrip) + the player's grip for the pose(s) she is in, in the HAND's axes -
        // so it stays glued to the hand. WeaponAdjustment edits the grip of the current (or previewed) pose with the numpad.
        private void PoseProp(Props.Kind kind, Quaternion yaw)
        {
            if (_prop == null || _propHand == null) return;
            var t = _prop.transform;
            t.localPosition = _propPos; t.localRotation = _propRot;
            // auto grip: the first time she stands still with this rifle in the rifle idle, point it from her right hand at her left hand
            // (= where the Mixamo rifle clips expect the gun) and keep that as a FIXED grip in the hand
            if (_mixamo && kind == Props.Kind.Rifle && !_gripDone && Plugin.AlignGun.Value && _speedSmooth < 0.1f && _crouch < 0.01f
                && _armW > 0.99f && _fireW < 0.01f && !_reloading && Time.time > _throwUntil && Time.time > _propSince + 0.5f && GunPose.Preview < 0)
            {
                AlignProp();
                _propPos = t.localPosition; _propRot = t.localRotation; _gripDone = true;
                _gripCache[_propFor] = new KeyValuePair<Vector3, Quaternion>(_propPos, _propRot);
                Plugin.Verbose("Third person: " + _propFor + " grip taken from the rifle idle pose");
            }
            Vector3 basePos = t.localPosition; Quaternion baseRot = t.localRotation;
            PoseWeights();
            Vector3 op; Quaternion orr;
            GunPose.Blend(_propFor, _poseW, out op, out orr);
            t.localPosition = basePos + op;
            t.localRotation = baseRot * orr;

            int pose = DominantPose();
            Vector3 move, rot;
            if (_propShown && GunPose.Keys(_propFor, pose, out move, out rot)) Adjust(t, basePos, baseRot, yaw, move, rot, pose);

            // shooting without a Fire grip of its own: the fire clips raise the left hand to another spot - follow it with the barrel
            float follow = 0f;
            for (int i = GunPose.FIRE0; i < GunPose.P_RELOAD; i++) if (_poseW[i] > 0f && !GunPose.HasPose(_propFor, i)) follow += _poseW[i];
            if (_mixamo && kind == Props.Kind.Rifle && Plugin.AlignGun.Value && follow > 0.01f)
            {
                Vector3 lp = t.localPosition; Quaternion lr = t.localRotation;
                AlignProp();
                t.localPosition = Vector3.Lerp(lp, t.localPosition, follow);
                t.localRotation = Quaternion.Slerp(lr, t.localRotation, follow);
            }
        }

        private void Adjust(Transform t, Vector3 basePos, Quaternion baseRot, Quaternion yaw, Vector3 move, Vector3 rot, int pose)
        {
            float dt = Time.unscaledDeltaTime;
            Vector3 right = yaw * Vector3.right, fwd = yaw * Vector3.forward;
            Vector3 barrel = _propBarrel != Vector3.zero ? t.TransformDirection(_propBarrel) : fwd;
            var q = Quaternion.AngleAxis(rot.z * dt, barrel) * Quaternion.AngleAxis(rot.y * dt, Vector3.up) * Quaternion.AngleAxis(-rot.x * dt, right);
            Vector3 wp = t.position + (right * move.x + Vector3.up * move.y + fwd * move.z) * (0.01f * dt);
            Quaternion wr = q * t.rotation;
            // back into the hand's axes
            Vector3 lp = _propHand.InverseTransformPoint(wp);
            Quaternion lr = Quaternion.Inverse(_propHand.rotation) * wr;
            t.localPosition = lp; t.localRotation = lr;
            Vector3 dp = (lp - basePos) * 100f;
            Vector3 de = (Quaternion.Inverse(baseRot) * lr).eulerAngles;
            GunPose.SetLive(_propFor, pose, new[] { dp.x, dp.y, dp.z, Mathf.DeltaAngle(0f, de.x), Mathf.DeltaAngle(0f, de.y), Mathf.DeltaAngle(0f, de.z) });
        }

        private void AlignProp()
        {
            if (_prop == null || !Plugin.AlignGun.Value || _propBarrel == Vector3.zero) return;
            Transform rh, lh;
            if (!Bones.TryGetValue("mixamorig:RightHand", out rh) || !Bones.TryGetValue("mixamorig:LeftHand", out lh)) return;
            Vector3 want = lh.position - rh.position;
            if (want.magnitude < 0.12f) return;   // hands together (pistol grip / reload): keep the hand pose
            var t = _prop.transform;
            Vector3 have = t.TransformDirection(_propBarrel);
            var q = Quaternion.FromToRotation(have, want.normalized);
            Vector3 pivot = rh.position;
            t.position = pivot + q * (t.position - pivot);
            t.rotation = q * t.rotation;
        }

        // ---------------------------------------------------------------- procedural helpers
        // rotate a bone by 'deg' about an axis given in the Anim object's local space (mirror-safe: only local rotations are used)
        private void Turn(string bone, Vector3 axisAnim, float deg)
        {
            Transform t;
            if (!Bones.TryGetValue(bone, out t)) return;
            var q = AnimSpace(t);
            t.localRotation = t.localRotation * Quaternion.Inverse(q) * Quaternion.AngleAxis(deg, axisAnim) * q;
        }

        private Quaternion AnimSpace(Transform t)
        {
            var q = Quaternion.identity;
            for (var x = t; x != null && x != _anim; x = x.parent) q = x.localRotation * q;
            return q;
        }

        private void Walk(float w, bool swingArms)
        {
            float s = Mathf.Sin(_phase), c = Mathf.Cos(_phase);
            float A = Plugin.ThighSwing.Value * w, K = Plugin.KneeBend.Value * w;
            // thighs: forward swing = negative about the right axis
            Turn("mixamorig:LeftUpLeg", Vector3.right, -A * s);
            Turn("mixamorig:RightUpLeg", Vector3.right, A * s);
            // knees bend while the leg swings forward
            float kl = K * Mathf.Max(0f, c) + 6f * w, kr = K * Mathf.Max(0f, -c) + 6f * w;
            Turn("mixamorig:LeftLeg", Vector3.right, kl);
            Turn("mixamorig:RightLeg", Vector3.right, kr);
            Turn("mixamorig:LeftFoot", Vector3.right, -0.5f * kl + 0.3f * A * s);
            Turn("mixamorig:RightFoot", Vector3.right, -0.5f * kr - 0.3f * A * s);
            Transform hips;
            if (Bones.TryGetValue("mixamorig:Hips", out hips))
                hips.localPosition += Vector3.up * (-Plugin.HipBob.Value * w * (1f - Mathf.Abs(c)));
            Turn("mixamorig:Spine1", Vector3.up, 4f * w * s);
            if (swingArms)
            {
                Turn("mixamorig:LeftArm", Vector3.right, Plugin.ArmSwing.Value * w * s);
                Turn("mixamorig:RightArm", Vector3.right, -Plugin.ArmSwing.Value * w * s);
            }
        }

        private void Crouch(float k)
        {
            float drop = Plugin.CrouchDrop.Value * k;
            float L = _legThigh + _legShin;
            float a = Mathf.Acos(Mathf.Clamp(1f - drop / L, -1f, 1f)) * Mathf.Rad2Deg;
            Transform hips;
            if (Bones.TryGetValue("mixamorig:Hips", out hips)) hips.localPosition += Vector3.down * drop;
            Turn("mixamorig:LeftUpLeg", Vector3.right, -a);
            Turn("mixamorig:RightUpLeg", Vector3.right, -a);
            Turn("mixamorig:LeftLeg", Vector3.right, 2f * a);
            Turn("mixamorig:RightLeg", Vector3.right, 2f * a);
            Turn("mixamorig:LeftFoot", Vector3.right, -a);
            Turn("mixamorig:RightFoot", Vector3.right, -a);
            Turn("mixamorig:Spine", Vector3.right, 18f * k);
        }

        // ---------------------------------------------------------------- weapon in hand (third person)
        private readonly Dictionary<string, KeyValuePair<Vector3, Quaternion>> _gripCache = new Dictionary<string, KeyValuePair<Vector3, Quaternion>>();
        private bool _propShown;
        private void ShowProp(bool on)
        {
            if (_prop == null || _propShown == on) return;
            _propShown = on;
            foreach (var r in _prop.GetComponentsInChildren<Renderer>(true)) r.enabled = on;
        }

        private void UpdateProp(string weapon)
        {
            if (weapon == _propFor && (_prop != null || weapon == "")) return;
            _propFor = weapon;
            if (_prop != null) { UnityEngine.Object.Destroy(_prop); _prop = null; }
            if (weapon == "") return;
            var p = Props.Find(weapon);
            if (p == null) { Plugin.Verbose("No third-person model for " + weapon); return; }
            Transform hand;
            if (!Bones.TryGetValue(p.Hand, out hand)) return;
            bool otherHand = _mixamo && p.Hand == "mixamorig:LeftHand";   // raider guns sit in the left hand; Mixamo clips hold them right-handed
            Transform right;
            if (otherHand && Bones.TryGetValue("mixamorig:RightHand", out right)) hand = right; else otherHand = false;
            _prop = Props.Instantiate(p, hand, otherHand);
            _propBarrel = Props.Barrel(_prop);
            _propPos = _prop.transform.localPosition; _propRot = _prop.transform.localRotation; _propHand = hand;
            _gripDone = false; _propSince = Time.time;
            KeyValuePair<Vector3, Quaternion> g;
            if (_gripCache.TryGetValue(weapon, out g)) { _propPos = g.Key; _propRot = g.Value; _gripDone = true; }   // AutoGrip done before for this weapon
            _propShown = true; ShowProp(false);
            Plugin.Verbose("Third person: barrel axis of " + p.Key + " = " + _propBarrel);
            Plugin.Verbose("Third person: " + weapon + " -> " + p.Owner + "'s " + p.Source.name + " on " + p.Hand);
        }
    }
}
