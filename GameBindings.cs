using System;
using InsaneSystems.InputManager;
using UnityEngine;

namespace Apocaplayer
{
    internal static class GameBindings
    {
        static readonly string[] IgnitionNames = { "Ignition", "Start Engine", "Start/Stop Engine" };
        internal static KeyAction Find(string name)
        {
            try
            {
                var storage = InputStorage.Singleton;
                if (storage == null || storage.Keys == null) return null;
                foreach (var action in storage.Keys)
                    if (action != null && string.Equals(action.Name, name, StringComparison.OrdinalIgnoreCase)) return action;
            }
            catch { }
            return null;
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        internal static bool KeyDown(KeyCode key) { return key != KeyCode.None && Input.GetKeyDown(key); }
        internal static bool Down(string name, KeyCode fallback)
        {
            var action = Find(name);
            return action != null ? KeyDown(action.Key) || KeyDown(action.AlternativeKey) : KeyDown(fallback);
        }
        internal static string Label(string name, KeyCode fallback)
        {
            var action = Find(name);
            if (action == null) return KeyName(fallback);
            string main = KeyName(action.Key), alt = KeyName(action.AlternativeKey);
            return string.IsNullOrEmpty(main) ? alt : string.IsNullOrEmpty(alt) || main == alt ? main : main + " / " + alt;
        }
        internal static string IgnitionAction
        {
            get
            {
                foreach (var name in IgnitionNames) if (Find(name) != null) return name;
                return "Ignition";
            }
        }
        internal static string KeyName(KeyCode key)
        {
            if (key == KeyCode.None) return "";
            if (key == KeyCode.Equals || key == KeyCode.Plus) return "+";
            if (key == KeyCode.Minus) return "-";
            string name = key.ToString();
            return name.StartsWith("Alpha") ? name.Substring(5) : name;
        }
    }
}
