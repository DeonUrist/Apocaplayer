using System.Collections.Generic;
using UnityEngine;

namespace Apocaplayer
{
    // Physics proxies are unscaled world-space objects, so mirrored raider rigs work too.
    // Animation stops at death; the existing mesh then follows these jointed bodies.
    internal sealed class Ragdoll
    {
        private readonly GameObject _root = new GameObject("ApocaplayerRagdoll");
        private readonly List<Transform> _bones = new List<Transform>();
        private readonly List<Rigidbody> _bodies = new List<Rigidbody>();
        private readonly List<Collider> _colliders = new List<Collider>();
        public Vector3 Focus { get { return _bodies.Count > 0 ? _bodies[0].position : _root.transform.position; } }

        public Ragdoll(Dictionary<string, Transform> bones, Vector3 velocity, Transform car)
        {
            Add(bones, "Hips", "Spine", -1, 0.16f, 12f);
            Add(bones, "Spine2", "Neck", 0, 0.17f, 18f);
            Add(bones, "Head", null, 1, 0.105f, 5f);
            Add(bones, "LeftArm", "LeftForeArm", 1, 0.065f, 2.5f);
            Add(bones, "LeftForeArm", "LeftHand", 3, 0.055f, 1.5f);
            Add(bones, "RightArm", "RightForeArm", 1, 0.065f, 2.5f);
            Add(bones, "RightForeArm", "RightHand", 5, 0.055f, 1.5f);
            Add(bones, "LeftUpLeg", "LeftLeg", 0, 0.09f, 7f);
            Add(bones, "LeftLeg", "LeftFoot", 7, 0.07f, 4f);
            Add(bones, "RightUpLeg", "RightLeg", 0, 0.09f, 7f);
            Add(bones, "RightLeg", "RightFoot", 9, 0.07f, 4f);

            var player = Game.Player != null ? Game.Player.GetComponentsInChildren<Collider>(true) : new Collider[0];
            var vehicle = car != null ? car.GetComponentsInChildren<Collider>(true) : new Collider[0];
            for (int i = 0; i < _colliders.Count; i++)
            {
                // Limbs collide with the world, but overlapping bodies must not explode on activation.
                for (int j = i + 1; j < _colliders.Count; j++) Physics.IgnoreCollision(_colliders[i], _colliders[j]);
                foreach (var c in player) if (c != null) Physics.IgnoreCollision(_colliders[i], c);
                foreach (var c in vehicle) if (c != null) Physics.IgnoreCollision(_colliders[i], c);
            }
            foreach (var rb in _bodies) { rb.isKinematic = false; rb.velocity = Vector3.ClampMagnitude(velocity, 25f); }
            // A small upper-body impulse breaks the upright balance when killed at rest.
            if (_bodies.Count > 1) _bodies[1].AddForce(Vector3.back * 1.2f, ForceMode.VelocityChange);
        }

        private void Add(Dictionary<string, Transform> bones, string name, string end, int parent, float radius, float mass)
        {
            var bone = bones["mixamorig:" + name];
            var go = new GameObject(name);
            go.layer = 2; // not pickable, and never a camera obstruction
            go.transform.SetParent(_root.transform, false);
            go.transform.SetPositionAndRotation(bone.position, bone.rotation);
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.mass = mass; rb.drag = 0.1f; rb.angularDrag = 0.5f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.solverIterations = 12; rb.solverVelocityIterations = 4;
            Vector3 segment = end != null ? bones["mixamorig:" + end].position - bone.position : bone.up * 0.15f;
            var col = go.AddComponent<CapsuleCollider>();
            Vector3 local = go.transform.InverseTransformVector(segment);
            Vector3 abs = new Vector3(Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z));
            col.direction = abs.x > abs.y && abs.x > abs.z ? 0 : abs.z > abs.y ? 2 : 1;
            col.center = local * 0.5f;
            col.radius = radius;
            col.height = Mathf.Max(segment.magnitude + radius, radius * 2f);
            if (parent >= 0)
            {
                var joint = go.AddComponent<CharacterJoint>();
                joint.connectedBody = _bodies[parent];
                joint.anchor = Vector3.zero;
                joint.axis = Vector3.right;
                joint.swingAxis = Vector3.forward;
                joint.lowTwistLimit = new SoftJointLimit { limit = -35f };
                joint.highTwistLimit = new SoftJointLimit { limit = 35f };
                joint.swing1Limit = new SoftJointLimit { limit = name.Contains("Arm") ? 65f : 45f };
                joint.swing2Limit = new SoftJointLimit { limit = 35f };
                joint.enableProjection = true;
            }
            _bones.Add(bone); _bodies.Add(rb); _colliders.Add(col);
        }

        public void Apply()
        {
            // Parents first: their transform propagation must finish before placing children.
            for (int i = 0; i < _bones.Count; i++)
                _bones[i].SetPositionAndRotation(_bodies[i].position, _bodies[i].rotation);
        }

        public void Destroy() { if (_root != null) Object.Destroy(_root); }
    }
}
