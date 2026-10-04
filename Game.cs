using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;

namespace FemalePlayer
{
    // References to the game's player objects, re-found after a scene load / when lost.
    internal static class Game
    {
        public static GameObject Player;            // root "Player" (capsule, Movement/InCar FSMs); parented under the car's sitPos while driving
        public static PlayMakerFSM InCarFsm, MovementFsm;
        public static Transform CameraHolder, PlayerCamera;
        public static Camera Cam;
        public static Transform WeaponsParent;
        public static PlayMakerFSM GrenadeFsm;      // PlayerCamera/QuickItems/grenade [Attack]: on -> (Throw Grenade) -> checkGrenade -> fire 0.4 s -> wait 0.35 s -> throw      // PlayerCamera/WeaponsArm/Parent: one child per first-person weapon, the drawn one active
        private static float _nextFind;

        public static void Reset() { _jumpFsm = _kickFsm = null; _reloadFsms.Clear(); _arms.Clear(); _attackFsms.Clear(); GrenadeFsm = null; Player = null; InCarFsm = MovementFsm = null; CameraHolder = PlayerCamera = WeaponsParent = null; Cam = null; _nextFind = 0f; }

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
        private static bool IsReload(AnimatorStateInfo s) { return s.IsName("reload") || s.IsName("Reload") || s.IsName("Base Layer.reload") || s.IsName("Base Layer.Reload"); }

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
        public static Transform CarRoot
        {
            get
            {
                if (Player == null) return null;
                for (var t = Player.transform.parent; t != null; t = t.parent) if (t.Find("DriveTrigger") != null) return t;
                return null;
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
