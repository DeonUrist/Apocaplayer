using System.Collections.Generic;
using UnityEngine;

namespace FemalePlayer
{
    // Third person on foot. The game only has a third-person camera in cars (DriveTrigger/3rdCamera, "Change Camera" button).
    // On foot we keep the PlayerCamera exactly where the game puts it (all shooting / picking / using raycasts start there) and only
    // render it from behind her shoulder: Camera.onPreCull overrides its worldToCameraMatrix for that frame. The first-person
    // weapon/arm renderers under the PlayerCamera are switched off (forceRenderingOff) while the view is behind her.
    internal static class ThirdPerson
    {
        public static bool On;
        private static bool _hooked;
        private static readonly List<Renderer> _hidden = new List<Renderer>();
        private static float _nextScan, _dist;
        private static readonly int Mask = ~((1 << 6) | (1 << 2) | (1 << 5) | (1 << 9) | (1 << 22));   // not the player, ignore-raycast, UI, loose items, map icons

        public static void Tick()
        {
            bool allowed = Plugin.Enabled.Value && Plugin.ThirdPersonOnFoot.Value && Game.Ready && !Game.InCar && Game.FirstPersonCameraOn;
            bool pressed = false;
            try { pressed = allowed && !Game.Paused && Input.GetButtonDown("Change Camera"); } catch (System.Exception) { }
            if (pressed)
            {
                On = !On;
                _dist = 0.3f;
                Plugin.Verbose("Third person on foot: " + (On ? "on" : "off"));
            }
            if (!allowed && On) On = false;
            if (On && !_hooked) { Camera.onPreCull += PreCull; _hooked = true; }
            if (On) HideViewModel();
            else if (_hidden.Count > 0) ShowViewModel();
            if (!On && Game.Cam != null && _hooked) { Game.Cam.ResetWorldToCameraMatrix(); Camera.onPreCull -= PreCull; _hooked = false; }
        }

        private static void PreCull(Camera cam)
        {
            if (!On || cam == null || cam != Game.Cam) return;
            var t = cam.transform;
            Vector3 fwd = t.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd); if (right.sqrMagnitude < 1e-4f) right = t.right; right.Normalize();
            Vector3 pivot = t.position + Vector3.up * Plugin.ThirdHeight.Value;
            Vector3 want = pivot - fwd * Plugin.ThirdDistance.Value + right * Plugin.ThirdShoulder.Value;
            Vector3 d = want - pivot;
            float max = d.magnitude;
            RaycastHit hit;
            float dist = max;
            if (Physics.SphereCast(pivot, 0.2f, d / Mathf.Max(max, 1e-4f), out hit, max, Mask, QueryTriggerInteraction.Ignore)) dist = Mathf.Max(0.2f, hit.distance);
            // come out smoothly, snap in when something is in the way
            _dist = dist < _dist ? dist : Mathf.MoveTowards(_dist, dist, Time.unscaledDeltaTime * 4f);
            Vector3 pos = pivot + d / Mathf.Max(max, 1e-4f) * _dist;
            var view = Matrix4x4.TRS(pos, t.rotation, Vector3.one).inverse;
            cam.worldToCameraMatrix = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * view;
        }

        private static void HideViewModel()
        {
            if (Time.unscaledTime < _nextScan || Game.PlayerCamera == null) return;
            _nextScan = Time.unscaledTime + 0.5f;
            foreach (var r in Game.PlayerCamera.GetComponentsInChildren<Renderer>(true))
                if (r != null && !r.forceRenderingOff) { r.forceRenderingOff = true; _hidden.Add(r); }
        }

        private static void ShowViewModel()
        {
            foreach (var r in _hidden) if (r != null) r.forceRenderingOff = false;
            _hidden.Clear();
        }

        public static void Off()
        {
            On = false;
            ShowViewModel();
            if (_hooked) { Camera.onPreCull -= PreCull; _hooked = false; }
            if (Game.Cam != null) Game.Cam.ResetWorldToCameraMatrix();
        }
    }
}
