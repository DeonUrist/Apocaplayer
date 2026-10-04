import re,numpy as np
src=open(__import__('os').path.join(__import__('os').path.dirname(__file__),'..','Bindposes.cs')).read()
BPF={}
for name,vals in re.findall(r'\{ "([^"]+)", new float\[\] \{([^}]*)\}',src):
    v=[float(x.strip().rstrip('f')) for x in vals.split(',')]
    M=np.eye(4); M[:3,:4]=np.array(v).reshape(3,4); BPF[name.split(':')[1]]=M
PARENT={'Hips':None,'Spine':'Hips','Spine1':'Spine','Spine2':'Spine1','Neck':'Spine2','Head':'Neck',
 'LeftShoulder':'Spine2','LeftArm':'LeftShoulder','LeftForeArm':'LeftArm','LeftHand':'LeftForeArm',
 'RightShoulder':'Spine2','RightArm':'RightShoulder','RightForeArm':'RightArm','RightHand':'RightForeArm',
 'LeftUpLeg':'Hips','LeftLeg':'LeftUpLeg','LeftFoot':'LeftLeg','LeftToeBase':'LeftFoot',
 'RightUpLeg':'Hips','RightLeg':'RightUpLeg','RightFoot':'RightLeg','RightToeBase':'RightFoot'}
