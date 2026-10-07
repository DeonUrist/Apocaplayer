# Mesh renders (Blender workbench, the pack's X Bot) of pose_check.py's frames: one sheet, 6 frames a row, each labelled with its scenario,
# the hands' clip / the action / the upper layer, red when pose_check flagged it.
# Usage: python3 render_frames.py poses.pkl XBot.fbx out.png [substring filter, comma separated]
import bpy, sys, os, pickle, math, numpy as np, mathutils
from PIL import Image, ImageDraw, ImageFont
args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
pk, fbx, outp = args[0], args[1], args[2]
flt = args[3].split(",") if len(args) > 3 and args[3] else None
data = pickle.load(open(pk, "rb")); bones = data["bones"]
C = np.array([[1, 0, 0], [0, 0, 1], [0, -1, 0]], float)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx, automatic_bone_orientation=False)
arm = [o for o in bpy.data.objects if o.type == "ARMATURE"][0]
if arm.animation_data: arm.animation_data_clear()
M = np.array(arm.matrix_world); Mi = np.linalg.inv(M)
pbs = {pb.name.split(":")[-1]: pb for pb in arm.pose.bones}
order = [b for b in bones if b in pbs]
rest = {b: np.array(pbs[b].bone.matrix_local) for b in order}
parent = {b: (pbs[b].parent.name.split(":")[-1] if pbs[b].parent else None) for b in order}
for o in bpy.data.objects:
    if o.type == "MESH":
        for i, ms in enumerate(o.material_slots):
            if ms.material: ms.material.diffuse_color = (0.75, 0.62, 0.45, 1) if i == 0 else (0.35, 0.38, 0.42, 1)
sc = bpy.context.scene
sc.render.engine = "BLENDER_WORKBENCH"; sc.display.shading.light = "STUDIO"; sc.display.shading.color_type = "MATERIAL"
sc.display.shading.show_shadows = True; sc.display.shading.show_cavity = True
sc.render.resolution_x, sc.render.resolution_y = 200, 260
sc.world = bpy.data.worlds.new("w"); sc.display.shading.background_type = "VIEWPORT"
bpy.ops.mesh.primitive_plane_add(size=6, location=(0, 0, 0)); g = bpy.context.object
gm = bpy.data.materials.new("g"); gm.diffuse_color = (0.93, 0.93, 0.93, 1); g.data.materials.append(gm)
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
def apply(D, P):
    W = {}; bi = {b: i for i, b in enumerate(bones)}
    floor = min(P[bi["LeftToeBase"]][1] if "LeftToeBase" in bi else P[bi["LeftFoot"]][1] - 0.08, P[bi["RightToeBase"]][1] if "RightToeBase" in bi else P[bi["RightFoot"]][1] - 0.08, P[bi["LeftFoot"]][1] - 0.08, P[bi["RightFoot"]][1] - 0.08)
    for b in order:
        i = bi[b]; Wr = M @ rest[b]
        w = Wr.copy(); w[:3, :3] = (C.T @ D[i] @ C) @ Wr[:3, :3]; p3 = P[i].copy(); p3[1] -= floor; w[:3, 3] = C.T @ p3
        Mb = Mi @ w; W[b] = Mb; p = parent[b]
        basis = np.linalg.inv(rest[b]) @ Mb if p is None else np.linalg.inv(np.linalg.inv(rest[p]) @ rest[b]) @ np.linalg.inv(W[p]) @ Mb
        pbs[b].matrix_basis = mathutils.Matrix(basis.tolist())
    bpy.context.view_layer.update()
def shot(path):
    pos = (-2.4, -3.4, 1.2); tgt = (0, 0, 0.8)
    cam.location = pos; d = mathutils.Vector(tgt) - mathutils.Vector(pos); cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    cam.data.type = "PERSP"; cam.data.lens = 50
    sc.render.filepath = path; bpy.ops.render.render(write_still=True)
poses = [p for p in data["poses"] if not flt or any(f in p["id"] for f in flt)]
try: font = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", 11)
except Exception: font = ImageFont.load_default()
COLS = 6; CW, CH = 200, 260 + 44
sheet = Image.new("RGB", (CW * COLS, CH * ((len(poses) + COLS - 1) // COLS)), "white"); dr = ImageDraw.Draw(sheet)
tmp = outp + ".tmp.png"
for k, p in enumerate(poses):
    apply(p["D"], p["P"]); shot(tmp)
    x, y = CW * (k % COLS), CH * (k // COLS)
    sheet.paste(Image.open(tmp).convert("RGB"), (x, y + 44))
    col = (170, 0, 0) if p["bad"] else (0, 0, 0)
    dr.text((x + 4, y + 2), p["id"], fill=col, font=font)
    dr.text((x + 4, y + 16), "hands: " + (p["rig"] or "-") + ("  act: " + p["act"] if p["act"] else ""), fill=(0, 70, 140), font=font)
    if p["up"]: dr.text((x + 4, y + 30), "upper: " + p["up"], fill=(0, 110, 0), font=font)
sheet.save(outp); os.remove(tmp); print("sheet", outp, len(poses))
