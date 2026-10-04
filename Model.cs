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
        public static Mesh Full, NoHead, NoArms;
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

        // Character switched: the next body is built from the other model
        public static void Reset() { Full = NoHead = NoArms = null; BodyTex = null; _tried = _texTried = false; }

        public static Texture2D Body()
        {
            if (!_texTried) { _texTried = true; BodyTex = Load(Plugin.BodyTextureFile, Plugin.Female ? "Apocaplayer" : "Apocaplayer_male"); }
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
