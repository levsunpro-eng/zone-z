import bpy, json, os
from mathutils import Vector

report = {"objects": [], "images": []}
for obj in bpy.data.objects:
    item = {"name": obj.name, "type": obj.type, "location": list(obj.location), "rotation": list(obj.rotation_euler), "scale": list(obj.scale), "dimensions": list(obj.dimensions)}
    if obj.type == 'MESH':
        item.update(vertices=len(obj.data.vertices), polygons=len(obj.data.polygons), materials=[m.name if m else None for m in obj.data.materials], modifiers=[{"name": m.name,"type": m.type} for m in obj.modifiers])
    if obj.type == 'ARMATURE':
        item['bones'] = [{"name":b.name, "parent": b.parent.name if b.parent else None, "head":list(b.head_local), "tail":list(b.tail_local)} for b in obj.data.bones]
        item['pose'] = [{"name":b.name,"rotation":list(b.rotation_quaternion),"constraints":[c.type for c in b.constraints]} for b in obj.pose.bones]
    report['objects'].append(item)
for im in bpy.data.images:
    report['images'].append({"name":im.name,"size":list(im.size),"packed":bool(im.packed_file)})
report['actions'] = [a.name for a in bpy.data.actions]
out=os.path.join(os.path.dirname(bpy.data.filepath),'inspection.json')
with open(out,'w',encoding='utf-8') as f: json.dump(report,f,indent=2)
print('INSPECTION_WRITTEN',out)
