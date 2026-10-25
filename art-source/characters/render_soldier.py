import bpy, os, json
from mathutils import Vector
rig=bpy.data.objects['Esqueleto Univers.000']
print('SCENE_MESHES')
for o in bpy.context.scene.objects:
    if o.type=='MESH' and len(o.data.polygons):
        print(json.dumps({'name':o.name,'hide':o.hide_get(),'render':not o.hide_render,'parent':o.parent.name if o.parent else None,'armatures':[m.object.name if m.object else None for m in o.modifiers if m.type=='ARMATURE'],'bounds':[[round(x,2) for x in (o.matrix_world @ Vector(o.bound_box[i]))] for i in [0,6]]}))
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
camera.location=(27,-40,12)
direction=Vector((0,-2,-4))-camera.location
camera.rotation_euler=direction.to_track_quat('-Z','Y').to_euler()
camera_data.type='ORTHO'
camera_data.ortho_scale=26
scene.camera=camera
scene.render.filepath=os.path.join(os.path.dirname(bpy.data.filepath),'source-render.png')
bpy.ops.render.render(write_still=True)
