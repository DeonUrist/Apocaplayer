using System;
using System.IO;
using UnityEngine;

namespace Apocaplayer
{
    internal static class VehicleHud
    {
        private static Texture2D _ignition, _brake, _cassette;
        private static bool _loaded;

        private static Texture2D Load(string file)
        {
            try
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (texture.LoadImage(File.ReadAllBytes(Plugin.ModPath("Models/Hud/" + file)))) return texture;
                UnityEngine.Object.Destroy(texture);
            }
            catch (Exception e) { Plugin.Warn("Vehicle HUD icon: " + e.Message); }
            return null;
        }

        public static void Draw(bool ignitionOff, bool handbrakeOn, bool playing, string volume)
        {
            if (!ignitionOff && !handbrakeOn && !playing) return;
            if (!_loaded)
            {
                _loaded = true;
                _ignition = Load("ignition.png"); _brake = Load("handbrake.png"); _cassette = Load("cassette.png");
            }
            float scale = Mathf.Clamp(Screen.height / 1080f, 0.65f, 2f);
            float width = 48f * scale, gap = 8f * scale, y = 24f * scale;
            float x = Screen.width - 24f * scale;
            var previous = GUI.color;
            var label = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(21f * scale), alignment = TextAnchor.MiddleCenter };
            label.normal.textColor = new Color(0.85f, 0.9f, 0.81f);
            // Build from the right so hidden conditions leave no gaps.
            if (playing)
            {
                x -= width * 1.8f;
                Background(new Rect(x, y, width * 1.8f, width));
                Icon(new Rect(x, y, width, width), _cassette, "Tape", label);
                GUI.color = Color.white;
                GUI.Label(new Rect(x + width, y, width * 0.8f, width), volume, label);
                x -= gap;
            }
            if (handbrakeOn)
            {
                x -= width;
                Background(new Rect(x, y, width, width));
                Icon(new Rect(x, y, width, width), _brake, "(P)", label);
                x -= gap;
            }
            if (ignitionOff)
            {
                x -= width;
                Background(new Rect(x, y, width, width));
                Icon(new Rect(x, y, width, width), _ignition, "Key", label);
            }
            GUI.color = previous;
        }

        private static void Background(Rect rect)
        {
            GUI.color = new Color(0.04f, 0.06f, 0.06f, 0.72f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
        }
        private static void Icon(Rect rect, Texture2D icon, string fallback, GUIStyle style)
        {
            GUI.color = Color.white;
            if (icon != null) GUI.DrawTexture(rect, icon, ScaleMode.ScaleToFit, true);
            else GUI.Label(rect, fallback, style);
        }
    }
}
