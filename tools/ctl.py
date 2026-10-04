import UnityPy,sys
env=UnityPy.load('out/sharedassets1.assets')
for o in env.objects:
    if o.type.name=='AnimatorController':
        c=o.read()
        if c.m_Name!=sys.argv[1]: continue
        tos=dict(c.m_TOS)
        C=c.m_Controller
        print('controller',c.m_Name)
        vals=C.m_Values.data.m_ValueArray
        print(' params',[(tos.get(v.m_ID,v.m_ID),v.m_Type) for v in vals])
        for sm in C.m_StateMachineArray:
            sm=sm.data
            for st in sm.m_StateConstantArray:
                s=st.data
                print('  state',tos.get(s.m_NameID,s.m_NameID),'speed',s.m_Speed,'mirror',getattr(s,'m_Mirror',None),'loop?')
                for t in s.m_TransitionConstantArray:
                    t=t.data
                    conds=[(tos.get(cc.data.m_ConditionEvent,cc.data.m_ConditionEvent),cc.data.m_ConditionMode,cc.data.m_EventTreshold) for cc in t.m_ConditionConstantArray]
                    print('     ->',t.m_DestinationState,conds,'exit',t.m_HasExitTime)
            for t in sm.m_AnyStateTransitionConstantArray:
                t=t.data
                conds=[(tos.get(cc.data.m_ConditionEvent,cc.data.m_ConditionEvent),cc.data.m_ConditionMode,cc.data.m_EventTreshold) for cc in t.m_ConditionConstantArray]
                print('   any ->',t.m_DestinationState,conds)
        print(' clips',[x.read().m_Name for x in c.m_AnimationClips])
