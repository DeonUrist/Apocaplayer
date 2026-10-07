using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Apocaplayer
{
    // Her body meshes (built once from the .glb against Flexa's 22-bone skeleton) and textures.
    //  Full    - everything (third person, the shadow)
    //  NoHead  - head and neck-up removed (first person in a car: the camera sits in her head)
    //  NoArms  - first person (on foot, and in a car with a gun drawn): the whole body up to the collar - legs, hips, belly, chest,
    //            shoulders; head, neck and arms removed (the game draws its own first-person arms)
    internal static class Model
    {
        public static Mesh Full, NoHead, NoArms, ArmsOnly;
        public static Texture2D BodyTex, ArmsTex;
        private static bool _tried, _texTried, _armsTried;

        private static readonly HashSet<string> HeadBones = new HashSet<string> { "mixamorig:Head" };
        private static readonly HashSet<string> HeadNeckBones = new HashSet<string> { "mixamorig:Head", "mixamorig:Neck" };
        private static readonly HashSet<string> ArmBones = new HashSet<string>
        {
            "mixamorig:LeftArm", "mixamorig:LeftForeArm", "mixamorig:LeftHand",
            "mixamorig:RightArm", "mixamorig:RightForeArm", "mixamorig:RightHand",
        };

        public static bool Build(SkinnedMeshRenderer smr)
        {
            if (Full != null) return true;
            if (_tried) return false;
            _tried = true;
            string path = Plugin.ModPath(Plugin.BodyModelFile);
            SkinModel model;
            try { model = Gltf.Load(path); }
            catch (Exception e) { Plugin.Log.LogError("Model " + path + " could not be read: " + e.Message); return false; }

            var bones = smr.bones;
            var names = new string[bones.Length];
            for (int i = 0; i < bones.Length; i++) names[i] = bones[i] != null ? bones[i].name : null;
            var bp = new Matrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                float[] v;
                if (names[i] == null || !Bindposes.Human.TryGetValue(names[i], out v)) { Plugin.Log.LogError("No bind pose for bone " + names[i]); return false; }
                var m = Matrix4x4.identity;
                for (int r = 0; r < 3; r++) for (int c = 0; c < 4; c++) m[r, c] = v[r * 4 + c];
                bp[i] = m;
            }
            int rootBone = 0;
            for (int i = 0; i < bones.Length; i++) if (bones[i] != null && bones[i] == smr.rootBone) rootBone = i;

            int[] idx4; float[] w4; List<string> unmapped;
            Gltf.Weights(model, names, rootBone, out idx4, out w4, out unmapped);
            int n = model.VertexCount;
            var verts = new Vector3[n]; var nrms = new Vector3[n]; var uvs = new Vector2[n]; var bws = new BoneWeight[n];
            var dom = new string[n];
            for (int v = 0; v < n; v++)
            {
                verts[v] = new Vector3(model.Pos[v * 3], model.Pos[v * 3 + 1], model.Pos[v * 3 + 2]);
                nrms[v] = new Vector3(model.Nrm[v * 3], model.Nrm[v * 3 + 1], model.Nrm[v * 3 + 2]);
                uvs[v] = new Vector2(model.Uv[v * 2], model.Uv[v * 2 + 1]);
                bws[v] = new BoneWeight
                {
                    boneIndex0 = idx4[v * 4], weight0 = w4[v * 4],
                    boneIndex1 = idx4[v * 4 + 1], weight1 = w4[v * 4 + 1],
                    boneIndex2 = idx4[v * 4 + 2], weight2 = w4[v * 4 + 2],
                    boneIndex3 = idx4[v * 4 + 3], weight3 = w4[v * 4 + 3],
                };
                dom[v] = names[idx4[v * 4]];
            }

            Full = Make("Apocaplayer", verts, nrms, uvs, bws, bp, model.Tris, model.HasNormals);
            NoHead = Make("Apocaplayer_nohead", verts, nrms, uvs, bws, bp, Filter(model.Tris, dom, HeadBones, null), model.HasNormals);
            NoArms = Make("Apocaplayer_noarms", verts, nrms, uvs, bws, bp, Filter(model.Tris, dom, HeadNeckBones, ArmBones), model.HasNormals);
            // first person, nothing in hand: exactly the arm triangles NoArms leaves out (no gap at the shoulder), without head/neck
            ArmsOnly = Make("Apocaplayer_armsonly", verts, nrms, uvs, bws, bp, Only(model.Tris, dom, ArmBones, HeadNeckBones), model.HasNormals);

            // the skeleton of the file vs Flexa's (must be ~0: same armature)
            float worst = 0f; string worstName = "";
            var boneOf = new Dictionary<string, int>();
            for (int i = 0; i < names.Length; i++) if (names[i] != null && !boneOf.ContainsKey(names[i])) boneOf[names[i]] = i;
            for (int j = 0; j < model.Joints.Length; j++)
            {
                int b;
                if (!boneOf.TryGetValue(model.Joints[j], out b)) continue;
                Vector3 game = bp[b].inverse.GetColumn(3);
                var mine = new Vector3(model.JointPos[j * 3], model.JointPos[j * 3 + 1], model.JointPos[j * 3 + 2]);
                float d = (game - mine).magnitude;
                if (d > worst) { worst = d; worstName = model.Joints[j]; }
            }
            string msg = "Body " + Path.GetFileName(path) + ": " + model.Info + "; first-person meshes " + (NoArms.triangles.Length / 3) + " / " + (NoHead.triangles.Length / 3)
                         + " triangles; skeleton offset max " + (worst * 100f).ToString("0.0") + " cm (" + worstName + ")"
                         + (unmapped.Count > 0 ? "; joints without a game bone: " + string.Join(", ", unmapped.ToArray()) : "");
            if (worst > 0.05f) Plugin.Log.LogWarning(msg + " - the armature differs from Flexa's, she will look deformed");
            else Plugin.Log.LogInfo(msg);
            return true;
        }

        // drops every triangle that has a vertex bound mainly to one of the removed bones
        private static int[] Filter(int[] tris, string[] dom, HashSet<string> a, HashSet<string> b)
        {
            var keep = new List<int>(tris.Length);
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                bool drop = false;
                for (int k = 0; k < 3 && !drop; k++)
                {
                    string d = dom[tris[t + k]];
                    if (d != null && (a.Contains(d) || (b != null && b.Contains(d)))) drop = true;
                }
                if (!drop) { keep.Add(tris[t]); keep.Add(tris[t + 1]); keep.Add(tris[t + 2]); }
            }
            return keep.ToArray();
        }

        // keeps the triangles with a vertex bound mainly to one of the wanted bones and none to an excluded one
        private static int[] Only(int[] tris, string[] dom, HashSet<string> want, HashSet<string> not)
        {
            var keep = new List<int>();
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                bool any = false, bad = false;
                for (int k = 0; k < 3; k++)
                {
                    string d = dom[tris[t + k]];
                    if (d == null) continue;
                    if (want.Contains(d)) any = true;
                    if (not.Contains(d)) bad = true;
                }
                if (any && !bad) { keep.Add(tris[t]); keep.Add(tris[t + 1]); keep.Add(tris[t + 2]); }
            }
            return keep.ToArray();
        }

        private static Mesh Make(string name, Vector3[] v, Vector3[] n, Vector2[] uv, BoneWeight[] bw, Matrix4x4[] bp, int[] tris, bool hasN)
        {
            var mesh = new Mesh { name = name };
            if (v.Length > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = v;
            mesh.uv = uv;
            mesh.triangles = tris;
            if (hasN) mesh.normals = n; else mesh.RecalculateNormals();
            mesh.boneWeights = bw;
            mesh.bindposes = bp;
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            mesh.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return mesh;
        }

        // the same mesh seen from inside: faces reversed, normals flipped (first person: what the camera cuts open shows her inside)
        private static readonly Dictionary<Mesh, Mesh> _inside = new Dictionary<Mesh, Mesh>();
        public static Mesh Inside(Mesh m)
        {
            if (m == null) return null;
            Mesh r;
            if (_inside.TryGetValue(m, out r) && r != null) return r;
            var verts = new List<Vector3>(m.vertices);
            var uvs = new List<Vector2>(m.uv);
            var nrms = new List<Vector3>(m.normals);
            for (int i = 0; i < nrms.Count; i++) nrms[i] = -nrms[i];
            var bws = new List<BoneWeight>(m.boneWeights);
            var t = m.triangles;
            for (int i = 0; i + 2 < t.Length; i += 3) { int a = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = a; }
            var tris = new List<int>(t);
            int caps = 0;
            if (Full != null && m != Full) caps = Caps(m, Full, verts, uvs, nrms, bws, tris);
            r = new Mesh { name = m.name + "_inside" };
            if (verts.Count > 65535) r.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            r.SetVertices(verts);
            r.SetUVs(0, uvs);
            r.SetNormals(nrms);
            r.SetTriangles(tris, 0);
            r.boneWeights = bws.ToArray();
            r.bindposes = m.bindposes;
            r.RecalculateBounds();
            r.hideFlags = HideFlags.DontUnloadUnusedAsset;
            _inside[m] = r;
            if (caps > 0) Plugin.Verbose("First person: " + caps + " cut opening(s) closed in " + m.name);
            return r;
        }

        // the openings the first-person mesh has and the full body doesn't (neck where the head was cut off, shoulders without arms):
        // each boundary loop is closed with a fan around its centre, both sides, skinned like the loop. The full body's own open edges
        // (sleeves, hems, separate pieces) are left alone. Vertices at UV seams are welded by position to find the loops.
        private static int Caps(Mesh cut, Mesh full, List<Vector3> verts, List<Vector2> uvs, List<Vector3> nrms, List<BoneWeight> bws, List<int> tris)
        {
            var pos = cut.vertices;
            var uv = cut.uv;
            var bw = cut.boneWeights;
            var weldOf = new Dictionary<string, int>();
            var weld = new int[pos.Length];
            for (int i = 0; i < pos.Length; i++)
            {
                var p = pos[i];
                string k = Mathf.RoundToInt(p.x * 20000f) + "," + Mathf.RoundToInt(p.y * 20000f) + "," + Mathf.RoundToInt(p.z * 20000f);
                int w;
                if (!weldOf.TryGetValue(k, out w)) { w = weldOf.Count; weldOf[k] = w; }
                weld[i] = w;
            }
            var cutCount = new Dictionary<long, int>();
            var fullCount = new Dictionary<long, int>();
            CountEdges(cut.triangles, weld, cutCount);
            CountEdges(full.triangles, weld, fullCount);
            var rep = new Dictionary<int, int>();   // welded id -> a real vertex index
            for (int i = 0; i < pos.Length; i++) if (!rep.ContainsKey(weld[i])) rep[weld[i]] = i;
            var nb = new Dictionary<int, List<int>>();
            foreach (var kv in cutCount)
            {
                int fc;
                if (kv.Value != 1 || !fullCount.TryGetValue(kv.Key, out fc) || fc < 2) continue;
                int a = (int)(kv.Key >> 32), b = (int)(kv.Key & 0xffffffffL);
                List<int> l;
                if (!nb.TryGetValue(a, out l)) { l = new List<int>(); nb[a] = l; } l.Add(b);
                if (!nb.TryGetValue(b, out l)) { l = new List<int>(); nb[b] = l; } l.Add(a);
            }
            var used = new HashSet<long>();
            int loops = 0;
            foreach (var start in new List<int>(nb.Keys))
            {
                foreach (var first in nb[start])
                {
                    if (used.Contains(Key(start, first))) continue;
                    var loop = new List<int> { start };
                    used.Add(Key(start, first));
                    int prev = start, cur = first, guard = 0;
                    while (cur != start && guard++ < 10000)
                    {
                        loop.Add(cur);
                        int next = -1;
                        foreach (var c in nb[cur]) if (c != prev && !used.Contains(Key(cur, c))) { next = c; break; }
                        if (next < 0) foreach (var c in nb[cur]) if (!used.Contains(Key(cur, c))) { next = c; break; }
                        if (next < 0) break;
                        used.Add(Key(cur, next));
                        prev = cur; cur = next;
                    }
                    if (cur != start || loop.Count < 3) continue;
                    AddCap(loop, rep, pos, uv, bw, verts, uvs, nrms, bws, tris);
                    loops++;
                }
            }
            return loops;
        }

        private static long Key(int a, int b) { int lo = Mathf.Min(a, b), hi = Mathf.Max(a, b); return ((long)lo << 32) | (uint)hi; }

        private static void CountEdges(int[] t, int[] weld, Dictionary<long, int> count)
        {
            for (int i = 0; i + 2 < t.Length; i += 3)
                for (int e = 0; e < 3; e++)
                {
                    int a = weld[t[i + e]], b = weld[t[i + (e + 1) % 3]];
                    if (a == b) continue;
                    long k = Key(a, b);
                    int c; count.TryGetValue(k, out c); count[k] = c + 1;
                }
        }

        private static void AddCap(List<int> loop, Dictionary<int, int> rep, Vector3[] pos, Vector2[] uv, BoneWeight[] bw,
                                   List<Vector3> verts, List<Vector2> uvs, List<Vector3> nrms, List<BoneWeight> bws, List<int> tris)
        {
            int n = loop.Count;
            var c = Vector3.zero; var normal = Vector3.zero; var wsum = new Dictionary<int, float>();
            for (int i = 0; i < n; i++)
            {
                var p = pos[rep[loop[i]]]; var q = pos[rep[loop[(i + 1) % n]]];
                c += p;
                normal += new Vector3((p.y - q.y) * (p.z + q.z), (p.z - q.z) * (p.x + q.x), (p.x - q.x) * (p.y + q.y));   // Newell
                var w = bw[rep[loop[i]]];
                AddW(wsum, w.boneIndex0, w.weight0); AddW(wsum, w.boneIndex1, w.weight1); AddW(wsum, w.boneIndex2, w.weight2); AddW(wsum, w.boneIndex3, w.weight3);
            }
            c /= n;
            normal = normal.sqrMagnitude > 1e-12f ? normal.normalized : Vector3.up;
            var top = new List<KeyValuePair<int, float>>(wsum);
            top.Sort((x, y) => y.Value.CompareTo(x.Value));
            float tot = 0f; for (int i = 0; i < top.Count && i < 4; i++) tot += top[i].Value;
            var cw = new BoneWeight();
            if (top.Count > 0) { cw.boneIndex0 = top[0].Key; cw.weight0 = top[0].Value / tot; }
            if (top.Count > 1) { cw.boneIndex1 = top[1].Key; cw.weight1 = top[1].Value / tot; }
            if (top.Count > 2) { cw.boneIndex2 = top[2].Key; cw.weight2 = top[2].Value / tot; }
            if (top.Count > 3) { cw.boneIndex3 = top[3].Key; cw.weight3 = top[3].Value / tot; }
            for (int side = 0; side < 2; side++)
            {
                var nn = side == 0 ? normal : -normal;
                int b0 = verts.Count;
                for (int i = 0; i < n; i++) { int v = rep[loop[i]]; verts.Add(pos[v]); uvs.Add(uv[v]); nrms.Add(nn); bws.Add(bw[v]); }
                int ci = verts.Count; verts.Add(c); uvs.Add(uv[rep[loop[0]]]); nrms.Add(nn); bws.Add(cw);
                for (int i = 0; i < n; i++)
                {
                    int a = b0 + i, b = b0 + (i + 1) % n;
                    if (side == 0) { tris.Add(ci); tris.Add(a); tris.Add(b); } else { tris.Add(ci); tris.Add(b); tris.Add(a); }
                }
            }
        }

        private static void AddW(Dictionary<int, float> d, int bone, float w) { if (w <= 0f) return; float o; d.TryGetValue(bone, out o); d[bone] = o + w; }

        // Character switched: the next body is built from the other model
        public static void Reset() { _inside.Clear(); Full = NoHead = NoArms = ArmsOnly = null; BodyTex = null; _tried = _texTried = false; }

        public static Texture2D Body()
        {
            if (!_texTried) { _texTried = true; BodyTex = Load(Plugin.BodyTextureFile, Plugin.Female ? "Apocaplayer" : Plugin.IsMax ? "Apocaplayer_max" : "Apocaplayer_male"); }
            return BodyTex;
        }

        public static Texture2D Arms()
        {
            if (!_armsTried) { _armsTried = true; ArmsTex = Load(Plugin.ArmsTextureFile.Value, "Apocaplayer_arms"); }
            return ArmsTex;
        }

        private static Texture2D Load(string rel, string name)
        {
            string path = Plugin.ModPath(rel);
            try
            {
                var bytes = File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = name };
                if (!ImageConversion.LoadImage(tex, bytes, true)) throw new InvalidDataException("not a PNG/JPG");
                tex.wrapMode = TextureWrapMode.Repeat;
                tex.filterMode = FilterMode.Trilinear;
                tex.anisoLevel = 4;
                tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
                Plugin.Log.LogInfo("Texture " + Path.GetFileName(path) + " (" + tex.width + "x" + tex.height + ")");
                return tex;
            }
            catch (Exception e) { Plugin.Log.LogError("Texture " + path + " could not be loaded: " + e.Message); return null; }
        }
    }
}
