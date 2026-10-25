using UnityEngine;

namespace ZoneZ
{
    public static partial class WeaponModelFactory
    {
        static void ApplySkinPattern(WeaponPresentation owner, Material paint, SkinPreset skin)
        {
            const int size=128;
            var pixels=new Color[size*size];
            float angle=skin.patternRotation*Mathf.Deg2Rad, ca=Mathf.Cos(angle), sa=Mathf.Sin(angle);
            for(int y=0;y<size;y++) for(int x=0;x<size;x++)
            {
                float px=(x-size*.5f)*ca-(y-size*.5f)*sa+size*.5f;
                float py=(x-size*.5f)*sa+(y-size*.5f)*ca+size*.5f;
                float value=.95f;
                if(skin.pattern==1) value=Mathf.Repeat(px+py*.5f,40)<12?.38f:.95f;
                else if(skin.pattern==2) value=Mathf.PerlinNoise(px*.055f+10,py*.06f+10)>.55f?.35f:Mathf.PerlinNoise(px*.11f+15,py*.11f+10)>.5f?.66f:.98f;
                else if(skin.pattern==3) value=(Mathf.FloorToInt(px/14)+Mathf.FloorToInt(py/14))%3==0?.4f:Mathf.Repeat(px+py,28)<3?.65f:.98f;
                float wear=Mathf.Clamp01(skin.wear);
                if(wear>0 && Mathf.PerlinNoise(x*.35f+33,y*.4f+11)>1-wear*.42f) value=.35f;
                pixels[y*size+x]=new Color(value,value,value);
            }
            var texture=new Texture2D(size,size,TextureFormat.RGB24,true){name="Original rotated weapon finish",wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Bilinear};
            texture.SetPixels(pixels); texture.Apply(true,true); owner.OwnedResources.Add(texture);
            paint.mainTexture=texture; paint.mainTextureScale=Vector2.one*(2*Mathf.Clamp(skin.patternScale,.5f,2));
        }

        static void AddSkinStickers(Transform root,SkinPreset skin,bool pistol,bool knife,Material accent)
        {
            var ivory=WorldArt.Mat("Sticker ivory",new Color(.91f,.88f,.7f),0,.3f);
            var red=WorldArt.Mat("Sticker rust red",new Color(.78f,.19f,.09f),0,.3f);
            var blue=WorldArt.Mat("Sticker petrol blue",new Color(.08f,.35f,.44f),0,.3f);
            string id=root.GetComponent<WeaponPresentation>().WeaponId;
            bool importedAK=id=="weapon_ak47" && root.Find("Imported AK AK");
            var arsenal=root.GetComponent<ImportedArsenalTag>();
            float offset=arsenal?arsenal.BodyWidth*.5f+.001f:importedAK?.0218f:knife?.0055f:pistol?.0248f:id=="weapon_p90"?.046f:id=="weapon_negev"||id=="weapon_m249"?.057f:.041f;
            for(int i=0;i<Mathf.Min(4,skin.stickers.Length);i++)
            {
                int design=skin.stickers[i]; if(design<=0) continue;
                int side=i%2==0?-1:1;
                float z=importedAK?-.028f+(i/2)*.115f:knife?.11f+(i/2)*.065f:-.07f+(i/2)*.14f;
                var sticker=WorldArt.Group(root,"Sticker slot "+(i+1),new Vector3(side*offset,knife?0:.072f,z));
                float rotation=skin.stickerRotations!=null&&i<skin.stickerRotations.Length?skin.stickerRotations[i]:0;
                float scale=skin.stickerScales!=null&&i<skin.stickerScales.Length?Mathf.Clamp(skin.stickerScales[i],.5f,1.5f):1;
                sticker.localRotation=Quaternion.Euler(rotation,0,0); sticker.localScale=Vector3.one*scale*(knife?.6f:1);
                B(sticker,"Sticker backing",Vector3.zero,new Vector3(.001f,.030f,.039f),design==2?red:design==4?blue:ivory);
                Vector3 face=new Vector3(side*.0011f,0,0);
                if(design==1) B(sticker,"Diagonal ranger badge",face,new Vector3(.001f,.023f,.006f),accent,new Vector3(32,0,0));
                else if(design==2)
                {
                    B(sticker,"Cross arm",face,new Vector3(.001f,.006f,.025f),ivory);
                    B(sticker,"Cross stem",face,new Vector3(.001f,.021f,.005f),ivory);
                }
                else if(design==4)
                {
                    for(int j=-1;j<=1;j++) B(sticker,"Wave",face+new Vector3(0,j*.008f,0),new Vector3(.001f,.003f,.028f),ivory,new Vector3(j*11,0,0));
                }
                else for(int j=0;j<3;j++) B(sticker,"Claw mark",face+new Vector3(0,0,-.011f+j*.011f),new Vector3(.001f,.022f,.003f),red,new Vector3(15,0,0));
            }
        }

        static void AddIdentityDetails(Transform root,WeaponPresentation view,string id,Material paint,Material grip,Material accent,Material metal)
        {
            // Shared mechanical families keep authored proportions, while each catalogue model has its own silhouette.
            switch(id)
            {
                case "weapon_glock":
                    B(root,"Glock squared slide nose",new Vector3(0,.074f,.127f),new Vector3(.047f,.069f,.045f),paint);
                    B(root,"Glock trigger safety",new Vector3(0,-.048f,.009f),new Vector3(.008f,.033f,.019f),accent); break;
                case "weapon_hkp2000":
                    WorldArt.Taper(root,"P2000 rounded dust cover",new Vector3(0,.004f,.071f),new Vector3(.047f,.035f,0),new Vector3(.027f,.025f,0),.14f,grip);
                    B(root,"P2000 ambidextrous slide release",new Vector3(-.030f,.038f,-.022f),new Vector3(.007f,.012f,.049f),accent); break;
                case "weapon_p250":
                    B(root,"P250 tall grip backstrap",new Vector3(0,-.080f,-.076f),new Vector3(.049f,.148f,.027f),paint,new Vector3(-20,0,0));
                    Rail(root,metal,.066f,.13f,-.017f); break;
                case "weapon_fiveseven":
                    WorldArt.Taper(root,"FiveSeven extended polymer slide",new Vector3(0,.073f,.059f),new Vector3(.052f,.066f,0),new Vector3(.038f,.060f,0),.232f,paint);
                    B(root,"FiveSeven broad grip",new Vector3(0,-.071f,-.043f),new Vector3(.054f,.142f,.084f),grip,new Vector3(-15,0,0));
                    view.Muzzle.localPosition=new Vector3(0,.077f,.18f); break;
                case "weapon_usp_silencer":
                    B(root,"USP raised tactical rear sight",new Vector3(0,.123f,-.040f),new Vector3(.043f,.018f,.027f),metal); break;
                case "weapon_tec9":
                    for(int i=0;i<4;i++) for(int side=-1;side<=1;side+=2) B(root,"Tec9 perforation",new Vector3(side*.032f,.077f,.055f+i*.021f),new Vector3(.002f,.026f,.012f),metal); break;
                case "weapon_cz75a":
                    B(root,"CZ slide contour",new Vector3(0,.106f,.049f),new Vector3(.034f,.024f,.202f),metal); break;
                case "weapon_m4a1":
                    B(root,"M4 carry handle bridge",new Vector3(0,.235f,-.020f),new Vector3(.038f,.029f,.19f),metal);
                    B(root,"M4 carry handle rear",new Vector3(0,.190f,-.09f),new Vector3(.043f,.076f,.035f),metal);
                    B(root,"M4 carry handle front",new Vector3(0,.190f,.06f),new Vector3(.043f,.076f,.030f),metal); break;
                case "weapon_m4a1_silencer":
                    for(int i=0;i<7;i++) Barrel(root,"M4A1S fore-end ridge",.115f+i*.03f,.008f,.043f,.073f,grip); break;
                case "weapon_sg556":
                    B(root,"SG angled stock brace",new Vector3(0,-.005f,-.30f),new Vector3(.03f,.035f,.24f),accent,new Vector3(-13,0,0));
                    B(root,"SG handguard lower",new Vector3(0,.015f,.20f),new Vector3(.078f,.029f,.27f),grip); break;
                case "weapon_galilar":
                    B(root,"Galil long receiver nose",new Vector3(0,.13f,.13f),new Vector3(.07f,.033f,.20f),accent); break;
                case "weapon_mp7":
                    B(root,"MP7 compact rear housing",new Vector3(0,.055f,-.135f),new Vector3(.057f,.105f,.066f),paint); break;
                case "weapon_mp9":
                    B(root,"MP9 slanted front receiver",new Vector3(0,.099f,.15f),new Vector3(.070f,.085f,.092f),paint,new Vector3(13,0,0));
                    B(root,"MP9 folding stock hinge",new Vector3(.041f,.074f,-.135f),new Vector3(.04f,.06f,.06f),accent); break;
                case "weapon_ump45":
                    B(root,"UMP long polymer side",new Vector3(0,.109f,.055f),new Vector3(.075f,.035f,.34f),paint);
                    B(root,"UMP skeleton stock lower",new Vector3(0,-.04f,-.235f),new Vector3(.026f,.023f,.16f),grip); break;
                case "weapon_mp5sd":
                    Barrel(root,"MP5 rounded stamped receiver",.035f,.26f,.032f,.098f,paint);
                    B(root,"MP5 cocking handle",new Vector3(-.046f,.127f,.20f),new Vector3(.05f,.014f,.018f),metal); break;
                case "weapon_g3sg1":
                    for(int i=0;i<6;i++) Barrel(root,"G3 handguard flute",.19f+i*.03f,.013f,.045f,.038f,grip);
                    B(root,"G3 raised cheek piece",new Vector3(0,.092f,-.31f),new Vector3(.065f,.055f,.20f),grip); break;
                case "weapon_negev":
                    B(root,"Negev folding shoulder brace",new Vector3(0,.008f,-.33f),new Vector3(.026f,.023f,.21f),accent,new Vector3(-18,0,0));
                    B(root,"Negev foregrip",new Vector3(0,-.062f,.31f),new Vector3(.05f,.13f,.054f),grip); break;
                case "weapon_nova":
                    B(root,"Nova receiver sweep",new Vector3(0,.12f,-.06f),new Vector3(.068f,.023f,.19f),paint); break;
            }
        }

        public static WeaponPresentation CreateAbility(Transform parent,string id,bool firstPerson)
        {
            if(id!="underbarrel" && id!="laser_burst" && id!="rocket_launcher") throw new System.ArgumentException("Unknown temporary weapon: "+id,nameof(id));
            var root=WorldArt.Group(parent,"Temporary weapon "+id);
            var view=root.gameObject.AddComponent<WeaponPresentation>(); view.FirstPerson=firstPerson; view.WeaponId=id;
            var metal=WorldArt.Mat("Weapon steel",new Color(.08f,.09f,.105f),.8f,.45f);
            var grip=WorldArt.Mat("Temporary weapon grips",new Color(.06f,.075f,.064f),.12f);
            var paint=WorldArt.Mat("Temporary weapon military paint",new Color(.30f,.36f,.21f),.35f);
            var accent=WorldArt.Mat("Temporary weapon warning paint",new Color(.80f,.49f,.12f),.3f);
            ImportedArsenalModels.Surface(paint,"Steel");ImportedArsenalModels.Surface(grip,"Polymer");ImportedArsenalModels.Surface(metal,"Steel");ImportedArsenalModels.Surface(accent,"Polymer");
            if(id=="underbarrel")
            {
                if(!BuildFreeArsenal(root,view,"weapon_m4a1",paint,grip,accent,metal,false)) BuildRifle(root,view,"weapon_m4a1",paint,grip,accent,metal,false);
                Barrel(root,"Grenade launch tube",.27f,.38f,.056f,-.022f,metal);
                for(int i=0;i<8;i++) Barrel(root,"Launcher sliding fore-end rib",.15f+i*.025f,.012f,.064f,-.022f,grip);
                B(root,"Launcher sight ladder",new Vector3(-.075f,.16f,.20f),new Vector3(.018f,.18f,.04f),accent);
                view.Muzzle.localPosition=new Vector3(0,-.022f,.47f);
            }
            else if(id=="rocket_launcher")
            {
                Barrel(root,"Launch tube",.18f,.99f,.095f,.13f,paint);
                Barrel(root,"Forward steel ring",.68f,.055f,.11f,.13f,metal);
                Barrel(root,"Rear exhaust ring",-.33f,.07f,.113f,.13f,metal);
                Barrel(root,"Forward dark bore",.710f,.008f,.086f,.13f,grip);
                for(int i=0;i<3;i++) Barrel(root,"Launcher warning stripe",.39f+i*.055f,.019f,.097f,.13f,accent);
                Grip(root,grip,new Vector3(0,-.053f,.06f)); Grip(root,grip,new Vector3(0,-.046f,.31f));
                B(root,"Launcher shoulder rest",new Vector3(0,.018f,-.20f),new Vector3(.13f,.10f,.28f),grip);
                Scope(root,metal,.20f,.30f); Muzzle(root,view,.73f,.13f);
            }
            else
            {
                view.Electric=true;
                var glow=WorldArt.Mat("Laser coil glow",new Color(.15f,.70f,.77f),.3f,.65f);
                glow.EnableKeyword("_EMISSION"); glow.SetColor("_EmissionColor",new Color(.15f,1.3f,1.5f));
                B(root,"Laser power housing",new Vector3(0,.07f,.025f),new Vector3(.105f,.17f,.38f),paint);
                Stock(root,grip,true,.19f); Grip(root,grip,new Vector3(0,-.105f,-.06f)); Trigger(root,metal);
                Barrel(root,"Laser emitter spine",.35f,.40f,.031f,.105f,metal);
                for(int i=0;i<6;i++) Barrel(root,"Energy focusing ring",.22f+i*.055f,.013f,.065f,.105f,glow);
                for(int side=-1;side<=1;side+=2) B(root,"Coil shielding rail",new Vector3(side*.071f,.105f,.37f),new Vector3(.018f,.04f,.39f),metal);
                view.Magazine=B(root,"Laser battery pack",new Vector3(0,-.082f,.07f),new Vector3(.075f,.15f,.14f),accent);
                B(root,"Charge gauge",new Vector3(.055f,.09f,.025f),new Vector3(.006f,.024f,.11f),glow);
                Muzzle(root,view,.58f);
            }
            if(id=="rocket_launcher")
            {
                for(int side=-1;side<=1;side+=2)P(root,"Launcher shaped shoulder shell",new Vector3(side*.055f,.08f,-.20f),.028f,new[]{V(-.13f,-.09f),V(.10f,-.03f),V(.11f,.06f),V(-.09f,.09f)},grip);
                P(root,"Launcher optic rangefinder",new Vector3(-.113f,.18f,.20f),.044f,new[]{V(-.09f,-.039f),V(.10f,-.039f),V(.12f,.025f),V(.07f,.054f),V(-.08f,.049f)},paint);
            }
            else if(id=="laser_burst")
            {
                P(root,"Laser machined upper spine",new Vector3(0,.148f,.12f),.11f,new[]{V(-.20f,-.019f),V(.27f,-.019f),V(.21f,.028f),V(-.14f,.04f)},metal);
                for(int side=-1;side<=1;side+=2)for(int i=0;i<5;i++)B(root,"Laser heat sink fin",new Vector3(side*.060f,.085f,.01f+i*.029f),new Vector3(.011f,.089f,.007f),metal);
            }
            AddArsenalSockets(root,view,id);
            if(firstPerson) AddHands(root,view,grip,false);
            view.Finish(); return view;
        }
    }
}
