using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;

namespace Apocaplayer
{
    // References to the game's player objects, re-found after a scene load / when lost.
    internal static class Game
    {
        public static GameObject Player;            // root "Player" (capsule, Movement/InCar FSMs); parented under the car's sitPos while driving
        public static PlayMakerFSM InCarFsm, MovementFsm;
        private static PlayMakerFSM _healthFsm;
        public static Transform CameraHolder, PlayerCamera;
        public static Camera Cam;
        public static Transform WeaponsParent;
        public static PlayMakerFSM GrenadeFsm;      // PlayerCamera/QuickItems/grenade [Attack]: on -> (Throw Grenade) -> checkGrenade -> fire 0.4 s -> wait 0.35 s -> throw      // PlayerCamera/WeaponsArm/Parent: one child per first-person weapon, the drawn one active
        private static float _nextFind;

        public static void Reset() { _jumpFsm = _kickFsm = null; _reloadFsms.Clear(); _perRound.Clear(); _arms.Clear(); _attackFsms.Clear(); _adsFsm = null; GrenadeFsm = null; Player = null; InCarFsm = MovementFsm = null; CameraHolder = PlayerCamera = WeaponsParent = null; Cam = null; _nextFind = 0f; }

        public static bool Ready
        {
            get
            {
                if (Player != null && InCarFsm != null && PlayerCamera != null) return true;
                if (Time.unscaledTime < _nextFind) return false;
                _nextFind = Time.unscaledTime + 1f;
                Find();
                return Player != null && InCarFsm != null && PlayerCamera != null;
            }
        }

        private static void Find()
        {
            if (Player == null || InCarFsm == null)
            {
                Player = null; InCarFsm = MovementFsm = null;
                var go = GameObject.Find("Player");
                if (go != null)
                    foreach (var f in go.GetComponents<PlayMakerFSM>())
                    {
                        if (f.FsmName == "InCar") InCarFsm = f;
                        else if (f.FsmName == "Movement") MovementFsm = f;
                    }
                if (InCarFsm != null) { Player = go; Plugin.Verbose("Found the Player"); }
            }
            if (PlayerCamera == null)
            {
                var holder = GameObject.Find("PlayerCameraHolder");
                if (holder != null)
                {
                    CameraHolder = holder.transform;
                    PlayerCamera = CameraHolder.Find("PlayerCamera");
                    if (PlayerCamera != null)
                    {
                        Cam = PlayerCamera.GetComponent<Camera>();
                        var wa = PlayerCamera.Find("WeaponsArm");
                        WeaponsParent = wa != null ? wa.Find("Parent") : null;
                        var gr = PlayerCamera.Find("QuickItems/grenade");
                        if (gr != null) foreach (var f in gr.GetComponents<PlayMakerFSM>()) if (f.FsmName == "Attack") GrenadeFsm = f;
                        Plugin.Verbose("Found the PlayerCamera" + (GrenadeFsm != null ? " (+ grenade)" : "") + (WeaponsParent != null ? " and WeaponsArm/Parent" : " (no WeaponsArm/Parent!)"));
                    }
                }
            }
        }

        // The game dies below 0.4 health, then spends five seconds in playerDeath.
        public static bool Dead
        {
            get
            {
                if (Player == null) return false;
                if (_healthFsm == null || _healthFsm.gameObject != Player)
                {
                    _healthFsm = null;
                    foreach (var f in Player.GetComponents<PlayMakerFSM>())
                        if (f.FsmName == "Health") { _healthFsm = f; break; }
                }
                if (_healthFsm == null || !_healthFsm.Fsm.Initialized) return false;
                var health = _healthFsm.FsmVariables.FindFsmFloat("Health");
                return _healthFsm.ActiveStateName == "playerDeath" || _healthFsm.ActiveStateName == "backToMenu"
                    || health != null && health.Value < 0.4f;
            }
        }

        public static bool InCar
        {
            get
            {
                try { return InCarFsm != null && InCarFsm.ActiveStateName == "InCar"; }
                catch (Exception) { return false; }
            }
        }

        // Standing / Running / Crouching / Crawling (+ checkAbove*)
        public static string MoveState
        {
            get
            {
                try { return MovementFsm != null && MovementFsm.enabled ? MovementFsm.ActiveStateName : ""; }
                catch (Exception) { return ""; }
            }
        }

        // the capsule height the Movement FSM tweens to (1.7 standing, 1.0 crouched, 0.56 crawling)
        public static float StandingHeight
        {
            get
            {
                if (MovementFsm == null) return 1.7f;
                var v = MovementFsm.FsmVariables.GetFsmFloat("standingHeight");
                return v != null && v.Value > 0.1f ? v.Value : 1.7f;
            }
        }

        public static Vector3 Velocity
        {
            get
            {
                if (Player == null) return Vector3.zero;
                var rb = Player.GetComponent<Rigidbody>();
                return rb != null ? rb.velocity : Vector3.zero;
            }
        }

        // the drawn first-person weapon (name of the active child of WeaponsArm/Parent), "" when none
        public static string DrawnWeapon
        {
            get
            {
                if (WeaponsParent == null || !WeaponsParent.gameObject.activeInHierarchy) return "";
                for (int i = 0; i < WeaponsParent.childCount; i++)
                {
                    var c = WeaponsParent.GetChild(i);
                    if (c.gameObject.activeSelf && c.name != "kick") return c.name;
                }
                return "";
            }
        }

        // the first-person camera is rendering (false while the car's third-person camera is on: the game deactivates the PlayerCamera hierarchy)
        public static bool FirstPersonCameraOn { get { return Cam != null && Cam.isActiveAndEnabled; } }

        private static PlayMakerFSM _jumpFsm, _kickFsm;
        private static readonly Dictionary<string, PlayMakerFSM[]> _reloadFsms = new Dictionary<string, PlayMakerFSM[]>();

        // Player [Jump]: Idle -> (Jump button) Jump -> Grounded -> Idle; Falling when walking off an edge
        public static string JumpState
        {
            get
            {
                try
                {
                    if (_jumpFsm == null && Player != null) foreach (var f in Player.GetComponents<PlayMakerFSM>()) if (f.FsmName == "Jump") _jumpFsm = f;
                    return _jumpFsm != null && _jumpFsm.enabled ? _jumpFsm.ActiveStateName : "";
                }
                catch (Exception) { return ""; }
            }
        }

        // WeaponsArm/Parent/kick [Attack]: on -> (Kick button) fire 0.55 s -> wait -> on
        public static string KickState
        {
            get
            {
                try
                {
                    if (_kickFsm == null && WeaponsParent != null)
                    {
                        var k = WeaponsParent.Find("kick");
                        if (k != null) foreach (var f in k.GetComponents<PlayMakerFSM>()) if (f.FsmName == "Attack") _kickFsm = f;
                    }
                    return _kickFsm != null && _kickFsm.gameObject.activeInHierarchy ? _kickFsm.ActiveStateName : "";
                }
                catch (Exception) { return ""; }
            }
        }

        // the drawn gun really reloads: its [ReloadAnimation] FSM is "on" (akms etc.), or its [Reload] FSM is past idle/checkAmmo
        public static bool IsReloading(string weapon)
        {
            if (string.IsNullOrEmpty(weapon) || WeaponsParent == null) return false;
            try
            {
                PlayMakerFSM[] f;
                if (!_reloadFsms.TryGetValue(weapon, out f) || f[0] == null && f[1] == null)
                {
                    f = new PlayMakerFSM[2];
                    var w = WeaponsParent.Find(weapon);
                    if (w != null) foreach (var x in w.GetComponents<PlayMakerFSM>()) { if (x.FsmName == "ReloadAnimation") f[0] = x; else if (x.FsmName == "Reload") f[1] = x; }
                    _reloadFsms[weapon] = f;
                }
                if (f[0] != null) { string s = f[0].ActiveStateName; if (s != "off" && !string.IsNullOrEmpty(s)) return true; }
                if (f[1] != null) { string s = f[1].ActiveStateName; return !string.IsNullOrEmpty(s) && s != "idle" && s != "checkAmmo" && s != f[1].Fsm.StartState; }
            }
            catch (Exception) { }
            return false;
        }

        // the drawn weapon's first-person arms play their reload: Animator (WeaponsArm/Parent/<weapon>/...) in a state named "reload" (the
        // ReloadAnimation FSM's AnimatorPlay) or playing a clip whose name contains "reload". known = there is an active Animator to ask.
        private static readonly Dictionary<string, Animator> _arms = new Dictionary<string, Animator>();

        // any of the game's first-person arms (material Player2*, under the camera holder; not the kick leg) is drawn this frame.
        // The renderer list is refreshed every second (weapons/items create theirs when first drawn).
        private static readonly List<Renderer> _fpArmRenderers = new List<Renderer>();
        private static float _fpArmsScan;
        private static Transform _fpArmsHolder;
        public static bool GameArmsShown
        {
            get
            {
                if (CameraHolder == null) return false;
                if (Time.unscaledTime >= _fpArmsScan || _fpArmsHolder != CameraHolder)
                {
                    _fpArmsScan = Time.unscaledTime + 1f;
                    _fpArmsHolder = CameraHolder;
                    _fpArmRenderers.Clear();
                    foreach (var r in CameraHolder.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        if (r == null || r.sharedMaterial == null || !r.sharedMaterial.name.StartsWith("Player2")) continue;
                        if (r.name.IndexOf("leg", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        _fpArmRenderers.Add(r);
                    }
                }
                for (int i = 0; i < _fpArmRenderers.Count; i++)
                {
                    var r = _fpArmRenderers[i];
                    if (r != null && r.enabled && r.gameObject.activeInHierarchy) return true;
                }
                return false;
            }
        }
        public static bool ArmsReloading(string weapon, out bool known)
        {
            known = false;
            if (string.IsNullOrEmpty(weapon) || WeaponsParent == null) return false;
            try
            {
                Animator an;
                if (!_arms.TryGetValue(weapon, out an) || an == null)
                {
                    var w = WeaponsParent.Find(weapon);
                    an = w != null ? w.GetComponentInChildren<Animator>(true) : null;
                    _arms[weapon] = an;
                }
                if (an == null || !an.isActiveAndEnabled || an.runtimeAnimatorController == null) return false;
                known = true;
                if (IsReload(an.GetCurrentAnimatorStateInfo(0))) return true;
                if (an.IsInTransition(0) && IsReload(an.GetNextAnimatorStateInfo(0))) return true;
                foreach (var ci in an.GetCurrentAnimatorClipInfo(0))
                    if (ci.clip != null && ci.weight > 0.5f && ci.clip.name.IndexOf("reload", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            catch (Exception) { }
            return false;
        }
        private static bool IsReload(AnimatorStateInfo s) { return ReloadPhaseOf(s) != 0; }
        // (2.1.8) the arms' reload states: "reload" (one magazine / one round), and for the break-action / bolt guns (rochester_m24, redmark_m11)
        // "reload 1" (open), "reload 2" (one round, again per round), "reload 3" / "reload 4" (close). 1 = opening, 2 = loading, 3 = closing
        private static int ReloadPhaseOf(AnimatorStateInfo s)
        {
            if (s.IsName("reload") || s.IsName("Reload") || s.IsName("Base Layer.reload") || s.IsName("Base Layer.Reload") || s.IsName("reload 2") || s.IsName("Base Layer.reload 2")) return 2;
            if (s.IsName("reload 1") || s.IsName("Base Layer.reload 1")) return 1;
            if (s.IsName("reload 3") || s.IsName("Base Layer.reload 3") || s.IsName("reload 4") || s.IsName("Base Layer.reload 4")) return 3;
            return 0;
        }
        // the drawn weapon's arms in a reload state: which phase, and how far through it (0..1)
        public static bool ArmsReloadPhase(string weapon, out int phase, out float norm)
        {
            phase = 0; norm = 0f;
            bool known;
            ArmsReloading(weapon, out known);
            Animator an;
            if (!known || !_arms.TryGetValue(weapon, out an) || an == null) return false;
            try
            {
                var s = an.GetCurrentAnimatorStateInfo(0);
                phase = ReloadPhaseOf(s);
                if (phase == 0 && an.IsInTransition(0)) { s = an.GetNextAnimatorStateInfo(0); phase = ReloadPhaseOf(s); }
                norm = Mathf.Clamp01(s.normalizedTime);
                return phase != 0;
            }
            catch (Exception) { return false; }
        }
        // the gun reloads one round at a time: its [Reload] FSM loops reload -> checkAmmoInStore -> the arms' round animation (revolvers,
        // shotguns, the bolt rifle, the double barrel); magazines don't have that loop
        private static readonly Dictionary<string, bool> _perRound = new Dictionary<string, bool>();
        public static bool ReloadsPerRound(string weapon)
        {
            if (string.IsNullOrEmpty(weapon)) return false;
            bool r;
            if (_perRound.TryGetValue(weapon, out r)) return r;
            IsReloading(weapon);
            PlayMakerFSM[] f;
            r = false;
            try
            {
                if (_reloadFsms.TryGetValue(weapon, out f) && f[1] != null)
                    foreach (var st in f[1].FsmStates) if (st.Name == "checkAmmoInStore") { r = true; break; }
            }
            catch (Exception) { }
            if (WeaponsParent != null && WeaponsParent.Find(weapon) != null) _perRound[weapon] = r;
            return r;
        }

        // right mouse button (the game's "Aim Down Sights"): PlayerCamera [AimDownSIghts_Hold] sits in "ads" while it is held (it sends
        // AimDownSights_ON to the drawn weapon: the first-person gun slides to the sights and MouseCrosshair is switched off; scoped
        // weapons also set the camera FOV to 25 and show their scope overlay)
        private static PlayMakerFSM _adsFsm;
        public static bool AimDownSights
        {
            get
            {
                try
                {
                    if (_adsFsm == null && PlayerCamera != null)
                        foreach (var f in PlayerCamera.GetComponents<PlayMakerFSM>()) if (f.FsmName == "AimDownSIghts_Hold" || f.FsmName == "AimDownSights_Hold") { _adsFsm = f; break; }
                    return _adsFsm != null && _adsFsm.enabled && _adsFsm.ActiveStateName == "ads";
                }
                catch (Exception) { return false; }
            }
        }

        // binoculars raised: PlayerCamera/ItemAnim/Binocular Anim [Animation] off -(Activate)-> animOn (raise, 0.4 s) -> on (arms off,
        // BinocularUI + Binocular_Effect on, SetCameraFOV 20 every frame) -(Deactivate)-> animOff -> off
        private static PlayMakerFSM _binoFsm;
        public static bool BinocularInUse
        {
            get
            {
                bool raised = Binoculars; // also resolves the animation FSM
                string state = _binoFsm != null && _binoFsm.enabled ? _binoFsm.ActiveStateName : "";
                return raised || state == "animOn" || state == "animOff";
            }
        }
        public static bool Binoculars
        {
            get
            {
                try
                {
                    if (_binoFsm == null && PlayerCamera != null)
                    {
                        var t = PlayerCamera.Find("ItemAnim/Binocular Anim");
                        if (t != null) foreach (var f in t.GetComponents<PlayMakerFSM>()) if (f.FsmName == "Animation") { _binoFsm = f; break; }
                    }
                    return _binoFsm != null && _binoFsm.enabled && _binoFsm.ActiveStateName == "on";
                }
                catch (Exception) { return false; }
            }
        }

        public static string GrenadeState
        {
            get
            {
                try { return GrenadeFsm != null && GrenadeFsm.gameObject.activeInHierarchy ? GrenadeFsm.ActiveStateName : ""; }
                catch (Exception) { return ""; }
            }
        }

        public static bool Paused { get { return Time.timeScale < 0.01f; } }

        // melee / bare hands: the drawn weapon's [Attack] FSM (on -> fire: SphereCast = the hit, at once; then its float "Time" (0.35 hands,
        // 0.4 machete ...) counts down to the next swing; a held Fire button goes straight on to the next one)
        private static readonly Dictionary<string, PlayMakerFSM> _attackFsms = new Dictionary<string, PlayMakerFSM>();
        public static bool Attack(string weapon, out string state, out float time)
        {
            state = ""; time = 0f;
            if (string.IsNullOrEmpty(weapon) || WeaponsParent == null) return false;
            try
            {
                PlayMakerFSM f;
                if (!_attackFsms.TryGetValue(weapon, out f) || f == null)
                {
                    f = null;
                    var w = WeaponsParent.Find(weapon);
                    if (w != null) foreach (var c in w.GetComponents<PlayMakerFSM>()) if (c.FsmName == "Attack") { f = c; break; }
                    _attackFsms[weapon] = f;
                }
                if (f == null || !f.gameObject.activeInHierarchy) return false;
                state = f.ActiveStateName ?? "";
                var v = f.FsmVariables.GetFsmFloat("Time");
                time = v != null ? v.Value : 0f;
                return true;
            }
            catch (Exception) { return false; }
        }

        // the FSM <name> on the DriveTrigger of the car she sits in (the Player rides under the car's sitPos): Camera (1st / 3rd), Drive (exit)
        public static PlayMakerFSM CarFsm(string name)
        {
            if (Player == null) return null;
            for (var t = Player.transform.parent; t != null; t = t.parent)
            {
                var dt = t.Find("DriveTrigger");
                if (dt == null) continue;
                foreach (var f in dt.GetComponents<PlayMakerFSM>()) if (f.FsmName == name) return f;
                return null;
            }
            return null;
        }

        // the car she sits in (the ancestor of the Player with a DriveTrigger child)
        private static int _carRootFrame = -1; private static Transform _carRoot; private static GameObject _carRootPlayer;
        public static Transform CarRoot
        {
            get
            {
                if (Player == null) return null;
                if (_carRootFrame == Time.frameCount && _carRootPlayer == Player) return _carRoot;     // (2.2.6) asked many times a frame
                _carRootFrame = Time.frameCount; _carRootPlayer = Player; _carRoot = null;
                for (var t = Player.transform.parent; t != null; t = t.parent) if (t.Find("DriveTrigger") != null) { _carRoot = t; break; }
                return _carRoot;
            }
        }

        // PlayerCamera [DriveUse]: the game's own enter/exit (Use while looking at the car's trigger, first person only)
        public static PlayMakerFSM DriveUse
        {
            get
            {
                if (PlayerCamera == null) return null;
                foreach (var f in PlayerCamera.GetComponents<PlayMakerFSM>()) if (f.FsmName == "DriveUse") return f;
                return null;
            }
        }

        public static string PathOf(Transform t)
        {
            if (t == null) return "";
            string p = t.name;
            for (var x = t.parent; x != null; x = x.parent) p = x.name + "/" + p;
            return p;
        }

        public static Transform FindDeep(Transform t, string name)
        {
            if (t == null) return null;
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var r = FindDeep(t.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }
    }
}
