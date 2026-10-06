using System;
using System.Collections.Generic;
using UnityEngine;

namespace Apocaplayer
{
    // Prototype: a second view omits only blockers; a depth-tested, feathered
    // screen window composites it over their native appearance. No material edits.
    internal sealed class OcclusionCutaway : MonoBehaviour
    {
        private static Shader _shader;
        private static bool _tried;
        private static OcclusionCutaway _active;
        private Camera _main, _clearCamera;
        private Material _composite;
        private RenderTexture _clear, _depth, _front, _frontDepth, _captureDepth;
        private readonly Dictionary<Renderer, float> _renderers = new Dictionary<Renderer, float>();
        private readonly Dictionary<Terrain, float> _terrains = new Dictionary<Terrain, float>();
        private readonly List<Renderer> _removeRenderers = new List<Renderer>();
        private readonly List<Terrain> _removeTerrains = new List<Terrain>();
        private readonly List<Renderer> _hidden = new List<Renderer>();
        private readonly List<TerrainState> _hiddenTerrain = new List<TerrainState>();
        private readonly RaycastHit[] _hits = new RaycastHit[64];
        private readonly Collider[] _near = new Collider[64];
        private bool _prepared;
        private bool _clearDepthReady;
        internal sealed class ClearViewCapture : MonoBehaviour
        {
            internal OcclusionCutaway Owner;
            private void OnRenderImage(RenderTexture source, RenderTexture destination)
            {
                var texture = Shader.GetGlobalTexture("_CameraDepthTexture");
                if (Owner != null && texture != null)
                {
                    Graphics.Blit(texture, Owner._captureDepth, Owner._composite, 1);
                    Owner._clearDepthReady = true;
                }
                Graphics.Blit(source, destination);
            }
        }
        private float _strength;
        private float _largeSeen = float.NegativeInfinity, _windowScale = 1f, _headHeight;
        private int _lastFrame = -1;
        private DepthTextureMode _oldDepthMode;
        private struct TerrainState { public Terrain Terrain; public bool Height, Trees; }
        private static readonly int Mask = ~((1 << 6) | (1 << 2) | (1 << 5) | (1 << 9) | (1 << 22));

        internal static bool Available
        {
            get
            {
                if (_shader != null) return _shader.isSupported;
                if (_tried) return false;
                _tried = true;
                try
                {
                    var bundle = AssetBundle.LoadFromFile(Plugin.ModPath("Models/apocaplayer_camera.bundle"));
                    if (bundle != null) { _shader = bundle.LoadAsset<Shader>("Assets/OcclusionComposite.shader"); bundle.Unload(false); }
                }
                catch (Exception e) { Plugin.Warn("Camera cutaway shader: " + e.Message); }
                if (_shader == null || !_shader.isSupported) Plugin.Warn("Camera cutaway shader unavailable; original collision camera remains active.");
                return _shader != null && _shader.isSupported;
            }
        }

        internal static void Prepare(Camera main, Vector3 pos, Quaternion rotation, Transform vehicle)
        {
            if (!Available) return;
            if (_active == null || _active._main != main)
            {
                Stop();
                _active = main.gameObject.AddComponent<OcclusionCutaway>();
                _active._main = main; _active._oldDepthMode = main.depthTextureMode;
            }
            try { _active.RenderClear(pos, rotation, vehicle); }
            catch (Exception e) { _active.Restore(); _active._prepared = false; Plugin.Warn("Camera cutaway: " + e.Message); }
        }

        private void FindBlockers(Vector3 camera, Vector3 head, Vector3 feet, Transform vehicle)
        {
            float now = Time.unscaledTime;
            _headHeight = head.y;
            Trace(camera, head, feet.y, now); Trace(camera, feet, feet.y, now); Trace(camera, (head + feet) * .5f, feet.y, now);
            int count = Physics.OverlapSphereNonAlloc(camera, Mathf.Max(.10f, _main.nearClipPlane), _near, Mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++) Add(_near[i], now);
            if (vehicle != null)
            {
                _largeSeen = now;
                foreach (var renderer in vehicle.GetComponentsInChildren<Renderer>(true)) Add(renderer, now);
            }
            _removeRenderers.Clear();
            foreach (var item in _renderers) if (item.Key == null || now - item.Value > .18f) _removeRenderers.Add(item.Key);
            foreach (var item in _removeRenderers) _renderers.Remove(item);
            _removeTerrains.Clear();
            foreach (var item in _terrains) if (item.Key == null || now - item.Value > .18f) _removeTerrains.Add(item.Key);
            foreach (var item in _removeTerrains) _terrains.Remove(item);
        }

        private void Trace(Vector3 from, Vector3 to, float floor, float now)
        {
            Vector3 delta = to - from; float distance = delta.magnitude;
            if (distance < .01f) return;
            float radius = Mathf.Clamp(Plugin.OcclusionRadius.Value, .2f, .9f);
            int count = Physics.SphereCastNonAlloc(from, radius, delta / distance, _hits, Mathf.Max(0, distance - radius - .075f), Mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                // Standing on ground is not an obstruction. Raised slopes behind the
                // character and geometry intersecting the camera are still handled.
                if (_hits[i].normal.y > .65f && _hits[i].point.y <= floor + .1f) continue;
                bool overhead = Mathf.Abs(_hits[i].normal.y) > .65f && (_hits[i].normal.y < 0 || _hits[i].point.y >= _headHeight - .15f);
                Add(_hits[i].collider, now, overhead);
            }
        }

        private static bool Mine(Transform target)
        {
            if (target == null) return true;
            if (Game.Player != null && target.IsChildOf(Game.Player.transform) || Game.PlayerCamera != null && target.IsChildOf(Game.PlayerCamera)) return true;
            for (var p = target; p != null; p = p.parent) if (p.name.StartsWith("Apocaplayer", StringComparison.Ordinal)) return true;
            return false;
        }

        private void Add(Renderer renderer, float now)
        {
            if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy && !renderer.forceRenderingOff && !Mine(renderer.transform)) _renderers[renderer] = now;
        }

        private void Add(Collider collider, float now, bool overhead = false)
        {
            if (collider == null || Mine(collider.transform)) return;
            var terrain = collider.GetComponent<Terrain>();
            if (terrain != null) { _terrains[terrain] = now; return; }
            var bounds = collider.bounds;
            bool overheadPanel = bounds.size.y < .5f && Mathf.Max(bounds.size.x, bounds.size.z) > 1f && bounds.center.y >= _headHeight - .15f;
            if (overhead || overheadPanel || RoofOrVehicle(collider.transform)) _largeSeen = now;
            var renderer = collider.GetComponent<Renderer>();
            if (renderer != null) { Add(renderer, now); return; }
            var lod = collider.GetComponentInParent<LODGroup>();
            var owner = lod != null ? lod.transform : collider.attachedRigidbody != null ? collider.attachedRigidbody.transform : collider.transform.parent;
            if (owner == null) owner = collider.transform;
            foreach (var item in owner.GetComponentsInChildren<Renderer>(true)) Add(item, now);
        }

        private static bool RoofOrVehicle(Transform target)
        {
            for (var parent = target; parent != null; parent = parent.parent)
            {
                string name = parent.name.ToLowerInvariant();
                if (name.Contains("roof") || name.Contains("ceiling") || parent.Find("DriveTrigger") != null) return true;
                foreach (var component in parent.GetComponents<Component>())
                    if (component != null && component.GetType().Name == "VehicleController") return true;
            }
            return false;
        }

        private void RenderClear(Vector3 pos, Quaternion rotation, Transform vehicle)
        {
            _prepared = false;
            Vector3 head, feet;
            if (!Runner.TryOcclusionPoints(out head, out feet))
            {
                Vector3 center = Game.Player != null ? Game.Player.transform.position : _main.transform.position;
                head = center + Vector3.up * .85f; feet = center - Vector3.up * .75f;
            }
            FindBlockers(pos, head, feet, vehicle);
            if (_lastFrame != Time.frameCount)
            {
                _lastFrame = Time.frameCount;
                _strength = Mathf.MoveTowards(_strength, _renderers.Count + _terrains.Count > 0 ? 1f : 0f, Time.unscaledDeltaTime * 8f);
                _windowScale = Mathf.MoveTowards(_windowScale, Time.unscaledTime - _largeSeen <= .18f ? 3f : 1f, Time.unscaledDeltaTime * 16f);
            }
            if (_strength <= .001f || _renderers.Count + _terrains.Count == 0) return;
            EnsureBuffers();
            _main.depthTextureMode |= DepthTextureMode.Depth;
            _clearCamera.CopyFrom(_main); _clearCamera.enabled = false;
            _clearCamera.transform.SetPositionAndRotation(pos, rotation);
            _clearCamera.ResetWorldToCameraMatrix(); _clearCamera.ResetCullingMatrix();
            _clearCamera.rect = new Rect(0, 0, 1, 1); _clearCamera.targetTexture = _clear;
            _clearCamera.depthTextureMode = DepthTextureMode.Depth;
            foreach (var item in _renderers)
            {
                var renderer = item.Key;
                if (renderer == null || renderer.forceRenderingOff) continue;
                renderer.forceRenderingOff = true; _hidden.Add(renderer);
            }
            foreach (var item in _terrains)
            {
                var terrain = item.Key; if (terrain == null) continue;
                _hiddenTerrain.Add(new TerrainState { Terrain = terrain, Height = terrain.drawHeightmap, Trees = terrain.drawTreesAndFoliage });
                terrain.drawHeightmap = false; terrain.drawTreesAndFoliage = false;
            }
            try
            {
                _clearDepthReady = false;
                _captureDepth = _depth;
                _clearCamera.Render();
                if (!_clearDepthReady) return;
                var a = _clearCamera.WorldToViewportPoint(head); var b = _clearCamera.WorldToViewportPoint(feet);
                float focusDepth = Mathf.Max(a.z, b.z);
                if (focusDepth <= .05f) return;
                float radius = Mathf.Clamp(Plugin.OcclusionRadius.Value * _main.projectionMatrix.m11 / (2f * Mathf.Max(.3f, (a.z + b.z) * .5f)), .02f, .45f) * _windowScale;
                _composite.SetVector("_Ends", new Vector4(a.x, a.y, b.x, b.y));
                _composite.SetFloat("_Radius", radius); _composite.SetFloat("_Aspect", _main.aspect);
                _composite.SetFloat("_Strength", _strength * (1f - Plugin.OcclusionOpacity.Value));
                Vector3 character = Game.Player != null && !Game.Dead ? Game.Player.transform.position : (head + feet) * .5f;
                float stopDepth = Mathf.Max(_main.nearClipPlane + .01f, Vector3.Dot(character - pos, rotation * Vector3.forward) - .075f);
                // Restore blockers, then render only the scene beyond the character
                // plane. A single building/terrain mesh may contain both rear and
                // front surfaces, so hiding that whole mesh must not reveal its far side.
                Restore();
                _clearCamera.targetTexture = _front;
                _clearCamera.nearClipPlane = Mathf.Min(stopDepth, _clearCamera.farClipPlane - .1f);
                _clearCamera.ResetProjectionMatrix(); _clearCamera.ResetCullingMatrix();
                _captureDepth = _frontDepth; _clearDepthReady = false;
                _clearCamera.Render();
                if (!_clearDepthReady) return;
                _composite.SetFloat("_FocusDepth", stopDepth);
                _composite.SetTexture("_CleanTex", _clear); _composite.SetTexture("_CleanDepth", _depth);
                _composite.SetTexture("_FrontTex", _front); _composite.SetTexture("_FrontDepth", _frontDepth);
                _prepared = true;
            }
            finally { Restore(); }
        }

        private void EnsureBuffers()
        {
            int width = Mathf.Max(16, _main.pixelWidth), height = Mathf.Max(16, _main.pixelHeight);
            if (_clear != null && _clear.width == width && _clear.height == height) return;
            ReleaseBuffers();
            if (_composite == null) _composite = new Material(_shader) { hideFlags = HideFlags.HideAndDontSave };
            if (_clearCamera == null)
            {
                var host = new GameObject("Apocaplayer.CutawayCamera") { hideFlags = HideFlags.HideAndDontSave };
                _clearCamera = host.AddComponent<Camera>(); _clearCamera.enabled = false;
                host.AddComponent<ClearViewCapture>().Owner = this;
            }
            _clear = new RenderTexture(width, height, 24, _main.allowHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default) { name = "Apocaplayer clear view", hideFlags = HideFlags.HideAndDontSave };
            _depth = new RenderTexture(width, height, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear) { name = "Apocaplayer clear depth", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
            _front = new RenderTexture(width, height, 24, _main.allowHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default) { name = "Apocaplayer protected front view", hideFlags = HideFlags.HideAndDontSave };
            _frontDepth = new RenderTexture(width, height, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear) { name = "Apocaplayer protected front depth", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
            _clear.Create(); _depth.Create(); _front.Create(); _frontDepth.Create();
        }

        private void Restore()
        {
            foreach (var renderer in _hidden) if (renderer != null) renderer.forceRenderingOff = false;
            _hidden.Clear();
            foreach (var item in _hiddenTerrain) if (item.Terrain != null) { item.Terrain.drawHeightmap = item.Height; item.Terrain.drawTreesAndFoliage = item.Trees; }
            _hiddenTerrain.Clear();
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (_prepared && ThirdPerson.On && !ThirdPerson.Peek && Plugin.OcclusionPrototype.Value) Graphics.Blit(source, destination, _composite, 0);
            else Graphics.Blit(source, destination);
            _prepared = false;
        }

        private void ReleaseBuffers()
        {
            if (_clearCamera != null) _clearCamera.targetTexture = null;
            if (_clear != null) { _clear.Release(); Destroy(_clear); _clear = null; }
            if (_depth != null) { _depth.Release(); Destroy(_depth); _depth = null; }
            if (_front != null) { _front.Release(); Destroy(_front); _front = null; }
            if (_frontDepth != null) { _frontDepth.Release(); Destroy(_frontDepth); _frontDepth = null; }
            _captureDepth = null;
        }

        private void OnDestroy()
        {
            Restore(); ReleaseBuffers();
            if (_main != null && _main.depthTextureMode == (_oldDepthMode | DepthTextureMode.Depth)) _main.depthTextureMode = _oldDepthMode;
            if (_clearCamera != null) Destroy(_clearCamera.gameObject);
            if (_composite != null) Destroy(_composite);
            if (_active == this) _active = null;
        }

        internal static void Stop()
        {
            if (_active == null) return;
            _active.Restore(); _active._prepared = false;
            Destroy(_active); _active = null;
        }
    }
}
