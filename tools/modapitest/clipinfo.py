# Per bundle clip: length (s), looping (as imported: locomotion / idles loop), stride phase (LocoPlan.PhaseFromHeights on the X Bot), measured
# from the FBX clips (../locotest/extract.py -> clips/*.npz; mirrored clips as Unity mirrors them). Clips without an FBX here get a length from
# the table below (marked "est"). Usage: CLIPS=<npz dir> python3 clipinfo.py bundle.txt > clipinfo.txt
import sys, os
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "locotest"))
import numpy as np
from pose import Clip, Skeleton
CL = os.environ["CLIPS"]
sk = Skeleton(); B = {b: i for i, b in enumerate(sk.bones)}
MIRROR = {"WalkStrafeRight": "WalkStrafeLeft", "RightTurn": "LeftTurn", "ThrowRight": "Throw", "CrouchStrafeLeft": "RifleCrouchStrafeRight"}
EST = {"Throw": 2.0, "ThrowRight": 2.0, "Punch1": 1.6, "Punch2": 1.1, "RifleDeathBack": 2.0, "RifleDeathFront": 2.0, "RifleDeathHeadBack": 2.0,
       "RifleDeathHeadFront": 2.0, "RifleDeathRight": 2.0, "RifleCrouchDeathHeadFront": 2.0}
LOOP = lambda n: n in ("Idle", "CrouchIdle", "PistolIdle", "RifleIdle", "RifleCrouchIdle", "RifleAim", "RifleCrouchAim", "RifleJumpLoop") or \
    any(k in n for k in ("Walk", "Run", "Sprint", "Strafe")) and "ToStop" not in n
def phase(c, N=32):
    d = []
    for k in range(N):
        q, h = c.sample(k / N); _, P = sk.fk(q, h); d.append(P[B["LeftFoot"]][1] - P[B["RightFoot"]][1])
    d = np.array(d) - np.mean(d); a = 2 * np.pi * np.arange(N) / N
    s, co = np.sum(d * np.sin(a)), np.sum(d * np.cos(a))
    return -1.0 if np.hypot(s, co) * 2 / N < 0.01 else (np.arctan2(s, co) / (2 * np.pi)) % 1.0
for n in open(sys.argv[1]).read().split():
    src = MIRROR.get(n)
    path = f"{CL}/{src or n}.npz"
    if os.path.exists(path):
        c = Clip(n, mirror_of=src) if src else Clip(n)
        ph = phase(c) if LOOP(n) else -1.0
        print(n, f"{c.length:.4f}", int(LOOP(n)), f"{ph:.4f}", "fbx")
    else:
        print(n, f"{EST.get(n, 1.0):.4f}", int(LOOP(n)), "-1", "est")
