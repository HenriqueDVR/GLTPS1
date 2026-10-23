# puts the new chito mesh on the game's chito skeleton so anims/events still work
#   blender --background --factory-startup --disable-autoexec ChitoTest.blend --python build_chito.py -- <old Chito.fbx> <out dir>
import bpy, os, sys, mathutils

old_fbx, out_dir = sys.argv[-2], sys.argv[-1]
here = os.path.dirname(bpy.data.filepath)

new_arm = bpy.data.objects["Chito"]
body = [o for o in bpy.data.objects if o.type == "MESH"][0]
new_arm.data.pose_position = "REST"
if new_arm.animation_data:
    new_arm.animation_data.action = None

face_img = bpy.data.images["face.png"]
face_img.filepath = os.path.join(here, "face.png")
face_img.reload()

# bake vertex colours to a texture, the game's material ignores them
me = body.data
slot_names = [s.material.name for s in body.material_slots]
body_slot = slot_names.index("Material.001")
face_slot = slot_names.index("Chito")
bake_uv = me.uv_layers.new(name="Bake")
me.uv_layers.active = bake_uv
bpy.context.view_layer.objects.active = body
for o in bpy.data.objects:
    o.select_set(o == body)
for p in me.polygons:
    p.select = p.material_index == body_slot
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_mode(type="FACE")
bpy.ops.uv.smart_project(angle_limit=1.15, island_margin=0.03)
bpy.ops.object.mode_set(mode="OBJECT")

size = 256
body_img = bpy.data.images.new("ChitoBody", size, size)
dummy_img = bpy.data.images.new("dummy", 8, 8)
for mat, img, colour in [(body.material_slots[body_slot].material, body_img, True), (body.material_slots[face_slot].material, dummy_img, False)]:
    nt = mat.node_tree
    out = [n for n in nt.nodes if n.type == "OUTPUT_MATERIAL"][0]
    node = nt.nodes.new("ShaderNodeTexImage")
    node.image = img
    nt.nodes.active = node
    if colour:
        emit = nt.nodes.new("ShaderNodeEmission")
        attr = nt.nodes.new("ShaderNodeVertexColor")
        attr.layer_name = me.color_attributes[0].name
        nt.links.new(attr.outputs["Color"], emit.inputs["Color"])
        nt.links.new(emit.outputs["Emission"], out.inputs["Surface"])
scene = bpy.context.scene
scene.render.engine = "CYCLES"
scene.cycles.samples = 4
scene.render.bake.margin = 4
bpy.ops.object.bake(type="EMIT")
body_img.filepath_raw = os.path.join(out_dir, "T_ChitoBody_new.png")
body_img.file_format = "PNG"
body_img.save()

baked = [tuple(d.uv) for d in me.uv_layers["Bake"].data]
main_uv = me.uv_layers["UVMap"]
for p in me.polygons:
    if p.material_index == body_slot:
        for li in p.loop_indices:
            main_uv.data[li].uv = baked[li]
me.uv_layers.remove(me.uv_layers["Bake"])
me.uv_layers.active = me.uv_layers["UVMap"]

# line up with the old one (hips, feet)
before = set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=old_fbx)
imported = [o for o in bpy.data.objects if o not in before]
old_arm = [o for o in imported if o.type == "ARMATURE"][0]
old_meshes = [o for o in imported if o.type == "MESH"]
old_mesh_name, old_mesh_data_name = old_meshes[0].name, old_meshes[0].data.name
print("OLD armature", old_arm.name, "rot", tuple(old_arm.rotation_euler), "scale", tuple(old_arm.scale), "mesh", old_mesh_name, old_mesh_data_name)
for o in old_meshes:
    bpy.data.objects.remove(o)

def world_heads(arm):
    return {b.name: arm.matrix_world @ b.head_local for b in arm.data.bones}, {b.name: arm.matrix_world @ b.tail_local for b in arm.data.bones}

new_h, new_t = world_heads(new_arm)
old_h, old_t = world_heads(old_arm)
shift = mathutils.Vector((old_h["Pelvis"].x - new_h["Pelvis"].x, old_h["Pelvis"].y - new_h["Pelvis"].y, old_h["Root"].z - new_h["Root"].z))
new_arm.location += shift
bpy.context.view_layer.update()
new_h, new_t = world_heads(new_arm)

# move the old joints to the new body, keep direction + roll so anims still work
same = ["Root", "Torso", "Pelvis", "Shoulder.L", "Forearm.L", "Hand.L", "Shoulder.R", "Forearm.R", "Hand.R",
        "Thigh.L", "Shin.L", "Foot.L", "Thigh.R", "Shin.R", "Foot.R"]
target_head = {n: new_h[n] for n in same}
target_len = {n: (new_t[n] - new_h[n]).length for n in same}
# new rig splits Head into Neck + Head
target_head["Head"] = new_h["Neck"]
target_len["Head"] = (new_t["Head"] - new_h["Neck"]).length
follow = {"Head.001": "Head", "Head.002": "Head", "Head.003": "Head", "Head.004": "Head",
          "Hand_IK.L": "Hand.L", "Hand_IK.R": "Hand.R", "Elbow_IK.L": "Forearm.L", "Elbow_IK.R": "Forearm.R",
          "Foot_IK.L": "Foot.L", "Foot_IK.R": "Foot.R", "Knee_IK.L": "Shin.L", "Knee_IK.R": "Shin.R"}
to_local = old_arm.matrix_world.inverted()
bpy.context.view_layer.objects.active = old_arm
for o in bpy.data.objects:
    o.select_set(o == old_arm)
bpy.ops.object.mode_set(mode="EDIT")
ebones = old_arm.data.edit_bones
for eb in ebones:
    eb.use_connect = False
moved = {}
for name, head in target_head.items():
    eb = ebones[name]
    direction = (eb.tail - eb.head).normalized()
    new_head = to_local @ head
    moved[name] = new_head - eb.head
    roll = eb.roll
    eb.head = new_head
    eb.tail = new_head + direction * (target_len[name] / old_arm.matrix_world.to_scale()[0])
    eb.roll = roll
for name, leader in follow.items():
    if name in ebones:
        eb = ebones[name]
        roll = eb.roll
        eb.translate(moved[leader])
        eb.roll = roll
for eb in ebones:
    if eb.name.endswith("_end") and eb.parent is not None:
        d = eb.tail - eb.head
        eb.head = eb.parent.tail.copy()
        eb.tail = eb.head + d
bpy.ops.object.mode_set(mode="OBJECT")
target_head["Root"] = old_h["Root"]
for n in ["Root", "Pelvis", "Head", "Hand.L", "Foot.L"]:
    print("MOVED", n, tuple(round(v, 3) for v in moved[n]))

# skin to the old skeleton
mw = body.matrix_world.copy()
body.parent = None
body.matrix_world = mw
for o in bpy.data.objects:
    o.select_set(o == body)
bpy.context.view_layer.objects.active = body
for md in list(body.modifiers):
    if md.type == "TRIANGULATE":
        bpy.ops.object.modifier_apply(modifier=md.name)
    else:
        body.modifiers.remove(md)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
groups = {g.name: g for g in body.vertex_groups}
# source weights a hand half to the neck, nothing below the shoulders should follow it
if "Neck" in groups:
    stray = [v.index for v in me.vertices if v.co.z < 1.05 and any(g.group == groups["Neck"].index for g in v.groups)]
    print("NECK weight removed from", len(stray), "low vertices")
    groups["Neck"].remove(stray)
if "Neck" in groups:
    head_g, neck_g = groups["Head"], groups["Neck"]
    for v in me.vertices:
        w = 0.0
        for g in v.groups:
            if g.group == neck_g.index:
                w = g.weight
        if w > 0:
            head_g.add([v.index], w, "ADD")
    body.vertex_groups.remove(neck_g)
missing = [g.name for g in body.vertex_groups if g.name not in old_arm.data.bones]
print("GROUPS without a bone:", missing)
mod = body.modifiers.new("Armature", "ARMATURE")
mod.object = old_arm
body.parent = old_arm
body.matrix_parent_inverse = old_arm.matrix_world.inverted()

# check with the game's anims
import math
scene.render.engine = "BLENDER_EEVEE_NEXT"
scene.render.image_settings.file_format = "PNG"
scene.render.resolution_x, scene.render.resolution_y = 360, 480
for mat in (body.material_slots[body_slot].material, body.material_slots[face_slot].material):
    nt = mat.node_tree
    out = [n for n in nt.nodes if n.type == "OUTPUT_MATERIAL"][0]
    bsdf = [n for n in nt.nodes if n.type == "BSDF_PRINCIPLED"][0]
    tex = [n for n in nt.nodes if n.type == "TEX_IMAGE" and n.image in (body_img, face_img)]
    if tex:
        nt.links.new(tex[0].outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(bsdf.outputs[0], out.inputs["Surface"])
for n in body.material_slots[face_slot].material.node_tree.nodes:
    if n.type == "TEX_IMAGE" and n.image == dummy_img:
        n.image = face_img
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
cam.data.type = "ORTHO"
cam.data.ortho_scale = 2.0
scene.collection.objects.link(cam)
scene.camera = cam
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
sun.rotation_euler = (math.radians(50), 0, math.radians(30))
scene.collection.objects.link(sun)
if scene.world is None:
    scene.world = bpy.data.worlds.new("w")
old_arm.data.pose_position = "POSE"
old_arm.animation_data_create()
for key, frame, angle in [("Walk_1", 8, 35), ("Run", 6, 90), ("Sit_Ket_2", 30, 35), ("Exit_Ket_Back", 30, 90), ("Drive_Left_Hard", 0, 0)]:
    acts = [a for a in bpy.data.actions if a.name.endswith("|" + key)]
    if not acts:
        print("no action", key)
        continue
    old_arm.animation_data.action = acts[-1]
    scene.frame_set(frame)
    a = math.radians(angle)
    cam.location = (math.sin(a) * 5, -math.cos(a) * 5, 0.9)
    cam.rotation_euler = (math.radians(90), 0, a)
    scene.render.filepath = os.path.join(out_dir, "pose_" + key + ".png")
    bpy.ops.render.render(write_still=True)
old_arm.animation_data.action = None
old_arm.data.pose_position = "REST"
bpy.data.objects.remove(cam)
bpy.data.objects.remove(sun)

# same names as the game's fbx so unity keeps the refs
bpy.data.objects.remove(new_arm)
old_arm.name = "Chito"
body.name = old_mesh_name
me.name = old_mesh_data_name
body_mat = body.material_slots[body_slot].material
face_mat = body.material_slots[face_slot].material
body_mat.name = "Chito.001"
face_mat.name = "ChitoHead.001"
# face first then body, same as the original
face_polys = [p.material_index == face_slot for p in me.polygons]
me.materials.clear()
me.materials.append(face_mat)
me.materials.append(body_mat)
for p, is_face in zip(me.polygons, face_polys):
    p.material_index = 0 if is_face else 1
for o in bpy.data.objects:
    if o.type not in ("ARMATURE", "MESH"):
        bpy.data.objects.remove(o)
for a in list(bpy.data.actions):
    bpy.data.actions.remove(a)
if old_arm.animation_data:
    old_arm.animation_data_clear()

bpy.ops.export_scene.fbx(filepath=os.path.join(out_dir, "Chito.fbx"), object_types={"ARMATURE", "MESH"},
                         add_leaf_bones=False, bake_anim=False, use_mesh_modifiers=True, mesh_smooth_type="FACE")
face_img.save_render(os.path.join(out_dir, "T_ChitoFace_new.png"))
print("SLOTS", [s.material.name for s in body.material_slots], "verts", len(me.vertices))
print("DONE")
