using System.Collections.Generic;
using UnityEngine;

namespace Apocaplayer
{
    // Third person on foot. The game only has a third-person camera in cars (DriveTrigger/3rdCamera, "Change Camera" button).
    // On foot we keep the PlayerCamera exactly where the game puts it (all shooting / picking / using raycasts start there) and only
    // render it from behind her shoulder: Camera.onPreCull overrides its worldToCameraMatrix for that frame. The first-person
    // weapon/arm renderers under the PlayerCamera are switched off (forceRenderingOff) while the view is behind her.
    internal static class ThirdPerson
    {
        public static bool On;
        public static bool CarShift;               // set by the Runner: first person in the car with her body shown -> view drawn a bit further forward
        public static bool Orbiting;
        // the third-person picture's camera, as last drawn (for PickAssist: what is under the cursor on screen)
        public static bool HasView;
        public static float ViewFov = 60f;
        // right mouse button with a non-scoped gun in third person: the game only hides its crosshair (its sights are on the hidden
        // first-person gun) - here the view zooms in toward the crosshair instead (narrower field of view, camera a bit closer) and the
        // crosshair stays. Scoped weapons keep the game's own scope (overlay + FOV 25).
        public static bool AimZoom;
        private static float _zoomK, _fovBase;
        private static bool _fovSet;
        private static GameObject _crosshair;
        public const float ZoomFov = 0.55f, ZoomDistance = 0.7f;

        // Harmony prefix on PlayMaker's ActivateGameObject.DoActivateGameObject: while zooming, the weapon's AimDownSights FSM may not switch
        // MouseCrosshair off
        public static bool BeforeActivateGameObject(HutongGames.PlayMaker.Actions.ActivateGameObject __instance)
        {
            if (!AimZoom) return true;
            try
            {
                if (__instance.activate == null || __instance.activate.Value || __instance.Fsm == null) return true;
                var go = __instance.Fsm.GetOwnerDefaultTarget(__instance.gameObject);
                if (go != null && go.name == "MouseCrosshair") { _crosshair = go; if (!go.activeSelf) go.SetActive(true); return false; }
            }
            catch (System.Exception) { }
            return true;
        }

        private static void UpdateZoom()
        {
            string w = Game.DrawnWeapon;
            var k = Props.KindOf(w);
            bool gun = k == Props.Kind.Rifle || k == Props.Kind.Pistol;
            AimZoom = On && !Game.Paused && gun && w.IndexOf("scoped", System.StringComparison.OrdinalIgnoreCase) < 0 && Game.AimDownSights;
            _zoomK = Mathf.MoveTowards(_zoomK, AimZoom ? 1f : 0f, Time.unscaledDeltaTime * 6f);
            if (!On) _zoomK = 0f;
            if (AimZoom && _crosshair != null && !_crosshair.activeSelf) _crosshair.SetActive(true);
        }
        private static float ZoomEase { get { return _zoomK * _zoomK * (3f - 2f * _zoomK); } }
        public static Vector3 ViewPos;
        public static Quaternion ViewRot = Quaternion.identity;               // middle mouse held: the mouse turns the camera around her, the game's mouse look is paused
        private static float _orbitYaw, _orbitPitch;
        private static bool _orbitOn;

        // Harmony prefix on PlayMaker's MouseLook: no player/camera turning while orbiting
        public static bool BeforeMouseLook() { return !Orbiting; }
        private static bool _hooked;
        private static readonly List<Renderer> _hidden = new List<Renderer>();
        private static float _nextScan, _dist, _aimDist = 20f;
        private static readonly int Mask = ~((1 << 6) | (1 << 2) | (1 << 5) | (1 << 9) | (1 << 22));   // not the player, ignore-raycast, UI, loose items, map icons

        // cars: our third person also works in the car (the game's own car view is replaced: it switches the whole first-person camera off, so
        // nothing in the car - ignition, cassette player, Exit (F) at the door - could be used). The PlayerCamera keeps working in the seat
        // and only the picture is drawn from behind / above the car.
        public static bool BeforeGetButtonDown(HutongGames.PlayMaker.Actions.GetButtonDown __instance)
        {
            try
            {
                if (Plugin.Enabled.Value && Plugin.ThirdPersonOnFoot.Value && __instance.Fsm != null && __instance.Fsm.Name == "Camera"
                    && __instance.buttonName != null && __instance.buttonName.Value == "Change Camera" && __instance.Fsm.GameObjectName == "DriveTrigger")
                    return false;   // the car's DriveTrigger [Camera] toggle: our Tick handles Change Camera in the car too
            }
            catch (System.Exception) { }
            return true;
        }

        public static void Tick()
        {
            bool allowed = Plugin.Enabled.Value && Plugin.ThirdPersonOnFoot.Value && Game.Ready && Game.FirstPersonCameraOn;
            bool pressed = false;
            try { pressed = allowed && !Game.Paused && Input.GetButtonDown("Change Camera"); } catch (System.Exception) { }
            if (pressed)
            {
                On = !On;
                _dist = 0.3f;
                Plugin.Verbose("Third person: " + (On ? "on" : "off"));
            }
            if (!allowed && On) On = false;
            // middle mouse button orbits the camera around her: held (back behind her on release), or with ToggleMiddleMouse a click turns it on / off
            bool click = false, held = false;
            try { click = On && !Game.Paused && Input.GetMouseButtonDown(2); held = On && !Game.Paused && Input.GetMouseButton(2); } catch (System.Exception) { }
            if (Plugin.ToggleMiddleMouse.Value) { if (click) _orbitOn = !_orbitOn; }
            else _orbitOn = held;
            if (!On) _orbitOn = false;
            Zoom();
            UpdateZoom();
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

        // mouse wheel: closer / further, one distance on foot and one in cars, both kept in the config. Not while she carries an item
        // on foot (the wheel moves the item then).
        private static float _saveAt;
        private static void Zoom()
        {
            if (!On || Game.Paused) { Save(); return; }
            float w = 0f;
            try { w = Input.mouseScrollDelta.y; } catch (System.Exception) { }
            bool car = Game.InCar;
            if (w != 0f && !car)
            {
                var hand = Game.PlayerCamera != null ? Game.PlayerCamera.Find("Hand") : null;
                if (hand != null && hand.childCount > 0) w = 0f;
            }
            if (w != 0f)
            {
                var e = car ? Plugin.ThirdCarDistance : Plugin.ThirdDistance;
                float lo = car ? 2.5f : 0.8f, hi = car ? 15f : 6f;
                _zoomTo[car ? 1 : 0] = Mathf.Clamp((_zoomTo[car ? 1 : 0] > 0f ? _zoomTo[car ? 1 : 0] : e.Value) * Mathf.Pow(0.88f, w), lo, hi);
                _saveAt = Time.unscaledTime + 1f;
            }
            Save();
        }
        private static readonly float[] _zoomTo = { 0f, 0f };
        private static void Save()
        {
            if (_saveAt <= 0f || Time.unscaledTime < _saveAt) return;
            _saveAt = 0f;
            if (_zoomTo[0] > 0f) Plugin.ThirdDistance.Value = (float)System.Math.Round(_zoomTo[0], 2);   // written to the config once the wheel rests
            if (_zoomTo[1] > 0f) Plugin.ThirdCarDistance.Value = (float)System.Math.Round(_zoomTo[1], 2);
            _zoomTo[0] = _zoomTo[1] = 0f;   // the config is the source again (also edited from the Mods menu)
        }
        private static float Distance(bool car)
        {
            float z = _zoomTo[car ? 1 : 0];
            return z > 0f ? z : (car ? Plugin.ThirdCarDistance.Value : Plugin.ThirdDistance.Value);
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
            Transform car = Game.InCar ? Game.CarRoot : null;
            Vector3 pivot = t.position + Vector3.up * (car != null ? Plugin.ThirdCarHeight.Value : Plugin.ThirdHeight.Value);
            float zk = ZoomEase;
            if (zk > 0.001f) { _fovBase = cam.fieldOfView; cam.fieldOfView = _fovBase * Mathf.Lerp(1f, ZoomFov, zk); _fovSet = true; }
            ViewFov = cam.fieldOfView;
            Vector3 want = pivot - fwd * (Distance(car != null) * Mathf.Lerp(1f, ZoomDistance, zk)) + right * (car != null ? 0f : Plugin.ThirdShoulder.Value);
            Vector3 d = want - pivot;
            float max = d.magnitude;
            float dist = max;
            // walls pull the camera in; in a car its own body doesn't (the camera starts inside it)
            foreach (var h in Physics.SphereCastAll(pivot, 0.2f, d / Mathf.Max(max, 1e-4f), max, Mask, QueryTriggerInteraction.Ignore))
            {
                if (car != null && h.collider != null && h.collider.transform.IsChildOf(car)) continue;
                if (h.distance > 0f && h.distance < dist) dist = Mathf.Max(0.2f, h.distance);
            }
            // come out smoothly, snap in when something is in the way
            _dist = dist < _dist ? dist : Mathf.MoveTowards(_dist, dist, Time.unscaledDeltaTime * 4f);
            Vector3 pos = pivot + d / Mathf.Max(max, 1e-4f) * _dist;
            // converge on the aim point: the camera sits over her shoulder, so looking parallel to her eye line would put the crosshair
            // ThirdShoulder metres beside where shots / picks really go. Find what the eye ray hits (the game casts from the eye) and turn the
            // view so the screen centre looks exactly at it; she stays where she is on screen. Not while orbiting (that view isn't for aiming).
            float conv = 1f - Mathf.Clamp01((Mathf.Abs(_orbitYaw) + Mathf.Abs(_orbitPitch)) / 10f);
            if (conv > 0.001f)
            {
                RaycastHit ah;
                float want2 = Physics.Raycast(t.position, t.forward, out ah, 100f, Mask, QueryTriggerInteraction.Ignore) ? Mathf.Max(ah.distance, 0.8f) : 100f;
                // fast toward nearer targets, a bit slower when the target goes away (no flicker on edges)
                float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * (want2 < _aimDist ? 20f : 8f));
                _aimDist = Mathf.Lerp(_aimDist, want2, k);
                Vector3 aim = t.position + t.forward * _aimDist;
                Vector3 look = aim - pos;
                if (look.sqrMagnitude > 1e-4f) viewRot = Quaternion.Slerp(viewRot, Quaternion.LookRotation(look.normalized, Vector3.up), conv);
            }
            ViewPos = pos; ViewRot = viewRot; HasView = true;
            var view = Matrix4x4.TRS(pos, viewRot, Vector3.one).inverse;
            cam.worldToCameraMatrix = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * view;
        }

        // after the frame is drawn: the camera's real view matrix back, so the game's MousePick / ScreenPointToRay (grab, use, enter the car,
        // vehicle parts, save points) cast from the first-person eye as always - not from the third-person view 2.4 m behind her
        public static void EndOfFrame()
        {
            if (_hooked && Game.Cam != null) Game.Cam.ResetWorldToCameraMatrix();
            if (_fovSet && Game.Cam != null) { Game.Cam.fieldOfView = _fovBase; _fovSet = false; }   // the game's own FOV back for the next frame
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
            On = false; Orbiting = false; _orbitOn = false; CarShift = false; HasView = false; AimZoom = false; _zoomK = 0f;
            if (_fovSet && Game.Cam != null) { Game.Cam.fieldOfView = _fovBase; _fovSet = false; }
            ShowViewModel();
            if (_hooked) { Camera.onPreCull -= PreCull; _hooked = false; }
            if (Game.Cam != null) Game.Cam.ResetWorldToCameraMatrix();
        }
    }
}
