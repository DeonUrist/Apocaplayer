// Runs Apocaplayer's locomotion decisions (LocoPlan.cs) for every weapon kind x every position and writes them as JSON for check.py.
// Build + run (mono):  mcs -out:locotest.exe ../../LocoPlan.cs LocoTest.cs && mono locotest.exe clips.txt > plan.json
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Apocaplayer;

static class LocoTest
{
    static HashSet<string> clips;
    static bool Has(string n) { return clips.Contains(n); }
    static string[] Dir = { "F", "FR", "R", "BR", "B", "BL", "L", "FL" };
    static void Main(string[] a)
    {
        if (a[0] == "marks")
        {   // the jump clips' marks: name lift apex touch
            foreach (var n in new[] { "Jump", "PistolJump", "RifleJumpUp", "RifleJumpDown" })
            {
                float l, ap, to; LocoPlan.JumpMarks(n, out l, out ap, out to);
                Console.WriteLine(n + " " + l.ToString("R") + " " + ap.ToString("R") + " " + to.ToString("R"));
            }
            return;
        }
        if (a[0] == "stop")
        {   // RifleWalkToStop's start for 48 strides (-1 = no stop clip)
            for (int k = 0; k < 48; k++) Console.WriteLine((k / 48.0).ToString("R") + " " + LocoPlan.StopStart(k / 48.0).ToString("R"));
            return;
        }
        clips = new HashSet<string>(File.ReadAllLines(a[0]));
        var sb = new StringBuilder("[\n"); bool first = true;
        string[] kinds = { "None", "Melee", "Pistol", "Rifle" };
        for (int k = 0; k < 4; k++)
        {
            var cases = new List<object[]>();   // label, m, r, sprint, dir(-1 none), crouch, aiming, shooting
            cases.Add(new object[] { "idle", 0f, 0f, 0f, -1, 0f, false, false });
            for (int d = 0; d < 8; d++) cases.Add(new object[] { "walk " + Dir[d], 1f, 0f, 0f, d, 0f, false, false });
            cases.Add(new object[] { "walk 20deg", 1f, 0f, 0f, 20, 0f, false, false });
            cases.Add(new object[] { "walk -70deg", 1f, 0f, 0f, -70, 0f, false, false });
            for (int d = 0; d < 8; d++) cases.Add(new object[] { "run " + Dir[d], 1f, 1f, 0f, d, 0f, false, false });
            cases.Add(new object[] { "run 115deg", 1f, 1f, 0f, 115, 0f, false, false });
            foreach (int d in new[] { 0, 1, 2 }) cases.Add(new object[] { "sprint " + Dir[d], 1f, 1f, 1f, d, 0f, false, false });
            cases.Add(new object[] { "crouch idle", 0f, 0f, 0f, -1, 1f, false, false });
            for (int d = 0; d < 8; d++) cases.Add(new object[] { "crouch " + Dir[d], 1f, 0f, 0f, d, 1f, false, false });
            if (k >= 2)
            {
                cases.Add(new object[] { "aim idle", 0f, 0f, 0f, -1, 0f, true, false });
                for (int d = 0; d < 8; d++) cases.Add(new object[] { "aim walk " + Dir[d], 1f, 0f, 0f, d, 0f, true, false });
                cases.Add(new object[] { "aim walk -60deg", 1f, 0f, 0f, -60, 0f, true, false });
                cases.Add(new object[] { "aim run F", 1f, 1f, 0f, 0, 0f, true, false });
                cases.Add(new object[] { "aim crouch idle", 0f, 0f, 0f, -1, 1f, true, false });
                cases.Add(new object[] { "aim crouch F", 1f, 0f, 0f, 0, 1f, true, false });
                cases.Add(new object[] { "fire idle", 0f, 0f, 0f, -1, 0f, false, true });
            }
            var w = new float[LocoPlan.BN];
            foreach (var c in cases)
            {
                string label = (string)c[0]; float m = (float)c[1], r = (float)c[2], sp = (float)c[3], cr = (float)c[5];
                int d = (int)c[4]; bool aim = (bool)c[6], shoot = (bool)c[7];
                float lx = 0f, lz = 0f;
                int deg = d >= 8 || d <= -8 ? d : d * 45;   // 0..7 = the eight directions, else degrees
                if (d != -1) { double ang = deg * Math.PI / 180.0; lx = (float)Math.Sin(ang); lz = (float)Math.Cos(ang); } else deg = -1;
                bool relaxed = LocoPlan.Relaxed(k, aim, shoot);
                float hip;
                LocoPlan.Weights(m, r, sp, lx, lz, cr, relaxed ? 1f : 0f, 75f, w, out hip);
                bool hold, sync;
                string up = LocoPlan.Upper(k, relaxed, aim, shoot, false, cr, m, r, lz, w[LocoPlan.B_RCLEFT], Has, out hold, out sync);
                var parts = new List<string>();
                for (int i = 0; i < LocoPlan.BN; i++)
                {
                    if (w[i] <= 0.001f) continue;
                    bool rev; string n = LocoPlan.Resolve(i, Has, out rev);
                    parts.Add(string.Format("{{\"slot\":{0},\"clip\":\"{1}\",\"w\":{2},\"rev\":{3},\"moving\":{4}}}", i, n, w[i].ToString("R"), rev ? "true" : "false", LocoPlan.Moving(i) ? "true" : "false"));
                }
                if (!first) sb.Append(",\n"); first = false;
                sb.AppendFormat("{{\"kind\":\"{0}\",\"case\":\"{1}\",\"relaxed\":{2},\"crouch\":{3},\"hipTurn\":{4},\"dir\":{5},\"upper\":{6},\"hold\":{7},\"sync\":{8},\"base\":[{9}]}}",
                    kinds[k], label, relaxed ? "true" : "false", cr.ToString("R"), hip.ToString("R"), deg, up == null ? "null" : "\"" + up + "\"", hold ? "true" : "false", sync ? "true" : "false", string.Join(",", parts.ToArray()));
            }
        }
        sb.Append("\n]");
        Console.WriteLine(sb.ToString());
    }
}
