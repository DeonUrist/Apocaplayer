using System.Collections.Generic;
using UnityEngine;

namespace FemalePlayer
{
    // The TAB screen (PlayerSheet: ammo, bosses killed ...) shows a player model in the middle: the game's 27-bone Player2 man (the same rig
    // and atlas as the seated car driver). His renderers are hidden and a second copy of her body takes his place, posed from his bones every
    // frame with the car-seat retarget (CarSeat.Pairs), on his layer (the screen's own camera) and at his size.
    internal static class InventoryModel
    {
        private static Transform _model;          // the man's root (parent of his mesh and armature)
        private static Transform[] _src;
        private static int _layer;
        private static Body _her;
        private static readonly List<Renderer> _hidden = new List<Renderer>();
        private static float _nextScan;
        private static bool _logged;

        public static void LateTick()
        {
            if (!Plugin.Enabled.Value || !Plugin.ReplaceDriver.Value) { Off(); return; }
            if (_model == null) { Find(); if (_model == null) return; }
            bool shown = _model.gameObject.activeInHierarchy;
            if (!shown) { if (_her != null && _her.Alive) _her.SetVisible(false, false); return; }
            if (_her == null || !_her.Alive) { _her = Body.Create(); if (_her == null) return; _her.Root.name = "FemalePlayerSheetModel"; }
            foreach (var r in _model.GetComponentsInChildren<Renderer>(true))
                if (r.enabled && !_hidden.Contains(r)) { r.enabled = false; _hidden.Add(r); }
            _her.LateMirror(_src, _layer);
            _her.SetVisible(true, false);
        }

        // an active Player2 skinned man outside the first-person camera and outside any car, with the seated driver's bones
        private static void Find()
        {
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + 1f;
            foreach (var smr in Object.FindObjectsOfType<SkinnedMeshRenderer>())
            {
                if (smr == null || smr.sharedMaterial == null || !smr.sharedMaterial.name.StartsWith("Player2")) continue;
                var t = smr.transform;
                if (Game.CameraHolder != null && t.IsChildOf(Game.CameraHolder)) continue;
                if (t.root.name.StartsWith("FemalePlayer")) continue;
                bool inCar = false;
                for (var p = t; p != null; p = p.parent) if (p.name == "PlayerModel_Sit" || p.Find("DriveTrigger") != null) { inCar = true; break; }
                if (inCar) continue;
                var root = smr.rootBone != null && smr.rootBone.parent != null ? CommonParent(t, smr.rootBone) : t.parent;
                if (root == null) continue;
                var src = CarSeat.FindSource(root);
                if (src == null) { if (!_logged) Plugin.Verbose("TAB model candidate without the driver's bones: " + Path(t)); continue; }
                _model = root; _src = src; _layer = smr.gameObject.layer;
                Plugin.Log.LogInfo("TAB screen player model found: " + Path(t) + " (layer " + _layer + ") - she replaces it");
                return;
            }
            _logged = true;
        }

        private static Transform CommonParent(Transform a, Transform b)
        {
            for (var p = a.parent; p != null; p = p.parent) if (b.IsChildOf(p)) return p;
            return null;
        }

        private static string Path(Transform t)
        {
            string s = t.name;
            for (var p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
            return s;
        }

        public static void Off()
        {
            foreach (var r in _hidden) if (r != null) r.enabled = true;
            _hidden.Clear();
            if (_her != null && _her.Alive) _her.SetVisible(false, false);
        }

        public static void Reset()
        {
            Off();
            if (_her != null) { _her.Destroy(); _her = null; }
            _model = null; _src = null; _nextScan = 0f; _logged = false;
        }
    }
}
