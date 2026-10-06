import sys; from glb import *; from render import *
m=G(sys.argv[1]).mesh(); tex=Image.open(sys.argv[2]).convert('RGB')
s=G('src.glb').mesh(); st=Image.open('src.png').convert('RGB')
imgs=[]
for yaw in (0,180,90):
    imgs.append(render(m['P'],m['T'],m['UV'],tex,yaw)); imgs.append(render(s['P'],s['T'],s['UV'],st,yaw))
sheet(imgs,sys.argv[3])
# head closeups
box=(-0.15,0.15,0.5,0.85)
h=[render(m['P'],m['T'],m['UV'],tex,yaw,W=360,H=420,box=box) for yaw in (0,45,90)]+[render(s['P'],s['T'],s['UV'],st,yaw,W=360,H=420,box=(-0.15,0.15,0.45,0.8)) for yaw in (0,45,90)]
sheet(h,sys.argv[3].replace('.png','_head.png'))
