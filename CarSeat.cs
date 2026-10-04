using System.Collections.Generic;
using UnityEngine;

namespace FemalePlayer
{
    // Her in the driver's seat. Every car has a "PlayerModel_Sit" (shown by the car's Camera FSM in third person): the game's 27-bone
    // seated man (playermodel = mesh Player2) + Hair/Beard/bags. Its bones hold a static driving pose. We hide its renderers and copy
    // that pose onto her mixamo skeleton every frame:  herBone.rotation = itsBone.rotation * Delta,  her hips at itsSpine1 * Offset.
    // Delta/Offset were computed offline from the two bind poses (Player2 mesh <-> Flexa's Bodyguard.001; both T-poses; mesh spaces
    // related by (x, y, z) -> (x, z, -y)), with a swing so each of her bones points exactly along its partner. See tools/retarget.py.
    internal static class CarSeat
    {
        internal struct Pair
        {
            public string Src, Dst; public Quaternion Rot; public Vector3 Off;
            public Pair(string src, string dst, Quaternion rot, Vector3 off) { Src = src; Dst = dst; Rot = rot; Off = off; }
        }

        internal static readonly Pair[] Pairs =
        {
            new Pair("spine1", "mixamorig:Hips", new Quaternion(0.0588262f, 0.0002647f, -0.0000285f, 0.9982682f), new Vector3(-0.004261f, 0.038996f, 0.010125f)),
            new Pair("spine2", "mixamorig:Spine", new Quaternion(0.0000004f, 0.0000659f, 0.0000085f, 1.0000000f), new Vector3(-0.003649f, 0.033171f, -0.014620f)),
            new Pair("spin3", "mixamorig:Spine1", new Quaternion(0.0321937f, -0.0000836f, 0.0013113f, 0.9994808f), new Vector3(-0.003337f, -0.025799f, -0.032872f)),
            new Pair("spin3", "mixamorig:Spine2", new Quaternion(0.0218082f, -0.0001224f, 0.0020929f, 0.9997600f), new Vector3(-0.003904f, 0.109321f, -0.026974f)),
            new Pair("neck", "mixamorig:Neck", new Quaternion(-0.1993415f, -0.0000021f, 0.0000121f, 0.9799301f), new Vector3(-0.003905f, -0.042222f, -0.017992f)),
            new Pair("head", "mixamorig:Head", new Quaternion(-0.0450353f, -0.0000000f, 0.0000009f, 0.9989854f), new Vector3(-0.003903f, -0.038649f, -0.035616f)),
            new Pair("arm1 Left", "mixamorig:LeftShoulder", new Quaternion(0.0000005f, -0.7483285f, -0.0000003f, 0.6633284f), new Vector3(0.008769f, -0.009473f, -0.023556f)),
            new Pair("arm2Left", "mixamorig:LeftArm", new Quaternion(0.0000001f, -0.7488402f, 0.0000004f, 0.6627506f), new Vector3(-0.016723f, -0.028424f, 0.019425f)),
            new Pair("arm3 Left", "mixamorig:LeftForeArm", new Quaternion(0.0000005f, -0.7536708f, -0.0000004f, 0.6572521f), new Vector3(-0.009192f, -0.013362f, 0.017457f)),
            new Pair("hand1 Left", "mixamorig:LeftHand", new Quaternion(-0.0155299f, -0.7536960f, -0.0708750f, 0.6532059f), new Vector3(-0.004612f, 0.013686f, 0.005786f)),
            new Pair("arm1 Right", "mixamorig:RightShoulder", new Quaternion(0.0000023f, 0.7486137f, 0.0000007f, 0.6630064f), new Vector3(-0.008371f, -0.016877f, -0.026026f)),
            new Pair("arm2Right", "mixamorig:RightArm", new Quaternion(-0.0000004f, 0.7497461f, 0.0000003f, 0.6617256f), new Vector3(0.016954f, -0.036271f, 0.019540f)),
            new Pair("arm3 Right", "mixamorig:RightForeArm", new Quaternion(0.0000003f, 0.7547204f, -0.0000004f, 0.6560466f), new Vector3(0.009760f, -0.021048f, 0.018820f)),
            new Pair("hand1 Right", "mixamorig:RightHand", new Quaternion(-0.0133002f, 0.7547968f, 0.0725730f, 0.6517960f), new Vector3(0.006317f, 0.006222f, 0.007670f)),
            new Pair("leg1 Left", "mixamorig:LeftUpLeg", new Quaternion(0.0000019f, -0.7105305f, -0.0000021f, 0.7036664f), new Vector3(-0.011598f, 0.007775f, 0.027183f)),
            new Pair("leg2 Left", "mixamorig:LeftLeg", new Quaternion(-0.0000027f, -0.7051274f, 0.0000032f, 0.7090807f), new Vector3(0.003976f, 0.032859f, -0.000629f)),
            new Pair("leg3 Left", "mixamorig:LeftFoot", new Quaternion(-0.1370096f, -0.7303305f, -0.2195177f, 0.6321849f), new Vector3(-0.066564f, 0.012101f, -0.007638f)),
            new Pair("leg1 Right", "mixamorig:RightUpLeg", new Quaternion(0.0000002f, 0.7106194f, 0.0000005f, 0.7035766f), new Vector3(0.010898f, 0.006118f, 0.019556f)),
            new Pair("leg2 Right", "mixamorig:RightLeg", new Quaternion(-0.0000001f, 0.7052129f, 0.0000005f, 0.7089956f), new Vector3(-0.005349f, 0.031810f, -0.008351f)),
            new Pair("leg3 Right", "mixamorig:RightFoot", new Quaternion(-0.1374472f, 0.7300606f, 0.2203206f, 0.6321223f), new Vector3(0.066322f, 0.010432f, -0.015350f)),
        };

        private static Transform _seat;                 // PlayerModel_Sit of the car we sit in
        private static Transform[] _src;                // per pair, the seat's bone
        private static readonly List<Renderer> _hidden = new List<Renderer>();

        public static Transform Seat { get { return _seat; } }

        // finds the seated driver of the car the player is in (Player is a child of the car's sitPos while driving)
        public static bool Attach(Transform player)
        {
            if (player == null) { Detach(); return false; }
            var car = player.root;
            if (_seat != null && _seat.root == car) return true;
            Detach();
            var seat = Game.FindDeep(car, "PlayerModel_Sit");
            if (seat == null) { Plugin.Verbose("Car " + car.name + " has no PlayerModel_Sit"); return false; }
            var src = new Transform[Pairs.Length];
            for (int i = 0; i < Pairs.Length; i++)
            {
                src[i] = Game.FindDeep(seat, Pairs[i].Src);
                if (src[i] == null) { Plugin.Log.LogWarning("Car seat of " + car.name + ": bone " + Pairs[i].Src + " missing"); return false; }
            }
            _seat = seat; _src = src;
            if (Plugin.ReplaceDriver.Value)
                foreach (var r in seat.GetComponentsInChildren<Renderer>(true))
                    if (r.enabled) { r.enabled = false; _hidden.Add(r); }
            Plugin.Verbose("Sitting in " + car.name + ", driver model hidden (" + _hidden.Count + " renderers)");
            return true;
        }

        public static void Detach()
        {
            foreach (var r in _hidden) if (r != null) r.enabled = true;
            _hidden.Clear();
            _seat = null; _src = null;
        }

        // pose her skeleton from the seat's bones (her body root must not be mirrored here)
        public static void Pose(Dictionary<string, Transform> bones, Dictionary<string, Quaternion> bindLocal)
        {
            if (_seat == null) return;
            PoseFrom(_src, bones, bindLocal);
        }

        // the bones of a Player2 man (car seat, TAB-screen model) -> her skeleton
        public static Transform[] FindSource(Transform model)
        {
            var src = new Transform[Pairs.Length];
            for (int i = 0; i < Pairs.Length; i++) { src[i] = Game.FindDeep(model, Pairs[i].Src); if (src[i] == null) return null; }
            return src;
        }

        public static void PoseFrom(Transform[] src, Dictionary<string, Transform> bones, Dictionary<string, Quaternion> bindLocal)
        {
            for (int i = 0; i < Pairs.Length; i++)
            {
                Transform dst;
                if (!bones.TryGetValue(Pairs[i].Dst, out dst) || src[i] == null) continue;
                var s = src[i];
                if (i == 0) dst.position = s.TransformPoint(Pairs[i].Off);
                dst.rotation = s.rotation * Pairs[i].Rot;
            }
            // the toes keep their bind pose relative to the foot
            Transform t; Quaternion q;
            if (bones.TryGetValue("mixamorig:LeftToeBase", out t) && bindLocal.TryGetValue("mixamorig:LeftToeBase", out q)) t.localRotation = q;
            if (bones.TryGetValue("mixamorig:RightToeBase", out t) && bindLocal.TryGetValue("mixamorig:RightToeBase", out q)) t.localRotation = q;
        }
    }
}
