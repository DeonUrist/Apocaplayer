using System;
using HutongGames.PlayMaker;
using UnityEngine;

namespace FemalePlayer
{
    // Ignition hotkey. The car's START [Start] FSM is driven by clicks on the key: off -(mouse over)-> checkEngine (engine attached?) -> over
    // -(click)-> Ignition (key turned) -> off2 -(click)-> over2 -> Start (cranks: fuel / rpm checks) -> wait -> off3/over3 = running
    // (a click there stops it). The key does the two clicks: SetState("Ignition") from off, then SetState("Start") from off2.
    internal static class Car
    {
        private static PlayMakerFSM _start;
        private static Transform _startCar;
        private static int _phase;           // 0 idle, 1 turning the key, 2 cranking
        private static float _phaseUntil;
        private static string _hint = "";
        private static Font _font;

        private static readonly string[] Running = { "Start", "wait", "off3", "over3", "Stop", "wait2" };

        public static void Tick()
        {
            _hint = "";
            if (!Plugin.Enabled.Value || !Game.Ready || !Game.InCar || Plugin.IgnitionKey.Value == KeyCode.None) { _phase = 0; return; }
            var car = Game.CarRoot;
            if (car == null) return;
            if (_startCar != car || _start == null)
            {
                _startCar = car; _start = null;
                var st = car.Find("START") ?? Game.FindDeep(car, "START");
                if (st != null) foreach (var f in st.GetComponents<PlayMakerFSM>()) if (f.FsmName == "Start") _start = f;
                if (_start == null) return;
            }
            if (!_start.enabled || _start.Fsm == null || !_start.Fsm.Initialized) return;
            string s = _start.ActiveStateName ?? "";
            bool running = Array.IndexOf(Running, s) >= 0;
            if (!running && !Game.Paused && _phase == 0) _hint = KeyName(Plugin.IgnitionKey.Value) + " - Start / Ignition";
            if (Game.Paused) return;

            if (_phase == 0 && !running && Input.GetKeyDown(Plugin.IgnitionKey.Value))
            {
                if (!HasEngine(car)) { Plugin.Verbose("Ignition: no engine in this car"); return; }
                _phase = 1; _phaseUntil = Time.unscaledTime + 1.5f;
            }
            if (_phase == 0) return;
            if (Time.unscaledTime > _phaseUntil) { Plugin.Verbose("Ignition: gave up in state " + s); _phase = 0; return; }
            try
            {
                if (_phase == 1)
                {
                    if (s == "off" || s == "checkEngine" || s == "over" || s == "back") _start.Fsm.SetState("Ignition");   // key turned (click 1)
                    else if (s == "off2" || s == "over2") { _start.Fsm.SetState("Start"); _phase = 2; Plugin.Verbose("Ignition: starting"); }   // crank (click 2)
                }
                else if (_phase == 2 && s != "Ignition") _phase = 0;
            }
            catch (Exception e) { Plugin.Warn("Ignition: " + e.Message); _phase = 0; }
        }

        private static bool HasEngine(Transform car)
        {
            var h = Game.FindDeep(car, "hinge_engine");
            return h == null || h.childCount > 0;
        }

        private static string KeyName(KeyCode k)
        {
            string n = k.ToString();
            return n.StartsWith("Alpha") ? n.Substring(5) : n;
        }

        public static void OnGUI()
        {
            if (string.IsNullOrEmpty(_hint)) return;
            if (_font == null)
            {   // the game's own UI font
                foreach (var t in UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Text>()) if (t != null && t.font != null && t.isActiveAndEnabled) { _font = t.font; break; }
            }
            var st = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Screen.height / 42f), alignment = TextAnchor.MiddleLeft };
            if (_font != null) st.font = _font;
            var r = new Rect(Screen.width * 0.025f, Screen.height * 0.62f, Screen.width * 0.4f, st.fontSize * 1.6f);
            st.normal.textColor = new Color(0f, 0f, 0f, 0.8f); GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), _hint, st);
            st.normal.textColor = Color.white; GUI.Label(r, _hint, st);
        }

        public static void Reset() { _start = null; _startCar = null; _phase = 0; _hint = ""; }
    }
}
