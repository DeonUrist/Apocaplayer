using System;
using System.Reflection;
using UnityEngine;

namespace Apocaplayer
{
    // Third person: picking what is under the crosshair. The game's picks (grab / carry items, use, vehicle parts, doors ...) all go through
    // PlayMaker's ActionHelpers.DoMousePick: one ray from the first-person eye through the cursor, at most `distance` long, cached per frame.
    // Behind her shoulder you aim with the third-person picture, so a small item that is plainly under the crosshair can be missed by that
    // eye ray (it passes beside or over it, or ends on the ground in front of it).
    //
    // Harmony postfix on DoMousePick, third person only, when the game's own ray found nothing usable: among the colliders the pick could
    // reach (within `distance` of the eye, on the pick's layer mask, that have a Rigidbody or a PlayMaker FSM = items, parts, switches),
    // the one whose point nearest the cursor ray is closest to the cursor ON SCREEN (the third-person picture) wins, if it lies within
    // PickAssistRadius of the screen height and the third-person camera can see it. Measured in screen pixels, so it works the same at
    // every zoom level. A ray from the eye to that point then becomes the pick result, exactly as if the game's own ray had hit it.
    internal static class PickAssist
    {
        private static FieldInfo _info;
        private static readonly Collider[] _near = new Collider[128];
        private static readonly RaycastHit[] _hits = new RaycastHit[32];

        public static void Patch(HarmonyLib.Harmony h)
        {
            var t = typeof(HutongGames.PlayMaker.ActionHelpers);
            _info = HarmonyLib.AccessTools.Field(t, "mousePickInfo");
            var m = HarmonyLib.AccessTools.Method(t, "DoMousePick");
            if (_info == null || m == null) { Plugin.Warn("Pick assist: ActionHelpers.DoMousePick / mousePickInfo not found - off"); return; }
            h.Patch(m, postfix: new HarmonyLib.HarmonyMethod(typeof(PickAssist), nameof(AfterDoMousePick)));
        }

        public static void AfterDoMousePick(float distance, int layerMask)
        {
            try
            {
                if (Plugin.Enabled.Value && ThirdPerson.On && Game.InCar && Game.CarRoot != null)
                {
                    // Dashboard/parts must never capture fire or wheel input in third person.
                    // Keep the DriveTrigger pick for the game's F-to-exit action.
                    var picked = (RaycastHit)_info.GetValue(null);
                    if (picked.collider != null && BlockedVehiclePart(picked.collider.transform))
                        _info.SetValue(null, default(RaycastHit));
                    return; // no screen-space assist inside a vehicle
                }
                if (Plugin.Enabled.Value && ThirdPerson.CarShift && !ThirdPerson.On && Game.Cam != null && Camera.main == Game.Cam && !Game.Paused)
                {   // first person in the car with her body: the picture is drawn from a bit in front of the eye, so the game's eye ray (from
                    // behind that) ends on what is in front - the cassette player instead of the ignition / light switches under the cursor.
                    // The pick is cast again from the drawn eye, through the same cursor direction.
                    var ray = Game.Cam.ScreenPointToRay(Input.mousePosition);
                    RaycastHit h;
                    Vector3 eye = ThirdPerson.CarShiftEye(Game.Cam.transform);
                    float d = Mathf.Max(0.01f, distance - Vector3.Dot(eye - ray.origin, ray.direction));   // the same reach from the real eye
                    _info.SetValue(null, Physics.Raycast(eye, ray.direction, out h, d, layerMask) ? h : default(RaycastHit));
                    return;
                }
                if (!Plugin.Enabled.Value || !ThirdPerson.On || !ThirdPerson.HasView || Game.Cam == null || Camera.main != Game.Cam || Game.Paused) return;
                var cur = (RaycastHit)_info.GetValue(null);
                if (cur.collider != null && Usable(cur.collider)) return;   // the game's own ray already has something
                RaycastHit hit;
                if (Find(distance, layerMask, out hit)) _info.SetValue(null, hit);
            }
            catch (Exception e) { Plugin.Verbose("Pick assist: " + e.Message); }
        }

        // things a pick is for: items and parts (a Rigidbody) or anything with its own FSM (switches, doors, ignition, cassette ...)
        private static bool BlockedVehiclePart(Transform target)
        {
            var car = Game.CarRoot;
            if (car == null || !target.IsChildOf(car)) return false;
            var drive = car.Find("DriveTrigger");
            return drive == null || !target.IsChildOf(drive);
        }

        private static bool Usable(Collider c)
        {
            return c.attachedRigidbody != null || c.GetComponent<PlayMakerFSM>() != null;
        }

        private static bool Mine(Collider c)
        {
            var t = c.transform;
            return Game.Player != null && t.IsChildOf(Game.Player.transform) || Game.PlayerCamera != null && t.IsChildOf(Game.PlayerCamera);
        }

        private static bool Find(float distance, int layerMask, out RaycastHit result)
        {
            result = default(RaycastHit);
            var cam = Game.Cam;
            Vector3 eye = cam.transform.position;
            // the cursor ray of the third-person picture
            Vector3 mouse = Input.mousePosition;
            float W = Screen.width, H = Screen.height;
            float nx = mouse.x / W * 2f - 1f, ny = mouse.y / H * 2f - 1f;
            float tan = Mathf.Tan(ThirdPerson.ViewFov * 0.5f * Mathf.Deg2Rad);
            Vector3 rpos = ThirdPerson.ViewPos; Quaternion rrot = ThirdPerson.ViewRot;
            Vector3 rdir = (rrot * new Vector3(nx * tan * cam.aspect, ny * tan, 1f)).normalized;
            var viewM = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.TRS(rpos, rrot, Vector3.one).inverse;
            var vp = Matrix4x4.Perspective(ThirdPerson.ViewFov, cam.aspect, cam.nearClipPlane, cam.farClipPlane) * viewM;
            float radius = Mathf.Max(16f, Plugin.PickAssistRadius.Value * H);

            int n = Physics.OverlapSphereNonAlloc(eye, distance, _near, layerMask, QueryTriggerInteraction.UseGlobal);
            Collider best = null; Vector3 bestPoint = Vector3.zero; float bestScore = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var c = _near[i];
                if (c == null || !c.enabled || Mine(c) || !Usable(c)) continue;
                // the point of it nearest the cursor ray: the ray's point nearest its centre, pulled onto its bounds
                var b = c.bounds;
                float along = Mathf.Max(0f, Vector3.Dot(b.center - rpos, rdir));
                Vector3 q = b.ClosestPoint(rpos + rdir * along);
                if ((q - eye).sqrMagnitude > distance * distance) continue;
                Vector4 clip = vp * new Vector4(q.x, q.y, q.z, 1f);
                if (clip.w <= 0.01f) continue;   // behind the third-person camera
                Vector2 sp = new Vector2((clip.x / clip.w * 0.5f + 0.5f) * W, (clip.y / clip.w * 0.5f + 0.5f) * H);
                float px = (sp - (Vector2)mouse).magnitude;
                if (px > radius) continue;
                // big things (a whole car, a wall with an FSM) only win when nothing small is near the cursor
                float size = b.size.magnitude;
                float score = px + (size > 1.5f ? radius * 0.5f : 0f) + (q - eye).magnitude * 2f;
                if (score >= bestScore || !Visible(rpos, q, c)) continue;
                best = c; bestPoint = q; bestScore = score;
            }
            if (best == null) return false;

            // the pick result: from the eye to that point, as the game's own ray would have hit it
            Vector3 dir = bestPoint - eye; float len = dir.magnitude;
            if (len < 1e-3f) return false;
            dir /= len;
            int m = Physics.RaycastNonAlloc(eye, dir, _hits, Mathf.Min(distance, len + 0.5f), layerMask, QueryTriggerInteraction.UseGlobal);
            float nearest = float.MaxValue; bool found = false;
            for (int i = 0; i < m; i++)
            {
                var h = _hits[i];
                if (h.collider == null || !Same(h.collider, best) || h.distance >= nearest) continue;
                nearest = h.distance; result = h; found = true;
            }
            if (!found)
            {
                // the eye ray grazes past (thin item, point on its bounds not its surface): aim at its centre instead
                dir = (best.bounds.center - eye); len = dir.magnitude;
                if (len > 1e-3f && len <= distance + 0.3f)
                {
                    m = Physics.RaycastNonAlloc(eye, dir / len, _hits, Mathf.Min(distance, len + 0.5f), layerMask, QueryTriggerInteraction.UseGlobal);
                    for (int i = 0; i < m; i++)
                    {
                        var h = _hits[i];
                        if (h.collider == null || !Same(h.collider, best) || h.distance >= nearest) continue;
                        nearest = h.distance; result = h; found = true;
                    }
                }
            }
            return found;
        }

        private static bool Same(Collider a, Collider b)
        {
            return a == b || a.attachedRigidbody != null && a.attachedRigidbody == b.attachedRigidbody;
        }

        // what you see is what you can pick: nothing but her and the thing itself between the third-person camera and the point
        private static bool Visible(Vector3 from, Vector3 to, Collider target)
        {
            Vector3 d = to - from; float len = d.magnitude;
            if (len < 1e-3f) return true;
            int m = Physics.RaycastNonAlloc(from, d / len, _hits, len - 0.02f, ~((1 << 2) | (1 << 5)), QueryTriggerInteraction.Ignore);
            for (int i = 0; i < m; i++)
            {
                var c = _hits[i].collider;
                if (c == null || Same(c, target) || Mine(c)) continue;
                if (Game.InCar && Game.CarRoot != null && c.transform.IsChildOf(Game.CarRoot)) continue;   // the car body around a seat
                return false;
            }
            return true;
        }
    }
}
