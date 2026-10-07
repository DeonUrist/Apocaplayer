using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Apocaplayer
{
    // (2.1.7) Aiming a gun, the game shoots from the camera (her eyes) - but the Mixamo aim clips hold the rifle ~30 cm lower, so the shots seemed
    // to leave from her mouth. While an aim / fire clip moves her hands, both hands are lifted (and pushed forward) together by a two-bone arm IK
    // (shoulders stay, elbows bend, the grip and the weapon pose in the hand are untouched) and her head tilts down onto the stock.
    // Per weapon and aim clip: lift (cm), head down (degrees), forward (cm) - in config/Apocaplayer/aim-lift.txt (its own file: weapon-poses.txt
    // is never touched). WeaponAdjustment on: Page Up / Page Down = lift, Home / End = head, Insert / Delete = forward, live.
    // The math here has no Unity types (tools/locotest checks it outside the game).
    internal static class AimLift
    {
        public static readonly string[] AimClips = { "RifleAim", "RifleCrouchAim", "RifleFire", "RifleCrouchFire", "PistolFire" };
        public static bool IsAimClip(string clip) { return Array.IndexOf(AimClips, clip) >= 0; }

        // the defaults when there is no entry: rifles lifted most of the way to the eyes (the right hand sits ~32 cm under them in RifleAim,
        // the sights ~8 cm over the grip), the head tilted onto the stock; pistols as they are (PistolFire holds the gun at eye level)
        public static float[] Default(string clip)
        {
            if (clip != null && clip.StartsWith("Rifle")) return new[] { 15f, 10f, 0f };
            return new[] { 0f, 0f, 0f };
        }

        private static readonly Dictionary<string, float[]> _v = new Dictionary<string, float[]>();
        private static string _file;
        public static void Load(string dir)
        {
            _file = Path.Combine(dir, "aim-lift.txt");
            _v.Clear();
            if (!File.Exists(_file)) return;
            foreach (var raw in File.ReadAllLines(_file))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int eq = line.IndexOf('='); if (eq <= 0) continue;
                var p = line.Substring(eq + 1).Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 3) continue;
                var v = new float[3]; bool ok = true;
                for (int i = 0; i < 3; i++) ok &= float.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]);
                if (ok) _v[line.Substring(0, eq).Trim()] = v;
            }
        }
        public static void Save()
        {
            if (_file == null) return;
            var sb = new StringBuilder();
            sb.AppendLine("# Apocaplayer aim lift: weapon|aim clip[@crouch] = lift (cm), head down (degrees), forward (cm). Edited live with WeaponAdjustment on:");
            sb.AppendLine("# Page Up / Page Down = lift, Home / End = head, Insert / Delete = forward (while she aims, or the aim clip picked with numpad 9/3).");
            var keys = new List<string>(_v.Keys); keys.Sort(StringComparer.Ordinal);
            foreach (var k in keys) sb.AppendLine(k + " = " + string.Join(", ", Array.ConvertAll(_v[k], x => x.ToString("0.#", CultureInfo.InvariantCulture))));
            File.WriteAllText(_file, sb.ToString());
        }
        // (2.1.8) standing and crouched have their own values: the key is the clip + "@crouch" when crouched; a crouched aim without its own entry
        // starts from the standing one (so a tuned weapon doesn't jump back to the default)
        public static string Key(string clip, bool crouched) { return crouched ? clip + "@crouch" : clip; }
        public static float[] Get(string weapon, string clip, bool crouched)
        {
            float[] v;
            if (_v.TryGetValue(weapon + "|" + Key(clip, crouched), out v)) return v;
            if (crouched && _v.TryGetValue(weapon + "|" + clip, out v)) return v;
            return Default(clip);
        }
        public static bool Own(string weapon, string clip, bool crouched) { return _v.ContainsKey(weapon + "|" + Key(clip, crouched)); }
        public static void Set(string weapon, string clip, bool crouched, float[] v) { _v[weapon + "|" + Key(clip, crouched)] = v; }

        // ---- two-bone IK: the new elbow position so the hand reaches t, the arm bending in the plane it bends in now
        //   s, e, h = shoulder, elbow, hand positions; returns the elbow (the caller turns the upper arm onto it, then the forearm onto t)
        public static double[] Elbow(double[] s, double[] e, double[] h, double[] t)
        {
            double a = Dist(s, e), b = Dist(e, h);
            var st = Sub(t, s); double c = Len(st);
            if (c < 1e-6 || a < 1e-6 || b < 1e-6) return (double[])e.Clone();
            c = Math.Max(Math.Abs(a - b) + 1e-4, Math.Min(a + b - 1e-4, c));
            var u = Mul(st, 1.0 / Len(st));
            var se = Sub(e, s);
            var p = Sub(se, Mul(u, Dot(se, u)));          // where the elbow sticks out from the shoulder-target line
            double pl = Len(p);
            if (pl < 1e-6) { p = Sub(new[] { 0.0, -1.0, 0.0 }, Mul(u, -u[1])); pl = Len(p); if (pl < 1e-6) return (double[])e.Clone(); }
            p = Mul(p, 1.0 / pl);
            double cosA = Math.Max(-1.0, Math.Min(1.0, (a * a + c * c - b * b) / (2 * a * c))), sinA = Math.Sqrt(1 - cosA * cosA);
            return Add(s, Add(Mul(u, a * cosA), Mul(p, a * sinA)));
        }
        static double[] Sub(double[] x, double[] y) { return new[] { x[0] - y[0], x[1] - y[1], x[2] - y[2] }; }
        static double[] Add(double[] x, double[] y) { return new[] { x[0] + y[0], x[1] + y[1], x[2] + y[2] }; }
        static double[] Mul(double[] x, double k) { return new[] { x[0] * k, x[1] * k, x[2] * k }; }
        static double Dot(double[] x, double[] y) { return x[0] * y[0] + x[1] * y[1] + x[2] * y[2]; }
        static double Len(double[] x) { return Math.Sqrt(Dot(x, x)); }
        static double Dist(double[] x, double[] y) { return Len(Sub(x, y)); }
    }
}
