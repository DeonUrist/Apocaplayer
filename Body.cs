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
        private readonly Dictionary<string, AnimationClip> _clips = new Dictionary<string, AnimationClip>();

        private float _phase, _speedSmooth, _strafeSmooth, _crouch, _prone, _runW, _meleeUntil;
        private float _legThigh, _legShin;
        private bool _inCar;
        private GameObject _prop; private string _propFor = "";

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
            if (_clips.TryGetValue(name, out c)) return c;
            foreach (var a in Resources.FindObjectsOfTypeAll<AnimationClip>())
                if (a != null && a.name == name && a.humanMotion) { c = a; break; }
            if (c == null)
                foreach (var a in Resources.FindObjectsOfTypeAll<AnimationClip>())
                    if (a != null && a.name == name) { c = a; break; }
            if (c == null) Plugin.Log.LogWarning("Animation clip " + name + " not found");
            _clips[name] = c;
            return c;
        }

        private void BuildGraph(GameObject flexa)
        {
            _graph = PlayableGraph.Create("FemalePlayer");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var output = AnimationPlayableOutput.Create(_graph, "body", _animator);
            _loco = AnimationMixerPlayable.Create(_graph, 2);
            var idle = Clip(Plugin.IdleClip.Value);
            var run = Clip(Plugin.RunClip.Value);
            var pIdle = idle != null ? AnimationClipPlayable.Create(_graph, idle) : AnimationClipPlayable.Create(_graph, null);
            var pRun = run != null ? AnimationClipPlayable.Create(_graph, run) : AnimationClipPlayable.Create(_graph, null);
            pIdle.SetApplyFootIK(false); pRun.SetApplyFootIK(false);
            _graph.Connect(pIdle, 0, _loco, 0);
            _graph.Connect(pRun, 0, _loco, 1);
            _loco.SetInputWeight(0, 1f); _loco.SetInputWeight(1, 0f);

            _layers = AnimationLayerMixerPlayable.Create(_graph, 2);
            _graph.Connect(_loco, 0, _layers, 0);
            _layers.SetInputWeight(0, 1f);
            _upper = AnimationClipPlayable.Create(_graph, null);
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

        private void SetUpper(string clipName, float weight, float speed)
        {
            if (clipName != _upperClip)
            {
                _upperClip = clipName;
                _graph.Disconnect(_layers, 1);
                if (_upper.IsValid()) _upper.Destroy();
                var c = string.IsNullOrEmpty(clipName) ? null : Clip(clipName);
                _upper = AnimationClipPlayable.Create(_graph, c);
                _upper.SetApplyFootIK(false);
                _graph.Connect(_upper, 0, _layers, 1);
                _upper.SetTime(0);
            }
            _layers.SetInputWeight(1, string.IsNullOrEmpty(clipName) ? 0f : weight);
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
            if (view == View.FirstPerson) pos -= yaw * Vector3.forward * Plugin.BodyBack.Value;
            var rot = yaw;
            if (_prone > 0f)
            {
                // lying face down, head toward the camera
                rot = yaw * Quaternion.Euler(82f * _prone, 0f, 0f);
                pos = feet - yaw * Vector3.forward * (1.45f * _prone) + Vector3.up * (0.12f * _prone);
            }
            Root.transform.SetPositionAndRotation(pos, rot);
            bool mirror = Plugin.MirrorBody.Value;
            Root.transform.localScale = new Vector3(mirror ? -1f : 1f, 1f, 1f);
            _anim.localPosition = Vector3.zero; _anim.localRotation = Quaternion.identity;

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
                if (kind == Props.Kind.Melee && !Game.Paused && Input.GetMouseButtonDown(0)) { _meleeUntil = Time.time + 0.8f; SetUpper(Plugin.MeleeClip.Value, 1f, 1f); _upper.SetTime(0); }
                if (Time.time < _meleeUntil) SetUpper(Plugin.MeleeClip.Value, 1f, 1f);
                else SetUpper("", 0f, 0f);
            }
            else
            {
                string clip = kind == Props.Kind.Pistol ? Plugin.PistolClip.Value : Plugin.RifleClip.Value;
                SetUpper(clip, 1f, fire ? 1f : 0f);
                if (!fire && _upper.IsValid()) _upper.SetTime(0);
            }
            UpdateProp(view == View.ThirdPerson ? weapon : "");

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
            float pitch = Mathf.DeltaAngle(0f, Game.PlayerCamera.eulerAngles.x) * Plugin.AimPitchShare.Value * (1f - _prone);
            if (Mathf.Abs(pitch) > 0.5f)
            {
                Turn("mixamorig:Spine", Vector3.right, pitch * 0.3f);
                Turn("mixamorig:Spine1", Vector3.right, pitch * 0.3f);
                Turn("mixamorig:Spine2", Vector3.right, pitch * 0.3f);
                Turn("mixamorig:Neck", Vector3.right, pitch * 0.1f);
            }
        }

        // in a car: no Animator, the pose comes from the car's seated driver
        public void LateCar(Transform player)
        {
            if (!_inCar) { _inCar = true; _animator.enabled = false; _crouch = _prone = 0f; UpdateProp(""); }
            Root.transform.localScale = Vector3.one;
            Root.transform.SetPositionAndRotation(player.position, player.rotation);
            CarSeat.Pose(Bones, _bindLocal);
        }

        public void SetMesh(bool firstPerson, bool inCar)
        {
            var want = !firstPerson ? Model.Full : inCar ? Model.NoHead : Model.NoArms;
            if (_smr.sharedMesh != want) _smr.sharedMesh = want;
            var mode = firstPerson ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
            if (_smr.shadowCastingMode != mode) _smr.shadowCastingMode = mode;
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
            _prop = Props.Instantiate(p, hand);
            Plugin.Verbose("Third person: " + weapon + " -> " + p.Owner + "'s " + p.Source.name + " on " + p.Hand);
        }
    }
}
