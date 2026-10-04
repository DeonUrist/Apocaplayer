using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;

namespace FemalePlayer
{
    // [Gun position] settings: one line per weapon, "right, up, forward, pitch, yaw, roll" -
    // cm along her right / up / forward, then degrees: pitch + = muzzle up, yaw + = muzzle to her right, roll about the barrel.
    // Applied live every frame in third person (change it in the Apocasetter menu and watch her).
    internal static class GunPose
    {
        public static readonly string[] Weapons =
        {
            "akm_trash", "akms", "akm_drum", "m16a1", "redmark_m11", "redmark_m11_scoped", "borz_smg", "22_pipe_smg",
            "slamfire_shotgun", "rochester_m24", "rochester_m24_chopped", "slamberg_500", "slamberg_500_chopped", "crossbow",
            "22_pipe_pistol", "22_pipe_revolver", "folk_17", "shiv", "old_knife", "machete", "pipe_wrench",
        };
        private static readonly Dictionary<string, ConfigEntry<string>> _cfg = new Dictionary<string, ConfigEntry<string>>();
        private static readonly Dictionary<string, KeyValuePair<string, float[]>> _parsed = new Dictionary<string, KeyValuePair<string, float[]>>();

        public static void Bind(ConfigFile config)
        {
            foreach (var w in Weapons)
                _cfg[w] = config.Bind("Gun position", w, "0, 0, 0, 0, 0, 0",
                    "Third person: how " + w + " sits in her hand, on top of the automatic placement: right, up, forward (cm), pitch, yaw, roll (degrees; pitch + = muzzle up, yaw + = muzzle to her right). Applied live.");
        }

        public static float[] For(string weapon)
        {
            ConfigEntry<string> e;
            if (string.IsNullOrEmpty(weapon) || !_cfg.TryGetValue(weapon, out e)) return null;
            string s = e.Value ?? "";
            KeyValuePair<string, float[]> p;
            if (_parsed.TryGetValue(weapon, out p) && p.Key == s) return p.Value;
            var v = new float[6];
            var parts = s.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            bool any = false;
            for (int i = 0; i < 6 && i < parts.Length; i++)
            {
                float f;
                if (float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out f)) { v[i] = f; if (f != 0f) any = true; }
            }
            var r = any ? v : null;
            _parsed[weapon] = new KeyValuePair<string, float[]>(s, r);
            return r;
        }
    }
}
