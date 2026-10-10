using System;
using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace Apocaplayer
{
    // (2.3.3) Smooth first-person weapon / item sway. The game's [Sway] FSMs under the PlayerCamera (WeaponsArm/Parent, ItemAnim,
    // Flashlight, GodHands_Model) run PlayMaker's SmoothLookAt toward an object "LookAt" at speed 40:
    //   lastRotation = Slerp(lastRotation, desired, speed * Time.deltaTime)
    // That step is frame-rate dependent (a share of speed*dt, clamped at 1): with uneven frame times the weapon's lag behind the view changes
    // from frame to frame, and the LookAt target can move in physics steps while the camera turns every frame - both show as jerky arms /
    // weapon when turning. Harmony prefix on SmoothLookAt.DoSmoothLookAt for those FSMs: the same look-at, but
    //  - the target is followed in the camera's space with a short exponential filter (a target that jumps at physics rate glides),
    //  - the rotation eases exponentially (1 - e^(-rate dt), rate = speed x 1.65 = the game's feel at 60 fps) - the same lag at every frame rate.
    // A handful of vector operations per active sway object and frame; anything else (other FSMs, Enabled off) runs the game's own code.
    internal static class FirstPersonSway
    {
        private sealed class State { public Quaternion Rot; public Vector3 LocalTarget; public bool Ready; public GameObject Go; public int Frame; }
        private static readonly Dictionary<SmoothLookAt, State> _state = new Dictionary<SmoothLookAt, State>();
        private static FieldInfo _lastRotation, _desiredRotation, _previousGo;
        private const float RateScale = 1.65f, TargetRate = 30f;

        public static void Patch(HarmonyLib.Harmony h)
        {
            var t = typeof(SmoothLookAt);
            var m = HarmonyLib.AccessTools.Method(t, "DoSmoothLookAt");
            _lastRotation = HarmonyLib.AccessTools.Field(t, "lastRotation");
            _desiredRotation = HarmonyLib.AccessTools.Field(t, "desiredRotation");
            _previousGo = HarmonyLib.AccessTools.Field(t, "previousGo");
            if (m == null) { Plugin.Warn("First-person sway: SmoothLookAt.DoSmoothLookAt not found - the game's own sway stays"); return; }
            h.Patch(m, prefix: new HarmonyLib.HarmonyMethod(typeof(FirstPersonSway), nameof(BeforeSmoothLookAt)));
        }

        public static bool BeforeSmoothLookAt(SmoothLookAt __instance)
        {
            try
            {
                if (!Plugin.Enabled.Value || __instance.Fsm == null || __instance.Fsm.Name != "Sway") return true;
                var cam = Game.PlayerCamera;
                var go = __instance.Fsm.GetOwnerDefaultTarget(__instance.gameObject);
                var target = __instance.targetObject != null ? __instance.targetObject.Value : null;
                if (cam == null || go == null || target == null || !go.transform.IsChildOf(cam)) return true;

                State s;
                if (!_state.TryGetValue(__instance, out s)) { s = new State(); _state[__instance] = s; }
                var tr = go.transform;
                Vector3 worldTarget = __instance.targetPosition != null && !__instance.targetPosition.IsNone
                    ? target.transform.TransformPoint(__instance.targetPosition.Value) : target.transform.position;
                Vector3 local = cam.InverseTransformPoint(worldTarget);
                float dt = Time.deltaTime;
                if (!s.Ready || s.Go != go || Time.frameCount - s.Frame > 2)   // first use, another object, or the sway was off (weapon swap): start where it is
                {
                    s.Rot = tr.rotation; s.LocalTarget = local; s.Go = go; s.Ready = true;
                }
                else s.LocalTarget = Vector3.Lerp(s.LocalTarget, local, 1f - Mathf.Exp(-TargetRate * dt));
                s.Frame = Time.frameCount;
                Vector3 lookAt = cam.TransformPoint(s.LocalTarget);
                if (__instance.keepVertical != null && __instance.keepVertical.Value) lookAt.y = tr.position.y;
                Vector3 diff = lookAt - tr.position;
                Quaternion desired = s.Rot;
                if (diff.sqrMagnitude > 1e-10f)
                {
                    Vector3 up = __instance.upVector == null || __instance.upVector.IsNone ? Vector3.up : __instance.upVector.Value;
                    desired = Quaternion.LookRotation(diff, up);
                }
                float speed = __instance.speed != null ? __instance.speed.Value : 40f;
                s.Rot = Quaternion.Slerp(s.Rot, desired, 1f - Mathf.Exp(-speed * RateScale * dt));
                tr.rotation = s.Rot;
                // keep the game's own fields in step (Enabled off continues from here)
                if (_lastRotation != null) _lastRotation.SetValue(__instance, s.Rot);
                if (_desiredRotation != null) _desiredRotation.SetValue(__instance, desired);
                if (_previousGo != null) _previousGo.SetValue(__instance, go);
                if (__instance.finishEvent != null && __instance.finishTolerance != null && Quaternion.Angle(s.Rot, desired) <= __instance.finishTolerance.Value)
                    __instance.Fsm.Event(__instance.finishEvent);
                return false;
            }
            catch (Exception e) { Plugin.Verbose("First-person sway: " + e.Message); return true; }
        }
    }
}
