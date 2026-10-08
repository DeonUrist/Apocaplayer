using System;
using System.Reflection;
using System.Text;
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
        private static PlayMakerFSM _lightOn, _lightOff, _handbrake, _radioOnOff, _play, _stop, _volume;
        private static AudioSource _radioAudio;
        private static float _nextControls, _startRetry;
        private static bool _running, _lightsOn, _brakeOn, _musicPlaying;

        private static readonly string[] RunningStates = { "Start", "wait", "off3", "over3" };
        private static readonly string[] Stopping = { "Stop", "wait2" };

        public static void Tick()
        {
            _hint = "";
            if (!Plugin.Enabled.Value || !Game.Ready || !Game.InCar || Game.Dead) { _phase = 0; return; }
            var car = Game.CarRoot;
            if (car == null) return;
            // (2.2.6) searched once per car (again every 5 s while nothing was found - parts can arrive late), not on every frame
            if (_startCar != car || (_start == null && Time.unscaledTime >= _startRetry))
            {
                bool newCar = _startCar != car;
                _startCar = car; _startRetry = Time.unscaledTime + 5f; _start = null; _engine = null; _isRunning = null;
                _phase = 0; _nextControls = 0f;
                _lightOn = _lightOff = _handbrake = _radioOnOff = _play = _stop = _volume = null;
                _radioAudio = null;
                foreach (var f in car.GetComponentsInChildren<PlayMakerFSM>(true))
                    if (f.FsmName == "Start" && f.name == "START" && HasState(f, "Ignition")) { _start = f; break; }
                FindEngine(car);
                if (_start != null) FindGameText();
                if (_start != null) Plugin.Verbose("Ignition: " + Game.PathOf(_start.transform) + (_engine != null ? ", engine state from " + _engine.GetType().Name : ", engine state from the FSM only"));
                if (newCar) Plugin.Log.LogInfo("Car: " + car.name + " - ignition " + (_start != null ? "found" : "not found (hotkey off)") + ", engine state " + (_engine != null ? "from the vehicle" : "from the FSM"));
            }
            if (Pressed(Plugin.HeadlightsKey.Value) || Pressed(Plugin.CassetteKey.Value) || Pressed(Plugin.VolumeDownKey.Value) || Pressed(Plugin.VolumeUpKey.Value))
                _nextControls = 0f;                           // a hotkey acts on the controls as they are now
            if (Time.unscaledTime >= _nextControls)
            {
                _nextControls = Time.unscaledTime + 2f;      // (2.2.6) was 0.5 s: three searches through the whole car
                FindControls(car);
            }
            if (Game.Paused) return;
            if (Pressed(Plugin.HeadlightsKey.Value))
                SendUse(Active(_lightOn) ? _lightOn : _lightOff);
            if (Pressed(Plugin.CassetteKey.Value) && _radioAudio != null && _radioAudio.clip != null)
            {
                bool playing = _radioOnOff != null && _radioOnOff.ActiveStateName == "on";
                var control = playing ? _stop : _play;
                if (control != null && control.gameObject.activeInHierarchy && control.Fsm.Initialized)
                {
                    if (!playing && _radioAudio.volume == 0f) SetVolume(0.5f);
                    // The game's Play/Stop FSMs disable one another. Re-enter the action
                    // explicitly so repeated hotkey presses also work with a dormant FSM.
                    control.enabled = true;
                    control.Fsm.SetState(playing ? "stop" : "play");
                }
            }
            if (Pressed(Plugin.VolumeDownKey.Value)) SetVolume(RadioVolume - 0.1f);
            if (Pressed(Plugin.VolumeUpKey.Value)) SetVolume(RadioVolume + 0.1f);
            UpdateHints();
            TickIgnition(car);
        }

        private static void TickIgnition(Transform car)
        {
            if (_start == null) return;
            if (!_start.enabled || _start.Fsm == null || !_start.Fsm.Initialized) return;
            string s = _start.ActiveStateName ?? "";
            bool running = EngineRunning(s);
            bool stopping = Array.IndexOf(Stopping, s) >= 0;
            if (_phase == 0 && Pressed(Plugin.IgnitionKey.Value))
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

        private static bool Active(PlayMakerFSM f) { return f != null && f.isActiveAndEnabled && f.Fsm != null && f.Fsm.Initialized; }
        private static bool Pressed(KeyCode key)
        {
            if (key == KeyCode.None) return false;
            if (Input.GetKeyDown(key)) return true;
            // Unity reports the physical = key for + on the main keyboard.
            if (key == KeyCode.Equals || key == KeyCode.Plus) return Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.Plus) || Input.GetKeyDown(KeyCode.KeypadPlus);
            if (key == KeyCode.Minus) return Input.GetKeyDown(KeyCode.KeypadMinus);
            return false;
        }
        private static void SendUse(PlayMakerFSM f) { if (Active(f)) f.SendEvent("useDoor"); }

        private static void FindControls(Transform car)
        {
            _lightOn = _lightOff = _handbrake = _radioOnOff = _play = _stop = _volume = null;
            _radioAudio = null;
            var lights = Game.FindDeep(car, "switch_lights");
            if (lights != null) foreach (var f in lights.GetComponents<PlayMakerFSM>())
            {
                if (f.FsmName == "LightOn") _lightOn = f;
                if (f.FsmName == "LightOff") _lightOff = f;
            }
            var brake = Game.FindDeep(car, "handbrake");
            if (brake != null) foreach (var f in brake.GetComponents<PlayMakerFSM>()) if (f.FsmName == "Handbrake") _handbrake = f;
            var radio = Game.FindDeep(car, "hinge_radio");
            if (radio == null) return;
            foreach (var f in radio.GetComponentsInChildren<PlayMakerFSM>(true))
                if (f.FsmName == "OnOff" && f.gameObject.activeInHierarchy && f.GetComponent<AudioSource>() != null)
                { _radioOnOff = f; _radioAudio = f.GetComponent<AudioSource>(); break; }
            if (_radioOnOff == null) return;
            foreach (var f in _radioOnOff.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                if (f.name == "PlayStop" && f.FsmName == "Play") _play = f;
                if (f.name == "PlayStop" && f.FsmName == "Stop") _stop = f;
                if (f.name == "Volume" && f.FsmName == "Volume") _volume = f;
            }
        }

        private static float RadioVolume { get { return _radioAudio != null ? _radioAudio.volume : 0f; } }
        private static void SetVolume(float value)
        {
            if (_radioAudio == null) return;
            float v = Mathf.Clamp01(Mathf.Round(value * 10f) / 10f);
            _radioAudio.volume = v;
            var variable = _volume != null ? _volume.FsmVariables.FindFsmFloat("audioVolume") : null;
            if (variable != null) variable.Value = v;
        }

        private static string VolumeText { get { return RadioVolume.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture); } }
        private static void AddHint(StringBuilder hints, KeyCode key, string text)
        {
            if (key == KeyCode.None) return;
            if (hints.Length > 0) hints.Append('\n');
            hints.Append(KeyName(key)).Append(" - ").Append(text);
        }
        private static void UpdateHints()
        {
            _running = _start != null && EngineRunning(_start.ActiveStateName ?? "");
            // LightOn is the action enabled when lights are ON (its useDoor switches them off).
            _lightsOn = Active(_lightOn);
            string brake = _handbrake != null ? _handbrake.ActiveStateName : "";
            _brakeOn = brake == "HandbrakeOn" || brake == "over" || brake == "Sound 2";
            _musicPlaying = _radioAudio != null && _radioAudio.isPlaying && _radioAudio.pitch > 0f;
            if (!Plugin.VehicleHotkeyHint.Value) { _hint = ""; return; }
            var hints = new StringBuilder();
            if (_start != null) AddHint(hints, Plugin.IgnitionKey.Value, _running ? "Ignition Stop" : "Ignition");
            if (_lightOn != null || _lightOff != null) AddHint(hints, Plugin.HeadlightsKey.Value, _lightsOn ? "Headlights Off" : "Headlights On");
            if (_radioAudio != null)
            {
                bool playing = _radioOnOff != null && _radioOnOff.ActiveStateName == "on";
                AddHint(hints, Plugin.CassetteKey.Value, playing ? "Cassette Stop" : "Cassette Start");
                AddHint(hints, Plugin.VolumeDownKey.Value, "Volume Down");
                AddHint(hints, Plugin.VolumeUpKey.Value, "Volume Up");
            }
            _hint = hints.ToString();
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
            if (k == KeyCode.Equals || k == KeyCode.Plus) return "+";
            if (k == KeyCode.Minus) return "-";
            string n = k.ToString();
            return n.StartsWith("Alpha") ? n.Substring(5) : n;
        }

        private static bool HasEngine(Transform car)
        {
            var h = Game.FindDeep(car, "hinge_engine");
            return h == null || h.childCount > 0;
        }

        private static GUIStyle _hintStyle;
        public static void OnGUI()
        {
            if (!Plugin.Enabled.Value || !Game.Ready || !Game.InCar || Game.Dead || Game.Paused) return;
            if (Plugin.VehicleStatusHint.Value) VehicleHud.Draw(_start != null && !_running, _brakeOn, _musicPlaying, VolumeText);
            if (!Plugin.VehicleHotkeyHint.Value || string.IsNullOrEmpty(_hint)) return;
            int size = Mathf.RoundToInt(Screen.height / 42f);
            if (_gameText != null && _gameText.canvas != null)
                size = Mathf.Max(10, Mathf.RoundToInt(_gameText.fontSize * _gameText.canvas.scaleFactor));
            if (_hintStyle == null) _hintStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, wordWrap = false };   // (2.2.6) reused
            var st = _hintStyle; st.fontSize = size;
            st.font = _font; st.fontStyle = _font != null && _gameText != null ? _gameText.fontStyle : FontStyle.Normal;
            var r = new Rect(Screen.width * 0.025f, Screen.height * 0.62f, Screen.width * 0.5f, size * 8f);
            st.normal.textColor = new Color(0f, 0f, 0f, 0.8f); GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), _hint, st);
            st.normal.textColor = _gameText != null ? new Color(_gameText.color.r, _gameText.color.g, _gameText.color.b, 1f) : Color.white;
            GUI.Label(r, _hint, st);
        }

        public static void Reset()
        {
            _start = null; _startCar = null; _phase = 0; _hint = ""; _gameText = null; _font = null; _engine = null; _isRunning = null;
            _lightOn = _lightOff = _handbrake = _radioOnOff = _play = _stop = _volume = null;
            _radioAudio = null; _nextControls = 0f;
            _running = _lightsOn = _brakeOn = _musicPlaying = false;
        }
    }
}
