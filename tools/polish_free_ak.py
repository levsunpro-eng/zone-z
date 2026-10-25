"""Optional offline edge treatment of the CC0 AK; emits the same runtime mesh format.

Run Blender --background --factory-startup --python tools/polish_free_ak.py.
UVs, separate moving parts and the author's materials are preserved.
"""
import bpy
import math
import struct
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parent.parent
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.gltf(filepath=str(ROOT / 'tools/ak-source/GLTF/AK.gltf'))
keep = {'AK', 'Bolt', 'Lock', 'Magazine.001', 'Trigger'}
parts = []
for obj in list(bpy.context.scene.objects):
    if obj.name not in keep:
        bpy.data.objects.remove(obj, do_unlink=True)
        continue
    bevel = obj.modifiers.new('Small machined edge radius', 'BEVEL')
    bevel.width = .00065
    bevel.segments = 1
    bevel.limit_method = 'ANGLE'
    bevel.angle_limit = math.radians(38)
    bevel.affect = 'EDGES'
    bevel.harden_normals = True
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=bevel.name)
    normals = obj.modifiers.new('Area weighted surface normals', 'WEIGHTED_NORMAL')
    normals.keep_sharp = True
    normals.weight = 50
    bpy.ops.object.modifier_apply(modifier=normals.name)
    mesh = obj.data
    mesh.calc_loop_triangles()
    entries = []
    indices = []
    mapping = {}
    transform = obj.matrix_world
    normal_transform = transform.to_3x3().inverted().transposed()
    for tri in mesh.loop_triangles:
        points = [transform @ mesh.vertices[v].co for v in tri.vertices]
        if (points[1]-points[0]).cross(points[2]-points[0]).length < 1e-12:
            continue
        face = []
        for loop_index in tri.loops:
            loop = mesh.loops[loop_index]
            p = transform @ mesh.vertices[loop.vertex_index].co
            n = (normal_transform @ mesh.corner_normals[loop_index].vector).normalized()
            if n.length < .5:
                n = (normal_transform @ tri.normal).normalized()
            uv = mesh.uv_layers.active.data[loop_index].uv
            # Blender +X forward, +Z up, -Y right => Unity +Z forward, +Y up, +X right.
            row = (-p.y, p.z-.02, p.x-.06, -n.y, n.z, n.x, uv.x, uv.y)
            key = tuple(round(v, 7) for v in row)
            if key not in mapping:
                mapping[key] = len(entries)
                entries.append(row)
            face.append(mapping[key])
        # Mapping is a reflection, so reverse the winding.
        indices += [face[0], face[2], face[1]]
    parts.append(('Magazine' if obj.name == 'Magazine.001' else obj.name, entries, indices))

out = ROOT / 'UnityProject/Assets/ZoneZ/Resources/Models/FreeAK/AK.bytes'
with out.open('wb') as f:
    f.write(struct.pack('<iii', 0x5A414B31, 1, len(parts)))
    for name, vertices, indices in parts:
        encoded = name.encode('utf8')
        f.write(struct.pack('<i', len(encoded)))
        f.write(encoded)
        f.write(struct.pack('<ii', len(vertices), len(indices)))
        for vertex in vertices: f.write(struct.pack('<8f', *vertex))
        f.write(struct.pack('<' + 'i'*len(indices), *indices))
print('POLISHED_AK', [(name, len(v), len(i)//3) for name,v,i in parts], 'TOTAL', sum(len(i)//3 for _,_,i in parts))

# Keep an editable original-derived source file outside the shipped Resources folder.
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT / 'tools/ak-source/ZoneZ-AK-polished.blend'))
