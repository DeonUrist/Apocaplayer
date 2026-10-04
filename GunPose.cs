using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using UnityEngine;

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

        // ---- live adjustment with the numpad (setting WeaponAdjustment): 7/1 forward/back, 8/2 up/down, 6/4 right/left;
        // with Shift: 7/1 roll, 8/2 pitch, 6/4 yaw. Saved to the weapon's line when the keys are released.
        private static string _editing;
        private static float[] _edit;
        private static string _hint = "";

        public static void Tick(string weapon)
        {
            _hint = "";
            if (Plugin.WeaponAdjust == null || !Plugin.WeaponAdjust.Value || string.IsNullOrEmpty(weapon) || !_cfg.ContainsKey(weapon) || Time.timeScale < 0.01f)
            { Flush(); return; }
            if (_editing != weapon) { Flush(); _editing = weapon; var cur = For(weapon); _edit = cur != null ? (float[])cur.Clone() : new float[6]; }
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            float dt = Time.unscaledDeltaTime;
            float a = Axis(KeyCode.Keypad7, KeyCode.Keypad1), b = Axis(KeyCode.Keypad8, KeyCode.Keypad2), c = Axis(KeyCode.Keypad6, KeyCode.Keypad4);
            bool any = a != 0f || b != 0f || c != 0f;
            if (any)
            {
                if (shift) { _edit[5] += a * 30f * dt; _edit[3] += b * 30f * dt; _edit[4] += c * 30f * dt; }
                else { _edit[2] += a * 3f * dt; _edit[1] += b * 3f * dt; _edit[0] += c * 3f * dt; }
                _live = _edit;
            }
            else { Flush(false); var cur = For(weapon); _edit = cur != null ? (float[])cur.Clone() : new float[6]; }   // follow edits made in the menu
            _hint = "Adjusting " + weapon + ":  right " + _edit[0].ToString("0.0") + "  up " + _edit[1].ToString("0.0") + "  forward " + _edit[2].ToString("0.0") + " cm   pitch "
                    + _edit[3].ToString("0") + "  yaw " + _edit[4].ToString("0") + "  roll " + _edit[5].ToString("0") + " deg\n"
                    + "Numpad 7/1 forward-back, 8/2 up-down, 6/4 right-left;  hold Shift: 7/1 roll, 8/2 pitch, 6/4 yaw.  (Turn WeaponAdjustment off when done.)";
        }

        private static float[] _live;
        private static float Axis(KeyCode plus, KeyCode minus) { return (Input.GetKey(plus) ? 1f : 0f) - (Input.GetKey(minus) ? 1f : 0f); }

        // writes the edited values into the config line (once, when the keys are let go / the weapon changes)
        private static void Flush(bool forget = true)
        {
            if (_live != null && _editing != null)
            {
                ConfigEntry<string> e;
                if (_cfg.TryGetValue(_editing, out e))
                {
                    var s = string.Join(", ", Array.ConvertAll(_live, x => x.ToString("0.##", CultureInfo.InvariantCulture)));
                    e.Value = s;
                }
                _live = null;
            }
            if (forget) { _editing = null; _edit = null; }
        }

        public static void OnGUI()
        {
            if (string.IsNullOrEmpty(_hint)) return;
            var r = new Rect(20, Screen.height - 90, Screen.width - 40, 60);
            var st = new GUIStyle(GUI.skin.label) { fontSize = 16 };
            st.normal.textColor = Color.black; GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), _hint, st);
            st.normal.textColor = Color.yellow; GUI.Label(r, _hint, st);
        }

        public static float[] For(string weapon)
        {
            ConfigEntry<string> e;
            if (string.IsNullOrEmpty(weapon) || !_cfg.TryGetValue(weapon, out e)) return null;
            if (_live != null && weapon == _editing) return _live;   // being adjusted right now
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
