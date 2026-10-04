import json,numpy as np,glb,render
from PIL import Image
from fbind import BPF,PARENT
def q2m(q): return glb.q2m(q)
sw={k:np.array(v) for k,v in json.load(open('sitworld.json'))['world'].items()}
P2=json.load(open('_export/Player2.json'))
bn=[b.split('/')[-1] for b in P2['bones']]
BPP={b:np.vstack([np.array(x).reshape(3,4),[0,0,0,1]]) for b,x in zip(bn,P2['bind'])}
pw={k.split('/')[-1]:v for k,v in sw.items()}
R4=np.eye(4); R4[:3,:3]=np.array([[1,0,0],[0,0,1],[0,-1,0]])  # P2 mesh -> female mesh
MAP=[('spine1','Hips'),('spine2','Spine'),('spin3','Spine1'),('spin3','Spine2'),('neck','Neck'),('head','Head'),
     ('arm1 Left','LeftShoulder'),('arm2Left','LeftArm'),('arm3 Left','LeftForeArm'),('hand1 Left','LeftHand'),
     ('arm1 Right','RightShoulder'),('arm2Right','RightArm'),('arm3 Right','RightForeArm'),('hand1 Right','RightHand'),
     ('leg1 Left','LeftUpLeg'),('leg2 Left','LeftLeg'),('leg3 Left','LeftFoot'),
     ('leg1 Right','RightUpLeg'),('leg2 Right','RightLeg'),('leg3 Right','RightFoot')]
CHILD_P={'spine1':'spine2','spine2':'spin3','spin3':'neck','neck':'head','arm1 Left':'arm2Left','arm2Left':'arm3 Left','arm3 Left':'hand1 Left','hand1 Left':'hand2 Left',
 'arm1 Right':'arm2Right','arm2Right':'arm3 Right','arm3 Right':'hand1 Right','hand1 Right':'hand2 Right','leg1 Left':'leg2 Left','leg2 Left':'leg3 Left','leg1 Right':'leg2 Right','leg2 Right':'leg3 Right'}
CHILD_F={'Hips':'Spine','Spine':'Spine1','Spine1':'Spine2','Spine2':'Neck','Neck':'Head','LeftShoulder':'LeftArm','LeftArm':'LeftForeArm','LeftForeArm':'LeftHand',
 'RightShoulder':'RightArm','RightArm':'RightForeArm','RightForeArm':'RightHand','LeftUpLeg':'LeftLeg','LeftLeg':'LeftFoot','RightUpLeg':'RightLeg','RightLeg':'RightFoot','LeftFoot':'LeftToeBase','RightFoot':'RightToeBase'}
def fromto(a,b):
    a=a/np.linalg.norm(a); b=b/np.linalg.norm(b); v=np.cross(a,b); c=a@b
    if c<-0.9999: return -np.eye(3)
    K=np.array([[0,-v[2],v[1]],[v[2],0,-v[0]],[-v[1],v[0],0]]); return np.eye(3)+K+K@K/(1+c)
# bind worlds in P2 mesh space
Pb={b:np.linalg.inv(BPP[b]) for b in bn}
Fb={f:np.linalg.inv(R4)@np.linalg.inv(BPF[f]) for f in BPF}   # female bind overlaid into P2 mesh space
D={}
for p,f in MAP:
    Fm=Fb[f].copy()
    if f in CHILD_F and p in CHILD_P and f not in ('Spine2',):
        dp=Pb[CHILD_P[p]][:3,3]-Pb[p][:3,3]; df=Fb[CHILD_F[f]][:3,3]-Fb[f][:3,3]
        S=fromto(df,dp); Fm[:3,:3]=S@Fm[:3,:3]
    D[f]=(p,np.linalg.inv(Pb[p])@Fm)
# runtime emulation: female local bind (positions), world = parent * local, mapped rotation override
def fpose():
    W={}
    order=['Hips','Spine','Spine1','Spine2','Neck','Head','LeftShoulder','LeftArm','LeftForeArm','LeftHand','RightShoulder','RightArm','RightForeArm','RightHand','LeftUpLeg','LeftLeg','LeftFoot','LeftToeBase','RightUpLeg','RightLeg','RightFoot','RightToeBase']
    for f in order:
        par=PARENT[f]
        if par is None:
            p,d=D[f]; M=pw[p]@d
        else:
            loc=np.linalg.inv(np.linalg.inv(BPF[par]))@np.linalg.inv(BPF[f])
            M=W[par]@loc
            if f in D:
                p,d=D[f]; T=pw[p]@d; M=M.copy(); M[:3,:3]=T[:3,:3]
        W[f]=M
    return W
W=fpose()
fem=glb.load('Boss_lady.glb')
FP=fem['P'].copy(); FP[:,0]*=-1
jn=[n.split(':')[1] for n in fem['jnames']]
Vh=np.c_[FP,np.ones(len(FP))]; out=np.zeros((len(FP),3))
for k in range(4):
    Ms=np.array([W[jn[j]]@BPF[jn[j]] for j in fem['J'][:,k]])
    out+=fem['Wt'][:,k:k+1]*np.einsum('nij,nj->ni',Ms,Vh)[:,:3]
out/=fem['Wt'].sum(1,keepdims=True)
T=fem['T'][:,[0,2,1]]
tex=Image.open('Boss_lady.png')
# also P2 render for overlay comparison
ims=[]
for v in ['front','side','top']:
    c,s=render.render(out,T,fem['UV'],tex,view=v,out='t.png',size=400); ims.append(Image.open('t.png').copy())
im=Image.new('RGB',(1200,400)); [im.paste(x,(i*400,0)) for i,x in enumerate(ims)]; im.save('fem_sit.png')
np.save('Ddelta.npy',{f:(p,d) for f,(p,d) in D.items()},allow_pickle=True)
