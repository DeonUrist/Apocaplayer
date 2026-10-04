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
        private LocoSet _unarmed, _rifle, _fire, _pistol, _pfire;   // _fire / _pfire: RifleFire* / PistolFire* clips = walking/crouching while shooting (full body)
        private float _armPW;   // pistol set weight (_armW = rifle set)
        private float _fireW;
        private AnimationMixerPlayable _sets;
        private float _armW, _reloadUntil, _throwUntil, _reloadStart;
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
                _graph.Disconnect(_layers, 2);
                _action.Destroy();
                _action = AnimationClipPlayable.Create(_graph, c);
                _action.SetApplyFootIK(true);
                _graph.Connect(_action, 0, _layers, 2);
            }
            float t0 = Mathf.Clamp01(startFraction) * c.length;   // jumps: skip the clip's crouch before take-off, the game pushes her up at once
            _action.SetTime(t0); _action.SetSpeed(1);
            _actionUntil = Time.time + c.length - t0;
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
        private sealed class Strike { public string Clip; public float From, Hit, To; public Strike(string c, float a, float h, float e) { Clip = c; From = a; Hit = h; To = e; } }
        private static readonly Strike[] Punches = { new Strike("Punch1", 0.75f, 1.0f, 1.5f), new Strike("Punch2", 0.3f, 0.5f, 0.95f) };   // right cross, left jab
        private static readonly Strike[] Combo = { new Strike("MeleeCombo", 1.5f, 1.73f, 2.3f), new Strike("MeleeCombo", 0.8f, 1.07f, 1.5f) };   // 2nd blow, 1st blow
        private Strike _strike;
        private bool _striking;
        private float _strikeAt, _strikeCycle = 0.4f, _strikeEnd = -10f, _strikeW, _atkTime;
        private int _punchN, _chainN;
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
                // bare hands: Punch1 / Punch2 by turns
                for (int k = 0; k < 2 && s == null; k++) { int i = (_punchN + k) % 2; s = Resolve(Punches[i], i == 0 ? "Melee1" : "Melee2"); if (s != null) _punchN = i + 1; }
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
            _strikeHand = kind == Props.Kind.None ? (s.Clip == "Punch1" || s.Clip == "Melee1" ? "mixamorig:RightHand" : "mixamorig:LeftHand") : null;
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
        private void PunchAim()
        {
            if (_strike == null || _strikeHand == null || !_striking) return;
            float el = Time.time - _strikeAt, w = Mathf.Min(Plugin.StrikeWindup.Value, _strikeCycle * 0.4f), back = (_strikeCycle - w) * 0.5f;
            float k = el < w ? el / Mathf.Max(0.001f, w) : el < w + back ? 1f : Mathf.Clamp01(1f - (el - w - back) / Mathf.Max(0.01f, back));
            float aim;
            _aimYaw.TryGetValue(_strike.Clip, out aim);
            float d = aim * k;
            if (Mathf.Abs(d) > 0.01f)
            {
                Turn("mixamorig:Spine", Vector3.up, d / 3f);
                Turn("mixamorig:Spine1", Vector3.up, d / 3f);
                Turn("mixamorig:Spine2", Vector3.up, d / 3f);
            }
            if (_aimMeasured || el < w) return;
            _aimMeasured = true;
            Transform hand, chest;
            if (!Bones.TryGetValue(_strikeHand, out hand) || !Bones.TryGetValue("mixamorig:Spine2", out chest)) return;
            var v = _anim.InverseTransformPoint(hand.position) - _anim.InverseTransformPoint(chest.position);
            if (v.z < 0.1f) return;
            float off = Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;   // + = right of the middle
            _aimYaw[_strike.Clip] = Mathf.Clamp(aim - off * (k > 0.99f ? 1f : 0.5f), -45f, 45f);
            Plugin.Verbose("Punch aim: " + _strike.Clip + " landed " + off.ToString("0.0") + " deg off the middle -> spine turn " + _aimYaw[_strike.Clip].ToString("0.0"));
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
            return Anims.Get(pre + name) ?? (pre.Length > 4 && pre.EndsWith("Fire") ? Anims.Get(pre.Substring(0, pre.Length - 4) + name) : null) ?? Anims.Get(name);
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
            _pistol = MakeSet("Pistol");
            if (_pistol != null && (Anims.Get("PistolFireWalk") != null || Anims.Get("PistolFireCrouchWalk") != null))
                _pfire = MakeSet("PistolFire", Anims.First("PistolFire", "PistolFireIdle", "PistolIdle"), Anims.First("PistolFireCrouchIdle", "CrouchPistolFire", "PistolCrouchFire"));
            _sets = AnimationMixerPlayable.Create(_graph, 5);
            _graph.Connect(_unarmed.Mix, 0, _sets, 0);
            _sets.SetInputWeight(0, 1f);
            if (_rifle != null) { _graph.Connect(_rifle.Mix, 0, _sets, 1); _sets.SetInputWeight(1, 0f); }
            if (_fire != null) { _graph.Connect(_fire.Mix, 0, _sets, 2); _sets.SetInputWeight(2, 0f); }
            if (_pistol != null) { _graph.Connect(_pistol.Mix, 0, _sets, 3); _sets.SetInputWeight(3, 0f); }
            if (_pfire != null) { _graph.Connect(_pfire.Mix, 0, _sets, 4); _sets.SetInputWeight(4, 0f); }
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
                + (_fire != null ? "; rifle firing " + _fire.Info : "")
                + (_pistol != null ? "; pistol " + _pistol.Info : "; no pistol set (PistolIdle) - pistol aim on the upper body only") + (_pfire != null ? "; pistol firing " + _pfire.Info : ""));
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

            // WeaponAdjustment, Numpad 9/3: the selected animation plays standing still so its weapon pose can be tuned
            int pv = GunPose.Preview;
            bool pvFire = GunPose.IsFire(pv);
            if (pv >= 0 && pv < GunPose.P_RELOAD)
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
            else if (pv >= GunPose.P_RELOAD) { _crouch = 0f; m = 0f; wF = 1f; wB = wL = wR = 0f; _runW = 0f; _speedSmooth = 0f; }

            string weapon = Game.DrawnWeapon;
            var kind = Props.KindOf(weapon);
            bool rifleSet = _rifle != null && kind == Props.Kind.Rifle;
            bool pistolSet = _pistol != null && kind == Props.Kind.Pistol;
            // the weapon type's animation set at once - no blending between unarmed / rifle / pistol animations
            _armW = rifleSet ? 1f : 0f;
            _armPW = pistolSet ? 1f : 0f;
            // another weapon drawn: nothing of the previous one carries over (fire, reload, upper-body clip, a pose being edited)
            if (weapon != _lastWeapon)
            {
                _lastWeapon = weapon; _weaponSince = Time.time;
                _fireW = 0f; _reloading = false; _reloadW = 0f; _reloadFresh = false; _upperClip = "";
                GunPose.Flush();
                _groupsKind = (Props.Kind)(-1);   // regroup for the new weapon (the selected animation is kept when it has it)
                Plugin.Verbose("Weapon: " + (weapon == "" ? "none" : weapon + " (" + kind + ")"));
            }
            var fireSet = rifleSet ? _fire : pistolSet ? _pfire : null;
            bool firing = !Game.Paused && (pvFire || Input.GetMouseButton(0)) && fireSet != null && !_reloading && Time.time >= _throwUntil;
            _fireW = _snap ? (firing ? 1f : 0f) : Mathf.MoveTowards(_fireW, firing ? 1f : 0f, dt * 10f);
            if (_snap) { _upperClip = ""; Plugin.Verbose("View switched: animation state re-synced"); }
            if (_rifle != null || _pistol != null)
            {
                _sets.SetInputWeight(0, Mathf.Clamp01(1f - _armW - _armPW));
                if (_rifle != null) _sets.SetInputWeight(1, _armW * (_fire != null ? 1f - _fireW : 1f));
                if (_fire != null) _sets.SetInputWeight(2, _armW * _fireW);
                if (_pistol != null) _sets.SetInputWeight(3, _armPW * (_pfire != null ? 1f - _fireW : 1f));
                if (_pfire != null) _sets.SetInputWeight(4, _armPW * _fireW);
            }
            var set = firing ? fireSet : rifleSet ? _rifle : pistolSet ? _pistol : _unarmed;
            Drive(_fire, m, _runW, wF, wB, wL, wR, set.CrouchMissing ? 0f : _crouch, _speedSmooth);
            Drive(_pistol, m, _runW, wF, wB, wL, wR, set.CrouchMissing ? 0f : _crouch, _speedSmooth);
            Drive(_pfire, m, _runW, wF, wB, wL, wR, set.CrouchMissing ? 0f : _crouch, _speedSmooth);
            Drive(_unarmed, m, _runW, wF, wB, wL, wR, set.CrouchMissing ? 0f : _crouch, _speedSmooth);
            Drive(_rifle, m, _runW, wF, wB, wL, wR, set.CrouchMissing ? 0f : _crouch, _speedSmooth);

            // upper body: fire / reload / melee / throw / aim
            bool live = !Game.Paused;
            bool fire = live && (pvFire || Input.GetMouseButton(0));
            bool click = live && Input.GetMouseButtonDown(0);
            bool reloadingNow = (kind == Props.Kind.Rifle || kind == Props.Kind.Pistol) && ReloadNow(weapon);
            if (pv == GunPose.P_RELOAD) reloadingNow = true;   // selected: over and over
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
            // jump: the game's Jump state lasts no frame at all (its "grounded?" test fires Grounded -> Idle in the same frame as the push),
            // so the jump is taken from the Jump button while the FSM sits in Idle (= on the ground), or from leaving the ground fast upward
            string js = Game.JumpState;
            bool jumpPress = live && js == "Idle" && Input.GetButtonDown("Jump");
            bool launched = js == "Falling" && _jumpState == "Idle" && Game.Velocity.y > 2f && Time.time > _jumpAt + 0.5f;   // a press this script missed
            if (jumpPress || launched)
            {
                _jumpAt = Time.time;
                StartAction(JumpClip(kind), Plugin.JumpClipStart.Value);
            }
            // landed (Falling -> Idle) well after the take-off: let the jump clip go (the clip's own landing may come later than the game's)
            if (_jumpState == "Falling" && js == "Idle" && _actionClip != null && _actionClip.EndsWith("Jump") && Time.time - _jumpAt > 0.25f && Time.time < _actionUntil)
                _actionUntil = Mathf.Min(_actionUntil, Time.time + 0.3f);
            _jumpState = js;
            if (pv == GunPose.P_JUMP && Time.time > _actionUntil - 0.2f) StartAction(JumpClip(kind), Plugin.JumpClipStart.Value);   // selected: over and over
            if (pv == GunPose.P_KICK && Time.time > _actionUntil - 0.2f) StartAction("Kick");
            UpdateAction(dt);
            Groups(kind, rifleSet ? _rifle : pistolSet ? _pistol : _unarmed, fireSet);

            // melee weapons and bare hands: one strike per game swing (its [Attack] FSM), the hit frame of the clip on the game's hit
            if (kind == Props.Kind.Melee || kind == Props.Kind.None && weapon != "") WatchAttack(kind, weapon, click);
            else { _atkFor = ""; _striking = false; _strikeW = 0f; _meleeStart = 0f; }
            if (kind == Props.Kind.Throw && click) StartThrow();
            string gs = Game.GrenadeState;   // the quick grenade (Throw Grenade key) is not a drawn weapon: watch its Attack FSM
            if (gs == "fire" && _grenadeState != "fire") StartThrow();
            _grenadeState = gs;

            if (Time.time < _throwUntil && Anims.Get("Throw") != null)
                SetUpper("Throw", 1f, 1f);
            else if (_reloading && kind != Props.Kind.None && kind != Props.Kind.Melee)
                SetUpper(kind == Props.Kind.Pistol ? "PistolReload" : "RifleReload", 1f, 1f);
            else if (kind == Props.Kind.Melee || kind == Props.Kind.None && weapon != "")
                PlayStrike(dt);
            else if (kind == Props.Kind.Throw)
            {
                if (Time.time < _throwUntil && Anims.Get("Throw") != null) SetUpper("Throw", 1f, 1f); else SetUpper("", 0f, 0f);
            }
            else if (kind == Props.Kind.Pistol)
            {
                // same pattern as rifles: the full-body PistolFire* set while shooting, else crouch-fire / fire / aim on the upper body
                string crouchPFire = Anims.Get("CrouchPistolFire") != null ? "CrouchPistolFire" : Anims.Get("PistolCrouchFire") != null ? "PistolCrouchFire" : null;
                if (fire && _pfire != null) SetUpper("", 0f, 0f);
                else if (fire && _crouch > 0.5f && crouchPFire != null) SetUpper(crouchPFire, 1f, 1f);
                else if (fire && Anims.Get("PistolFire") != null) SetUpper("PistolFire", 1f, 1f);
                else if (Anims.Get("PistolAim") != null) SetUpper("PistolAim", 1f, 1f);
                else if (_pistol == null) SetUpper(Plugin.PistolClip.Value, 1f, 1f);
                else SetUpper("", 0f, 0f);
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
            PunchAim();
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
            if (!_mixamo)
            {   // game clips: no slots - idle / walk / run, crouched or not
                float mv = Mathf.Clamp01(_speedSmooth / 0.4f), c = _crouch;
                for (int i = 0; i < N; i++) _slotW[i] = 0f;
                _slotW[S_IDLE] = (1f - mv) * (1f - c); _slotW[S_FWD] = mv * (1f - _runW) * (1f - c); _slotW[S_RUN] = mv * _runW * (1f - c);
                _slotW[S_CIDLE] = (1f - mv) * c; _slotW[S_CWALK] = mv * c;
            }
            float R = _reloadW, F = _mixamo && (_fire != null || _pfire != null) ? _fireW : 0f;
            for (int i = 0; i < N; i++)
            {
                _poseW[i] = (1f - R) * (1f - F) * _slotW[i];
                _poseW[GunPose.FIRE0 + i] = (1f - R) * F * _slotW[i];
            }
            _poseW[GunPose.P_RELOAD] = R;
            // a jump (whole-body action layer) takes over as much as its layer weight
            float J = _actionClip != null && _actionClip.EndsWith("Jump") ? _actionW : 0f;
            float K = _actionClip == "Kick" ? _actionW : 0f;
            for (int i = 0; i < GunPose.P_JUMP; i++) _poseW[i] *= 1f - J - K;
            _poseW[GunPose.P_JUMP] = J;
            _poseW[GunPose.P_KICK] = K;
            int pv = GunPose.Preview;
            if (pv >= 0) for (int i = 0; i < _poseW.Length; i++) _poseW[i] = i == pv ? 1f : 0f;
        }

        private string JumpClip(Props.Kind kind)
        {
            return kind == Props.Kind.Rifle && Anims.Get("RifleJump") != null ? "RifleJump"
                 : kind == Props.Kind.Pistol && Anims.Get("PistolJump") != null ? "PistolJump" : "Jump";
        }

        // which animations the drawn weapon really has and which of them are the same clip (WalkBack = Walk reversed, a missing strafe = the walk,
        // a firing slot without its own clip = the plain one ...): GunPose shows each clip once (Numpad 9/3) and an edit applies to every entry
        // that plays it. Rifles: Rifle* clips, pistols / SMGs: Pistol* clips, anything else: the unarmed set.
        private Props.Kind _groupsKind = (Props.Kind)(-1);
        private void Groups(Props.Kind kind, LocoSet set, LocoSet fireSet)
        {
            if (kind == _groupsKind) return;
            _groupsKind = kind;
            int n = GunPose.Poses.Length;
            var clip = new AnimationClip[n];
            if (set != null) for (int i = 0; i < N; i++) clip[i] = set.P[i].GetAnimationClip();
            if (fireSet != null) for (int i = 0; i < N; i++) clip[GunPose.FIRE0 + i] = fireSet.P[i].GetAnimationClip();
            if (kind == Props.Kind.Rifle || kind == Props.Kind.Pistol) clip[GunPose.P_RELOAD] = Clip(kind == Props.Kind.Pistol ? "PistolReload" : "RifleReload");
            if (kind != Props.Kind.None) clip[GunPose.P_JUMP] = Anims.Get(JumpClip(kind));
            if (kind != Props.Kind.None) clip[GunPose.P_KICK] = Anims.Get("Kick");
            var rep = new int[n];
            var names = new string[n];
            for (int i = 0; i < n; i++)
            {
                rep[i] = -1;
                if (clip[i] == null) continue;
                rep[i] = i;
                for (int j = 0; j < i; j++) if (clip[j] == clip[i]) { rep[i] = rep[j]; break; }
                names[i] = clip[i].name;
            }
            GunPose.SetGroups(rep, names);
        }

        private int DominantPose()
        {
            int pv = GunPose.Preview;
            if (pv >= 0) return pv;
            int best = 0;
            for (int i = 1; i < _poseW.Length; i++) if (_poseW[i] > _poseW[best]) best = i;
            return best;
        }

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
            if (known && _armsShowReload.Contains(weapon)) return arms;
            if (!fsm) { _reloadFresh = true; _fsmReloadSince = 0f; return false; }
            if (!_reloadFresh) return false;                           // already "reloading" when drawn: stale
            if (_fsmReloadSince <= 0f) _fsmReloadSince = Time.time;
            return Time.time - _fsmReloadSince < 8f;
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
                else if (_speedSmooth < 0.1f && _crouch < 0.01f && _armW > 0.99f && _fireW < 0.01f && !_reloading && Time.time > _throwUntil
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
            GunPose.Blend(_propFor, _poseW, _propPos, _propRot, out p, out r);
            t.localPosition = p; t.localRotation = r;

            int pose = DominantPose();
            var shown = GunPose.Effective(_propFor, pose, GunPose.Abs(_propPos, _propRot));
            Vector3 move, rot;
            GunPose.Status((kind == Props.Kind.Pistol ? "pistol" : kind == Props.Kind.Rifle ? "rifle" : kind.ToString().ToLowerInvariant()) + " animations" + (_reloading ? ", RELOADING" : "") + (_fireW > 0.5f ? ", firing" : ""));
            if (_propShown && GunPose.Keys(_propFor, pose, shown, out move, out rot)) Adjust(t, yaw, move, rot, pose);
        }

        private void Adjust(Transform t, Quaternion yaw, Vector3 move, Vector3 rot, int pose)
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
