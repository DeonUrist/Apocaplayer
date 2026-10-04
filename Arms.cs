using System.Collections.Generic;
using UnityEngine;

namespace FemalePlayer
{
    // The game's first-person arms (player_arm_left / player_arm_left.002, 8 bones each, one pair per weapon, tool and item animation)
    // and the kick leg (Player_leg) all use the material "Player2" - the same texture atlas as the seated driver. We give those
    // renderers a copy of the material with our atlas (Player2_female_arms.png: the arm/leg islands repainted from her texture, the
    // rest unchanged), so every vanilla animation - reloads, melee, items - keeps working, just with her arms and gloves.
    internal static class Arms
    {
        private static Material _mat;
        private static readonly Dictionary<Renderer, Material> _orig = new Dictionary<Renderer, Material>();
        private static float _next;

        public static void Tick()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 3f;
            if (Game.CameraHolder == null) return;
            var tex = Model.Arms();
            if (tex == null) return;
            int added = 0;
            foreach (var smr in Game.CameraHolder.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr == null || _orig.ContainsKey(smr)) continue;
                var m = smr.sharedMaterial;
                if (m == null || !m.name.StartsWith("Player2")) continue;
                if (_mat == null)
                {
                    _mat = new Material(m) { name = "Player2 (FemalePlayer)", mainTexture = tex };
                    _mat.hideFlags = HideFlags.DontUnloadUnusedAsset;
                }
                _orig[smr] = m;
                smr.sharedMaterial = _mat;
                added++;
            }
            if (added > 0) Plugin.Verbose("Arms: " + added + " first-person arm/leg renderer(s) dressed");
        }

        public static void Restore()
        {
            foreach (var kv in _orig) if (kv.Key != null) kv.Key.sharedMaterial = kv.Value;
            _orig.Clear();
        }

        public static void OnSceneLoaded() { _orig.Clear(); _next = 0f; }
    }
}
