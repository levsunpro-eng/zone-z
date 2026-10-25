using System.Collections.Generic;
using UnityEngine;

namespace ZoneZ
{
    public static partial class WeaponModelFactory
    {
        static bool BuildFreeArsenal(Transform root, WeaponPresentation view, string id, Material paint, Material grip, Material accent, Material steel, bool silenced)
        {
            if (!ImportedArsenalModels.Build(root,view,id,paint,grip,accent,steel)) return false;
            bool pistol = id=="weapon_glock"||id=="weapon_hkp2000"||id=="weapon_usp_silencer"||id=="weapon_p250"||id=="weapon_fiveseven"||id=="weapon_cz75a"||id=="weapon_elite"||id=="weapon_deagle";
            if(!view.Magazine && !view.SingleShellReload)
            {
                view.Magazine=WorldArt.Group(root,"Authored detachable magazine",pistol?new Vector3(0,-.055f,-.05f):new Vector3(0,-.03f,.13f));
                B(view.Magazine,"Magazine steel body",new Vector3(0,.025f,0),pistol?new Vector3(.023f,.058f,.036f):new Vector3(.035f,.078f,.08f),grip);
                B(view.Magazine,"Magazine floorplate",new Vector3(0,-.012f,0),pistol?new Vector3(.032f,.009f,.043f):new Vector3(.043f,.012f,.088f),steel);
            }
            if(id=="weapon_revolver") view.MagazineRemoval=new Vector3(-.08f,0,0);
            if(silenced)
            {
                float z=view.Muzzle.localPosition.z,y=view.Muzzle.localPosition.y,length=pistol?.18f:.20f;
                view.BareMuzzleZ=z;view.Suppressor=WorldArt.Group(root,"Service suppressor",new Vector3(0,y,z+length*.5f));
                Barrel(view.Suppressor,"Suppressor tube",0,length,pistol?.023f:.027f,0,steel);
                Barrel(view.Suppressor,"Suppressor front collar",length*.45f,.012f,pistol?.024f:.028f,0,steel);
                view.Muzzle.localPosition=new Vector3(0,y,z+length);view.Length+=length;
            }
            if(id=="weapon_mp5sd")
            {
                float z=view.Muzzle.localPosition.z;
                Barrel(root,"MP5SD integral baffled shroud",z-.015f,.23f,.032f,view.Muzzle.localPosition.y,steel);
                view.Muzzle.localPosition+=Vector3.forward*.102f;
            }
            if(id=="weapon_elite")
            {
                var other=WorldArt.Group(root,"Left paired pistol",new Vector3(-.19f,-.018f,-.035f),new Vector3(0,-4,0));
                foreach(var filter in root.GetComponentsInChildren<MeshFilter>())
                {
                    if(filter.transform.IsChildOf(other)) continue;
                    var renderer=filter.GetComponent<MeshRenderer>();if(!renderer)continue;
                    var copy=WorldArt.Group(other,"Paired "+filter.name,root.InverseTransformPoint(filter.transform.position));
                    copy.localRotation=Quaternion.Inverse(root.rotation)*filter.transform.rotation;
                    copy.gameObject.AddComponent<MeshFilter>().sharedMesh=filter.sharedMesh;
                    copy.gameObject.AddComponent<MeshRenderer>().sharedMaterial=renderer.sharedMaterial;
                }
            }
            switch(id)
            {
                case "weapon_glock":
                    P(root,"Glock polygonal dust cover",new Vector3(0,.017f,.035f),.032f,new[]{V(-.047f,-.012f),V(.071f,-.012f),V(.082f,.003f),V(.077f,.018f),V(-.047f,.018f)},grip);break;
                case "weapon_hkp2000":
                    B(root,"P2000 ambidextrous decocker",new Vector3(-.019f,.018f,-.039f),new Vector3(.005f,.009f,.032f),accent);break;
                case "weapon_p250":
                    B(root,"P250 compact backstrap",new Vector3(0,-.025f,-.075f),new Vector3(.029f,.062f,.014f),grip,new Vector3(-13,0,0));break;
                case "weapon_fiveseven":
                    P(root,"FiveSeven reinforced polymer nose",new Vector3(0,.025f,.095f),.033f,new[]{V(-.045f,-.016f),V(.058f,-.016f),V(.067f,.013f),V(.059f,.024f),V(-.045f,.019f)},grip);break;
                case "weapon_cz75a":
                    P(root,"CZ front spare magazine",new Vector3(0,-.029f,.092f),.031f,new[]{V(-.02f,-.05f),V(.017f,-.05f),V(.023f,.037f),V(-.017f,.037f)},grip);break;
                case "weapon_deagle":
                    P(root,"Deagle angular barrel shroud",new Vector3(0,.058f,.105f),.037f,new[]{V(-.055f,-.011f),V(.068f,-.011f),V(.070f,.018f),V(.050f,.03f),V(-.055f,.026f)},paint);break;
                case "weapon_m4a1":
                case "weapon_famas":
                    float y=id=="weapon_famas"?.22f:.205f;
                    P(root,"Raised carry sight bridge",new Vector3(0,y,0),.032f,new[]{V(-.13f,-.014f),V(.135f,-.014f),V(.12f,.020f),V(-.09f,.027f),V(-.13f,.013f)},steel);
                    B(root,"Rear carry sight pedestal",new Vector3(0,y-.043f,-.10f),new Vector3(.035f,.060f,.028f),steel);
                    B(root,"Forward carry sight pedestal",new Vector3(0,y-.043f,.11f),new Vector3(.032f,.060f,.024f),steel);break;
                case "weapon_aug":
                    CompactOptic(root,steel,.235f);break;
                case "weapon_sg556":
                    CompactOptic(root,steel,.240f);
                    P(root,"SG sculpted folding-stock cheek rest",new Vector3(0,.075f,-.235f),.053f,new[]{V(-.09f,-.027f),V(.08f,-.01f),V(.09f,.035f),V(-.05f,.04f),V(-.09f,.02f)},grip);
                    for(int i=0;i<5;i++)B(root,"SG longitudinal vent",new Vector3(-.036f,.076f,.13f+i*.025f),new Vector3(.002f,.018f,.016f),steel);break;
                case "weapon_galilar":
                    B(root,"Galil handguard heat shield",new Vector3(0,.056f,.265f),new Vector3(.065f,.047f,.19f),grip);
                    for(int i=0;i<5;i++) B(root,"Galil pressed vent",new Vector3(-.034f,.07f,.19f+i*.032f),new Vector3(.002f,.017f,.019f),steel);break;
                case "weapon_g3sg1":
                    Grip(root,grip,new Vector3(0,-.042f,-.018f),.13f,-16);
                    B(root,"G3 claw scope mounting block",new Vector3(0,.155f,.075f),new Vector3(.052f,.039f,.15f),steel);
                    if(view.Magazine) view.Magazine.localScale=new Vector3(1,1.75f,1);break;
                case "weapon_scar20":
                    P(root,"SCAR adjustable cheek riser",new Vector3(0,.094f,-.205f),.061f,new[]{V(-.12f,-.035f),V(.055f,-.018f),V(.070f,.027f),V(-.10f,.037f)},grip);
                    for(int i=0;i<6;i++)B(root,"SCAR side cooling slot",new Vector3(-.040f,.070f,.18f+i*.032f),new Vector3(.002f,.016f,.017f),steel);break;
                case "weapon_mp9":
                    P(root,"MP9 curved vertical foregrip",new Vector3(0,-.006f,.154f),.030f,new[]{V(-.018f,-.061f),V(.013f,-.065f),V(.024f,.033f),V(-.022f,.033f)},grip);break;
                case "weapon_mac10":
                    B(root,"MAC wire shoulder rail",new Vector3(-.028f,.056f,-.14f),new Vector3(.007f,.007f,.20f),steel);
                    B(root,"MAC wire shoulder end",new Vector3(0,.025f,-.24f),new Vector3(.063f,.061f,.009f),steel);break;
            }
            // Small skin-C controls remain visible even on source models that contain no timber.
            float side=ImportedArsenalModels.GetSpec(id).bodyWidth*.5f+.001f;
            WorldArt.Cylinder(root,"Machined selector pin",new Vector3(-side,.025f,-.005f),.0035f,.002f,accent,false,"metal",new Vector3(0,0,90));
            return true;
        }

        static void CompactOptic(Transform r,Material steel,float y)
        {
            B(r,"Optic rear mount",new Vector3(0,y-.046f,-.055f),new Vector3(.032f,.051f,.025f),steel);
            B(r,"Optic front mount",new Vector3(0,y-.046f,.065f),new Vector3(.032f,.051f,.025f),steel);
            Barrel(r,"Compact optic tube",.010f,.18f,.022f,y,steel);
            Barrel(r,"Optic eyepiece collar",-.08f,.027f,.027f,y,steel);
            Barrel(r,"Coated optic objective",.104f,.003f,.019f,y,WorldArt.Mat("Arsenal blue glass",new Color(.025f,.11f,.13f),.45f,.94f));
        }

        static void UpgradeAuthoredArsenal(Transform root,WeaponPresentation view,string id,Material a,Material b,Material c,Material steel)
        {
            if(id=="weapon_tec9")
            {
                Remove(root,"Pistol frame");Remove(root,"Reciprocating slide");
                view.Slide=B(root,"Tec9 charging bolt",new Vector3(.035f,.093f,-.014f),new Vector3(.023f,.012f,.022f),steel);
                for(int side=-1;side<=1;side+=2) for(int i=0;i<6;i++)
                    WorldArt.Cylinder(root,"Tec9 jacket perforation",new Vector3(side*.0325f,.071f,.04f+i*.021f),.008f,.002f,steel,false,"metal",new Vector3(0,0,90));
                P(root,"Tec9 formed lower receiver",new Vector3(0,.008f,.010f),.043f,new[]{V(-.09f,-.025f),V(.10f,-.025f),V(.14f,.017f),V(.12f,.029f),V(-.10f,.029f)},a);
            }
            else if(id=="weapon_p90")
            {
                Remove(root,"SMG receiver");Remove(root,"P90 bullpup stock");Remove(root,"P90 foregrip");
                P(root,"P90 curved upper shell",Vector3.zero,.084f,new[]{V(-.245f,.12f),V(.22f,.12f),V(.25f,.084f),V(.20f,.046f),V(-.15f,.031f),V(-.24f,.013f)},a);
                P(root,"P90 rounded shoulder shell",Vector3.zero,.077f,new[]{V(-.245f,.112f),V(-.17f,.10f),V(-.15f,-.105f),V(-.21f,-.115f),V(-.25f,-.067f)},b);
                P(root,"P90 lower thumbhole bridge",Vector3.zero,.061f,new[]{V(-.19f,-.115f),V(.04f,-.094f),V(.14f,-.061f),V(.11f,-.033f),V(.03f,-.059f),V(-.18f,-.078f)},a);
                P(root,"P90 ergonomic forward loop",Vector3.zero,.061f,new[]{V(.105f,-.044f),V(.18f,-.07f),V(.225f,-.014f),V(.237f,.078f),V(.193f,.078f),V(.175f,-.026f)},b);
                B(root,"P90 translucent magazine central strip",new Vector3(0,.16f,.012f),new Vector3(.055f,.009f,.31f),c);
            }
            else if(id=="weapon_bizon")
            {
                P(root,"Bizon shaped receiver cover",new Vector3(0,.12f,.015f),.063f,new[]{V(-.14f,-.024f),V(.145f,-.024f),V(.12f,.018f),V(-.115f,.027f)},steel);
                for(int i=0;i<7;i++)for(int side=-1;side<=1;side+=2)
                    B(root,"Bizon longitudinal magazine flute",new Vector3(side*.030f,-.031f,.025f+i*.043f),new Vector3(.004f,.011f,.03f),steel);
            }
            else if(id=="weapon_m249"||id=="weapon_negev")
            {
                bool negev=id=="weapon_negev";
                P(root,"Stamped feed tray shoulders",new Vector3(0,.15f,.017f),negev?.115f:.12f,new[]{V(-.13f,-.031f),V(.15f,-.031f),V(.12f,.016f),V(-.11f,.023f)},steel);
                for(int i=0;i<6;i++)B(view.Magazine,"Ammunition container pressed rib",new Vector3(-.071f,0,-.058f+i*.023f),new Vector3(.003f,.135f,.006f),b);
                P(root,negev?"Negev sloped gas housing":"M249 gas-regulator housing",new Vector3(0,.036f,.36f),.072f,new[]{V(-.06f,-.027f),V(.086f,-.024f),V(.065f,.025f),V(-.072f,.029f)},b);
                Barrel(root,"Gas regulator cap",.47f,.055f,.019f,.037f,steel);
                if(negev) { Remove(root,"Carry handle");B(root,"Negev folded carrying lever",new Vector3(.065f,.14f,.12f),new Vector3(.02f,.023f,.16f),b,new Vector3(0,0,-28)); }
            }
            else if(id=="weapon_mag7")
            {
                Remove(root,"Shotgun action");
                P(root,"MAG7 compact folded receiver",new Vector3(0,.067f,.031f),.071f,new[]{V(-.155f,-.041f),V(.16f,-.041f),V(.19f,.015f),V(.16f,.045f),V(-.12f,.054f),V(-.16f,.028f)},a);
                B(root,"MAG7 padded folding stock",new Vector3(0,.078f,-.235f),new Vector3(.061f,.052f,.16f),b);
            }
            else if(id=="weapon_knife")
            {
                Remove(root,"Blade");Remove(root,"Blade fuller");
                P(root,"Forged clip-point blade",Vector3.zero,.006f,new[]{V(.059f,-.025f),V(.226f,-.023f),V(.282f,.014f),V(.227f,.020f),V(.184f,.030f),V(.059f,.027f)},steel);
                P(root,"Blade bevel ground face",new Vector3(.0032f,0,0),.0005f,new[]{V(.067f,-.024f),V(.226f,-.022f),V(.277f,.012f),V(.217f,-.009f),V(.067f,-.010f)},c);
                WorldArt.Cylinder(root,"Handle pommel lanyard eye",new Vector3(0,0,-.09f),.014f,.038f,steel,false,"metal",new Vector3(0,0,90));
            }
            else if(id=="weapon_taser")
            {
                P(root,"Zeus contoured polymer backstrap",new Vector3(0,-.062f,-.044f),.051f,new[]{V(-.017f,-.06f),V(.013f,-.062f),V(.038f,.053f),V(.004f,.064f),V(-.02f,.019f)},b);
                for(int side=-1;side<=1;side+=2)for(int i=0;i<4;i++) B(root,"Zeus rubber traction groove",new Vector3(side*.028f,-.035f-i*.019f,-.034f),new Vector3(.002f,.007f,.031f),steel);
            }
        }

        static void AddArsenalSockets(Transform root,WeaponPresentation view,string id)
        {
            Vector3 trigger=new Vector3(0,-.075f,-.05f),support=new Vector3(0,.015f,.24f);
            var spec=ImportedArsenalModels.GetSpec(id);
            if(spec!=null) { trigger=new Vector3(spec.triggerGrip[0],spec.triggerGrip[1],spec.triggerGrip[2]);support=new Vector3(spec.supportGrip[0],spec.supportGrip[1],spec.supportGrip[2]); }
            if(id=="weapon_ak47") { trigger=new Vector3(0,-.02f,-.052f);support=new Vector3(0,.025f,.245f); }
            if(id=="weapon_mp7")support=new Vector3(0,-.008f,.18f);
            if(id=="weapon_mp9")support=new Vector3(0,-.025f,.155f);
            if(id=="weapon_mac10")support=new Vector3(0,-.03f,.12f);
            if(id=="weapon_p90") { trigger=new Vector3(0,-.060f,-.03f);support=new Vector3(0,-.012f,.195f); }
            if(id=="weapon_bizon")support=new Vector3(0,-.018f,.22f);
            if(id=="weapon_tec9") { trigger=new Vector3(0,-.075f,-.05f);support=new Vector3(0,-.04f,.06f); }
            if(id=="weapon_knife") { trigger=new Vector3(0,0,-.025f);support=new Vector3(-.23f,-.02f,.08f); }
            if(id=="weapon_taser") { trigger=new Vector3(0,-.073f,-.02f);support=new Vector3(0,-.052f,-.023f); }
            if(id=="weapon_elite")support=new Vector3(-.19f,-.043f,-.087f);
            if(id=="rocket_launcher") { trigger=new Vector3(0,-.053f,.06f);support=new Vector3(0,-.046f,.31f); }
            if(view.Pump) support=root.InverseTransformPoint(view.Pump.position)+Vector3.down*.02f;
            WorldArt.Group(root,"TriggerGrip",trigger);WorldArt.Group(root,"SupportGrip",support);
        }

        static void Remove(Transform root,string name) { var child=root.Find(name);if(child)Object.DestroyImmediate(child.gameObject); }
        static Vector2 V(float z,float y)=>new Vector2(z,y);
        static Transform P(Transform root,string name,Vector3 position,float width,Vector2[] outline,Material material)
        {
            var node=WorldArt.Group(root,name,position);
            node.gameObject.AddComponent<MeshFilter>().sharedMesh=ProfileMesh(name,width,outline);
            node.gameObject.AddComponent<MeshRenderer>().sharedMaterial=material;
            return node;
        }

        static readonly Dictionary<string,Mesh> ArsenalProfiles=new Dictionary<string,Mesh>();
        static Mesh ProfileMesh(string name,float width,Vector2[] points)
        {
            string key=name+width;foreach(var p in points)key+=p.ToString("F4");
            if(ArsenalProfiles.TryGetValue(key,out var cached)&&cached)return cached;
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var indices=new List<int>();
            Vector2 center=Vector2.zero;foreach(var p in points)center+=p;center/=points.Length;
            void Triangle(Vector3 a,Vector3 b,Vector3 c,Vector3 normal)
            {
                if(Vector3.Dot(Vector3.Cross(b-a,c-a),normal)<0){var swap=b;b=c;c=swap;}
                int index=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);
                uv.Add(new Vector2(a.z*6,a.y*6));uv.Add(new Vector2(b.z*6,b.y*6));uv.Add(new Vector2(c.z*6,c.y*6));
                indices.Add(index);indices.Add(index+1);indices.Add(index+2);
            }
            var order=new List<int>();for(int i=0;i<points.Length;i++)order.Add(i);
            float area=0;for(int i=0;i<points.Length;i++){var a=points[i];var b=points[(i+1)%points.Length];area+=a.x*b.y-b.x*a.y;}
            if(area<0)order.Reverse();
            var faces=new List<int>();int guard=0;
            while(order.Count>2&&guard++<200)
            {
                bool clipped=false;
                for(int i=0;i<order.Count;i++)
                {
                    int ia=order[(i+order.Count-1)%order.Count],ib=order[i],ic=order[(i+1)%order.Count];
                    Vector2 a=points[ia],b=points[ib],c=points[ic];
                    if(Cross2(b-a,c-b)<=0)continue;bool inside=false;
                    foreach(int j in order)if(j!=ia&&j!=ib&&j!=ic)
                    {var p=points[j];if(Cross2(b-a,p-a)>=0&&Cross2(c-b,p-b)>=0&&Cross2(a-c,p-c)>=0){inside=true;break;}}
                    if(inside)continue;faces.Add(ia);faces.Add(ib);faces.Add(ic);order.RemoveAt(i);clipped=true;break;
                }
                if(!clipped)break;
            }
            for(int side=-1;side<=1;side+=2)
            {
                for(int t=0;t<faces.Count;t+=3)
                {
                    Vector3 Q(int j){Vector2 p=Vector2.Lerp(points[j],center,.035f);return new Vector3(side*width*.5f,p.y,p.x);}
                    Triangle(Q(faces[t]),Q(faces[t+1]),Q(faces[t+2]),Vector3.right*side);
                }
                for(int i=0;i<points.Length;i++)
                {
                    var a=points[i];var b=points[(i+1)%points.Length];var ai=Vector2.Lerp(a,center,.035f);var bi=Vector2.Lerp(b,center,.035f);
                    var v0=new Vector3(side*width*.5f,ai.y,ai.x);var v1=new Vector3(side*width*.5f,bi.y,bi.x);var v2=new Vector3(side*width*.38f,b.y,b.x);var v3=new Vector3(side*width*.38f,a.y,a.x);
                    Vector3 normal=Vector3.right*side+new Vector3(0,(a.y+b.y)*.5f-center.y,(a.x+b.x)*.5f-center.x).normalized;
                    Triangle(v0,v1,v2,normal);Triangle(v0,v2,v3,normal);
                    if(side==1)
                    {
                        v0=new Vector3(-width*.38f,a.y,a.x);v1=new Vector3(-width*.38f,b.y,b.x);
                        normal=new Vector3(0,(a.y+b.y)*.5f-center.y,(a.x+b.x)*.5f-center.x);
                        Triangle(v0,v1,v2,normal);Triangle(v0,v2,v3,normal);
                    }
                }
            }
            var mesh=new Mesh{name="Authored contour "+name};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();ArsenalProfiles[key]=mesh;return mesh;
        }
        static float Cross2(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;

        static Mesh ContouredBlock(string name)
        {
            if(name=="Pistol grip"||name=="Knife handle"||name.Contains("forward grip")||name.Contains("foregrip"))
                return ProfileMesh("Ergonomic grip",1,new[]{V(-.45f,-.43f),V(.17f,-.5f),V(.44f,-.36f),V(.48f,.42f),V(.27f,.5f),V(-.34f,.48f),V(-.5f,.27f),V(-.38f,-.13f)});
            if(name.Contains("receiver")||name.Contains("housing")||name=="Shotgun action"||name=="Pistol frame"||name.Contains("power housing"))
                return ProfileMesh("Machined stepped casing",1,new[]{V(-.5f,-.24f),V(-.30f,-.48f),V(.32f,-.5f),V(.5f,-.21f),V(.5f,.26f),V(.30f,.5f),V(-.37f,.47f),V(-.5f,.29f)});
            if(name=="Belt ammunition box")
                return ProfileMesh("Pressed belt container",1,new[]{V(-.5f,-.30f),V(-.31f,-.5f),V(.35f,-.5f),V(.5f,-.32f),V(.43f,.5f),V(-.42f,.5f)});
            if(name.Contains("handguard")||name.Contains("fore-end"))
                return ProfileMesh("Ventilated contoured fore-end",1,new[]{V(-.48f,-.28f),V(-.34f,-.48f),V(.30f,-.5f),V(.5f,-.20f),V(.45f,.32f),V(.28f,.5f),V(-.45f,.45f)});
            if(name=="Feed cover")
                return ProfileMesh("Rounded feed tray cover",1,new[]{V(-.5f,-.5f),V(.5f,-.5f),V(.43f,.10f),V(.25f,.5f),V(-.33f,.40f)});
            return null;
        }
    }
}
