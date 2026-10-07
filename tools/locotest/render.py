# Mesh renders of check.py's poses on the pack's X Bot (Blender, workbench): per weapon kind one sheet, a row per position,
# 4 moments of the stride from her front-right + one from above (green arrow = the way she moves, grey = where she faces).
# Usage: python3 render.py poses.pkl XBot.fbx out_dir
import bpy, sys, os, pickle, math, numpy as np, mathutils
from PIL import Image, ImageDraw, ImageFont
pk, fbx, out = sys.argv[-3], sys.argv[-2], sys.argv[-1]
data = pickle.load(open(pk, "rb")); bones = data["bones"]
C = np.array([[1, 0, 0], [0, 0, 1], [0, -1, 0]], float)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx, automatic_bone_orientation=False)
arm = [o for o in bpy.data.objects if o.type == "ARMATURE"][0]
if arm.animation_data: arm.animation_data_clear()      # the FBX's own take (T-pose) would override the pose at render
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
sc.render.engine = "BLENDER_WORKBENCH"
sc.display.shading.light = "STUDIO"; sc.display.shading.color_type = "MATERIAL"
sc.display.shading.show_shadows = True; sc.display.shading.show_cavity = True
sc.render.resolution_x, sc.render.resolution_y = 220, 300
sc.render.film_transparent = False
sc.world = bpy.data.worlds.new("w"); sc.display.shading.background_type = "VIEWPORT"
# ground
bpy.ops.mesh.primitive_plane_add(size=6, location=(0, 0, 0)); g = bpy.context.object
gm = bpy.data.materials.new("g"); gm.diffuse_color = (0.93, 0.93, 0.93, 1); g.data.materials.append(gm)
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
def look(cam, pos, target, ortho=None):
    cam.location = pos
    d = mathutils.Vector(target) - mathutils.Vector(pos)
    cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    if ortho: cam.data.type = "ORTHO"; cam.data.ortho_scale = ortho
    else: cam.data.type = "PERSP"; cam.data.lens = 50

def apply(D, P):
    W = {}
    bi = {b: i for i, b in enumerate(bones)}
    for b in order:
        i = bi[b]
        Wr = M @ rest[b]
        w = Wr.copy(); w[:3, :3] = (C.T @ D[i] @ C) @ Wr[:3, :3]; w[:3, 3] = C.T @ P[i]
        Mb = Mi @ w; W[b] = Mb
        p = parent[b]
        if p is None: basis = np.linalg.inv(rest[b]) @ Mb
        else: basis = np.linalg.inv(np.linalg.inv(rest[p]) @ rest[b]) @ np.linalg.inv(W[p]) @ Mb
        pbs[b].matrix_basis = mathutils.Matrix(basis.tolist())
    bpy.context.view_layer.update()

def shot(path, view, hips):
    if view == "34":
        look(cam, (hips[0] - 2.6, hips[1] - 3.6, 1.25), (hips[0], hips[1], 0.85))
        sc.render.resolution_x, sc.render.resolution_y = 220, 300
    else:
        look(cam, (hips[0], hips[1], 6), (hips[0], hips[1], 0), ortho=1.6); cam.rotation_euler = (0, 0, math.pi)
        sc.render.resolution_x, sc.render.resolution_y = 220, 220
    sc.render.filepath = path; bpy.ops.render.render(write_still=True)

os.makedirs(out, exist_ok=True); tmp = os.path.join(out, "_tmp"); os.makedirs(tmp, exist_ok=True)
try: font = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", 13); fontb = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", 15)
except Exception: font = fontb = ImageFont.load_default()
kinds = []
for p in data["poses"]:
    if p["kind"] not in kinds: kinds.append(p["kind"])
only = os.environ.get("ONLY")
for kind in kinds:
    if only and kind not in only.split(","): continue
    rows = [p for p in data["poses"] if p["kind"] == kind]
    RH = 300 + 40; Wd = 220 * 4 + 230
    sheet = Image.new("RGB", (Wd, 50 + RH * len(rows)), "white"); dr = ImageDraw.Draw(sheet)
    dr.text((10, 12), "Apocaplayer locomotion - %s   (4 moments of one stride, from her front-right; last: from above, grey = facing, green = moving)" % kind, fill="black", font=fontb)
    for r, p in enumerate(rows):
        y0 = 50 + r * RH
        for k in range(4):
            apply(p["D"][k], p["P"][k]); fn = os.path.join(tmp, "a.png"); shot(fn, "34", (0, 0))
            sheet.paste(Image.open(fn).convert("RGB"), (220 * k, y0 + 34))
        apply(p["D"][0], p["P"][0]); fn = os.path.join(tmp, "t.png"); shot(fn, "top", (0, 0))
        top = Image.open(fn).convert("RGB"); td = ImageDraw.Draw(top)
        cx, cy, s = 110, 110, 220 / 1.6
        td.line((cx, cy, cx, cy - 0.55 * s), fill=(150, 150, 150), width=3)
        if p["dir"] != -1:
            a = math.radians(p["dir"]); ex, ey = cx + 0.6 * s * math.sin(a), cy - 0.6 * s * math.cos(a)
            td.line((cx, cy, ex, ey), fill=(30, 160, 30), width=4); td.ellipse((ex - 6, ey - 6, ex + 6, ey + 6), fill=(30, 160, 30))
        sheet.paste(top, (220 * 4 + 5, y0 + 34 + 40))
        dr.text((10, y0 + 6), p["label"], fill=(160, 0, 0) if p["bad"] else (0, 90, 0), font=font)
        if p["bad"]: dr.text((10, y0 + 22), "CHECK: " + "; ".join(p["bad"]), fill=(160, 0, 0), font=font)
    sheet.save(os.path.join(out, "loco_%s.png" % kind)); print("sheet", kind, len(rows))
