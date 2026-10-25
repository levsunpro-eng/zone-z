using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZoneZ
{
    /// <summary>Original, collider-free equipment art. Sizes are metres; gameplay stays with the owner.</summary>
    internal static class EquipmentModels
    {
        static readonly Dictionary<string, Mesh> Meshes = new Dictionary<string, Mesh>();
        static Material steel, rubber, olive, ivory, red, gold, glass;
        static void Materials()
        {
            if (steel) return;
            steel = WorldArt.Mat("Equipment brushed steel", new Color(.19f,.22f,.23f), .8f,.42f);
            rubber = WorldArt.Mat("Equipment graphite rubber", new Color(.034f,.043f,.044f),0,.18f);
            olive = WorldArt.Mat("Equipment painted olive", new Color(.22f,.26f,.18f),.12f,.27f);
            ivory = WorldArt.Mat("Equipment ceramic", new Color(.64f,.66f,.60f),0,.31f);
            red = WorldArt.Mat("Equipment medical vermilion", new Color(.55f,.085f,.06f),.02f,.28f);
            gold = WorldArt.Mat("Equipment contact brass", new Color(.52f,.37f,.13f),.78f,.47f);
            glass = WorldArt.Mat("Equipment dark glass", new Color(.035f,.13f,.15f),.3f,.8f);
        }

        public static Transform BuildLoot(Transform parent, string id)
        {
            Materials();
            var root = WorldArt.Group(parent,"Equipment " + id);
            if (id == "medkit")
            {
                Box(root,"Rounded medical case",Vector3.zero,new Vector3(.35f,.23f,.17f),ivory,.025f);
                Box(root,"Case centre seal",new Vector3(0,0,0),new Vector3(.354f,.233f,.022f),rubber,.01f);
                Box(root,"Inset red medical panel",new Vector3(0,0,-.087f),new Vector3(.18f,.145f,.007f),red,.015f);
                Box(root,"Medical cross horizontal",new Vector3(0,0,-.093f),new Vector3(.10f,.029f,.007f),ivory,.003f);
                Box(root,"Medical cross vertical",new Vector3(0,0,-.094f),new Vector3(.029f,.10f,.007f),ivory,.003f);
                for(int sign=-1;sign<=1;sign+=2)
                {
                    Box(root,"Case latch",new Vector3(sign*.125f,.105f,0),new Vector3(.031f,.028f,.058f),steel,.005f);
                    Box(root,"Handle upright",new Vector3(sign*.057f,.136f,0),new Vector3(.022f,.058f,.029f),rubber,.007f);
                }
                Box(root,"Medical carry handle",new Vector3(0,.159f,0),new Vector3(.128f,.022f,.029f),rubber,.009f);
            }
            else if (id == "haste" || id == "vampirism")
            {
                Box(root,"Injector carrier",new Vector3(0,-.10f,.012f),new Vector3(.26f,.036f,.115f),rubber,.012f);
                for(int i=0;i<2;i++)
                {
                    var vial=WorldArt.Group(root,"Sealed injector " + i,new Vector3((i-.5f)*.117f,0,0));
                    Lathe(vial,"Injector milled cylinder",new[]{new Vector2(.025f,-.085f),new Vector2(.039f,-.075f),new Vector2(.039f,.063f),new Vector2(.033f,.08f)},steel);
                    Lathe(vial,"Metered cartridge",new[]{new Vector2(.040f,-.04f),new Vector2(.040f,.045f)},id=="haste"?gold:red);
                    Lathe(vial,"Protective cap",new[]{new Vector2(.033f,.078f),new Vector2(.031f,.12f),new Vector2(.022f,.13f)},rubber);
                    for(int k=0;k<4;k++)Box(vial,"Dose graduation",new Vector3(.014f,-.024f+k*.017f,-.041f),new Vector3(.023f,.004f,.003f),ivory,.001f);
                    Box(vial,"Cartridge observation strip",new Vector3(-.018f,.002f,-.041f),new Vector3(.012f,.064f,.004f),glass,.002f);
                }
            }
            else if (id == "fast_reload")
            {
                Box(root,"Twin magazine clamp",new Vector3(0,0,0),new Vector3(.26f,.057f,.08f),rubber,.009f);
                for(int i=0;i<2;i++)
                {
                    var magazine=WorldArt.Group(root,"Spare magazine " + i,new Vector3((i-.5f)*.126f,0,0));
                    Box(magazine,"Stamped magazine body",Vector3.zero,new Vector3(.095f,.27f,.062f),olive,.016f);
                    Box(magazine,"Magazine floorplate",new Vector3(0,-.136f,0),new Vector3(.107f,.019f,.073f),rubber,.004f);
                    for(int k=0;k<3;k++)Box(magazine,"Pressed channel",new Vector3((k-1)*.024f,0,-.032f),new Vector3(.005f,.21f,.004f),steel,.001f);
                    for(int k=0;k<2;k++)
                    {
                        var round=WorldArt.Group(magazine,"Visible cartridge",new Vector3((k-.5f)*.035f,.141f,0));
                        Lathe(round,"Cartridge",new[]{new Vector2(.014f,0),new Vector2(.014f,.035f),new Vector2(.011f,.045f),new Vector2(0,.06f)},gold,16);
                    }
                }
            }
            else if (id == "protective_field")
            {
                Box(root,"Shield battery body",Vector3.zero,new Vector3(.29f,.225f,.135f),olive,.029f);
                Box(root,"Shield armoured face",new Vector3(0,0,-.071f),new Vector3(.25f,.18f,.018f),steel,.023f);
                Box(root,"Status screen",new Vector3(0,.018f,-.083f),new Vector3(.14f,.078f,.006f),glass,.006f);
                for(int i=0;i<4;i++)Box(root,"Battery indicator",new Vector3(-.047f+i*.031f,.018f,-.088f),new Vector3(.020f,.043f,.004f),AbilityEffects.Material(new Color(.15f,.63f,.65f)),.002f);
                for(int sign=-1;sign<=1;sign+=2)
                {
                    Box(root,"Shockproof side bumper",new Vector3(sign*.139f,0,0),new Vector3(.028f,.21f,.148f),rubber,.009f);
                    LatheAt(root,"Power connector",new Vector3(sign*.07f,.121f,0),new[]{new Vector2(.021f,0),new Vector2(.021f,.026f),new Vector2(.014f,.031f)},gold);
                }
            }
            else
            {
                Box(root,"Gyro mounting foot",new Vector3(0,-.108f,0),new Vector3(.28f,.031f,.145f),rubber,.008f);
                Lathe(root,"Stabilizer housing",new[]{new Vector2(.066f,-.09f),new Vector2(.10f,-.05f),new Vector2(.10f,.063f),new Vector2(.065f,.098f)},steel);
                Ring(root,"Gyro outer cage",new Vector3(0,.01f,-.112f),.102f,.009f,olive);
                Ring(root,"Gyro instrument rim",new Vector3(0,.01f,-.087f),.065f,.007f,rubber);
                Box(root,"Gyro ceramic core",new Vector3(0,.01f,-.088f),new Vector3(.065f,.065f,.025f),ivory,.014f);
                for(int sign=-1;sign<=1;sign+=2)Box(root,"Gyro bracket",new Vector3(sign*.122f,.0f,0),new Vector3(.026f,.20f,.10f),olive,.01f);
            }
            return root;
        }

        public static Transform BuildProjectile(Transform parent, string id)
        {
            Materials();
            var root=WorldArt.Group(parent,"Equipment projectile " + id);
            if(id=="rocket_launcher")
            {
                Lathe(root,"Rocket casing",new[]{new Vector2(.030f,-.26f),new Vector2(.043f,-.21f),new Vector2(.043f,.17f),new Vector2(.052f,.21f),new Vector2(.043f,.28f),new Vector2(.022f,.35f),new Vector2(0,.385f)},olive);
                Lathe(root,"Rocket tail nozzle",new[]{new Vector2(.025f,-.275f),new Vector2(.027f,-.245f)},rubber);
                Lathe(root,"Rocket safety band",new[]{new Vector2(.044f,.105f),new Vector2(.044f,.12f)},gold);
                for(int i=0;i<4;i++)
                {
                    var fin=WorldArt.Group(root,"Tail fin " + i);fin.localRotation=Quaternion.Euler(0,i*90,0);
                    Box(fin,"Stabilizing fin",new Vector3(.047f,-.195f,0),new Vector3(.061f,.11f,.007f),steel,.003f);
                }
            }
            else
            {
                bool emp=id=="emp_grenade";
                Lathe(root,emp?"EMP shielded canister":"Fragmentation shell",emp
                    ?new[]{new Vector2(.034f,-.07f),new Vector2(.054f,-.059f),new Vector2(.054f,.062f),new Vector2(.036f,.075f)}
                    :new[]{new Vector2(.017f,-.068f),new Vector2(.045f,-.047f),new Vector2(.062f,-.009f),new Vector2(.058f,.032f),new Vector2(.038f,.061f)},emp?steel:olive);
                if(emp)
                {
                    for(int i=0;i<8;i++)
                    {
                        float angle=i*Mathf.PI/4;
                        var strip=Box(root,"EMP status strip",new Vector3(Mathf.Sin(angle)*.054f,0,Mathf.Cos(angle)*.054f),new Vector3(.011f,.078f,.004f),AbilityEffects.Material(new Color(.08f,.49f,.53f)),.002f);
                        strip.localRotation=Quaternion.Euler(0,angle*Mathf.Rad2Deg,0);
                    }
                }
                LatheAt(root,"Fuze collar",new Vector3(0,.060f,0),new[]{new Vector2(.026f,0),new Vector2(.026f,.028f),new Vector2(.018f,.036f)},steel);
                var lever=Box(root,"Safety lever",new Vector3(.028f,.039f,0),new Vector3(.015f,.114f,.025f),steel,.003f);lever.localEulerAngles=new Vector3(0,0,19);
                Ring(root,"Safety pull ring",new Vector3(-.03f,.087f,0),.027f,.003f,steel);
            }
            return root;
        }

        static Transform Box(Transform parent,string name,Vector3 position,Vector3 size,Material material,float radius)
        {
            string key="box:"+size.ToString("F4")+":"+radius.ToString("F4",System.Globalization.CultureInfo.InvariantCulture);
            if(!Meshes.TryGetValue(key,out var mesh))
            {
                const int steps=4;
                var vertices=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();var indices=new List<int>();
                Vector3 half=size*.5f,core=half-Vector3.one*Mathf.Min(radius,Mathf.Min(half.x,Mathf.Min(half.y,half.z))*.8f);
                float r=half.x-core.x;
                for(int axis=0;axis<3;axis++)for(int sign=-1;sign<=1;sign+=2)
                {
                    int u=(axis+1)%3,v=(axis+2)%3,first=vertices.Count;
                    for(int y=0;y<=steps;y++)for(int x=0;x<=steps;x++)
                    {
                        var p=Vector3.zero;p[axis]=half[axis]*sign;p[u]=Mathf.Lerp(-half[u],half[u],x/(float)steps);p[v]=Mathf.Lerp(-half[v],half[v],y/(float)steps);
                        var q=new Vector3(Mathf.Clamp(p.x,-core.x,core.x),Mathf.Clamp(p.y,-core.y,core.y),Mathf.Clamp(p.z,-core.z,core.z));
                        var n=(p-q).normalized;vertices.Add(q+n*r);normals.Add(n);uv.Add(new Vector2(x/(float)steps,y/(float)steps));
                    }
                    for(int y=0;y<steps;y++)for(int x=0;x<steps;x++)
                    {
                        int a=first+y*(steps+1)+x,b=a+1,c=a+steps+1,d=c+1;
                        if(sign>0){indices.AddRange(new[]{a,b,d,a,d,c});}else{indices.AddRange(new[]{a,d,b,a,c,d});}
                    }
                }
                mesh=new Mesh{name="Rounded equipment housing"};mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetUVs(0,uv);mesh.SetTriangles(indices,0);mesh.RecalculateTangents();mesh.RecalculateBounds();Meshes[key]=mesh;
            }
            return Render(parent,name,position,mesh,material);
        }

        static Transform LatheAt(Transform parent,string name,Vector3 position,Vector2[] profile,Material material)
        {var t=Lathe(parent,name,profile,material);t.localPosition=position;return t;}
        static Transform Lathe(Transform parent,string name,Vector2[] profile,Material material,int sides=24)
        {
            string key="lathe:"+name+":"+sides;foreach(var p in profile)key+=p.ToString("F4");
            if(!Meshes.TryGetValue(key,out var mesh))
            {
                var points=new List<Vector2>{new Vector2(0,profile[0].y)};points.AddRange(profile);points.Add(new Vector2(0,profile[profile.Length-1].y));
                var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
                for(int y=0;y<points.Count;y++)for(int x=0;x<=sides;x++)
                {float angle=x*Mathf.PI*2/sides;vertices.Add(new Vector3(Mathf.Cos(angle)*points[y].x,points[y].y,Mathf.Sin(angle)*points[y].x));uv.Add(new Vector2(x/(float)sides,y/(float)(points.Count-1)));}
                for(int y=0;y<points.Count-1;y++)for(int x=0;x<sides;x++)
                {
                    int a=y*(sides+1)+x,b=a+1,c=a+sides+1,d=c+1;
                    if(points[y].x>0)triangles.AddRange(new[]{a,c,b});if(points[y+1].x>0)triangles.AddRange(new[]{b,c,d});
                }
                mesh=new Mesh{name="Profiled equipment shell"};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();Meshes[key]=mesh;
            }
            return Render(parent,name,Vector3.zero,mesh,material);
        }

        static Transform Ring(Transform parent,string name,Vector3 position,float radius,float tube,Material material)
        {
            string key="ring:"+radius.ToString("F4")+":"+tube.ToString("F4");
            if(!Meshes.TryGetValue(key,out var mesh))
            {
                const int sides=24,section=6;var vertices=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();var indices=new List<int>();
                for(int i=0;i<=sides;i++)for(int j=0;j<=section;j++)
                {float a=i*Mathf.PI*2/sides,b=j*Mathf.PI*2/section;var n=new Vector3(Mathf.Cos(a)*Mathf.Cos(b),Mathf.Sin(a)*Mathf.Cos(b),Mathf.Sin(b));vertices.Add(new Vector3(Mathf.Cos(a)*radius,Mathf.Sin(a)*radius,0)+n*tube);normals.Add(n);uv.Add(new Vector2(i/(float)sides,j/(float)section));}
                for(int i=0;i<sides;i++)for(int j=0;j<section;j++){int a=i*(section+1)+j,b=a+section+1;indices.AddRange(new[]{a,b,a+1,a+1,b,b+1});}
                mesh=new Mesh{name="Equipment retaining ring"};mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetUVs(0,uv);mesh.SetTriangles(indices,0);mesh.RecalculateTangents();mesh.RecalculateBounds();Meshes[key]=mesh;
            }
            return Render(parent,name,position,mesh,material);
        }

        static Transform Render(Transform parent,string name,Vector3 position,Mesh mesh,Material material)
        {
            var t=WorldArt.Group(parent,name,position);t.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=t.gameObject.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.On;return t;
        }
    }
}
