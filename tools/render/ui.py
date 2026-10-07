import numpy as np
from PIL import Image
from showcase import *
M='/mnt/user-data/uploads/common--Apocalypter/BepInEx/plugins/Apocaplayer/Models/'
APOSE={'LeftArm':rz(-71),'RightArm':rz(71),'LeftForeArm':rz(-8)@ry(-10),'RightForeArm':rz(8)@ry(10),
      'LeftUpLeg':rz(-5),'RightUpLeg':rz(5),'LeftLeg':rz(2),'RightLeg':rz(-2),'LeftFoot':rz(3),'RightFoot':rz(-3),'LeftHand':rz(-4),'RightHand':rz(4),'Head':rx(-3)}
LIGHTS=[((-0.5,0.6,1.0),(1.05,0.80,0.55),'d'),((0.8,0.2,0.6),(0.30,0.28,0.30),'d'),((0.3,0.5,-1.0),(0.55,0.42,0.30),'rim')]
def ui(glb,png,out,S=2):
    V,N,UV,T=posed(M+glb,APOSE); tex=Image.open(M+png).convert('RGB')
    W,H=512*S,1024*S
    lo,hi=V.min(0),V.max(0); h=hi[1]-lo[1]; top=37/1024; bot=991/1024
    sc=(bot-top)*H/h; cx=(lo[0]+hi[0])/2
    box=(cx-W/2/sc,cx+W/2/sc,lo[1]-(1-bot)*H/sc,hi[1]+top*H/sc)
    proj,vd=ortho(box,W,H)
    alb,nor,m,_=raster(V,N,UV,T,tex,W,H,proj)
    img=light(alb,nor,vd,LIGHTS,(0.32,0.28,0.25)); img[~m]=0
    im=Image.fromarray((np.clip(img,0,1)*255).astype(np.uint8)).resize((512,1024),Image.LANCZOS).convert('RGBA')
    im.save(out); return im
a=ui('Player_female.glb','Player_female.png','ui_female_new.png')
b=ui('Player_Max.glb','Player_max.png','ui_max_new.png')
old_f=Image.open(M+'player_character_2_UI.png').convert('RGB'); old_m=Image.open(M+'player_character_2_UI_max.png').convert('RGB')
s=Image.new('RGB',(2048,1024));[s.paste(x.convert('RGB'),(i*512,0)) for i,x in enumerate([old_f,a,old_m,b])];s.save('/tmp/claude-0/ui_cmp3.png')
