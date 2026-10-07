import json,struct,numpy as np
CT={5120:np.int8,5121:np.uint8,5122:np.int16,5123:np.uint16,5125:np.uint32,5126:np.float32}
NC={'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4,'MAT4':16}
class G:
    def __init__(s,p):
        b=open(p,'rb').read(); l=struct.unpack('<I',b[12:16])[0]
        s.j=json.loads(b[20:20+l]); o=20+l; bl=struct.unpack('<I',b[o:o+4])[0]; s.bin=bytearray(b[o+8:o+8+bl])
    def acc(s,i):
        j=s.j; a=j['accessors'][i]; bv=j['bufferViews'][a['bufferView']]; dt=CT[a['componentType']]; n=NC[a['type']]
        off=bv.get('byteOffset',0)+a.get('byteOffset',0); st=bv.get('byteStride'); isz=np.dtype(dt).itemsize*n
        if st and st!=isz: arr=np.array([np.frombuffer(s.bin,dt,n,off+k*st) for k in range(a['count'])])
        else: arr=np.frombuffer(bytes(s.bin),dt,a['count']*n,off).reshape(a['count'],n)
        if a.get('normalized'): arr=arr/np.iinfo(dt).max
        return arr.astype(np.float64) if dt==np.float32 or a.get('normalized') else arr.astype(np.int64)
    def mesh(s):
        pr=s.j['meshes'][0]['primitives'][0]; A=pr['attributes']
        d=dict(P=s.acc(A['POSITION']),N=s.acc(A['NORMAL']),UV=s.acc(A['TEXCOORD_0']),J=s.acc(A['JOINTS_0']),W=s.acc(A['WEIGHTS_0']),T=s.acc(pr['indices']).reshape(-1,3))
        sk=s.j['skins'][0]; d['names']=[s.j['nodes'][i]['name'] for i in sk['joints']]
        d['ibm']=s.acc(sk['inverseBindMatrices']).reshape(-1,4,4).transpose(0,2,1)
        return d
def write(path,template,P,N,UV,J,W,T,image_png=None,keep_image=False):
    """new glb: template's nodes/skin/IBM, new mesh data (non-interleaved)."""
    g=G(template); j=g.j
    binb=bytearray(); bvs=[]; accs=[]
    def add(arr,ctype,typ,target=None,minmax=False):
        nonlocal binb
        while len(binb)%4: binb.append(0)
        off=len(binb); data=arr.tobytes(); binb.extend(data)
        bv={'buffer':0,'byteOffset':off,'byteLength':len(data)}
        if target: bv['target']=target
        bvs.append(bv); a={'bufferView':len(bvs)-1,'componentType':ctype,'count':len(arr),'type':typ}
        if minmax: a['min']=arr.min(0).tolist(); a['max']=arr.max(0).tolist()
        accs.append(a); return len(accs)-1
    ibm_old=g.acc(j['skins'][0]['inverseBindMatrices']).astype(np.float32)
    attrs={'POSITION':add(P.astype(np.float32),5126,'VEC3',34962,True),'NORMAL':add(N.astype(np.float32),5126,'VEC3',34962),
           'TEXCOORD_0':add(UV.astype(np.float32),5126,'VEC2',34962),'JOINTS_0':add(J.astype(np.uint16),5123,'VEC4',34962),'WEIGHTS_0':add(W.astype(np.float32),5126,'VEC4',34962)}
    idx=add(T.reshape(-1).astype(np.uint32),5125,'SCALAR',34963)
    ibmi=add(ibm_old,5126,'MAT4')
    j['skins'][0]['inverseBindMatrices']=ibmi
    prim={'attributes':attrs,'indices':idx}
    for k in ('images','textures','samplers','materials'): j.pop(k,None)
    if image_png is not None:
        while len(binb)%4: binb.append(0)
        off=len(binb); binb.extend(image_png); bvs.append({'buffer':0,'byteOffset':off,'byteLength':len(image_png)})
        j['images']=[{'bufferView':len(bvs)-1,'mimeType':'image/png','name':'Player_max'}]; j['samplers']=[{}]; j['textures']=[{'sampler':0,'source':0}]
        j['materials']=[{'name':'Player_max','doubleSided':False,'pbrMetallicRoughness':{'baseColorTexture':{'index':0},'metallicFactor':0,'roughnessFactor':0.8}}]
        prim['material']=0
    j['meshes'][0]['primitives']=[prim]; j['accessors']=accs; j['bufferViews']=bvs
    while len(binb)%4: binb.append(0)
    j['buffers']=[{'byteLength':len(binb)}]
    js=json.dumps(j,separators=(',',':')).encode()
    while len(js)%4: js+=b' '
    open(path,'wb').write(struct.pack('<III',0x46546C67,2,12+8+len(js)+8+len(binb))+struct.pack('<II',len(js),0x4E4F534A)+js+struct.pack('<II',len(binb),0x004E4942)+bytes(binb))
