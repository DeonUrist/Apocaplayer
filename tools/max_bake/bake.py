# Max texture bake for Apocaplayer (Character = Max)
# target.glb : Denis's low-poly Max (the game man's mesh, edited; game man's skin weights)
# src.glb/png: the high-poly madmax model (14k verts, Assets/Models/madmax/Player_max_highpoly.glb) and its atlas
# 1. fresh unwrap of the target with xatlas (no overlaps, 6 px padding, head unwrapped at 2x scale = 4x texel density)
# 2. per texel: body = closest facing point on the high-poly surface after per-bone bounding-box alignment (the two bodies
#    have different proportions); head/neck = landmark-warped (eyes, nose, mouth, chin, hairline) orthographic front projection
#    for front-facing texels, spherical projection of the outermost head surface elsewhere; leg-mounted shotgun rig left out
# 3. 24 px edge dilation, output baked.png + glb (target with new UVs, embedded texture)
# run: python3 bake.py 2048   (pip: numpy scipy pillow xatlas)
import sys,time,numpy as np,xatlas
from scipy.spatial import cKDTree
from scipy import ndimage
from PIL import Image
from glb import *
RES=int(sys.argv[1]) if len(sys.argv)>1 else 2048
t0=time.time()
tg=G('target.glb').mesh(); sr=G('src.glb').mesh()
names=[n[10:] for n in tg['names']]; nb=len(names)
assert [n[10:] for n in sr['names']]==names
P,N,T,J,W=tg['P'],tg['N'],tg['T'],tg['J'],tg['W']
# ---------- 1. unwrap
# texel density: the face matters most - unwrap with the head scaled up 2x (4x texels); neck triangles take the gradient.
# Only the unwrap sees the scaled copy, the bake uses the real positions, so nothing is distorted, just denser.
hk0=names.index('Head'); hw=np.zeros(len(P))
for c in range(4): hw+=np.where(J[:,c]==hk0,W[:,c],0)
hcen=np.array([0.0,0.71,0.03]); PU=P+(P-hcen)*(np.clip((hw-0.2)/0.6,0,1)[:,None]*1.0)
atlas=xatlas.Atlas(); atlas.add_mesh(PU.astype(np.float32),T.astype(np.uint32),N.astype(np.float32))
co=xatlas.ChartOptions(); co.max_iterations=4
po=xatlas.PackOptions(); po.resolution=RES; po.padding=6; po.bilinear=True; po.blockAlign=True; po.bruteForce=True
atlas.generate(co,po)
vmap,idx,uvs=atlas[0]
print('atlas',atlas.width,atlas.height,'charts',atlas.chart_count,'verts',len(vmap),'util %.3f'%atlas.utilization, '%.1fs'%(time.time()-t0))
P2,N2,J2,W2=P[vmap],N[vmap],J[vmap],W[vmap]; T2=idx.astype(np.int64); UV2=uvs.astype(np.float64)*[atlas.width,atlas.height]/max(atlas.width,atlas.height)  # uniform texel scale into the square texture
np.savez('atlas.npz',vmap=vmap,idx=idx,uvs=uvs)
# dense weights per target vertex
D2=np.zeros((len(P2),nb)); 
for c in range(4): np.add.at(D2,(np.arange(len(P2)),J2[:,c]),W2[:,c])
# ---------- 2. per-part alignment target -> source
def part_boxes(Pm,Jm,Wm):
    dom=Jm[np.arange(len(Pm)),Wm.argmax(1)]; out={}
    for k in range(nb):
        m=dom==k
        if m.sum()>=8: out[k]=(np.percentile(Pm[m],2,0),np.percentile(Pm[m],98,0))
    return out
bt=part_boxes(P,J,W); bs=part_boxes(sr['P'],sr['J'],sr['W'])
maps=np.zeros((nb,2,3)); maps[:,0]=1   # scale, offset
for k in range(nb):
    if k in bt and k in bs:
        (tl,th),(sl,sh)=bt[k],bs[k]; sc=(sh-sl)/np.maximum(th-tl,1e-6); maps[k,0]=sc; maps[k,1]=sl-tl*sc
spine=names.index('Spine')
for k in range(nb):
    if not (k in bt and k in bs): maps[k]=maps[spine]
OVR={}  # optional per-part overrides filled below
exec(open('align_overrides.py').read()) if __import__('os').path.exists('align_overrides.py') else None
def align(p,d):  # p (n,3), d (n,nb) weights
    sc=d@maps[:,0]; of=d@maps[:,1]; return p*sc+of
# ---------- 3. source surface samples
Ps,Ts,UVs=sr['P'],sr['T'],sr['UV']
from scipy.sparse import coo_matrix
from scipy.sparse.csgraph import connected_components
kq=np.round(Ps/1e-5).astype(np.int64); _,wi=np.unique(kq,axis=0,return_inverse=True); wi=wi.ravel()
e=np.concatenate([wi[Ts][:,[0,1]],wi[Ts][:,[1,2]]]); ncomp,lab=connected_components(coo_matrix((np.ones(len(e)),(e[:,0],e[:,1])),shape=(wi.max()+1,)*2),directed=False)
lab=lab[wi]; # the leg-mounted shotgun/holster rig (separate small pieces below the knee standing off the leg) cannot be painted onto a
# smooth trouser leg - leave it out of the bake (thigh straps and the main trousers/boots stay)
skip=np.zeros(ncomp,bool)
for c in range(ncomp):
    m_=lab==c
    if m_.sum()<400 and Ps[m_,1].max()<-0.4: skip[c]=True
print('skipped leg pieces',skip.sum())
Ts=Ts[~skip[lab[Ts[:,0]]]]

A,B,C=Ps[Ts[:,0]],Ps[Ts[:,1]],Ps[Ts[:,2]]
fn=np.cross(B-A,C-A); area=np.linalg.norm(fn,axis=1)/2; fn/=np.linalg.norm(fn,axis=1,keepdims=True)+1e-12
NS=3_000_000
cnt=np.maximum(1,np.round(area/area.sum()*NS)).astype(int)
ti=np.repeat(np.arange(len(Ts)),cnt)
r1=np.random.default_rng(1).random(len(ti)); r2=np.random.default_rng(2).random(len(ti))
s1=np.sqrt(r1); bu=1-s1; bv=s1*(1-r2); bw=s1*r2
SP=bu[:,None]*A[ti]+bv[:,None]*B[ti]+bw[:,None]*C[ti]
SUV=bu[:,None]*UVs[Ts[ti,0]]+bv[:,None]*UVs[Ts[ti,1]]+bw[:,None]*UVs[Ts[ti,2]]
SN=fn[ti]
tree=cKDTree(SP); print('samples',len(SP),'%.1fs'%(time.time()-t0))
# ---------- 4. rasterize target UV triangles
H=Wd=RES
pos=np.zeros((H,Wd,3)); nrm=np.zeros((H,Wd,3)); dw=np.zeros((H,Wd,nb),np.float32); mask=np.zeros((H,Wd),bool)
uvp=UV2*[Wd,H]
for t in T2:
    xs,ys=uvp[t,0],uvp[t,1]
    area2=(xs[1]-xs[0])*(ys[2]-ys[0])-(xs[2]-xs[0])*(ys[1]-ys[0])
    if abs(area2)<1e-12: continue
    xa,xb=int(max(0,np.floor(xs.min()-1))),int(min(Wd-1,np.ceil(xs.max()+1))); ya,yb=int(max(0,np.floor(ys.min()-1))),int(min(H-1,np.ceil(ys.max()+1)))
    gx,gy=np.meshgrid(np.arange(xa,xb+1)+0.5,np.arange(ya,yb+1)+0.5)
    w1=((gx-xs[0])*(ys[2]-ys[0])-(xs[2]-xs[0])*(gy-ys[0]))/area2; w2=((xs[1]-xs[0])*(gy-ys[0])-(gx-xs[0])*(ys[1]-ys[0]))/area2; w0=1-w1-w2
    # conservative: allow texels whose centre is within ~0.7 px outside the triangle (edge texels get extrapolated-clamped values)
    eps=0.7/np.sqrt(abs(area2))
    m=(w0>=-eps)&(w1>=-eps)&(w2>=-eps)
    m&=~mask[ya:yb+1,xa:xb+1]
    if not m.any(): continue
    ww=np.clip(np.stack([w0,w1,w2],-1),0,None); ww/=ww.sum(-1,keepdims=True)
    sub=(slice(ya,yb+1),slice(xa,xb+1))
    pos[sub][m]=(ww[...,:,None]*P2[t][None,None]).sum(2)[m]
    nrm[sub][m]=(ww[...,:,None]*N2[t][None,None]).sum(2)[m]
    dw[sub][m]=(ww[...,:,None]*D2[t][None,None]).sum(2)[m]
    mask[sub][m]=True
print('texels',mask.sum(),'%.1fs'%(time.time()-t0))
# ---------- 5. project
pp=pos[mask]; nn=nrm[mask]; nn/=np.linalg.norm(nn,axis=1,keepdims=True)+1e-12; dd=dw[mask]
q=align(pp,dd)
# aligned normal ~ same (part scaling is mild)
K=16; dist,ii=tree.query(q,k=K,workers=-1)
dots=(SN[ii]*nn[:,None,:]).sum(-1)
score=dist+0.06*(1-dots)+np.where(dots<0,1.0,0)
best=ii[np.arange(len(ii)),score.argmin(1)]
uvq=SUV[best]; bad=(dots.max(1)<0)
print('projected; no facing sample within k for %d texels; median dist %.4f p99 %.4f'%(bad.sum(),np.median(dist[:,0]),np.percentile(dist[:,0],99)),'%.1fs'%(time.time()-t0))
# ---------- 5b. head: spherical projection of Max's head (outermost visible surface), landmark-aligned
from headproj import *
hk=names.index('Head'); nk=names.index('Neck')
sd=sr['J'][np.arange(len(Ps)),sr['W'].argmax(1)]
# Max's head = the mesh pieces (connected components) that lie entirely in the head box: face/neck skin, hair, eyes, brows
# (not by skin weights - Max's lower face sits where the rig expects the neck)
keepc=np.zeros(ncomp,bool)
for c in range(ncomp):
    q_=Ps[lab==c]
    if q_.size and q_[:,1].min()>0.46 and q_[:,1].max()>0.585 and q_[:,0].min()>-0.11 and q_[:,0].max()<0.12: keepc[c]=True
keep=keepc[lab[Ts[:,0]]]
print('head pieces',keepc.sum(),'tris',keep.sum())
cs=np.array([0.004,0.605,0.0125])
uvm,okm=sphere_map(Ps,Ts,UVs,cs,keep)
Image.fromarray((okm*255).astype(np.uint8)).save('sphere_ok.png')
# close small holes in the sphere map (gaps between head pieces) from the nearest covered pixel, up to 12 px
dist_,(iy,ix)=ndimage.distance_transform_edt(~okm,return_indices=True)
fillm=(~okm)&(dist_<=12); uvm[fillm]=uvm[iy[fillm],ix[fillm]]; okm=okm|fillm
qh=head_warp(pp)
huv,hok=lookup(qh,cs,uvm,okm)
fm=front_map(Ps,Ts,UVs,keep)
fuv,fok=front_lookup(qh,fm)
fb=np.clip((nn[:,2]-0.35)/0.35,0,1); fb=fb*fb*(3-2*fb)*fok

wh=np.clip((dd[:,hk]+dd[:,nk]-0.3)/0.4,0,1); wh=wh*wh*(3-2*wh); front=(nn[:,2]>0.35)&fok
wh*=(hok|front); fb=np.where(hok,fb,front*1.0)
print('head texels',(wh>0).sum())
src=np.asarray(Image.open('src.png').convert('RGB'),np.float32); sh,sw=src.shape[:2]
def bilinear(img,uv):
    x=uv[:,0]*sw-0.5; y=uv[:,1]*sh-0.5; x0=np.floor(x).astype(int); y0=np.floor(y).astype(int); fx=(x-x0)[:,None]; fy=(y-y0)[:,None]
    def g(yy,xx): return img[np.clip(yy,0,sh-1),np.clip(xx,0,sw-1)]
    return g(y0,x0)*(1-fx)*(1-fy)+g(y0,x0+1)*fx*(1-fy)+g(y0+1,x0)*(1-fx)*fy+g(y0+1,x0+1)*fx*fy
out=np.zeros((H,Wd,3),np.float32); out[mask]=bilinear(src,uvq)*(1-wh[:,None])+(bilinear(src,huv)*(1-fb[:,None])+bilinear(src,fuv)*fb[:,None])*wh[:,None]   # front-facing: orthographic front projection (no parallax); sides/back: spherical
# ---------- 6. dilate (edge padding) 
filled=mask.copy(); o=out.copy()
for it in range(24):
    ring=ndimage.binary_dilation(filled)&~filled
    if not ring.any(): break
    acc=np.zeros_like(o); c=np.zeros((H,Wd),np.float32)
    for dy in (-1,0,1):
        for dx in (-1,0,1):
            if dy==0 and dx==0: continue
            sf=np.roll(np.roll(filled,dy,0),dx,1); so=np.roll(np.roll(o,dy,0),dx,1)
            acc+=so*sf[...,None]; c+=sf
    o[ring]=acc[ring]/np.maximum(c[ring],1)[:,None]; filled|=ring
o[~filled]=out[mask].mean(0)
Image.fromarray(np.clip(o,0,255).astype(np.uint8)).save('baked.png')
# ---------- 7. glb
import io
buf=io.BytesIO(); Image.open('baked.png').save(buf,'PNG')
write('Player_max_new.glb','target.glb',P2,N2,UV2,J2,W2,T2,image_png=buf.getvalue())
print('done %.1fs'%(time.time()-t0))
