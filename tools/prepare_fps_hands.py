"""Adapt para/MakeHuman CC0 topology into wrist-local gloves with a release morph.
Run with the verified portable Blender. No game assets from other games are used.
"""
import bpy, math, bmesh, json
from mathutils import Vector, Matrix
from pathlib import Path

root=Path(__file__).resolve().parent.parent
dest=root/'UnityProject/Assets/ZoneZ/Resources/ImportedHands'
dest.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(root/'art-source/hands/FPS ARMS RIG 1.blend'))
arm=next(o for o in bpy.data.objects if o.type=='ARMATURE')
source=next(o for o in bpy.data.objects if o.type=='MESH')
arm.animation_data_clear()
for p in arm.pose.bones:
    for c in list(p.constraints):p.constraints.remove(c)
    p.rotation_mode='XYZ';p.rotation_euler=(0,0,0);p.location=(0,0,0);p.scale=(1,1,1)

def bake(side,relaxed=False):
    for p in arm.pose.bones:
        p.rotation_euler=(0,0,0)
        if p.name.endswith('.'+side):
            if p.name.startswith('f_'):
                seg=int(p.name.split('.')[1])
                degrees=([34,48,32] if relaxed else [60,76,48])[seg-1]
                if 'index' in p.name and side=='R':degrees*=.47
                p.rotation_euler.x=math.radians(degrees)
            elif p.name.startswith('thumb.'):
                seg=int(p.name.split('.')[1]);p.rotation_euler.x=math.radians([12,25,22][seg-1])
    bpy.context.view_layer.update()
    dg=bpy.context.evaluated_depsgraph_get()
    e=source.evaluated_get(dg);mesh=e.to_mesh()
    hand=arm.data.bones['hand.'+side]
    # Bake in anatomical wrist coordinates, decimeters in original MakeHuman file.
    convert=hand.matrix_local.inverted() @ arm.matrix_world.inverted() @ source.matrix_world
    points=[convert @ v.co * .10 for v in mesh.vertices]
    e.to_mesh_clear()
    return points

def material(name,color,roughness):
    m=bpy.data.materials.new(name);m.diffuse_color=(*color,1);m.use_nodes=True
    bsdf=m.node_tree.nodes.get('Principled BSDF');bsdf.inputs['Base Color'].default_value=(*color,1);bsdf.inputs['Roughness'].default_value=roughness
    return m
glove=material('ZoneZ charcoal tactical glove',(.10,.115,.10),.72)
sleeve=material('ZoneZ olive woven sleeve',(.22,.26,.18),.90)
pad=material('ZoneZ rubber reinforcement',(.04,.046,.039),.66)

for side,label in [('R','RightGrip'),('L','LeftGrip'),('L','LeftGripCompact')]:
    closed=bake(side);opened=bake(side,True)
    sideids={v.index for v in source.data.vertices if sum(g.weight for g in v.groups if source.vertex_groups[g.group].name.endswith('.'+side))>.5}
    # Forearms only: remove shoulders and upper arms from the source.
    selected={i for i in sideids if closed[i].y>-.28}
    result=[]
    for part,mat in [('Glove',glove),('Sleeve',sleeve)]:
        polys=[p for p in source.data.polygons if all(i in selected for i in p.vertices) and ((sum(closed[i].y for i in p.vertices)/len(p.vertices)>-.018)==(part=='Glove'))]
        used=sorted({i for p in polys for i in p.vertices});remap={old:i for i,old in enumerate(used)}
        verts=[];morph=[]
        for i in used:
            pos=closed[i].copy();relax=opened[i].copy()
            if part=='Sleeve':
                # Tailored shell: thicker fabric, restrained folds around the original anatomy.
                for point in (pos,relax):
                    gain=1.13+.07*math.sin(point.y*150)+.035*math.sin(point.y*285)
                    point.x*=gain;point.z*=gain
                    if side=='L' and label!='LeftGripCompact':
                        reach=max(0,min(1,(-point.y-.02)/.26))**1.4
                        point.x+=.10*reach;point.z-=.43*reach;point.y-=.05*reach
            verts.append(pos);morph.append(relax)
        mesh=bpy.data.meshes.new(label+' '+part);mesh.from_pydata(verts,[],[[remap[i] for i in p.vertices] for p in polys]);mesh.update()
        uv=mesh.uv_layers.new(name='UVMap');srcuv=source.data.uv_layers.active
        for p,newpoly in zip(polys,mesh.polygons):
            for oldloop,newloop in zip(p.loop_indices,newpoly.loop_indices):uv.data[newloop].uv=srcuv.data[oldloop].uv
        ob=bpy.data.objects.new(label+' '+part,mesh);bpy.context.collection.objects.link(ob);ob.data.materials.append(mat)
        for p in mesh.polygons:p.use_smooth=True
        ob.shape_key_add(name='Basis')
        release=ob.shape_key_add(name='Release')
        for v,co in zip(release.data,morph):v.co=co
        result.append(ob)
    # A raised cuff follows the wrist, and is visibly separate from the fabric.
    bpy.ops.mesh.primitive_torus_add(major_radius=.031,minor_radius=.004,major_segments=32,minor_segments=8,location=(0,-.012,0),rotation=(math.pi/2,0,0))
    cuff=bpy.context.object;cuff.name=label+' cuff';cuff.scale=(1.12,1,.83);cuff.data.materials.append(pad)
    for p in cuff.data.polygons:p.use_smooth=True
    result.append(cuff)
    # Store wrist-local source geometry directly to avoid importer axis/unit differences.
    parts=[]
    vec=lambda v:dict(x=round(v.x,7),y=round(v.y,7),z=round(v.z,7))
    for ob in result:
        mesh=ob.data;mesh.calc_loop_triangles()
        mat=ob.matrix_world
        verts=[vec(mat @ v.co) for v in mesh.vertices]
        normals=[vec(mat.to_3x3() @ v.normal) for v in mesh.vertices]
        delta=[vec(mat.to_3x3() @ (b.co-a.co)) for a,b in zip(mesh.vertices,mesh.shape_keys.key_blocks['Release'].data)] if mesh.shape_keys else []
        normaldelta=[]
        if mesh.shape_keys:
            relaxed=bpy.data.meshes.new('relaxed normals')
            relaxed.from_pydata([k.co for k in mesh.shape_keys.key_blocks['Release'].data],[],[list(p.vertices) for p in mesh.polygons]);relaxed.update()
            normaldelta=[vec(mat.to_3x3() @ (b.normal-a.normal)) for a,b in zip(mesh.vertices,relaxed.vertices)]
            bpy.data.meshes.remove(relaxed)
        parts.append(dict(name=ob.name,vertices=verts,normals=normals,triangles=[i for p in mesh.loop_triangles for i in p.vertices],release=delta,releaseNormals=normaldelta))
    (dest/(label+'.bytes')).write_text(json.dumps(dict(parts=parts),separators=(',',':')),encoding='utf-8')
    for ob in result:bpy.data.objects.remove(ob,do_unlink=True)
print('ZONEZ_HANDS_EXPORTED',dest)
