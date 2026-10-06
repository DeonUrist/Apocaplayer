import numpy as np
def sphere_map(P,T,UV,center,keep,Wd=2048,Hh=1024):
    """rasterize triangles (keep mask) in spherical coords around center; per pixel keep the outermost surface's UV"""
    d=P-center; r=np.linalg.norm(d,axis=1); th=np.arctan2(d[:,0],d[:,2]); ph=np.arcsin(np.clip(d[:,1]/np.maximum(r,1e-9),-1,1))
    X=(th+np.pi)/(2*np.pi)*Wd; Y=(np.pi/2-ph)/np.pi*Hh
    best=np.full((Hh,Wd),-1.0); uvm=np.zeros((Hh,Wd,2)); 
    for t in T[keep]:
        xs=X[t].copy(); ys=Y[t]
        shifts=[0.0]
        if xs.max()-xs.min()>Wd/2: xs=np.where(xs<Wd/2,xs+Wd,xs); shifts=[0.0,-Wd]
        for sh in shifts:
            x=xs+sh
            area=(x[1]-x[0])*(ys[2]-ys[0])-(x[2]-x[0])*(ys[1]-ys[0])
            if abs(area)<1e-9: continue
            xa,xb=int(max(0,np.floor(x.min()))),int(min(Wd-1,np.ceil(x.max()))); ya,yb=int(max(0,np.floor(ys.min()))),int(min(Hh-1,np.ceil(ys.max())))
            if xa>xb or ya>yb: continue
            gx,gy=np.meshgrid(np.arange(xa,xb+1)+0.5,np.arange(ya,yb+1)+0.5)
            w1=((gx-x[0])*(ys[2]-ys[0])-(x[2]-x[0])*(gy-ys[0]))/area; w2=((x[1]-x[0])*(gy-ys[0])-(gx-x[0])*(ys[1]-ys[0]))/area; w0=1-w1-w2
            m=(w0>=-1e-4)&(w1>=-1e-4)&(w2>=-1e-4)
            rr=w0*r[t[0]]+w1*r[t[1]]+w2*r[t[2]]
            sub=best[ya:yb+1,xa:xb+1]; m&=rr>sub
            if not m.any(): continue
            sub[m]=rr[m]
            u=w0*UV[t[0],0]+w1*UV[t[1],0]+w2*UV[t[2],0]; v=w0*UV[t[0],1]+w1*UV[t[1],1]+w2*UV[t[2],1]
            uvm[ya:yb+1,xa:xb+1][m]=np.stack([u,v],-1)[m]
    return uvm,best>0
def lookup(q,center,uvm,ok):
    Hh,Wd=ok.shape; d=q-center; r=np.linalg.norm(d,axis=1); th=np.arctan2(d[:,0],d[:,2]); ph=np.arcsin(np.clip(d[:,1]/np.maximum(r,1e-9),-1,1))
    X=np.clip(((th+np.pi)/(2*np.pi)*Wd).astype(int),0,Wd-1); Y=np.clip(((np.pi/2-ph)/np.pi*Hh).astype(int),0,Hh-1)
    return uvm[Y,X],ok[Y,X]
def front_map(P,T,UV,keep,box=(-0.12,0.13,0.44,0.76),Wd=1024,Hh=1300):
    x0,x1,y0,y1=box; X=(P[:,0]-x0)/(x1-x0)*Wd; Y=(y1-P[:,1])/(y1-y0)*Hh; Z=P[:,2]
    best=np.full((Hh,Wd),-9.0); uvm=np.zeros((Hh,Wd,2))
    for t in T[keep]:
        x=X[t]; ys=Y[t]
        area=(x[1]-x[0])*(ys[2]-ys[0])-(x[2]-x[0])*(ys[1]-ys[0])
        if abs(area)<1e-9: continue
        xa,xb=int(max(0,np.floor(x.min()))),int(min(Wd-1,np.ceil(x.max()))); ya,yb=int(max(0,np.floor(ys.min()))),int(min(Hh-1,np.ceil(ys.max())))
        if xa>xb or ya>yb: continue
        gx,gy=np.meshgrid(np.arange(xa,xb+1)+0.5,np.arange(ya,yb+1)+0.5)
        w1=((gx-x[0])*(ys[2]-ys[0])-(x[2]-x[0])*(gy-ys[0]))/area; w2=((x[1]-x[0])*(gy-ys[0])-(gx-x[0])*(ys[1]-ys[0]))/area; w0=1-w1-w2
        m=(w0>=-1e-4)&(w1>=-1e-4)&(w2>=-1e-4)
        zz=w0*Z[t[0]]+w1*Z[t[1]]+w2*Z[t[2]]
        sub=best[ya:yb+1,xa:xb+1]; m&=zz>sub
        if not m.any(): continue
        sub[m]=zz[m]; u=w0*UV[t[0],0]+w1*UV[t[1],0]+w2*UV[t[2],0]; v=w0*UV[t[0],1]+w1*UV[t[1],1]+w2*UV[t[2],1]
        uvm[ya:yb+1,xa:xb+1][m]=np.stack([u,v],-1)[m]
    return uvm,best>-9,box
def front_lookup(q,fm):
    uvm,ok,(x0,x1,y0,y1)=fm; Hh,Wd=ok.shape
    X=((q[:,0]-x0)/(x1-x0)*Wd).astype(int); Y=((y1-q[:,1])/(y1-y0)*Hh).astype(int)
    inb=(X>=0)&(X<Wd)&(Y>=0)&(Y<Hh); X=np.clip(X,0,Wd-1); Y=np.clip(Y,0,Hh-1)
    return uvm[Y,X],ok[Y,X]&inb
# landmark warp of the game man's head onto Max's (target -> source space)
LM_T=[0.50,0.556,0.585,0.638,0.665,0.708,0.755,0.84]   # neck base, neck, chin, mouth, nose bottom, eyes, hairline, top
LM_S=[0.43,0.485,0.512,0.537,0.557,0.600,0.636,0.712]
def head_warp(p):
    return np.stack([0.002+0.79*p[:,0], np.interp(p[:,1],LM_T,LM_S,left=None,right=None), 0.101+(p[:,2]-0.142)*0.78],-1)
