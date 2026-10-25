import bpy,json
from pathlib import Path
root=Path(__file__).resolve().parent.parent
src=root/'tools/quaternius-guns/Ultimate Gun Pack - July 2019/Blends'
out={}
for path in src.glob('*.blend'):
    bpy.ops.wm.open_mainfile(filepath=str(path))
    out[path.stem]=[dict(name=o.name,type=o.type,location=list(o.location),dimensions=list(o.dimensions),materials=[m.name for m in o.data.materials],vertices=len(o.data.vertices)) for o in bpy.context.scene.objects if o.type=='MESH']
(root/'tools/quaternius-guns/inspection.json').write_text(json.dumps(out,indent=2))
print(json.dumps(out))
