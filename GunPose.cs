using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace Apocaplayer
{
    // Where each weapon sits in her RIGHT HAND, per animation clip: ABSOLUTE local position (cm) and rotation (degrees, Euler x, y, z) of the
    // weapon model under the hand bone. Nothing in the game changes these: no AutoGrip, no base line, no follow-the-left-hand - only the numpad
    // (WeaponAdjustment) edits them.
    //   (2.0) entries are keyed by the CLIP NAME that moves her hands (RifleWalkForwardLeft, PistolFire, Kick ...), one entry per clip.
    //   lookup per (weapon, clip): config/Apocaplayer/weapon-poses.txt  >  BuiltinPoses (hard-coded in the mod)  >  the weapon kind's idle clip
    //   (RifleIdle / PistolIdle, set by Body)  >  the weapon's old "Idle" entry  >  the raider's grip.
    // Every save also writes config/Apocaplayer/BuiltinPoses.generated.cs = the complete table as C#: copy it over BuiltinPoses.cs in the repo
    // to hard-code the poses.
    // Grips from 0.5.x / 0.6.x (offsets on top of the raider grip + AutoGrip, weapon-grips.cfg + the [Weapon grip] lines) are converted once per
    // weapon into absolute poses (Body.PoseProp -> ConvertLegacy), then never read again for that weapon.
    internal static class GunPose
    {
        // the clips the drawn weapon can show (set by Body.SetPoseList): the numpad 9/3 cycle, in that order
        private static readonly List<string> _list = new List<string>();
        private static string _idleName = "Idle";
        public static string[] Poses { get { return _list.ToArray(); } }   // (kept for other mods reading it by reflection)
        private static int PoseIndex(string name) { int i = _list.IndexOf(name); return i >= 0 ? i : 1000; }

        private static string _dir, _file, _export;
        private static readonly Dictionary<string, float[]> _user = new Dictionary<string, float[]>();      // "weapon|Anim" -> x,y,z,rx,ry,rz
        private static readonly Dictionary<string, float[]> _builtin = new Dictionary<string, float[]>();
        // legacy (0.5/0.6): offsets per weapon|Anim and the per-weapon base line
        private static readonly Dictionary<string, float[]> _legacy = new Dictionary<string, float[]>();
        private static readonly Dictionary<string, float[]> _legacyBase = new Dictionary<string, float[]>();

        public static void Bind(ConfigFile config)
        {
            _dir = Path.Combine(Paths.ConfigPath, "Apocaplayer");
            Directory.CreateDirectory(_dir);
            _file = Path.Combine(_dir, "weapon-poses.txt");
            _export = Path.Combine(_dir, "BuiltinPoses.generated.cs");
            foreach (var line in BuiltinPoses.Data)
            {
                var p = line.Split('|');
                float[] v;
                if (p.Length == 3 && (v = Parse(p[2])) != null) _builtin[p[0] + "|" + p[1]] = v;
            }
            LoadUser();
            LoadLegacy(config.ConfigFilePath);
            Plugin.Log.LogInfo("Weapon poses: " + _user.Count + " in weapon-poses.txt, " + _builtin.Count + " built in"
                + (_legacy.Count + _legacyBase.Count > 0 ? "; old grips waiting for conversion: " + string.Join(", ", LegacyWeapons().ToArray()) : ""));
        }

        private static float[] Parse(string s)
        {
            var parts = (s ?? "").Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 6) return null;
            var v = new float[6];
            for (int i = 0; i < 6; i++)
                if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i])) return null;
            return v;
        }

        private static string Fmt(float[] v) { return string.Join(", ", Array.ConvertAll(v, x => x.ToString("0.###", CultureInfo.InvariantCulture))); }

        private static void LoadUser()
        {
            _user.Clear();
            if (!File.Exists(_file)) return;
            foreach (var raw in File.ReadAllLines(_file))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                var v = Parse(line.Substring(eq + 1));
                if (v != null && key.IndexOf('|') > 0) _user[key] = v;
            }
        }

        // weapon-grips.cfg ([weapon] Anim = offsets) and the main config's [Weapon grip] <weapon> = offsets lines, read as text (never written)
        private static void LoadLegacy(string mainConfig)
        {
            try
            {
                string old = Path.Combine(_dir, "weapon-grips.cfg");
                if (File.Exists(old))
                {
                    string section = null;
                    foreach (var raw in File.ReadAllLines(old))
                    {
                        string line = raw.Trim();
                        if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2); continue; }
                        if (section == null || line.StartsWith("#")) continue;
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        string anim = line.Substring(0, eq).Trim();
                        var v = Parse(line.Substring(eq + 1));
                        if (v != null && !HasAny(section)) _legacy[section + "|" + anim] = v;
                    }
                }
                // the main config's [Weapon grip] lines are removed from it on this start (old settings) - keep them in legacy-base-lines.txt
                string kept = Path.Combine(_dir, "legacy-base-lines.txt");
                if (File.Exists(mainConfig) && !File.Exists(kept))
                {
                    var lines = new List<string> { "[Weapon grip]" };
                    bool inG = false;
                    foreach (var raw in File.ReadAllLines(mainConfig))
                    {
                        string line = raw.Trim();
                        if (line.StartsWith("[")) { inG = line == "[Weapon grip]"; continue; }
                        if (inG && !line.StartsWith("#") && line.IndexOf('=') > 0) lines.Add(line);
                    }
                    File.WriteAllLines(kept, lines.ToArray());
                }
                if (File.Exists(kept))
                {
                    bool inGrip = false;
                    foreach (var raw in File.ReadAllLines(kept))
                    {
                        string line = raw.Trim();
                        if (line.StartsWith("[")) { inGrip = line == "[Weapon grip]"; continue; }
                        if (!inGrip || line.StartsWith("#")) continue;
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        string w = line.Substring(0, eq).Trim();
                        var v = Parse(line.Substring(eq + 1));
                        if (v != null && !HasAny(w) && Array.Exists(v, x => x != 0f)) _legacyBase[w] = v;
                    }
                }
            }
            catch (Exception e) { Plugin.Warn("Weapon poses: reading the old grips failed: " + e.Message); }
        }

        private static List<string> LegacyWeapons()
        {
            var s = new SortedDictionary<string, bool>();
            foreach (var k in _legacy.Keys) s[k.Substring(0, k.IndexOf('|'))] = true;
            foreach (var k in _legacyBase.Keys) s[k] = true;
            return new List<string>(s.Keys);
        }

        // ---------------- values
        public static bool HasAny(string weapon)
        {
            string pre = weapon + "|";
            foreach (var k in _user.Keys) if (k.StartsWith(pre, StringComparison.Ordinal)) return true;
            foreach (var k in _builtin.Keys) if (k.StartsWith(pre, StringComparison.Ordinal)) return true;
            return false;
        }

        public static bool HasLegacy(string weapon)
        {
            if (string.IsNullOrEmpty(weapon) || HasAny(weapon)) return false;
            if (_legacyBase.ContainsKey(weapon)) return true;
            string pre = weapon + "|";
            foreach (var k in _legacy.Keys) if (k.StartsWith(pre, StringComparison.Ordinal)) return true;
            return false;
        }

        // the converted poses: base (the grip the old offsets were tuned on, hand-local) ∘ each animation's offset (or the base line)
        public static void ConvertLegacy(string weapon, Vector3 basePos, Quaternion baseRot)
        {
            float[] line;
            _legacyBase.TryGetValue(weapon, out line);
            int n = 0;
            var names = new List<string> { "Idle" };
            string pre = weapon + "|";
            foreach (var k in _legacy.Keys) if (k.StartsWith(pre, StringComparison.Ordinal)) { string a = k.Substring(pre.Length); if (!names.Contains(a)) names.Add(a); }
            foreach (var a in names)
            {
                float[] off;
                bool own = _legacy.TryGetValue(weapon + "|" + a, out off);
                if (!own) off = line ?? new float[6];
                Vector3 p = basePos + new Vector3(off[0], off[1], off[2]) * 0.01f;
                Quaternion r = baseRot * Quaternion.Euler(off[3], off[4], off[5]);
                _user[weapon + "|" + a] = Abs(p, r);
                if (own) n++;
            }
            Save();
            Plugin.Log.LogInfo("Weapon poses: " + weapon + " converted from the old grips (" + n + " tuned animation(s), " + names.Count + " written)");
        }

        public static float[] Abs(Vector3 p, Quaternion r)
        {
            var e = r.eulerAngles;
            return new[] { p.x * 100f, p.y * 100f, p.z * 100f, Mathf.DeltaAngle(0f, e.x), Mathf.DeltaAngle(0f, e.y), Mathf.DeltaAngle(0f, e.z) };
        }

        private static string _liveKey;
        private static float[] _live;

        // the clip's own entry (live edit > file > built in), null when it has none
        public static float[] Own(string weapon, string clip)
        {
            if (string.IsNullOrEmpty(clip)) return null;
            string k = weapon + "|" + clip;
            if (_live != null && _liveKey == k) return _live;
            float[] v;
            if (_user.TryGetValue(k, out v)) return v;
            if (_builtin.TryGetValue(k, out v)) return v;
            return null;
        }

        // ---------------- the drawn weapon's clips (set by Body): the numpad cycle, and the kind's idle clip = the fallback for untuned ones
        public static void SetPoseList(List<string> names, string idleName)
        {
            string was = _sel >= 0 && _sel < _list.Count ? _list[_sel] : null;
            _list.Clear(); _list.AddRange(names);
            _idleName = idleName ?? "Idle";
            _sel = was != null ? _list.IndexOf(was) : -1;   // another weapon: stay on the same clip when it has it (to copy a pose across weapons)
        }
        private static void Put(string weapon, string clip, float[] v)
        {
            if (v == null) _user.Remove(weapon + "|" + clip);
            else _user[weapon + "|" + clip] = (float[])v.Clone();
        }

        public static bool HasPose(string weapon, string clip) { return !string.IsNullOrEmpty(weapon) && Own(weapon, clip) != null; }

        // what the clip shows: its own entry, else the kind's idle clip, else the weapon's old Idle entry, else the raider grip (def)
        public static float[] Effective(string weapon, string clip, float[] def)
        {
            return Own(weapon, clip) ?? Own(weapon, _idleName) ?? Own(weapon, "Idle") ?? def;
        }
        // (2.2.0, ModAPI) the same for a character that is not the player: its own kind's idle clip as the fallback (not the drawn weapon's)
        public static float[] EffectiveFor(string weapon, string clip, string idleName, float[] def)
        {
            return Own(weapon, clip) ?? Own(weapon, idleName) ?? Own(weapon, "Idle") ?? def;
        }
        public static void BlendFor(string weapon, Dictionary<string, float> w, string idleName, Vector3 defPos, Quaternion defRot, out Vector3 pos, out Quaternion rot)
        {
            var def = Abs(defPos, defRot);
            pos = Vector3.zero; float sum = 0f; var q = Vector4.zero; Quaternion first = Quaternion.identity; bool any = false;
            foreach (var kv in w)
            {
                if (kv.Value <= 0.0001f) continue;
                var v = EffectiveFor(weapon, kv.Key, idleName, def);
                pos += new Vector3(v[0], v[1], v[2]) * (0.01f * kv.Value);
                var r = Quaternion.Euler(v[3], v[4], v[5]);
                if (!any) { first = r; any = true; }
                if (Quaternion.Dot(first, r) < 0f) r = new Quaternion(-r.x, -r.y, -r.z, -r.w);
                q += new Vector4(r.x, r.y, r.z, r.w) * kv.Value;
                sum += kv.Value;
            }
            if (sum <= 0f) { var v = EffectiveFor(weapon, idleName, idleName, def); pos = new Vector3(v[0], v[1], v[2]) * 0.01f; rot = Quaternion.Euler(v[3], v[4], v[5]); return; }
            pos /= sum;
            q.Normalize();
            rot = new Quaternion(q.x, q.y, q.z, q.w);
        }
        public static float[] Effective(string weapon, int pose, float[] def)   // (other mods, by index into Poses)
        {
            return Effective(weapon, pose >= 0 && pose < _list.Count ? _list[pose] : null, def);
        }

        // blend of the clips' poses by weight -> hand-local position (m) and rotation
        public static void Blend(string weapon, Dictionary<string, float> w, Vector3 defPos, Quaternion defRot, out Vector3 pos, out Quaternion rot)
        {
            var def = Abs(defPos, defRot);
            pos = Vector3.zero; float sum = 0f; var q = Vector4.zero; Quaternion first = Quaternion.identity; bool any = false;
            foreach (var kv in w)
            {
                if (kv.Value <= 0.0001f) continue;
                var v = Effective(weapon, kv.Key, def);
                pos += new Vector3(v[0], v[1], v[2]) * (0.01f * kv.Value);
                var r = Quaternion.Euler(v[3], v[4], v[5]);
                if (!any) { first = r; any = true; }
                if (Quaternion.Dot(first, r) < 0f) r = new Quaternion(-r.x, -r.y, -r.z, -r.w);
                q += new Vector4(r.x, r.y, r.z, r.w) * kv.Value;
                sum += kv.Value;
            }
            if (sum <= 0f) { var v = Effective(weapon, _idleName, def); pos = new Vector3(v[0], v[1], v[2]) * 0.01f; rot = Quaternion.Euler(v[3], v[4], v[5]); return; }
            pos /= sum;
            q.Normalize();
            rot = new Quaternion(q.x, q.y, q.z, q.w);
        }

        public static void SetLive(string weapon, string clip, float[] v)
        {
            string k = weapon + "|" + clip;
            if (_liveKey != k) Flush();
            _liveKey = k; _live = v; _liveWeapon = weapon; _livePose = clip;
        }
        private static string _liveWeapon, _livePose;

        // writes the edited pose (once, when the keys are let go / the weapon or animation changes)
        public static void Flush()
        {
            if (_live != null && _liveKey != null) { var v = _live; _live = null; _liveKey = null; Put(_liveWeapon, _livePose, v); Save(); }
            _live = null; _liveKey = null;
        }

        private static void Save()
        {
            try
            {
                var keys = new List<string>(_user.Keys); keys.Sort(Compare);
                var sb = new StringBuilder();
                sb.AppendLine("# Apocaplayer weapon poses: weapon|Animation = x, y, z (cm), rotation x, y, z (degrees) of the weapon model in her right hand.");
                sb.AppendLine("# Absolute values, edited in game with WeaponAdjustment (numpad). Missing animation = the weapon's Idle, else the raider's grip.");
                foreach (var k in keys) sb.Append(k).Append(" = ").AppendLine(Fmt(_user[k]));
                File.WriteAllText(_file, sb.ToString());
                // the complete table (built in + yours) as C#, ready to replace BuiltinPoses.cs
                var all = new Dictionary<string, float[]>(_builtin);
                foreach (var kv in _user) all[kv.Key] = kv.Value;
                var ak = new List<string>(all.Keys); ak.Sort(Compare);
                var cs = new StringBuilder();
                cs.AppendLine("namespace Apocaplayer");
                cs.AppendLine("{");
                cs.AppendLine("    // Weapon poses hard-coded in the mod (generated by Apocaplayer from config/Apocaplayer/weapon-poses.txt on " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + ").");
                cs.AppendLine("    // \"weapon|Animation|x, y, z (cm), rotation x, y, z (degrees)\" of the weapon model in her right hand. weapon-poses.txt overrides these.");
                cs.AppendLine("    internal static class BuiltinPoses");
                cs.AppendLine("    {");
                cs.AppendLine("        public static readonly string[] Data =");
                cs.AppendLine("        {");
                foreach (var k in ak) cs.Append("            \"").Append(k).Append('|').Append(Fmt(all[k])).AppendLine("\",");
                cs.AppendLine("        };");
                cs.AppendLine("    }");
                cs.AppendLine("}");
                File.WriteAllText(_export, cs.ToString());
            }
            catch (Exception e) { Plugin.Warn("Weapon poses: saving failed: " + e.Message); }
        }

        // weapon, then animation in clip order
        private static int Compare(string a, string b)
        {
            int ia = a.IndexOf('|'), ib = b.IndexOf('|');
            int c = string.CompareOrdinal(a.Substring(0, ia), b.Substring(0, ib));
            if (c != 0) return c;
            return PoseIndex(a.Substring(ia + 1)).CompareTo(PoseIndex(b.Substring(ib + 1)));
        }

        // ---------------- keys (WeaponAdjustment)
        // Numpad 8/2 up/down, 6/4 right/left, 7/1 forward/back; Numpad 5 (or 0 . Enter) switches MOVING <-> ROTATING (8/2 muzzle up/down,
        // 6/4 muzzle right/left, 7/1 roll). Numpad 9/3 select the next/previous animation the drawn weapon has (each clip once; she plays it standing
        // still), after the last one back to what she really plays. An edit applies to every entry that plays the same clip. Numpad - = delete the animation's own
        // pose (the built-in one shows again, else the weapon's Idle pose), Numpad / = copy the shown pose, Numpad * = paste it into the current animation.
        // Shift/Ctrl can't be used: with NumLock on, Shift+Numpad 8 arrives as the Up arrow.
        private static bool _rotateMode;
        private static readonly Dictionary<KeyCode, bool> _held = new Dictionary<KeyCode, bool>();
        private static float[] _clip;
        private static string _hint = "", _note = "";

        public static bool Editing { get { return Plugin.WeaponAdjust != null && Plugin.WeaponAdjust.Value; } }
        // Numpad 9/3: the selected clip of the drawn weapon (index into _list), played standing still; -1 = what she really plays
        private static int _sel = -1;
        public static string Preview { get { return Editing && ThirdPerson.On && _sel >= 0 && _sel < _list.Count ? _list[_sel] : null; } }
        public static void ResetSelection() { _sel = -1; }
        private static void Step(int d)
        {
            if (_list.Count == 0) { _sel = -1; return; }
            _sel += d;
            if (_sel >= _list.Count) _sel = -1;
            else if (_sel < -1) _sel = _list.Count - 1;
        }

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

        public static void Note(string s) { _note = s ?? ""; }
        public static string AimLine = "";      // (2.1.7) Body's aim-lift line under the hint
        private static string _status = "";
        public static void Status(string s) { _status = s ?? ""; }

        public static bool Keys(string weapon, string pose, float[] shown, out Vector3 move, out Vector3 rot)
        {
            move = rot = Vector3.zero;
            _hint = "";
            if (!Editing || string.IsNullOrEmpty(weapon) || Time.timeScale < 0.01f) { Flush(); return false; }
            if (Pressed(KeyCode.Keypad5, KeyCode.Keypad0, KeyCode.KeypadPeriod, KeyCode.KeypadEnter, KeyCode.Clear)) _rotateMode = !_rotateMode;
            if (Pressed(KeyCode.Keypad9)) { Flush(); Step(+1); }
            if (Pressed(KeyCode.Keypad3)) { Flush(); Step(-1); }
            if (Pressed(KeyCode.KeypadMinus)) { Flush(); Put(weapon, pose, null); Save(); }
            if (Pressed(KeyCode.KeypadDivide)) _clip = (float[])shown.Clone();
            if (Pressed(KeyCode.KeypadMultiply) && _clip != null) { Flush(); Put(weapon, pose, _clip); Save(); }
            float a = Axis(KeyCode.Keypad7, KeyCode.Keypad1), b = Axis(KeyCode.Keypad8, KeyCode.Keypad2), c = Axis(KeyCode.Keypad6, KeyCode.Keypad4);
            if (_rotateMode) rot = new Vector3(b, c, a) * 45f;   // pitch (muzzle up), yaw (muzzle right), roll - degrees per second
            else move = new Vector3(c, b, a) * 3f;               // right, up, forward - cm per second
            bool anyKey = a != 0f || b != 0f || c != 0f;
            if (!anyKey) Flush();
            bool own = HasPose(weapon, pose);
            _hint = (_rotateMode ? "ROTATING " : "MOVING ") + weapon + "   " + (_sel >= 0 ? "SELECTED " + (_sel + 1) + "/" + _list.Count + ": " : "playing: ") + pose + "   [" + _status + "]"
                  + (own ? "" : HasPose(weapon, _idleName) ? " - shows the " + _idleName + " pose" : HasPose(weapon, "Idle") ? " - shows the old Idle pose" : " - shows the raider grip") + "   pose " + string.Join(", ", Array.ConvertAll(shown, x => x.ToString("0.0", CultureInfo.InvariantCulture))) + "\n"
                  + "8/2, 6/4, 7/1 = move (or turn).  5 = MOVING / ROTATING.  9/3 = next/previous clip of this weapon (" + _list.Count + "; then back to what she plays).  - = delete.  / = copy pose, * = paste" + (_clip != null ? " (copied)" : "") + ".  Saved in config/Apocaplayer/weapon-poses.txt"
                  + (string.IsNullOrEmpty(_note) ? "" : "\n" + _note) + (string.IsNullOrEmpty(AimLine) ? "" : "\n" + AimLine);
            return anyKey;
        }

        private static float Axis(KeyCode plus, KeyCode minus) { return (Input.GetKey(plus) ? 1f : 0f) - (Input.GetKey(minus) ? 1f : 0f); }

        public static void ClearHint() { _hint = ""; }

        public static void OnGUI()
        {
            if (string.IsNullOrEmpty(_hint)) return;
            var r = new Rect(20, Screen.height - 150, Screen.width - 40, 130);
            var st = new GUIStyle(GUI.skin.label) { fontSize = 16 };
            st.normal.textColor = Color.black; GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), _hint, st);
            st.normal.textColor = Color.yellow; GUI.Label(r, _hint, st);
        }
    }
}
