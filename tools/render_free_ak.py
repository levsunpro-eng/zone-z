import bpy
from pathlib import Path
from mathutils import Vector
root=Path(__file__).resolve().parent.parent
bpy.ops.wm.open_mainfile(filepath=str(root/'tools/ak-source/ZoneZ-AK-polished.blend'))
scene=bpy.context.scene
scene.render.engine='CYCLES'
scene.cycles.samples=24
scene.cycles.use_denoising=True
scene.render.resolution_x=1400
scene.render.resolution_y=800
scene.render.resolution_percentage=100
scene.world.color=(.16,.16,.16)
scene.world.use_nodes=True
scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.07,.09,.13,1)
scene.world.node_tree.nodes['Background'].inputs[1].default_value=.7
for position,power,size,color in [((.25,-.8,1.4),80,1.5,(.83,.90,1)),((.4,.9,.8),110,1.0,(1,.8,.6)),((-.7,-.4,.5),40,.8,(.66,.78,1))]:
    bpy.ops.object.light_add(type='AREA', location=position)
    obj=bpy.context.object
    obj.data.energy=power
    obj.data.shape='DISK'
    obj.data.size=size
    obj.data.color=color
    obj.rotation_euler=(Vector((.16,0,.07))-obj.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(location=(.75,-1.5,.70))
cam=bpy.context.object
cam.rotation_euler=(Vector((.16,0,.065))-cam.location).to_track_quat('-Z','Y').to_euler()
cam.data.type='ORTHO'
cam.data.ortho_scale=1.05
scene.camera=cam
scene.render.image_settings.file_format='PNG'
scene.render.filepath=str(root/'tools/ak-source/ak-preview.png')
bpy.ops.render.render(write_still=True)
