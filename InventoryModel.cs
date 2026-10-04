using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace FemalePlayer
{
    // The TAB screen (PlayerSheet: ammo, bosses killed ...) shows the player as a picture: RawImage PlayerSheet_Canvas/PlayerSheet/player/player_icon
    // with the game's texture player_character_2_UI (512 x 1024, the bearded man, front view, arms down, black background).
    // Models/player_character_2_UI.original.png is that picture, dumped from the game. Put your own picture in Models/TabPicture.png (any size;
    // 512 x 1024 with a black background matches the screen) and it is shown instead. No file = the game's picture. Off (mod or ReplaceDriver
    // off) = the game's picture back.
    internal static class InventoryModel
    {
        private static RawImage _icon;
        private static Texture _orig;
        private static Texture2D _mine;
        private static bool _tried;
        private static float _nextFind;

        public static void LateTick()
        {
            if (!Plugin.Enabled.Value || !Plugin.ReplaceDriver.Value) { Off(); return; }
            if (_icon == null) { Find(); if (_icon == null) return; }
            if (!_icon.isActiveAndEnabled) return;
            var tex = Picture();
            if (tex == null) return;
            if (_icon.texture != tex) { if (_orig == null) _orig = _icon.texture; _icon.texture = tex; }
        }

        private static Texture2D Picture()
        {
            if (_mine != null || _tried) return _mine;
            _tried = true;
            string path = Plugin.ModPath(Plugin.TabPictureFile.Value);
            if (!File.Exists(path)) { Plugin.Verbose("TAB screen: no " + path + " - the game's picture stays"); return null; }
            try
            {
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "FemalePlayer TAB picture" };
                if (!ImageConversion.LoadImage(t, File.ReadAllBytes(path), true)) throw new InvalidDataException("not a PNG/JPG");
                t.wrapMode = TextureWrapMode.Clamp;
                t.filterMode = FilterMode.Trilinear;
                t.hideFlags = HideFlags.DontUnloadUnusedAsset;
                _mine = t;
                Plugin.Log.LogInfo("TAB screen picture: " + Path.GetFileName(path) + " (" + t.width + "x" + t.height + ")");
            }
            catch (Exception e) { Plugin.Warn("TAB screen picture " + path + ": " + e.Message); }
            return _mine;
        }

        private static void Find()
        {
            if (Time.unscaledTime < _nextFind) return;
            _nextFind = Time.unscaledTime + 2f;
            foreach (var r in Resources.FindObjectsOfTypeAll<RawImage>())
                if (r != null && r.name == "player_icon" && r.gameObject.scene.IsValid() && r.transform.root.name == "PlayerSheet_Canvas") { _icon = r; return; }
        }

        public static void Off()
        {
            if (_icon != null && _orig != null && _icon.texture != _orig) _icon.texture = _orig;
        }

        public static void Reset()
        {
            Off();
            _icon = null; _orig = null; _nextFind = 0f;
        }
    }
}
