import numpy as np,sys
from PIL import Image, ImageFilter, ImageDraw
from showcase import *
M='/mnt/user-data/uploads/common--Apocalypter/BepInEx/plugins/Apocaplayer/Models/'
# world-space rotations about each joint (model: +x = character's left, +y up, +z front)
FEM={'Hips':ry(-12)@rz(4),'Spine':rz(-3),'Spine1':rz(-3),'Spine2':ry(8),'Neck':rz(2),'Head':ry(14)@rz(4)@rx(-4),
     'LeftArm':('aim',(0.75,-0.62,-0.22)),'LeftForeArm':('aim',(-0.72,-0.55,0.30)),          # hand on hip
     'RightArm':('aim',(-0.22,-1,0.02)),'RightForeArm':('aim',(-0.12,-1,0.22)),
     'LeftUpLeg':('aim',(0.14,-1,0.10)),'LeftLeg':('aim',(0.02,-1,-0.06)),
     'RightUpLeg':('aim',(0.02,-1,0.0)),'RightLeg':('aim',(-0.02,-1,-0.02))}
MAX={'Hips':ry(8),'Spine':rx(5),'Spine1':rx(4),'Spine2':ry(-8)@rx(3),'Neck':rx(-2),'Head':ry(-14)@rx(4),
     'LeftArm':('aim',(0.42,-1,0.0)),'LeftForeArm':('aim',(0.22,-1,0.38)),
     'RightArm':('aim',(-0.42,-1,-0.04)),'RightForeArm':('aim',(-0.26,-1,0.2)),
     'LeftUpLeg':('aim',(0.17,-1,0.03)),'LeftLeg':('aim',(0.08,-1,-0.03)),'LeftFoot':ry(-14),
     'RightUpLeg':('aim',(-0.17,-1,-0.03)),'RightLeg':('aim',(-0.08,-1,-0.05)),'RightFoot':ry(14)}
LIGHTS=[((-0.6,0.7,0.9),(1.15,0.86,0.58),'d'),((0.9,0.1,0.5),(0.22,0.26,0.34),'d'),((0.9,0.5,-1.0),(0.95,0.62,0.35),'rim'),((-0.9,0.4,-1.0),(0.45,0.5,0.65),'rim')]
def bg(W,H):
    y=np.linspace(0,1,H)[:,None,None]; x=np.linspace(-1,1,W)[None,:,None]
    top=np.array([0.10,0.07,0.05]); mid=np.array([0.36,0.20,0.09]); low=np.array([0.06,0.045,0.035])
    g=np.where(y<0.62, top+(mid-top)*(y/0.62)**1.6, mid+(low-mid)*((y-0.62)/0.38)**0.7)
    v=1-0.55*np.clip(x**2*0.7+(y-0.55)**2,0,1); return g*v
def shot(glb,png,pose,yaw,W,H,S=2,fill=0.86,pitch=-4,layers=False):
    V,N,UV,T=posed(M+glb,pose); tex=Image.open(M+png).convert('RGB')
    lo,hi=V.min(0),V.max(0); c=(lo+hi)/2; h=hi[1]-lo[1]
    dist=6.0; focal=fill*H*S*dist/h
    proj,vd=camera(yaw,pitch,c,dist,focal,W*S,H*S)
    alb,nor,m,_=raster(V,N,UV,T,tex,W*S,H*S,proj)
    img=light(alb,nor,vd,LIGHTS,(0.26,0.23,0.21))
    # ground shadow under the feet
    sx,sy,_=proj(V); fy=sy.max(); fx=(sx[V[:,1]<lo[1]+0.08]).mean()
    sh=Image.new('L',(W*S,H*S),0); d=ImageDraw.Draw(sh); rw=0.34*h*focal/dist
    d.ellipse([fx-rw,fy-rw*0.16,fx+rw,fy+rw*0.10],fill=200); sh=np.asarray(sh.filter(ImageFilter.GaussianBlur(18*S)),np.float32)[...,None]/255
    B=bg(W*S,H*S)*(1-0.75*sh)
    if layers: return img,m,sh
    out=np.where(m[...,None],img,B)
    im=Image.fromarray((np.clip(out,0,1)*255).astype(np.uint8)).resize((W,H),Image.LANCZOS)
    return im
def banner(W=1920,H=1080,S=2):
    B=bg(W*S,H*S); out=B.copy()
    for glb,png,pose,yaw,x0 in [('Player_female.glb','Player_female.png',FEM,22,0.14),('Player_Max.glb','Player_max.png',MAX,-20,0.50)]:
        w=int(W*0.36); img,m,sh=shot(glb,png,pose,yaw,w,H,S,fill=0.84,layers=True)
        X=int(x0*W*S); reg=out[:,X:X+w*S]
        reg*=(1-0.75*sh); reg[m]=img[m]
    Image.fromarray((np.clip(out,0,1)*255).astype(np.uint8)).resize((W,H),Image.LANCZOS).save('pose_banner.png')
if __name__=='__main__':
    banner()
    f=shot('Player_female.glb','Player_female.png',FEM,22,1200,1600); f.save('pose_female.png')
    x=shot('Player_Max.glb','Player_max.png',MAX,-20,1200,1600); x.save('pose_max.png')
    s=Image.new('RGB',(2400,1600)); s.paste(f,(0,0)); s.paste(x,(1200,0)); s.resize((1200,800)).save('/tmp/claude-0/pose_prev.png')
