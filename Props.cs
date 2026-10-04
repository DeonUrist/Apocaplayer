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
        internal sealed class Prop { public string Key; public GameObject Source; public string Hand; public string Owner; }

        private static readonly Dictionary<string, Prop> _catalog = new Dictionary<string, Prop>();
        private static bool _built;

        public enum Kind { None, Rifle, Pistol, Melee, Throw }

        public static Kind KindOf(string weapon)
        {
            if (string.IsNullOrEmpty(weapon) || weapon == "hands") return Kind.None;
            string w = weapon.ToLowerInvariant();
            if (w.Contains("blastlance") || w.Contains("grenade")) return Kind.Throw;
            if (w.Contains("shiv") || w.Contains("knife") || w.Contains("machete") || w.Contains("wrench") || w.Contains("pipe_wrench")) return Kind.Melee;
            if (w.Contains("pistol") || w.Contains("revolver") || w.Contains("folk_17")) return Kind.Pistol;
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
            if (string.IsNullOrEmpty(weapon)) return null;
            string w = Norm(weapon);
            Prop p;
            if (_catalog.TryGetValue(w, out p)) return p;
            foreach (var kv in _catalog) if (kv.Key.StartsWith(w) || w.StartsWith(kv.Key)) return kv.Value;
            string[] standIn;
            switch (KindOf(weapon))
            {
                case Kind.Pistol: standIn = new[] { "22_pipe_pistol", "folk_17" }; break;
                case Kind.Melee: standIn = new[] { "machete", "old_knife", "shiv" }; break;
                case Kind.Throw: return null;
                default:
                    if (w.Contains("slam") || w.Contains("rochester")) standIn = new[] { "slamberg_500_chopped", "rochester_m24_chopped" };
                    else if (w.Contains("smg") || w.Contains("borz")) standIn = new[] { "borz_smg", "22_pipe_smg" };
                    else if (w.Contains("crossbow")) standIn = new[] { "crossbow" };
                    else standIn = new[] { "akms", "m16a1", "akm_trash" };
                    break;
            }
            foreach (var s in standIn) if (_catalog.TryGetValue(s, out p)) { Plugin.Verbose("Props: no NPC model for " + weapon + ", using " + s); return p; }
            return null;
        }

        // a render-only copy of the prop, parented to the given hand bone at the NPC's local pose
        public static GameObject Instantiate(Prop p, Transform hand, bool mirrorToRightHand)
        {
            var holder = new GameObject("FemalePlayer.PropHolder");
            holder.SetActive(false);
            var go = UnityEngine.Object.Instantiate(p.Source, holder.transform, false);
            go.name = "FemalePlayerProp_" + p.Key;
            // strip everything but transforms and mesh rendering (FSMs, lights, colliders, audio ...) while nothing has woken up
            var comps = go.GetComponentsInChildren<Component>(true);
            for (int pass = 0; pass < 2; pass++)
                foreach (var c in comps)
                {
                    if (c == null || c is Transform || c is MeshFilter || c is MeshRenderer) continue;
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

        private static Matrix4x4 BindWorld(string bone)
        {
            float[] v;
            var m = Matrix4x4.identity;
            if (!Bindposes.Human.TryGetValue(bone, out v)) return m;
            for (int r = 0; r < 3; r++) for (int c = 0; c < 4; c++) m[r, c] = v[r * 4 + c];
            return m.inverse;
        }

        public static void OnSceneLoaded() { _built = false; _catalog.Clear(); }
    }
}
