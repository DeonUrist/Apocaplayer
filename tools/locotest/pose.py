# Offline pose engine for Apocaplayer's locomotion: reads the FBX clips (extract.py -> clips/*.npz), retargets every clip onto one
# reference skeleton (the pack's X Bot) as rest-relative rotations (what a humanoid retarget does), blends like an AnimationMixerPlayable
# (weighted rotations per bone, hips height), mirrors like Unity's clip mirror, and lays an UpperRig clip over the spine like RigApply.
# Canonical frame: x = her LEFT, y = up, z = her forward (a proper rotation of Blender's frame).
import numpy as np, os
HERE = os.path.dirname(os.path.abspath(__file__)); CLIPS = os.environ.get("CLIPS", os.path.join(HERE, "clips"))
# Blender frame of the imported FBX: Z up, she faces -Y, her left is +X  ->  canonical (x left, y up, z forward)
C = np.array([[1, 0, 0], [0, 0, 1], [0, -1, 0]], float)

def q_from_m(m):
    t = np.trace(m)
    if t > 0:
        s = np.sqrt(t + 1) * 2; return np.array([(m[2,1]-m[1,2])/s, (m[0,2]-m[2,0])/s, (m[1,0]-m[0,1])/s, s/4])
    i = np.argmax([m[0,0], m[1,1], m[2,2]])
    if i == 0:
        s = np.sqrt(1 + m[0,0] - m[1,1] - m[2,2]) * 2; return np.array([s/4, (m[0,1]+m[1,0])/s, (m[0,2]+m[2,0])/s, (m[2,1]-m[1,2])/s])
    if i == 1:
        s = np.sqrt(1 + m[1,1] - m[0,0] - m[2,2]) * 2; return np.array([(m[0,1]+m[1,0])/s, s/4, (m[1,2]+m[2,1])/s, (m[0,2]-m[2,0])/s])
    s = np.sqrt(1 + m[2,2] - m[0,0] - m[1,1]) * 2; return np.array([(m[0,2]+m[2,0])/s, (m[1,2]+m[2,1])/s, s/4, (m[1,0]-m[0,1])/s])
def m_from_q(q):
    x, y, z, w = q / np.linalg.norm(q)
    return np.array([[1-2*(y*y+z*z), 2*(x*y-z*w), 2*(x*z+y*w)], [2*(x*y+z*w), 1-2*(x*x+z*z), 2*(y*z-x*w)], [2*(x*z-y*w), 2*(y*z+x*w), 1-2*(x*x+y*y)]])
def ortho(m):
    u, _, vt = np.linalg.svd(m); r = u @ vt
    if np.linalg.det(r) < 0: u[:, -1] *= -1; r = u @ vt
    return r
def roty(deg):
    a = np.radians(deg); return np.array([[np.cos(a), 0, np.sin(a)], [0, 1, 0], [-np.sin(a), 0, np.cos(a)]])

MOVES = lambda n: any(k in n for k in ("Walk", "Run", "Sprint", "Strafe", "Jump"))

class Clip:
    def __init__(self, name, path=None, mirror_of=None):
        self.name = name
        d = np.load(path or f"{CLIPS}/{mirror_of or name}.npz")
        self.bones = [str(b) for b in d['bones']]; self.par = list(d['par']); self.fps = float(d['fps'])
        rest = d['rest']; fr = d['frames']
        n = len(self.bones); T = fr.shape[0]
        Rr = np.array([ortho(C @ rest[i][:3, :3]) for i in range(n)])
        D = np.zeros((T, n, 3, 3)); H = np.zeros((T, 3))
        hi = self.bones.index('Hips')
        for t in range(T):
            for i in range(n):
                D[t, i] = ortho(C @ fr[t, i][:3, :3]) @ Rr[i].T
            H[t] = C @ fr[t, hi][:3, 3]
        self.rest_h = (C @ rest[hi][:3, 3])[1]
        if MOVES(name):   # in place: the root takes the average motion (Unity root motion, not baked)
            lin = np.linspace(0, 1, T)[:, None] * (H[-1] - H[0]) + H[0]
            H[:, 0] -= lin[:, 0] - H[0, 0]; H[:, 2] -= lin[:, 2] - H[0, 2]
            self.travel = H[-1] - H[0]
        if mirror_of:
            S = np.diag([-1., 1., 1.])
            idx = [self.bones.index(b.replace('Left', '#').replace('Right', 'Left').replace('#', 'Right')) for b in self.bones]
            D = np.einsum('ab,tnbc,cd->tnad', S, D[:, idx], S); H = H * np.array([-1, 1, 1])
        # local rest-relative rotations
        L = np.zeros_like(D)
        for i in range(n):
            p = self.par[i]
            L[:, i] = D[:, i] if p < 0 else np.einsum('tba,tbc->tac', D[:, p], D[:, i])
        self.Q = np.array([[q_from_m(L[t, i]) for i in range(n)] for t in range(T)])
        self.H = H; self.T = T
        self.length = (T - 1) / self.fps if T > 1 else 1 / self.fps
    def sample(self, u):
        """u = normalized time (loops) -> (quats[n,4], hips[3])"""
        x = (u % 1.0) * (self.T - 1); i0 = int(np.floor(x)); i1 = min(i0 + 1, self.T - 1); f = x - i0
        q0, q1 = self.Q[i0], self.Q[i1].copy()
        s = np.sign(np.sum(q0 * q1, axis=1, keepdims=True)); s[s == 0] = 1
        q = q0 * (1 - f) + q1 * s * f
        return q / np.linalg.norm(q, axis=1, keepdims=True), self.H[i0] * (1 - f) + self.H[i1] * f

class Skeleton:
    """the reference skeleton (X Bot from the pack) for FK"""
    def __init__(self, name="RifleIdle"):
        d = np.load(f"{CLIPS}/{name}.npz")
        self.bones = [str(b) for b in d['bones']]; self.par = list(d['par'])
        self.restp = np.array([C @ d['rest'][i][:3, 3] for i in range(len(self.bones))])
    def fk(self, Q, hips):
        n = len(self.bones); D = np.zeros((n, 3, 3)); P = np.zeros((n, 3))
        for i in range(n):
            p = self.par[i]; L = m_from_q(Q[i])
            if p < 0: D[i] = L; P[i] = hips
            else: D[i] = D[p] @ L; P[i] = P[p] + D[p] @ (self.restp[i] - self.restp[p])
        return D, P

def blend(samples):
    """samples: list of (weight, quats, hips) -> quats, hips (mixer: weighted, normalised)"""
    ws = sum(w for w, _, _ in samples)
    ref = max(samples, key=lambda s: s[0])[1]
    acc = np.zeros_like(ref); hp = np.zeros(3)
    for w, q, h in samples:
        s = np.sign(np.sum(q * ref, axis=1, keepdims=True)); s[s == 0] = 1
        acc += w / ws * q * s; hp += w / ws * h
    return acc / np.linalg.norm(acc, axis=1, keepdims=True), hp
