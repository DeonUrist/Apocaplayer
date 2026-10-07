// Offline test of ModAPI's animation decisions (CharPlan.cs, the player's own logic for other mods' characters): every weapon kind through every
// situation an NPC mod drives (stand, walk, run, sprint, strafe, back, crouch, aim, fire, reload whole / one round at a time, pump, turn in
// place, jump, throw, strike, kick, walk-to-stop), 60 frames a second, with the bundle's real clip lengths and stride phases (clipinfo.txt).
// Checks every frame (only player clips shown, weights sum to 1, no NaN, no jumps in any weight, clip times inside the clips) and every
// scenario's expected result; writes frames.json (selected frames) for pose_check.py.
// Build + run (mono):  mcs -out:modapitest.exe ../../LocoPlan.cs ../../CharPlan.cs ../../AimLift.cs ModApiTest.cs && mono modapitest.exe clipinfo.txt frames.json
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Apocaplayer;

static class ModApiTest
{
    static Dictionary<string, float> len = new Dictionary<string, float>(), ph = new Dictionary<string, float>();
    static HashSet<string> loop = new HashSet<string>(), bundle = new HashSet<string>(), player;
    static int fails, checks;
    static string scen = "";
    static StringBuilder json = new StringBuilder();
    static bool firstJson = true;
    static CultureInfo ci = CultureInfo.InvariantCulture;
    const float DT = 1f / 60f;
    static readonly string[] KN = { "None", "Melee", "Pistol", "Rifle", "Throw" };

    static void Check(bool ok, string what)
    {
        checks++;
        if (!ok) { fails++; if (fails < 200) Console.WriteLine("FAIL [" + scen + "] " + what); }
    }

    static CharPlan New(int kind)
    {
        var p = new CharPlan();
        p.Has = n => n != null && bundle.Contains(n);
        p.Length = n => len.ContainsKey(n) ? len[n] : 1f;
        p.Looping = n => loop.Contains(n);
        p.PhaseOf = n => ph.ContainsKey(n) ? ph[n] : -1f;
        p.NativeOf = (n, f) => f;
        p.Kind = kind;
        return p;
    }

    // one simulated frame with the per-frame invariants
    static float T;
    static float pRig = -1, pAct = -1, pUp = -1; static float[] pW;
    static void Step(CharPlan p, bool first)
    {
        p.Step(T, DT);
        T += DT;
        float sum = 0f;
        for (int i = 0; i < LocoPlan.BN; i++)
        {
            float w = p.W[i];
            Check(!float.IsNaN(w) && w >= -1e-4f && w <= 1.0001f, "weight " + i + " = " + w);
            sum += w;
            if (w > 0.001f)
            {
                Check(player.Contains(p.SlotClip[i]) && bundle.Contains(p.SlotClip[i]), "base clip " + p.SlotClip[i] + " not a player clip");
                Check(p.SlotTime[i] >= -1e-3f && p.SlotTime[i] <= len[p.SlotClip[i]] + 1e-3f && !float.IsNaN(p.SlotTime[i]), "slot time " + p.SlotClip[i] + " " + p.SlotTime[i]);
            }
            if (pW != null && !first) Check(Math.Abs(w - pW[i]) < 0.26f, "base weight jump slot " + i + " (" + p.SlotClip[i] + ") " + pW[i] + " -> " + w);
        }
        Check(Math.Abs(sum - 1f) < 0.01f, "base weights sum " + sum);
        if (pW == null) pW = new float[LocoPlan.BN];
        Array.Copy(p.W, pW, LocoPlan.BN);
        Shown(p.RigEff, p.RigClip, p.RigTime, "hands");
        if (p.RigOld != null && p.RigX < 1f) Shown(p.RigEff * (1 - p.RigX), p.RigOld, p.RigOldTime, "hands(old)");
        Shown(p.ActionW, p.ActionClip, p.ActionTime, "action");
        Shown(p.UpperW, p.UpperClip, p.UpperTime, "upper");
        if (p.UpperOld != null && p.UpperX < 1f) Shown(p.UpperW * (1 - p.UpperX), p.UpperOld, p.UpperOldTime, "upper(old)");
        foreach (var v in new[] { p.RigEff, p.ActionW, p.UpperW, p.RigX, p.UpperX, p.AimLiftW, p.HipTurn, p.ChestFollow }) Check(!float.IsNaN(v), "NaN");
        Check(p.RigEff >= 0f && p.RigEff <= 1.0001f && p.ActionW >= 0f && p.ActionW <= 1.0001f && p.UpperW >= 0f && p.UpperW <= 1.0001f, "layer weight range");
        if (!first)
        {
            if (pRig >= 0) Check(Math.Abs(p.RigEff - pRig) <= DT / CharPlan.RigCross * 1.02f + Math.Abs(p.ActionW - pAct) + Math.Abs(p.UpperW - pUp) + 1e-3f, "hands weight jump " + pRig + " -> " + p.RigEff);
            if (pAct >= 0) Check(Math.Abs(p.ActionW - pAct) <= DT * 10.01f + 1e-4f, "action weight jump " + pAct + " -> " + p.ActionW);
            if (pUp >= 0) Check(Math.Abs(p.UpperW - pUp) <= DT / CharPlan.UpperIn * 1.01f + 1e-4f, "upper weight jump " + pUp + " -> " + p.UpperW);
        }
        pRig = p.RigEff; pAct = p.ActionW; pUp = p.UpperW;
        float ps = 0f; foreach (var kv in p.PoseW) { ps += kv.Value; Check(player.Contains(kv.Key), "weapon pose of a non-player clip " + kv.Key); }
        Check(p.PoseW.Count == 0 || Math.Abs(ps - 1f) < 0.02f, "weapon pose weights sum " + ps);
        Check(Math.Abs(p.HipTurn) <= 75.01f, "hip turn " + p.HipTurn);
    }
    static void Shown(float w, string clip, float t, string what)
    {
        if (w <= 0.001f) return;
        Check(clip != null && clip != "" && player.Contains(clip) && bundle.Contains(clip), what + " clip '" + clip + "' is not a player clip");
        if (clip != null && len.ContainsKey(clip)) Check(t >= -1e-3f && t <= len[clip] + 1e-3f && !float.IsNaN(t), what + " time " + clip + " " + t + " / " + len[clip]);
    }

    // a scenario: setup, then frames; each(p, t) sets the inputs; returns the plan for checks after
    static CharPlan Run(string name, int kind, float seconds, Action<CharPlan, float> each, Action<CharPlan, float> after = null, string dumpAt = null)
    {
        scen = name; T = 100f; pW = null; pRig = pAct = pUp = -1;
        var p = New(kind);
        var dumps = dumpAt != null ? dumpAt.Split(',').Select(s => float.Parse(s, ci)).ToList() : new List<float>();
        int n = (int)Math.Round(seconds / DT);
        for (int f = 0; f < n; f++)
        {
            float t = f * DT;
            each(p, t);
            Step(p, f == 0);
            if (after != null) after(p, t);
            foreach (var d in dumps) if (Math.Abs(t - d) < DT * 0.5f) Dump(name + "@" + d.ToString("0.00", ci), p);
        }
        return p;
    }

    static void Dump(string id, CharPlan p)
    {
        var sb = new StringBuilder();
        sb.Append(firstJson ? "" : ",\n"); firstJson = false;
        sb.Append("{\"id\":\"").Append(id).Append("\",\"base\":[");
        bool f = true;
        for (int i = 0; i < LocoPlan.BN; i++)
        {
            if (p.W[i] <= 0.001f) continue;
            sb.Append(f ? "" : ",").Append("{\"clip\":\"").Append(p.SlotClip[i]).Append("\",\"w\":").Append(p.W[i].ToString("R", ci)).Append(",\"t\":").Append(p.SlotTime[i].ToString("R", ci)).Append("}");
            f = false;
        }
        sb.Append("],\"hipTurn\":").Append(p.HipTurn.ToString("R", ci));
        sb.Append(",\"rig\":\"").Append(p.RigClip).Append("\",\"rigT\":").Append(p.RigTime.ToString("R", ci)).Append(",\"rigEff\":").Append(p.RigEff.ToString("R", ci));
        sb.Append(",\"rigOld\":\"").Append(p.RigOld ?? "").Append("\",\"rigOldT\":").Append(p.RigOldTime.ToString("R", ci)).Append(",\"rigX\":").Append(p.RigX.ToString("R", ci));
        sb.Append(",\"act\":\"").Append(p.ActionClip).Append("\",\"actT\":").Append(p.ActionTime.ToString("R", ci)).Append(",\"actW\":").Append(p.ActionW.ToString("R", ci));
        sb.Append(",\"up\":\"").Append(p.UpperClip).Append("\",\"upT\":").Append(p.UpperTime.ToString("R", ci)).Append(",\"upW\":").Append(p.UpperW.ToString("R", ci));
        sb.Append(",\"follow\":").Append(p.ChestFollow.ToString("R", ci)).Append(",\"lift\":").Append(p.AimLiftW.ToString("R", ci)).Append(",\"liftClip\":\"").Append(p.AimLiftClip ?? "").Append("\"");
        sb.Append(",\"crouch\":").Append(p.CrouchW.ToString("R", ci)).Append("}");
        json.Append(sb);
    }

    static void Move(CharPlan p, float deg, float speed) { p.Lx = (float)Math.Sin(deg * Math.PI / 180) * speed; p.Lz = (float)Math.Cos(deg * Math.PI / 180) * speed; }
    static float W(CharPlan p, params int[] slots) { float s = 0; foreach (var i in slots) s += p.W[i]; return s; }
    static float Range(int from, int count, CharPlan p) { float s = 0; for (int i = from; i < from + count; i++) s += p.W[i]; return s; }

    static void Main(string[] args)
    {
        foreach (var line in File.ReadAllLines(args[0]))
        {
            var x = line.Split(' '); if (x.Length < 4) continue;
            bundle.Add(x[0]); len[x[0]] = float.Parse(x[1], ci); if (x[2] == "1") loop.Add(x[0]);
            float f = float.Parse(x[3], ci); if (f >= 0) ph[x[0]] = f;
        }
        player = new HashSet<string>(CharPlan.PlayerClips(n => bundle.Contains(n)));
        Console.WriteLine("player clips in the bundle: " + player.Count + " of " + bundle.Count + "; left out: " + string.Join(", ", bundle.Where(b => !player.Contains(b)).OrderBy(b => b)));
        Check(!player.Any(c => c.Contains("Death")), "death clips must not be offered");
        json.Append("[\n");

        int[] kinds = { LocoPlan.K_NONE, LocoPlan.K_MELEE, LocoPlan.K_PISTOL, LocoPlan.K_RIFLE };
        // ---------------- locomotion, every kind x every situation, with and without aiming
        foreach (int k in kinds)
        {
            string K = KN[k]; bool gun = k == LocoPlan.K_PISTOL || k == LocoPlan.K_RIFLE;
            foreach (bool aim in gun ? new[] { false, true } : new[] { false })
            {
                string A = aim ? "+aim" : "";
                var p = Run(K + A + " idle", k, 2f, (q, t) => { q.Aim = aim; }, null, "1.5");
                if (k == LocoPlan.K_RIFLE) Check(p.RigClip == (aim ? "RifleAim" : "RifleIdle") && p.RigEff > 0.99f, "rifle idle hands " + p.RigClip + " " + p.RigEff);
                if (k == LocoPlan.K_PISTOL) Check(p.RigClip == (aim ? "PistolFire" : "PistolIdle") && p.RigEff > 0.99f && (!aim || p.RigTime == 0f), "pistol idle hands " + p.RigClip + " t " + p.RigTime);
                if (k == LocoPlan.K_NONE || k == LocoPlan.K_MELEE) Check(p.RigClip == "Idle", "unarmed idle hands " + p.RigClip);
                bool relaxedLegs = !(k == LocoPlan.K_MELEE || aim);
                Check(relaxedLegs ? p.W[LocoPlan.B_RIDLE] > 0.99f : p.W[LocoPlan.B_IDLE] > 0.99f, "idle legs relaxed=" + relaxedLegs);
                if (aim) Check(p.AimLiftW > 0.99f && p.AimLiftClip == p.RigClip, "aim lift on while aiming");
                else Check(p.AimLiftW < 0.01f, "no aim lift when not aiming");

                foreach (var mv in new[] { new { n = "walk", s = 1.4f }, new { n = "run", s = 3.8f }, new { n = "sprint", s = 5.5f } })
                    for (int d = 0; d < 8; d++)
                    {
                        float deg = d * 45f;
                        string nm = K + A + " " + mv.n + " " + deg;
                        float lastStride = -1; int advanced = 0;
                        p = Run(nm, k, 2.5f, (q, t) => { q.Aim = aim; Move(q, deg, mv.s); }, (q, t) => { }, d % 2 == 0 && mv.n != "sprint" ? "2.0,2.2" : null);
                        float moving = 0; for (int i = 0; i < LocoPlan.BN; i++) if (LocoPlan.Moving(i)) moving += p.W[i];
                        Check(moving > 0.99f, "moving legs " + moving);
                        if (mv.n == "walk") Check(relaxedLegs ? W(p, LocoPlan.B_RWALK, LocoPlan.B_RWALK + 1, LocoPlan.B_RWALK + 2, LocoPlan.B_RWALK + 3) > 0.99f : Range(LocoPlan.B_WALK, 8, p) > 0.99f, "walk set relaxed=" + relaxedLegs);
                        if (mv.n == "sprint" && !relaxedLegs) Check(Range(LocoPlan.B_SPRINT, 8, p) > 0.99f, "sprint set");
                        if (gun && aim) Check(p.RigClip == (k == LocoPlan.K_RIFLE ? "RifleAim" : "PistolFire") && p.RigEff > 0.99f, "aiming hands while moving " + p.RigClip);
                    }
                // crouched: idle and 8 directions
                p = Run(K + A + " crouch idle", k, 2f, (q, t) => { q.Aim = aim; q.Crouch = true; }, null, "1.5");
                Check(p.W[LocoPlan.B_CIDLE] > 0.99f, "crouch idle legs " + p.W[LocoPlan.B_CIDLE]);
                if (k == LocoPlan.K_PISTOL) Check(p.RigClip == (aim ? "PistolFire" : "PistolIdle") && p.RigEff > 0.99f, "PISTOL CROUCHED: pistol hands over crouched legs " + p.RigClip + " " + p.RigEff);
                if (k == LocoPlan.K_RIFLE) Check(aim ? p.RigClip == "RifleCrouchAim" && p.RigEff > 0.99f : p.RigEff < 0.01f, "rifle crouched hands " + p.RigClip + " " + p.RigEff);
                if (aim) Check(p.AimCrouched && p.AimLiftW > 0.99f, "crouched aim lift");
                for (int d = 0; d < 8; d++)
                {
                    float deg = d * 45f;
                    p = Run(K + A + " crouch walk " + deg, k, 2.5f, (q, t) => { q.Aim = aim; q.Crouch = true; Move(q, deg, 1.0f); }, null, d % 2 == 0 ? "2.0" : null);
                    Check(Range(LocoPlan.B_CWALK, 8, p) + p.W[LocoPlan.B_RCLEFT] > 0.99f, "crouch walk legs");
                    if (k == LocoPlan.K_PISTOL) Check(p.RigClip == (aim ? "PistolFire" : "PistolIdle") && p.RigEff > 0.99f, "PISTOL CROUCH WALK: pistol hands " + p.RigClip);
                    if (k == LocoPlan.K_NONE && deg == 270f) Check(p.W[LocoPlan.B_RCLEFT] > 0.99f, "bare hands crouch strafe left = the mirrored strafe right");
                    if (k != LocoPlan.K_NONE) Check(p.W[LocoPlan.B_RCLEFT] < 0.001f, "no mirrored crouch strafe with a weapon");
                }
            }
            // stopping: walk forward relaxed then stop at different points of the stride -> RifleWalkToStop for some, never a jump of weights
            int stops = 0;
            for (int s = 0; s < 12; s++)
            {
                float stopAt = 1.5f + s * 0.09f;
                bool hit = false;
                var p2 = Run(K + " walk-to-stop " + s, k, stopAt + 2.6f, (q, t) => { Move(q, 0f, t < stopAt ? 1.4f : 0f); },
                    (q, t) => { if (q.ActionClip == "RifleWalkToStop" && q.ActionW > 0.5f) hit = true; });
                if (hit) stops++;
                Check(p2.ActionW < 0.01f && p2.W[LocoPlan.B_RIDLE] + p2.W[LocoPlan.B_IDLE] > 0.99f, "settled after the stop");
            }
            if (k != LocoPlan.K_MELEE) Check(stops > 0, "walk-to-stop never played (" + stops + ")");
            else Check(stops == 0, "walk-to-stop with the fighting stance");
        }

        // ---------------- a stride, frame by frame (pose_check measures the hips over it): sideways running at walk/run mixes
        if (Environment.GetEnvironmentVariable("SWEEP") != null)
            foreach (float v in new[] { 3.4f, 3.8f, 4.2f })
                Run("None run 90 v" + v.ToString("0.0", ci), LocoPlan.K_NONE, 2.6f, (q, t) => { Move(q, 90f, v); }, null, string.Join(",", Enumerable.Range(0, 12).Select(i => (2.0f + i * 0.05f).ToString("0.00", ci))));

        // ---------------- firing
        {
            var p = Run("Pistol burst", LocoPlan.K_PISTOL, 2f, (q, t) => { q.Aim = true; q.Fire = t > 0.5f && t < 1.2f; }, (q, t) =>
            {
                if (t > 0.6f && t < 1.15f) Check(q.RigClip == "PistolFire" && q.RigTime > 0f, "pistol firing plays PistolFire " + q.RigTime);
                if (t > 1.3f) Check(q.RigClip == "PistolFire" && q.RigTime == 0f, "pistol back to the aim (frame 0) " + q.RigTime);
            }, "0.9");
            Run("Rifle burst", LocoPlan.K_RIFLE, 2f, (q, t) => { q.Aim = false; q.Fire = t > 0.5f && t < 1.2f; }, (q, t) =>
            {
                if (t > 0.7f && t < 1.15f) Check(q.RigClip == "RifleAim" && q.RigEff > 0.95f && q.W[LocoPlan.B_IDLE] > 0.9f, "rifle firing: aiming hands, aiming stance");
            });
        }

        // ---------------- reloads: whole clip, standing / walking / crouched; one round at a time
        foreach (int k in new[] { LocoPlan.K_PISTOL, LocoPlan.K_RIFLE })
        {
            string rc = k == LocoPlan.K_RIFLE ? "RifleReload" : "PistolReload";
            foreach (var sit in new[] { "stand", "walk", "crouch", "crouchwalk" })
            {
                float last = -1; bool seen = false, back = false;
                var p = Run(KN[k] + " reload " + sit, k, 4.5f, (q, t) =>
                {
                    q.Aim = true; q.Crouch = sit.StartsWith("crouch"); if (sit.EndsWith("walk")) Move(q, 0f, sit == "walk" ? 1.4f : 1.0f);
                    if (Math.Abs(t - 1f) < DT / 2) q.Reload(2.5f, 0);
                }, (q, t) =>
                {
                    if (t > 1.05f && t < 3.45f)
                    {
                        Check(q.RigClip == rc && q.Reloading, "reloading hands " + q.RigClip);
                        Check(q.RigTime >= last - 1e-4f, "reload clip runs forward " + last + " -> " + q.RigTime); last = q.RigTime; seen = true;
                        Check(q.AimLiftW < 1f - 5f * (t - 1.05f) + 0.05f || q.AimLiftW < 0.05f, "aim lift fades out in a reload");
                        Check(q.PoseW.ContainsKey(rc), "weapon pose follows the reload clip");
                    }
                    if (t > 3.6f) { back = true; Check(q.RigClip == (k == LocoPlan.K_RIFLE ? (q.CrouchW > 0.5f ? "RifleCrouchAim" : "RifleAim") : "PistolFire"), "back to aiming after the reload " + q.RigClip); }
                }, sit == "stand" || sit == "crouchwalk" ? "1.5,2.5" : null);
                Check(seen && back && last > 0.95f * len[rc], "the whole reload clip played (" + last + " of " + len[rc] + ")");
                if (sit == "walk") Check(W(p, LocoPlan.B_WALK) + Range(LocoPlan.B_WALK, 8, p) > 0.9f, "legs keep walking through a reload");
            }
            // one round at a time: 5 rounds in 3.5 s
            {
                float a, b; LocoPlan.RoundSegment(rc, out a, out b);
                int wraps = 0; float last = -1;
                Run(KN[k] + " reload 5 rounds", k, 5f, (q, t) => { q.Aim = true; if (Math.Abs(t - 0.5f) < DT / 2) q.Reload(3.5f, 5); }, (q, t) =>
                {
                    if (t > 0.52f && t < 3.98f)
                    {
                        Check(q.RigClip == rc, "round reload hands " + q.RigClip);
                        Check(q.RigTime >= a * len[rc] - 1e-3f && q.RigTime <= b * len[rc] + 1e-3f, "round part only: " + q.RigTime);
                        if (last >= 0 && q.RigTime < last - 0.1f) wraps++;
                        last = q.RigTime;
                    }
                }, k == LocoPlan.K_RIFLE ? "0.8,1.1" : null);
                Check(wraps == 4, "5 rounds = 4 restarts of the round part, got " + wraps);
            }
            // a reload is dropped with the weapon
            var p3 = Run(KN[k] + " reload then weapon change", k, 2f, (q, t) => { q.Aim = true; if (Math.Abs(t - 0.2f) < DT / 2) q.Reload(3f, 0); if (t > 1f) q.Kind = LocoPlan.K_NONE; });
            Check(!p3.Reloading && p3.RigClip == "Idle", "weapon change ends the reload " + p3.RigClip);
        }

        // ---------------- cocking: ShotgunPump after a shot (rifle kind only, not in a reload, restarts on each cock)
        {
            float pumpLen = (LocoPlan.PumpTo - LocoPlan.PumpFrom) * len["ShotgunPump"] / 2f;
            float last = -1; int starts = 0; bool seen = false;
            Run("Rifle pump", LocoPlan.K_RIFLE, 4f, (q, t) =>
            {
                q.Aim = true; q.Fire = t > 0.3f && t < 0.5f;
                if (Math.Abs(t - 0.5f) < DT / 2 || Math.Abs(t - 2.5f) < DT / 2) q.Pump();
                if (Math.Abs(t - 2.8f) < DT / 2) q.Pump();             // cocked again mid-rack: starts over
            }, (q, t) =>
            {
                bool inWin = (t > 0.52f && t < 0.5f + pumpLen - 0.02f) || (t > 2.52f && t < 2.8f + pumpLen - 0.02f);
                if (inWin) { Check(q.RigClip == "ShotgunPump" && q.Pumping, "pumping hands " + q.RigClip); seen = true; Check(q.RigTime >= LocoPlan.PumpFrom * len["ShotgunPump"] - 1e-3f && q.RigTime <= LocoPlan.PumpTo * len["ShotgunPump"] + 1e-3f, "pump inside its rack " + q.RigTime); }
                if (q.RigClip == "ShotgunPump") { if (last >= 0 && q.RigTime < last - 1e-3f) starts++; last = q.RigTime; }
                if ((t > 0.5f + pumpLen + 0.05f && t < 2.4f) || t > 2.8f + pumpLen + 0.05f) Check(q.RigClip == "RifleAim", "back to aiming after the pump " + q.RigClip);
            }, "0.8");
            Check(seen && starts == 2, "pump started over at the second cock and again at the third (" + starts + ")");
            Run("Pistol pump ignored", LocoPlan.K_PISTOL, 2f, (q, t) => { q.Aim = true; if (Math.Abs(t - 0.5f) < DT / 2) q.Pump(); }, (q, t) => Check(q.RigClip != "ShotgunPump" && !q.Pumping, "a pistol never pumps"));
            Run("Rifle pump in reload ignored", LocoPlan.K_RIFLE, 3f, (q, t) => { q.Aim = true; if (Math.Abs(t - 0.2f) < DT / 2) q.Reload(2f, 0); if (Math.Abs(t - 0.6f) < DT / 2) q.Pump(); }, (q, t) => { if (t < 2.1f && t > 0.25f) Check(q.RigClip == "RifleReload", "no pump in a reload " + q.RigClip); });
            Run("Rifle pump crouched", LocoPlan.K_RIFLE, 2f, (q, t) => { q.Aim = true; q.Crouch = true; if (Math.Abs(t - 0.5f) < DT / 2) q.Pump(); }, (q, t) => { if (t > 0.55f && t < 0.5f + pumpLen - 0.05f) Check(q.RigClip == "ShotgunPump" && q.W[LocoPlan.B_CIDLE] > 0.99f, "pump over crouched legs"); }, "0.9");
        }

        // ---------------- turning in place
        foreach (int k in kinds)
            foreach (bool cr in new[] { false, true })
                foreach (float rate in new[] { 45f, 120f, -90f })
                {
                    float lastFrac = -1; int frames = 0;
                    var p = Run(KN[k] + (cr ? " crouched" : "") + " turn " + rate, k, 3f, (q, t) => { q.Crouch = cr; q.Yaw = t < 2f ? rate * t : rate * 2f; }, (q, t) =>
                    {
                        if (t > 0.6f && t < 1.9f) { Check(q.ActionW > 0.99f && q.ActionClip.Contains("Turn"), "turning: a turn clip " + q.ActionClip + " " + q.ActionW); frames++; }
                        if (t > 0.6f && t < 1.9f) Check((rate > 0) == q.ActionClip.Contains("Right"), "turn clip side " + q.ActionClip);
                        if (t > 0.6f && t < 1.9f && cr) Check(q.ActionClip.Contains("Crouch"), "crouched turn clip " + q.ActionClip);
                    }, cr || rate != 45f ? null : "1.0");
                    Check(p.ActionW < 0.01f, "turn settled after it stopped (" + p.ActionW + ")");
                }

        // ---------------- jumps
        foreach (int k in kinds)
        {
            string seq = "";
            var p = Run(KN[k] + " jump", k, 2.5f, (q, t) => { if (Math.Abs(t - 0.5f) < DT / 2) q.Jump(); q.Airborne = t > 0.55f && t < 1.25f; }, (q, t) =>
            {
                if (q.ActionW > 0.3f && !seq.EndsWith(q.ActionClip + ">")) seq += q.ActionClip + ">";
            }, k == LocoPlan.K_RIFLE ? "0.6,1.0,1.3" : null);
            string want = k == LocoPlan.K_RIFLE ? "RifleJumpUp>RifleJumpLoop>RifleJumpDown>" : k == LocoPlan.K_PISTOL ? "PistolJump>" : "Jump>";
            Check(seq == want, "jump sequence " + seq + " (want " + want + ")");
            Check(p.ActionW < 0.01f, "back on the legs after landing");
            var p2 = Run(KN[k] + " jump, Airborne never set", k, 2.5f, (q, t) => { if (Math.Abs(t - 0.5f) < DT / 2) q.Jump(); });
            Check(p2.ActionW < 0.01f, "a jump without Airborne ends by itself");
        }

        // ---------------- throws, strikes, kick
        foreach (int k in kinds)
        {
            bool seen = false;
            var p = Run(KN[k] + " throw", k, 3.5f, (q, t) => { q.Aim = k == LocoPlan.K_RIFLE; if (Math.Abs(t - 0.5f) < DT / 2) q.Throw(false); }, (q, t) =>
            {
                if (t > 0.7f && t < 2.2f) { Check(q.UpperClip == "Throw" && q.UpperW > 0.99f, "throwing " + q.UpperClip + " " + q.UpperW); seen = true; }
            }, k == LocoPlan.K_RIFLE ? "1.2" : null);
            Check(seen && p.UpperW < 0.01f, "throw over");
            var pk = Run(KN[k] + " kick", k, 2.5f, (q, t) => { if (Math.Abs(t - 0.5f) < DT / 2) q.Kick(); }, (q, t) => { if (t > 0.7f && t < 1.6f) Check(q.ActionClip == "Kick" && q.ActionW > 0.99f, "kicking " + q.ActionClip); });
            Check(pk.ActionW < 0.01f, "kick over");
        }
        {
            var blows = new List<float>();
            var p = Run("None punches x3", LocoPlan.K_NONE, 3f, (q, t) => { if (Math.Abs(t - 0.5f) < DT / 2 || Math.Abs(t - 0.9f) < DT / 2 || Math.Abs(t - 1.3f) < DT / 2) q.Strike(0.35f); },
                (q, t) => { if (q.UpperClip == "Punch" && q.UpperW > 0.99f && (blows.Count == 0 || Math.Abs(blows[blows.Count - 1] - q.UpperTime) > 0.2f) && Math.Abs((t - 0.5f) % 0.4f - 0.08f) < DT / 2) blows.Add(q.UpperTime); }, "0.58");
            Check(blows.Count == 3 && blows[0] < blows[1] && blows[1] < blows[2], "chained punches land blow after blow (" + string.Join(",", blows) + ")");
            Check(p.UpperW < 0.01f, "punches over");
            string seq = "";
            p = Run("Melee swings x3", LocoPlan.K_MELEE, 3.5f, (q, t) => { if (Math.Abs(t - 0.5f) < DT / 2 || Math.Abs(t - 1.0f) < DT / 2 || Math.Abs(t - 1.5f) < DT / 2) q.Strike(0.45f); },
                (q, t) => { if (q.UpperW > 0.5f && !seq.EndsWith(q.UpperClip + ">")) seq += q.UpperClip + ">"; }, "0.7");
            Check(seq == "Melee>MeleeCombo>", "melee: the swing, then the combo's blows (" + seq + ")");
            Check(p.UpperW < 0.01f, "swings over");
        }

        // ---------------- NPC timelines, driven the way NPCAI's Brain.Drive drives ModAPI (aim when squared up, Firing in a burst, Reload when the
        // magazine is empty - rounds one at a time for a shotgun -, Pump after a burst of a pump gun and after its reload)
        {
            string seq = ""; bool shown = false;
            // a pump shotgunner: chases (runs), stops and aims, two bursts (pump after each), empty -> reload 5 rounds -> pump, chases again
            Run("NPC shotgunner", LocoPlan.K_RIFLE, 14f, (q, t) =>
            {
                bool chase = t < 3f || t > 11.5f;
                Move(q, 0f, chase ? 4.0f : 0f);
                q.Aim = !chase; q.Fire = (t > 4f && t < 4.3f) || (t > 6f && t < 6.3f);
                if (Math.Abs(t - 4.3f) < DT / 2) q.Pump();                                          // burst over: racked
                if (Math.Abs(t - 6.3f) < DT / 2) { q.Reload(4.3f, 5); shown = true; }               // the 5th shell went: reload, no pump first
                if (shown && !q.ReloadingAt(T)) { shown = false; q.Pump(); }                         // reload over (Drive's clock): racked
            }, (q, t) => { string h = q.RigEff > 0.5f ? q.RigClip : "-"; if (!seq.EndsWith(">" + h)) seq += ">" + h; }, "1.0,4.5,7.0,10.8");
            Console.WriteLine("shotgunner hands: " + seq);
            Check(seq == ">RifleRunLow>RifleAim>ShotgunPump>RifleAim>RifleReload>ShotgunPump>RifleWalkLow>RifleRunLow", "shotgunner hands sequence " + seq);
            // a pistolman kneels in cover, aims, fires, reloads kneeling, aims again
            seq = "";
            Run("NPC pistolman in cover", LocoPlan.K_PISTOL, 9f, (q, t) =>
            {
                Move(q, 0f, t < 2f ? 3.0f : 0f); q.Crouch = t > 2.2f; q.Aim = t > 2.2f; q.Fire = t > 3.5f && t < 4.5f;
                if (Math.Abs(t - 4.6f) < DT / 2) q.Reload(3.0f, 0);
            }, (q, t) =>
            {
                string h = q.RigEff > 0.5f ? q.RigClip : "-"; if (!seq.EndsWith(">" + h)) seq += ">" + h;
                if (t > 3f) Check(q.W[LocoPlan.B_CIDLE] > 0.99f, "the pistolman stays kneeling");
            }, "3.0,4.0,5.5");
            Console.WriteLine("pistolman hands: " + seq);
            Check(seq == ">PistolIdle>PistolFire>PistolReload>PistolFire", "pistolman hands sequence " + seq);
        }

        // ---------------- first frame: no fade in from nothing
        foreach (int k in kinds)
        {
            scen = KN[k] + " first frame"; T = 50f;
            var p = New(k); p.Aim = true; p.Crouch = true; p.Step(T, DT);
            Check(p.W[LocoPlan.B_CIDLE] > 0.99f && (k == LocoPlan.K_RIFLE ? p.RigEff > 0.99f : true), "first frame is already the pose");
        }

        json.Append("\n]\n");
        if (args.Length > 1) File.WriteAllText(args[1], json.ToString());
        Console.WriteLine((fails == 0 ? "ALL PASS" : fails + " FAILED") + " - " + checks + " checks");
        Environment.Exit(fails == 0 ? 0 : 1);
    }
}
