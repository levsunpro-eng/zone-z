using System.Collections.Generic;
using UnityEngine;

namespace ZoneZ
{
    internal enum OperatorSurface { Fabric, Armor, Rubber, Skin, Hardware }

    /// <summary>Original restrained material detail shared by operators and first-person hands.</summary>
    internal static class OperatorMaterials
    {
        static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();
        sealed class Surface { public Texture2D Albedo, Normal, MetallicSmoothness; }
        static readonly Dictionary<OperatorSurface, Surface> Surfaces = new Dictionary<OperatorSurface, Surface>();

        public static Material Get(string importedName, int team)
        {
            bool fabric = importedName.Contains("Fabric"), armor = importedName.Contains("Armor"), rubber = importedName.Contains("Rubber"), skin = importedName.Contains("Skin"), glass = importedName.Contains("Visor"), marker = importedName.Contains("Team");
            string key = importedName + (fabric || marker ? team.ToString() : "");
            if (Cache.TryGetValue(key, out var cached) && cached) return cached;
            Color color = fabric ? (team == 0 ? new Color(.20f,.30f,.36f) : new Color(.42f,.32f,.22f)) :
                armor ? new Color(.072f,.086f,.090f) : rubber ? new Color(.036f,.042f,.045f) : skin ? new Color(.53f,.35f,.25f) :
                glass ? new Color(.065f,.19f,.22f) : marker ? (team == 0 ? new Color(.26f,.67f,.82f) : new Color(.88f,.45f,.16f)) : new Color(.27f,.29f,.28f);
            var mat = WorldArt.NewMaterial(key, color, glass ? .45f : 0, glass ? .87f : .22f);
            if (!glass)
            {
                var type=fabric||marker?OperatorSurface.Fabric:armor?OperatorSurface.Armor:rubber?OperatorSurface.Rubber:skin?OperatorSurface.Skin:OperatorSurface.Hardware;
                ApplySurface(mat,type,fabric||marker?14:skin?7:9);
            }
            Cache[key] = mat; return mat;
        }

        public static void ApplySurface(Material material,OperatorSurface type,float repeats)
        {
            if(!Surfaces.TryGetValue(type,out var surface) || !surface.Albedo) { surface=MakeSurface(type);Surfaces[type]=surface; }
            material.mainTexture=surface.Albedo; material.mainTextureScale=Vector2.one*repeats;
            material.SetTexture("_BumpMap",surface.Normal); material.SetTextureScale("_BumpMap",Vector2.one*repeats);
            material.SetFloat("_BumpScale",type==OperatorSurface.Fabric?.12f:type==OperatorSurface.Skin?.06f:.085f);
            material.EnableKeyword("_NORMALMAP");
            material.SetTexture("_MetallicGlossMap",surface.MetallicSmoothness); material.SetTextureScale("_MetallicGlossMap",Vector2.one*repeats);
            // Keep the scalar matte even if a player shader variant loses its maps.
            // Map alpha only modulates this value; it must not contain roughness twice.
            material.SetFloat("_Smoothness",Smoothness(type)); material.SetFloat("_Glossiness",Smoothness(type));
            material.SetFloat("_Metallic",Metallic(type)); material.EnableKeyword("_METALLICSPECGLOSSMAP");
            // This exact NORMAL + METALLIC + AO combination is retained by the
            // serialized Resources/Models/FreeAK/ImportedWeaponPbr material.
            material.SetTexture("_OcclusionMap",Texture2D.whiteTexture);
            material.SetFloat("_OcclusionStrength",0); material.EnableKeyword("_OCCLUSIONMAP");
        }

        static Surface MakeSurface(OperatorSurface type)
        {
            const int size=256;
            var albedo=new Color[size*size];var normals=new Color[size*size];var roughness=new Color[size*size];
            float metal=Metallic(type);
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float noise=Grain(x,y),grain=Grain(x/3,y/3);
                // A neutral low-contrast albedo prevents the old large checker pattern.
                float value=.974f+noise*.018f+grain*.008f;
                if(type==OperatorSurface.Skin)value=.982f+noise*.012f;
                albedo[y*size+x]=new Color(value,value,value,1);
                float dx=Height(x+1,y,type)-Height(x-1,y,type),dy=Height(x,y+1,type)-Height(x,y-1,type);
                Vector3 n=new Vector3(-dx,-dy,1).normalized;
                normals[y*size+x]=new Color(n.x*.5f+.5f,n.y*.5f+.5f,n.z*.5f+.5f,1);
                roughness[y*size+x]=new Color(metal,0,0,Mathf.Clamp01(.96f+noise*.05f+grain*.025f));
            }
            return new Surface { Albedo=Texture("Operator "+type+" micro albedo",albedo,false), Normal=Texture("Operator "+type+" micro normal",normals,true), MetallicSmoothness=Texture("Operator "+type+" surface response",roughness,true) };
        }

        static float Smoothness(OperatorSurface type) => type==OperatorSurface.Fabric?.16f:type==OperatorSurface.Armor?.30f:type==OperatorSurface.Rubber?.20f:type==OperatorSurface.Skin?.27f:.48f;
        static float Metallic(OperatorSurface type) => type==OperatorSurface.Hardware?.72f:type==OperatorSurface.Armor?.055f:0;

        static float Height(int x,int y,OperatorSurface type)
        {
            x=(x+256)%256;y=(y+256)%256;
            if(type==OperatorSurface.Fabric)return .12f*(Mathf.Sin(x*Mathf.PI*.5f)+Mathf.Sin(y*Mathf.PI*.5f))+.025f*Grain(x,y);
            return Grain(x,y)*(type==OperatorSurface.Skin?.035f:type==OperatorSurface.Armor?.06f:.10f);
        }

        static float Grain(int x,int y)
        {
            unchecked { uint h=(uint)(x*374761393+y*668265263+1376312589);h=(h^(h>>13))*1274126177;return ((h^(h>>16))&65535)/65535f-.5f; }
        }

        static Texture2D Texture(string name,Color[] pixels,bool linear)
        {
            var texture=new Texture2D(256,256,TextureFormat.RGBA32,true,linear){name=name,wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Trilinear,anisoLevel=8};
            texture.SetPixels(pixels);texture.Apply(true,true);return texture;
        }
    }
}
