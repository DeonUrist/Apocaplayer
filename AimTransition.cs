using System;
using System.Collections.Generic;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace Apocaplayer
{
    // First-person aim down sights. Each gun's [AimDownSights] FSM moves the first-person gun between hip and sights with an iTweenMoveTo
    // (on anim / off anim) at speed 10 m/s with easeInQuad: over the ~0.1-0.2 m between the two spots that takes 1-2 frames, so it looks
    // like a jump. Harmony prefix on iTweenMoveTo.OnEnter, for the AimDownSights FSM only: the speed is set so the move takes AdsTime
    // seconds from wherever the gun is now (speed = remaining distance / AdsTime), with easeOutQuad (starts at once, settles into place).
    // The FSM itself is unchanged (its crosshair on/off and the "next" events still come from the tween's start/finish). Enabled off
    // puts the game's own speed and ease back.
    internal static class AimTransition
    {
        private struct Orig { public float Speed; public object Ease; }
        // iTween lives in Assembly-CSharp-firstpass: its EaseType is set by reflection (no extra reference)
        private static readonly System.Reflection.FieldInfo EaseField = HarmonyLib.AccessTools.Field(typeof(iTweenMoveTo), "easeType");
        private static object _easeOut;
        private static readonly Dictionary<iTweenMoveTo, Orig> _orig = new Dictionary<iTweenMoveTo, Orig>();

        public static void Patch(HarmonyLib.Harmony h)
        {
            var m = HarmonyLib.AccessTools.Method(typeof(iTweenMoveTo), "OnEnter");
            if (m == null) { Plugin.Warn("Aim transition: iTweenMoveTo.OnEnter not found - aiming down sights stays instant"); return; }
            h.Patch(m, prefix: new HarmonyLib.HarmonyMethod(typeof(AimTransition), nameof(BeforeMoveTo)));
        }

        public static void BeforeMoveTo(iTweenMoveTo __instance)
        {
            try
            {
                if (__instance == null || __instance.Fsm == null || __instance.Fsm.Name != "AimDownSights" || __instance.speed == null) return;
                Orig o;
                if (!_orig.TryGetValue(__instance, out o))
                {
                    o = new Orig { Speed = __instance.speed.Value, Ease = EaseField != null ? EaseField.GetValue(__instance) : null };
                    _orig[__instance] = o;
                }
                float t = Plugin.AdsTime.Value;
                if (!Plugin.Enabled.Value || t <= 0.001f || __instance.speed.IsNone || __instance.vectorPosition == null || __instance.vectorPosition.IsNone
                    || __instance.transformPosition != null && __instance.transformPosition.Value != null)
                {   // the game's own move
                    __instance.speed.Value = o.Speed; if (EaseField != null && o.Ease != null) EaseField.SetValue(__instance, o.Ease);
                    return;
                }
                var go = __instance.Fsm.GetOwnerDefaultTarget(__instance.gameObject);
                if (go == null) return;
                Vector3 from = __instance.space == Space.Self ? go.transform.localPosition : go.transform.position;
                float dist = (__instance.vectorPosition.Value - from).magnitude;
                __instance.speed.Value = Mathf.Max(dist / t, 0.01f);
                if (EaseField != null)
                {
                    if (_easeOut == null) _easeOut = Enum.Parse(EaseField.FieldType, "easeOutQuad");
                    EaseField.SetValue(__instance, _easeOut);
                }
            }
            catch (Exception e) { Plugin.Verbose("Aim transition: " + e.Message); }
        }
    }
}
