"""Adapt Quaternius CC0 geometry to Zone Z, preserving separate moving components.
Blender 4.5: --background --factory-startup --python tools/import_free_arsenal.py.
These are game-specific interpretations, not claims of exact real-world replicas.
"""
import bpy,struct,json,uuid,math
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parent.parent
SRC=ROOT/'tools/quaternius-guns/Ultimate Gun Pack - July 2019/Blends'
OUT=ROOT/'UnityProject/Assets/ZoneZ/Resources/Models/FreeArsenal'
OUT.mkdir(parents=True,exist_ok=True)
# id: source, length in metres, physical width, animated source component index
CAT={
'glock':('Pistol_5',.202,.034,-1), 'hkp2000':('Pistol_1',.192,.034,-1),
'usp_silencer':('Pistol_2',.220,.038,-1), 'p250':('Pistol_4',.183,.034,-1),
'fiveseven':('Pistol_6',.226,.036,-1), 'cz75a':('Pistol_3',.216,.035,-1),
'elite':('Pistol_1',.218,.032,-1), 'deagle':('Pistol_6',.269,.046,-1),
'revolver':('Revolver_5',.320,.054,513),
'mac10':('SubmachineGun_4',.390,.063,264), 'mp9':('SubmachineGun_1',.430,.051,175),
'mp7':('SubmachineGun_3',.620,.051,269), 'mp5sd':('SubmachineGun_2',.690,.056,249),
'ump45':('SubmachineGun_5',.680,.061,405),
'm4a1':('AssaultRifle2_1',.860,.068,259), 'm4a1_silencer':('AssaultRifle2_2',.830,.061,57),
'famas':('Bullpup_3',.760,.070,138), 'galilar':('AssaultRifle_4',.840,.063,402),
'aug':('Bullpup_1',.810,.071,1019), 'sg556':('AssaultRifle2_2',.900,.070,57),
'ssg08':('SniperRifle_3',1.040,.069,-1), 'awp':('SniperRifle_1',1.160,.074,-1),
'g3sg1':('SniperRifle_2',1.075,.066,106), 'scar20':('SniperRifle_5',1.070,.078,-1),
'nova':('Shotgun_1',1.060,.059,621), 'xm1014':('Shotgun_3',.970,.066,-1),
'sawedoff':('Shotgun_ShortStock',.590,.055,685),
}
report=[]
for wid,(source,length,width,animated) in CAT.items():
 bpy.ops.wm.open_mainfile(filepath=str(SRC/(source+'.blend')))
 obj=next(o for o in bpy.context.scene.objects if o.type=='MESH')
 mesh=obj.data
 parents=list(range(len(mesh.vertices)))
 def find(i):
  while parents[i]!=i:i=parents[i]
  return i
 for edge in mesh.edges:
  a,b=map(find,edge.vertices); parents[b]=a
 groups={}
 for v in mesh.vertices: groups.setdefault(find(v.index),[]).append(obj.matrix_world@v.co)
 comps={i:([min(v[d] for v in vs) for d in range(3)],[max(v[d] for v in vs) for d in range(3)]) for i,vs in groups.items()}
 allv=[obj.matrix_world@v.co for v in mesh.vertices]
 lo=[min(v[d] for v in allv) for d in range(3)];hi=[max(v[d] for v in allv) for d in range(3)]
 scale=length/(hi[0]-lo[0]);sx=width/(hi[1]-lo[1])
 pistol=source.startswith('Pistol') or source.startswith('Revolver')
 muzzle_y=.077 if pistol else .105
 at_end=[v.z for v in allv if v.x>hi[0]-.0005]
 barrel_z=(max(at_end)+min(at_end))*.5
 offset=muzzle_y-barrel_z*scale
 # Distinct broad-profile Desert Eagle slide, compact P250 grip and FiveSeven extended polymer nose.
 if wid=='deagle':
  for v in mesh.vertices:
   if v.co.z>.38:v.co.z=.38+(v.co.z-.38)*1.15
 if wid=='p250':
  for v in mesh.vertices:
   if v.co.z<0:v.co.z*=.78
 role_mats={}
 new_mats=[]
 for poly in mesh.polygons:
  comp=find(poly.vertices[0]);clo,chi=comps[comp];mat=mesh.materials[poly.material_index].name
  role='Static'
  if comp==animated:role='Pump' if source.startswith('Shotgun') else 'Magazine'
  if source.startswith('Pistol') and clo[2]>.35 and not(clo[0]>1 and len(groups[comp])>30 and chi[0]-clo[0]<.5):role='Slide'
  # Physical surface assignment replaces source cartoon palette.
  kind='Steel'
  if mat in ('Wood','DarkWood'):kind='Accent'
  elif mat in ('Black','Black2','MainDark'):kind='Polymer'
  elif mat in ('Green','Main','Grey'):kind='Paint'
  elif mat in ('Metal','LightMetal','MainLight'):kind='Steel' if mat=='LightMetal' else 'Paint'
  if mat=='Glass':kind='Glass'
  if source.startswith('Pistol') and role=='Slide':kind='Paint'
  if role=='Magazine':kind='Polymer'
  name=role+'|'+kind
  if name not in role_mats:
   role_mats[name]=len(new_mats);m=bpy.data.materials.new(name);new_mats.append(m)
  poly.material_index=role_mats[name]
 mesh.materials.clear()
 for m in new_mats:mesh.materials.append(m)
 bpy.context.view_layer.objects.active=obj
 bevel=obj.modifiers.new('Machined edge radius','BEVEL');bevel.width=.0006/scale;bevel.segments=1;bevel.limit_method='ANGLE';bevel.angle_limit=math.radians(40);bevel.harden_normals=True
 bpy.ops.object.modifier_apply(modifier=bevel.name)
 normals=obj.modifiers.new('Weighted normals','WEIGHTED_NORMAL');normals.keep_sharp=True;normals.weight=50
 bpy.ops.object.modifier_apply(modifier=normals.name)
 mesh=obj.data;mesh.calc_loop_triangles();parts={}
 for tri in mesh.loop_triangles:
  name=mesh.materials[tri.material_index].name
  positions=[obj.matrix_world@mesh.vertices[v].co for v in tri.vertices]
  if (positions[1]-positions[0]).cross(positions[2]-positions[0]).length<1e-9:continue
  part=parts.setdefault(name,dict(vertices=[],indices=[],mapping={}))
  face=[]
  for li in tri.loops:
   loop=mesh.loops[li];p=obj.matrix_world@mesh.vertices[loop.vertex_index].co;n=mesh.corner_normals[li].vector.copy()
   if n.length<.5:n=tri.normal.copy()
   n=Vector((-n.y/sx,n.z/scale,n.x/scale)).normalized()
   pos=Vector((-p.y*sx,p.z*scale+offset,p.x*scale-.052))
   # Planar metre-scale UVs give consistent fine surface grain across every model.
   if abs(n.x)>.5:uv=(pos.z*7,pos.y*7)
   elif abs(n.y)>.5:uv=(pos.z*7,pos.x*7)
   else:uv=(pos.x*7,pos.y*7)
   row=tuple(pos)+tuple(n)+uv;key=tuple(round(v,7) for v in row)
   if key not in part['mapping']:part['mapping'][key]=len(part['vertices']);part['vertices'].append(row)
   face.append(part['mapping'][key])
  part['indices']+= [face[0],face[2],face[1]]
 out=OUT/(wid+'.bytes')
 with out.open('wb') as f:
  f.write(struct.pack('<iii',0x5A415231,1,len(parts)))
  for name,p in parts.items():
   b=name.encode();f.write(struct.pack('<i',len(b)));f.write(b);f.write(struct.pack('<ii',len(p['vertices']),len(p['indices'])))
   for v in p['vertices']:f.write(struct.pack('<8f',*v))
   f.write(struct.pack('<'+'i'*len(p['indices']),*p['indices']))
 out.with_suffix('.bytes.meta').write_text('fileFormatVersion: 2\nguid: '+uuid.uuid5(uuid.NAMESPACE_URL,'zonez-free-arsenal/'+wid).hex+'\nTextScriptImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n')
 triangles=sum(len(p['indices'])//3 for p in parts.values())
 item=dict(id='weapon_'+wid,source=source,triangles=triangles,muzzleZ=hi[0]*scale-.052,muzzleY=muzzle_y,minimumZ=lo[0]*scale-.052,bodyWidth=width,triggerGrip=[0,-.025 if pistol else -.02,-.052],supportGrip=[0,-.01 if pistol else .035,-.052 if pistol else .22])
 report.append(item)
 print('ARSENAL',wid,triangles)
(OUT/'manifest.json').write_text(json.dumps(dict(models=report),indent=2))
(ROOT/'tools/quaternius-guns/adapted-manifest.json').write_text(json.dumps(report,indent=2))
print('COMPLETE',len(report),'weapons; MAX',max(i['triangles'] for i in report))
