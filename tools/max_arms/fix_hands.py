# Max's first-person gloves: the projected bake (bake_max2.py) smears Max's low-poly mitten onto the game's fist (fingers, knuckles),
# leaving grey patches and bright specks. Replace every hand texel with the game's own glove shading re-coloured to Max's glove colour,
# then re-pad the islands. Run after bake_max2.py, from the same folder.
import json,numpy as np,glb
from PIL import Image
from matplotlib.path import Path
W=2048
def masks(meshes):
    hand=np.zeros((W,W),bool); other=np.zeros((W,W),bool)
    for mesh in meshes:
        d=json.load(open('_export/%s.json'%mesh)); UV=np.array(d['uv']).reshape(-1,2); T=np.array(d['tris']).reshape(-1,3)
        bw=np.array(d['bw']); bi=np.array(d['bi']); bn=[b.split('/')[-1] for b in d['bones']]
        dom=bi[np.arange(len(bi)),bw.argmax(1)]
        for t in T:
            segs=[bn[dom[i]][:4] for i in t]; seg=max(set(segs),key=segs.count)
            p=np.array([(UV[i,0]*W,(1-UV[i,1])*W) for i in t])
            x0,y0=np.maximum(np.floor(p.min(0)).astype(int)-1,0); x1,y1=np.minimum(np.ceil(p.max(0)).astype(int)+1,W-1)
            gx,gy=np.meshgrid(np.arange(x0,x1+1)+.5,np.arange(y0,y1+1)+.5)
            m=Path(p).contains_points(np.c_[gx.ravel(),gy.ravel()],radius=1.0).reshape(gx.shape)
            yy,xx=np.nonzero(m); yy+=y0; xx+=x0
            (hand if seg=='hand' else other)[yy,xx]=True
    return hand,other
def glove_colour(side):
    f=glb.load('Player_max.glb'); tex=np.asarray(Image.open('Player_max.png').convert('RGB')).astype(float); H,Wt=tex.shape[:2]
    fj={n.split(':')[1]:i for i,n in enumerate(f['jnames'])}
    dom=f['J'][np.arange(len(f['J'])),f['Wt'].argmax(1)]
    uv=f['UV'][dom==fj[side+'Hand']]
    c=tex[(uv[:,1]*(H-1)).astype(int).clip(0,H-1),(uv[:,0]*(Wt-1)).astype(int).clip(0,Wt-1)]
    c=c[(c.mean(1)>15)&(c.mean(1)<90)]           # drop specks/black seams
    return np.median(c,0)
org=np.asarray(Image.open('_export/Player2.png').convert('RGB')).astype(float)
for png,mesh,side,leg in [('Player2_max_arms.png','player_arm_left_002','Right',True),('Player2_max_arms_left.png','player_arm_left','Left',False)]:
    new=np.asarray(Image.open(png).convert('RGB')).astype(float).copy()
    hand,other=masks([mesh]+(['Player_leg'] if leg else []))
    h=hand&~other
    gc=glove_colour(side)
    lum=org[h].mean(-1); lm=np.median(lum)
    k=np.clip(0.55+0.45*lum/lm,0.6,1.5)[:,None]     # keep the game's folds/seams, softened
    new[h]=np.clip(gc*1.15*k,0,255)
    if side=='Left':   # sleeve side: stray skin-coloured texels from the wrist projection -> glove colour
        r,g,b=new[...,0],new[...,1],new[...,2]; sk=other&(r-b>35)&(r>90)
        new[sk]=np.clip(gc*1.15,0,255); print('left sleeve skin texels',sk.sum())
    used=hand|other; filled=used.copy()
    # re-pad: unused texels next to islands take the island colour (mipmaps)
    work=new.copy(); work[~used]=0
    for _ in range(12):
        acc=np.zeros_like(work); cnt=np.zeros((W,W))
        for dy,dx in ((1,0),(-1,0),(0,1),(0,-1),(1,1),(1,-1),(-1,1),(-1,-1)):
            sh=np.roll(np.roll(filled,dy,0),dx,1); sv=np.roll(np.roll(work,dy,0),dx,1)
            acc[sh]+=sv[sh]; cnt[sh]+=1
        grow=(~filled)&(cnt>0); work[grow]=acc[grow]/cnt[grow][:,None]; filled|=grow
    pad=filled&~used; new[pad]=work[pad]
    Image.fromarray(new.clip(0,255).astype(np.uint8)).save(png)
    print(png,'glove',gc.round(),'hand texels',h.sum())
