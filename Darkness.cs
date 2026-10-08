using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Apocaplayer
{
    // (2.2.9) the camera culling is switched off in the dark (caves, unlit interiors, night): there the cut-away views lose the blockers'
    // shadows and light leaks in as bright patches. Twice a second a 64x36 copy of the game camera's picture (after the scene, before any
    // image effect - so before the cutaway's own composite) is read back asynchronously and its mean brightness taken; below the
    // "3rd person camera culling darkness" level (for about a second) the culling stops and the camera moves in front of what blocks it, as
    // with the culling off; it comes back once the picture is clearly brighter again. Costs a tiny blit and a 9 KB readback every 0.5 s.
    internal static class Darkness
    {
        public static bool Dark;
        public static float Level = -1f;          // last mean brightness 0..1, -1 = not measured
        private static RenderTexture _rt;
        private static CommandBuffer _cb;
        private static Camera _on;
        private static int _addedFrame;
        private static float _next, _pendingSince = -1f;
        private static int _votes;
        private static bool? _supported;
        private const CameraEvent Event = CameraEvent.AfterForwardAlpha;

        // every frame from Runner.Update; want = the culling would be on now
        public static void Tick(bool want)
        {
            float now = Time.unscaledTime;
            if (_on != null && Time.frameCount > _addedFrame) { _on.RemoveCommandBuffer(Event, _cb); _on = null; }
            float threshold = Plugin.CullingDarkness.Value;
            if (!want || threshold <= 0f) { Dark = false; _votes = 0; return; }
            if (_supported == null) { _supported = SystemInfo.supportsAsyncGPUReadback; if (_supported == false) Plugin.Log.LogInfo("Camera culling: this GPU has no async readback - not switched off in the dark"); }
            if (_supported == false) { Dark = false; return; }
            if (_pendingSince >= 0f && now - _pendingSince < 3f) return;    // the last sample is still on its way
            var cam = Game.Cam;
            if (cam == null || now < _next || _on != null) return;
            _next = now + 0.5f;
            try
            {
                if (_rt == null || !_rt.IsCreated())
                {
                    if (_rt != null) UnityEngine.Object.Destroy(_rt);
                    _rt = new RenderTexture(64, 36, 0, RenderTextureFormat.ARGB32) { name = "Apocaplayer darkness", hideFlags = HideFlags.HideAndDontSave };
                    _rt.Create();
                }
                if (_cb == null)
                {
                    _cb = new CommandBuffer { name = "Apocaplayer darkness" };
                    _cb.Blit(BuiltinRenderTextureType.CurrentActive, _rt);
                    _cb.RequestAsyncReadback(_rt, OnReadback);
                }
                cam.AddCommandBuffer(Event, _cb); _on = cam; _addedFrame = Time.frameCount; _pendingSince = now;
            }
            catch (Exception e) { _supported = false; Dark = false; Plugin.Warn("Camera culling darkness check off: " + e.Message); }
        }

        private static void OnReadback(AsyncGPUReadbackRequest r)
        {
            _pendingSince = -1f;
            if (r.hasError) return;
            NativeArray<Color32> px = r.GetData<Color32>();
            if (px.Length == 0) return;
            double sum = 0;
            for (int i = 0; i < px.Length; i++) { var c = px[i]; sum += 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b; }
            Level = (float)(sum / (px.Length * 255.0));
            float threshold = Plugin.CullingDarkness.Value;
            bool want = Dark ? Level < threshold * 1.6f : Level < threshold;     // a gap so it doesn't flicker at the edge
            if (want != Dark) { if (++_votes >= 2) { Dark = want; _votes = 0; Plugin.Verbose("Camera culling: " + (Dark ? "off in the dark" : "back on") + " (picture brightness " + Level.ToString("0.000") + ")"); } }
            else _votes = 0;
        }

        public static void Reset()
        {
            if (_on != null && _cb != null) _on.RemoveCommandBuffer(Event, _cb);
            _on = null; Dark = false; _votes = 0; _pendingSince = -1f; Level = -1f;
        }
    }
}
