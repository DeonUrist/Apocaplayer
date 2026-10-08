using System.Collections.Generic;
using UnityEngine;


namespace Apocaplayer
{
    // (2.2.9) no camera culling inside caves: there the cut-away views lose the rock's shadows and light leaks in as bright patches.
    // The game's caves are the Cave_1..7 structures (Cave_N(Clone) under Objects, also inside some camps) with a cave_N MeshCollider shell,
    // and level1's "Caves" root ("cave 1".."cave 7"). Four times a second a ray goes straight up from her head (back faces count: the shell is
    // seen from inside); it hits a collider of an object named cave... (itself or up to 5 parents) = she is in a cave. Leaving takes two misses
    // in a row (half a second), so the camera doesn't switch back and forth under a gap in the roof.
    internal static class Cave
    {
        public static bool Inside;
        private static float _next;
        private static int _misses;
        private static readonly RaycastHit[] _hits = new RaycastHit[16];
        private static readonly Dictionary<int, bool> _isCave = new Dictionary<int, bool>();

        public static void Tick(bool want)
        {
            if (!want) { Inside = false; _misses = 0; return; }
            float now = Time.unscaledTime;
            if (now < _next) return;
            _next = now + 0.25f;
            bool hit = Probe();
            if (hit) { _misses = 0; if (!Inside) { Inside = true; Plugin.Verbose("Camera culling: off in the cave"); } }
            else if (Inside && ++_misses >= 2) { Inside = false; _misses = 0; Plugin.Verbose("Camera culling: out of the cave, back on"); }
        }

        private static bool Probe()
        {
            if (Game.Player == null) return false;
            Vector3 head;
            Vector3 feet;
            if (!Runner.TryOcclusionPoints(out head, out feet)) head = Game.Player.transform.position + Vector3.up * 0.8f;
            bool back = Physics.queriesHitBackfaces;
            int n;
            try { Physics.queriesHitBackfaces = true; n = Physics.RaycastNonAlloc(head, Vector3.up, _hits, 60f, ~0, QueryTriggerInteraction.Ignore); }
            finally { Physics.queriesHitBackfaces = back; }
            for (int i = 0; i < n; i++)
            {
                var c = _hits[i].collider;
                if (c != null && IsCave(c.transform)) return true;
            }
            return false;
        }

        private static bool IsCave(Transform t)
        {
            int id = t.GetInstanceID();
            bool r;
            if (_isCave.TryGetValue(id, out r)) return r;
            r = false;
            for (int d = 0; t != null && d < 6; d++, t = t.parent)
                if (t.name.StartsWith("cave", System.StringComparison.OrdinalIgnoreCase)) { r = true; break; }
            if (_isCave.Count > 4096) _isCave.Clear();
            _isCave[id] = r;
            return r;
        }

        public static void Reset() { Inside = false; _misses = 0; _isCave.Clear(); }
    }
}
