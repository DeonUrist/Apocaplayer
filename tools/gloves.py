# Post-pass for bake.py: (1) the game's glove stays glove (her glove ends lower on the wrist than the game's cuff, and the hand mapping
# can land on her pale wrist skin -> skin-coloured cuff / a light line across the hand), (2) islands padded so mipmaps don't pull in
# the neighbouring atlas parts. Glove texels are re-coloured with her glove colour, keeping the game texture's shading (folds, seams).
import json,numpy as np
from PIL import Image
from matplotlib.path import Path
def skin(c):
    r,g,b=c[...,0],c[...,1],c[...,2]; return (r>g-5)&(r-b>30)&(r>100)
def dark(c): return c.max(-1)<75
def fix(baked, orig_png, meshes, out_png, mask_png=None, pad=12):
    W=2048
    org=np.asarray(Image.open(orig_png).convert('RGB')).astype(float)
    new=np.asarray(Image.open(baked).convert('RGB')).astype(float).copy()
    hand=np.zeros((W,W),bool); wrist=np.zeros((W,W),bool); used=np.zeros((W,W),bool)
    for mesh in meshes:
        d=json.load(open('_export/%s.json'%mesh)); UV=np.array(d['uv']).reshape(-1,2); T=np.array(d['tris']).reshape(-1,3)
        bw=np.array(d['bw']); bi=np.array(d['bi']); bn=[b.split('/')[-1] for b in d['bones']]
        dom=bi[np.arange(len(bi)),bw.argmax(1)]
        for t in T:
            segs=[bn[dom[i]][:4] for i in t]; seg=max(set(segs),key=segs.count)
            p=np.array([(UV[i,0]*W,(1-UV[i,1])*W) for i in t])
            x0,y0=np.maximum(np.floor(p.min(0)).astype(int)-1,0); x1,y1=np.minimum(np.ceil(p.max(0)).astype(int)+1,W-1)
            gx,gy=np.meshgrid(np.arange(x0,x1+1)+.5,np.arange(y0,y1+1)+.5)
            m=Path(p).contains_points(np.c_[gx.ravel(),gy.ravel()],radius=1.5).reshape(gx.shape)
            yy,xx=np.nonzero(m); yy+=y0; xx+=x0
            used[yy,xx]=True
            if seg=='hand': hand[yy,xx]=True
            elif seg=='arm3': wrist[yy,xx]=True
    gl=hand&dark(new)
    gc=np.median(new[gl],0) if gl.any() else np.array([28.,26.,26.])
    og=(~skin(org))&(hand|wrist)
    lm=np.median(org[og].mean(-1)) if og.any() else 100.
    force=(hand&skin(new)) | (wrist&og&~dark(new))
    k=np.clip(org[force].mean(-1)/lm,0.55,1.7)[:,None]
    new[force]=np.clip(gc*k,0,255)
    print('glove colour',gc.round(),'texels re-coloured',force.sum(),'(hand',(hand&skin(new)).sum(),')')
    # padding: grow every island outward (only texels no arm/leg triangle uses; this texture is only on the arm/leg renderers)
    filled=used.copy()
    for _ in range(pad):
        acc=np.zeros_like(new); cnt=np.zeros((W,W))
        for dy,dx in ((1,0),(-1,0),(0,1),(0,-1),(1,1),(1,-1),(-1,1),(-1,-1)):
            sh=np.roll(np.roll(filled,dy,0),dx,1); sv=np.roll(np.roll(new,dy,0),dx,1)
            acc[sh]+=sv[sh]; cnt[sh]+=1
        grow=(~filled)&(cnt>0)
        new[grow]=acc[grow]/cnt[grow][:,None]; filled|=grow
    Image.fromarray(new.clip(0,255).astype(np.uint8)).save(out_png)
if __name__=='__main__':
    fix('Player2_female.png','_export/Player2.png',['player_arm_left','player_arm_left_002','Player_leg'],'Player2_female_fixed.png')
