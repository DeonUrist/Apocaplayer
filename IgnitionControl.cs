using System;
using System.IO;
using System.Reflection;
using BepInEx;
using InsaneSystems.InputManager;
using UnityEngine;

namespace Apocaplayer
{
    // (2.3.2) The ignition hotkey is a row in the game's own Controls screen ("Ignition", after Handbrake), not a config entry.
    // The game's rebinding asset (InsaneSystems InputManager) drops actions that are not in its built-in list whenever it loads its
    // saved bindings, so the row is added again on every start and its keys are kept in config/Apocaplayer/ignition-key.txt
    // ("Key,AlternativeKey"). A rebind in the Controls screen is written there at once. Car.cs reads the action through GameBindings
    // (IgnitionAction = "Ignition"); Plugin.IgnitionKey (hidden, never saved) mirrors the key for the moment before the row exists.
    // First start: the old [VEHICLE] / [General] IgnitionKey values from the .cfg are carried over once, then removed from the .cfg.
    internal static class IgnitionControl
    {
        internal const string Name = "Ignition";
        private static KeyCode _key = KeyCode.E, _alt = KeyCode.None, _lastKey, _lastAlt;
        private static bool _loaded, _lastEnabled;
        private static float _next;

        private static string FilePath { get { return Path.Combine(Path.Combine(Paths.ConfigPath, "Apocaplayer"), "ignition-key.txt"); } }

        // called from Plugin.Awake with the key found in the old config entries (None = nothing to carry over)
        internal static void Load(KeyCode legacy)
        {
            _loaded = true;
            try
            {
                if (File.Exists(FilePath))
                {
                    var parts = File.ReadAllText(FilePath).Trim().Split(',');
                    _key = Parse(parts.Length > 0 ? parts[0] : "", KeyCode.E);
                    _alt = parts.Length > 1 ? Parse(parts[1], KeyCode.None) : KeyCode.None;
                }
                else
                {
                    if (legacy != KeyCode.None) _key = legacy;
                    Save();
                    Plugin.Log.LogInfo("Ignition key " + _key + " moved to the game's Controls screen (" + FilePath + ")");
                }
            }
            catch (Exception e) { Plugin.Warn("Ignition key file: " + e.Message); }
            _lastKey = _key; _lastAlt = _alt;
            Mirror();
        }

        private static KeyCode Parse(string s, KeyCode fallback)
        {
            try { return (KeyCode)Enum.Parse(typeof(KeyCode), s.Trim(), true); } catch { return fallback; }
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, _key + "," + _alt);
            }
            catch (Exception e) { Plugin.Warn("Ignition key file: " + e.Message); }
        }

        private static void Mirror() { if (Plugin.IgnitionKey != null) Plugin.IgnitionKey.Value = _key; }

        internal static void Tick()
        {
            if (!_loaded || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.5f;
            InputStorage st;
            try { st = InputStorage.Singleton; } catch { return; }
            if (st == null || st.Keys == null) return;
            var keys = st.Keys;
            bool enabled = Plugin.Enabled.Value, changed = false;
            int idx = keys.FindIndex(k => k != null && k.Name == Name);
            if (enabled)
            {
                if (idx < 0)
                {
                    var ka = Make();
                    if (ka == null) return;
                    int after = keys.FindIndex(k => k != null && k.Name == "Handbrake");
                    keys.Insert(after >= 0 ? Mathf.Min(after + 1, keys.Count) : keys.Count, ka);
                    _lastKey = _key; _lastAlt = _alt;
                    changed = true;
                    Plugin.Verbose("Controls: added \"" + Name + "\" (" + _key + ")");
                }
                else
                {
                    var ka = keys[idx];
                    if (ka.Key != _lastKey || ka.AlternativeKey != _lastAlt)
                    {   // rebound in the Controls screen
                        _key = _lastKey = ka.Key; _alt = _lastAlt = ka.AlternativeKey;
                        Save(); Mirror();
                        Plugin.Verbose("Controls: Ignition rebound to " + _key + (_alt != KeyCode.None ? " / " + _alt : ""));
                    }
                }
            }
            else if (idx >= 0) { keys.RemoveAt(idx); changed = true; }
            if (changed || enabled != _lastEnabled) { _lastEnabled = enabled; RefreshMenu(); }
        }

        private static KeyAction Make()
        {
            try
            {
                var ka = new KeyAction();
                SetField(typeof(InputAction), ka, "name", Name);
                SetField(typeof(KeyAction), ka, "key", _key);
                SetField(typeof(KeyAction), ka, "alternativeKey", _alt);
                return ka;
            }
            catch (Exception e) { Plugin.Warn("Could not add the Ignition control: " + e.Message); _loaded = false; return null; }
        }

        private static void SetField(Type t, object o, string name, object v)
        {
            var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f == null) throw new MissingFieldException(t.Name, name);
            f.SetValue(o, v);
        }

        // rebuild the Controls screen if it is open (its rows are generated from InputStorage.keys)
        private static void RefreshMenu()
        {
            try
            {
                var s = InsaneSystems.InputManager.UI.Settings.singleton;
                if (s == null) return;
                var m = typeof(InsaneSystems.InputManager.UI.Settings).GetMethod("RefreshUI", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (m != null) m.Invoke(s, null);
            }
            catch (Exception e) { Plugin.Verbose("Controls refresh: " + e.Message); }
        }
    }
}
