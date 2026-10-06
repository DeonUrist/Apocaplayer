using System;
using UnityEngine;

namespace Apocaplayer
{
    // Collision callbacks are free of scene searches: landing/headroom queries run
    // only when a grounded player is actively pushing into a low contact.
    internal static class AutoStepUp
    {
        internal const float MaxHeight = .35f;
        private static Rigidbody _body;
        private static CapsuleCollider[] _capsules;
        private static CapsuleCollider _primary;
        private static Contacts _contacts;
        private static float _nextStep;
        internal static int LastReject;
        private static readonly RaycastHit[] Hits = new RaycastHit[32];
        private static readonly Collider[] Nearby = new Collider[32];

        internal sealed class Contacts : MonoBehaviour
        {
            internal float GroundAt = float.NegativeInfinity, LowAt = float.NegativeInfinity;
            internal Collider Low;
            internal Vector3 Normal;
            private void OnCollisionEnter(Collision collision) { Record(collision); }
            private void OnCollisionStay(Collision collision) { Record(collision); }
            private void Record(Collision collision)
            {
                if (_primary == null || gameObject != _primary.gameObject) return;
                Vector3 bottom, top; float radius, feet;
                Geometry(_primary, Vector3.zero, out bottom, out top, out radius, out feet);
                for (int i = 0; i < collision.contactCount; i++)
                {
                    var contact = collision.GetContact(i);
                    var other = contact.otherCollider;
                    if (!Solid(other, _primary)) continue;
                    float height = contact.point.y - feet;
                    // A rounded sole resting on a stair edge can be supported by
                    // a diagonal normal and lose its previous floor contact.
                    if (contact.normal.y > .35f && height >= -.035f && height <= MaxHeight + .035f) GroundAt = Time.fixedTime;
                    if (height >= -.035f && height <= MaxHeight + .035f && Mathf.Abs(contact.normal.y) < .95f)
                    { Low = other; Normal = contact.normal; LowAt = Time.fixedTime; }
                }
            }
        }

        public static void Tick()
        {
            if (!Plugin.Enabled.Value || !Plugin.AutomaticStepUp.Value || !Game.Ready || Game.Paused || Game.Dead || Game.InCar) return;
            if (Game.StandingHeight < .75f || Game.MoveState.StartsWith("Crawl", StringComparison.Ordinal)) return;
            if (Game.JumpState == "Jump" || Game.JumpState == "Falling") return;
            if (_body == null || _body.gameObject != Game.Player) Attach(Game.Player);
            if (_body == null || _body.isKinematic || _primary == null || _contacts == null || Time.fixedTime < _nextStep) return;
            float fresh = Mathf.Max(.06f, Time.fixedDeltaTime * 2.5f);
            if (Time.fixedTime - _contacts.GroundAt > fresh || Time.fixedTime - _contacts.LowAt > fresh || _body.velocity.y > .3f) return;
            var movement = Game.MovementFsm;
            var horizontal = movement != null ? movement.FsmVariables.FindFsmFloat("AxisHorizontal") : null;
            var vertical = movement != null ? movement.FsmVariables.FindFsmFloat("AxisVertical") : null;
            Vector3 intent = horizontal != null && vertical != null
                ? Game.Player.transform.right * horizontal.Value + Game.Player.transform.forward * vertical.Value : _body.velocity;
            intent.y = 0;
            if (intent.sqrMagnitude < .04f || Vector3.Dot(intent.normalized, _contacts.Normal) > -.1f) return;
            if (TryStep(intent.normalized)) { _nextStep = Time.fixedTime + .08f; _contacts.LowAt = float.NegativeInfinity; }
        }

        private static void Attach(GameObject player)
        {
            Reset();
            _body = player != null ? player.GetComponent<Rigidbody>() : null;
            if (_body == null) return;
            _capsules = player.GetComponents<CapsuleCollider>();
            foreach (var capsule in _capsules)
                if (capsule.enabled && !capsule.isTrigger && capsule.direction == 1 && (_primary == null || capsule.height > _primary.height)) _primary = capsule;
            if (_primary != null) _contacts = player.AddComponent<Contacts>();
            if (_contacts != null) _contacts.hideFlags = HideFlags.HideAndDontSave;
        }

        private static void Geometry(CapsuleCollider capsule, Vector3 offset, out Vector3 bottom, out Vector3 top, out float radius, out float feet)
        {
            var scale = capsule.transform.lossyScale;
            radius = capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float height = Mathf.Max(radius * 2, capsule.height * Mathf.Abs(scale.y));
            Vector3 center = capsule.transform.TransformPoint(capsule.center) + offset;
            feet = center.y - height * .5f;
            bottom = center - Vector3.up * (height * .5f - radius);
            top = center + Vector3.up * (height * .5f - radius);
        }

        private static bool Solid(Collider other, CapsuleCollider capsule)
        {
            return other != null && other.enabled && !other.isTrigger && other.gameObject.activeInHierarchy
                && !other.transform.IsChildOf(capsule.transform) && !Physics.GetIgnoreCollision(capsule, other)
                && !Physics.GetIgnoreLayerCollision(capsule.gameObject.layer, other.gameObject.layer);
        }

        private static bool SlowSurface(Collider collider)
        {
            var body = collider.attachedRigidbody;
            return body == null || body.isKinematic || body.velocity.sqrMagnitude < .5625f && body.angularVelocity.sqrMagnitude < .25f;
        }

        private static bool TryStep(Vector3 forward)
        {
            Vector3 bottom, top; float radius, feet;
            Geometry(_primary, Vector3.zero, out bottom, out top, out radius, out feet);
            float largestRadius = radius;
            foreach (var capsule in _capsules) if (capsule.enabled && !capsule.isTrigger) largestRadius = Mathf.Max(largestRadius, capsule.radius * Mathf.Max(Mathf.Abs(capsule.transform.lossyScale.x), Mathf.Abs(capsule.transform.lossyScale.z)));
            float reach = largestRadius + .075f + Mathf.Min(.08f, new Vector2(_body.velocity.x, _body.velocity.z).magnitude * Time.fixedDeltaTime);
            Vector3 origin = new Vector3(bottom.x, feet + MaxHeight + .05f, bottom.z) + forward * reach;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, Hits, MaxHeight + .06f, ~0, QueryTriggerInteraction.Ignore);
            RaycastHit landing = default(RaycastHit); float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
                if (Solid(Hits[i].collider, _primary) && Hits[i].distance < nearest) { landing = Hits[i]; nearest = landing.distance; }
            if (landing.collider == null || landing.normal.y < .65f || !SlowSurface(landing.collider)) { LastReject = 1; return false; }
            float rise = landing.point.y - feet;
            if (rise < .025f || rise > MaxHeight) { LastReject = 2; return false; }
            Vector3 lift = Vector3.up * (rise + .015f);
            foreach (var capsule in _capsules)
            {
                if (!capsule.enabled || capsule.isTrigger) continue;
                Geometry(capsule, Vector3.zero, out bottom, out top, out radius, out feet);
                if (BlockedSweep(capsule, bottom, top, radius, Vector3.up, lift.y)) { LastReject = 3; return false; }
                bottom += lift; top += lift;
                if (BlockedSweep(capsule, bottom, top, radius, forward, reach)) { LastReject = 4; return false; }
                int overlaps = Physics.OverlapCapsuleNonAlloc(bottom, top, Mathf.Max(.01f, radius - .005f), Nearby, ~0, QueryTriggerInteraction.Ignore);
                if (overlaps == Nearby.Length) { LastReject = 5; return false; }
                for (int i = 0; i < overlaps; i++) if (Solid(Nearby[i], capsule)) { LastReject = 5; return false; }
            }
            _body.position += lift;
            LastReject = 0;
            if (_body.velocity.y < 0) { var velocity = _body.velocity; velocity.y = 0; _body.velocity = velocity; }
            return true;
        }

        private static bool BlockedSweep(CapsuleCollider capsule, Vector3 bottom, Vector3 top, float radius, Vector3 direction, float distance)
        {
            int count = Physics.CapsuleCastNonAlloc(bottom, top, Mathf.Max(.01f, radius - .005f), direction, Hits, distance, ~0, QueryTriggerInteraction.Ignore);
            if (count == Hits.Length) return true;
            for (int i = 0; i < count; i++) if (Solid(Hits[i].collider, capsule)) return true;
            return false;
        }

        public static void Reset()
        {
            if (_contacts != null) UnityEngine.Object.Destroy(_contacts);
            _contacts = null; _body = null; _primary = null; _capsules = null; _nextStep = 0;
        }
    }
}
