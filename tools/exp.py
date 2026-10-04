import UnityPy, json, numpy as np, os
from UnityPy.helpers.MeshHelper import MeshHandler
from util import env
os.makedirs('exp',exist_ok=True)
def tname(t): return t.m_GameObject.read().m_Name
def path(t):
    p=[]
    while True:
        p.append(tname(t))
        if not t.m_Father.path_id: break
        t=t.m_Father.read()
    return '/'.join(reversed(p))
def meshdump(m,name):
    h=MeshHandler(m); h.process()
    d={'name':m.m_Name,'v':list(h.m_Vertices),'n':list(h.m_Normals or []),'uv':list(h.m_UV0 or []),'tris':[int(i) for i in h.m_IndexBuffer],
       'bw':[list(x) for x in (h.m_BoneWeights or [])],'bi':[list(x) for x in (h.m_BoneIndices or [])],
       'bind':[[float(e) for e in (b.e00,b.e01,b.e02,b.e03,b.e10,b.e11,b.e12,b.e13,b.e20,b.e21,b.e22,b.e23)] for b in m.m_BindPose],
       'subs':[(s.firstByte,s.indexCount) for s in m.m_SubMeshes]}
    return d
done=set()
for o in env.objects:
    if o.type.name!='SkinnedMeshRenderer': continue
    r=o.read()
    if not r.m_Mesh.path_id: continue
    m=r.m_Mesh.read()
    if m.m_Name not in ('player_arm_left','player_arm_left.002','Player2','Player_leg') or m.m_Name in done: continue
    done.add(m.m_Name)
    d=meshdump(m,m.m_Name)
    go=r.m_GameObject.read(); tr=go.m_Components[0].read()
    d['smr_path']=path(tr)
    d['bones']=[path(b.read()) for b in r.m_Bones]
    d['root']=path(r.m_RootBone.read()) if r.m_RootBone.path_id else None
    mats=[]
    for x in r.m_Materials:
        mm=x.read(); e={'name':mm.m_Name,'shader':mm.m_Shader.read().m_Name if mm.m_Shader.path_id else None,'tex':{}}
        for k,v in mm.m_SavedProperties.m_TexEnvs:
            if v.m_Texture.path_id:
                t=v.m_Texture.read(); e['tex'][k]=t.m_Name
                fn='exp/%s.png'%t.m_Name
                if not os.path.exists(fn): t.image.save(fn)
        e['floats']=dict(mm.m_SavedProperties.m_Floats); e['colors']={k:[c.r,c.g,c.b,c.a] for k,c in mm.m_SavedProperties.m_Colors}
        mats.append(e)
    d['mats']=mats
    # bone local TRS
    bl={}
    for b in r.m_Bones:
        t=b.read(); q=t.m_LocalRotation; p=t.m_LocalPosition; s=t.m_LocalScale
        bl[path(t)]=[p.x,p.y,p.z,q.x,q.y,q.z,q.w,s.x,s.y,s.z]
    d['bone_local']=bl
    json.dump(d,open('exp/%s.json'%m.m_Name.replace('.','_'),'w'))
    print(m.m_Name,d['smr_path'],len(d['v'])//3,'verts',len(d['bones']),'bones',mats)
