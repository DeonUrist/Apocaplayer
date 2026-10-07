using System.Collections.Generic;
using UnityEngine;

namespace Apocaplayer
{
    // The game's first-person arms (player_arm_left / player_arm_left.002, 8 bones each, one pair per weapon, tool and item animation)
    // and the kick leg (Player_leg) all use the material "Player2" - the same texture atlas as the seated driver. We give those
    // renderers a copy of the material with our atlas (the arm/leg islands repainted from the character's texture, the rest unchanged),
    // so every vanilla animation - reloads, melee, items - keeps working, just with her (or Max's) arms.
    //  - Female: Player2_female_arms.png on both arms and the leg (her arms are alike).
    //  - Max: both arms share one atlas island, but his arms differ (left: armoured sleeve to the glove, right: bare forearm), so the
    //    left arm mesh gets Player2_max_arms_left.png and the right arm + kick leg Player2_max_arms.png (tools/max_arms/bake_max2.py).
    internal static class Arms
    {
        private static Material _mat, _matLeft;
        private static readonly Dictionary<Renderer, Material> _orig = new Dictionary<Renderer, Material>();
        private static float _next;

        public static void Tick()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 3f;
            if (Game.CameraHolder == null) return;
            var tex = Model.Arms();
            if (tex == null) return;
            var texLeft = Model.ArmsLeft();
            int added = 0;
            foreach (var smr in Game.CameraHolder.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr == null || _orig.ContainsKey(smr)) continue;
                var m = smr.sharedMaterial;
                if (m == null || !m.name.StartsWith("Player2")) continue;
                if (_mat == null)
                {
                    _mat = new Material(m) { name = "Player2 (Apocaplayer)", mainTexture = tex };
                    _mat.hideFlags = HideFlags.DontUnloadUnusedAsset;
                }
                if (texLeft != null && _matLeft == null)
                {
                    _matLeft = new Material(m) { name = "Player2 (Apocaplayer, left arm)", mainTexture = texLeft };
                    _matLeft.hideFlags = HideFlags.DontUnloadUnusedAsset;
                }
                _orig[smr] = m;
                smr.sharedMaterial = texLeft != null && IsLeftArm(smr) ? _matLeft : _mat;
                added++;
            }
            if (added > 0) Plugin.Verbose("Arms: " + added + " first-person arm/leg renderer(s) dressed");
        }

        // player_arm_left = the left arm, player_arm_left.002 = the right one (mesh names; the renderer names vary per weapon)
        private static bool IsLeftArm(SkinnedMeshRenderer smr)
        {
            var mesh = smr.sharedMesh;
            string n = mesh != null ? mesh.name : smr.name;
            n = n.ToLowerInvariant();
            return n.Contains("arm") && !n.Contains("002");
        }

        public static void Restore()
        {
            foreach (var kv in _orig) if (kv.Key != null) kv.Key.sharedMaterial = kv.Value;
            _orig.Clear();
            _mat = _matLeft = null;   // the next character's textures
            _next = 0f;
        }

        public static void OnSceneLoaded() { _orig.Clear(); _next = 0f; }
    }
}
