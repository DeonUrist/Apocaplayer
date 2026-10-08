using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Apocaplayer
{
    // A lease on the player's animation only. The caller owns collision detection,
    // physical movement and temporarily suspending the game's movement FSMs.
    public sealed class PlayerTraversal : IDisposable
    {
        internal readonly Body Body;
        internal float Seconds;
        internal Quaternion Facing;
        internal PlayerTraversal(Body body, Quaternion facing) { Body = body; Facing = facing; }
        public bool Active { get { return Body != null && Body.Alive && Runner.PlayerBody == Body && Body.Traversal == this; } }
        public bool StowWeapon { get; set; }
        public bool FollowHeadInFirstPerson { get; set; }
        public Transform Camera { get { return Active ? Game.PlayerCamera : null; } }
        internal bool TryEye(out Vector3 eye)
        {
            eye = Vector3.zero;
            Transform head;
            if (!Active || !FollowHeadInFirstPerson || !Body.Bones.TryGetValue("mixamorig:Head", out head) || head == null) return false;
            // Follow position without the animation's roll/pitch: mouse look stays
            // under the player's control. The small eye offset keeps the lens forward
            // of the skull pivot when the character leans over the ledge.
            Vector3 target = head.position + Facing * new Vector3(0, .075f, .06f);
            eye = Game.PlayerCamera != null ? Vector3.Lerp(Game.PlayerCamera.position, target, Mathf.Clamp01(Seconds / .08f)) : target;
            return true;
        }
        public bool Sample(float seconds, Quaternion facing)
        {
            if (!Active) return false;
            Seconds = Mathf.Max(0, seconds); Facing = facing;
            return true;
        }
        public void Dispose() { if (Body != null && Body.Traversal == this) Body.EndTraversal(); }
    }

    public static partial class ModAPI
    {
        public static GameObject PlayerRoot { get { return Game.Ready ? Game.Player : null; } }
        public static bool PlayerCanTraverse
        {
            get { return Plugin.Enabled.Value && Game.Ready && !Game.Dead && !Game.Paused && !Game.InCar
                && Game.StandingHeight >= 1.4f && Runner.PlayerBody != null && Runner.PlayerBody.Alive && !Runner.PlayerBody.IsRagdoll; }
        }
        public static bool PlayerTraversalActive { get { return Runner.PlayerBody != null && Runner.PlayerBody.Traversal != null; } }
        internal static bool PlayerWeaponStowed { get { return PlayerTraversalActive && Runner.PlayerBody.Traversal.StowWeapon; } }
        internal static bool PlayerTraversalCameraActive { get { return PlayerTraversalActive && Runner.PlayerBody.Traversal.FollowHeadInFirstPerson && !Game.InCar; } }
        internal static bool TryPlayerTraversalEye(out Vector3 eye)
        {
            eye = Vector3.zero;
            return PlayerTraversalCameraActive && Runner.PlayerBody.Traversal.TryEye(out eye);
        }
        public static PlayerTraversal BeginPlayerTraversal(AnimationClip clip, Quaternion facing)
        {
            if (!PlayerCanTraverse || PlayerTraversalActive || clip == null || !clip.humanMotion) return null;
            return Runner.PlayerBody.BeginTraversal(clip, facing);
        }
    }

    internal sealed partial class Body
    {
        internal PlayerTraversal Traversal;
        private AnimationMixerPlayable _traversalMix;
        private AnimationClipPlayable _traversalClip;
        private AnimationPlayableOutput _traversalOutput;
        private float _traversalLength;

        internal PlayerTraversal BeginTraversal(AnimationClip clip, Quaternion facing)
        {
            if (!_graph.IsValid() || _animator == null || !_animator.isHuman) return null;
            _traversalOutput = (AnimationPlayableOutput)_graph.GetOutput(0);
            _traversalMix = AnimationMixerPlayable.Create(_graph, 2);
            _traversalClip = AnimationClipPlayable.Create(_graph, clip);
            _traversalClip.SetApplyFootIK(false);
            _traversalClip.SetApplyPlayableIK(false);
            _traversalClip.SetSpeed(0);
            _graph.Connect(_layers, 0, _traversalMix, 0);
            _graph.Connect(_traversalClip, 0, _traversalMix, 1);
            _traversalMix.SetInputWeight(0, 1);
            _traversalMix.SetInputWeight(1, 0);
            _traversalOutput.SetSourcePlayable(_traversalMix);
            _traversalLength = clip.length;
            _sway = Vector3.zero;
            Traversal = new PlayerTraversal(this, facing);
            return Traversal;
        }

        private bool LateTraversal(View view)
        {
            if (Traversal == null) return false;
            if (Game.Paused) return true;
            if (!ModAPI.PlayerCanTraverse) { EndTraversal(); return false; }
            var player = Game.Player.transform;
            float seconds = Mathf.Clamp(Traversal.Seconds, 0, _traversalLength);
            float weight = Mathf.Min(Mathf.Clamp01(seconds / .1f), Mathf.Clamp01((_traversalLength - seconds) / .12f));
            _traversalMix.SetInputWeight(0, 1 - weight);
            _traversalMix.SetInputWeight(1, weight);
            _traversalClip.SetTime(seconds);
            _animator.enabled = true;
            Root.transform.SetPositionAndRotation(player.position - Vector3.up * (Game.StandingHeight * .5f), Traversal.Facing);
            Root.transform.localScale = Vector3.one;
            _anim.localPosition = Vector3.zero;
            _anim.localRotation = Quaternion.identity;
            _graph.Evaluate(0);
            if (_rootMotion != null) _rootMotion.Take();
            ShowProp(false);
            _lastView = view;
            CapturePose(null);
            return true;
        }

        internal void EndTraversal()
        {
            Traversal = null;
            if (_graph.IsValid() && _traversalMix.IsValid())
            {
                _traversalOutput.SetSourcePlayable(_layers);
                _graph.Disconnect(_traversalMix, 0);
                _graph.Disconnect(_traversalMix, 1);
                _traversalMix.Destroy();
                if (_traversalClip.IsValid()) _traversalClip.Destroy();
                _snap = true;
                StartPoseFade(.08f, false);
            }
        }
    }
}
