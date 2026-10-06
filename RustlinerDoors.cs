using System;
using System.Collections.Generic;
using UnityEngine;

namespace Apocaplayer
{
    // Doorway-only player clearance. Stock upper-frame boxes are retained for
    // bullets, other objects and the rest of the bus; only player capsule pairs
    // are bypassed while the player is crossing an entrance.
    internal static class RustlinerDoors
    {
        private sealed class Bus
        {
            public Transform Root;
            public readonly List<BoxCollider> Side = new List<BoxCollider>();
            public readonly List<BoxCollider> Rear = new List<BoxCollider>();
        }
        private sealed class Pair
        {
            public Collider Player, Frame;
            public bool Original, Wanted;
        }
        private static readonly List<Bus> _buses = new List<Bus>();
        private static readonly List<Pair> _pairs = new List<Pair>();
        private static float _scanAt;
        private static readonly Vector3[] SideFrames = {
            new Vector3(1.233f, 1.628f, 1.844f), // upper side-door lip
            new Vector3(1.278f, 1.515f, -1.366f), // side rail ending at doorway
            new Vector3(.836f, 1.780f, -.998f) // roof shoulder along side-door edge
        };
        private static readonly Vector3 RearFrame = new Vector3(.014f, 1.710f, -4.232f);

        public static void Tick()
        {
            if (!Plugin.Enabled.Value || !Game.Ready || Game.Dead || Game.InCar) { Restore(); return; }
            if (Time.unscaledTime >= _scanAt)
            {
                _scanAt = Time.unscaledTime + 1f;
                foreach (var body in UnityEngine.Object.FindObjectsOfType<Rigidbody>())
                    if (body != null && IsRustliner(body.transform)) Track(body.transform);
                for (int i = _buses.Count - 1; i >= 0; i--) if (_buses[i].Root == null) _buses.RemoveAt(i);
            }
            Begin();
            foreach (var bus in _buses) Apply(bus, Game.Player);
            Finish();
        }

        private static bool IsRustliner(Transform root)
        { return root != null && root.name.StartsWith("Rustliner", StringComparison.OrdinalIgnoreCase) && root.Find("DriveTrigger") != null; }

        private static Bus Track(Transform root)
        {
            foreach (var known in _buses) if (known.Root == root) return known;
            if (!IsRustliner(root)) return null;
            var bus = new Bus { Root = root };
            foreach (var collider in root.GetComponentsInChildren<BoxCollider>(true))
            {
                var parent = collider.transform.parent;
                // Fitted/moving door panels and unrelated body boxes are excluded.
                if (parent == null || parent.name != "colliders" || parent.parent == null || parent.parent.name != "Rustliner"
                    || parent.parent.parent == null || parent.parent.parent.name != "parts" || collider.isTrigger) continue;
                Vector3 position = root.InverseTransformPoint(collider.transform.position);
                foreach (var expected in SideFrames)
                    if ((position - expected).sqrMagnitude < .025f * .025f) { bus.Side.Add(collider); break; }
                if ((position - RearFrame).sqrMagnitude < .025f * .025f) bus.Rear.Add(collider);
            }
            _buses.Add(bus);
            Plugin.Verbose("Rustliner door clearance: " + bus.Side.Count + " side frame(s), " + bus.Rear.Count + " rear frame(s)");
            return bus;
        }

        private static void Begin() { foreach (var pair in _pairs) pair.Wanted = false; }

        private static void Apply(Bus bus, GameObject player)
        {
            if (bus == null || bus.Root == null || player == null || !bus.Root.gameObject.activeInHierarchy) return;
            Vector3 point = bus.Root.InverseTransformPoint(player.transform.position);
            if (point.y < -.4f || point.y > 1.5f) return;
            bool side = point.x >= .75f && point.x <= 1.95f && point.z >= 1.52f && point.z <= 2.19f;
            bool rear = Mathf.Abs(point.x) <= .40f && point.z >= -4.75f && point.z <= -3.75f;
            if (!side && !rear) return;
            foreach (var capsule in player.GetComponents<CapsuleCollider>())
            {
                if (!capsule.enabled || capsule.isTrigger) continue;
                if (side) foreach (var frame in bus.Side) Ignore(capsule, frame);
                if (rear) foreach (var frame in bus.Rear) Ignore(capsule, frame);
            }
        }

        private static void Ignore(Collider player, Collider frame)
        {
            if (frame == null || !frame.enabled || !frame.gameObject.activeInHierarchy) return;
            foreach (var pair in _pairs)
                if (pair.Player == player && pair.Frame == frame)
                { pair.Wanted = true; Physics.IgnoreCollision(player, frame, true); return; }
            var added = new Pair { Player = player, Frame = frame, Original = Physics.GetIgnoreCollision(player, frame), Wanted = true };
            _pairs.Add(added); Physics.IgnoreCollision(player, frame, true);
        }

        private static void Finish()
        {
            for (int i = _pairs.Count - 1; i >= 0; i--)
            {
                var pair = _pairs[i];
                if (pair.Wanted && pair.Player != null && pair.Frame != null) continue;
                if (pair.Player != null && pair.Frame != null) Physics.IgnoreCollision(pair.Player, pair.Frame, pair.Original);
                _pairs.RemoveAt(i);
            }
        }

        // Also exercises the real implementation against isolated vanilla assets.
        internal static void ApplyForPlayer(Transform bus, GameObject player)
        { Begin(); Apply(Track(bus), player); Finish(); }

        public static void Restore() { Begin(); Finish(); }
        public static void Reset() { Restore(); _buses.Clear(); _scanAt = 0; }
    }
}
