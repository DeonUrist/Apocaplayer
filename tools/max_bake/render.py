import numpy as np
from PIL import Image
def look(V,yaw):
    a=np.radians(yaw); R=np.array([[np.cos(a),0,np.sin(a)],[0,1,0],[-np.sin(a),0,np.cos(a)]]); return V@R.T
def render(P,T,UV,tex,yaw=0,W=420,H=760,box=(-0.55,0.55,-1.0,0.9),N=None,cull=True):
    """orthographic textured render, camera looking down -z after yaw (glTF: front is +z)"""
    V=look(P,yaw); x0,x1,y0,y1=box
    sx=(V[:,0]-x0)/(x1-x0)*W; sy=(y1-V[:,1])/(y1-y0)*H; z=V[:,2]
    img=np.full((H,W,3),255,np.float32); zb=np.full((H,W),-1e9)
    tex=np.asarray(tex,np.float32)/255.; th,tw=tex.shape[:2]
    for t in T:
        xs,ys=sx[t],sy[t]
        area=(xs[1]-xs[0])*(ys[2]-ys[0])-(xs[2]-xs[0])*(ys[1]-ys[0])
        if abs(area)<1e-9: continue
        if cull and area>0: continue   # back face (screen y down flips sign)
        xa,xb=int(max(0,np.floor(xs.min()))),int(min(W-1,np.ceil(xs.max()))); ya,yb=int(max(0,np.floor(ys.min()))),int(min(H-1,np.ceil(ys.max())))
        if xa>xb or ya>yb: continue
        gx,gy=np.meshgrid(np.arange(xa,xb+1)+0.5,np.arange(ya,yb+1)+0.5)
        w1=((gx-xs[0])*(ys[2]-ys[0])-(xs[2]-xs[0])*(gy-ys[0]))/area
        w2=((xs[1]-xs[0])*(gy-ys[0])-(gx-xs[0])*(ys[1]-ys[0]))/area
        w0=1-w1-w2; m=(w0>=-1e-6)&(w1>=-1e-6)&(w2>=-1e-6)
        if not m.any(): continue
        zz=w0*z[t[0]]+w1*z[t[1]]+w2*z[t[2]]
        sub=zb[ya:yb+1,xa:xb+1]; m&=zz>sub
        if not m.any(): continue
        u=w0*UV[t[0],0]+w1*UV[t[1],0]+w2*UV[t[2],0]; v=w0*UV[t[0],1]+w1*UV[t[1],1]+w2*UV[t[2],1]
        col=tex[np.clip((v*th).astype(int),0,th-1),np.clip((u*tw).astype(int)%tw,0,tw-1),:3]
        # simple shading from face normal
        fn=np.cross(V[t[1]]-V[t[0]],V[t[2]]-V[t[0]]); fn/=np.linalg.norm(fn)+1e-12
        sh=0.55+0.45*abs(fn[2])
        sub[m]=zz[m]; img[ya:yb+1,xa:xb+1][m]=col[m]*sh
    return Image.fromarray((np.clip(img,0,1)*255).astype(np.uint8))
def sheet(imgs,path):
    W=sum(i.width for i in imgs); H=max(i.height for i in imgs); s=Image.new('RGB',(W,H),'white'); x=0
    for i in imgs: s.paste(i,(x,0)); x+=i.width
    s.save(path)
