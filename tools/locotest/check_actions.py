# Offline check of the one-shot actions (2.1.2): RifleWalkToStop's start against the walking legs, and the jump clips' take-off / apex /
# touch-down marks (LocoPlan.JumpMarks via locotest.exe). Usage: python3 check_actions.py out_dir   (after LocoTest.exe is built)
import os, sys, subprocess, numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pose import Clip, Skeleton
D = os.path.dirname(os.path.abspath(__file__)); out = sys.argv[1]; os.makedirs(out, exist_ok=True)
sk = Skeleton(); B = {b: i for i, b in enumerate(sk.bones)}
def P(c, u): q, h = c.sample(u); return sk.fk(q, h)[1]
def phase(c, N=32):
    d = np.array([P(c, k / N)[B["LeftFoot"]][1] - P(c, k / N)[B["RightFoot"]][1] for k in range(N)]); d -= d.mean()
    a = 2 * np.pi * np.arange(N) / N
    return (np.arctan2(np.sum(d * np.sin(a)), np.sum(d * np.cos(a))) / (2 * np.pi)) % 1
lines = []
# ---- walk to stop: the feet (relative to the hips) of the walk at stride s vs the stop clip at LocoPlan.StopStart(s)
walk, stop = Clip("RifleWalkLow"), Clip("RifleWalkToStop")
off = phase(walk)
starts = [l.split() for l in subprocess.run(["mono", os.path.join(D, "locotest.exe"), "stop"], capture_output=True, text=True).stdout.split("\n") if l.strip()]
feet = lambda p: np.r_[p[B["LeftFoot"]] - p[0], p[B["RightFoot"]] - p[0]]
used = 0; worst = 0
for s, t in starts:
    s, t = float(s), float(t)
    if t < 0: continue
    used += 1
    e = np.linalg.norm(feet(P(walk, (off + s) % 1)) - feet(P(stop, t))) * 100; worst = max(worst, e)
    lines.append("%s stop  stride %.2f -> clip %.3f  feet jump %.1f cm" % ("PASS" if e < 12 else "FAIL", s, t, e))
old = max(np.linalg.norm(feet(P(walk, (off + k / 48) % 1)) - feet(P(stop, 0.30))) * 100 for k in range(48))
lines.append("INFO stop  clip used on %d of 48 strides; worst feet jump %.1f cm (2.1.1's fixed start at 0.30: up to %.0f cm)" % (used, worst, old))
# ---- jumps: the marks in LocoPlan against the FBX's real ground contact (lowest toe 3 cm above the clip's ground = in the air)
marks = {l.split()[0]: [float(x) for x in l.split()[1:]] for l in subprocess.run(["mono", os.path.join(D, "locotest.exe"), "marks"], capture_output=True, text=True).stdout.split("\n") if l.strip()}
for n, (l, a, t) in marks.items():
    c = Clip(n); T = c.T
    toe = np.array([min(q[B["LeftToeBase"]][1], q[B["RightToeBase"]][1]) for q in (P(c, k / (T - 1) * 0.9999) for k in range(T))])
    # Clip() keeps the hips' height of the FBX (only the XZ of moving clips is taken out), so toe heights are real heights above the ground
    g = toe.min(); air = toe > g + 0.03
    ml = next((k for k in range(T) if air[k]), -1); ma = int(np.argmax(toe)); mt = next((k for k in range(ma, T) if not air[k]), -1)
    meas = [ml / (T - 1) if ml > 0 else -1, ma / (T - 1), mt / (T - 1) if mt >= 0 else -1]
    ok = all(abs(x - y) < 0.03 for x, y in zip(meas, (l, a, t)))
    lines.append("%s jump  %-14s plan lift %.2f apex %.2f touch %.2f | measured %.2f %.2f %.2f (%.2f s clip)" % ("PASS" if ok else "FAIL", n, l, a, t, meas[0], meas[1], meas[2], c.length))
open(os.path.join(out, "report_actions.txt"), "w").write("\n".join(lines) + "\n"); print("\n".join(lines))

# ---- (2.1.5) direction changes: 60 frames (60 fps) after the keys change, legs blended by LocoTest "sweep" (LocoPlan.SlewAngle + Weights);
# the feet's largest move between two frames compared with steady walking. Instant (2.1.4) vs slewed (2.1.5).
_c = {}
def clipc(n):
    if n not in _c: _c[n] = Clip(n, mirror_of={"WalkStrafeRight": "WalkStrafeLeft", "CrouchStrafeLeft": "RifleCrouchStrafeRight"}.get(n))
    return _c[n]
_ph = {}
def phc(n):
    if n not in _ph: _ph[n] = phase(clipc(n))
    return _ph[n]
from pose import blend
def sweep(kind, a0, a1, slew, run=0):
    run_ = lambda x0, x1, sl: subprocess.run(["mono", os.path.join(D, "locotest.exe"), "sweep", os.path.join(D, "clips.txt"), str(kind), str(x0), str(x1), str(sl), str(run)], capture_output=True, text=True).stdout.strip().split("\n")
    rows = run_(a0, a0, 0)[:1] + run_(a0, a1, slew)          # one frame still walking the old way, then the keys change
    cyc = 0.73 if run else 1.3; prev = None; worst = 0; steady = 0; stride = 0.0
    for k, row in enumerate(rows):
        ang, hip, rest = row.split(" ", 2)
        parts = rest.strip("| ").split()
        smp = []
        for j in range(0, len(parts), 3):
            n, w, d = parts[j], float(parts[j + 1]), parts[j + 2]
            t = ((phc(n) - stride) if d == "rev" else (phc(n) + stride)) % 1
            q, h = clipc(n).sample(t); smp.append((w, q, h))
        Q, H = blend(smp); p = sk.fk(Q, H)[1]; f = feet(p)
        if prev is not None:
            mv = np.max(np.abs(f - prev)) * 100
            if k > 40: steady = max(steady, mv)
            worst = max(worst, mv)
        prev = f; stride += (1 / 60) / cyc
    return worst, steady
for kind, kn in ((0, "None"), (1, "Melee")):
    for a0, a1, run in ((-90, 0, 0), (90, 0, 0), (-90, 90, 0), (0, 180, 0), (-90, 0, 1)):
        wi, st = sweep(kind, a0, a1, 0, run); ws, _ = sweep(kind, a0, a1, 360, run)
        ok = ws <= max(st * 1.6, st + 1.5)
        lines.append("%s dir   %-5s %s %4d -> %4d deg: feet move per frame - instant %.1f cm, turned at 360°/s %.1f cm, steady %.1f cm" % ("PASS" if ok else "FAIL", kn, "run " if run else "walk", a0, a1, wi, ws, st))
open(os.path.join(out, "report_actions.txt"), "w").write("\n".join(lines) + "\n"); print("\n".join(lines[-10:]))
