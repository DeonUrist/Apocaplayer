using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace FemalePlayer
{
    // The TAB screen (PlayerSheet: ammo, bosses killed ...) shows the player as a picture: the game's texture player_character_2_UI (512 x 1024,
    // the bearded man, front view, arms down, black background), on RawImage PlayerSheet_Canvas/PlayerSheet/player/player_icon.
    // Models/player_character_2_UI.png (same name; Denis's picture of her - the game's own is in the repo, tools/ref) is shown instead.
    // Every UI element showing that texture is switched - any RawImage with it (or named player_icon) and any Image whose sprite uses it -
    // checked once a second, so a copy the game makes later or a texture it puts back is caught too.
    // No file = the game's picture. Off (mod or ReplaceDriver off) = the game's picture back.
    internal static class InventoryModel
    {
        private const string GameTexture = "player_character_2_UI";
        private static readonly Dictionary<RawImage, Texture> _raw = new Dictionary<RawImage, Texture>();
        private static readonly Dictionary<Image, Sprite> _img = new Dictionary<Image, Sprite>();
        private static Texture2D _mine;
        private static Sprite _mineSprite;
        private static bool _tried;
        private static float _nextScan;

        public static void LateTick()
        {
            if (!Plugin.Enabled.Value || !Plugin.ReplaceDriver.Value) { Off(); return; }
            var tex = Picture();
            if (tex == null) return;
            // the known ones every frame (the game may set its texture back), a full search once a second
            foreach (var kv in _raw) if (kv.Key != null && kv.Key.texture != tex) kv.Key.texture = tex;
            foreach (var kv in _img) if (kv.Key != null && kv.Key.overrideSprite != _mineSprite) kv.Key.overrideSprite = _mineSprite;
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + 1f;
            Scan(tex);
        }

        private static void Scan(Texture2D tex)
        {
            try
            {
                foreach (var r in Resources.FindObjectsOfTypeAll<RawImage>())
                {
                    if (r == null || !r.gameObject.scene.IsValid() || _raw.ContainsKey(r)) continue;
                    var t = r.texture;
                    if ((t != null && t.name == GameTexture) || r.name == "player_icon")
                    {
                        _raw[r] = t;
                        r.texture = tex;
                        Plugin.Log.LogInfo("TAB screen picture: replaced on " + Path(r.transform) + " (was " + (t != null ? t.name : "none") + ")");
                    }
                }
                foreach (var im in Resources.FindObjectsOfTypeAll<Image>())
                {
                    if (im == null || !im.gameObject.scene.IsValid() || _img.ContainsKey(im)) continue;
                    var s = im.sprite;
                    if (s == null || s.texture == null || s.texture.name != GameTexture) continue;
                    if (_mineSprite == null) _mineSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                    _img[im] = im.overrideSprite;
                    im.overrideSprite = _mineSprite;
                    Plugin.Log.LogInfo("TAB screen picture: replaced on " + Path(im.transform) + " (sprite " + s.name + ")");
                }
            }
            catch (Exception e) { Plugin.Warn("TAB screen picture: " + e.Message); }
        }

        private static string Path(Transform t)
        {
            string p = t.name;
            for (var x = t.parent; x != null; x = x.parent) p = x.name + "/" + p;
            return p;
        }

        private static Texture2D Picture()
        {
            if (_mine != null || _tried) return _mine;
            _tried = true;
            string path = Plugin.ModPath("Models/player_character_2_UI.png");
            if (!File.Exists(path)) { Plugin.Log.LogInfo("TAB screen: no " + path + " - the game's picture stays"); return null; }
            try
            {
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "FemalePlayer TAB picture" };
                if (!ImageConversion.LoadImage(t, File.ReadAllBytes(path), false)) throw new InvalidDataException("not a PNG/JPG");
                t.wrapMode = TextureWrapMode.Clamp;
                t.filterMode = FilterMode.Trilinear;
                t.hideFlags = HideFlags.DontUnloadUnusedAsset;
                _mine = t;
                Plugin.Log.LogInfo("TAB screen picture: " + System.IO.Path.GetFileName(path) + " (" + t.width + "x" + t.height + ")");
            }
            catch (Exception e) { Plugin.Warn("TAB screen picture " + path + ": " + e.Message); }
            return _mine;
        }

        public static void Off()
        {
            foreach (var kv in _raw) if (kv.Key != null) kv.Key.texture = kv.Value;
            foreach (var kv in _img) if (kv.Key != null) kv.Key.overrideSprite = kv.Value;
            _raw.Clear(); _img.Clear(); _nextScan = 0f;
        }

        public static void Reset()
        {
            Off();
        }
    }
}
