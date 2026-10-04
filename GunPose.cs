using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace FemalePlayer
{
    // How each weapon sits in her RIGHT HAND, in the hand's own axes: "x, y, z (cm), rotX, rotY, rotZ (degrees)" on top of the raider's grip.
    // - [Weapon grip] <weapon> (main config, Apocasetter): the base grip, used by every pose that has no grip of its own.
    // - config/FemalePlayer/weapon-grips.cfg, [<weapon>] <Pose>: a grip for one pose (Idle, Walk, Run, Crouch, Fire, CrouchFire, Reload).
    //   In play the grips of the poses she is in are blended by how much she is in each (e.g. walking while starting to fire).
    // WeaponAdjustment edits them with the numpad (see Keys); Numpad 9/3 previews a pose so it can be tuned standing still.
    internal static class GunPose
    {
        public static readonly string[] Weapons =
        {
            "akm_trash", "akms", "akm_drum", "m16a1", "redmark_m11", "redmark_m11_scoped", "borz_smg", "22_pipe_smg",
            "slamfire_shotgun", "rochester_m24", "rochester_m24_chopped", "slamberg_500", "slamberg_500_chopped", "crossbow",
            "22_pipe_pistol", "22_pipe_revolver", "folk_17", "shiv", "old_knife", "machete", "pipe_wrench",
        };
        public static readonly string[] Poses = { "Idle", "Walk", "Run", "Crouch", "Fire", "CrouchFire", "Reload" };
        public const int P_IDLE = 0, P_WALK = 1, P_RUN = 2, P_CROUCH = 3, P_FIRE = 4, P_CFIRE = 5, P_RELOAD = 6;

        private static readonly Dictionary<string, ConfigEntry<string>> _cfg = new Dictionary<string, ConfigEntry<string>>();
        private static readonly Dictionary<string, ConfigEntry<string>> _pose = new Dictionary<string, ConfigEntry<string>>();   // key weapon|pose
        private static readonly Dictionary<string, KeyValuePair<string, float[]>> _parsed = new Dictionary<string, KeyValuePair<string, float[]>>();
        private static ConfigFile _grips;

        public static void Bind(ConfigFile config)
        {
            foreach (var w in Weapons)
                _cfg[w] = config.Bind("Weapon grip", w, "0, 0, 0, 0, 0, 0",
                    "Third person: " + w + " in her right hand, relative to the hand, for every pose that has no grip of its own: x, y, z in cm, then rotation x, y, z in degrees. Per-pose grips (Idle, Walk, Run, Crouch, Fire, CrouchFire, Reload) are set with WeaponAdjustment and kept in config/FemalePlayer/weapon-grips.cfg.");
            _grips = new ConfigFile(Path.Combine(Path.Combine(Paths.ConfigPath, "FemalePlayer"), "weapon-grips.cfg"), true);
            foreach (var w in Weapons)
                foreach (var p in Poses)
                    _pose[w + "|" + p] = _grips.Bind(w, p, "", "Grip of " + w + " in the " + p + " pose (empty = the base grip from the main config).");
        }

        private static float[] Parse(string key, string s)
        {
            s = s ?? "";
            KeyValuePair<string, float[]> p;
            if (_parsed.TryGetValue(key, out p) && p.Key == s) return p.Value;
            float[] v = null;
            var parts = s.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
            {
                v = new float[6];
                for (int i = 0; i < 6 && i < parts.Length; i++)
                {
                    float f;
                    if (float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out f)) v[i] = f;
                }
            }
            _parsed[key] = new KeyValuePair<string, float[]>(s, v);
            return v;
        }

        // ---------------- values
        private static string _liveKey;    // weapon|pose being edited
        private static float[] _live;

        public static bool Known(string weapon) { return weapon != null && _cfg.ContainsKey(weapon); }

        public static float[] Base(string weapon)
        {
            ConfigEntry<string> e;
            if (!Known(weapon) || !_cfg.TryGetValue(weapon, out e)) return new float[6];
            return Parse(weapon, e.Value) ?? new float[6];
        }

        // the pose's own grip, null when it has none
        public static float[] PoseGrip(string weapon, int pose)
        {
            string k = weapon + "|" + Poses[pose];
            if (_live != null && _liveKey == k) return _live;
            ConfigEntry<string> e;
            return _pose.TryGetValue(k, out e) ? Parse(k, e.Value) : null;
        }

        public static bool HasPose(string weapon, int pose) { return Known(weapon) && PoseGrip(weapon, pose) != null; }

        public static float[] Effective(string weapon, int pose) { return PoseGrip(weapon, pose) ?? Base(weapon); }

        // blend of the poses' grips by weight -> local offset position (m) and rotation
        public static void Blend(string weapon, float[] w, out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero; rot = Quaternion.identity;
            if (!Known(weapon)) return;
            float sum = 0f; var q = new Vector4(0, 0, 0, 0); Quaternion first = Quaternion.identity; bool any = false;
            for (int i = 0; i < Poses.Length; i++)
            {
                if (w[i] <= 0.0001f) continue;
                var v = Effective(weapon, i);
                pos += new Vector3(v[0], v[1], v[2]) * (0.01f * w[i]);
                var r = Quaternion.Euler(v[3], v[4], v[5]);
                if (!any) { first = r; any = true; }
                if (Quaternion.Dot(first, r) < 0f) r = new Quaternion(-r.x, -r.y, -r.z, -r.w);
                q += new Vector4(r.x, r.y, r.z, r.w) * w[i];
                sum += w[i];
            }
            if (sum <= 0f) { var v = Base(weapon); pos = new Vector3(v[0], v[1], v[2]) * 0.01f; rot = Quaternion.Euler(v[3], v[4], v[5]); return; }
            pos /= sum;
            q.Normalize();
            rot = new Quaternion(q.x, q.y, q.z, q.w);
        }

        public static void SetLive(string weapon, int pose, float[] v)
        {
            string k = weapon + "|" + Poses[pose];
            if (_liveKey != k) Flush();
            _liveKey = k; _live = v;
        }

        // writes the edited values into the pose's line (once, when the keys are let go / the weapon or pose changes)
        public static void Flush()
        {
            if (_live != null && _liveKey != null)
            {
                ConfigEntry<string> e;
                if (_pose.TryGetValue(_liveKey, out e))
                    e.Value = string.Join(", ", Array.ConvertAll(_live, x => x.ToString("0.##", CultureInfo.InvariantCulture)));
            }
            _live = null; _liveKey = null;
        }

        // ---------------- keys (WeaponAdjustment)
        // Numpad 8/2 up/down, 6/4 right/left, 7/1 forward/back; Numpad 5 (or 0 . Enter) switches MOVING <-> ROTATING (8/2 muzzle up/down,
        // 6/4 muzzle right/left, 7/1 roll). Numpad 9 / 3 = preview the next / previous pose (Idle .. Reload, then live), Numpad - = clear the
        // grip of the pose being edited (back to the base grip). Shift/Ctrl can't be used: with NumLock on, Shift+Numpad 8 arrives as the Up arrow.
        private static bool _rotateMode;
        private static readonly Dictionary<KeyCode, bool> _held = new Dictionary<KeyCode, bool>();
        private static int _preview = -1;     // -1 = live
        private static string _hint = "";

        public static int Preview { get { return Plugin.WeaponAdjust != null && Plugin.WeaponAdjust.Value && ThirdPerson.On ? _preview : -1; } }

        private static bool Pressed(params KeyCode[] keys)
        {
            bool down = false;
            foreach (var k in keys)
            {
                bool now = Input.GetKey(k);
                bool was; _held.TryGetValue(k, out was);
                if (now && !was) down = true;
                _held[k] = now;
            }
            return down;
        }

        public static bool Keys(string weapon, int pose, out Vector3 move, out Vector3 rot)
        {
            move = rot = Vector3.zero;
            _hint = "";
            if (Plugin.WeaponAdjust == null || !Plugin.WeaponAdjust.Value || !Known(weapon) || Time.timeScale < 0.01f) { Flush(); return false; }
            if (Pressed(KeyCode.Keypad5, KeyCode.Keypad0, KeyCode.KeypadPeriod, KeyCode.KeypadEnter, KeyCode.Clear)) _rotateMode = !_rotateMode;
            if (Pressed(KeyCode.Keypad9)) { Flush(); _preview = _preview + 1 >= Poses.Length ? -1 : _preview + 1; }
            if (Pressed(KeyCode.Keypad3)) { Flush(); _preview = _preview - 1 < -1 ? Poses.Length - 1 : _preview - 1; }
            if (Pressed(KeyCode.KeypadMinus))
            {
                Flush();
                ConfigEntry<string> e;
                if (_pose.TryGetValue(weapon + "|" + Poses[pose], out e)) e.Value = "";
            }
            float a = Axis(KeyCode.Keypad7, KeyCode.Keypad1), b = Axis(KeyCode.Keypad8, KeyCode.Keypad2), c = Axis(KeyCode.Keypad6, KeyCode.Keypad4);
            if (_rotateMode) rot = new Vector3(b, c, a) * 45f;   // pitch (muzzle up), yaw (muzzle right), roll - degrees per second
            else move = new Vector3(c, b, a) * 3f;               // right, up, forward - cm per second
            bool anyKey = a != 0f || b != 0f || c != 0f;
            if (!anyKey) Flush();
            var v = Effective(weapon, pose);
            _hint = (_rotateMode ? "ROTATING " : "MOVING ") + weapon + "   pose: " + Poses[pose] + (_preview >= 0 ? " (PREVIEW)" : "")
                  + (HasPose(weapon, pose) ? "" : " - uses the base grip") + "   grip " + string.Join(", ", Array.ConvertAll(v, x => x.ToString("0.0", CultureInfo.InvariantCulture))) + "\n"
                  + "Numpad 8/2, 6/4, 7/1 = move (or turn).  5 = switch MOVING / ROTATING.  9/3 = preview next/previous pose.  - = clear this pose's grip.  Turn WeaponAdjustment off when done.";
            return anyKey;
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
