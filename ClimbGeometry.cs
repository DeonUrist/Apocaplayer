using UnityEngine;

namespace Apocaplayer
{
    internal struct ClimbTarget
    {
        public Collider Surface;
        public Vector3 Start, Lift, End;
        public Quaternion Facing;
        public float Rise;
    }

    // Shared by the mod and the Unity physics verification project.
    internal sealed class ClimbGeometry
    {
        const float Skin = .015f;
        readonly RaycastHit[] hits = new RaycastHit[128];
        readonly Collider[] overlaps = new Collider[128];
        readonly Rigidbody body;
        readonly CapsuleCollider[] capsules;
        readonly CapsuleCollider primary;
        public string Rejection { get; private set; }
        public bool Valid { get { return primary != null && capsules.Length > 0; } }

        public ClimbGeometry(Rigidbody player)
        {
            body = player;
            capsules = player.GetComponentsInChildren<CapsuleCollider>();
            foreach (var c in capsules)
                if (c.enabled && !c.isTrigger && c.attachedRigidbody == body && c.direction == 1
                    && (primary == null || c.bounds.size.y > primary.bounds.size.y)) primary = c;
        }

        void Capsule(CapsuleCollider c, Vector3 offset, out Vector3 bottom, out Vector3 top, out float radius, out Vector3 feet)
        {
            var s = c.transform.lossyScale;
            radius = c.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z));
            float height = Mathf.Max(radius * 2, c.height * Mathf.Abs(s.y));
            // Rigidbody.position changes during a physics tick before Unity always
            // publishes that pose to Transform (especially with interpolation).
            // Cast from the physical position, retaining each capsule's local offset.
            var center = body.position + (c.transform.TransformPoint(c.center) - body.transform.position) + offset;
            feet = center - Vector3.up * (height * .5f);
            bottom = feet + Vector3.up * radius;
            top = center + Vector3.up * (height * .5f - radius);
        }

        bool Solid(Collider other, CapsuleCollider capsule)
        {
            return other != null && other.enabled && !other.isTrigger && other.gameObject.activeInHierarchy
                && other.attachedRigidbody != body && !other.transform.IsChildOf(body.transform)
                && !Physics.GetIgnoreCollision(capsule, other)
                && !Physics.GetIgnoreLayerCollision(capsule.gameObject.layer, other.gameObject.layer);
        }

        public static bool Slow(Collider c)
        {
            if (c == null) return false;
            var rb = c.attachedRigidbody;
            return rb == null || rb.velocity.sqrMagnitude < .5625f && rb.angularVelocity.sqrMagnitude < .25f;
        }

        bool Nearest(int count, out RaycastHit hit)
        {
            hit = default(RaycastHit);
            if (count == hits.Length) return false;
            float distance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
                if (Solid(hits[i].collider, primary) && hits[i].distance < distance)
                { hit = hits[i]; distance = hit.distance; }
            return hit.collider != null;
        }

        bool Reject(string reason) { Rejection = reason; return false; }

        public bool Find(Vector3 forward, float minHeight, float maxHeight, float reach, out ClimbTarget target)
        {
            target = default(ClimbTarget);
            Rejection = "";
            if (!Valid || body.isKinematic) return Reject("Player capsule unavailable");
            foreach (var c in capsules)
                if (c.enabled && !c.isTrigger && c.attachedRigidbody == body && c.direction != 1) return Reject("Player capsule is not upright");
            forward.y = 0;
            if (forward.sqrMagnitude < .01f) return Reject("No forward direction");
            forward.Normalize();
            Vector3 bottom, top, feet; float radius;
            Capsule(primary, Vector3.zero, out bottom, out top, out radius, out feet);
            RaycastHit ground;
            // A sphere beside a wall can report the wall corner before the floor.
            // Probe the sole centre so pushing into a wall does not lose grounding.
            int count = Physics.RaycastNonAlloc(feet + Vector3.up * .15f, Vector3.down, hits,
                .35f, ~0, QueryTriggerInteraction.Ignore);
            if (!Nearest(count, out ground) || ground.normal.y < .65f || body.velocity.y > .6f) return Reject("Must stand on ground");
            RaycastHit face;
            face = default(RaycastHit);
            // Check slightly ahead at multiple heights: a car body may begin above
            // the lowest ray, while its roof is still a reachable climb target.
            for (int level = 0; level < 3; level++)
            {
                float probeHeight = minHeight + .08f + level * .55f;
                count = Physics.SphereCastNonAlloc(feet + Vector3.up * probeHeight, .08f, forward, hits,
                    reach + .12f, ~0, QueryTriggerInteraction.Ignore);
                RaycastHit candidate;
                if (Nearest(count, out candidate) && Mathf.Abs(candidate.normal.y) <= .5f) { face = candidate; break; }
            }
            if (face.collider == null) return Reject("No climbable face in reach");
            if (!Slow(face.collider)) return Reject("Obstacle is moving too fast");
            // Sample real surfaces behind the face; bounds.max.y would incorrectly
            // treat a car's entire bounding box as its roof.
            string last = "No walkable top within height limit";
            for (int i = 0; i < 5; i++)
            {
                var probe = face.point + forward * (radius + .22f + i * .12f);
                probe.y = feet.y + maxHeight + .12f;
                RaycastHit landing;
                count = Physics.RaycastNonAlloc(probe, Vector3.down, hits, maxHeight - minHeight + .13f, ~0, QueryTriggerInteraction.Ignore);
                if (!Nearest(count, out landing) || landing.normal.y < .65f || !Slow(landing.collider)) continue;
                float rise = landing.point.y - feet.y;
                if (rise < minHeight || rise > maxHeight) continue;
                var end = landing.point + (body.position - feet) + Vector3.up * .025f;
                var lift = new Vector3(body.position.x, end.y, body.position.z);
                if (!Clear(body.position, lift) || !Clear(lift, end)) { last = "Climb path or landing has no body clearance"; continue; }
                target = new ClimbTarget { Surface = landing.collider, Start = body.position, Lift = lift, End = end,
                    Rise = rise, Facing = Quaternion.LookRotation(forward, Vector3.up) };
                return true;
            }
            return Reject(last);
        }

        public bool Clear(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            foreach (var c in capsules)
            {
                if (!c.enabled || c.isTrigger || c.attachedRigidbody != body) continue;
                if (c.direction != 1) return false;
                Vector3 bottom, top, feet; float radius;
                Capsule(c, from - body.position, out bottom, out top, out radius, out feet);
                radius = Mathf.Max(.01f, radius - Skin);
                if (distance > .0001f)
                {
                    int count = Physics.CapsuleCastNonAlloc(bottom, top, radius, delta / distance, hits, distance, ~0, QueryTriggerInteraction.Ignore);
                    if (count == hits.Length) return false;
                    for (int i = 0; i < count; i++)
                        if (Solid(hits[i].collider, c) && !EscapesExistingContact(c, hits[i].collider, from, to)) return false;
                }
                int n = Physics.OverlapCapsuleNonAlloc(bottom + delta, top + delta, radius, overlaps, ~0, QueryTriggerInteraction.Ignore);
                if (n == overlaps.Length) return false;
                for (int i = 0; i < n; i++)
                    if (Solid(overlaps[i], c) && !EscapesExistingContact(c, overlaps[i], from, to)) return false;
            }
            return true;
        }

        bool EscapesExistingContact(CapsuleCollider capsule, Collider other, Vector3 from, Vector3 to)
        {
            // PhysX contact pressure can leave a few millimetres of penetration.
            // An upward mantle must be able to slide out of that existing wall
            // contact; it must never excuse a new obstacle or deepen penetration.
            Vector3 origin = capsule.transform.position + from - body.transform.position;
            Vector3 direction; float depth;
            if (!Physics.ComputePenetration(capsule, origin, capsule.transform.rotation,
                other, other.transform.position, other.transform.rotation, out direction, out depth)) return false;
            if (depth > .045f || Vector3.Dot(to - from, direction) < -.0001f) return false;
            Vector3 endDirection; float endDepth;
            if (!Physics.ComputePenetration(capsule, origin + to - from, capsule.transform.rotation,
                other, other.transform.position, other.transform.rotation, out endDirection, out endDepth)) return true;
            return endDepth <= depth + .002f;
        }
    }

    internal static class ClimbPath
    {
        static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3 - 2 * x); }
        // Lift before crossing the lip so the actual capsule never enters the wall.
        public static Vector3 At(ClimbTarget target, float phase, bool high)
        {
            float split = high ? .72f : .6f;
            if (phase < split) return Vector3.Lerp(target.Start, target.Lift, Smooth(phase / split));
            return Vector3.Lerp(target.Lift, target.End, Smooth((phase - split) / (1 - split)));
        }
    }
}
