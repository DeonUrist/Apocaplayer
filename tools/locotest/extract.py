# FBX -> per-frame bone world rotations/positions in a common frame (Unity-like: Y up, character faces +Z), saved as npz
import bpy, sys, os, numpy as np, mathutils
src, out = sys.argv[-2], sys.argv[-1]
os.makedirs(out, exist_ok=True)
def clean():
    bpy.ops.wm.read_factory_settings(use_empty=True)
for f in sorted(os.listdir(src)):
    if not f.endswith('.fbx'): continue
    name=f[:-4]
    if os.path.exists(f'{out}/{name}.npz'): continue
    clean()
    bpy.ops.import_scene.fbx(filepath=os.path.join(src,f), automatic_bone_orientation=False)
    arm=[o for o in bpy.data.objects if o.type=='ARMATURE'][0]
    act=arm.animation_data.action if arm.animation_data else None
    fr=act.frame_range if act else (1,1)
    f0,f1=int(round(fr[0])),int(round(fr[1]))
    fps=bpy.context.scene.render.fps/bpy.context.scene.render.fps_base
    bones=[b.name.split(':')[-1] for b in arm.pose.bones]
    par=[ (bones.index(b.parent.name.split(':')[-1]) if b.parent else -1) for b in arm.pose.bones]
    # rest (edit) matrices in armature space
    rest=np.array([np.array(b.bone.matrix_local) for b in arm.pose.bones])
    M=np.array(arm.matrix_world)
    frames=[]
    for t in range(f0,f1+1):
        bpy.context.scene.frame_set(t)
        frames.append([np.array(M@b.matrix) for b in arm.pose.bones])
    np.savez_compressed(f'{out}/{name}.npz', bones=np.array(bones), par=np.array(par), rest=M@rest if False else np.einsum('ij,njk->nik',M,rest), frames=np.array(frames), fps=fps, skinned=any(o.type=='MESH' for o in bpy.data.objects))
    print(name, len(frames), fps, len(bones))
