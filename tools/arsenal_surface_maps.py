"""Deterministic material data: micro-normal / physical roughness, original walnut grain.
This is surface signal generation, no external images or paid texture library.
"""
from pathlib import Path
import numpy as np
from PIL import Image
import uuid
ROOT=Path(__file__).resolve().parent.parent
OUT=ROOT/'UnityProject/Assets/ZoneZ/Resources/Models/FreeArsenal'
template=(ROOT/'UnityProject/Assets/ZoneZ/Resources/Textures/worn-asphalt.png.meta').read_text()
rng=np.random.default_rng(12731)
size=256;y,x=np.mgrid[0:size,0:size];noise=rng.random((size,size)).astype(np.float32)
for kind in ('Steel','Polymer','Wood'):
 if kind=='Steel':
  height=(np.sin(x*.9+noise*.4)*.4+noise*.6)*.035
  rough=np.clip(.44+noise*.12+np.sin(x*.9)*.035,0,1);metal=np.full_like(noise,.94)
  col=np.dstack([.52+noise*.055]*3)
 elif kind=='Polymer':
  height=(np.sin(x*2)*np.sin(y*2)+noise*.4)*.028
  rough=np.clip(.70+noise*.17,0,1);metal=np.full_like(noise,.025)
  col=np.dstack([.77+noise*.10]*3)
 else:
  wave=np.sin(x*.35+np.sin(y*.026)*1.8+np.sin(y*.095)*.6)
  grain=wave*.08+np.sin(x*1.7+y*.023)*.025+noise*.035
  height=grain*.08;rough=np.clip(.67+noise*.13+grain*.18,0,1);metal=np.zeros_like(noise)
  col=np.stack([.46+grain,.29+grain*.65,.145+grain*.34],axis=-1)
 gy,gx=np.gradient(height);nx=-gx*2;ny=-gy*2;nz=np.sqrt(np.maximum(.01,1-nx*nx-ny*ny))
 normal=np.stack([nx*.5+.5,ny*.5+.5,nz*.5+.5],axis=-1)
 maps={'Albedo':np.clip(col,0,1),'Normal':normal,'MetallicSmoothness':np.stack([metal,metal,metal,1-rough],axis=-1),'AO':np.dstack([.97+noise*.03]*3)}
 for suffix,data in maps.items():
  p=OUT/(kind+'_'+suffix+'.png');Image.fromarray((data*255).astype('uint8')).save(p)
  meta=template.replace('guid: 71c84e12fe695c04d9c425c7b2706d11','guid: '+uuid.uuid5(uuid.NAMESPACE_URL,'zonez-arsenal/'+p.name).hex)
  if suffix!='Albedo':meta=meta.replace('sRGBTexture: 1','sRGBTexture: 0')
  if suffix=='Normal':meta=meta.replace('textureType: 0','textureType: 1')
  p.with_suffix('.png.meta').write_text(meta)
for p in OUT.glob('*.json'):
 p.with_suffix('.json.meta').write_text('fileFormatVersion: 2\nguid: '+uuid.uuid5(uuid.NAMESPACE_URL,'zonez-arsenal/'+p.name).hex+'\nTextScriptImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n')

# Preserve the author's atlas, replace only orange furniture with matte walnut stain and fine fibres.
ak=ROOT/'UnityProject/Assets/ZoneZ/Resources/Models/FreeAK'
src=ROOT/'tools/ak-source/Textures/AK_1'
base=np.asarray(Image.open(src/'AK_1_Albedo.png').convert('RGB')).astype(float)/255
h,w,_=base.shape;yy,xx=np.mgrid[0:h,0:w]
wood=(base[:,:,0]>base[:,:,1]*1.28)&(base[:,:,0]>base[:,:,2]*1.6)
fibre=np.sin(xx*.55+np.sin(yy*.018)*3+np.sin(yy*.083)*.4)*.06+np.sin(xx*1.8+yy*.029)*.02
variation=np.clip(base[:,:,0]/.63,.22,1.55)
walnut=np.stack([(.32+fibre)*variation,(.17+fibre*.6)*variation,(.074+fibre*.3)*variation],axis=-1)
base[wood]=walnut[wood]
Image.fromarray(np.uint8(np.clip(base,0,1)*255)).save(ak/'Body_Albedo.png')
metal=np.asarray(Image.open(src/'AK_1_Metalness.png').convert('L')).astype(float)/255
rough=np.asarray(Image.open(src/'AK_1_Roughness.png').convert('L')).astype(float)/255
rough=np.maximum(rough,np.where(wood,.72,.38));smooth=1-rough
Image.fromarray(np.uint8(np.stack([metal,metal,metal,smooth],axis=-1)*255)).save(ak/'Body_MetallicSmoothness.png')
print('Generated original steel/polymer/walnut surface signals and adapted AK furniture.')
