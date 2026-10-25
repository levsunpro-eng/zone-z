"""Offline geometry fitting review, not a substitute for a Unity player capture."""
import bpy, json, struct, math, sys
from pathlib import Path
from mathutils import Vector, Matrix

root=Path(__file__).resolve().parent.parent
dest=root/'artifacts/hand-fit-offline';dest.mkdir(exist_ok=True)
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
def vec(v):return Vector((v['x'],v['y'],v['z']))
def unity(v):return Vector((v[0],v[2],v[1]))
def look(forward,up):
    z=Vector(forward).normalized();x=Vector(up).cross(z).normalized();y=z.cross(x)
    return Matrix((x,y,z)).transposed()
def material(name,color):
    m=bpy.data.materials.new(name);m.diffuse_color=(*color,1);m.use_nodes=True
    node=m.node_tree.nodes.get('Principled BSDF');node.inputs['Base Color'].default_value=(*color,1);node.inputs['Roughness'].default_value=.8
    return m
glove=material('glove',(.10,.12,.095));cloth=material('cloth',(.23,.27,.18));steel=material('steel',(.16,.19,.21));grip=material('grip',(.26,.26,.20))
def mesh(name,vs,ts,mat,smooth=False):
    me=bpy.data.meshes.new(name);me.from_pydata([unity(v) for v in vs],[],[ts[i:i+3][::-1] for i in range(0,len(ts),3)]);me.update()
    ob=bpy.data.objects.new(name,me);bpy.context.collection.objects.link(ob);ob.data.materials.append(mat)
    for p in me.polygons:p.use_smooth=smooth
    return ob
def hands(name,rotation,contact,palm=(0,.105,.029),end=(.30,-.37,-.34),mirror=False):
    parts=json.loads((root/'UnityProject/Assets/ZoneZ/Resources/ImportedHands'/f'{name}.bytes').read_text())['parts']
    wrist=Vector(contact)-rotation@Vector(palm)
    for p in parts:
        vs=[vec(v) for v in p['vertices']];ts=p['triangles']
        if mirror:
            vs=[Vector((-v.x,v.y,v.z)) for v in vs];ts=sum([ts[i:i+3][::-1] for i in range(0,len(ts),3)],[])
        if 'Sleeve' in p['name']:
            endpoints=[v for v in vs if v.y<-.24];mean=sum(endpoints,Vector())/len(endpoints)
            delta=rotation.inverted()@(Vector(end)-wrist)-mean
            for v in vs:
                t=max(0,min(1,(-v.y-.025)/.235));v+=delta*t*t*(3-2*t)
        mesh(p['name'],[wrist+rotation@v for v in vs],ts,cloth if 'Sleeve' in p['name'] else glove,True)
def gun(name):
    data=(root/'UnityProject/Assets/ZoneZ/Resources/Models/FreeArsenal'/f'{name}.bytes').read_bytes();offset=0
    def read(fmt):
        nonlocal offset
        val=struct.unpack_from('<'+fmt,data,offset);offset+=struct.calcsize('<'+fmt);return val
    magic,version,count=read('iii')
    for i in range(count):
        length,=read('i');label=data[offset:offset+length].decode();offset+=length;vc,ic=read('ii')
        vs=[read('8f')[:3] for _ in range(vc)];ts=list(read('i'*ic));mesh(label,vs,ts,grip if 'Polymer' in label else steel)
gun('glock')
hands('RightGrip',look((-1,0,0),(0,.30,1)),(0,-.025,-.052))
hands('LeftGripCompact',look((1,0,0),(0,.30,1)),(-.012,-.035,-.042),(0,.101,.029),(-.28,-.35,-.32))
world=bpy.context.scene.world;world.color=(.1,.1,.1);world.use_nodes=True;world.node_tree.nodes['Background'].inputs[0].default_value=(.12,.15,.19,1)
for name,location,power,size in [('Key',(.2,-.3,.7),70,.7),('Fill',(-.5,.1,.1),40,.6)]:
    bpy.ops.object.light_add(type='AREA',location=location);o=bpy.context.object;o.name=name;o.data.energy=power;o.data.shape='DISK';o.data.size=size
    o.rotation_euler=(Vector((0,0,0))-o.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add();camera=bpy.context.object;bpy.context.scene.camera=camera;camera.data.lens=50
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=20;scene.render.resolution_x=900;scene.render.resolution_y=900;scene.render.resolution_percentage=100
for name,position,target in [('glock-rear',(.18,.10,-.62),(0,-.055,-.01)),('glock-side',(.6,.06,-.16),(0,-.04,-.005))]:
    camera.location=unity(position);camera.rotation_euler=(unity(target)-camera.location).to_track_quat('-Z','Y').to_euler()
    scene.render.filepath=str(dest/(name+'.png'));bpy.ops.render.render(write_still=True)
print('HAND_PREVIEW_DONE',dest)
