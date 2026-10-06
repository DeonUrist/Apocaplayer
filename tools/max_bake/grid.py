import sys; from glb import *; from render import *
from PIL import ImageDraw
def grid(img,box,step=0.02):
    x0,x1,y0,y1=box; d=ImageDraw.Draw(img); W,H=img.size
    for k in np.arange(np.ceil(y0/step)*step,y1,step):
        y=(y1-k)/(y1-y0)*H; d.line([(0,y),(W,y)],fill=(0,160,255) if abs(k*100%10)<1e-6 else (190,230,255)); d.text((2,y-10),'%.2f'%k,fill=(0,0,200))
    for k in np.arange(np.ceil(x0/step)*step,x1,step):
        x=(k-x0)/(x1-x0)*W; d.line([(x,0),(x,H)],fill=(190,230,255)); d.text((x+1,H-12),'%.2f'%k,fill=(200,0,0))
    return img
tm=G('target.glb').mesh(); man=Image.open('man.png').convert('RGB')
mm=G('man.glb').mesh()
s=G('src.glb').mesh(); st=Image.open('src.png').convert('RGB')
bt=(-0.15,0.15,0.52,0.86); bs=(-0.15,0.15,0.46,0.80)
imgs=[grid(render(mm['P'],mm['T'],mm['UV'],man,0,W=450,H=510,box=bt),bt),grid(render(mm['P'],mm['T'],mm['UV'],man,-90,W=450,H=510,box=bt),bt),
      grid(render(s['P'],s['T'],s['UV'],st,0,W=450,H=510,box=bs),bs),grid(render(s['P'],s['T'],s['UV'],st,-90,W=450,H=510,box=bs),bs)]
sheet(imgs,'heads_grid.png')
# also target vs man geometry at head: same?
print('target head verts vs man: max dist', max(np.min(np.linalg.norm(mm['P']-p,axis=1)) for p in tm['P'][tm['P'][:,1]>0.6]))
