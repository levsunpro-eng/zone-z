import bpy,json
from pathlib import Path
from mathutils import Vector
root=Path(__file__).resolve().parent.parent
src=root/'tools/quaternius-guns/Ultimate Gun Pack - July 2019/Blends'
out={}
for path in src.glob('*.blend'):
 bpy.ops.wm.open_mainfile(filepath=str(path))
 o=next(o for o in bpy.context.scene.objects if o.type=='MESH'); mesh=o.data
 parents=list(range(len(mesh.vertices)))
 def find(i):
  while parents[i]!=i: i=parents[i]
  return i
 for edge in mesh.edges:
  a,b=map(find,edge.vertices); parents[b]=a
 groups={}
 for vertex in mesh.vertices: groups.setdefault(find(vertex.index),[]).append(o.matrix_world@vertex.co)
 comps=[]
 for ci,points in groups.items():
  lo=[min(p[i] for p in points) for i in range(3)]; hi=[max(p[i] for p in points) for i in range(3)]
  mats=sorted(set(mesh.materials[p.material_index].name for p in mesh.polygons if find(p.vertices[0])==ci))
  comps.append(dict(index=ci,count=len(points),lo=lo,hi=hi,mats=mats))
 out[path.stem]=comps
(root/'tools/quaternius-guns/components.json').write_text(json.dumps(out,indent=2))
