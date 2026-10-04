import UnityPy, struct
import pmfsm
env=UnityPy.load('out/level1','out/sharedassets1.assets','out/globalgamemanagers.assets','out/resources.assets','out/sharedassets0.assets')
files={f.name:f for f in env.files.values()}
_sc={}
def script(rd):
    raw=rd.get_raw_data()
    fid,pid=struct.unpack('<iq',raw[16:28])
    af=rd.assets_file
    if fid==0: tf=af
    else:
        ext=af.externals[fid-1].path.split('/')[-1].lower()
        tf=None
        for n,f in files.items():
            if n.lower()==ext: tf=f
    if tf is None: return '?ext'
    k=(tf.name,pid)
    if k not in _sc:
        try: _sc[k]=tf.objects[pid].read().m_ClassName
        except Exception as e: _sc[k]='?'
    return _sc[k]
def fsm(rd):
    return pmfsm.parse(rd.get_raw_data())['fsm']
