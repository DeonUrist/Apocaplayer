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

        public static void OnSceneLoaded()
        {
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
                if (!Plugin.Female) { Arms.Restore(); InventoryModel.Off(); } else InventoryModel.Reset();
                Plugin.Log.LogInfo("Character: " + Plugin.Character.Value);
            }
            catch (Exception e) { Plugin.Log.LogError("Character switch: " + e); }
        }

        private static void DestroyBody()
        {
            if (_body != null) _body.Destroy();
            _body = null;
        }

        private void Update()
        {
            try
            {
                bool on = Plugin.Enabled.Value;
                if (!on)
                {
                    if (_wasEnabled) { DestroyBody(); CarSeat.Detach(); ThirdPerson.Off(); Arms.Restore(); InventoryModel.Off(); Plugin.Log.LogInfo("Disabled: the game's own player is back"); }
                    _wasEnabled = false;
                    return;
                }
                _wasEnabled = true;
                if (!Game.Ready) { if (_body != null) DestroyBody(); return; }
                if (Plugin.FemaleArms.Value && Plugin.Female) Arms.Tick(); else Arms.Restore();
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
                if (_body == null || !_body.Alive)
                {
                    _body = null;
                    if (Time.unscaledTime < _nextBuild) return;
                    _nextBuild = Time.unscaledTime + 2f;
                    _body = Body.Create();
                    if (_body == null) return;
                }

                bool inCar = Game.InCar;
                bool fpCam = Game.FirstPersonCameraOn;
                ThirdPerson.CarShift = false;
                string mode;
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
                        bool firstPerson = fpCam && !ThirdPerson.On;
                        _body.LateCar(Game.Player.transform, firstPerson);
                        _body.SetMesh(firstPerson, true, Game.DrawnWeapon != "");
                        _body.SetVisible(!firstPerson || Plugin.BodyFirstPerson.Value, firstPerson && Plugin.BodyFirstPerson.Value);
                        ThirdPerson.CarShift = firstPerson && Plugin.BodyFirstPerson.Value && Plugin.CarCameraForward.Value != 0f;
                        mode = firstPerson ? "car, first person" : "car, third person";
                    }
                }
                else
                {
                    CarSeat.Detach();
                    bool third = ThirdPerson.On;
                    _body.LateFoot(third ? Body.View.ThirdPerson : Body.View.FirstPerson, Time.deltaTime);
                    _body.SetMesh(!third, false);
                    bool show = third || Plugin.BodyFirstPerson.Value;
                    _body.SetVisible(show && fpCam, !third && show && fpCam);
                    mode = third ? "on foot, third person" : "on foot, first person";
                }
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

        private void OnDestroy() { ThirdPerson.Off(); }
    }
}
