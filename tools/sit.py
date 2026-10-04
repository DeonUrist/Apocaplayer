import json,numpy as np,glb,render
from PIL import Image
def q2m(q): return glb.q2m(q)
def trs(t):
    M=np.eye(4); M[:3,:3]=q2m(t[3:7])@np.diag(t[7:10]); M[:3,3]=t[:3]; return M
pf=json.load(open('_export/prefabs.json'))['Rustallion']
root=[k for k in pf if k.endswith('PlayerModel_Sit')][0]
world={}
for k in sorted(pf,key=lambda s:s.count('/')):
    if not k.startswith(root): continue
    par=k.rsplit('/',1)[0]
    world[k]=(world[par] if k!=root else np.eye(4))@trs(pf[k]['trs'])
P2=json.load(open('_export/Player2.json'))
V=np.array(P2['v'],float).reshape(-1,3); T=np.array(P2['tris']).reshape(-1,3); UV=np.array(P2['uv']).reshape(-1,2).copy(); UV[:,1]=1-UV[:,1]
bn=[b.split('/')[-1] for b in P2['bones']]
bpath={}
for k in world:
    bpath.setdefault(k.split('/')[-1],k)
BP=[np.vstack([np.array(b).reshape(3,4),[0,0,0,1]]) for b in P2['bind']]
smr=world[[k for k in world if k.endswith('playermodel')][0]]
bw=np.array(P2['bw']); bi=np.array(P2['bi'])
Vh=np.c_[V,np.ones(len(V))]
def skin(Ms):
    out=np.zeros((len(V),3))
    for k in range(4):
        M=np.array([Ms[i] for i in bi[:,k]]); out+=bw[:,k:k+1]*np.einsum('nij,nj->ni',M,Vh)[:,:3]
    return out
# skin matrix = inv(smr) * boneworld * bindpose  (mesh space of SMR)
Ms=[np.linalg.inv(smr)@world[bpath[b]]@BP[i] for i,b in enumerate(bn)]
S=skin(Ms)
# to world-ish (PlayerModel_Sit space), smr is identity mostly
Sw=(smr@np.c_[S,np.ones(len(S))].T).T[:,:3]
tex=Image.open('_export/Player2.png')
ims=[]
for v in ['front','side','top']:
    c,s=render.render(Sw,T,UV,tex,view=v,out='t.png',size=400); ims.append(Image.open('t.png').copy())
im=Image.new('RGB',(1200,400)); [im.paste(x,(i*400,0)) for i,x in enumerate(ims)]; im.save('p2_sit.png')
print('smr world',smr.round(3)); print('rest-check', np.abs(skin([np.eye(4)]*len(bn))-V).max())
json.dump({'world':{k:v.tolist() for k,v in world.items()}},open('sitworld.json','w'))
