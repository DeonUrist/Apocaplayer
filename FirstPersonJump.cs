using UnityEngine;

namespace Apocaplayer
{
    internal sealed partial class Body
    {
        private float _fpLeftJump, _fpRightJump;

        // Applied after animation, seat/traversal pose fades and first-person body
        // offsets. Limit the forward thigh lift in body space, independent of mouse
        // pitch: looking down still shows the legs, without pulling a knee toward the eye.
        public void FitFirstPersonJump(bool firstPerson, float dt)
        {
            if (!firstPerson || !Plugin.BodyFirstPerson.Value || Traversal != null || _inCar || _prone > .01f)
            { _fpLeftJump = _fpRightJump = 0f; return; }
            if (Game.Paused) dt = 0f;
            float strength = Mathf.Clamp01(Plugin.FirstPersonJumpKneeCorrection.Value);
            if (strength <= 0f) { _fpLeftJump = _fpRightJump = 0f; return; }
            bool jumping = _actionW > .001f && _actionClip != null && _actionClip.Contains("Jump");
            if (!jumping && _fpLeftJump <= 0f && _fpRightJump <= 0f) return;
            FitJumpThigh("mixamorig:LeftUpLeg", "mixamorig:LeftLeg", jumping, strength, dt, ref _fpLeftJump);
            FitJumpThigh("mixamorig:RightUpLeg", "mixamorig:RightLeg", jumping, strength, dt, ref _fpRightJump);
        }

        private void FitJumpThigh(string thighName, string kneeName, bool jumping, float strength, float dt, ref float correction)
        {
            Transform thigh, knee;
            if (!Bones.TryGetValue(thighName, out thigh)
                || !Bones.TryGetValue(kneeName, out knee)) { correction = 0f; return; }
            Vector3 leg = Quaternion.Inverse(Root.transform.rotation) * (knee.position - thigh.position);
            float lift = Mathf.Atan2(leg.z, -leg.y) * Mathf.Rad2Deg;
            float target = jumping ? Mathf.Max(0f, lift - 30f) * strength : 0f;
            // Up to a 90-degree tuck settles in 0.1 s; clip/action fades already
            // ease the incoming pose. Never change the knee joint or bone lengths.
            correction = Mathf.MoveTowards(correction, target, Mathf.Max(0f, dt) * 900f);
            if (correction > .001f) thigh.rotation = Quaternion.AngleAxis(correction, Root.transform.right) * thigh.rotation;
        }
    }
}
