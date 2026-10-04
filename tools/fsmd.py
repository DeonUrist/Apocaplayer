import sys,struct
import pmfsm
from util import env,script
FILE,PATH,FN=sys.argv[1],sys.argv[2],sys.argv[3]
parts=PATH.split('/')
root=None
for o in env.objects:
    if o.type.name=='Transform' and o.assets_file.name==FILE:
        t=o.read()
        if t.m_Father.path_id==0 and t.m_GameObject.read().m_Name==parts[0]: root=t; break
def child(t,n):
    for c in t.m_Children:
        ct=c.read()
        if ct.m_GameObject.read().m_Name==n: return ct
for p in parts[1:]: root=child(root,p)
TYPES={0:'int',1:'bool',2:'float',3:'string',5:'obj',7:'enum',12:'Array',15:'FsmFloat',16:'FsmInt',17:'FsmBool',18:'FsmString',19:'FsmGameObject',20:'FsmOwnerDefault',23:'FsmEvent',24:'FsmObject',28:'FsmVector3',31:'FsmEventTarget',39:'FsmVar',41:'FsmArray',42:'FsmEnum'}
def val(ad,t,pos,i):
    try:
        if t==15: f=ad['fsmFloatParams'][pos]; return ('{%s}'%f['name'] if f.get('useVariable') else '')+'%g'%f['value']
        if t==16: f=ad['fsmIntParams'][pos]; return ('{%s}'%f['name'] if f.get('useVariable') else '')+str(f['value'])
        if t==17: f=ad['fsmBoolParams'][pos]; return ('{%s}'%f['name'] if f.get('useVariable') else '')+str(f['value'])
        if t==18: f=ad['fsmStringParams'][pos]; return ('{%s}'%f['name'] if f.get('useVariable') else '')+repr(f['value'])
        if t==23: return 'event:'+ad['stringParams'][pos]
        if t==20: return 'owner' if ad['fsmOwnerDefaultParams'][pos]['ownerOption']==0 else 'go'
        if t==19: f=ad['fsmGameObjectParams'][pos]; return '{%s}'%f['name'] if f.get('useVariable') else 'go'
        if t in (0,1,2,7):
            b=bytes(ad['byteData'][pos:pos+ad['paramByteDataSize'][i]])
            if t==2 and len(b)==4: return '%g'%struct.unpack('<f',b)[0]
            if len(b)==4: return str(struct.unpack('<i',b)[0])
            return b.hex()
        if t==41: f=ad['fsmArrayParams'][pos]; return '{%s}'%f['name'] if f.get('useVariable') else 'array'
        if t==24: f=ad['fsmObjectParams'][pos]; return '{%s}'%f['name'] if f.get('useVariable') else 'obj'
        if t==39: f=ad['fsmVarParams'][pos]; return 'var '+f.get('variableName','')
    except Exception as e: return '?'+str(e)[:20]
    return TYPES.get(t,str(t))
def dump(root,FN):
  for c in root.m_Components:
     rd=c.deref()
     if rd.type.name!='MonoBehaviour': continue
     raw=rd.get_raw_data()
     if script(rd)!='PlayMakerFSM': continue
     fsm=pmfsm.parse(raw)['fsm']
     if FN!='*' and fsm['name']!=FN: continue
     print('FSM',FN,'start',fsm['startState'])
     print(' globals:',[(t['fsmEvent']['name'],t['toState']) for t in fsm.get('globalTransitions',[])])
     for st in fsm['states']:
         ad=st['actionData']; names=ad['actionNames']; starts=ad['actionStartIndex']+[len(ad['paramDataType'])]
         print('  [%s] -> %s'%(st['name'],[(t['fsmEvent']['name'],t['toState']) for t in st['transitions']]))
         for k,n in enumerate(names):
             ps=[]
             for i in range(starts[k],starts[k+1]):
                 ps.append('%s=%s'%(ad['paramName'][i],val(ad,ad['paramDataType'][i],ad['paramDataPos'][i],i)))
             print('     %s(%s)'%(n.split('.')[-1],', '.join(ps)))

dump(root.m_GameObject.read(),FN)
