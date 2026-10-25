import bpy, json
from pathlib import Path
root=Path(__file__).resolve().parent.parent
bpy.ops.wm.open_mainfile(filepath=str(root/'artifacts/source-assets/fps-arms/FPS ARMS RIG 1.blend'))
for ob in bpy.data.objects:
    print('OBJECT',ob.name,ob.type,'loc',list(ob.location),'scale',list(ob.scale),'rotation',list(ob.rotation_euler))
    if ob.type=='MESH':
        print('MESH',len(ob.data.vertices),len(ob.data.polygons),'BOUNDS',list(ob.dimensions),'VGROUPS',[(v.index,v.name) for v in ob.vertex_groups])
        print('MATERIALS',[m.name for m in ob.data.materials])
    if ob.type=='ARMATURE':
        for b in ob.data.bones:
            print('BONE',b.name,'head',list(b.head_local),'tail',list(b.tail_local),'parent',b.parent.name if b.parent else '', 'deform',b.use_deform)
        for p in ob.pose.bones:
            if p.constraints: print('CONSTRAINTS',p.name,[(c.type,c.name) for c in p.constraints])
