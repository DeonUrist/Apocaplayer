# Max's first-person arms: bakes his left/right arm (and the kick leg) from Models/Player_max.glb/.png onto the game's Player2 atlas islands.
# Same method as tools/bake.py (female); run from a folder with _export/ (player_arm_left*.json, Player_leg.json, Player2.png from ModDev/Models/_export), glb.py, gloves.py, Player_max.glb/.png.
import json,numpy as np,glb
from PIL import Image, ImageFilter
R=np.array([[1,0,0],[0,0,1],[0,-1,0]],float)   # P2 mesh space -> female Unity space
fem=glb.load('Player_max.glb')
FP=fem['P'].copy(); FP[:,0]*=-1                  # Unity space
FT=fem['T']; FUV=fem['UV']; FJ=fem['J']; FW=fem['Wt']
fj={n.split(':')[1]:i for i,n in enumerate(fem['jnames'])}
jpos={n.split(':')[1]:(lambda p:np.array([-p[0],p[1],p[2]]))(w[:3,3]) for n,w in zip(fem['jnames'],fem['jworld'])}
dom=FJ[np.arange(len(FJ)),FW.argmax(1)]
ftex=np.asarray(Image.open('Player_max.png').convert('RGB')).astype(float)
def fem_tris(jointnames):
    ids={fj[n] for n in jointnames}
    sel=[t for t in FT if sum(dom[v] in ids for v in t)>=2]
    return np.array(sel)
def closest(p,a,b,c):
    # vectorised closest point on triangles; p (N,3), a,b,c (M,3) -> best tri index + barycentrics
    best_d=np.full(len(p),1e9); best_t=np.zeros(len(p),int); best_bc=np.zeros((len(p),3))
    for i in range(len(a)):
        A,B,C=a[i],b[i],c[i]
        ab=B-A; ac=C-A; ap=p-A
        d1=ap@ab; d2=ap@ac
        bp=p-B; d3=bp@ab; d4=bp@ac
        cp=p-C; d5=cp@ab; d6=cp@ac
        va=d3*d6-d5*d4; vb=d5*d2-d1*d6; vc=d1*d4-d3*d2
        denom=va+vb+vc; denom[np.abs(denom)<1e-12]=1e-12
        v=vb/denom; w=vc/denom; u=1-v-w
        bc=np.stack([u,v,w],1)
        # clamp to region (simple: clip & renormalise; adequate for nearest-colour lookup)
        bc=np.clip(bc,0,None); bc/=bc.sum(1,keepdims=True)
        q=bc[:,0:1]*A+bc[:,1:2]*B+bc[:,2:3]*C
        d=((p-q)**2).sum(1)
        m=d<best_d; best_d[m]=d[m]; best_t[m]=i; best_bc[m]=bc[m]
    return best_t,best_bc,np.sqrt(best_d)
def sample(uv):
    H,W=ftex.shape[:2]
    x=np.clip(uv[:,0]*(W-1),0,W-1); y=np.clip(uv[:,1]*(H-1),0,H-1)
    return ftex[y.astype(int),x.astype(int)]

def bake(meshname, segs, out_tex, mask_img, side_joints):
    m=json.load(open('_export/%s.json'%meshname))
    V=np.array(m['v'],float).reshape(-1,3); UV=np.array(m['uv']).reshape(-1,2); T=np.array(m['tris']).reshape(-1,3)
    bw=np.array(m['bw']); bi=np.array(m['bi'])
    bnames=[b.split('/')[-1] for b in m['bones']]
    bw_world={}
    for b,bp in zip(bnames,m['bind']):
        B=np.vstack([np.array(bp).reshape(3,4),[0,0,0,1]]); bw_world[b]=np.linalg.inv(B)
    vdom=bi[np.arange(len(bi)),bw.argmax(1)]
    vseg=[bnames[k] for k in vdom]
    H,W=out_tex.shape[:2]
    ftri=fem_tris(side_joints)
    A,B,C=FP[ftri[:,0]],FP[ftri[:,1]],FP[ftri[:,2]]
    for ti,(a,b,c) in enumerate(T):
        # pick the segment of the triangle = most common
        segk=max(set([vseg[a],vseg[b],vseg[c]]),key=[vseg[a],vseg[b],vseg[c]].count)
        (p0,p1,q0,q1)=segs[segk]
        pu=np.array([[UV[a,0]*W,(1-UV[a,1])*H],[UV[b,0]*W,(1-UV[b,1])*H],[UV[c,0]*W,(1-UV[c,1])*H]])
        x0,x1=int(np.floor(pu[:,0].min()))-2,int(np.ceil(pu[:,0].max()))+2
        y0,y1=int(np.floor(pu[:,1].min()))-2,int(np.ceil(pu[:,1].max()))+2
        x0,y0=max(x0,0),max(y0,0); x1,y1=min(x1,W-1),min(y1,H-1)
        gx,gy=np.meshgrid(np.arange(x0,x1+1)+0.5,np.arange(y0,y1+1)+0.5)
        den=(pu[1,1]-pu[2,1])*(pu[0,0]-pu[2,0])+(pu[2,0]-pu[1,0])*(pu[0,1]-pu[2,1])
        if abs(den)<1e-9: continue
        l0=((pu[1,1]-pu[2,1])*(gx-pu[2,0])+(pu[2,0]-pu[1,0])*(gy-pu[2,1]))/den
        l1=((pu[2,1]-pu[0,1])*(gx-pu[2,0])+(pu[0,0]-pu[2,0])*(gy-pu[2,1]))/den
        l2=1-l0-l1
        e=-0.08   # small margin so island borders get filled too
        msk=(l0>=e)&(l1>=e)&(l2>=e)
        if not msk.any(): continue
        L=np.stack([l0[msk],l1[msk],l2[msk]],1); L=np.clip(L,0,None); L/=L.sum(1,keepdims=True)
        P=L[:,0:1]*V[a]+L[:,1:2]*V[b]+L[:,2:3]*V[c]
        d=p1-p0; t=((P-p0)@d)/(d@d); radial=P-(p0+np.outer(t,d))
        Q=q0+np.outer(t,q1-q0)+radial@R.T
        idx,bc,dist=closest(Q,A,B,C)
        tri=ftri[idx]
        fuv=bc[:,0:1]*FUV[tri[:,0]]+bc[:,1:2]*FUV[tri[:,1]]+bc[:,2:3]*FUV[tri[:,2]]
        col=sample(fuv)
        yy,xx=np.nonzero(msk)
        out_tex[y0+yy,x0+xx]=col
        mask_img[y0+yy,x0+xx]=255
    return out_tex

def segs_for(prefix_vm, fside, armnames, mesh):
    m=json.load(open('_export/%s.json'%mesh))
    bnames=[b.split('/')[-1] for b in m['bones']]
    W={}
    for b,bp in zip(bnames,m['bind']):
        B=np.vstack([np.array(bp).reshape(3,4),[0,0,0,1]]); W[b]=np.linalg.inv(B)[:3,3]
    return W,bnames


import gloves
def fresh(): return np.asarray(Image.open('_export/Player2.png').convert('RGB')).astype(float).copy(), None
def bake_arm(mesh,side,tex,mask):
    Wp,bn=segs_for(None,side,None,mesh)
    n=lambda k:[b for b in bn if b.startswith(k)][0]
    a1,a2,a3,h1=Wp[n('arm1')],Wp[n('arm2')],Wp[n('arm3')],Wp[n('hand1')]
    tip=h1+(h1-a3)/np.linalg.norm(h1-a3)*0.17
    J=lambda k:jpos[side+k]
    ftip=J('Hand')+(J('Hand')-J('ForeArm'))/np.linalg.norm(J('Hand')-J('ForeArm'))*0.17
    S={}
    for b in bn:
        if b.startswith('arm1'): S[b]=(a1,a2,J('Shoulder'),J('Arm'))
        elif b.startswith('arm2'): S[b]=(a2,a3,J('Arm'),J('ForeArm'))
        elif b.startswith('arm3'): S[b]=(a3,h1,J('ForeArm'),J('Hand'))
        else: S[b]=(h1,tip,J('Hand'),ftip)
    bake(mesh,S,tex,mask,[side+'Shoulder',side+'Arm',side+'ForeArm',side+'Hand'])
def bake_leg(tex,mask):
    Wp,bn=segs_for(None,'Right',None,'Player_leg')
    n=lambda k:[b for b in bn if b.startswith(k)][0]
    l1,l2,l3=Wp[n('leg1')],Wp[n('leg2')],Wp[n('leg3')]
    J=lambda k:jpos['Right'+k]
    toe=l3+np.array([0,-0.17,0.02])
    S={}
    for b in bn:
        if b.startswith('leg1'): S[b]=(l1,l2,J('UpLeg'),J('Leg'))
        elif b.startswith('leg2'): S[b]=(l2,l3,J('Leg'),J('Foot'))
        else: S[b]=(l3,toe,J('Foot'),J('ToeBase'))
    bake('Player_leg',S,tex,mask,['RightUpLeg','RightLeg','RightFoot','RightToeBase'])
# the game's two first-person arms share one atlas island -> one texture per side (Arms.cs picks by mesh: player_arm_left = left,
# player_arm_left.002 = right). The right-arm texture also carries the kick leg.
for out,arm,side,leg in [('Player2_max_arms.png','player_arm_left_002','Right',True),('Player2_max_arms_left.png','player_arm_left','Left',False)]:
    tex=np.asarray(Image.open('_export/Player2.png').convert('RGB')).astype(float).copy(); mask=np.zeros(tex.shape[:2],np.uint8)
    bake_arm(arm,side,tex,mask)
    if leg: bake_leg(tex,mask)
    Image.fromarray(tex.clip(0,255).astype(np.uint8)).save('tmp_'+out)
    gloves.fix('tmp_'+out,'_export/Player2.png',[arm]+(['Player_leg'] if leg else []),out)
    print('wrote',out)
