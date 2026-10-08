using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Apocaplayer
{
    // Her body: a clone of Flexa's "Anim" object (Animator with the humanoid avatar enemy_1_IdleAvatar + the 22 mixamorig bones +
    // the skinned mesh), stripped of props/colliders/FSMs, with our mesh and texture. Animated by a PlayableGraph with the game's own
    // humanoid clips (idle, run, rifle / pistol aim, melee swing), plus procedural layers in LateUpdate: walk cycle, strafe twist,
    // aim pitch, crouch, prone. In a car the Animator is off and the pose is copied from the car's seated driver (CarSeat).
    internal sealed partial class Body
    {
        public GameObject Root;          // mirror lives here (scale x -1)
        private Transform _anim;         // clone of Flexa/Anim
        private Ragdoll _ragdoll;
        private Equipment _equipment;
        public void LateEquipment()
        {
            if (_equipment == null) _equipment = new Equipment(this);
            _equipment.Tick();
            _equipment.SetVisible(_smr.enabled, _shadow.enabled);
        }
        public bool IsRagdoll { get { return _ragdoll != null; } }
        internal bool TryOcclusionPoints(out Vector3 head, out Vector3 feet)
        {
            Transform h, left, right;
            if (!Bones.TryGetValue("mixamorig:Head", out h) || !Bones.TryGetValue("mixamorig:LeftFoot", out left) || !Bones.TryGetValue("mixamorig:RightFoot", out right))
            { head = feet = Vector3.zero; return false; }
            head = h.position + Vector3.up * .15f; feet = (left.position + right.position) * .5f;
            return true;
        }
        public Vector3 DeathFocus { get { return _ragdoll != null ? _ragdoll.Focus : Root.transform.position; } }

        public void LateDead(Vector3 velocity, Transform vehicle)
        {
            if (_ragdoll == null)
            {
                if (_graph.IsValid()) _graph.Stop();
                _animator.enabled = false;
                ShowProp(false);
                SetMesh(false, false);
                SetVisible(true, false);
                SetFirstPersonArms(false);
                _ragdoll = new Ragdoll(Bones, velocity, vehicle);
                Plugin.Verbose("Death: animation stopped, jointed ragdoll activated");
            }
            _ragdoll.Apply();
        }
        private RootMotionTap _rootMotion;
        private Vector3 _sway;           // standing still in third person: the clips' own sideways/forward root motion (Idle sways 16 cm), Anim-parent space

        // the Animator's root motion each evaluation (OnAnimatorMove = handled by script: nothing moves by itself)
        internal sealed class RootMotionTap : MonoBehaviour
        {
            private Animator _a;
            public Vector3 Delta;
            private void OnAnimatorMove()
            {
                if (_a == null) _a = GetComponent<Animator>();
                if (_a != null) Delta += _a.deltaPosition;
            }
            public Vector3 Take() { var d = Delta; Delta = Vector3.zero; return d; }
        }
        private Animator _animator;
        private SkinnedMeshRenderer _smr, _shadow, _fpArms, _inner;
        private bool _innerOn;
        private Material _mat;
        public readonly Dictionary<string, Transform> Bones = new Dictionary<string, Transform>();
        private readonly Dictionary<string, Quaternion> _bindLocal = new Dictionary<string, Quaternion>();

        private PlayableGraph _graph;
        private AnimationMixerPlayable _loco;
        private AnimationLayerMixerPlayable _layers;
        private AnimationClipPlayable _upper;
        // upper layer = a 2-input mixer: _upper (the current clip, input 0) and _upperOld (the clip it replaced, input 1) cross-faded.
        // The new clip always starts at once at its own time and speed - only the old pose fades out under it - so nothing gets longer.
        private AnimationMixerPlayable _upperMix;
        private AnimationClipPlayable _upperOld;
        private float _upperX = 1f, _upperW, _upperWTarget;
        private const float UpperIn = 0.08f, UpperOut = 0.15f, UpperCross = 0.12f;
        // the layer mixer's inputs: 0 the base, then the upper-body layer (strikes / throws, masked) and the whole-body actions (kick, jumps,
        // turns). (2.1.9) In Mixamo mode the upper layer is ABOVE the actions (index 2): a grenade thrown or a punch mid-jump shows on top
        private int _lUpper = 1, _lAction = 2;

        private void MakeUpper(AnimationClip c)
        {
            _upperMix = AnimationMixerPlayable.Create(_graph, 2);
            _upper = AnimationClipPlayable.Create(_graph, c);
            _graph.Connect(_upper, 0, _upperMix, 0);
            _upperMix.SetInputWeight(0, 1f); _upperMix.SetInputWeight(1, 0f);
            _graph.Connect(_upperMix, 0, _layers, _lUpper);
            _layers.SetInputWeight(_lUpper, 0f);
            _upperW = _upperWTarget = 0f; _upperX = 1f;
        }

        // every frame: the upper layer's weight eases toward what SetUpper asked (fast in, a little slower out), the cross-fade advances
        private void TickUpper(float dt)
        {
            if (!_layers.IsValid() || !_upperMix.IsValid()) return;
            if (_snap) _upperW = _upperWTarget;
            else _upperW = Mathf.MoveTowards(_upperW, _upperWTarget, dt / (_upperWTarget > _upperW ? UpperIn : UpperOut));
            _layers.SetInputWeight(_lUpper, _upperW);
            if (_upperOld.IsValid())
            {
                _upperX = _snap ? 1f : Mathf.MoveTowards(_upperX, 1f, dt / UpperCross);
                _upperMix.SetInputWeight(0, _upperX); _upperMix.SetInputWeight(1, 1f - _upperX);
                if (_upperX >= 1f) { _graph.Disconnect(_upperMix, 1); _upperOld.Destroy(); }
            }
            else { _upperMix.SetInputWeight(0, 1f); _upperMix.SetInputWeight(1, 0f); }
        }
        private string _upperClip = "";
        private AnimationClip _fallback;
        private bool _mixamo;                       // locomotion from the animation bundle (Mixamo clips) instead of the game's clips + procedural walk
        private float _reloadUntil, _throwUntil, _reloadStart;
        private bool _reloading;
        private string _kickState = "", _jumpState = "";
        private AnimationClipPlayable _action;
        private string _actionClip = "";
        private float _actionUntil, _actionW;

        private float _jumpAt = -10f;
        private void StartAction(string name, float startFraction = 0f)
        {
            var c = Anims.Get(name);
            if (c == null || !_action.IsValid()) return;
            if (_actionClip != name)
            {
                _actionClip = name;
                _graph.Disconnect(_layers, _lAction);
                _action.Destroy();
                _action = AnimationClipPlayable.Create(_graph, c);
                _action.SetApplyFootIK(false);   // jumps / turns / kick: no foot IK (2.0.2)
                _graph.Connect(_action, 0, _layers, _lAction);
            }
            float t0 = Mathf.Clamp01(startFraction) * c.length;   // jumps: skip the clip's crouch before take-off, the game pushes her up at once
            _action.SetTime(t0); _action.SetSpeed(1);
            _actionUntil = Time.time + c.length - t0;
        }

        private void UpdateAction(float dt)
        {
            if (!_action.IsValid()) return;
            bool on = Time.time < _actionUntil - 0.15f;
            _actionW = Mathf.MoveTowards(_actionW, on ? 1f : 0f, dt * (on ? 10f : _actionFade));
            if (!on && _actionW <= 0f) _actionFade = 6f;
            _layers.SetInputWeight(_lAction, _actionW);
        }
        private string _grenadeState = "";

        // a throw: the quick grenade with the LEFT hand ("Throw", mirrored in the bundle - the right one holds the gun), a drawn throwing weapon
        // (blast lance) with the RIGHT hand ("ThrowRight" = the same clip unmirrored, else "Throw"). The blast lance's [Attack] FSM goes
        // on -> fire (0.4 s) -> throw (the lance leaves the hand), so its clip starts ThrowLead s in: her release (0.85 s into the clip) on the game's
        private string _throwClip = "Throw";
        private float _throwFrom;
        private bool _throwFresh;
        private const float ThrowRelease = 0.85f, ThrowLead = 0.4f;
        private float _throwAt = -10f;
        private bool _throwAim;          // the quick grenade: aimed at where the game's grenade flies (ThrowAim)
        private void StartThrow(bool rightHand = false, bool timed = false)
        {
            if (Time.time - _throwAt < 1.2f) return;   // one throw at a time (the lance's FSM can pass "on" again around its throw)
            _throwAt = Time.time;
            _throwAim = !rightHand; _aimMeasured = false;
            string n = rightHand && Anims.Get("ThrowRight") != null ? "ThrowRight" : "Throw";
            var c = Anims.Get(n);
            if (c == null) return;
            _throwClip = n;
            _throwFrom = timed ? Mathf.Clamp(ThrowRelease - ThrowLead, 0f, c.length * 0.5f) : 0f;
            _throwUntil = Time.time + Mathf.Clamp(c.length - _throwFrom, 0.5f, 2f);
            _upperClip = ""; _throwFresh = true;   // restart the clip
            Plugin.Verbose("Throw: " + n + " from " + _throwFrom.ToString("0.00") + " s");
        }
        // the throw runs on its own clock (started at _throwAt from _throwFrom): a weapon change during it (the lance leaving her hand, the
        // grenade holstering the drawn weapon) resets the upper clip, which must not start the throw over
        private void PlayThrow()
        {
            SetUpper(_throwClip, 1f, 1f);
            if (!_upper.IsValid()) return;
            double want = _throwFrom + (Time.time - _throwAt);
            if (_throwFresh || System.Math.Abs(_upper.GetTime() - want) > 0.05) _upper.SetTime(want);
            _throwFresh = false;
        }
        private string _throwState = "", _throwFor = "";

        // drawn throwing weapon: its [Attack] FSM leaving "on" = the throw has begun (button pressed)
        private void WatchThrow(string weapon, bool click)
        {
            string st; float tv;
            if (!Game.Attack(weapon, out st, out tv)) { if (click) StartThrow(true); _throwFor = ""; return; }
            if (weapon != _throwFor) { _throwFor = weapon; _throwState = st; return; }
            if (_throwState == "on" && st != "on" && st != "" && st != "InMenu") { Plugin.Verbose("Throw: " + weapon + " [Attack] on -> " + st); StartThrow(true, true); }
            _throwState = st;
        }
        private readonly Dictionary<string, AnimationClip> _clips = new Dictionary<string, AnimationClip>();

        private float _phase, _speedSmooth, _strafeSmooth, _crouch, _prone, _runW;
        private float _legThigh, _legShin;
        private bool _inCar, _snap;
        private View _lastView = View.FirstPerson;
        private float _propRetry;
        private GameObject _prop; private string _propFor = ""; private Vector3 _propBarrel, _propPos; private Quaternion _propRot; private Transform _propHand; private float _propSince;

        public enum View { FirstPerson, ThirdPerson }

        // ---------------------------------------------------------------- build
        public static Body Create()
        {
            var flexa = FlexaPrefab();
            if (flexa == null) return null;
            var animSrc = flexa.transform.Find("Anim");
            if (animSrc == null) { Plugin.Log.LogError("Flexa prefab has no Anim child"); return null; }

            var b = new Body();
            b.Root = new GameObject("ApocaplayerBody");
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
            b._mat = new Material(b._smr.sharedMaterial) { name = "Apocaplayer body" };
            var tex = Model.Body();
            if (tex != null) b._mat.mainTexture = tex;
            b._smr.sharedMesh = Model.NoArms;
            b._smr.sharedMaterial = b._mat;
            b._smr.updateWhenOffscreen = true;
            b._smr.localBounds = new Bounds(Vector3.zero, Vector3.one * 2.5f);
            b._smr.gameObject.SetActive(true);

            var sh = new GameObject("ApocaplayerShadow");
            sh.transform.SetParent(b._smr.transform.parent, false);
            b._shadow = sh.AddComponent<SkinnedMeshRenderer>();
            b._shadow.bones = b._smr.bones;
            b._shadow.rootBone = b._smr.rootBone;
            b._shadow.sharedMesh = Model.Full;
            b._shadow.sharedMaterial = b._mat;
            b._shadow.updateWhenOffscreen = true;
            b._shadow.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;

            // first person with nothing in hand: her own arms (same bones, same animation), no shadow (the shadow body has them)
            var fa = new GameObject("ApocaplayerFirstPersonArms");
            fa.transform.SetParent(b._smr.transform.parent, false);
            b._fpArms = fa.AddComponent<SkinnedMeshRenderer>();
            b._fpArms.bones = b._smr.bones;
            b._fpArms.rootBone = b._smr.rootBone;
            b._fpArms.sharedMesh = Model.ArmsOnly;
            b._fpArms.sharedMaterial = b._mat;
            b._fpArms.updateWhenOffscreen = true;
            b._fpArms.localBounds = new Bounds(Vector3.zero, Vector3.one * 2.5f);
            b._fpArms.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            b._fpArms.enabled = false;

            // first person: the camera sits inside her (neck opening, near-plane cuts through the chest when looking down). The inside of
            // the body mesh is drawn too (same mesh, reversed faces, darker), so a cut shows her inside instead of the ground through her.
            var inr = new GameObject("ApocaplayerInside");
            inr.transform.SetParent(b._smr.transform.parent, false);
            b._inner = inr.AddComponent<SkinnedMeshRenderer>();
            b._inner.bones = b._smr.bones;
            b._inner.rootBone = b._smr.rootBone;
            var im = new Material(b._mat) { name = "Apocaplayer inside" };
            if (im.HasProperty("_Color")) im.color = im.color * 0.45f;
            if (im.HasProperty("_Glossiness")) im.SetFloat("_Glossiness", 0f);
            b._inner.sharedMaterials = new[] { im, CapMaterial(b._mat) };
            b._inner.updateWhenOffscreen = true;
            b._inner.localBounds = new Bounds(Vector3.zero, Vector3.one * 2.5f);
            b._inner.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            b._inner.receiveShadows = false;
            b._inner.enabled = false;

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
            b._rootMotion = clone.AddComponent<RootMotionTap>();
            b.Root.SetActive(true);
            b.BuildGraph(flexa);
            if (b._mixamo) b.BuildUpperRig(clone);
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
            _graph = PlayableGraph.Create("Apocaplayer");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var output = AnimationPlayableOutput.Create(_graph, "body", _animator);
            _mixamo = Anims.Loaded && Anims.Get("RifleIdle") != null && (Anims.Get("RifleWalk") != null || Anims.Get("RifleWalkLow") != null);   // 2.0: the rifle pack's legs carry every weapon
            if (Anims.Loaded && !_mixamo) Plugin.Log.LogWarning("Animation bundle without RifleIdle + RifleWalk / RifleWalkLow: the bundle's locomotion is OFF (the game's clips + procedural legs)");
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
            MakeUpper(idle);
            var mask = new AvatarMask();
            foreach (AvatarMaskBodyPart part in Enum.GetValues(typeof(AvatarMaskBodyPart)))
            {
                if (part == AvatarMaskBodyPart.LastBodyPart) continue;
                bool upper = part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.Head || part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm
                          || part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers || part == AvatarMaskBodyPart.LeftHandIK || part == AvatarMaskBodyPart.RightHandIK;
                mask.SetHumanoidBodyPartActive(part, upper);
            }
            _layers.SetLayerMaskFromAvatarMask((uint)_lUpper, mask);
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
        private string _meleeFor = "";
        private readonly Dictionary<string, float> _meleeLens = new Dictionary<string, float>();   // measured swing length per weapon (a held button repeats the
                                                                                                 // swing state, so the next swings can't be measured)
        private void StartMelee(string weapon)
        {
            float known;
            _meleeStart = Time.time; _upperClip = ""; _meleeLen = _meleeLens.TryGetValue(weapon ?? "", out known) ? known : 0.5f; _meleeMeasuring = false; _meleeFp = null; _meleeFor = weapon ?? "";
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
                    _meleeLens[_meleeFor] = _meleeLen;
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

        // ---- strikes (Mixamo mode): a segment of a clip - From (wind-up) .. Hit (the blow lands) .. To (back on guard), clip seconds, read off the
        // clips' arm-muscle speed (peak = the blow). The game's hit comes the moment the swing starts (its [Attack] FSM SphereCasts in "fire"),
        // so the wind-up is played in StrikeWindup s and the rest stretched over the swing's cycle (0.35 s hands, 0.4 s machete ...).
        private sealed class Strike
        {
            public string Clip, Hand; public float From, Hit, To;
            public Strike(string c, float a, float h, float e, string hand = null) { Clip = c; From = a; Hit = h; To = e; Hand = hand; }
            public string Key { get { return Clip + "@" + Hit.ToString("0.00"); } }   // per blow (one clip can hold several)
        }
        // Punch.fbx (1.7.3): one clip with eight blows, left/right - read off its forearm-stretch curves (Punch.anim): peaks at
        // L .47, L .80, R 1.17, R 1.47, L 1.87, R 2.07, L 2.33, R 2.53 s. A fresh punch starts at the first; chained ones go on in order.
        private const string LH = "mixamorig:LeftHand", RH = "mixamorig:RightHand";
        private static readonly Strike[] PunchSeq = {
            new Strike("Punch", 0.30f, 0.47f, 0.65f, LH), new Strike("Punch", 0.65f, 0.80f, 1.00f, LH),
            new Strike("Punch", 0.97f, 1.17f, 1.32f, RH), new Strike("Punch", 1.30f, 1.47f, 1.65f, RH),
            new Strike("Punch", 1.70f, 1.87f, 2.05f, LH), new Strike("Punch", 1.93f, 2.07f, 2.22f, RH),
            new Strike("Punch", 2.17f, 2.33f, 2.47f, LH), new Strike("Punch", 2.40f, 2.53f, 2.85f, RH) };
        private static readonly Strike[] Punches = { new Strike("Punch1", 0.75f, 1.0f, 1.5f), new Strike("Punch2", 0.3f, 0.5f, 0.95f) };   // right cross, left jab
        private static readonly Strike[] Combo = { new Strike("MeleeCombo", 1.5f, 1.73f, 2.3f), new Strike("MeleeCombo", 0.8f, 1.07f, 1.5f) };   // 2nd blow, 1st blow
        private Strike _strike;
        private bool _striking;
        private float _strikeAt, _strikeCycle = 0.4f, _strikeEnd = -10f, _strikeW, _atkTime;
        private int _punchN, _chainN, _seqN;
        private string _atkFor = "", _atkState = "";

        // the clip of a strike: "Melee1"/"Melee2" are taken as Punch1/Punch2 too; a missing one = a generic share of whatever clip there is
        private static Strike Resolve(Strike s, string alias)
        {
            if (Anims.Get(s.Clip) != null) return s;
            if (alias != null && Anims.Get(alias) != null) return new Strike(alias, s.From, s.Hit, s.To);
            return null;
        }

        private void WatchAttack(Props.Kind kind, string weapon, bool click)
        {
            string st; float tv;
            bool has = Game.Attack(weapon, out st, out tv);
            if (weapon != _atkFor) { _atkFor = weapon; _atkState = st; _atkTime = tv; _chainN = 0; return; }
            bool strike;
            if (has) strike = st != "" && st != "on" && (_atkState == "on" || tv > _atkTime + 0.05f);   // left "on", or the timer restarted (held: on -> fire in one frame)
            else { strike = click; tv = 0.4f; }
            _atkState = st; _atkTime = tv;
            if (strike) BeginStrike(kind, weapon, Mathf.Clamp(tv + Time.deltaTime, 0.2f, 1.5f));
        }

        private void BeginStrike(Props.Kind kind, string weapon, float cycle)
        {
            bool chained = _striking || _meleeStart > 0f || Time.time - _strikeEnd < 0.25f;   // straight after the last one: held button / fast clicks
            Strike s = null;
            if (kind == Props.Kind.None)
            {
                // bare hands: the Punch clip's blows in order (a fresh punch = the first), else Punch1 / Punch2 by turns
                if (Anims.Get("Punch") != null) { _seqN = chained ? (_seqN + 1) % PunchSeq.Length : 0; s = PunchSeq[_seqN]; }
                else for (int k = 0; k < 2 && s == null; k++) { int i = (_punchN + k) % 2; s = Resolve(Punches[i], i == 0 ? "Melee1" : "Melee2"); if (s != null) _punchN = i + 1; }
            }
            else
            {
                // melee weapon: the first swing = the Melee clip timed to the first-person swing (as before); the swings chained after it
                // = MeleeCombo's two blows by turns
                _chainN = chained ? _chainN + 1 : 0;
                if (_chainN > 0) s = Resolve(Combo[(_chainN - 1) % 2], null);
                if (s == null) { _striking = false; _strikeW = 0f; _strike = null; StartMelee(weapon); return; }
                _meleeStart = 0f;
            }
            if (s == null) return;
            _strikeHand = kind == Props.Kind.None ? (s.Hand ?? (s.Clip == "Punch1" || s.Clip == "Melee1" ? RH : LH)) : null;
            _aimMeasured = false;
            _strike = s; _striking = true; _strikeAt = Time.time; _strikeCycle = cycle; _strikeW = 1f;
            Plugin.Verbose("Strike: " + s.Clip + " " + s.From.ToString("0.00") + "-" + s.Hit.ToString("0.00") + "-" + s.To.ToString("0.00") + " in " + cycle.ToString("0.00") + " s" + (chained ? " (chained)" : ""));
        }

        // punches land on the middle line in front of her chest: the spine turns (Spine, Spine1, Spine2 a third each) by an angle learned per clip -
        // measured where the fist is when the blow lands (its sideways angle from the chest), corrected on every punch, ramped in over the
        // wind-up and out over the second half of the way back
        private readonly Dictionary<string, float> _aimYaw = new Dictionary<string, float>();
        private string _strikeHand;
        private bool _aimMeasured;
        // the quick grenade leaves from the game's "spawn throw" point (right of her head) straight along the view: her throwing hand (left) is
        // turned (spine yaw, learned per clip like the punches) to be on that line when she lets go
        private Transform _throwSpawn;
        private void ThrowAim()
        {
            if (!_throwAim || Time.time >= _throwUntil || !_upper.IsValid() || _upperClip != _throwClip) return;
            float t = (float)_upper.GetTime(), rel = ThrowRelease;
            float k = t < rel ? Mathf.Clamp01((t - _throwFrom) / 0.2f) : Mathf.Clamp01(1f - (t - rel - 0.25f) / 0.4f);
            float aim;
            string key = "aim:" + _throwClip;
            _aimYaw.TryGetValue(key, out aim);
            float d = aim * k;
            if (Mathf.Abs(d) > 0.01f)
            {
                Turn("mixamorig:Spine", Vector3.up, d / 3f);
                Turn("mixamorig:Spine1", Vector3.up, d / 3f);
                Turn("mixamorig:Spine2", Vector3.up, d / 3f);
            }
            if (_aimMeasured || t < rel) return;
            _aimMeasured = true;
            Transform hand, chest;
            if (!Bones.TryGetValue("mixamorig:LeftHand", out hand) || !Bones.TryGetValue("mixamorig:Spine2", out chest)) return;
            if (_throwSpawn == null && Game.PlayerCamera != null) _throwSpawn = Game.FindDeep(Game.PlayerCamera, "spawn throw");
            var c = _anim.InverseTransformPoint(chest.position);
            var v = _anim.InverseTransformPoint(hand.position) - c;
            float tx = _throwSpawn != null ? (_anim.InverseTransformPoint(_throwSpawn.position) - c).x : 0f;   // the grenade's line, sideways from her chest
            if (v.z < 0.1f) return;
            float off = (Mathf.Atan2(v.x, v.z) - Mathf.Atan2(tx, Mathf.Max(0.3f, v.z))) * Mathf.Rad2Deg;   // + = hand right of the grenade's line
            _aimYaw[key] = Mathf.Clamp(aim - off * (k > 0.99f ? 1f : 0.5f), -45f, 45f);
            Plugin.Verbose("Throw aim: hand " + off.ToString("0.0") + " deg off the grenade's line (spawn " + tx.ToString("0.00") + " m) -> spine turn " + _aimYaw[key].ToString("0.0"));
        }

        // 1.7.4: during a strike the chest faces where the game hits (the eye's yaw = the crosshair), whatever the legs / strafe / the clip's own
        // hip turn do: spine, spine1, spine2 turn (a third each, about the world up) so the shoulder line is square to the aim; for punches a
        // learned per-blow offset is added on top so the fist lands on the middle line (measured against the aim, not the body)
        private void PunchAim()
        {
            if (_strike == null || !_striking || Game.PlayerCamera == null) return;
            float el = Time.time - _strikeAt, w = Mathf.Min(Plugin.StrikeWindup.Value, _strikeCycle * 0.4f), back = (_strikeCycle - w) * 0.5f;
            float k = el < w ? Mathf.Clamp01(el / Mathf.Max(0.001f, w * 0.5f)) : el < w + back ? 1f : Mathf.Clamp01(1f - (el - w - back) / Mathf.Max(0.01f, back));
            Transform s0, s1, s2, la, ra, chest;
            if (!Bones.TryGetValue("mixamorig:Spine", out s0) || !Bones.TryGetValue("mixamorig:Spine1", out s1) || !Bones.TryGetValue("mixamorig:Spine2", out s2)
                || !Bones.TryGetValue("mixamorig:LeftArm", out la) || !Bones.TryGetValue("mixamorig:RightArm", out ra)) return;
            chest = s2;
            Vector3 up = Root != null ? Root.transform.up : Vector3.up;
            Vector3 aimF = Vector3.ProjectOnPlane(Game.PlayerCamera.forward, up);
            if (aimF.sqrMagnitude < 1e-4f) return;
            Vector3 face = Vector3.ProjectOnPlane(Vector3.Cross(ra.position - la.position, up), up);
            float square = face.sqrMagnitude > 1e-6f ? Vector3.SignedAngle(face, aimF, up) : 0f;   // turn that makes the chest face the aim
            float learned = 0f;
            if (_strikeHand != null) _aimYaw.TryGetValue(_strike.Key, out learned);
            float d = Mathf.Clamp(square + learned, -90f, 90f) * k;
            if (Mathf.Abs(d) > 0.01f)
            {
                var q = Quaternion.AngleAxis(d / 3f, up);
                s0.rotation = q * s0.rotation; s1.rotation = q * s1.rotation; s2.rotation = q * s2.rotation;
            }
            if (_strikeHand == null || _aimMeasured || el < w) return;
            _aimMeasured = true;
            Transform hand;
            if (!Bones.TryGetValue(_strikeHand, out hand)) return;
            Vector3 v = Vector3.ProjectOnPlane(hand.position - chest.position, up);
            if (Vector3.Dot(v, aimF.normalized) < 0.1f) return;
            float off = Vector3.SignedAngle(aimF, v, up);   // + = fist right of the aim line
            _aimYaw[_strike.Key] = Mathf.Clamp(learned - off * (k > 0.99f ? 1f : 0.5f), -45f, 45f);
            Plugin.Verbose("Punch aim: " + _strike.Key + " landed " + off.ToString("0.0") + " deg off the aim (chest squared " + square.ToString("0.0") + ") -> blow offset " + _aimYaw[_strike.Key].ToString("0.0"));
        }

        private void PlayStrike(float dt)
        {
            if (_meleeStart > 0f)
            {
                string mc = Anims.Get("Melee") != null ? "Melee" : Plugin.MeleeClip.Value;
                if (UpdateMelee(mc)) { SetUpper(mc, 1f, _meleeSpeed); return; }
                _strikeEnd = Time.time;
            }
            if (_strike == null) { SetUpper("", 0f, 0f); return; }
            float el = Time.time - _strikeAt, t;
            if (_striking && el >= _strikeCycle) { _striking = false; _strikeEnd = Time.time; }
            if (_striking)
            {
                float w = Mathf.Min(Plugin.StrikeWindup.Value, _strikeCycle * 0.4f);
                t = el < w ? Mathf.Lerp(_strike.From, _strike.Hit, el / Mathf.Max(0.001f, w))
                           : Mathf.Lerp(_strike.Hit, _strike.To, (el - w) / Mathf.Max(0.01f, _strikeCycle - w));
            }
            else
            {
                t = _strike.To;   // back on guard: fade out
                _strikeW = Mathf.MoveTowards(_strikeW, 0f, dt * 8f);
            }
            if (_strikeW <= 0f) { SetUpper("", 0f, 0f); return; }
            SetUpper(_strike.Clip, _strikeW, 0f);
            _upper.SetTime(t);
        }

        private void SetUpper(string clipName, float weight, float speed)
        {
            if (string.IsNullOrEmpty(clipName)) { _upperWTarget = 0f; return; }   // keep the last clip connected, fading out
            if (clipName != _upperClip)
            {
                _upperClip = clipName;
                // the clip being shown fades out under the new one (only when it is visible at all)
                if (_upperOld.IsValid()) { _graph.Disconnect(_upperMix, 1); _upperOld.Destroy(); }
                _graph.Disconnect(_upperMix, 0);
                if (_upper.IsValid() && _upperW > 0.01f && !_snap)
                {
                    _upperOld = _upper; _upperOld.SetSpeed(0);   // frozen where it was
                    _graph.Connect(_upperOld, 0, _upperMix, 1);
                    _upperX = 0f;
                }
                else { if (_upper.IsValid()) _upper.Destroy(); _upperX = 1f; }
                var c = Clip(clipName) ?? _fallback;
                _upper = AnimationClipPlayable.Create(_graph, c);
                _upper.SetApplyFootIK(false);
                _graph.Connect(_upper, 0, _upperMix, 0);
                _upper.SetTime(0);
                _upperMix.SetInputWeight(0, _upperX); _upperMix.SetInputWeight(1, 1f - _upperX);
            }
            _upperWTarget = weight;
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
            if (_ragdoll != null) { _ragdoll.Destroy(); _ragdoll = null; }
            try { if (_graph.IsValid()) _graph.Destroy(); } catch (Exception) { }
            try { if (_rigGraph.IsValid()) _rigGraph.Destroy(); } catch (Exception) { }
            if (Root != null) UnityEngine.Object.Destroy(Root);
            Root = null;
        }

        public bool Alive { get { return Root != null && _anim != null; } }

        // ---------------------------------------------------------------- per frame
        public void SetVisible(bool body, bool shadow)
        {
            if (_equipment != null) _equipment.SetVisible(body, shadow);
            if (_smr.enabled != body) _smr.enabled = body;
            if (_shadow.enabled != shadow) _shadow.enabled = shadow;
            bool inner = body && _innerOn && _inner != null && _inner.sharedMesh != null;
            if (_inner != null && _inner.enabled != inner) _inner.enabled = inner;
        }

        // the caps closing the first-person cut openings: pure black, no lighting (Unlit/Color if the game ships it, else a black
        // Standard copy without texture, specular or reflections)
        private static Material _capMat;
        private static Material CapMaterial(Material body)
        {
            if (_capMat != null) return _capMat;
            var sh = Shader.Find("Unlit/Color");
            Material m;
            if (sh != null) m = new Material(sh);
            else
            {
                m = new Material(body) { mainTexture = null };
                if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0f);
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
                if (m.HasProperty("_SpecularHighlights")) { m.SetFloat("_SpecularHighlights", 0f); m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF"); }
                if (m.HasProperty("_GlossyReflections")) { m.SetFloat("_GlossyReflections", 0f); m.EnableKeyword("_GLOSSYREFLECTIONS_OFF"); }
                if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
            }
            m.name = "Apocaplayer cut cap";
            m.color = Color.black;
            m.hideFlags = HideFlags.DontUnloadUnusedAsset;
            _capMat = m;
            return m;
        }

        // first person: [Debug] camera offsets (view right/up/forward in the given frame) - her body moves the opposite way under the camera
        public void ShiftFirstPerson(Quaternion frame, Vector3 viewOffset)
        {
            if (Root == null || viewOffset.sqrMagnitude < 1e-8f) return;
            Root.transform.position -= frame * viewOffset;
        }

        // the body's own arms in first person (nothing in hand) - drawn on top of whatever mesh the body has (NoArms / hidden)
        public void SetFirstPersonArms(bool on)
        {
            if (_fpArms != null && _fpArms.enabled != on) _fpArms.enabled = on;
        }

        // on foot: place, animate (graph is already evaluated by the Animator this frame), then the procedural layers
        // pose fades: the last shown pose (captured after every frame) blends into the new one over a short time when she gets into / out of
        // the driver's seat or turns off the seat in the car (bone local rotations; hips stay where the new pose puts them)
        private readonly Dictionary<string, Quaternion> _shown = new Dictionary<string, Quaternion>(), _fadeFrom = new Dictionary<string, Quaternion>();
        private float _fadeT, _fadeLen = 0.3f;
        private Quaternion _fadeRoot = Quaternion.identity, _shownRoot = Quaternion.identity;
        private bool _fadeRootOn;
        private void CapturePose(Transform rel)
        {
            foreach (var kv in Bones) _shown[kv.Key] = kv.Value.localRotation;
            _shownRoot = rel != null ? Quaternion.Inverse(rel.rotation) * Root.transform.rotation : Root.transform.rotation;
        }
        private void StartPoseFade(float len, bool root)
        {
            if (_shown.Count == 0) return;
            _fadeFrom.Clear(); foreach (var kv in _shown) _fadeFrom[kv.Key] = kv.Value;
            _fadeRoot = _shownRoot; _fadeRootOn = root;
            _fadeT = _fadeLen = len;
        }
        private void ApplyPoseFade(float dt, Transform rel)
        {
            if (_fadeT <= 0f) return;
            float k = 1f - _fadeT / _fadeLen; k = k * k * (3f - 2f * k);
            if (_fadeRootOn && rel != null)
            {
                var hipsT = Bones.ContainsKey("mixamorig:Hips") ? Bones["mixamorig:Hips"] : null;
                Vector3 hp = hipsT != null ? hipsT.position : Root.transform.position;
                var cur = Quaternion.Inverse(rel.rotation) * Root.transform.rotation;
                Root.transform.rotation = rel.rotation * Quaternion.Slerp(_fadeRoot, cur, k);
                if (hipsT != null) Root.transform.position += hp - hipsT.position;   // turn about her hips
            }
            foreach (var kv in Bones)
            {
                if (kv.Key == "mixamorig:Hips") continue;
                Quaternion from;
                if (_fadeFrom.TryGetValue(kv.Key, out from)) kv.Value.localRotation = Quaternion.Slerp(from, kv.Value.localRotation, k);
            }
            _fadeT -= dt;
        }

        public void LateFoot(View view, float dt)
        {
            TickUpper(dt);
            if (_inCar) StartPoseFade(0.3f, false);   // out of the driver's seat: the seated pose eases into standing
            LateFootInner(view, dt);
            if (_mixamo) { ApplyPoseFade(dt, null); CapturePose(null); }
        }

        private void LateFootInner(View view, float dt)
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
            if (_mixamo) { Sway(view, dt); LateMixamo(view, dt, yaw, camPitch); _snap = false; FitFirstPerson(view, yaw, camPitch); return; }

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
                if (kind == Props.Kind.Melee && !Game.Paused && (Input.GetMouseButtonDown(0) || Input.GetMouseButton(0) && _meleeStart <= 0f)) StartMelee(weapon);
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

        // in a car: the pose comes from the car's seated driver. With a gun drawn (bundle clips) her arms come from the gun clips instead -
        // RifleIdle / PistolIdle held, RifleFire / PistolFire while shooting, the Reload clips while reloading - played on the upper-body layer
        // of her graph; after the Animator wrote them, their local rotations (shoulders, arms, forearms, hands) are kept over the seat pose,
        // so hips, legs and spine stay seated. The gun model sits in her right hand at the weapon's Idle / Fire / Reload pose; shown in third person.
        private static readonly string[] CarArmBones = { "mixamorig:LeftShoulder", "mixamorig:LeftArm", "mixamorig:LeftForeArm", "mixamorig:LeftHand",
                                                         "mixamorig:RightShoulder", "mixamorig:RightArm", "mixamorig:RightForeArm", "mixamorig:RightHand" };
        private readonly Quaternion[] _carArms = new Quaternion[8];
        private bool _carArmsReady;
        private string _carWeapon = "";
        private bool _carTurned, _carTurnedReady;
        // seated, her arms come from standing gun clips whose stance twists the whole body - without that twist the gun points off to a side
        // (≈45 degrees left). Per clip, an extra chest yaw is learned from where the barrel points against the aim (sideways only), every frame
        private readonly Dictionary<string, float> _carYawFix = new Dictionary<string, float>();
        private readonly Dictionary<string, float> _carW = new Dictionary<string, float>();
        private void LearnCarYaw(Transform player, float aimYaw)
        {
            if (_prop == null || _propBarrel == Vector3.zero || string.IsNullOrEmpty(_upperClip)) return;
            var l = Quaternion.Inverse(player.rotation) * _prop.transform.TransformDirection(_propBarrel);
            if (new Vector2(l.x, l.z).sqrMagnitude < 0.25f) return;   // pointing up/down: no sideways reading
            float barrelYaw = Mathf.Atan2(l.x, l.z) * Mathf.Rad2Deg;
            float err = Mathf.DeltaAngle(Mathf.Clamp(aimYaw, -90f, 90f), barrelYaw);   // + = barrel right of the aim
            if (Mathf.Abs(err) > 120f) return;   // the barrel axis runs butt-first on this model: no use
            float corr; _carYawFix.TryGetValue(_upperClip, out corr);
            _carYawFix[_upperClip] = Mathf.Clamp(corr - err * 0.25f, -90f, 90f);
        }
        private readonly Dictionary<string, Quaternion> _carAll = new Dictionary<string, Quaternion>();

        // chest to the aim: yaw over Spine/Spine1/Spine2, pitch like on foot
        private void AimPitch(float pitch, float yaw)
        {
            if (Mathf.Abs(yaw) > 0.1f)
            {
                Turn("mixamorig:Spine", Vector3.up, yaw / 3f);
                Turn("mixamorig:Spine1", Vector3.up, yaw / 3f);
                Turn("mixamorig:Spine2", Vector3.up, yaw / 3f);
            }
            float p = pitch * Plugin.AimPitchShare.Value;
            if (Mathf.Abs(p) > 0.5f)
            {
                Turn("mixamorig:Spine", Vector3.right, p * 0.3f);
                Turn("mixamorig:Spine1", Vector3.right, p * 0.3f);
                Turn("mixamorig:Spine2", Vector3.right, p * 0.3f);
                Turn("mixamorig:Neck", Vector3.right, p * 0.1f);
            }
        }

        // the base layer in the car: the drawn gun's set, crouch idle (used when she has turned off the seat)
        private void CarBase(Props.Kind kind, bool crouch)
        {
            if (!_base.IsValid()) return;
            for (int i = 0; i < BN; i++) _base.SetInputWeight(i, i == (crouch && !_noCrouchClips ? B_CIDLE : B_IDLE) ? 1f : 0f);
            _layers.SetInputWeight(0, 1f);
            _rigWant = 0f; _rigW = 0f; _rigEff = 0f;
        }

        public void LateCar(Transform player, bool firstPerson)
        {
            float dt = Time.deltaTime;
            TickUpper(dt);
            if (!_inCar && _mixamo) StartPoseFade(0.3f, false);   // into the driver's seat: the standing pose eases into the seat
            LateCarInner(player, firstPerson);
            if (_mixamo) { ApplyPoseFade(dt, player); CapturePose(player); }
        }

        private void LateCarInner(Transform player, bool firstPerson)
        {
            if (!_inCar) { _inCar = true; _animator.enabled = false; _crouch = _prone = 0f; UpdateProp(""); _carArmsReady = false; _carWeapon = ""; }
            Root.transform.localScale = Vector3.one;
            Root.transform.SetPositionAndRotation(player.position, player.rotation);
            _anim.localPosition = Vector3.zero; _anim.localRotation = Quaternion.identity;
            string weapon = Game.DrawnWeapon;
            var kind = Props.KindOf(weapon);
            bool live = !Game.Paused;
            // throws from the seat: the blast lance's [Attack] FSM and the quick grenade, as on foot (same clips, timing, one play per throw)
            if (_mixamo && _layers.IsValid())
            {
                if (kind == Props.Kind.Throw) WatchThrow(weapon, live && Input.GetMouseButtonDown(0)); else _throwFor = "";
                string gs = Game.GrenadeState;
                if (gs == "fire" && _grenadeState != "fire") StartThrow(false, true);
                _grenadeState = gs;
            }
            bool throwing = _mixamo && _layers.IsValid() && Time.time < _throwUntil && Anims.Get(_throwClip) != null;
            bool gun = _mixamo && _layers.IsValid() && (kind == Props.Kind.Rifle || kind == Props.Kind.Pistol);
            bool arms = gun || throwing;   // the Animator drives her arms (gun clips or the throw)
            // the lance leaving her hand / the grenade holstering the gun changes the weapon mid-throw: that must not reset the throw's arms
            if (weapon != _carWeapon) { _carWeapon = weapon; if (!throwing) { _carArmsReady = false; _carTurnedReady = false; _upperClip = ""; } _weaponSince = Time.time; }

            // where she aims (the game shoots / throws along the first-person camera), relative to her seat: yaw + = right, pitch + = down
            float aimYaw = 0f, aimPitch = 0f;
            if (arms && Game.PlayerCamera != null)
            {
                var l = Quaternion.Inverse(player.rotation) * Game.PlayerCamera.forward;
                aimYaw = Mathf.Atan2(l.x, l.z) * Mathf.Rad2Deg;
                aimPitch = -Mathf.Asin(Mathf.Clamp(l.y, -1f, 1f)) * Mathf.Rad2Deg;
            }
            // past 90 degrees to a side she gets off the seat: turned to the aim, crouched, hips at the seat's height (back below 80)
            bool turned = arms && (_carTurned ? Mathf.Abs(aimYaw) > 80f : Mathf.Abs(aimYaw) > 95f);
            if (turned != _carTurned) { _carTurned = turned; _carTurnedReady = false; StartPoseFade(0.25f, true); }
            string fixKey = throwing ? "throw:" + _throwClip : _upperClip ?? "";

            bool animated = arms && _carArmsReady && _animator.enabled;
            if (turned && animated && _carTurnedReady)
            {
                // the whole body from the Animator (crouch idle + gun clip / throw); only the seat's hip position is used
                foreach (var kv in Bones) _carAll[kv.Key] = kv.Value.localRotation;
                Transform hips; Vector3 hipsLocal = Vector3.zero;
                bool hasHips = Bones.TryGetValue("mixamorig:Hips", out hips);
                if (hasHips) hipsLocal = hips.localPosition;
                CarSeat.Pose(Bones, _bindLocal);
                Vector3 seatHips = hasHips ? hips.position : Root.transform.position;
                foreach (var kv in Bones) { Quaternion q; if (_carAll.TryGetValue(kv.Key, out q)) kv.Value.localRotation = q; }
                if (hasHips) hips.localPosition = hipsLocal;
                Root.transform.rotation = player.rotation * Quaternion.Euler(0f, aimYaw, 0f);
                if (hasHips) Root.transform.position += seatHips - hips.position;
                float tcorr = 0f; if (throwing) _carYawFix.TryGetValue(fixKey, out tcorr);
                AimPitch(aimPitch, tcorr);
                if (throwing) LearnCarThrow(player, aimYaw, true);
            }
            else
            {
                // seated: the arms the Animator evaluated this frame (from the clip set last frame) over the seat pose, the chest turned to the aim
                if (animated)
                    for (int i = 0; i < CarArmBones.Length; i++) { Transform b; if (Bones.TryGetValue(CarArmBones[i], out b)) _carArms[i] = b.localRotation; }
                CarSeat.Pose(Bones, _bindLocal);
                if (animated)
                {
                    for (int i = 0; i < CarArmBones.Length; i++) { Transform b; if (Bones.TryGetValue(CarArmBones[i], out b)) b.localRotation = _carArms[i]; }
                    float corr; _carYawFix.TryGetValue(fixKey, out corr);
                    AimPitch(aimPitch, Mathf.Clamp(aimYaw, -90f, 90f) + corr);
                    if (throwing) LearnCarThrow(player, aimYaw, false);
                }
            }
            if (!arms)
            {
                if (_animator.enabled) _animator.enabled = false;
                _carArmsReady = false; _carTurned = false;
                // a lance held in the seat (not being thrown): in her hand, the seat's arms
                if (kind == Props.Kind.Throw) { UpdateProp(weapon); ShowProp(!firstPerson); } else UpdateProp("");
                return;
            }
            // base layer for the next frame: crouch idle when turned (the gun's set; unarmed for a throw)
            CarBase(gun && !throwing ? kind : Props.Kind.None, turned);
            if (turned) _carTurnedReady = true;
            if (!_animator.enabled) _animator.enabled = true;
            if (_action.IsValid()) { _actionW = 0f; _layers.SetInputWeight(_lAction, 0f); }
            bool reload = false, fire = false;
            if (throwing) PlayThrow();   // on its own clock: a weapon change mid-throw doesn't start it over
            else
            {
                // next frame's arms
                reload = ReloadNow(weapon);
                fire = !reload && live && Input.GetMouseButton(0);
                string pre = kind == Props.Kind.Pistol ? "Pistol" : "Rifle";
                string clip = reload ? pre + "Reload" : fire && Anims.Get(pre + "Fire") != null ? pre + "Fire" : pre + "Idle";
                if (reload && !_reloading) _upperClip = "";   // a reload starts from its beginning
                _reloading = reload;
                SetUpper(clip, 1f, 1f);
            }
            _carArmsReady = true;
            // the weapon in her right hand (the gun; the lance until it leaves the hand)
            UpdateProp(gun || kind == Props.Kind.Throw ? weapon : "");
            if (_prop != null)
            {
                var w = _carW; w.Clear();             // (2.2.6) reused, not one per frame
                string pre = kind == Props.Kind.Pistol ? "Pistol" : "Rifle";
                w[reload ? pre + "Reload" : fire && Anims.Get(pre + "Fire") != null ? pre + "Fire" : pre + "Idle"] = 1f;
                Vector3 p; Quaternion r;
                GunPose.Blend(_propFor, w, _propPos, _propRot, out p, out r);
                _prop.transform.localPosition = p; _prop.transform.localRotation = r;
                if (gun && !throwing && !turned && animated) LearnCarYaw(player, aimYaw);
                LanceAlign(kind);
            }
            ShowProp(!firstPerson);
        }

        // a throw from the seat goes where she aims: when her throwing hand passes the release point of the clip, its sideways angle from her
        // chest is compared with the aim and the chest turn for that clip is corrected (per clip, learned throw by throw like the punches)
        private void LearnCarThrow(Transform player, float aimYaw, bool turned)
        {
            if (_aimMeasured || !_upper.IsValid() || _upperClip != _throwClip || _upper.GetTime() < ThrowRelease) return;
            _aimMeasured = true;
            Transform hand, chest;
            if (!Bones.TryGetValue(_throwClip == "ThrowRight" ? "mixamorig:RightHand" : "mixamorig:LeftHand", out hand) || !Bones.TryGetValue("mixamorig:Spine2", out chest)) return;
            var v = Quaternion.Inverse(player.rotation) * (hand.position - chest.position);
            if (new Vector2(v.x, v.z).sqrMagnitude < 0.01f) return;
            float handYaw = Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
            float target = turned ? aimYaw : Mathf.Clamp(aimYaw, -90f, 90f);
            float err = Mathf.DeltaAngle(target, handYaw);   // + = hand right of the aim
            if (Mathf.Abs(err) > 120f) return;
            string key = "throw:" + _throwClip;
            float corr; _carYawFix.TryGetValue(key, out corr);
            _carYawFix[key] = Mathf.Clamp(corr - err, -90f, 90f);
            Plugin.Verbose("Car throw aim: " + _throwClip + " hand " + err.ToString("0.0") + " deg off -> chest turn " + _carYawFix[key].ToString("0.0"));
        }

        // first person: no head; her own arms only in a car with nothing drawn (hands on the wheel) - with a gun the game draws its arms

        public void SetMesh(bool firstPerson, bool inCar, bool gameArms = false)
        {
            var want = !firstPerson ? Model.Full : inCar && !gameArms && Plugin.EmptyHandArms.Value ? Model.NoHead : Model.NoArms;   // arms on the wheel only with FirstPersonArms
            if (_smr.sharedMesh != want) _smr.sharedMesh = want;
            _innerOn = firstPerson;
            if (_inner != null) { var inside = firstPerson ? Model.Inside(want) : null; if (_inner.sharedMesh != inside) _inner.sharedMesh = inside; }
            var mode = firstPerson ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
            if (_smr.shadowCastingMode != mode) _smr.shadowCastingMode = mode;
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

        // standing still in third person her whole body follows the clip's root motion (the bundle puts a clip's sideways/forward motion in the
        // root): the Idle's sway moves her hips and body while the feet stay planted. Moving / first person: back to the middle at once.
        // A slow pull to the middle keeps a clip whose loop doesn't close from drifting away.
        private void Sway(View view, float dt)
        {
            var d = _rootMotion != null ? _rootMotion.Take() : Vector3.zero;
            bool still = view == View.ThirdPerson && _speedSmooth < 0.15f && Game.Velocity.sqrMagnitude < 0.04f;
            if (still && _anim.parent != null)
            {
                var l = _anim.parent.InverseTransformVector(d);
                l.y = 0f;
                if (l.sqrMagnitude < 0.04f) _sway += l;   // a loop wrap / teleport is not a sway
                _sway *= 1f - Mathf.Clamp01(dt * 0.15f);
            }
            else _sway = Vector3.MoveTowards(_sway, Vector3.zero, dt * 0.6f);
            _sway = Vector3.ClampMagnitude(_sway, 0.25f);
            _anim.localPosition = _sway;
        }

        // Rifles: the raider prop's pose was made for Flexa's left-handed raider clip; in Mixamo clips the hands hold it differently.
        // Point the barrel from her right hand toward her left (support) hand, turning about the right hand, so it lies along both hands.
        // every frame: the prop's hand pose, then (rifles, bundle clips) the barrel along both hands, then the player's own offset for this weapon
        // from the [Gun position] settings (her right/up/forward in cm, pitch/yaw/roll in degrees, about the hand holding it)
        // every frame: the prop's grip in her right hand (raider pose, optionally turned toward the left hand) + the player's grip offset for
        // this weapon in the HAND's axes - so it stays glued to the hand in every animation. WeaponAdjustment edits that offset with the numpad
        // (moves/turns along her body axes, converted to the hand's axes).
        // how much she is in each grip animation (GunPose.Poses): reload, else the locomotion slots split into firing / not firing
        // every frame: the weapon model's pose in her right hand = the absolute poses of the animations she is in (GunPose, weapon-poses.txt),
        // blended by the animations' weights. Nothing changes them but the numpad (WeaponAdjustment). Grips from 0.5/0.6 (offsets on the raider
        // grip, for rifles on the AutoGrip one) are converted once per weapon.
        // reload, robustly: a reload is shown only
        //  - while the game's first-person arms of this weapon play their reload (Animator state "reload" / a clip named *reload*), once this
        //    weapon was seen doing that together with its Reload FSM (then that is all that counts), else
        //  - while its Reload / ReloadAnimation FSM reloads - but only if it started reloading AFTER the weapon was drawn (a state left over
        //    from before never counts) and for at most 8 s
        //  never in the first 0.3 s after drawing.
        private string _lastWeapon = "";
        private float _weaponSince, _fsmReloadSince;
        private bool _reloadFresh;                                     // the FSM was seen not reloading since the weapon was drawn
        private readonly HashSet<string> _armsShowReload = new HashSet<string>();   // weapons whose FP arms are known to play a recognisable reload
        private bool ReloadNow(string weapon)
        {
            if (Time.time - _weaponSince < 0.3f) return false;
            bool known;
            bool arms = Game.ArmsReloading(weapon, out known);
            bool fsm = Game.IsReloading(weapon);
            if (fsm && arms && _armsShowReload.Add(weapon)) Plugin.Verbose("Reload: " + weapon + " - following its first-person arms' reload animation");
            if (known && _armsShowReload.Contains(weapon)) return arms || (fsm && Game.ReloadsPerRound(weapon));   // (2.1.8) one round at a time: the arms rest between rounds
            if (!fsm) { _reloadFresh = true; _fsmReloadSince = 0f; return false; }
            if (!_reloadFresh) return false;                           // already "reloading" when drawn: stale
            if (_fsmReloadSince <= 0f) _fsmReloadSince = Time.time;
            return Time.time - _fsmReloadSince < 8f;
        }

        // the blast lance in a throw: the throwing hand turns it end over end (it pointed backwards). Its forward/backward sense (the barrel axis
        // against her facing) is learned while she just holds it; during the throw, whenever it points the other way, it is turned 180 degrees
        // about the vertical (kept until it clearly points the held way again, so it doesn't flicker when it's sideways)
        private float _lanceSense;   // + = held pointing forward along _propBarrel, - = backward, 0 = unknown
        private bool _lanceFlip;
        private void LanceAlign(Props.Kind kind)
        {
            if (kind != Props.Kind.Throw || _prop == null || _propBarrel == Vector3.zero) { _lanceSense = 0f; _lanceFlip = false; return; }
            var t = _prop.transform;
            float d = Vector3.Dot(t.TransformDirection(_propBarrel), _anim.forward);
            bool throwing = Time.time < _throwUntil && _throwClip == "ThrowRight" || Time.time < _throwUntil && _throwClip == "Throw" && !_throwAim;
            if (!throwing)
            {
                if (Mathf.Abs(d) > 0.2f) _lanceSense = Mathf.Lerp(_lanceSense, Mathf.Sign(d), 0.1f);
                _lanceFlip = false;
                return;
            }
            if (Mathf.Abs(_lanceSense) < 0.3f) _lanceSense = 1f;   // never seen held: the barrel axis is the tip
            if (Mathf.Abs(d) > 0.3f) _lanceFlip = Mathf.Sign(d) != Mathf.Sign(_lanceSense);
            if (_lanceFlip) t.rotation = Quaternion.AngleAxis(180f, _anim.up) * t.rotation;
        }

        private void PoseProp(Props.Kind kind, Quaternion yaw)
        {
            if (_prop == null || _propHand == null) return;
            var t = _prop.transform;
            if (GunPose.HasLegacy(_propFor))
            {
                // rifles were tuned on top of AutoGrip (the barrel pointed from the right hand to the left hand in the rifle idle): measure it
                // once more, the same way and in the same pose, then convert; everything else was tuned on the raider grip
                if (!(_mixamo && kind == Props.Kind.Rifle)) GunPose.ConvertLegacy(_propFor, _propPos, _propRot);
                else if (_speedSmooth < 0.1f && _crouch < 0.01f && !_aimingNow && !_shootingNow && !_reloading && Time.time > _throwUntil
                         && Time.time > _propSince + 0.5f)
                {
                    t.localPosition = _propPos; t.localRotation = _propRot;
                    AlignProp();
                    GunPose.ConvertLegacy(_propFor, t.localPosition, t.localRotation);
                    GunPose.Note("");
                }
                else GunPose.Note("Converting your old " + _propFor + " grips: stand still (not crouched, not shooting) for a moment.");
            }
            PoseWeights();
            Vector3 p; Quaternion r;
            GunPose.Blend(_propFor, _poseWn, _propPos, _propRot, out p, out r);
            t.localPosition = p; t.localRotation = r;

            string pose = DominantPose();
            var shown = GunPose.Effective(_propFor, pose, GunPose.Abs(_propPos, _propRot));
            Vector3 move, rot;
            GunPose.Status((kind == Props.Kind.Pistol ? "pistol" : kind == Props.Kind.Rifle ? "rifle" : kind.ToString().ToLowerInvariant()) + " animations" + (_reloading ? ", RELOADING" : "") + (_shootingNow ? ", firing" : _aimingNow ? ", aiming" : ""));
            if (_propShown && GunPose.Keys(_propFor, pose, shown, out move, out rot)) Adjust(t, yaw, move, rot, pose);
        }

        private void Adjust(Transform t, Quaternion yaw, Vector3 move, Vector3 rot, string pose)
        {
            float dt = Time.unscaledDeltaTime;
            Vector3 right = yaw * Vector3.right, fwd = yaw * Vector3.forward;
            Vector3 barrel = _propBarrel != Vector3.zero ? t.TransformDirection(_propBarrel) : fwd;
            var q = Quaternion.AngleAxis(rot.z * dt, barrel) * Quaternion.AngleAxis(rot.y * dt, Vector3.up) * Quaternion.AngleAxis(-rot.x * dt, right);
            Vector3 wp = t.position + (right * move.x + Vector3.up * move.y + fwd * move.z) * (0.01f * dt);
            Quaternion wr = q * t.rotation;
            // back into the hand's axes: that IS the pose saved for this animation
            Vector3 lp = _propHand.InverseTransformPoint(wp);
            Quaternion lr = Quaternion.Inverse(_propHand.rotation) * wr;
            t.localPosition = lp; t.localRotation = lr;
            GunPose.SetLive(_propFor, pose, GunPose.Abs(lp, lr));
        }

        private void AlignProp()
        {
            if (_prop == null || _propBarrel == Vector3.zero) return;
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
        private bool _propShown;
        // the weapon model: drawn in third person; in first person (the game draws its own gun) only its shadow, next to her body's shadow
        private void ShowProp(bool on)
        {
            if (_prop == null || _propShown == on) return;
            _propShown = on;
            bool shadow = !on && Plugin.BodyFirstPerson.Value;
            var mode = on ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            foreach (var r in _prop.GetComponentsInChildren<Renderer>(true)) { r.enabled = on || shadow; r.shadowCastingMode = mode; }
        }

        private void UpdateProp(string weapon)
        {
            if (weapon == _propFor && (_prop != null || weapon == "" || Time.unscaledTime < _propRetry)) return;   // (2.2.8) no model: tried again every 2 s, not every frame
            _propFor = weapon; _propRetry = Time.unscaledTime + 2f;
            if (_prop != null) { UnityEngine.Object.Destroy(_prop); _prop = null; }
            if (weapon == "" || Props.KindOf(weapon) == Props.Kind.None) return;   // bare hands ("hands"): no weapon model
            var p = Props.Find(weapon);
            if (p == null) { Plugin.Verbose("No third-person model for " + weapon); return; }
            Transform hand;
            if (!Bones.TryGetValue(p.Hand, out hand)) return;
            bool otherHand = _mixamo && p.Hand == "mixamorig:LeftHand";   // raider guns sit in the left hand; Mixamo clips hold them right-handed
            Transform right;
            if (otherHand && Bones.TryGetValue("mixamorig:RightHand", out right)) hand = right; else otherHand = false;
            if (p.Item)
            {
                // the weapon's own world model: real size, put where the stand-in NPC gun would sit, its barrel along the stand-in's barrel
                var stand = Props.Instantiate(p.Ref, hand, otherHand);
                _prop = Props.Instantiate(p, hand, false);
                var ls = hand.lossyScale; var ss = p.Source.transform.localScale;
                _prop.transform.localScale = new Vector3(ss.x / Mathf.Max(1e-4f, Mathf.Abs(ls.x)), ss.y / Mathf.Max(1e-4f, Mathf.Abs(ls.y)), ss.z / Mathf.Max(1e-4f, Mathf.Abs(ls.z)));
                _prop.transform.SetPositionAndRotation(stand.transform.position, stand.transform.rotation);
                Vector3 bi = Props.Barrel(_prop), bs = Props.Barrel(stand);
                if (bi != Vector3.zero && bs != Vector3.zero)
                    _prop.transform.rotation = Quaternion.FromToRotation(_prop.transform.TransformDirection(bi), stand.transform.TransformDirection(bs)) * _prop.transform.rotation;
                UnityEngine.Object.DestroyImmediate(stand);
            }
            else _prop = Props.Instantiate(p, hand, otherHand);
            _propBarrel = Props.Barrel(_prop);
            _propPos = _prop.transform.localPosition; _propRot = _prop.transform.localRotation; _propHand = hand;
            _propSince = Time.time; GunPose.Note("");
            _propShown = true; ShowProp(false);
            Plugin.Verbose("Third person: barrel axis of " + p.Key + " = " + _propBarrel);
            Plugin.Verbose("Third person: " + weapon + " -> " + p.Owner + "'s " + p.Source.name + " on " + p.Hand);
        }
    }
}
