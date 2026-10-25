"""Keep the exact URP PBR shader variants in Player builds through serialized Resources materials."""
from pathlib import Path
import re
import uuid
root=Path(__file__).resolve().parent.parent
assets=root/'UnityProject/Assets/ZoneZ'
template=(assets/'Resources/ShaderSupport/FoliageCutout.mat').read_text()
out=assets/'Resources/Models/FreeAK'
for name, keywords in [('ImportedWeaponPbr',['_NORMALMAP','_OCCLUSIONMAP','_METALLICSPECGLOSSMAP']),('ImportedSurfacePbr',['_NORMALMAP','_OCCLUSIONMAP'])]:
    text=template.replace('m_Name: FoliageCutout','m_Name: '+name)
    text=text.replace('  - _ALPHATEST_ON','\n'.join('  - '+k for k in keywords))
    text=text.replace('m_CustomRenderQueue: 2450','m_CustomRenderQueue: -1').replace('RenderType: TransparentCutout','RenderType: Opaque')
    text=text.replace('_AlphaClip: 1','_AlphaClip: 0').replace('_AlphaToMask: 1','_AlphaToMask: 0').replace('_Cull: 0','_Cull: 2')
    text=text.replace('m_EnableInstancingVariants: 0','m_EnableInstancingVariants: 1')
    for prop,texture in [('_BaseMap','Body_Albedo'),('_MainTex','Body_Albedo'),('_BumpMap','Body_Normal'),('_OcclusionMap','Body_AO'),('_MetallicGlossMap','Body_MetallicSmoothness')]:
        guid=re.search(r'guid: (\w+)',(out/(texture+'.png.meta')).read_text()).group(1)
        reference='{fileID: 2800000, guid: '+guid+', type: 3}'
        if name=='ImportedSurfacePbr' and prop=='_MetallicGlossMap': reference='{fileID: 0}'
        text=re.sub(r'(- '+re.escape(prop)+r':\n        m_Texture: )[^\n]+',lambda m:m.group(1)+reference,text)
    (out/(name+'.mat')).write_text(text)
    (out/(name+'.mat.meta')).write_text('fileFormatVersion: 2\nguid: '+uuid.uuid5(uuid.NAMESPACE_URL,'zonez-free-ak/'+name).hex+'\nNativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 2100000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n')
for name in ['ImportedWeaponModels.cs']:
    (assets/'Scripts/World'/(name+'.meta')).write_text('fileFormatVersion: 2\nguid: '+uuid.uuid5(uuid.NAMESPACE_URL,'zonez-free-ak/'+name).hex+'\n')
