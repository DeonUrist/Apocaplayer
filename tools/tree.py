import UnityPy,sys
from util import env,script,fsm
f=[x for x in env.files.values()]
lvl=env.files['level1'] if 'level1' in env.files else None
roots=[]
for o in env.objects:
    if o.type.name=='Transform' and o.assets_file.name=='level1':
        t=o.read()
        if not t.m_Father.path_id: roots.append(t)
want=sys.argv[1:]
def comps(go):
    out=[]
    for c in go.m_Components:
        try:
            rd=c.deref(); tn=rd.type.name
            if tn=='MonoBehaviour':
                s=script(rd)
                if s=='PlayMakerFSM':
                    try: s='FSM['+fsm(rd)['name']+']'
                    except Exception as e: s+='[?]'
                out.append(s); continue
            ob=rd.read()
            if tn=='MonoBehaviour':
                s=script(ob.object_reader)
                if s=='PlayMakerFSM':
                    try: s='FSM['+fsm(ob.object_reader)['name']+']'
                    except Exception as e: s+='[?]'
                tn=s
            elif tn=='SkinnedMeshRenderer':
                m=ob.m_Mesh.read() if ob.m_Mesh.path_id else None
                tn+='{%s,%d bones,en=%s}'%(m.m_Name if m else None,len(ob.m_Bones),ob.m_Enabled)
            elif tn=='MeshFilter':
                m=ob.m_Mesh.read() if ob.m_Mesh.path_id else None; tn+='{%s}'%(m.m_Name if m else None)
            elif tn=='Animator':
                tn+='{av=%s,ctrl=%s}'%(ob.m_Avatar.read().m_Name if ob.m_Avatar.path_id else None, ob.m_Controller.read().m_Name if ob.m_Controller.path_id else None)
            elif tn=='Camera':
                tn+='{mask=%d,fov=%.0f,near=%.3f,depth=%s}'%(ob.m_CullingMask.m_Bits,ob.field_of_view,ob.near_clip_plane,ob.m_Depth)
            out.append(tn)
        except Exception as e: out.append('?'+str(e)[:30])
    return out
def walk(t,d,maxd):
    go=t.m_GameObject.read()
    p=t.m_LocalPosition; 
    print('  '*d+'%s%s L%d (%.2f,%.2f,%.2f) s%.2f  %s'%(go.m_Name,'' if go.m_IsActive else ' [off]',go.m_Layer,p.x,p.y,p.z,t.m_LocalScale.x,' '.join(c for c in comps(go) if c!='Transform')))
    if d>=maxd: return
    for c in t.m_Children: walk(c.read(),d+1,maxd)
if __name__=="__main__":
    import sys
    want=sys.argv[1:]
    FILE=sys.argv[1] if sys.argv[1].endswith('.assets') else 'level1'
    if FILE!='level1':
        want=sys.argv[2:]
        roots=[]
        for o in env.objects:
            if o.type.name=='Transform' and o.assets_file.name==FILE:
                t=o.read()
                if not t.m_Father.path_id: roots.append(t)
    for r in roots:
        n=r.m_GameObject.read().m_Name
        if not want or n in want: walk(r,0,40)
