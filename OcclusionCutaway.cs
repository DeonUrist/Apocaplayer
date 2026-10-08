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
                var texture = Shader.GetGlobalTexture(IdDepthTex);
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
        // (2.2.6) performance: the blocker search runs 10 times a second (a blocker is kept .18 s after it was last seen, so nothing is lost),
        // what a collider stands for (its renderers, roof / vehicle, terrain) is worked out once and remembered, a collider without a renderer of
        // its own never pulls in a whole camp / building / terrain tile (an owner with more than MaxOwnerRenderers meshes: only those touching the collider), the vehicle's
        // renderers are collected every 2 s, the two extra views render at 3/4 resolution with shadows only to 25 m past her, and the main camera's
        // depth texture and the composite are only on in the frames that really cut something away.
        private const float ScanInterval = .1f, ClearShadowDistance = 20f, VehicleRescan = 2f;
        private const int MaxOwnerRenderers = 24;
        private float _nextScan;
        private sealed class Blocker { public Renderer[] Renderers; public bool Terrain, RoofOrVehicle; public float At; }
        private readonly Dictionary<Collider, Blocker> _blockers = new Dictionary<Collider, Blocker>();
        private static readonly Renderer[] NoRenderers = new Renderer[0];
        private Transform _vehicle; private Renderer[] _vehicleRenderers = NoRenderers; private float _vehicleAt;
        private bool _depthOn;
        private Vector3 _scanPos, _scanFwd;
        private struct TerrainState { public Terrain Terrain; public bool Height, Trees; }
        private static readonly int IdDepthTex = Shader.PropertyToID("_CameraDepthTexture"), IdEnds = Shader.PropertyToID("_Ends"),
            IdRadius = Shader.PropertyToID("_Radius"), IdAspect = Shader.PropertyToID("_Aspect"), IdStrength = Shader.PropertyToID("_Strength"),
            IdFocus = Shader.PropertyToID("_FocusDepth"), IdClean = Shader.PropertyToID("_CleanTex"), IdCleanDepth = Shader.PropertyToID("_CleanDepth"),
            IdFront = Shader.PropertyToID("_FrontTex"), IdFrontDepth = Shader.PropertyToID("_FrontDepth");
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
            if (!Available || !Game.Ready) return;
            if (_active == null || _active._main != main)
            {
                Stop();
                _active = main.gameObject.AddComponent<OcclusionCutaway>();
                _active._main = main;
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
            if (vehicle == null) { _vehicle = null; _vehicleRenderers = NoRenderers; }
            else
            {
                _largeSeen = now;
                if (vehicle != _vehicle || now >= _vehicleAt + VehicleRescan) { _vehicle = vehicle; _vehicleAt = now; _vehicleRenderers = vehicle.GetComponentsInChildren<Renderer>(true); }
                foreach (var renderer in _vehicleRenderers) Add(renderer, now);
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
            return ModNamed(target);
        }
        // an object of this mod (its name starts with Apocaplayer, or a parent's does) - remembered per transform while its root stays the same
        // (Transform.name allocates a string on every read)
        private struct Owned { public int Root; public bool Mod; }
        private static readonly Dictionary<int, Owned> _owned = new Dictionary<int, Owned>();
        private static bool ModNamed(Transform t)
        {
            int id = t.GetInstanceID(), root = t.root.GetInstanceID(); Owned o;
            if (_owned.TryGetValue(id, out o) && o.Root == root) return o.Mod;
            o.Root = root; o.Mod = false;
            for (var p = t; p != null; p = p.parent) if (p.name.StartsWith("Apocaplayer", StringComparison.Ordinal)) { o.Mod = true; break; }
            if (_owned.Count > 8192) _owned.Clear();
            _owned[id] = o; return o.Mod;
        }

        private void Add(Renderer renderer, float now)
        {
            if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy && !renderer.forceRenderingOff && !Mine(renderer.transform)) _renderers[renderer] = now;
        }

        private void Add(Collider collider, float now, bool overhead = false)
        {
            if (collider == null || Mine(collider.transform)) return;
            var b = BlockerOf(collider);
            if (b.Terrain) return;   // the ground is never cut away (1.6.0: the window used to open holes into the terrain)
            var bounds = collider.bounds;
            bool overheadPanel = bounds.size.y < .5f && Mathf.Max(bounds.size.x, bounds.size.z) > 1f && bounds.center.y >= _headHeight - .15f;
            if (overhead || overheadPanel || b.RoofOrVehicle) _largeSeen = now;
            foreach (var item in b.Renderers) Add(item, now);
        }

        // what a collider stands for, worked out once: its own renderer, else the renderers of the object it belongs to (LOD group, rigidbody,
        // parent) - but when that "object" is really an area (a camp, a building, a terrain tile with hundreds of meshes), only the meshes at the
        // collider itself (their bounds touching it)
        private Blocker BlockerOf(Collider collider)
        {
            Blocker b;
            if (_blockers.TryGetValue(collider, out b) && Time.unscaledTime - b.At < 10f) return b;   // re-worked every 10 s (parts added, objects moved)
            if (_blockers.Count > 2048) _blockers.Clear();
            b = new Blocker { At = Time.unscaledTime };
            if (collider.GetComponent<Terrain>() != null) { b.Terrain = true; b.Renderers = NoRenderers; _blockers[collider] = b; return b; }
            b.RoofOrVehicle = RoofOrVehicle(collider.transform);
            var own = collider.GetComponent<Renderer>();
            if (own != null) b.Renderers = new[] { own };
            else
            {
                var lod = collider.GetComponentInParent<LODGroup>();
                var owner = lod != null ? lod.transform : collider.attachedRigidbody != null ? collider.attachedRigidbody.transform : collider.transform.parent;
                if (owner == null) owner = collider.transform;
                var all = owner.GetComponentsInChildren<Renderer>(true);
                if (all.Length <= MaxOwnerRenderers) b.Renderers = all;
                else
                {
                    var near = new List<Renderer>();
                    var cb = collider.bounds; cb.Expand(.1f);
                    foreach (var r in all)
                    {
                        if (r == null || !r.bounds.Intersects(cb)) continue;
                        near.Add(r);
                        if (near.Count >= 256) break;     // sanity limit only
                    }
                    b.Renderers = near.ToArray();
                    Plugin.Verbose("Camera cutaway: " + collider.name + " belongs to " + owner.name + " (" + all.Length + " meshes) - only the " + b.Renderers.Length + " touching the collider are cut away");
                }
            }
            _blockers[collider] = b;
            return b;
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
            float now = Time.unscaledTime;
            Vector3 fwd = rotation * Vector3.forward;
            if (now >= _nextScan || vehicle != _vehicle || (pos - _scanPos).sqrMagnitude > .25f || Vector3.Dot(fwd, _scanFwd) < .996f)
            { _nextScan = now + ScanInterval; _scanPos = pos; _scanFwd = fwd; FindBlockers(pos, head, feet, vehicle); }   // at once when the camera moved / turned
            if (_lastFrame != Time.frameCount)
            {
                _lastFrame = Time.frameCount;
                _strength = Mathf.MoveTowards(_strength, _renderers.Count + _terrains.Count > 0 ? 1f : 0f, Time.unscaledDeltaTime * 8f);
                _windowScale = Mathf.MoveTowards(_windowScale, Time.unscaledTime - _largeSeen <= .18f ? 3f : 1f, Time.unscaledDeltaTime * 16f);
            }
            if (_strength <= .001f || _renderers.Count + _terrains.Count == 0) { DepthOff(); enabled = false; return; }
            EnsureBuffers();
            DepthOn();
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
            float shadowDistance = QualitySettings.shadowDistance;
            float capped = Mathf.Min(shadowDistance, Mathf.Max(ClearShadowDistance, (head - pos).magnitude + 25f));   // shadows to 25 m past her
            bool capShadows = capped < shadowDistance;
            try
            {
                if (capShadows) QualitySettings.shadowDistance = capped;
                _clearDepthReady = false;
                _captureDepth = _depth;
                _clearCamera.Render();
                if (!_clearDepthReady) return;
                var a = _clearCamera.WorldToViewportPoint(head); var b = _clearCamera.WorldToViewportPoint(feet);
                float focusDepth = Mathf.Max(a.z, b.z);
                if (focusDepth <= .05f) return;
                float radius = Mathf.Clamp(Plugin.OcclusionRadius.Value * _main.projectionMatrix.m11 / (2f * Mathf.Max(.3f, (a.z + b.z) * .5f)), .02f, .45f) * _windowScale;
                _composite.SetVector(IdEnds, new Vector4(a.x, a.y, b.x, b.y));
                _composite.SetFloat(IdRadius, radius); _composite.SetFloat(IdAspect, _main.aspect);
                _composite.SetFloat(IdStrength, _strength * (1f - Plugin.OcclusionOpacity.Value));
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
                _composite.SetFloat(IdFocus, stopDepth);
                _prepared = true;
            }
            finally
            {
                if (capShadows) QualitySettings.shadowDistance = shadowDistance;
                Restore();
                enabled = _prepared;                 // the composite (OnRenderImage) only in a frame that cuts something away
            }
        }

        // the main camera's depth texture (the composite needs it) only while something is cut away
        private bool _addedDepth;
        private void DepthOn()
        {
            if (_main == null) return;
            _depthOn = true;
            var m = _main.depthTextureMode;
            if ((m & DepthTextureMode.Depth) == 0) { _main.depthTextureMode = m | DepthTextureMode.Depth; _addedDepth = true; }   // re-checked every frame: someone else may set the mode
        }
        private void DepthOff()
        {
            if (!_depthOn || _main == null) return;
            _depthOn = false;
            if (_addedDepth) { _main.depthTextureMode &= ~DepthTextureMode.Depth; _addedDepth = false; }   // only the bit we added; the game's own use stays
        }

        private void EnsureBuffers()
        {
            int width = Mathf.Max(16, _main.pixelWidth * 3 / 4), height = Mathf.Max(16, _main.pixelHeight * 3 / 4);   // 3/4 resolution (~44 % fewer pixels): seen only through the feathered window
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
            _composite.SetTexture(IdClean, _clear); _composite.SetTexture(IdCleanDepth, _depth);   // bound once per set of buffers
            _composite.SetTexture(IdFront, _front); _composite.SetTexture(IdFrontDepth, _frontDepth);
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
            if (_prepared && ThirdPerson.On && !ThirdPerson.Peek && Plugin.OcclusionPrototype.Value && (!Game.InCar || Plugin.OcclusionInVehicle.Value)) Graphics.Blit(source, destination, _composite, 0);
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
            DepthOff();
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
