import json,struct,numpy as np
def q2m(q):
    x,y,z,w=q; n=np.sqrt(x*x+y*y+z*z+w*w); x,y,z,w=x/n,y/n,z/n,w/n
    return np.array([[1-2*(y*y+z*z),2*(x*y-z*w),2*(x*z+y*w)],[2*(x*y+z*w),1-2*(x*x+z*z),2*(y*z-x*w)],[2*(x*z-y*w),2*(y*z+x*w),1-2*(x*x+y*y)]])
def load(path):
    b=open(path,'rb').read(); off=12; js=None; bin_=None
    while off<len(b):
        l,t=struct.unpack('<II',b[off:off+8]); off+=8
        if t==0x4E4F534A: js=json.loads(b[off:off+l])
        else: bin_=b[off:off+l]
        off+=l
    def acc(i):
        a=js['accessors'][i]; bv=js['bufferViews'][a['bufferView']]
        comps={'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4,'MAT4':16}[a['type']]
        dt={5126:np.float32,5123:np.uint16,5121:np.uint8,5125:np.uint32}[a['componentType']]
        o=bv.get('byteOffset',0)+a.get('byteOffset',0); n=a['count']
        st=bv.get('byteStride',0) or np.dtype(dt).itemsize*comps
        raw=np.frombuffer(bin_,dtype=np.uint8,count=st*(n-1)+np.dtype(dt).itemsize*comps,offset=o)
        arr=np.lib.stride_tricks.as_strided(raw.view(np.uint8),shape=(n,np.dtype(dt).itemsize*comps),strides=(st,1)).copy().view(dt).reshape(n,comps)
        if a.get('normalized') and dt==np.uint8: arr=arr/255.0
        if a.get('normalized') and dt==np.uint16: arr=arr/65535.0
        return arr.astype(np.float64) if dt==np.float32 else arr
    nodes=js['nodes']; N=len(nodes); par=[-1]*N
    for i,n in enumerate(nodes):
        for c in n.get('children',[]): par[c]=i
    loc=[]
    for n in nodes:
        M=np.eye(4)
        if 'matrix' in n: M=np.array(n['matrix']).reshape(4,4).T
        else:
            R=q2m(n.get('rotation',[0,0,0,1])); S=np.diag(n.get('scale',[1,1,1])); M[:3,:3]=R@S; M[:3,3]=n.get('translation',[0,0,0])
        loc.append(M)
    W=[None]*N
    def wo(i):
        if W[i] is None: W[i]=loc[i] if par[i]<0 else wo(par[i])@loc[i]
        return W[i]
    for i in range(N): wo(i)
    mn=[i for i,n in enumerate(nodes) if 'mesh' in n and 'skin' in n][0]
    skin=js['skins'][nodes[mn]['skin']]; joints=skin['joints']
    ibm=acc(skin['inverseBindMatrices']).reshape(-1,4,4).transpose(0,2,1)
    skinM=np.array([W[j]@ibm[k] for k,j in enumerate(joints)])
    P=[];Nn=[];UV=[];T=[];J=[];Wt=[]
    for pr in js['meshes'][nodes[mn]['mesh']]['primitives']:
        at=pr['attributes']; base=sum(len(p) for p in P)
        p=acc(at['POSITION']); P.append(p); Nn.append(acc(at['NORMAL'])); UV.append(acc(at['TEXCOORD_0']))
        J.append(acc(at['JOINTS_0']).astype(int)); Wt.append(acc(at['WEIGHTS_0']))
        T.append(acc(pr['indices']).reshape(-1,3).astype(int)+base)
    P=np.vstack(P);Nn=np.vstack(Nn);UV=np.vstack(UV);T=np.vstack(T);J=np.vstack(J);Wt=np.vstack(Wt)
    # rest pose positions (glTF space)
    Ph=np.c_[P,np.ones(len(P))]
    R=np.zeros((len(P),3))
    for k in range(4):
        M=skinM[J[:,k]]; R+=Wt[:,k:k+1]*np.einsum('nij,nj->ni',M,Ph)[:,:3]
    R/=Wt.sum(1,keepdims=True)
    jn=[nodes[j]['name'] for j in joints]
    jw=np.array([W[j] for j in joints])
    return dict(js=js,nodes=nodes,par=par,W=W,joints=joints,jnames=jn,jworld=jw,P=R,N=Nn,UV=UV,T=T,J=J,Wt=Wt,ibm=ibm)
