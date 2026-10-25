"""Convert erik90mx's CC0 Blender model into the game's operator FBX.
The original file is preserved next to this reproducible conversion script.
"""
import bpy, os, json, math, re
from mathutils import Vector, Matrix, Quaternion

source=bpy.data.filepath
root=os.path.dirname(os.path.dirname(source))
game=os.path.dirname(os.path.dirname(root))
dest=os.path.join(game,'UnityProject','Assets','ZoneZ','Resources','Art','Characters')
os.makedirs(dest,exist_ok=True)
rig=bpy.data.objects['Esqueleto Univers.000']
rig.animation_data_clear()
for b in rig.pose.bones:
    for c in list(b.constraints): b.constraints.remove(c)
    b.matrix_basis=Matrix.Identity(4)
rig.data.pose_position='REST'
bpy.context.view_layer.update()
selected=[]
for o in list(bpy.context.scene.objects):
    if o.type=='MESH' and o.parent==rig and len(o.data.polygons) and o.visible_get():
        print('PART',o.name,'parent',o.parent_type,o.parent_bone,'groups',len(o.vertex_groups),'visible',o.visible_get(),'collections',[(c.name,c.hide_render,c.hide_viewport) for c in o.users_collection])
        selected.append(o)
        coords=[o.matrix_world@Vector(p) for p in o.bound_box]
        print('BOUNDS',o.name,[[round(min(v[i] for v in coords),2),round(max(v[i] for v in coords),2)] for i in range(3)])

# Bake non-skeletal modifiers, preserve the author's skin weights, and convert
# bone-parented equipment to rigid weights so the game needs one renderer.
palette={
    'Operator Fabric':(.19,.26,.23,1),
    'Operator Armor':(.075,.092,.091,1),
    'Operator Rubber':(.025,.033,.035,1),
    'Operator Skin':(.47,.29,.20,1),
    'Operator Visor':(.075,.24,.29,1),
    'Operator Hardware':(.26,.28,.25,1),
}
mats=[]
for name,color in palette.items():
    m=bpy.data.materials.new(name);m.diffuse_color=color
    m.use_nodes=True
    bsdf=m.node_tree.nodes.get('Principled BSDF');bsdf.inputs['Base Color'].default_value=color
    bsdf.inputs['Roughness'].default_value=.68 if 'Fabric' in name else .48
    mats.append(m)
deps=bpy.context.evaluated_depsgraph_get()
copies=[]
for original in selected:
    mesh=bpy.data.meshes.new_from_object(original.evaluated_get(deps),preserve_all_data_layers=True,depsgraph=deps)
    mesh.transform(original.matrix_world)
    obj=bpy.data.objects.new('Operator '+original.name,mesh)
    bpy.context.scene.collection.objects.link(obj)
    for group in original.vertex_groups: obj.vertex_groups.new(name=group.name)
    if original.parent_type=='BONE':
        obj.vertex_groups.clear()
        group=obj.vertex_groups.new(name=original.parent_bone)
        group.add(list(range(len(mesh.vertices))),1,'REPLACE')
    mesh.materials.clear()
    for m in mats: mesh.materials.append(m)
    for p in mesh.polygons:
        center=sum((mesh.vertices[i].co for i in p.vertices),Vector())/len(p.vertices)
        index=1
        if original.name=='Soldado':
            index=0
            if center.z < -8.2: index=2
            if center.z>7.1: index=3
            if center.z>9.2: index=1
        elif original.name.startswith('Mano'): index=2
        elif original.name=='Sphere': index=4
        elif original.name in ['Sphere.001','Sphere.002','Soldado.005','Soldado.007','Soldado.008']:index=5
        p.material_index=index
        p.use_smooth=True
    # Split the normals only at sharp manufactured edges; anatomy stays smooth.
    import bmesh
    bm=bmesh.new();bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    for e in bm.edges:
        if e.is_manifold:e.smooth=e.calc_face_angle(0)<math.radians(48)
    bm.to_mesh(mesh);bm.free()
    copies.append(obj)
for o in bpy.context.selected_objects:o.select_set(False)
for o in copies:o.select_set(True)
bpy.context.view_layer.objects.active=copies[0]
bpy.ops.object.join()
model=copies[0];model.name='OperatorMesh';model.data.name='CC0 operator skinned mesh'
low=min(v.co.z for v in model.data.vertices);high=max(v.co.z for v in model.data.vertices)
factor=1.80/(high-low)
normalize=Matrix.Scale(factor,4) @ Matrix.Translation(Vector((0,0,-low)))
model.data.transform(normalize)
rig.matrix_world=normalize @ rig.matrix_world
mod=model.modifiers.new('Operator skin','ARMATURE');mod.object=rig
# UVs for the project's reusable woven-fabric and micro-surface textures.
bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=math.radians(66),island_margin=.01)
bpy.ops.object.mode_set(mode='OBJECT')
# Bake a relaxed grip with anatomically consistent flexion, then make that pose
# the bind pose. The source's individual finger bones have different local axes.
# Keeping this out of runtime avoids applying the same wrong axis to every bone.
rig.data.pose_position='POSE'
for bone in rig.pose.bones:
    match=re.fullmatch(r'dedo([1-4]),([1-3])\.(R|L)',bone.name)
    if not match:continue
    joint=int(match.group(2))
    sign=1 if match.group(3)=='R' else -1
    axis=bone.bone.matrix_local.to_3x3().inverted() @ Vector((0,sign,0))
    bone.rotation_mode='QUATERNION'
    bone.rotation_quaternion=Quaternion(axis.normalized(),math.radians({1:18,2:24,3:16}[joint]))
bpy.context.view_layer.update()
bpy.context.view_layer.objects.active=model
bpy.ops.object.modifier_apply(modifier=mod.name)
bpy.context.view_layer.objects.active=rig
bpy.ops.object.mode_set(mode='POSE')
bpy.ops.pose.armature_apply(selected=False)
bpy.ops.object.mode_set(mode='OBJECT')
mod=model.modifiers.new('Operator skin','ARMATURE');mod.object=rig
rig.name='OperatorRig'
for o in bpy.context.scene.objects:o.select_set(False)
model.select_set(True);rig.select_set(True)
bpy.context.view_layer.objects.active=rig
rig.hide_set(False)
bpy.ops.export_scene.fbx(filepath=os.path.join(dest,'Operator.fbx'),use_selection=True,object_types={'ARMATURE','MESH'},use_mesh_modifiers=True,add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',use_armature_deform_only=True)
model.data.calc_loop_triangles()
print('OPERATOR_EXPORTED',len(model.data.vertices),'vertices',len(model.data.loop_triangles),'triangles',len(model.data.materials),'materials',factor,'scale')
with open(os.path.join(root,'operator-export.json'),'w') as f:
    json.dump({'source':source,'vertices':len(model.data.vertices),'triangles':len(model.data.loop_triangles),'height':1.8,'materials':[m.name for m in model.data.materials],'bones':[b.name for b in rig.data.bones]},f,indent=2)
selected=[model]
for o in bpy.context.scene.objects: o.hide_render=o not in selected
rig.hide_render=False
scene=bpy.context.scene
scene.render.engine='BLENDER_WORKBENCH'
scene.display.shading.light='STUDIO'
scene.display.shading.color_type='MATERIAL'
scene.display.shading.show_shadows=True
scene.display.shading.show_cavity=True
scene.display.shading.cavity_type='BOTH'
if not scene.world: scene.world=bpy.data.worlds.new('Audit World')
scene.world.color=(.18,.18,.18)
scene.render.resolution_x=1100
scene.render.resolution_y=1100
scene.render.resolution_percentage=100
camera_data=bpy.data.cameras.new('Audit camera')
camera=bpy.data.objects.new('Audit camera',camera_data)
scene.collection.objects.link(camera)
camera.location=(2.7,-4,1.7)
direction=Vector((0,0,.92))-camera.location
camera.rotation_euler=direction.to_track_quat('-Z','Y').to_euler()
camera_data.type='ORTHO'
camera_data.ortho_scale=2.3
scene.camera=camera
scene.render.filepath=os.path.join(root,'operator-rest.png')
bpy.ops.render.render(write_still=True)
print('BONES_REST')
for b in rig.data.bones:
    if b.name in ['Pelvis','Cabeza','Pecho','Brazo.R','Antebrazo.R','HuesoMuneca.R','Muslo.R','Espinilla.R','Tarso.R','Muslo.L','Espinilla.L','Tarso.L']:
        print(b.name, 'head',list(rig.matrix_world @ b.head_local),'tail',list(rig.matrix_world @ b.tail_local))
