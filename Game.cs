using System;
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
        public static Transform WeaponsParent;      // PlayerCamera/WeaponsArm/Parent: one child per first-person weapon, the drawn one active
        private static float _nextFind;

        public static void Reset() { Player = null; InCarFsm = MovementFsm = null; CameraHolder = PlayerCamera = WeaponsParent = null; Cam = null; _nextFind = 0f; }

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
                        Plugin.Verbose("Found the PlayerCamera" + (WeaponsParent != null ? " and WeaponsArm/Parent" : " (no WeaponsArm/Parent!)"));
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

        public static bool Paused { get { return Time.timeScale < 0.01f; } }

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
