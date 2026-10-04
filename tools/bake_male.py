# Player2 (the game's man, 27-bone Armature) -> a glb on Flexa's mixamo skeleton (Boss_lady.glb's nodes), for FemalePlayer's Male mode.
# Each of his bones follows the mixamo bone it maps to (CarSeat.Pairs / retarget.py MAP): his bind mesh is overlaid on her bind skeleton
# (R4: his mesh space -> hers) and each part turned by the same swing CarSeat uses (his bone direction onto hers), so at runtime
# herBone * Inverse(D) == his bone, exactly like the seated driver.
import json, struct, numpy as np, glb
from PIL import Image
from fbind import BPF
P2 = json.load(open('_export/Player2.json'))
bn = [b.split('/')[-1] for b in P2['bones']]
BPP = {b: np.vstack([np.array(x).reshape(3, 4), [0, 0, 0, 1]]) for b, x in zip(bn, P2['bind'])}
R4 = np.eye(4); R4[:3, :3] = np.array([[1, 0, 0], [0, 0, 1], [0, -1, 0]])
MAP = [('spine1','Hips'),('spine2','Spine'),('spin3','Spine2'),('neck','Neck'),('head','Head'),
       ('arm1 Left','LeftShoulder'),('arm2Left','LeftArm'),('arm3 Left','LeftForeArm'),('hand1 Left','LeftHand'),
       ('arm1 Right','RightShoulder'),('arm2Right','RightArm'),('arm3 Right','RightForeArm'),('hand1 Right','RightHand'),
       ('leg1 Left','LeftUpLeg'),('leg2 Left','LeftLeg'),('leg3 Left','LeftFoot'),
       ('leg1 Right','RightUpLeg'),('leg2 Right','RightLeg'),('leg3 Right','RightFoot')]
CHILD_P = {'spine1':'spine2','spine2':'spin3','spin3':'neck','neck':'head','arm1 Left':'arm2Left','arm2Left':'arm3 Left','arm3 Left':'hand1 Left','hand1 Left':'hand2 Left',
 'arm1 Right':'arm2Right','arm2Right':'arm3 Right','arm3 Right':'hand1 Right','hand1 Right':'hand2 Right','leg1 Left':'leg2 Left','leg2 Left':'leg3 Left','leg1 Right':'leg2 Right','leg2 Right':'leg3 Right'}
CHILD_F = {'Hips':'Spine','Spine':'Spine1','Spine1':'Spine2','Spine2':'Neck','Neck':'Head','LeftShoulder':'LeftArm','LeftArm':'LeftForeArm','LeftForeArm':'LeftHand',
 'RightShoulder':'RightArm','RightArm':'RightForeArm','RightForeArm':'RightHand','LeftUpLeg':'LeftLeg','LeftLeg':'LeftFoot','RightUpLeg':'RightLeg','RightLeg':'RightFoot','LeftFoot':'LeftToeBase','RightFoot':'RightToeBase'}
def fromto(a, b):
    a = a/np.linalg.norm(a); b = b/np.linalg.norm(b); v = np.cross(a, b); c = a@b
    if c < -0.9999: return -np.eye(3)
    K = np.array([[0,-v[2],v[1]],[v[2],0,-v[0]],[-v[1],v[0],0]]); return np.eye(3)+K+K@K/(1+c)
Pb = {b: np.linalg.inv(BPP[b]) for b in bn}
Fb = {f: np.linalg.inv(R4)@np.linalg.inv(BPF[f]) for f in BPF}
# per his bone: the matrix that moves his bind mesh (his mesh space) to where it sits on her bind skeleton
Mp = {}; F_of = {}
for p, f in MAP:
    Fm = Fb[f].copy()
    if f in CHILD_F and p in CHILD_P and f != 'Spine2':
        dp = Pb[CHILD_P[p]][:3,3]-Pb[p][:3,3]; df = Fb[CHILD_F[f]][:3,3]-Fb[f][:3,3]
        Fm[:3,:3] = fromto(df, dp)@Fm[:3,:3]
    Mp[p] = Fb[f]@np.linalg.inv(Fm); F_of[p] = f
# unmapped bones (fingers, thumbs, Armature root): their nearest mapped ancestor
for path in P2['bones']:
    b = path.split('/')[-1]
    if b in F_of: continue
    for anc in reversed(path.split('/')[:-1]):
        if anc in F_of: F_of[b] = F_of[anc]; Mp[b] = Mp[anc]; break
    else: F_of[b] = 'Hips'; Mp[b] = Mp['spine1']
V = np.array(P2['v'], float).reshape(-1,3); N = np.array(P2['n'], float).reshape(-1,3)
UV = np.array(P2['uv'], float).reshape(-1,2); T = np.array(P2['tris']).reshape(-1,3)
bw = np.array(P2['bw'], float); bi = np.array(P2['bi'])
Vh = np.c_[V, np.ones(len(V))]
out = np.zeros((len(V),3)); nout = np.zeros((len(V),3))
for k in range(4):
    M = np.array([R4@Mp[bn[i]] for i in bi[:,k]])
    out += bw[:,k:k+1]*np.einsum('nij,nj->ni', M, Vh)[:,:3]
    nout += bw[:,k:k+1]*np.einsum('nij,nj->ni', M[:,:3,:3], N)
out /= bw.sum(1, keepdims=True)
nout /= np.linalg.norm(nout, axis=1, keepdims=True)+1e-9
fem = glb.load('Boss_lady.glb')
jn = [n.split(':')[1] for n in fem['jnames']]
jidx = {n: i for i, n in enumerate(jn)}
J = np.zeros((len(V),4), int); W = np.zeros((len(V),4))
for v in range(len(V)):
    acc = {}
    for k in range(4):
        if bw[v,k] <= 0: continue
        j = jidx[F_of[bn[bi[v,k]]]]; acc[j] = acc.get(j,0)+bw[v,k]
    it = sorted(acc.items(), key=lambda x: -x[1])[:4]; s = sum(w for _,w in it)
    for k,(j,w) in enumerate(it): J[v,k] = j; W[v,k] = w/s
# Unity (left-handed) -> glTF: mirror X, flip v, reverse winding (Gltf.cs undoes all three)
Pg = out.copy(); Pg[:,0] *= -1; Ng = nout.copy(); Ng[:,0] *= -1
UVg = UV.copy(); UVg[:,1] = 1-UVg[:,1]
Tg = T[:, [0,2,1]]
# height check vs hers
print('his mesh (her space): y %.3f..%.3f  x %.3f..%.3f' % (out[:,1].min(), out[:,1].max(), out[:,0].min(), out[:,0].max()))
print('her mesh:             y %.3f..%.3f' % (fem['P'][:,1].min(), fem['P'][:,1].max()))
np.savez('male.npz', P=Pg, N=Ng, UV=UVg, T=Tg, J=J, W=W)

# ---- rigid extras of the seated man (Hair, Beard on his head; bags + pouch on spine2), baked in as 100 % that bone
ex = json.load(open('_export/extras.json'))
pf = json.load(open('_export/prefabs.json'))['Rustallion']
def trs(t):
    M = np.eye(4); M[:3,:3] = glb.q2m(t[3:7])@np.diag(t[7:10]); M[:3,3] = t[:3]; return M
EXTRA = [('PlayerHair.002','head','/Armature/spine1/spine2/spin3/neck/head/Hair'), ('PlayerBeard.003','head','/Armature/spine1/spine2/spin3/neck/head/Beard'),
         ('bag1','spine2','/Armature/spine1/spine2/bag1'), ('bag2','spine2','/Armature/spine1/spine2/bag2'), ('pouch1.001','spine2','/Armature/spine1/spine2/pouch')]
root = [k for k in pf if k.endswith('PlayerModel_Sit')][0]
tex = np.asarray(Image.open('_export/Player2.png').convert('RGB')).astype(float); th, tw = tex.shape[:2]
def texel(uv): return tex[int((1-uv[1])*(th-1)), int(uv[0]*(tw-1))]
# the beard's own texture (hair3) isn't exported: its verts take one texel of the hair colour in the Player2 atlas
hu = np.array(ex['PlayerHair.002']['uv']).reshape(-1,2)
cols = np.array([texel(u) for u in hu]); lum = cols.sum(1)
beardUV = hu[np.argsort(lum)[len(lum)//4]]
print('beard colour', texel(beardUV), 'at', beardUV)
allP=[out]; allN=[nout]; allUV=[UV]; allT=[T]; allJ=[J]; allW=[W]
base = len(V)
for mesh, bone, rel in EXTRA:
    e = ex[mesh]; ev = np.array(e['v'],float).reshape(-1,3); en = np.array(e['n'],float).reshape(-1,3); eu = np.array(e['uv'],float).reshape(-1,2)
    et = np.array(e['tris']).reshape(-1,3)
    M = R4@Mp[bone]@Pb[bone]@trs(pf[root+rel]['trs'])        # extra mesh space -> his bind (via his bone) -> her bind
    allP.append((M@np.c_[ev,np.ones(len(ev))].T).T[:,:3]); n = (M[:3,:3]@en.T).T; allN.append(n/np.linalg.norm(n,axis=1,keepdims=True))
    if mesh.startswith('PlayerBeard'): eu = np.tile(beardUV, (len(ev),1))
    allUV.append(eu); allT.append(et+base)
    j = np.zeros((len(ev),4),int); j[:,0] = jidx[F_of[bone]]; w = np.zeros((len(ev),4)); w[:,0] = 1
    allJ.append(j); allW.append(w); base += len(ev)
out=np.vstack(allP); nout=np.vstack(allN); UV=np.vstack(allUV); T=np.vstack(allT); J=np.vstack(allJ); W=np.vstack(allW)
Pg = out.copy(); Pg[:,0] *= -1; Ng = nout.copy(); Ng[:,0] *= -1
UVg = UV.copy(); UVg[:,1] = 1-UVg[:,1]
Tg = T[:, [0,2,1]]
np.savez('male.npz', P=Pg, N=Ng, UV=UVg, T=Tg, J=J, W=W)
print('total verts', len(Pg), 'tris', len(Tg))

# ---- glb: Boss_lady's nodes + skin (Flexa's skeleton), inverse binds = inverse joint worlds (mesh stored in the bind pose), one primitive
js = fem['js']; Wn = fem['W']
joints = fem['joints']
ibm = np.array([np.linalg.inv(Wn[j]) for j in joints])
mesh_node = [i for i,n in enumerate(js['nodes']) if 'mesh' in n and 'skin' in n][0]
bufs = []; views = []; accs = []
def add(arr, ctype, typ, target=None, minmax=False):
    raw = arr.tobytes(); off = sum(len(b) for b in bufs); pad = (-len(raw)) % 4
    bufs.append(raw + b'\0'*pad)
    v = {'buffer':0,'byteOffset':off,'byteLength':len(raw)}
    if target: v['target'] = target
    views.append(v)
    a = {'bufferView':len(views)-1,'componentType':ctype,'count':len(arr),'type':typ}
    if minmax: a['min'] = arr.min(0).tolist(); a['max'] = arr.max(0).tolist()
    accs.append(a); return len(accs)-1
iP = add(Pg.astype(np.float32),5126,'VEC3',34962,True); iN = add(Ng.astype(np.float32),5126,'VEC3',34962)
iU = add(UVg.astype(np.float32),5126,'VEC2',34962); iJ = add(J.astype(np.uint16),5123,'VEC4',34962)
iW = add(W.astype(np.float32),5126,'VEC4',34962); iI = add(Tg.reshape(-1).astype(np.uint32),5125,'SCALAR',34963)
iB = add(ibm.transpose(0,2,1).reshape(-1,16).astype(np.float32),5126,'MAT4')
nodes = json.loads(json.dumps(js['nodes']))
for n in nodes: n.pop('mesh',None); n.pop('skin',None)
nodes[mesh_node]['mesh'] = 0; nodes[mesh_node]['skin'] = 0; nodes[mesh_node]['name'] = 'Player_male'
out_js = {'asset':{'version':'2.0','generator':'FemalePlayer bake_male.py'},'scene':0,'scenes':js.get('scenes',[{'nodes':[0]}]),'nodes':nodes,
          'meshes':[{'name':'Player_male','primitives':[{'attributes':{'POSITION':iP,'NORMAL':iN,'TEXCOORD_0':iU,'JOINTS_0':iJ,'WEIGHTS_0':iW},'indices':iI}]}],
          'skins':[{'joints':joints,'inverseBindMatrices':iB,**({'skeleton':js['skins'][0]['skeleton']} if 'skeleton' in js['skins'][0] else {})}],
          'accessors':accs,'bufferViews':views,'buffers':[{'byteLength':sum(len(b) for b in bufs)}]}
jb = json.dumps(out_js).encode(); jb += b' '*((-len(jb))%4); bb = b''.join(bufs)
with open('Player_male.glb','wb') as f:
    f.write(struct.pack('<III',0x46546C67,2,12+8+len(jb)+8+len(bb))); f.write(struct.pack('<II',len(jb),0x4E4F534A)); f.write(jb)
    f.write(struct.pack('<II',len(bb),0x004E4942)); f.write(bb)
print('wrote Player_male.glb', 12+16+len(jb)+len(bb))
