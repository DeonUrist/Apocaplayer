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
        public static bool CarShift;               // set by the Runner: first person in the car with her body shown -> view drawn a bit further forward
        public static bool Orbiting;               // middle mouse held: the mouse turns the camera around her, the game's mouse look is paused
        private static float _orbitYaw, _orbitPitch;
        private static bool _orbitOn;

        // Harmony prefix on PlayMaker's MouseLook: no player/camera turning while orbiting
        public static bool BeforeMouseLook() { return !Orbiting; }
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
            // middle mouse button = orbit on / off (toggle)
            bool click = false;
            try { click = On && !Game.Paused && Input.GetMouseButtonDown(2); } catch (System.Exception) { }
            if (click) _orbitOn = !_orbitOn;
            if (!On) _orbitOn = false;
            Orbiting = false;
            if (On && !Game.Paused)
            {
                float mx = 0f, my = 0f;
                try { mx = Input.GetAxis("Mouse X"); my = Input.GetAxis("Mouse Y"); } catch (System.Exception) { }
                if (_orbitOn)
                {
                    Orbiting = true;
                    _orbitYaw += mx * Plugin.OrbitSpeed.Value;
                    _orbitPitch = Mathf.Clamp(_orbitPitch - my * Plugin.OrbitSpeed.Value, -70f, 70f);
                }
            }
            if (!Orbiting)   // released: swing back behind her
            {
                float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 6f);
                _orbitYaw = Mathf.LerpAngle(_orbitYaw, 0f, k); _orbitPitch = Mathf.Lerp(_orbitPitch, 0f, k);
                if (Mathf.Abs(_orbitYaw) < 0.05f) _orbitYaw = 0f;
            }
            if (!On) { _orbitYaw = _orbitPitch = 0f; }
            bool want = On || CarShift;
            if (want && !_hooked) { Camera.onPreCull += PreCull; _hooked = true; }
            if (On) HideViewModel();
            else if (_hidden.Count > 0) ShowViewModel();
            if (!want && _hooked) { if (Game.Cam != null) Game.Cam.ResetWorldToCameraMatrix(); Camera.onPreCull -= PreCull; _hooked = false; }
        }

        private static void PreCull(Camera cam)
        {
            if (cam == null || cam != Game.Cam) return;
            var t = cam.transform;
            if (!On)
            {
                if (!CarShift || Game.Player == null) { cam.ResetWorldToCameraMatrix(); return; }
                // driving, first person: the eye a few cm forward along her (the seat's) facing, so her own head/chest don't fill the view
                Vector3 f = Game.Player.transform.forward; f.y = 0f;
                if (f.sqrMagnitude < 1e-4f) f = t.forward;
                Vector3 p = t.position + f.normalized * Plugin.CarCameraForward.Value;
                cam.worldToCameraMatrix = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.TRS(p, t.rotation, Vector3.one).inverse;
                return;
            }
            // orbit: the view direction turned about her by the middle-mouse offsets (yaw about up, pitch clamped)
            var e = t.rotation.eulerAngles;
            float pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, e.x) + _orbitPitch, -80f, 85f);
            var viewRot = Quaternion.Euler(pitch, e.y + _orbitYaw, 0f);
            Vector3 fwd = viewRot * Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd); if (right.sqrMagnitude < 1e-4f) right = viewRot * Vector3.right; right.Normalize();
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
            var view = Matrix4x4.TRS(pos, viewRot, Vector3.one).inverse;
            cam.worldToCameraMatrix = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * view;
        }

        // after the frame is drawn: the camera's real view matrix back, so the game's MousePick / ScreenPointToRay (grab, use, enter the car,
        // vehicle parts, save points) cast from the first-person eye as always - not from the third-person view 2.4 m behind her
        public static void EndOfFrame()
        {
            if (_hooked && Game.Cam != null) Game.Cam.ResetWorldToCameraMatrix();
        }

        private static void HideViewModel()
        {
            if (Time.unscaledTime < _nextScan || Game.PlayerCamera == null) return;
            _nextScan = Time.unscaledTime + 0.5f;
            var hand = Game.PlayerCamera.Find("Hand");   // the item being carried stays visible
            for (int i = _hidden.Count - 1; i >= 0; i--)
            {
                var h = _hidden[i];
                if (h == null) { _hidden.RemoveAt(i); continue; }
                if (!h.transform.IsChildOf(Game.PlayerCamera) || hand != null && h.transform.IsChildOf(hand)) { h.forceRenderingOff = false; _hidden.RemoveAt(i); }   // dropped / picked up
            }
            foreach (var r in Game.PlayerCamera.GetComponentsInChildren<Renderer>(true))
                if (r != null && !r.forceRenderingOff && (hand == null || !r.transform.IsChildOf(hand))) { r.forceRenderingOff = true; _hidden.Add(r); }
        }

        private static void ShowViewModel()
        {
            foreach (var r in _hidden) if (r != null) r.forceRenderingOff = false;
            _hidden.Clear();
        }

        public static void Off()
        {
            On = false; Orbiting = false; _orbitOn = false; CarShift = false;
            ShowViewModel();
            if (_hooked) { Camera.onPreCull -= PreCull; _hooked = false; }
            if (Game.Cam != null) Game.Cam.ResetWorldToCameraMatrix();
        }
    }
}
