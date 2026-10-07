# Character renders for Apocaplayer: TAB/UI picture (front, A-pose, black bg, 512x1024) and posed presentation shots.
import sys,numpy as np
from PIL import Image, ImageFilter
from glb import G
def q2m(q):
    x,y,z,w=q; return np.array([[1-2*(y*y+z*z),2*(x*y-z*w),2*(x*z+y*w)],[2*(x*y+z*w),1-2*(x*x+z*z),2*(y*z-x*w)],[2*(x*z-y*w),2*(y*z+x*w),1-2*(x*x+y*y)]])
def rz(d):
    a=np.radians(d); return np.array([[np.cos(a),-np.sin(a),0],[np.sin(a),np.cos(a),0],[0,0,1]])
def rx(d):
    a=np.radians(d); return np.array([[1,0,0],[0,np.cos(a),-np.sin(a)],[0,np.sin(a),np.cos(a)]])
def ry(d):
    a=np.radians(d); return np.array([[np.cos(a),0,np.sin(a)],[0,1,0],[-np.sin(a),0,np.cos(a)]])

def posed(path,POSE,root=None):
    """POSE: joint name -> world-space rotation about the (already posed) joint; applied parent first."""
    g=G(path); j=g.j; m=g.mesh(); sk=j['skins'][0]
    par={}
    for i,n in enumerate(j['nodes']):
        for c in n.get('children',[]): par[c]=i
    rest={}
    def R(i):
        if i in rest: return rest[i]
        n=j['nodes'][i]; M=np.eye(4); M[:3,:3]=q2m(n.get('rotation',[0,0,0,1]))*np.array(n.get('scale',[1,1,1])); M[:3,3]=n.get('translation',[0,0,0])
        rest[i]=(R(par[i])@M) if i in par else M; return rest[i]
    pos={}
    def Pz(i):
        if i in pos: return pos[i]
        M=(Pz(par[i])@np.linalg.inv(R(par[i]))@R(i)) if i in par else R(i).copy()
        nm=j['nodes'][i]['name'].split(':')[-1]
        if nm in POSE:
            p=M[:3,3].copy(); E=np.eye(4); Rq=POSE[nm]
            if isinstance(Rq,tuple):        # ('aim', world direction[, twist matrix]): rotate so the bone (joint->first child) points there
                ch=j['nodes'][i]['children'][0]; c=(M@np.linalg.inv(R(i))@R(ch))[:3,3]-p
                a=c/np.linalg.norm(c); b=np.array(Rq[1],float); b/=np.linalg.norm(b)
                v=np.cross(a,b); cs=a@b; K=np.array([[0,-v[2],v[1]],[v[2],0,-v[0]],[-v[1],v[0],0]])
                Rq=np.eye(3)+K+K@K/(1+cs)
            E[:3,:3]=Rq; T1=np.eye(4); T1[:3,3]=p; T2=np.eye(4); T2[:3,3]=-p
            M=T1@E@T2@M
        pos[i]=M; return M
    S=np.array([Pz(ji)@m['ibm'][k] for k,ji in enumerate(sk['joints'])])
    Ph=np.c_[m['P'],np.ones(len(m['P']))]; V=np.zeros((len(Ph),3)); N=np.zeros((len(Ph),3))
    for c in range(4):
        Sm=S[m['J'][:,c]]; V+=m['W'][:,c,None]*np.einsum('nij,nj->ni',Sm,Ph)[:,:3]; N+=m['W'][:,c,None]*np.einsum('nij,nj->ni',Sm[:,:3,:3],m['N'])
    N/=np.linalg.norm(N,axis=1,keepdims=True)+1e-12
    return V,N,m['UV'],m['T']

def raster(V,N,UV,T,tex,W,H,proj):
    """proj(V)->(sx,sy,depth larger=closer). returns albedo, normal, mask, depth"""
    sx,sy,z=proj(V)
    alb=np.zeros((H,W,3),np.float32); nor=np.zeros((H,W,3),np.float32); zb=np.full((H,W),-1e9)
    tx=np.asarray(tex,np.float32)/255.; th,tw=tx.shape[:2]
    for t in T:
        xs,ys=sx[t],sy[t]; area=(xs[1]-xs[0])*(ys[2]-ys[0])-(xs[2]-xs[0])*(ys[1]-ys[0])
        if abs(area)<1e-9: continue
        xa,xb=int(max(0,np.floor(xs.min()))),int(min(W-1,np.ceil(xs.max()))); ya,yb=int(max(0,np.floor(ys.min()))),int(min(H-1,np.ceil(ys.max())))
        if xa>xb or ya>yb: continue
        gx,gy=np.meshgrid(np.arange(xa,xb+1)+0.5,np.arange(ya,yb+1)+0.5)
        w1=((gx-xs[0])*(ys[2]-ys[0])-(xs[2]-xs[0])*(gy-ys[0]))/area; w2=((xs[1]-xs[0])*(gy-ys[0])-(gx-xs[0])*(ys[1]-ys[0]))/area; w0=1-w1-w2
        m=(w0>=0)&(w1>=0)&(w2>=0)
        if not m.any(): continue
        zz=w0*z[t[0]]+w1*z[t[1]]+w2*z[t[2]]; sub=zb[ya:yb+1,xa:xb+1]; m&=zz>sub
        if not m.any(): continue
        u=w0*UV[t[0],0]+w1*UV[t[1],0]+w2*UV[t[2],0]; v=w0*UV[t[0],1]+w1*UV[t[1],1]+w2*UV[t[2],1]
        col=tx[np.clip((v*th).astype(int),0,th-1),np.clip((u*tw).astype(int)%tw,0,tw-1),:3]
        n=w0[...,None]*N[t[0]]+w1[...,None]*N[t[1]]+w2[...,None]*N[t[2]]; n/=np.linalg.norm(n,axis=-1,keepdims=True)+1e-9
        sub[m]=zz[m]; alb[ya:yb+1,xa:xb+1][m]=col[m]; nor[ya:yb+1,xa:xb+1][m]=n[m]
    return alb,nor,zb>-1e9,zb

def light(alb,nor,view_dir,lights,amb):
    out=alb*np.array(amb,np.float32)
    nv=(nor*view_dir).sum(-1,keepdims=True); nor=np.where(nv<0,-nor,nor)   # double-sided
    for d,c,kind in lights:
        d=np.array(d,np.float32); d/=np.linalg.norm(d)
        ndl=np.clip((nor*d).sum(-1,keepdims=True),0,None)
        if kind=='rim':
            f=(1-np.clip((nor*view_dir).sum(-1,keepdims=True),0,1))**2.5; out+=np.array(c,np.float32)*ndl*f
        else:
            out+=alb*np.array(c,np.float32)*ndl
    return out

def camera(yaw,pitch,center,dist,focal,W,H):
    Rm=rx(pitch)@ry(-yaw)
    def proj(V):
        P=(V-center)@Rm.T; zc=dist-P[:,2]          # camera on +z looking -z
        sx=W/2+focal*P[:,0]/zc; sy=H/2-focal*P[:,1]/zc; return sx,sy,-zc
    vd=Rm.T@np.array([0,0,1.])                        # towards camera, world space
    return proj,vd
def ortho(box,W,H):
    x0,x1,y0,y1=box
    def proj(V): return (V[:,0]-x0)/(x1-x0)*W,(y1-V[:,1])/(y1-y0)*H,V[:,2]
    return proj,np.array([0,0,1.])
