# Repairs baked-in leg spikes in a body .glb (madmax: the right knee had a wedge of inner-thigh vertices dragged toward the left knee).
# Per leg, vertices of the big pants pieces are measured radially around the UpLeg-Leg-Foot bone line; those sticking out more than THR
# (default 3 cm) above the local median (same angle +-20 deg, height +-12 cm) are pulled back, then relaxed with their neighbours.
# Usage: python3 repair_legs.py in.glb out.glb [THR=0.03]   then: python3 reweight.py Models/Player_male.glb out.glb Player_max.glb
import os,sys,json,struct,numpy as np,collections
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
src,out=sys.argv[1],sys.argv[2]; THR=float(sys.argv[3]) if len(sys.argv)>3 else 0.02
j,acc=load(src); pr=j['meshes'][0]['primitives'][0]; s=j['skins'][0]
P=acc(pr['attributes']['POSITION']).copy(); T=acc(pr['indices']).astype(int).reshape(-1,3)
ibm=acc(s['inverseBindMatrices']).reshape(-1,4,4).transpose(0,2,1)
jp={j['nodes'][ji]['name'][10:]:np.linalg.inv(ibm[k])[:3,3] for k,ji in enumerate(s['joints'])}
key=np.round(P/1e-5).astype(np.int64); _,first,inv=np.unique(key,axis=0,return_index=True,return_inverse=True); inv=inv.ravel()
U=P[first].copy(); nu=len(U)
# components on welded verts
par=np.arange(nu)
def f(x):
    r=x
    while par[r]!=r: r=par[r]
    while par[x]!=r: par[x],x=r,par[x]
    return r
for a,b,c in inv[T]:
    ra=f(a); par[f(b)]=ra; par[f(c)]=ra
roots=np.array([f(i) for i in range(nu)]); big=collections.Counter(roots)
# adjacency
E=np.concatenate([inv[T][:,[0,1]],inv[T][:,[1,2]],inv[T][:,[2,0]]]); E=np.unique(np.concatenate([E,E[:,::-1]]),axis=0)
nbr=[[] for _ in range(nu)]
for a,b in E:
    if a!=b: nbr[a].append(b)
def chain(side):
    return [jp[side+'UpLeg'],jp[side+'Leg'],jp[side+'Foot']]
def param(p,ch):
    # distance along chain + radial vector, using closest segment
    best=None; acc_=0
    for a,b in zip(ch[:-1],ch[1:]):
        d=b-a; L=np.linalg.norm(d); t=np.clip(((p-a)@d)/L**2,0,1); q=a+t[:,None]*d
        dist=np.linalg.norm(p-q,axis=1)
        if best is None: best=[dist,acc_+t*L,p-q,np.tile(d/L,(len(p),1))]
        else:
            m=dist<best[0]; best[0]=np.where(m,dist,best[0]); best[1]=np.where(m,acc_+t*L,best[1]); best[2]=np.where(m[:,None],p-q,best[2]); best[3]=np.where(m[:,None],d/L,best[3])
        acc_+=L
    return best
moved=np.zeros(nu,bool)
for side,sg in (('Right',-1),('Left',1)):
    ch=chain(side)
    sel=np.where((U[:,0]*sg>0.0)&(U[:,1]<-0.12)&(U[:,1]>-0.82))[0]
    # only the big pants piece(s): components with >1000 verts
    sel=sel[np.array([big[roots[i]]>400 for i in sel])]
    r,h,rv,ax=param(U[sel],ch)
    # angle around the leg: reference axes
    ref=np.array([0,0,1.0]); e1=ref-np.outer(ax@ref,np.ones(3))*ax; e1/=np.linalg.norm(e1,axis=1,keepdims=True); e2=np.cross(ax,e1)
    th=np.arctan2((rv*e2).sum(1),(rv*e1).sum(1))
    fit=np.zeros(len(sel))
    for k in range(len(sel)):
        dth=np.angle(np.exp(1j*(th-th[k])))
        m=(np.abs(dth)<0.35)&(np.abs(h-h[k])<0.12)
        fit[k]=np.median(r[m])
    bad=(r-fit)>THR
    print(side,'leg verts',len(sel),'outliers',bad.sum(),'max dev %.3f'%np.abs(r-fit).max())
    idx=sel[bad]; U[idx]-= (rv[bad]/np.maximum(r[bad],1e-6)[:,None])*(r[bad]-fit[bad])[:,None]; moved[idx]=True
# relax moved verts (+1 ring) toward neighbor average, others fixed
ring=moved.copy()
for i in np.where(moved)[0]: ring[nbr[i]]=True
for it in range(10):
    newU=U.copy()
    for i in np.where(ring)[0]:
        if nbr[i]: newU[i]=0.5*U[i]+0.5*U[nbr[i]].mean(0)
    U=newU
print('moved',moved.sum(),'relaxed',ring.sum())
P2=U[inv].astype(np.float32)
# write: replace POSITION accessor data in place (same count/type) -> append new buffer
b=open(src,'rb').read(); l=struct.unpack('<I',b[12:16])[0]; jj=json.loads(b[20:20+l]); o0=20+l; bl=struct.unpack('<I',b[o0:o0+4])[0]; binb=bytearray(b[o0+8:o0+8+bl])
pr2=jj['meshes'][0]['primitives'][0]
while len(binb)%4: binb.append(0)
off=len(binb); binb.extend(P2.tobytes())
jj['bufferViews'].append({'buffer':0,'byteOffset':off,'byteLength':P2.nbytes})
jj['accessors'].append({'bufferView':len(jj['bufferViews'])-1,'componentType':5126,'count':len(P2),'type':'VEC3','min':P2.min(0).tolist(),'max':P2.max(0).tolist()})
pr2['attributes']['POSITION']=len(jj['accessors'])-1
jj['buffers'][0]['byteLength']=len(binb)
js=json.dumps(jj,separators=(',',':')).encode()
while len(js)%4: js+=b' '
open(out,'wb').write(struct.pack('<III',0x46546C67,2,12+8+len(js)+8+len(binb))+struct.pack('<II',len(js),0x4E4F534A)+js+struct.pack('<II',len(binb),0x004E4942)+bytes(binb))
