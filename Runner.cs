using System;
using UnityEngine;

namespace Apocaplayer
{
    internal class Runner : MonoBehaviour
    {
        private static Body _body;
        private static float _nextBuild;
        private static bool _wasEnabled;
        private static string _lastMode = "";
        private static Vector3 _lastVelocity;
        private static Transform _lastCar;
        internal static bool RagdollVisible { get { return _body != null && _body.IsRagdoll; } }
        internal static Vector3 DeathFocus { get { return _body.DeathFocus; } }
        internal static bool TryOcclusionPoints(out Vector3 head, out Vector3 feet)
        {
            if (_body != null && _body.Alive) return _body.TryOcclusionPoints(out head, out feet);
            head = feet = Vector3.zero; return false;
        }

        public static void OnSceneLoaded()
        {
            AutoStepUp.Reset();
            RustlinerDoors.Reset();
            DestroyBody();
            CarSeat.Detach();
            ThirdPerson.Off();
            Game.Reset();
            Arms.OnSceneLoaded();
            Car.Reset();
            InventoryModel.Reset();
            Props.OnSceneLoaded();
        }

        // Male / Female: rebuild the body from the other model; her arms texture and TAB picture only for her
        public static void CharacterChanged()
        {
            try
            {
                DestroyBody();
                Model.Reset();
                Arms.Restore();   // the next character's arms (or the game's for Male)
                InventoryModel.CharacterChanged();
                Plugin.Log.LogInfo("Character: " + Plugin.Character.Value);
            }
            catch (Exception e) { Plugin.Log.LogError("Character switch: " + e); }
        }

        private static void DestroyBody()
        {
            if (_body != null) _body.Destroy();
            _body = null;
            _lastVelocity = Vector3.zero;
            _lastCar = null;
        }

        private void Update()
        {
            try
            {
                bool on = Plugin.Enabled.Value;
                if (!on)
                {
                    RustlinerDoors.Restore();
                    AutoStepUp.Reset();
                    if (_wasEnabled) { DestroyBody(); CarSeat.Detach(); ThirdPerson.Off(); Arms.Restore(); InventoryModel.Off(); Plugin.Log.LogInfo("Disabled: the game's own player is back"); }
                    _wasEnabled = false;
                    return;
                }
                _wasEnabled = true;
                if (!Game.Ready) { RustlinerDoors.Restore(); OcclusionCutaway.Stop(); if (_body != null) DestroyBody(); return; }   // (2.2.6) the cutaway taken down in menus / loading too
                RustlinerDoors.Tick();
                if (!Game.Dead) { _lastVelocity = Game.Velocity; _lastCar = Game.InCar ? Game.CarRoot : null; }
                if (Plugin.FemaleArms.Value && (Plugin.Female || Plugin.IsMax)) Arms.Tick(); else Arms.Restore();
                ThirdPerson.Tick();
                Car.Tick();
                GunPose.ClearHint();                       // set again in LateUpdate while a weapon is being adjusted
                if (!ThirdPerson.On || Game.InCar) GunPose.Flush();
            }
            catch (Exception e) { Plugin.Log.LogError("Update: " + e); }
        }

        private void LateUpdate()
        {
            try
            {
                if (!Plugin.Enabled.Value || !Game.Ready) return;
                ThirdPerson.LateCrosshair();
                if (!Game.Dead && _body != null && _body.IsRagdoll) DestroyBody();
                if (_body == null || !_body.Alive)
                {
                    _body = null;
                    if (Time.unscaledTime < _nextBuild) return;
                    _nextBuild = Time.unscaledTime + 2f;
                    _body = Body.Create();
                    if (_body == null) return;
                }

                if (Game.Dead)
                {
                    _body.LateDead(_lastVelocity, _lastCar);
                    ThirdPerson.CarShift = false;
                    return;
                }
                bool inCar = Game.InCar;
                bool fpCam = Game.FirstPersonCameraOn;
                ThirdPerson.CarShift = false;
                string mode;
                bool fpArms = false;
                if (inCar)
                {
                    bool seated = CarSeat.Attach(Game.Player.transform);
                    if (!seated || !Plugin.ReplaceDriver.Value)
                    {
                        // no seat to copy (or the player wants the game's driver): no body in the car
                        if (!Plugin.ReplaceDriver.Value) CarSeat.Detach();
                        _body.SetVisible(false, false);
                        mode = "car (game driver)";
                    }
                    else
                    {
                        bool firstPerson = fpCam && (!ThirdPerson.On || ThirdPerson.Peek);   // binoculars: first person
                        _body.LateCar(Game.Player.transform, firstPerson);
                        if (firstPerson) _body.ShiftFirstPerson(Game.Player.transform.rotation, Plugin.CarViewBase + new Vector3(Plugin.FpCarCamX.Value, Plugin.FpCarCamY.Value, Plugin.FpCarCamZ.Value));
                        _body.SetMesh(firstPerson, true, Game.DrawnWeapon != "");
                        _body.SetVisible(!firstPerson || Plugin.BodyFirstPerson.Value, firstPerson && Plugin.BodyFirstPerson.Value);
                        ThirdPerson.CarShift = firstPerson && Plugin.BodyFirstPerson.Value && Plugin.CarCameraForward.Value != 0f;
                        // no body in first person: still her arms on the wheel while nothing is drawn (the body mesh has them when it is shown)
                        fpArms = firstPerson && fpCam && !Plugin.BodyFirstPerson.Value && Plugin.EmptyHandArms.Value && Game.DrawnWeapon == "" && !Game.Binoculars && !Game.GameArmsShown;
                        mode = firstPerson ? "car, first person" : "car, third person";
                    }
                }
                else
                {
                    CarSeat.Detach();
                    bool third = ThirdPerson.On && !ThirdPerson.Peek;   // binoculars raised: drawn as in first person
                    _body.LateFoot(third ? Body.View.ThirdPerson : Body.View.FirstPerson, Time.deltaTime);
                    if (!third && Game.PlayerCamera != null)
                        _body.ShiftFirstPerson(Quaternion.Euler(0f, Game.PlayerCamera.eulerAngles.y, 0f), new Vector3(Plugin.FpCamX.Value, Plugin.FpCamY.Value, Plugin.FpCamZ.Value));
                    _body.SetMesh(!third, false);
                    bool show = third || Plugin.BodyFirstPerson.Value;
                    _body.SetVisible(show && fpCam, !third && show && fpCam);
                    // first person, nothing in hand: the game draws no arms - show the body's own, animated like the rest of her
                    fpArms = !third && fpCam && Plugin.EmptyHandArms.Value && !Game.Binoculars && !Game.GameArmsShown;
                    mode = third ? "on foot, third person" : "on foot, first person";
                }
                _body.SetFirstPersonArms(fpArms);
                _body.LateEquipment();
                if (mode != _lastMode) { _lastMode = mode; Plugin.Verbose("View: " + mode); }
                InventoryModel.LateTick();
            }
            catch (Exception e) { Plugin.Log.LogError("LateUpdate: " + e); _nextBuild = Time.unscaledTime + 5f; }
        }

        private void Start() { StartCoroutine(EndOfFrameLoop()); }

        private System.Collections.IEnumerator EndOfFrameLoop()
        {
            var eof = new WaitForEndOfFrame();
            while (true)
            {
                yield return eof;
                try { ThirdPerson.EndOfFrame(); } catch (Exception) { }
            }
        }

        private void OnGUI() { try { GunPose.OnGUI(); Car.OnGUI(); } catch (Exception) { } }

        private void FixedUpdate() { try { AutoStepUp.Tick(); } catch (Exception e) { Plugin.Warn("Step up: " + e.Message); } }
        private void OnDestroy() { AutoStepUp.Reset(); RustlinerDoors.Reset(); ThirdPerson.Off(); }
    }
}
