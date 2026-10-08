using System.Diagnostics;
using UnityEngine;

namespace Apocaplayer
{
    // (2.2.6) always-on performance line (Debug / PerformanceLog): the game's FPS and how much of each frame is Apocaplayer's own CPU time,
    // split by where it is spent - so a slow-machine report shows at once whether the mod or the game's rendering is the cost.
    // Every 60 s on foot, every 15 s while driving (the first one 5 s after getting in), plus the cameras that render in the car.
    internal static class Perf
    {
        internal const int Update = 0, Late = 1, Camera = 2, ModApi = 3, Gui = 4, EndFrame = 5;
        private static readonly string[] Names = { "update", "late", "camera+cutaway", "modapi", "gui", "endframe" };
        private static readonly long[] _ticks = new long[6];
        private static int _frames; private static float _start = -1f, _next; private static bool _wasInCar;
        private static readonly double MsPerTick = 1000.0 / Stopwatch.Frequency;

        internal static long Now { get { return Stopwatch.GetTimestamp(); } }
        internal static void Add(int part, long since) { _ticks[part] += Stopwatch.GetTimestamp() - since; }

        // once a frame, from Runner.Update
        internal static void Frame()
        {
            if (Plugin.PerformanceLog == null || !Plugin.PerformanceLog.Value || !Game.Ready) { _start = -1f; return; }   // a fresh full window once it counts again
            float now = Time.unscaledTime;
            bool car = Game.InCar;
            if (car != _wasInCar) { _wasInCar = car; Clear(now); _next = now + (car ? 5f : 60f); return; }
            if (_start < 0f) { Clear(now); _next = now + (car ? 15f : 60f); return; }
            _frames++;
            if (now < _next) return;
            float span = now - _start;
            try
            {
            if (_frames > 0 && span > 0f)
            {
                var sb = new System.Text.StringBuilder(256);
                double total = 0; for (int i = 0; i < _ticks.Length; i++) total += _ticks[i] * MsPerTick;
                sb.Append("Perf ").Append(car ? "(car" : "(on foot").Append(ThirdPerson.On ? ", third person" : ", first person")
                  .Append(Plugin.OcclusionPrototype.Value && (!car || Plugin.OcclusionInVehicle.Value) ? ", cutaway on" : ", cutaway off").Append("): ")
                  .Append((_frames / span).ToString("0.0")).Append(" fps (").Append((span * 1000f / _frames).ToString("0.0")).Append(" ms/frame), Apocaplayer ")
                  .Append((total / _frames).ToString("0.00")).Append(" ms/frame [");
                for (int i = 0; i < _ticks.Length; i++) sb.Append(i > 0 ? ", " : "").Append(Names[i]).Append(' ').Append((_ticks[i] * MsPerTick / _frames).ToString("0.00"));
                sb.Append("], shadows ").Append(QualitySettings.shadowDistance.ToString("0")).Append(" m, ").Append(Screen.width).Append('x').Append(Screen.height);
                if (car)
                {
                    sb.Append(", cameras:");
                    foreach (var c in UnityEngine.Camera.allCameras)
                        if (c != null && c.isActiveAndEnabled) sb.Append(' ').Append(c.name).Append(c.targetTexture != null ? "(rt " + c.targetTexture.width + ")" : "");
                }
                Plugin.Log.LogInfo(sb.ToString());
            }
            }
            finally { Clear(now); _next = now + (car ? 15f : 60f); }
        }
        private static void Clear(float now) { for (int i = 0; i < _ticks.Length; i++) _ticks[i] = 0; _frames = 0; _start = now; }
    }
}
