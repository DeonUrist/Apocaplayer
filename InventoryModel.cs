using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace FemalePlayer
{
    // The TAB screen (PlayerSheet: ammo, bosses killed ...) shows the player as the game's texture player_character_2_UI (512 x 1024, the bearded
    // man, front view, black background; the game's own is in the repo, tools/ref). Swapping the texture on its UI elements didn't change what
    // is seen (0.10/0.11: player_icon and Tutorial_Vid_2 were switched, the man stayed), so the texture ITSELF is overwritten: her picture
    // (Models/player_character_2_UI.png) is copied into the game's texture on the GPU, and whatever draws it - RawImage, Image, material -
    // shows her. Done once per scene load (and when Enabled changes); nothing per frame. The man is kept in a backup texture and copied
    // back when the mod is turned off.
    //  - same size, format (DXT1 = her PNG as RGB24 compressed; DXT5 = RGBA32 compressed; uncompressed = loaded as that format) and mip
    //    levels -> Graphics.CopyTexture level by level
    //  - else, if the game's texture is readable: her pixels loaded straight into it
    //  - else, last resort: her texture put on the UI elements that show it, once
    internal static class InventoryModel
    {
        private const string GameTexture = "player_character_2_UI";
        private static Texture2D _game, _backup, _mine;
        private static bool _applied, _loadFailed, _hooked;
        private static float _nextTry;
        private static int _tries;

        // Runner calls this every frame, but it only works until the swap is done for the loaded scene (a cheap bool check after that)
        public static void LateTick()
        {
            if (!_hooked) { _hooked = true; Plugin.Enabled.SettingChanged += (s, e) => { if (Plugin.Enabled.Value) { _applied = false; _tries = 0; } else Off(); }; }
            if (_applied || !Plugin.Enabled.Value || !Plugin.ReplaceDriver.Value || _loadFailed) return;
            if (Time.unscaledTime < _nextTry || _tries >= 15) return;   // the texture may load a moment after the scene: retry for ~30 s
            _nextTry = Time.unscaledTime + 2f; _tries++;
            Apply();
        }

        private static void Apply()
        {
            if (_game == null)
                foreach (var t in Resources.FindObjectsOfTypeAll<Texture2D>())
                    if (t != null && t.name == GameTexture) { _game = t; break; }
            if (_game == null) return;
            var mine = Mine(_game);
            if (mine == null) return;
            try
            {
                if (_backup == null) _backup = Copy(_game);   // the man, for Off()
                if (CopyInto(mine, _game)) { _applied = true; Plugin.Log.LogInfo("TAB screen picture: the game's " + GameTexture + " (" + _game.width + "x" + _game.height + " " + _game.format + ", " + _game.mipmapCount + " mips) now shows her"); return; }
                if (_game.isReadable && ImageConversion.LoadImage(_game, File.ReadAllBytes(Plugin.ModPath("Models/player_character_2_UI.png")), false))
                { _applied = true; Plugin.Log.LogInfo("TAB screen picture: her pixels loaded into the game's " + GameTexture); return; }
            }
            catch (Exception e) { Plugin.Warn("TAB screen picture: overwriting the texture failed (" + e.Message + "), switching the UI elements instead"); }
            // last resort, once: the UI elements that show it
            int n = 0;
            foreach (var r in Resources.FindObjectsOfTypeAll<RawImage>())
                if (r != null && r.gameObject.scene.IsValid() && r.texture == _game) { r.texture = mine; n++; }
            _applied = true;
            Plugin.Log.LogInfo("TAB screen picture: " + n + " UI element(s) switched to her picture (the game's texture can't be overwritten: " + _game.format + ")");
        }

        // her picture in the game texture's size / format / mip count
        private static Texture2D Mine(Texture2D game)
        {
            if (_mine != null || _loadFailed) return _mine;
            string path = Plugin.ModPath("Models/player_character_2_UI.png");
            if (!File.Exists(path)) { _loadFailed = true; Plugin.Log.LogInfo("TAB screen: no " + path + " - the game's picture stays"); return null; }
            try
            {
                var f = game.format;
                bool compress = f == TextureFormat.DXT1 || f == TextureFormat.DXT5;
                var load = f == TextureFormat.DXT1 || f == TextureFormat.RGB24 ? TextureFormat.RGB24 : compress ? TextureFormat.RGBA32 : f;
                var t = new Texture2D(2, 2, load, game.mipmapCount > 1) { name = "FemalePlayer TAB picture" };
                if (!ImageConversion.LoadImage(t, File.ReadAllBytes(path), false)) throw new InvalidDataException("not a PNG/JPG");
                if (t.width != game.width || t.height != game.height)
                {   // scale to the game's size (bilinear, on the CPU)
                    var s = new Texture2D(game.width, game.height, load, game.mipmapCount > 1);
                    var px = new Color32[game.width * game.height];
                    for (int y = 0; y < game.height; y++)
                        for (int x = 0; x < game.width; x++)
                            px[y * game.width + x] = t.GetPixelBilinear((x + 0.5f) / game.width, (y + 0.5f) / game.height);
                    s.SetPixels32(px);
                    UnityEngine.Object.Destroy(t);
                    t = s;
                }
                t.Apply(true, false);
                if (compress) t.Compress(true);
                t.Apply(false, true);   // upload, drop the CPU copy
                t.wrapMode = game.wrapMode; t.filterMode = game.filterMode;
                t.hideFlags = HideFlags.DontUnloadUnusedAsset;
                _mine = t;
                Plugin.Verbose("TAB screen picture: her picture " + t.width + "x" + t.height + " " + t.format + ", " + t.mipmapCount + " mips");
            }
            catch (Exception e) { _loadFailed = true; Plugin.Warn("TAB screen picture " + path + ": " + e.Message); }
            return _mine;
        }

        private static Texture2D Copy(Texture2D src)
        {
            var b = new Texture2D(src.width, src.height, src.format, src.mipmapCount > 1) { name = GameTexture + " (FemalePlayer backup)", hideFlags = HideFlags.DontUnloadUnusedAsset };
            if (!CopyInto(src, b)) { UnityEngine.Object.Destroy(b); return null; }
            return b;
        }

        // level by level, as many levels as both have (same size and format needed)
        private static bool CopyInto(Texture2D src, Texture2D dst)
        {
            if (src == null || dst == null || src.width != dst.width || src.height != dst.height || src.format != dst.format) return false;
            if (SystemInfo.copyTextureSupport == UnityEngine.Rendering.CopyTextureSupport.None) return false;
            int mips = Mathf.Min(src.mipmapCount, dst.mipmapCount);
            for (int m = 0; m < mips; m++) Graphics.CopyTexture(src, 0, m, dst, 0, m);
            return true;
        }

        public static void Off()
        {
            if (_applied && _game != null && _backup != null) { CopyInto(_backup, _game); Plugin.Verbose("TAB screen picture: the game's picture back"); }
            _applied = false; _tries = 0;
        }

        // new scene: the texture may be a new instance - find it again and overwrite it once more
        public static void Reset()
        {
            if (_game == null) { _backup = null; }
            _applied = false; _tries = 0; _nextTry = 0f;
        }
    }
}
