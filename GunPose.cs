using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using UnityEngine;

namespace FemalePlayer
{
    // [Weapon grip] settings: one line per weapon = how it sits in her RIGHT HAND, in the hand's own axes:
    // "x, y, z (cm), rotX, rotY, rotZ (degrees)" added to the raider's grip. Because it is relative to the hand bone, the weapon stays
    // glued to the hand in every animation (idle, walk, run, crouch, fire, reload) - it is set once, not per pose.
    // Easiest to set with WeaponAdjustment (numpad, see Keys()); the numbers are written here when the keys are released.
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
                _cfg[w] = config.Bind("Weapon grip", w, "0, 0, 0, 0, 0, 0",
                    "Third person: " + w + " in her right hand, relative to the hand (stays the same in every animation): x, y, z in cm, then rotation x, y, z in degrees. Set it with WeaponAdjustment and the numpad.");
        }

        // ---------------- values
        private static string _liveFor;
        private static float[] _live;      // being edited (not yet written to the config)

        public static float[] For(string weapon)
        {
            ConfigEntry<string> e;
            if (string.IsNullOrEmpty(weapon) || !_cfg.TryGetValue(weapon, out e)) return null;
            if (_live != null && weapon == _liveFor) return _live;
            string s = e.Value ?? "";
            KeyValuePair<string, float[]> p;
            if (_parsed.TryGetValue(weapon, out p) && p.Key == s) return p.Value;
            var v = new float[6];
            var parts = s.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < 6 && i < parts.Length; i++)
            {
                float f;
                if (float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out f)) v[i] = f;
            }
            _parsed[weapon] = new KeyValuePair<string, float[]>(s, v);
            return v;
        }

        public static bool Known(string weapon) { return weapon != null && _cfg.ContainsKey(weapon); }

        public static void SetLive(string weapon, float[] v) { if (_liveFor != weapon) Flush(); _liveFor = weapon; _live = v; }

        // writes the edited values into the weapon's line (once, when the keys are let go / the weapon changes)
        public static void Flush()
        {
            if (_live != null && _liveFor != null)
            {
                ConfigEntry<string> e;
                if (_cfg.TryGetValue(_liveFor, out e))
                    e.Value = string.Join(", ", Array.ConvertAll(_live, x => x.ToString("0.##", CultureInfo.InvariantCulture)));
            }
            _live = null; _liveFor = null;
        }

        // ---------------- keys (WeaponAdjustment)
        // Numpad 7/1 forward/back, 8/2 up/down, 6/4 right/left. Numpad 5 switches move <-> rotate (Ctrl/Shift are game keys):
        // 8/2 muzzle up/down, 6/4 muzzle right/left, 7/1 roll. (Shift can't be used: with NumLock on, Windows turns Shift+Numpad 8 into the
        // Up arrow key - the game walks and the numpad key never arrives.)
        private static bool _rotateMode, _toggleHeld;
        private static string _hint = "";

        public static bool Keys(string weapon, out Vector3 move, out Vector3 rot)
        {
            move = rot = Vector3.zero;
            _hint = "";
            if (Plugin.WeaponAdjust == null || !Plugin.WeaponAdjust.Value || !Known(weapon) || Time.timeScale < 0.01f) { Flush(); return false; }
            // Numpad 5 (also 0, ".", Enter, or 5 with NumLock off = Clear): edge-detected by hand so it works from LateUpdate whatever reads it first
            bool toggle = Input.GetKey(KeyCode.Keypad5) || Input.GetKey(KeyCode.Keypad0) || Input.GetKey(KeyCode.KeypadPeriod) || Input.GetKey(KeyCode.KeypadEnter) || Input.GetKey(KeyCode.Clear);
            if (toggle && !_toggleHeld) _rotateMode = !_rotateMode;
            _toggleHeld = toggle;
            bool rotate = _rotateMode;
            float a = Axis(KeyCode.Keypad7, KeyCode.Keypad1), b = Axis(KeyCode.Keypad8, KeyCode.Keypad2), c = Axis(KeyCode.Keypad6, KeyCode.Keypad4);
            if (rotate) rot = new Vector3(b, c, a) * 45f;   // pitch (muzzle up), yaw (muzzle right), roll - degrees per second
            else move = new Vector3(c, b, a) * 3f;          // right, up, forward - cm per second
            bool any = a != 0f || b != 0f || c != 0f;
            if (!any) Flush();
            var v = For(weapon);
            _hint = (rotate ? "ROTATING " : "MOVING ") + weapon + "   grip " + string.Join(", ", Array.ConvertAll(v, x => x.ToString("0.0", CultureInfo.InvariantCulture))) + "\n"
                  + "Numpad 8/2 up-down (rotate: muzzle up-down), 6/4 right-left (rotate: muzzle right-left), 7/1 forward-back (rotate: roll).  Numpad 5 = switch MOVING / ROTATING.  Turn WeaponAdjustment off when done.";
            return any;
        }

        private static float Axis(KeyCode plus, KeyCode minus) { return (Input.GetKey(plus) ? 1f : 0f) - (Input.GetKey(minus) ? 1f : 0f); }

        public static void ClearHint() { _hint = ""; }

        public static void OnGUI()
        {
            if (string.IsNullOrEmpty(_hint)) return;
            var r = new Rect(20, Screen.height - 90, Screen.width - 40, 60);
            var st = new GUIStyle(GUI.skin.label) { fontSize = 16 };
            st.normal.textColor = Color.black; GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), _hint, st);
            st.normal.textColor = Color.yellow; GUI.Label(r, _hint, st);
        }
    }
}
