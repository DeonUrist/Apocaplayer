# Offline check of Apocaplayer's locomotion: takes the decisions LocoTest.exe wrote (plan.json: per weapon kind and position the base clips,
# weights, the upper-rig clip, the hips' turn) and the real FBX clips (pose.py), builds her pose over one stride, measures the legs and draws
# every case. Usage: python3 check.py plan.json out_dir
import json, sys, os, numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pose import Clip, Skeleton, blend, m_from_q, q_from_m, roty
import matplotlib; matplotlib.use("Agg"); import matplotlib.pyplot as plt

plan = json.load(open(sys.argv[1])); out = sys.argv[2]; os.makedirs(out, exist_ok=True)
sk = Skeleton(); B = {b: i for i, b in enumerate(sk.bones)}
_clips = {}
def clip(n):
    if n not in _clips:
        if n in ("WalkStrafeRight", "RightTurn", "ThrowRight", "CrouchStrafeLeft"):
            _clips[n] = Clip(n, mirror_of={"ThrowRight": "Throw", "CrouchStrafeLeft": "RifleCrouchStrafeRight"}.get(n, n.replace("Right", "Left")))
        else: _clips[n] = Clip(n)
    return _clips[n]
def fk(q, h): return sk.fk(q, h)
_off = {}
def phase(n, N=32):
    """the clip's phase offset as LocoPlan.PhaseFromHeights: first harmonic of left-minus-right foot height (measured on the target skeleton)"""
    if n in _off: return _off[n]
    c = clip(n); d = []
    for k in range(N):
        _, P = fk(*c.sample(k / N)); d.append(P[B["LeftFoot"]][1] - P[B["RightFoot"]][1])
    d = np.array(d) - np.mean(d); a = 2 * np.pi * np.arange(N) / N
    s, co = np.sum(d * np.sin(a)), np.sum(d * np.cos(a))
    ph = -1.0 if np.hypot(s, co) * 2 / N < 0.01 else (np.arctan2(s, co) / (2 * np.pi)) % 1.0
    _off[n] = ph; return ph
def time01(u, off, rev):
    o = 0.0 if off < 0 else off
    return ((o - u) if rev else (o + u)) % 1.0

IDLE_T = 0.3
def pose(case, u, use_phase=True):
    smp = []
    for b in case["base"]:
        c = clip(b["clip"])
        t = (time01(u, phase(b["clip"]) if use_phase else -1, b["rev"]) if b["moving"] else IDLE_T)
        q, h = c.sample(t); smp.append((b["w"], q, h))
    Q, H = blend(smp)
    Q = Q.copy()
    if abs(case["hipTurn"]) > 0.2:      # Turn(Hips, up, +deg) = toward her right; canonical x = her left
        Q[0] = q_from_m(roty(-case["hipTurn"]) @ m_from_q(Q[0]))
    if case["upper"]:
        rc = clip(case["upper"])
        t = 0.0 if case["hold"] else time01(u, phase(case["upper"]) if use_phase else -1, False) if case["sync"] else IDLE_T
        rq, rh = rc.sample(t)
        Dr, _ = fk(rq, rh)
        D, _ = fk(Q, H)
        sp = B["Spine"]
        Q[sp] = q_from_m(D[0].T @ Dr[sp])          # her spine = the copy's spine orientation relative to the character
        for i, p in enumerate(sk.par):
            anc = i
            while anc >= 0 and anc != sp: anc = sk.par[anc]
            if anc == sp and i != sp: Q[i] = rq[i]
    if case.get("follow", 0) > 0.01: Q = twist(Q, H, case["follow"])
    return fk(Q, H)

def face(P, a, b):
    """facing of the line a->b (pointing to her right) about up, + = right (as Loco.YawIn)"""
    v = P[B[b]] - P[B[a]]; v[1] = 0
    f = np.cross([0, 1, 0], v)                      # canonical x = her left: up x right = forward
    return -np.degrees(np.arctan2(f[0], f[2]))
def turn(Q, H, bone, deg):
    """Body.Turn(bone, up, deg): the bone (and everything under it) turned about her up axis, + = to her right"""
    D, _ = fk(Q, H); i = B[bone]; p = sk.par[i]; R = roty(-deg)
    Q = Q.copy(); Q[i] = q_from_m(D[p].T @ R @ D[p] @ m_from_q(Q[i])); return Q
def twist(Q, H, follow):
    """Loco.TwistSpine with LocoPlan.Twist (ported; check_actions.py compares it with the C#)"""
    _, P = fk(Q, H)
    pel, ch = face(P, "LeftUpLeg", "RightUpLeg"), face(P, "LeftArm", "RightArm")
    s0, s1, s2, nk = twist_angles(pel, ch, follow)
    for b, d in (("Spine", s0), ("Spine1", s1), ("Spine2", s2), ("Neck", nk)): Q = turn(Q, H, b, d)
    return Q
def twist_angles(pel, ch, follow, head_back=0.6):
    target = follow * pel; delta = target - ch; t = target - pel
    return delta - 2 * t / 3, t / 3, t / 3, -head_back * delta

def measure(case, N=24, use_phase=True):
    """over one stride: stance-foot slide vs the way she moves, foot yaw vs her forward, anti-phase of the feet, feet crossing, hips height"""
    Ps = [pose(case, k / N, use_phase)[1] for k in range(N)]
    P = np.array(Ps)
    r = {"hips": P[:, 0, 1].mean()}
    mv = case["dir"]
    md = None if mv == -1 or not any(b["moving"] for b in case["base"]) else np.array([-np.sin(np.radians(mv)), 0, np.cos(np.radians(mv))])  # canonical x = left
    peaks = {}
    for s in ("Left", "Right"):
        f = P[:, B[s + "Foot"]]; t = P[:, B[s + "ToeBase"]]
        h = f[:, 1]; lo = h.min(); stance = h < lo + 0.025
        v = t - f; yaw = -np.degrees(np.arctan2(v[:, 0], v[:, 2]))       # + = toe to her right
        r[s + "yaw"] = float(np.mean(yaw[stance]))
        r[s + "lift"] = float(h.max() - lo)
        d = h - h.mean(); a = 2 * np.pi * np.arange(N) / N
        peaks[s] = (np.arctan2(np.sum(d * np.sin(a)), np.sum(d * np.cos(a))) / (2 * np.pi)) % 1
        if md is not None:
            rel = f - P[:, 0]; vel = np.roll(rel, -1, 0) - np.roll(rel, 1, 0); vel[:, 1] = 0
            st = stance & (np.linalg.norm(vel, axis=1) > 1e-3)
            if st.sum() > 0:
                vm = vel[st].mean(0); r[s + "slide"] = float(np.degrees(np.arccos(np.clip(-vm @ md / (np.linalg.norm(vm) + 1e-9), -1, 1))))
            else: r[s + "slide"] = np.nan
    r["antiphase"] = float(abs(((peaks["Left"] - peaks["Right"]) % 1) - 0.5))      # 0 = feet exactly alternating
    lat = P[:, B["LeftFoot"], 0] - P[:, B["RightFoot"], 0]                          # left foot's lateral position minus the right one's
    r["cross"] = float(lat.min())
    return r, P

def verdict(case, r):
    bad = []
    moving = any(b["moving"] and b["w"] > 0.05 for b in case["base"])
    if moving:
        if r["antiphase"] > 0.15: bad.append("feet not alternating (%.2f)" % r["antiphase"])
        for s in ("Left", "Right"):
            if not np.isnan(r.get(s + "slide", np.nan)) and r[s + "slide"] > 50: bad.append("%s foot pushes %.0f° off the way she moves" % (s[0], r[s + "slide"]))
    if case["relaxed"] and case["crouch"] < 0.5:
        for s in ("Left", "Right"):
            yaw = r[s + "yaw"] - (case["hipTurn"] if case["hipTurn"] else 0)
            if abs(yaw) > 45: bad.append("%s toe turned %.0f° (relaxed)" % (s[0], yaw))
    if case["crouch"] > 0.5 and r["hips"] > 0.8: bad.append("not crouched (hips %.2f)" % r["hips"])
    return bad

LINES = [("Hips", "Spine"), ("Spine", "Spine1"), ("Spine1", "Spine2"), ("Spine2", "Neck"), ("Neck", "Head"), ("Head", "HeadTop_End")] + \
    [(s + a, s + b) for s in ("Left", "Right") for a, b in (("UpLeg", "Leg"), ("Leg", "Foot"), ("Foot", "ToeBase"), ("ToeBase", "Toe_End"), ("Shoulder", "Arm"), ("Arm", "ForeArm"), ("ForeArm", "Hand"))] + \
    [("Hips", "LeftUpLeg"), ("Hips", "RightUpLeg"), ("Spine2", "LeftShoulder"), ("Spine2", "RightShoulder")]
def draw(ax, P, view, title=None, mv=-1):
    # view: 'front34' (from her front-right, 3/4) or 'top'
    if view == "top":
        X, Y = -P[:, 0], P[:, 2]
    else:
        yaw = np.radians(-35); X = -(P[:, 0] * np.cos(yaw) + P[:, 2] * np.sin(yaw)); Y = P[:, 1] + 0.12 * (P[:, 2] * np.cos(yaw) - P[:, 0] * np.sin(yaw))
    for a, b in LINES:
        if a not in B or b not in B: continue
        col = "#1f6fd1" if a.startswith("Left") or b.startswith("Left") and a != "Hips" and a != "Spine2" else "#d1361f" if a.startswith("Right") or b.startswith("Right") and a != "Hips" and a != "Spine2" else "#333"
        ax.plot([X[B[a]], X[B[b]]], [Y[B[a]], Y[B[b]]], color=col, lw=2.2 if "Leg" in a or "Foot" in a else 1.6, solid_capstyle="round")
    if view == "top":
        ax.annotate("", xy=(0, 0.45), xytext=(0, 0.25), arrowprops=dict(arrowstyle="->", color="#999"))   # her facing
        if mv != -1:
            a = np.radians(mv); ax.annotate("", xy=(0.45 * np.sin(a), 0.45 * np.cos(a)), xytext=(0, 0), arrowprops=dict(arrowstyle="-|>", color="#2a2", lw=2))
        ax.set_xlim(-0.6, 0.6); ax.set_ylim(-0.6, 0.6)
    else:
        ax.set_xlim(-0.7, 0.7); ax.set_ylim(-0.05, 1.85)
    ax.set_aspect("equal"); ax.axis("off")
    if title: ax.set_title(title, fontsize=7)

def label(case):
    base = ", ".join("%s%s %.0f%%" % (b["clip"], "(rev)" if b["rev"] else "", b["w"] * 100) for b in case["base"] if b["w"] > 0.01)
    return "%s | legs: %s | hands: %s%s" % (case["case"], base, case["upper"] or "(legs' clip)", "  hips %+.0f°" % case["hipTurn"] if abs(case["hipTurn"]) > 0.2 else "")
def sheet(kind, cases, results, fname):
    n = len(cases); cols = 5
    fig, axs = plt.subplots(n, cols, figsize=(cols * 1.5, n * 1.75))
    for r_, case in enumerate(cases):
        res, P = results[case["case"]]
        for k, u in enumerate((0, 6, 12, 18)):
            draw(axs[r_, k], P[u], "front34")
        draw(axs[r_, 4], P[0], "top", mv=case["dir"])
        bad = verdict(case, res)
        base = ", ".join("%s%s %.0f%%" % (b["clip"], "(rev)" if b["rev"] else "", b["w"] * 100) for b in case["base"] if b["w"] > 0.01)
        txt = "%s  |  legs: %s  |  hands: %s%s" % (case["case"], base, case["upper"] or "(legs' clip)", "  hips %+.0f°" % case["hipTurn"] if abs(case["hipTurn"]) > 0.2 else "")
        axs[r_, 0].text(0, 1.02, txt, transform=axs[r_, 0].transAxes, fontsize=6.5, ha="left", va="bottom", color="#b00" if bad else "#060")
        if bad: axs[r_, 0].text(0, -0.06, "FAIL: " + "; ".join(bad), transform=axs[r_, 0].transAxes, fontsize=6, ha="left", va="top", color="#b00")
    fig.suptitle("Apocaplayer locomotion check - %s (front 3/4 at stride 0, .25, .5, .75; top view, green = way she moves)" % kind, fontsize=9)
    fig.tight_layout(rect=(0, 0, 1, 0.995)); fig.savefig(fname, dpi=110); plt.close(fig)

if __name__ == "__main__":
    use_phase = os.environ.get("NOPHASE") is None
    report = []; poses = []
    kinds = []
    for c in plan:
        if c["kind"] not in kinds: kinds.append(c["kind"])
    for kind in kinds:
        cases = [c for c in plan if c["kind"] == kind]
        results = {}
        for c in cases:
            r, P = measure(c, use_phase=use_phase); results[c["case"]] = (r, P)
            bad = verdict(c, r)
            # an angle between two keyed directions (…deg) only passes by while she turns: there a mix is expected, so it is a note, not a failure
            report.append("%s %-7s %-16s %s" % (("TRANS" if "deg" in c["case"] else "FAIL") if bad else "PASS", kind, c["case"], "; ".join(bad) if bad else
                          "yaw L%+.0f R%+.0f slide L%s R%s antiphase %.2f hips %.2f" % (r["Leftyaw"], r["Rightyaw"], "%.0f" % r["Leftslide"] if "Leftslide" in r else "-", "%.0f" % r["Rightslide"] if "Rightslide" in r else "-", r["antiphase"], r["hips"])))
        if os.environ.get("STICKS"): sheet(kind, cases, results, os.path.join(out, "loco_%s.png" % kind))
        # poses for the mesh renders (render.py): stride 0, .25, .5, .75
        for c in cases:
            Ds, Ps = zip(*[pose(c, u, use_phase) for u in (0, .25, .5, .75)])
            poses.append({"kind": kind, "case": c["case"], "D": np.array(Ds), "P": np.array(Ps), "dir": c["dir"], "label": label(c), "bad": verdict(c, results[c["case"]][0])})
    open(os.path.join(out, "report.txt"), "w").write("\n".join(report) + "\n")
    import pickle; pickle.dump({"bones": sk.bones, "poses": poses}, open(os.path.join(out, "poses.pkl"), "wb"))
    print("\n".join(report))
    print("phase offsets:", {k: round(v, 2) for k, v in sorted(_off.items())})
