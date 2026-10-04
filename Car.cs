using System;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using UnityEngine.UI;

namespace Apocaplayer
{
    // Ignition hotkey. The car's START [Start] FSM is driven by clicks on the key: off -(mouse over)-> checkEngine (engine attached?) -> over
    // -(click)-> Ignition (key turned) -> off2 -(click)-> over2 -> Start (cranks: fuel / rpm checks) -> wait -> off3/over3 = running
    // -(click / stall)-> Stop (the key-off + engine-stop sounds) -> wait2 -> off.
    // One press when the engine is off does both clicks: SetState("Ignition") from off, then SetState("Start") from off2.
    // One press while it runs: SetState("Stop") - the game's own stop, with its sounds.
    // Running = the engine says so (NWH VehicleController.powertrain.engine.IsRunning, by reflection) or the FSM is in a running state.
    internal static class Car
    {
        private static PlayMakerFSM _start;
        private static Transform _startCar;
        private static int _phase;           // 0 idle, 1 turning the key, 2 cranking
        private static float _phaseUntil;
        private static string _hint = "";
        private static Text _gameText;       // the game's own hint text (what the START FSM writes "Start" into): its font
        private static Font _font;
        private static object _engine;
        private static PropertyInfo _isRunning;

        private static readonly string[] RunningStates = { "Start", "wait", "off3", "over3" };
        private static readonly string[] Stopping = { "Stop", "wait2" };

        public static void Tick()
        {
            _hint = "";
            if (!Plugin.Enabled.Value || !Game.Ready || !Game.InCar || Plugin.IgnitionKey.Value == KeyCode.None) { _phase = 0; return; }
            var car = Game.CarRoot;
            if (car == null) return;
            if (_startCar != car || _start == null)
            {
                _startCar = car; _start = null; _engine = null; _isRunning = null;
                foreach (var f in car.GetComponentsInChildren<PlayMakerFSM>(true))
                    if (f.FsmName == "Start" && f.name == "START" && HasState(f, "Ignition")) { _start = f; break; }
                if (_start == null) return;
                FindEngine(car);
                FindGameText();
                Plugin.Verbose("Ignition: " + Game.PathOf(_start.transform) + (_engine != null ? ", engine state from " + _engine.GetType().Name : ", engine state from the FSM only"));
            }
            if (!_start.enabled || _start.Fsm == null || !_start.Fsm.Initialized) return;
            string s = _start.ActiveStateName ?? "";
            bool running = EngineRunning(s);
            bool stopping = Array.IndexOf(Stopping, s) >= 0;
            if (!running && !stopping && !Game.Paused && _phase == 0) _hint = KeyName(Plugin.IgnitionKey.Value) + " - Start / Ignition";
            if (Game.Paused) return;

            if (_phase == 0 && Input.GetKeyDown(Plugin.IgnitionKey.Value))
            {
                if (running)
                {
                    if (s == "Start") return;   // still cranking
                    try { _start.Fsm.SetState("Stop"); Plugin.Verbose("Ignition: engine off (from " + s + ")"); } catch (Exception e) { Plugin.Warn("Ignition: " + e.Message); }
                    return;
                }
                if (stopping) return;
                if (!HasEngine(car)) { Plugin.Verbose("Ignition: no engine in this car"); return; }
                _phase = 1; _phaseUntil = Time.unscaledTime + 1.5f;
            }
            if (_phase == 0) return;
            if (Time.unscaledTime > _phaseUntil) { Plugin.Verbose("Ignition: gave up in state " + s); _phase = 0; return; }
            try
            {
                if (_phase == 1)
                {
                    if (s == "off" || s == "checkEngine" || s == "over" || s == "back" || s == "start") _start.Fsm.SetState("Ignition");   // key turned (click 1)
                    else if (s == "off2" || s == "over2") { _start.Fsm.SetState("Start"); _phase = 2; Plugin.Verbose("Ignition: starting"); }   // crank (click 2)
                }
                else if (_phase == 2 && s != "Ignition") _phase = 0;
            }
            catch (Exception e) { Plugin.Warn("Ignition: " + e.Message); _phase = 0; }
        }

        private static bool HasState(PlayMakerFSM f, string name)
        {
            try { foreach (var st in f.FsmStates) if (st.Name == name) return true; } catch (Exception) { }
            return false;
        }

        private static bool EngineRunning(string state)
        {
            if (_engine != null && _isRunning != null)
            {
                try { return (bool)_isRunning.GetValue(_engine, null) || state == "Start"; } catch (Exception) { }
            }
            if (Array.IndexOf(RunningStates, state) >= 0) return true;
            try { var b = _start.FsmVariables.GetFsmBool("engineIsRunning"); if (b != null && b.Value) return true; } catch (Exception) { }
            return false;
        }

        // NWH Vehicle Physics 2: VehicleController.powertrain.engine.IsRunning
        private static void FindEngine(Transform car)
        {
            try
            {
                foreach (var c in car.GetComponents<Component>())
                {
                    if (c == null || c.GetType().Name != "VehicleController") continue;
                    var pt = Member(c, "powertrain");
                    var en = pt != null ? Member(pt, "engine") : null;
                    if (en == null) return;
                    var p = en.GetType().GetProperty("IsRunning", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (p != null && p.PropertyType == typeof(bool)) { _engine = en; _isRunning = p; }
                    return;
                }
            }
            catch (Exception) { }
        }

        private static object Member(object o, string name)
        {
            var t = o.GetType();
            var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null) return f.GetValue(o);
            var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return p != null ? p.GetValue(o, null) : null;
        }

        // the Text the START FSM writes its hint into (UiTextSetText actions): the game's hint font
        private static void FindGameText()
        {
            if (_gameText != null) return;
            try
            {
                foreach (var st in _start.FsmStates)
                    foreach (var a in st.Actions)
                    {
                        if (a == null || a.GetType().Name != "UiTextSetText") continue;
                        var f = a.GetType().GetField("gameObject", BindingFlags.Instance | BindingFlags.Public);
                        var od = f != null ? f.GetValue(a) as FsmOwnerDefault : null;
                        var go = od != null ? _start.Fsm.GetOwnerDefaultTarget(od) : null;
                        var t = go != null ? go.GetComponent<Text>() : null;
                        if (t != null && t.font != null) { _gameText = t; _font = t.font; Plugin.Verbose("Ignition hint font: " + t.font.name + " (" + Game.PathOf(t.transform) + ")"); return; }
                    }
            }
            catch (Exception) { }
        }

        private static string KeyName(KeyCode k)
        {
            string n = k.ToString();
            return n.StartsWith("Alpha") ? n.Substring(5) : n;
        }

        private static bool HasEngine(Transform car)
        {
            var h = Game.FindDeep(car, "hinge_engine");
            return h == null || h.childCount > 0;
        }

        public static void OnGUI()
        {
            if (string.IsNullOrEmpty(_hint)) return;
            int size = Mathf.RoundToInt(Screen.height / 42f);
            if (_gameText != null && _gameText.canvas != null)
                size = Mathf.Max(10, Mathf.RoundToInt(_gameText.fontSize * _gameText.canvas.scaleFactor));
            var st = new GUIStyle(GUI.skin.label) { fontSize = size, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            if (_font != null) { st.font = _font; if (_gameText != null) st.fontStyle = _gameText.fontStyle; }
            var r = new Rect(Screen.width * 0.025f, Screen.height * 0.62f, Screen.width * 0.5f, size * 1.6f);
            st.normal.textColor = new Color(0f, 0f, 0f, 0.8f); GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), _hint, st);
            st.normal.textColor = _gameText != null ? new Color(_gameText.color.r, _gameText.color.g, _gameText.color.b, 1f) : Color.white;
            GUI.Label(r, _hint, st);
        }

        public static void Reset() { _start = null; _startCar = null; _phase = 0; _hint = ""; _gameText = null; _font = null; _engine = null; _isRunning = null; }
    }
}
