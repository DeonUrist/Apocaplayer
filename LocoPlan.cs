using System;

namespace Apocaplayer
{
    // 2.1.1: the DECISIONS of the Mixamo locomotion, with no Unity types, so tools/locotest can run every weapon x every position outside the
    // game and the offline pose check (tools/locotest/check.py) can draw and measure what she would do. Loco.cs applies them to the playables.
    //   which legs (the pack's aiming set or the relaxed set), the base slots' weights, the hips' turn, which clip moves the hands,
    //   and each moving clip's phase (all clips put at the same point of the stride: LEFT foot highest at stride 0).
    internal static class LocoPlan
    {
        public const int K_NONE = 0, K_MELEE = 1, K_PISTOL = 2, K_RIFLE = 3, K_THROW = 4;
        // base slots: 0..33 the pack's aiming set, 34..40 the relaxed set (idle, walk F / R / B / L, run, sprint)
        public const int B_IDLE = 0, B_WALK = 1, B_RUN = 9, B_SPRINT = 17, B_CIDLE = 25, B_CWALK = 26, B_RIDLE = 34, B_RWALK = 35, B_RRUN = 39, B_RSPRINT = 40, BN = 41;
        public static readonly string[] RelaxedNames = { "Idle", "RifleWalkLow", "WalkStrafeRight", "WalkBack", "WalkStrafeLeft", "RifleRunLow", "RifleSprint" };
        static readonly string[] DirWord = { "", "ForwardRight", "Right", "BackRight", "Back", "BackLeft", "Left", "ForwardLeft" };   // 0 = forward, clockwise

        static string DirName(string tier, string strafe, int d) { return (d == 2 || d == 6) ? strafe + DirWord[d] : tier + DirWord[d]; }
        public static bool Moving(int i) { return i != B_IDLE && i != B_CIDLE && i != B_RIDLE; }
        public static string SlotName(int i)
        {
            if (i == B_IDLE) return "RifleIdle";
            if (i == B_CIDLE) return "RifleCrouchIdle";
            if (i >= B_RIDLE) return RelaxedNames[i - B_RIDLE];
            if (i < B_RUN) return "Rifle" + DirName("Walk", "Strafe", i - B_WALK);
            if (i < B_SPRINT) return "Rifle" + DirName("Run", "RunStrafe", i - B_RUN);
            if (i < B_CIDLE) return "Rifle" + DirName("Sprint", "SprintStrafe", i - B_SPRINT);
            return "Rifle" + DirName("CrouchWalk", "CrouchStrafe", i - B_CWALK);
        }
        static int Cardinal(int d) { return d == 1 || d == 7 ? 0 : d == 3 || d == 5 ? 4 : d; }   // a diagonal's forward / back
        static string Pick(Func<string, bool> has, params string[] names) { foreach (var n in names) if (n != null && has(n)) return n; return null; }

        // the clip really used in slot i (stand-ins for what the bundle lacks); reverse = a forward clip played backward
        public static string Resolve(int i, Func<string, bool> has, out bool reverse)
        {
            reverse = false;
            string n = SlotName(i);
            if (has(n)) return n;
            if (i == B_IDLE) return Pick(has, "RifleIdle", "Idle");
            if (i == B_CIDLE) return Pick(has, "RifleCrouchIdle", "CrouchIdle", "RifleIdle");
            if (i >= B_RIDLE)
            {
                switch (i - B_RIDLE)
                {
                    case 0: return Pick(has, "Idle", "RifleIdle");
                    case 1: return Pick(has, "RifleWalkLow", "Walk", "RifleWalk");
                    case 2: return Pick(has, "WalkStrafeRight", "RifleStrafeRight", "RifleWalkLow", "RifleWalk");
                    case 3: return Pick(has, "WalkBack", "RifleWalkBack", "RifleWalkLow", "RifleWalk");
                    case 4: return Pick(has, "WalkStrafeLeft", "RifleStrafeLeft", "RifleWalkLow", "RifleWalk");
                    case 5: return Pick(has, "RifleRunLow", "Run", "RifleRun");
                    default: return Pick(has, "RifleSprint", "RifleRunLow", "RifleRun");
                }
            }
            int d = i >= B_CWALK ? i - B_CWALK : i >= B_SPRINT ? i - B_SPRINT : i >= B_RUN ? i - B_RUN : i - B_WALK;
            string fwdWalk = Pick(has, "RifleWalk", "RifleWalkLow"), fwdRun = Pick(has, "RifleRun", "RifleRunLow");
            string r = null;
            if (i >= B_CWALK) r = Pick(has, SlotName(B_CWALK + Cardinal(d)));
            else if (i >= B_SPRINT) r = Pick(has, SlotName(B_SPRINT + Cardinal(d)), SlotName(B_RUN + d), SlotName(B_RUN + Cardinal(d)));
            else if (i >= B_RUN) r = Pick(has, SlotName(B_RUN + Cardinal(d)), d == 0 ? fwdRun : null);
            else r = Pick(has, SlotName(B_WALK + Cardinal(d)), d == 0 ? fwdWalk : null);
            if (r == null && (d == 0 || d == 1 || d == 7))
                r = i >= B_CWALK ? Pick(has, "RifleCrouchWalk", fwdWalk) : i >= B_RUN && i < B_CIDLE ? Pick(has, fwdRun, fwdWalk) : fwdWalk;
            if (r == null && d >= 3 && d <= 5)
            {   // backward without any back clip: the forward one reversed
                r = i >= B_CWALK ? Pick(has, "RifleCrouchWalk", fwdWalk) : i >= B_RUN && i < B_CIDLE ? Pick(has, fwdRun, fwdWalk) : fwdWalk;
                reverse = true;
            }
            if (r == null) r = i >= B_CWALK ? Pick(has, "RifleCrouchWalk", fwdWalk) : fwdWalk;
            return r;
        }

        // which legs: the relaxed set (natural feet) or the pack's aiming set (bladed stance, the right foot turned out)
        //   bare hands, throwables: relaxed (the aim of a throw / punch is the chest's, PunchAim / ThrowAim)
        //   melee weapons: always the aiming stance (a fighting stance - Denis: "good for melee weapons")
        //   pistol / rifle: relaxed until she aims (right mouse) or shoots
        public static bool Relaxed(int kind, bool aiming, bool shooting)
        {
            if (kind == K_MELEE) return false;
            if (kind == K_PISTOL || kind == K_RIFLE) return !(aiming || shooting);
            return true;
        }

        static float Clamp01(float v) { return v < 0f ? 0f : v > 1f ? 1f : v; }
        static float Clamp(float v, float a, float b) { return v < a ? a : v > b ? b : v; }

        // the base layer's weights. m = moving 0..1, r = running 0..1, sprint 0..1, (lx, lz) = her velocity in her own frame (x right, z forward),
        // crouch 0..1, relax 0..1 (smoothed Relaxed). hipTurn = degrees the hips turn toward the way she runs (relaxed running only)
        public static void Weights(float m, float r, float sprint, float lx, float lz, float crouch, float relax, float legsTurnMax, float[] w, out float hipTurn)
        {
            hipTurn = 0f;
            for (int i = 0; i < BN; i++) w[i] = 0f;
            float sang = (lx * lx + lz * lz) > 1e-8f ? (float)(Math.Atan2(lx, lz) * 180.0 / Math.PI) : 0f;   // + = to her right
            float ang = sang < 0f ? sang + 360f : sang;
            float bin = ang / 45f; int i0 = (int)Math.Floor(bin) % 8, i1 = (i0 + 1) % 8; float f = bin - (float)Math.Floor(bin);
            // the pack's neighbouring directions have different stances (a strafe crosses its feet, a diagonal doesn't): a half-and-half mix steps
            // sideways to neither - so they only cross-fade in the middle 15° between two directions (the keys give the 8 directions exactly)
            f = Clamp01((f - 0.5f) / 0.33f + 0.5f);
            float st = 1f - crouch, aimS = st * (1f - relax), relS = st * relax;
            w[B_IDLE] = (1f - m) * aimS;
            w[B_RIDLE] = (1f - m) * relS;
            w[B_CIDLE] = (1f - m) * crouch;
            // the aiming set: 8 directions in every tier; crouched always the pack (relaxed or not)
            float walk = m * (1f - r) * aimS, run = m * r * (1f - sprint) * aimS, spr = m * r * sprint * aimS, cw = m * crouch;
            w[B_WALK + i0] += walk * (1f - f); w[B_WALK + i1] += walk * f;
            w[B_RUN + i0] += run * (1f - f); w[B_RUN + i1] += run * f;
            w[B_SPRINT + i0] += spr * (1f - f); w[B_SPRINT + i1] += spr * f;
            w[B_CWALK + i0] += cw * (1f - f); w[B_CWALK + i1] += cw * f;
            // the relaxed set: walking = the four real directions blended, all clips at the same point of the stride (see Phase), so a diagonal
            // is a diagonal step - and the same on both sides (the right strafe is the left one mirrored)
            float rwalk = m * (1f - r) * relS, rrun = m * r * relS;
            float qb = ang / 90f; int q0 = (int)Math.Floor(qb) % 4, q1 = (q0 + 1) % 4; float qf = qb - (float)Math.Floor(qb);
            w[B_RWALK + q0] += rwalk * (1f - qf); w[B_RWALK + q1] += rwalk * qf;
            // relaxed running: the forward run / sprint with the hips turned toward the way she runs (the chest is kept on the camera by the upper
            // rig); running backward (more than 115° off her facing): the pack's straight run back, the hips turned so it points the way she goes
            // (the pack's back diagonals have the aiming stance's turned-out feet)
            float back = Clamp01((Math.Abs(sang) - 110f) / 10f);
            float fwdRun = rrun * (1f - back), backRun = rrun * back;
            w[B_RRUN] += fwdRun * (1f - sprint); w[B_RSPRINT] += fwdRun * sprint;
            w[B_RUN + 4] += backRun * (1f - sprint);
            w[B_SPRINT + 4] += backRun * sprint;
            float fromBack = sang > 0f ? sang - 180f : sang + 180f;      // the run back's own direction is 180°
            if (m > 0.05f) hipTurn = (Clamp(sang, -legsTurnMax, legsTurnMax) * (1f - back) + Clamp(fromBack, -legsTurnMax, legsTurnMax) * back) * (r * relS);
        }

        // the upper source (UpperRig) - which clip moves the hands; null = the legs' own clip moves them too
        //   hold = its first frame, frozen (aiming without firing); sync = walks / runs in step with the legs
        public static string Upper(int kind, bool relaxed, bool aiming, bool shooting, bool reloading, float crouch, float m, float runW, float lz, Func<string, bool> has, out bool hold, out bool sync)
        {
            hold = false; sync = false;
            bool moving = m > 0.5f, running = runW > 0.5f, crouched = crouch > 0.5f;
            if (kind == K_RIFLE)
            {
                if (reloading && has("RifleReload")) return "RifleReload";
                if (aiming || shooting)
                {
                    string aim = crouched && has("RifleCrouchAim") ? "RifleCrouchAim" : has("RifleAim") ? "RifleAim" : null;
                    string fire = crouched && has("RifleCrouchFire") ? "RifleCrouchFire" : has("RifleFire") ? "RifleFire" : null;
                    if (shooting && fire != null) return fire;
                    if (aim != null) return aim;
                    if (fire != null) { hold = true; return fire; }
                    return null;
                }
                // crouched: the pack's crouch clips carry the rifle themselves (crouched arms)
                if (crouched) return null;
                // standing relaxed: the low-ready rifle hands over whatever legs (unarmed strafes / walk back / idle)
                if (moving && running) { sync = true; return Pick(has, "RifleRunLow", "RifleRun"); }
                if (moving) { sync = true; return Pick(has, "RifleWalkLow", "RifleWalk"); }
                return Pick(has, "RifleIdleLow", "RifleIdle");
            }
            if (kind == K_PISTOL)
            {
                if (reloading && has("PistolReload")) return "PistolReload";
                if ((aiming || shooting) && has("PistolFire")) { hold = !shooting; return "PistolFire"; }
                if (moving && running && has("PistolRun")) { sync = true; return "PistolRun"; }
                return has("PistolIdle") ? "PistolIdle" : null;
            }
            // bare hands, melee, throwables
            if (crouched && has("CrouchIdle")) return "CrouchIdle";
            if (moving && running && has("Run")) { sync = true; return "Run"; }
            if (moving && has("Walk")) { sync = true; return lz < -0.1f && has("WalkBack") ? "WalkBack" : "Walk"; }
            return has("Idle") ? "Idle" : null;
        }

        // ---- the stride: every moving clip (base and upper) is set to the SAME point of its step cycle. A clip's phase offset is where its
        // left foot is highest above the right one (the first harmonic of left-minus-right foot height over one loop), so clips from different
        // sources (the pack, single Mixamo downloads, a mirrored strafe - which leads with the other foot) still step together.
        public static float PhaseFromHeights(float[] leftMinusRight)
        {
            int n = leftMinusRight.Length;
            if (n < 4) return 0f;
            double s = 0, c = 0, mean = 0;
            for (int k = 0; k < n; k++) mean += leftMinusRight[k];
            mean /= n;
            for (int k = 0; k < n; k++)
            {
                double a = 2.0 * Math.PI * k / n, v = leftMinusRight[k] - mean;
                s += v * Math.Sin(a); c += v * Math.Cos(a);
            }
            if (Math.Sqrt(s * s + c * c) * 2.0 / n < 0.01) return -1f;      // no steps (an idle): no phase
            double ph = Math.Atan2(s, c) / (2.0 * Math.PI);
            if (ph < 0) ph += 1.0;
            return (float)ph;
        }
        // the normalized time of a clip with phase offset off at the shared stride (off < 0: unknown -> the stride itself)
        public static double Time01(double stride, float off, bool reverse)
        {
            double o = off < 0f ? 0f : off;
            double t = reverse ? o - stride : o + stride;      // played backward: the left foot is still highest at stride 0
            t -= Math.Floor(t);
            return t;
        }
    }
}
