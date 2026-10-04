using System;
using System.Collections.Generic;
using UnityEngine;

namespace FemalePlayer
{
    // The weapon in her hand in third person = the game's own NPC prop for that weapon (Flexa's akm_trash_model, Sprokka's 22_pipe_pistol,
    // Lugnut's slamberg_500_chopped ...), at the exact spot the NPC prefab holds it on its mixamorig:LeftHand (guns) / RightHand (blades).
    // Same skeleton, same animations -> it sits in her hand like in theirs.
    internal static class Props
    {
        // Item = the weapon's own world model (the item you pick up), used when no NPC carries the weapon; Ref = the stand-in NPC prop that
        // gives it its starting place in the hand
        internal sealed class Prop { public string Key; public GameObject Source; public string Hand; public string Owner; public bool Item; public Prop Ref; }
        private static readonly Dictionary<string, Prop> _items = new Dictionary<string, Prop>();

        private static readonly Dictionary<string, Prop> _catalog = new Dictionary<string, Prop>();
        private static bool _built;

        public enum Kind { None, Rifle, Pistol, Melee, Throw }

        public static Kind KindOf(string weapon)
        {
            if (string.IsNullOrEmpty(weapon) || weapon == "hands") return Kind.None;
            string w = weapon.ToLowerInvariant();
            if (w.Contains("blastlance") || w.Contains("grenade")) return Kind.Throw;
            if (w.Contains("shiv") || w.Contains("knife") || w.Contains("machete") || w.Contains("wrench") || w.Contains("pipe_wrench")) return Kind.Melee;
            // SMGs (borz_smg, 22_pipe_smg) are held and animated like pistols (Pistol* clips, PistolReload)
            if (w.Contains("pistol") || w.Contains("revolver") || w.Contains("folk_17") || w.Contains("smg") || w.Contains("borz")) return Kind.Pistol;
            return Kind.Rifle;
        }

        private static string Norm(string n)
        {
            n = n.ToLowerInvariant();
            int p = n.IndexOf(" (", StringComparison.Ordinal); if (p > 0) n = n.Substring(0, p);
            if (n.EndsWith("_model")) n = n.Substring(0, n.Length - 6);
            if (n.StartsWith("9mm_")) n = n.Substring(4);
            return n.Trim();
        }

        private static void Build()
        {
            _built = true;
            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (t == null || t.gameObject.scene.IsValid()) continue;   // prefab assets only
                if (t.name != "mixamorig:LeftHand" && t.name != "mixamorig:RightHand") continue;
                string owner = t.root != null ? t.root.name : "?";
                for (int i = 0; i < t.childCount; i++)
                {
                    var c = t.GetChild(i);
                    if (c.GetComponentInChildren<MeshRenderer>(true) == null) continue;
                    string key = Norm(c.name);
                    if (_catalog.ContainsKey(key)) continue;
                    _catalog[key] = new Prop { Key = key, Source = c.gameObject, Hand = t.name, Owner = owner };
                }
            }
            var keys = new List<string>(_catalog.Keys); keys.Sort();
            Plugin.Log.LogInfo("Props: " + _catalog.Count + " NPC weapon models found (" + string.Join(", ", keys.ToArray()) + ")");
        }

        // NPC prop for the player's weapon (first-person object name), with a stand-in of the same kind when no NPC carries it
        public static Prop Find(string weapon)
        {
            if (!_built || _catalog.Count == 0) Build();
            if (string.IsNullOrEmpty(weapon) || KindOf(weapon) == Kind.None) return null;   // bare hands: nothing in them
            string w = Norm(weapon);
            Prop p;
            if (_catalog.TryGetValue(w, out p)) return p;
            foreach (var kv in _catalog) if (kv.Key.StartsWith(w) || w.StartsWith(kv.Key)) return kv.Value;
            string[] standIn;
            switch (KindOf(weapon))
            {
                case Kind.Pistol: standIn = w.Contains("smg") || w.Contains("borz") ? new[] { "borz_smg", "22_pipe_smg" } : new[] { "22_pipe_pistol", "folk_17" }; break;
                case Kind.Melee: standIn = new[] { "machete", "old_knife", "shiv" }; break;
                case Kind.Throw: return null;
                default:
                    if (w.Contains("slam") || w.Contains("rochester")) standIn = new[] { "slamberg_500_chopped", "rochester_m24_chopped" };
                    else if (w.Contains("smg") || w.Contains("borz")) standIn = new[] { "borz_smg", "22_pipe_smg" };
                    else if (w.Contains("crossbow")) standIn = new[] { "crossbow" };
                    else standIn = new[] { "akms", "m16a1", "akm_trash" };
                    break;
            }
            Prop stand = null;
            foreach (var s in standIn) if (_catalog.TryGetValue(s, out stand)) break;
            // no NPC carries it (redmark_m11, redmark_m11_scoped, the long rochester_m24 / slamberg_500): the weapon's own world model, placed
            // where the stand-in sits
            var item = ItemModel(w);
            if (item != null && stand != null) { Plugin.Verbose("Props: no NPC model for " + weapon + ", using its world item model (placed like " + stand.Key + ")"); return new Prop { Key = w, Source = item, Hand = stand.Hand, Owner = "item", Item = true, Ref = stand }; }
            if (stand != null) { Plugin.Verbose("Props: no NPC model for " + weapon + ", using " + stand.Key); return stand; }
            return null;
        }

        // the item prefab (a root prefab asset named like the weapon, with a mesh and a Rigidbody/Collider) - not the first-person model
        private static GameObject ItemModel(string w)
        {
            Prop cached;
            if (_items.TryGetValue(w, out cached)) return cached != null ? cached.Source : null;
            GameObject best = null;
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go == null || go.scene.IsValid() || go.transform.parent != null) continue;
                if (Norm(go.name) != w) continue;
                if (go.GetComponentInChildren<MeshRenderer>(true) == null) continue;
                if (go.GetComponent<Rigidbody>() == null && go.GetComponentInChildren<Collider>(true) == null) continue;
                best = go; break;
            }
            _items[w] = best != null ? new Prop { Key = w, Source = best } : null;
            if (best == null) Plugin.Verbose("Props: no world item model named " + w);
            return best;
        }

        // a render-only copy of the prop, parented to the given hand bone at the NPC's local pose
        public static GameObject Instantiate(Prop p, Transform hand, bool mirrorToRightHand)
        {
            var holder = new GameObject("FemalePlayer.PropHolder");
            holder.SetActive(false);
            var go = UnityEngine.Object.Instantiate(p.Source, holder.transform, false);
            go.name = "FemalePlayerProp_" + p.Key;
            // skinned parts (the crossbow's bow limbs / string): kept when their bones are inside the copy, else turned into a plain mesh in
            // its bind pose (their bones would be the NPC's)
            var keep = new HashSet<Component>();
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null) continue;
                bool inside = smr.rootBone == null || smr.rootBone.IsChildOf(go.transform);
                foreach (var b in smr.bones) if (b == null || !b.IsChildOf(go.transform)) { inside = false; break; }
                if (inside) { smr.updateWhenOffscreen = true; keep.Add(smr); continue; }
                var host = smr.gameObject; var mesh = smr.sharedMesh; var mats = smr.sharedMaterials;
                UnityEngine.Object.DestroyImmediate(smr);
                host.AddComponent<MeshFilter>().sharedMesh = mesh;
                host.AddComponent<MeshRenderer>().sharedMaterials = mats;
            }
            // LOD groups (item models): only the most detailed level is drawn, the others' renderers go
            foreach (var lg in go.GetComponentsInChildren<LODGroup>(true))
            {
                var lods = lg.GetLODs();
                if (lods.Length < 2) continue;
                var keepR = new HashSet<Renderer>(lods[0].renderers);
                for (int i = 1; i < lods.Length; i++)
                    foreach (var r in lods[i].renderers) if (r != null && !keepR.Contains(r)) UnityEngine.Object.DestroyImmediate(r);
            }
            // strip everything but transforms and mesh rendering (FSMs, lights, colliders, audio, animators ...) while nothing has woken up
            var comps = go.GetComponentsInChildren<Component>(true);
            for (int pass = 0; pass < 2; pass++)
                foreach (var c in comps)
                {
                    if (c == null || c is Transform || c is MeshFilter || c is MeshRenderer || keep.Contains(c)) continue;
                    try { UnityEngine.Object.DestroyImmediate(c); } catch (Exception) { }
                }
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
            var src = p.Source.transform;
            go.transform.SetParent(hand, false);
            go.transform.localPosition = src.localPosition;
            go.transform.localRotation = src.localRotation;
            if (mirrorToRightHand)
            {
                // the left-hand pose reflected across the body's middle plane (in Flexa's bind pose), expressed under the right hand
                var L = BindWorld("mixamorig:LeftHand"); var R = BindWorld("mixamorig:RightHand");
                var S = Matrix4x4.Scale(new Vector3(-1f, 1f, 1f));
                var P = Matrix4x4.TRS(src.localPosition, src.localRotation, Vector3.one);
                var M = R.inverse * S * L * P * S;
                go.transform.localPosition = M.GetColumn(3);
                go.transform.localRotation = Quaternion.LookRotation(M.GetColumn(2), M.GetColumn(1));
            }
            go.transform.localScale = src.localScale;
            go.SetActive(true);
            UnityEngine.Object.Destroy(holder);
            return go;
        }

        // the barrel direction in the prop's own space: toward the muzzle flash light ("fire_effect" / "muzzle*") when the raider model has one,
        // else along the longest side of its meshes, toward the end farther from the grip (the prop's origin sits in the hand)
        public static Vector3 Barrel(GameObject prop)
        {
            if (prop == null) return Vector3.zero;
            var t = prop.transform;
            foreach (var c in prop.GetComponentsInChildren<Transform>(true))
            {
                string n = c.name.ToLowerInvariant();
                if (c != t && (n.StartsWith("fire_effect") || n.StartsWith("muzzle")))
                {
                    var d = t.InverseTransformPoint(c.position);
                    if (d.sqrMagnitude > 1e-4f) return d.normalized;
                }
            }
            bool any = false; var b = new Bounds();
            foreach (var mf in prop.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var lp = t.InverseTransformPoint(mf.transform.TransformPoint(corner));
                    if (!any) { b = new Bounds(lp, Vector3.zero); any = true; } else b.Encapsulate(lp);
                }
            }
            if (!any) return Vector3.zero;
            var s = b.size; int axis = s.x >= s.y && s.x >= s.z ? 0 : s.y >= s.z ? 1 : 2;
            var dir = Vector3.zero;
            dir[axis] = Mathf.Abs(b.max[axis]) >= Mathf.Abs(b.min[axis]) ? 1f : -1f;
            return dir;
        }

        private static Matrix4x4 BindWorld(string bone)
        {
            float[] v;
            var m = Matrix4x4.identity;
            if (!Bindposes.Human.TryGetValue(bone, out v)) return m;
            for (int r = 0; r < 3; r++) for (int c = 0; c < 4; c++) m[r, c] = v[r * 4 + c];
            return m.inverse;
        }

        public static void OnSceneLoaded() { _built = false; _catalog.Clear(); _items.Clear(); }
    }
}
