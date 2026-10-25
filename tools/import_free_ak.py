"""Import loafbrr_1's CC0 AK glTF into a small, deterministic Unity mesh resource.

Run with the bundled Python (Pillow + numpy); no Blender/Unity importer is required.
The source and its README are downloaded from OpenGameArt, not from an existing game.
"""
from pathlib import Path
import base64
import json
import struct
import uuid
import shutil
import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / 'tools/ak-source'
OUT = ROOT / 'UnityProject/Assets/ZoneZ/Resources/Models/FreeAK'
OUT.mkdir(parents=True, exist_ok=True)
gltf = json.loads((SOURCE / 'GLTF/AK.gltf').read_text())
buffers = [base64.b64decode(b['uri'].split(',')[1]) for b in gltf['buffers']]

def accessor(index):
    a = gltf['accessors'][index]
    b = gltf['bufferViews'][a['bufferView']]
    dtype = {5126: '<f4', 5123: '<u2', 5125: '<u4'}[a['componentType']]
    size = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}[a['type']]
    offset = b.get('byteOffset', 0) + a.get('byteOffset', 0)
    return np.frombuffer(buffers[b['buffer']], dtype=dtype, count=a['count'] * size, offset=offset).reshape((-1, size))

parts = []
for node in gltf['nodes']:
    if node['name'] not in ('AK', 'Bolt', 'Lock', 'Magazine.001', 'Trigger'):
        continue
    prim = gltf['meshes'][node['mesh']]['primitives'][0]
    p = accessor(prim['attributes']['POSITION']).copy()
    p += np.array(node.get('translation', [0, 0, 0]))
    n = accessor(prim['attributes']['NORMAL']).copy()
    # Source +X is forward. Reflect into Unity +Z forward, +Y up, +X right.
    p = p[:, [2, 1, 0]]
    p += np.array([0, -.020, -.060])
    n = n[:, [2, 1, 0]]
    uv = accessor(prim['attributes']['TEXCOORD_0']).copy()
    uv[:, 1] = 1 - uv[:, 1]
    indices = accessor(prim['indices']).reshape((-1, 3))[:, [0, 2, 1]]
    name = 'Magazine' if node['name'] == 'Magazine.001' else node['name']
    parts.append((name, p, n, uv, indices))

with (OUT / 'AK.bytes').open('wb') as f:
    f.write(struct.pack('<iii', 0x5A414B31, 1, len(parts)))
    for name, p, n, uv, indices in parts:
        encoded = name.encode('utf8')
        f.write(struct.pack('<i', len(encoded)))
        f.write(encoded)
        f.write(struct.pack('<ii', len(p), indices.size))
        f.write(np.concatenate((p, n, uv), axis=1).astype('<f4').tobytes())
        f.write(indices.astype('<i4').tobytes())

texture_meta_template = (ROOT / 'UnityProject/Assets/ZoneZ/Resources/Textures/worn-asphalt.png.meta').read_text()
for source_prefix, dest_prefix in [('AK_1/AK_1', 'Body'), ('Ammunition/Ammunition', 'Magazine')]:
    for source_suffix, dest_suffix in [('Albedo', 'Albedo'), ('Normal', 'Normal'), ('AO', 'AO')]:
        target = OUT / f'{dest_prefix}_{dest_suffix}.png'
        shutil.copyfile(SOURCE / 'Textures' / f'{source_prefix}_{source_suffix}.png', target)
        meta = texture_meta_template
        meta = meta.replace('guid: 71c84e12fe695c04d9c425c7b2706d11', 'guid: ' + uuid.uuid5(uuid.NAMESPACE_URL, 'zonez-free-ak/' + target.name).hex)
        if dest_suffix == 'Normal':
            meta = meta.replace('textureType: 0', 'textureType: 1')
        if dest_suffix != 'Albedo':
            meta = meta.replace('sRGBTexture: 1', 'sRGBTexture: 0')
        if dest_suffix == 'Albedo':
            meta = meta.replace('isReadable: 0', 'isReadable: 1')
        target.with_suffix('.png.meta').write_text(meta)
    metallic = Image.open(SOURCE / 'Textures' / f'{source_prefix}_Metalness.png').convert('L')
    roughness = Image.open(SOURCE / 'Textures' / f'{source_prefix}_Roughness.png').convert('L')
    smoothness = Image.eval(roughness, lambda x: 255 - x)
    target = OUT / f'{dest_prefix}_MetallicSmoothness.png'
    Image.merge('RGBA', (metallic, metallic, metallic, smoothness)).save(target)
    meta = texture_meta_template.replace('guid: 71c84e12fe695c04d9c425c7b2706d11', 'guid: ' + uuid.uuid5(uuid.NAMESPACE_URL, 'zonez-free-ak/' + target.name).hex).replace('sRGBTexture: 1', 'sRGBTexture: 0')
    target.with_suffix('.png.meta').write_text(meta)

shutil.copyfile(SOURCE / 'README.txt', OUT / 'SOURCE-LICENSE.txt')
for file in [OUT / 'AK.bytes', OUT / 'SOURCE-LICENSE.txt']:
    file.with_name(file.name + '.meta').write_text('fileFormatVersion: 2\nguid: ' + uuid.uuid5(uuid.NAMESPACE_URL, 'zonez-free-ak/' + file.name).hex + '\nTextScriptImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n')

# Flat side reference for attachment placement (a mesh render, not a supplied image edit).
im = Image.new('RGB', (1400, 560), '#24313a')
draw = ImageDraw.Draw(im)
albedos = {False: Image.open(OUT / 'Body_Albedo.png').convert('RGB'), True: Image.open(OUT / 'Magazine_Albedo.png').convert('RGB')}
tris = []
for name, p, normals, uv, indices in parts:
    texture = albedos[name == 'Magazine']
    for tri in indices:
        pts = p[tri]
        if np.mean(normals[tri], axis=0)[0] < -.1: continue
        u, v = np.mean(uv[tri], axis=0)
        color = texture.getpixel((int(u * (texture.width - 1)), int((1-v) * (texture.height - 1))))
        light = .65 + .35 * max(0, float(np.mean(normals[tri], axis=0)[0]))
        color = tuple(int(c * light) for c in color)
        tris.append((float(pts[:, 0].mean()), [(float(q[2]) * 1400 + 530, 235 - float(q[1]) * 1400) for q in pts], color))
for _, pts, color in sorted(tris, key=lambda t:t[0]): draw.polygon(pts, fill=color)
draw.text((30, 20), 'CC0 AK / loafbrr_1 | converted source geometry + PBR albedo', fill='white')
draw.text((30, 530), f'{sum(len(x[4]) for x in parts)} triangles, separate magazine / bolt / selector / trigger', fill='white')
im.save(ROOT / 'tools/ak-source/side-reference.png')
print('Imported:', [(name, len(p), len(tri)) for name,p,n,uv,tri in parts])
