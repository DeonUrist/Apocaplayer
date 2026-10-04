import json,sys
from util import env
from tree import comps
out={}
def walk(t,pfx,d):
    go=t.m_GameObject.read(); n=pfx+'/'+go.m_Name if pfx else go.m_Name
    p=t.m_LocalPosition;q=t.m_LocalRotation;s=t.m_LocalScale
    d[n]={'trs':[p.x,p.y,p.z,q.x,q.y,q.z,q.w,s.x,s.y,s.z],'active':go.m_IsActive,'layer':go.m_Layer,'c':[c for c in comps(go) if c!='Transform']}
    for c in t.m_Children: walk(c.read(),n,d)
names=sys.argv[1:]
for o in env.objects:
    if o.type.name=='Transform' and o.assets_file.name=='sharedassets1.assets':
        t=o.read()
        if t.m_Father.path_id==0:
            n=t.m_GameObject.read().m_Name
            if n in names and n not in out:
                d={}; walk(t,'',d); out[n]=d
json.dump(out,open('exp/prefabs.json','w'))
print({k:len(v) for k,v in out.items()})
