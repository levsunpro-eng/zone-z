import bpy, os, json
base=os.path.dirname(bpy.context.preferences.filepaths.temporary_directory) if False else os.path.dirname(__file__)
game=os.path.dirname(os.path.dirname(base))
path=os.path.join(game,'UnityProject','Assets','ZoneZ','Resources','Art','Characters','Operator.fbx')
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=path)
rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
required=['Cabeza','Pecho','Cuello','Brazo.L','Antebrazo.L','HuesoMuneca.L','Brazo.R','Antebrazo.R','HuesoMuneca.R','Muslo.L','Espinilla.L','Tarso.L','Muslo.R','Espinilla.R','Tarso.R']
missing=[n for n in required if n not in rig.data.bones]
unweighted=sum(1 for o in meshes for v in o.data.vertices if sum(g.weight for g in v.groups)<.001)
tris=0
for o in meshes:o.data.calc_loop_triangles();tris+=len(o.data.loop_triangles)
print('FBX_CHECK',json.dumps({'missing_bones':missing,'mesh_count':len(meshes),'triangles':tris,'unweighted_vertices':unweighted,'materials':[[m.name for m in o.data.materials] for o in meshes],'dimensions':[list(o.dimensions) for o in meshes]}))
assert not missing
assert len(meshes)==1 and tris<25000 and unweighted==0
