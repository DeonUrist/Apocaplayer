# Retargets a non-Mixamo humanoid animation FBX onto the pack's X Bot (mixamorig skeleton) and writes a Mixamo-style FBX the Unity builder
# takes like any other clip. Made for the Sketchfab "Desert Eagle reload" (PistolReload: Root/Hips/Stomach/Chest/Upperarm.L ... F11..F53).
# Per mapped bone: the source's world rotation relative to its rest pose, with each source bone's rest direction first swung onto the
# target's rest direction (so an A-pose source would still land right), applied to the target's rest. Unmapped target bones follow their parent.
# Usage (bpy): python3 retarget_to_mixamo.py source.fbx XBot.fbx out.fbx [fps]
import bpy, sys, numpy as np, mathutils
src_path, xbot_path, out_path = sys.argv[1:4]
out_fps = int(sys.argv[4]) if len(sys.argv) > 4 else 30

MAP = {"Hips": "Hips", "Stomach": "Spine", "Chest": "Spine2", "Neck": "Neck", "Head": "Head"}
for s, S in (("L", "Left"), ("R", "Right")):
    MAP.update({"Shoulder." + s: S + "Shoulder", "Upperarm." + s: S + "Arm", "Lowerarm." + s: S + "ForeArm", "Hand." + s: S + "Hand",
                "Upperleg." + s: S + "UpLeg", "Lowerleg." + s: S + "Leg", "Foot." + s: S + "Foot", "Toe." + s: S + "ToeBase"})
    # F1 index, F2 middle, F3 ring, F4 pinky, F5 thumb (from the rest positions: F5 sits at the wrist, F1 in front)
    for f, F in ((1, "Index"), (2, "Middle"), (3, "Ring"), (4, "Pinky"), (5, "Thumb")):
        for k in (1, 2, 3):
            MAP["F%d%d.%s" % (f, k, s)] = "%sHand%s%d" % (S, F, k)

def ortho(m):
    u, _, vt = np.linalg.svd(m); r = u @ vt
    if np.linalg.det(r) < 0: u[:, -1] *= -1; r = u @ vt
    return r
def swing(a, b):
    a = a / np.linalg.norm(a); b = b / np.linalg.norm(b); v = np.cross(a, b); c = a @ b
    if c < -0.9999: return -np.eye(3)
    K = np.array([[0, -v[2], v[1]], [v[2], 0, -v[0]], [-v[1], v[0], 0]]); return np.eye(3) + K + K @ K / (1 + c)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src_path, automatic_bone_orientation=False)
src = [o for o in bpy.data.objects if o.type == "ARMATURE"][0]
src_objs = list(bpy.data.objects)
act = src.animation_data.action; f0, f1 = int(round(act.frame_range[0])), int(round(act.frame_range[1]))
src_fps = bpy.context.scene.render.fps / bpy.context.scene.render.fps_base
bpy.ops.import_scene.fbx(filepath=xbot_path, automatic_bone_orientation=False)
tgt = [o for o in bpy.data.objects if o.type == "ARMATURE" and o not in src_objs][0]
if tgt.animation_data: tgt.animation_data_clear()
Ms, Mt = np.array(src.matrix_world), np.array(tgt.matrix_world)
sname = {b.name: b for b in src.pose.bones}
tb = {b.name.split(":")[-1]: b for b in tgt.pose.bones}
inv = {v: k for k, v in MAP.items()}

def world_rot(M, m): return ortho((M @ m)[:3, :3])
def rest_dir(M, b): return (M[:3, :3] @ (np.array(b.bone.tail_local) - np.array(b.bone.head_local)))
# per mapped bone: the source's "T-pose" world rotation = its rest swung onto the target rest direction
srcT = {}
for sn, tn in MAP.items():
    if sn not in sname or tn not in tb: continue
    S = swing(rest_dir(Ms, sname[sn]), rest_dir(Mt, tb[tn]))
    srcT[tn] = S @ world_rot(Ms, np.array(sname[sn].bone.matrix_local))
t_rest = {n: np.array(b.bone.matrix_local) for n, b in tb.items()}
t_restW = {n: world_rot(Mt, m) for n, m in t_rest.items()}
order = [b.name.split(":")[-1] for b in tgt.pose.bones]   # parents first
parent = {n: (tb[n].parent.name.split(":")[-1] if tb[n].parent else None) for n in order}

frames = list(range(f0, f1 + 1, max(1, int(round(src_fps / out_fps)))))
bpy.context.scene.render.fps = out_fps; bpy.context.scene.render.fps_base = 1
for b in tgt.pose.bones: b.rotation_mode = "QUATERNION"
for i, fr in enumerate(frames):
    bpy.context.scene.frame_set(fr)
    D = {}; Rarm = {}
    for n in order:
        p = parent[n]
        if n in srcT and inv[n] in sname:
            D[n] = world_rot(Ms, np.array(sname[inv[n]].matrix)) @ srcT[n].T
        else:
            D[n] = D[p] if p else np.eye(3)
        Rw = D[n] @ t_restW[n]                                     # wanted world rotation
        Rarm[n] = ortho(np.linalg.inv(Mt[:3, :3]) @ Rw)       # in armature space (object scale removed by ortho)
        Rl = ortho(t_rest[n][:3, :3]) if not p else ortho((np.linalg.inv(t_rest[p]) @ t_rest[n])[:3, :3])
        Rp = np.eye(3) if not p else Rarm[p]
        basis = Rl.T @ Rp.T @ Rarm[n]
        pb = tb[n]; pb.rotation_quaternion = mathutils.Matrix(basis.tolist()).to_quaternion(); pb.location = (0, 0, 0)
        pb.keyframe_insert("rotation_quaternion", frame=i + 1)
        if not p: pb.keyframe_insert("location", frame=i + 1)
bpy.context.scene.frame_start, bpy.context.scene.frame_end = 1, len(frames)
# only the X Bot (armature + its meshes) goes out
for o in bpy.data.objects: o.select_set(False)
tgt.select_set(True)
for o in bpy.data.objects:
    if o.type == "MESH" and o.parent == tgt: o.select_set(True)
bpy.context.view_layer.objects.active = tgt
bpy.ops.export_scene.fbx(filepath=out_path, use_selection=True, object_types={"ARMATURE", "MESH"}, add_leaf_bones=False,
                         bake_anim=True, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False, bake_anim_simplify_factor=0.0)
print("retargeted", len(frames), "frames at", out_fps, "fps ->", out_path)
