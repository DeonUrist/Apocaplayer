using UnityEngine;
using UnityEngine.UI;

namespace FemalePlayer
{
    // The TAB screen (PlayerSheet: ammo, bosses killed ...) shows the player as a picture: RawImage PlayerSheet_Canvas/PlayerSheet/player/player_icon
    // with the texture player_character_2_UI (512 x 1024, the bearded man in front view, arms down, black background). While that screen is
    // open, a hidden camera films a second copy of her (her idle animation, front view, same framing, black background) into a 512 x 1024
    // RenderTexture that replaces the picture. Off (mod or ReplaceDriver off) = the game's picture back.
    internal static class InventoryModel
    {
        private const int Layer = 31;                      // a layer no game camera draws
        private static readonly Vector3 Spot = new Vector3(0f, -5000f, 0f);
        private static RawImage _icon;
        private static Texture _orig;
        private static RenderTexture _rt;
        private static Camera _cam;
        private static GameObject _rig;
        private static Body _her;
        private static float _nextFind;

        public static void LateTick()
        {
            if (!Plugin.Enabled.Value || !Plugin.ReplaceDriver.Value) { Off(); return; }
            if (_icon == null) { Find(); if (_icon == null) return; }
            bool open = _icon.isActiveAndEnabled;
            if (!open) { if (_cam != null) _cam.enabled = false; return; }
            if (!Build()) return;
            if (_icon.texture != _rt) { if (_icon.texture != null && _orig == null) _orig = _icon.texture; _icon.texture = _rt; }
            // frame her like the game's picture: whole body, centred, a little margin
            var b = _her.RenderBounds;
            float h = Mathf.Max(0.5f, b.size.y) * 1.06f;
            float dist = h * 0.5f / Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            var fwd = _her.Root.transform.forward;
            _cam.transform.position = b.center + fwd * dist;
            _cam.transform.rotation = Quaternion.LookRotation(-fwd, Vector3.up);
            _cam.enabled = true;
        }

        private static void Find()
        {
            if (Time.unscaledTime < _nextFind) return;
            _nextFind = Time.unscaledTime + 2f;
            foreach (var r in Resources.FindObjectsOfTypeAll<RawImage>())
                if (r != null && r.name == "player_icon" && r.gameObject.scene.IsValid() && r.transform.root.name == "PlayerSheet_Canvas")
                {
                    _icon = r;
                    Plugin.Verbose("TAB screen picture found (" + (r.texture != null ? r.texture.name : "no texture") + ")");
                    return;
                }
        }

        private static bool Build()
        {
            if (_her != null && _her.Alive && _cam != null) return true;
            if (_her == null || !_her.Alive)
            {
                _her = Body.Create();
                if (_her == null) return false;
                _her.Root.name = "FemalePlayerPortrait";
                Object.DontDestroyOnLoad(_her.Root);
                _her.Root.transform.SetPositionAndRotation(Spot, Quaternion.identity);
                _her.UsePortrait(Layer);
            }
            if (_rig == null)
            {
                _rig = new GameObject("FemalePlayerPortraitRig");
                Object.DontDestroyOnLoad(_rig);
                _rt = new RenderTexture(512, 1024, 24) { name = "FemalePlayer portrait", antiAliasing = 4 };
                var camGo = new GameObject("Camera"); camGo.transform.SetParent(_rig.transform, false);
                _cam = camGo.AddComponent<Camera>();
                _cam.cullingMask = 1 << Layer;
                _cam.clearFlags = CameraClearFlags.SolidColor;
                _cam.backgroundColor = Color.black;
                _cam.fieldOfView = 20f;
                _cam.nearClipPlane = 0.1f; _cam.farClipPlane = 50f;
                _cam.targetTexture = _rt;
                _cam.enabled = false;
                // her own lights (only her layer): key from the front-left above, a softer fill from the right
                AddLight("Key", Quaternion.Euler(25f, 160f, 0f), 1.1f);
                AddLight("Fill", Quaternion.Euler(10f, 215f, 0f), 0.45f);
            }
            return true;
        }

        private static void AddLight(string name, Quaternion rot, float intensity)
        {
            var go = new GameObject(name); go.transform.SetParent(_rig.transform, false);
            go.transform.rotation = rot;
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional; l.intensity = intensity; l.cullingMask = 1 << Layer; l.shadows = LightShadows.None;
        }

        public static void Off()
        {
            if (_icon != null && _orig != null && _icon.texture == _rt) _icon.texture = _orig;
            if (_cam != null) _cam.enabled = false;
        }

        public static void Reset()
        {
            Off();
            _icon = null; _orig = null; _nextFind = 0f;   // the canvas is rebuilt with the scene; her rig survives (DontDestroyOnLoad)
        }
    }
}
