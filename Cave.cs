using UnityEngine;

namespace Apocaplayer
{
    // (2.2.9) no camera culling inside caves: there the cut-away views lose the rock's shadows and light leaks in as bright patches.
    // "Inside a cave" is the game's own flag: the Player's CaveDarkness FSM sets its CaveDarkness_bool when she passes a cave mouth's Cave_IN
    // trigger and clears it at Cave_OUT (the same switch that dims the sky in caves) - so camps, wrecks and buildings never count.
    internal static class Cave
    {
        public static bool Inside;
        private static PlayMakerFSM _fsm;
        private static GameObject _for;
        private static float _next;

        public static void Tick(bool want)
        {
            if (!want) { Inside = false; return; }
            float now = Time.unscaledTime;
            if (now < _next) return;
            _next = now + 0.25f;
            bool inside = false;
            var player = Game.Player;
            if (player != null)
            {
                if (_for != player || _fsm == null)
                {
                    _for = player; _fsm = null;
                    foreach (var f in player.GetComponents<PlayMakerFSM>()) if (f != null && f.FsmName == "CaveDarkness") { _fsm = f; break; }
                }
                if (_fsm != null && _fsm.FsmVariables != null)
                {
                    var b = _fsm.FsmVariables.GetFsmBool("CaveDarkness_bool");
                    inside = b != null && b.Value;
                }
            }
            if (inside != Inside) { Inside = inside; Plugin.Verbose(inside ? "Camera culling: off in the cave" : "Camera culling: out of the cave, back on"); }
        }

        public static void Reset() { Inside = false; _fsm = null; _for = null; }
    }
}
