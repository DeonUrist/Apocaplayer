# Re-weights a body .glb that uses the 22-bone mixamorig skeleton: weights are transferred from a working model (closest point on its
# surface, barycentric), limb weights forced to the vertex side, smoothed N times over the welded mesh, top 4 kept.
# Usage: python3 reweight.py Models/Player_male.glb <broken.glb> <out.glb> [smooth=2]   (used for Player_max.glb from Assets/Models/madmax)
import json,struct,sys,numpy as np
CT={5120:np.int8,5121:np.uint8,5122:np.int16,5123:np.uint16,5125:np.uint32,5126:np.float32}
NC={'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4,'MAT4':16}
def load(p):
    b=open(p,'rb').read(); l=struct.unpack('<I',b[12:16])[0]
    j=json.loads(b[20:20+l]); o=20+l; bl=struct.unpack('<I',b[o:o+4])[0]; bin=b[o+8:o+8+bl]
    def acc(i):
        a=j['accessors'][i]; bv=j['bufferViews'][a['bufferView']]; dt=CT[a['componentType']]; n=NC[a['type']]
        off=bv.get('byteOffset',0)+a.get('byteOffset',0); st=bv.get('byteStride')
        isz=np.dtype(dt).itemsize*n
        if st and st!=isz:
            arr=np.array([np.frombuffer(bin,dt,n,off+k*st) for k in range(a['count'])])
        else: arr=np.frombuffer(bin,dt,a['count']*n,off).reshape(a['count'],n)
        if a.get('normalized'): arr=arr/np.iinfo(dt).max
        return arr.astype(np.float64) if dt==np.float32 or a.get('normalized') else arr
    return j,acc
src,dst,out=sys.argv[1:4]
def mesh(p):
    j,acc=load(p); pr=j['meshes'][0]['primitives'][0]
    P=acc(pr['attributes']['POSITION']); J=acc(pr['attributes']['JOINTS_0']).astype(int); W=acc(pr['attributes']['WEIGHTS_0'])
    T=acc(pr['indices']).astype(int).reshape(-1,3)
    names=[j['nodes'][i]['name'] for i in j['skins'][0]['joints']]
    return j,P,J,W,T,names
sj,SP,SJ,SW,ST,snames=mesh(src); dj,DP,DJ,DW,DT,dnames=mesh(dst)
assert snames==dnames
nb=len(snames)
# dense source weights
SD=np.zeros((len(SP),nb))
for c in range(4): np.add.at(SD,(np.arange(len(SP)),SJ[:,c]),SW[:,c])
A=SP[ST[:,0]];B=SP[ST[:,1]];C=SP[ST[:,2]]
def closest(p):
    # p (n,3) vs all tris -> barycentric of closest point, returns (u,v,w,dist2) per tri; vectorized (n,t)
    ab=B-A; ac=C-A; ap=p[:,None,:]-A[None]
    d00=(ab*ab).sum(-1); d01=(ab*ac).sum(-1); d11=(ac*ac).sum(-1)
    d20=(ap*ab).sum(-1); d21=(ap*ac).sum(-1)
    den=d00*d11-d01*d01; den=np.where(abs(den)<1e-12,1e-12,den)
    v=(d11*d20-d01*d21)/den; w=(d00*d21-d01*d20)/den
    # clamp into triangle (approximate: project onto region then renormalize)
    v=np.clip(v,0,1); w=np.clip(w,0,1); s=v+w; over=s>1; v=np.where(over,v/s,v); w=np.where(over,w/s,w); u=1-v-w
    q=u[...,None]*A+v[...,None]*B+w[...,None]*C
    d=((q-p[:,None,:])**2).sum(-1)
    return u,v,w,d
DD=np.zeros((len(DP),nb)); dist=np.zeros(len(DP))
for s in range(0,len(DP),128):
    p=DP[s:s+128]; u,v,w,d=closest(p); k=d.argmin(1); r=np.arange(len(p))
    t=ST[k]; DD[s:s+128]=u[r,k,None]*SD[t[:,0]]+v[r,k,None]*SD[t[:,1]]+w[r,k,None]*SD[t[:,2]]; dist[s:s+128]=np.sqrt(d[r,k])
print('transfer dist mean %.4f max %.4f  >3cm %d'%(dist.mean(),dist.max(),(dist>0.03).sum()))
short=[n[10:] for n in snames]; idx={n:i for i,n in enumerate(short)}
# side fix for limbs
for part in ['UpLeg','Leg','Foot','ToeBase','Shoulder','Arm','ForeArm','Hand']:
    L=idx['Left'+part]; R=idx['Right'+part]
    m=DP[:,0]>0.03; DD[m,L]+=DD[m,R]; DD[m,R]=0
    m=DP[:,0]<-0.03; DD[m,R]+=DD[m,L]; DD[m,L]=0
# weld + smooth over adjacency
key=np.round(DP/1e-5).astype(np.int64); _,wid,inv=np.unique(key,axis=0,return_index=True,return_inverse=True); inv=inv.ravel()
nw=inv.max()+1
E=np.concatenate([DT[:,[0,1]],DT[:,[1,2]],DT[:,[2,0]]]); E=inv[E]; E=np.concatenate([E,E[:,::-1]])
WW=np.zeros((nw,nb)); np.add.at(WW,inv,DD); cnt=np.bincount(inv,minlength=nw)[:,None]; WW/=cnt
deg=np.bincount(E[:,0],minlength=nw)[:,None]
for it in range(int(sys.argv[4]) if len(sys.argv)>4 else 2):
    acc_=np.zeros_like(WW); np.add.at(acc_,E[:,0],WW[E[:,1]]); WW=0.5*WW+0.5*acc_/np.maximum(deg,1)
DD=WW[inv]
# top4 normalize
o=np.argsort(-DD,1)[:,:4]; w4=np.take_along_axis(DD,o,1); w4[w4<0.01]=0; w4/=w4.sum(1,keepdims=True)
J4=o.astype(np.uint16); W4=w4.astype(np.float32)
for c in range(4): J4[:,c]=np.where(W4[:,c]==0,0,J4[:,c])
# write glb
b=open(dst,'rb').read(); l=struct.unpack('<I',b[12:16])[0]; j=json.loads(b[20:20+l]); o0=20+l; bl=struct.unpack('<I',b[o0:o0+4])[0]; binb=bytearray(b[o0+8:o0+8+bl])
pr=j['meshes'][0]['primitives'][0]
def add(data,ctype,typ,count):
    while len(binb)%4: binb.append(0)
    off=len(binb); binb.extend(data)
    j['bufferViews'].append({'buffer':0,'byteOffset':off,'byteLength':len(data)})
    j['accessors'].append({'bufferView':len(j['bufferViews'])-1,'componentType':ctype,'count':count,'type':typ}); return len(j['accessors'])-1
pr['attributes']['JOINTS_0']=add(J4.tobytes(),5123,'VEC4',len(J4))
pr['attributes']['WEIGHTS_0']=add(W4.tobytes(),5126,'VEC4',len(W4))
while len(binb)%4: binb.append(0)
j['buffers'][0]['byteLength']=len(binb)
js=json.dumps(j,separators=(',',':')).encode();
while len(js)%4: js+=b' '
tot=12+8+len(js)+8+len(binb)
open(out,'wb').write(struct.pack('<III',0x46546C67,2,tot)+struct.pack('<II',len(js),0x4E4F534A)+js+struct.pack('<II',len(binb),0x004E4942)+bytes(binb))
print('wrote',out,tot)
