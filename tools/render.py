import numpy as np
from PIL import Image, ImageDraw
def render(P,T,UV,tex,view='front',size=600,out='r.png',extra=None,center=None,scale=None):
    # P in some space with y up; view front looks along -z (camera at +z looking toward -z)? choose
    tex=np.asarray(tex.convert('RGB')); th,tw=tex.shape[:2]
    if view=='front': X,Y,Z=P[:,0],P[:,1],P[:,2]
    elif view=='back': X,Y,Z=-P[:,0],P[:,1],-P[:,2]
    elif view=='side': X,Y,Z=P[:,2],P[:,1],-P[:,0]
    elif view=='side2': X,Y,Z=-P[:,2],P[:,1],P[:,0]
    elif view=='top': X,Y,Z=P[:,0],-P[:,2],P[:,1]
    lo=np.array([X.min(),Y.min()]); hi=np.array([X.max(),Y.max()])
    if center is None: center=(lo+hi)/2
    if scale is None: scale=(size*0.9)/max(hi-lo)
    sx=(X-center[0])*scale+size/2; sy=size/2-(Y-center[1])*scale
    img=np.full((size,size,3),40,np.uint8); zb=np.full((size,size),-1e9)
    for t in T:
        a,b,c=t
        xs=np.array([sx[a],sx[b],sx[c]]); ys=np.array([sy[a],sy[b],sy[c]]); zs=np.array([Z[a],Z[b],Z[c]])
        x0,x1=int(max(0,np.floor(xs.min()))),int(min(size-1,np.ceil(xs.max())))
        y0,y1=int(max(0,np.floor(ys.min()))),int(min(size-1,np.ceil(ys.max())))
        if x1<x0 or y1<y0: continue
        den=(ys[1]-ys[2])*(xs[0]-xs[2])+(xs[2]-xs[1])*(ys[0]-ys[2])
        if abs(den)<1e-9: continue
        gx,gy=np.meshgrid(np.arange(x0,x1+1)+0.5,np.arange(y0,y1+1)+0.5)
        l0=((ys[1]-ys[2])*(gx-xs[2])+(xs[2]-xs[1])*(gy-ys[2]))/den
        l1=((ys[2]-ys[0])*(gx-xs[2])+(xs[0]-xs[2])*(gy-ys[2]))/den
        l2=1-l0-l1
        m=(l0>=0)&(l1>=0)&(l2>=0)
        if not m.any(): continue
        z=l0*zs[0]+l1*zs[1]+l2*zs[2]
        u=l0*UV[a,0]+l1*UV[b,0]+l2*UV[c,0]; v=l0*UV[a,1]+l1*UV[b,1]+l2*UV[c,1]
        yy,xx=np.nonzero(m); zz=z[m]
        sub=zb[y0+yy,x0+xx]; ok=zz>sub
        yy,xx,zz=yy[ok],xx[ok],zz[ok]
        zb[y0+yy,x0+xx]=zz
        tu=(np.clip(u[m][ok],0,1)*(tw-1)).astype(int); tv=(np.clip(v[m][ok],0,1)*(th-1)).astype(int)
        img[y0+yy,x0+xx]=tex[tv,tu]
    im=Image.fromarray(img)
    if extra:
        d=ImageDraw.Draw(im)
        for (x,y,z),col in extra:
            if view=='front': X2,Y2=x,y
            elif view=='side': X2,Y2=z,y
            elif view=='back': X2,Y2=-x,y
            else: X2,Y2=x,-z
            px=(X2-center[0])*scale+size/2; py=size/2-(Y2-center[1])*scale
            d.ellipse([px-3,py-3,px+3,py+3],fill=col)
    im.save(out); return center,scale
