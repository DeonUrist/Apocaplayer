# Poses of ModApiTest's frames (frames.json: the base slots with weights and times, the hands' clip on the UpperRig, the whole-body action, the
# upper-body layer, the hips' turn, the chest follow) built from the real FBX clips on the pack's X Bot, the way the game layers them
# (ModAPI.Character / Body: mixer -> action layer -> masked upper layer -> UpperRig over the spine -> twist), then measured.
# Usage: CLIPS=<npz dir> python3 pose_check.py frames.json out_dir   (writes out_dir/poses.pkl for render_frames.py, prints the checks)
import json, sys, os, pickle, numpy as np
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "locotest"))
from pose import Clip, Skeleton, blend, m_from_q, q_from_m, roty

frames = json.load(open(sys.argv[1])); out = sys.argv[2]; os.makedirs(out, exist_ok=True)
sk = Skeleton(); B = {b: i for i, b in enumerate(sk.bones)}
MIRROR = {"WalkStrafeRight": "WalkStrafeLeft", "RightTurn": "LeftTurn", "ThrowRight": "Throw", "CrouchStrafeLeft": "RifleCrouchStrafeRight"}
CL = os.environ["CLIPS"]
_c = {}
def clip(n):
    if n not in _c:
        src = MIRROR.get(n)
        _c[n] = (Clip(n, mirror_of=src) if src else Clip(n)) if os.path.exists(f"{CL}/{src or n}.npz") else None
    return _c[n]
def sample(n, t):
    c = clip(n)
    if c is None: return None
    u = 0.0 if c.length <= 0 else min(t / c.length, 0.9999)
    return c.sample(u)
def under(i, top):
    a = i
    while a >= 0 and a != top: a = sk.par[a]
    return a == top
SP = B["Spine"]
UPPER = [i for i in range(len(sk.bones)) if under(i, SP)]          # the masked layer's bones (spine and everything above it)
def slerp_q(a, b, w):
    if np.dot(a, b) < 0: b = -b
    q = a * (1 - w) + b * w; return q / np.linalg.norm(q)
def turn(Q, H, bone, deg):
    D, _ = sk.fk(Q, H); i = B[bone]; p = sk.par[i]; R = roty(-deg)
    Q = Q.copy(); Q[i] = q_from_m(D[p].T @ R @ D[p] @ m_from_q(Q[i])); return Q
def face(P, a, b):
    v = P[B[b]] - P[B[a]]; v[1] = 0; f = np.cross([0, 1, 0], v); return -np.degrees(np.arctan2(f[0], f[2]))

missing = set()
def build(f):
    smp = []
    for b in f["base"]:
        s = sample(b["clip"], b["t"])
        if s is None: missing.add(b["clip"]); continue
        smp.append((b["w"], s[0], s[1]))
    Q, H = blend(smp); Q = Q.copy()
    if abs(f["hipTurn"]) > 0.2 and f["actW"] < 0.5: Q[0] = q_from_m(roty(-f["hipTurn"]) @ m_from_q(Q[0]))   # Turn(Hips, up): the root bone
    if f["actW"] > 0.001 and f["act"]:
        s = sample(f["act"], f["actT"])
        if s is None: missing.add(f["act"])
        else:
            for i in range(len(Q)): Q[i] = slerp_q(Q[i], s[0][i], f["actW"])
            H = H * (1 - f["actW"]) + s[1] * f["actW"]
    if f["upW"] > 0.001 and f["up"]:
        s = sample(f["up"], f["upT"])
        if s is None: missing.add(f["up"])
        else:
            for i in UPPER: Q[i] = slerp_q(Q[i], s[0][i], f["upW"])
    eff = f["rigEff"]
    if eff > 0.001 and f["rig"]:
        s = sample(f["rig"], f["rigT"])
        if f["rigOld"] and f["rigX"] < 1:
            so = sample(f["rigOld"], f["rigOldT"])
            if s is not None and so is not None: s = blend([(f["rigX"], s[0], s[1]), (1 - f["rigX"], so[0], so[1])])
        if s is None: missing.add(f["rig"])
        else:
            rq, rh = s
            Dr, _ = sk.fk(rq, rh); D, _ = sk.fk(Q, H)
            Q[SP] = slerp_q(Q[SP], q_from_m(D[0].T @ Dr[SP]), eff)
            for i in UPPER:
                if i != SP: Q[i] = slerp_q(Q[i], rq[i], eff)
    if f["follow"] > 0.01:
        _, P = sk.fk(Q, H)
        pel, ch = face(P, "LeftUpLeg", "RightUpLeg"), face(P, "LeftArm", "RightArm")
        target = f["follow"] * pel; delta = target - ch; t = target - pel
        for b, d in (("Spine", delta - 2 * t / 3), ("Spine1", t / 3), ("Spine2", t / 3), ("Neck", -0.6 * delta)): Q = turn(Q, H, b, d)
    return sk.fk(Q, H)

poses = []; fails = 0
stand = None
for f in frames:
    D, P = build(f)
    fid = f["id"]
    hips, head, rh, lh, ch = P[B["Hips"]], P[B["Head"]], P[B["RightHand"]], P[B["LeftHand"]], P[B["Spine2"]]
    floor = min(P[B["LeftFoot"]][1], P[B["RightFoot"]][1])
    m = {"hips": hips[1] - floor, "rh_up": rh[1] - ch[1], "rh_fwd": rh[2] - ch[2], "hands": float(np.linalg.norm(rh - lh)), "head": head[1] - floor}
    bad = []
    if not np.all(np.isfinite(P)): bad.append("NaN")
    crouched = f["crouch"] > 0.9
    if crouched and m["hips"] > 0.80: bad.append("crouched but hips %.2f m up" % m["hips"])
    if not crouched and f["actW"] < 0.1 and m["hips"] < 0.70: bad.append("standing but hips %.2f m up" % m["hips"])
    aimclip = f["rig"] in ("RifleAim", "RifleCrouchAim", "PistolFire") and f["rigEff"] > 0.9 and f["upW"] < 0.1
    if aimclip and m["rh_up"] < -0.05: bad.append("aiming but the gun hand %.2f m below the chest" % m["rh_up"])
    if aimclip and m["rh_fwd"] < (0.3 if f["rig"] == "PistolFire" else 0.05): bad.append("aiming but the gun hand only %.2f m in front" % m["rh_fwd"])
    if aimclip and f["rig"].startswith("Rifle") and m["hands"] > 0.75: bad.append("rifle aim with the hands %.2f m apart" % m["hands"])
    print(("FAIL " if bad else "ok   ") + fid + "  " + " ".join("%s=%.2f" % kv for kv in m.items()) + ("  <- " + "; ".join(bad) if bad else ""))
    fails += bool(bad)
    poses.append({"id": fid, "D": D, "P": P, "bad": bad, "rig": f["rig"], "act": f["act"] if f["actW"] > 0.1 else "", "up": f["up"] if f["upW"] > 0.1 else ""})
pickle.dump({"bones": sk.bones, "poses": poses}, open(os.path.join(out, "poses.pkl"), "wb"))
print("clips without an FBX here (not drawn):", ", ".join(sorted(missing)) or "none")
print("POSES: %d frames, %d flagged" % (len(poses), fails))
