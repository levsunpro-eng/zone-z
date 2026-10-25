#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace ZoneZ.Editor
{
    // Uses only a new, uniquely named OS temporary directory. Never reads a player profile.
    public static class ProfileValidation
    {
        static int assertions;

        public static void Run()
        {
            assertions=0;
            string directory=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"ZoneZ-ProfileValidation-"+Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(directory);
            try {
                Transactions(Path.Combine(directory,"transactions"));
                Skins(Path.Combine(directory,"skins"));
                Recovery(Path.Combine(directory,"recovery"));
                Schema(Path.Combine(directory,"schema"));
                FailedWrites(Path.Combine(directory,"failure"));
                SettingsAndBindings(Path.Combine(directory,"settings"));
                Debug.Log("ZONEZ_PROFILE_VALIDATION_OK assertions="+assertions);
            } finally {
                // Deletion is restricted to the exact GUID directory created above.
                string temporary=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
                if(directory.StartsWith(temporary,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(directory).StartsWith("ZoneZ-ProfileValidation-",StringComparison.Ordinal)&&Directory.Exists(directory)) Directory.Delete(directory,true);
            }
        }

        static void Check(bool value,string message)
        {
            assertions++;
            if(!value) throw new InvalidOperationException("Profile validation: "+message);
        }
        static void Fund(ProfileStore store,int value) { store.Current.credits=value; Check(store.Save(),"test seed saved"); }

        static void Transactions(string directory)
        {
            var store=ProfileStore.Load(directory);
            Check(store.Current.credits==500,"new profile starts at 500");
            Check(store.Current.ownedAbilities.SequenceEqual(new[]{"frag_grenade"}),"only default ability unlocked");
            Check(store.Current.ultimateId=="frag_grenade","default ability selected");
            Check(store.Save(),"initial save");
            Check(ProfileStore.Load(directory).Current.credits==500,"reload does not grant start currency again");
            Check(!store.PurchaseAbility("adrenaline",out _)&&store.Current.credits==500&&!store.Current.ownedAbilities.Contains("adrenaline"),"insufficient ability purchase changes nothing");
            Check(!store.PurchaseAbility("missing",out _)&&store.Current.credits==500,"unknown ability rejected");
            Check(!store.PurchaseCosmetic("pattern_1",1,out _)&&store.Current.credits==500,"caller cannot choose price");
            Check(!store.PurchaseCosmetic("pattern_01",250,out _),"noncanonical cosmetic id rejected");
            Check(store.GrantReward("test-match",100)&&store.Current.credits==600,"first reward");
            Check(store.GrantReward("test-match",999)&&store.Current.credits==600,"duplicate reward no extra currency");
            store=ProfileStore.Load(directory);
            Check(store.GrantReward("test-match",100)&&store.Current.credits==600,"duplicate reward after restart");
            Check(store.PurchaseAbility("adrenaline",out _)&&store.Current.credits==0,"exact balance purchase");
            Check(store.Current.ultimateId=="frag_grenade","purchase does not equip or grant readiness");
            Check(store.PurchaseAbility("adrenaline",out _)&&store.Current.credits==0&&store.Current.ownedAbilities.Count(x=>x=="adrenaline")==1,"repeat ability purchase idempotent");
            Check(store.SelectAbility("adrenaline",out _),"owned ability selectable");
            Check(!store.SelectAbility("absolute_shot",out _),"unowned ability cannot be selected");
            Fund(store,500);
            Check(store.Upgrade("health",0,out _)&&store.Current.healthLevel==1&&store.Current.credits==300,"upgrade expected level");
            Check(!store.Upgrade("health",0,out _)&&store.Current.healthLevel==1&&store.Current.credits==300,"retry same level cannot buy next");
            Check(!store.Upgrade("health",1,out _)&&store.Current.credits==300,"unaffordable next upgrade");
            Check(!store.Upgrade("unknown",0,out _),"unknown branch rejected");
            Check(store.PurchaseCosmetic("skin_1",300,out _)&&store.Current.credits==0,"exact cosmetic price");
            Check(store.PurchaseCosmetic("skin_1",300,out _)&&store.Current.credits==0,"duplicate cosmetic purchase");
            Fund(store,4400);
            for(int level=0;level<5;level++) Check(store.Upgrade("speed",level,out _),"consecutive upgrade "+level);
            Check(store.Current.credits==0&&store.Current.speedLevel==5,"full branch total 4400");
            Check(!store.Upgrade("speed",5,out _),"max branch cannot upgrade");
            Fund(store,10000); store.PurchasesAllowed=()=>false;
            Check(!store.PurchaseAbility("armor_shield",out _)&&!store.PurchaseCosmetic("pattern_1",250,out _)&&!store.Upgrade("strength",0,out _)&&!store.SelectAbility("frag_grenade",out _)&&store.Current.credits==10000,"economy locked during match");
            Check(store.SelectWeapon("weapon_awp",out _)&&store.SelectWeapon("weapon_deagle",out _),"loadout changes allowed independently");
            Check(!store.SelectWeapon("weapon_knife",out _)&&store.Current.primaryId=="weapon_awp", "special slot cannot replace primary");
            Check(store.GrantReward("completed-while-locked",20),"results reward allowed during match lock");
            Fund(store,int.MaxValue);
            Check(!store.GrantReward("overflow",1)&&store.Current.credits==int.MaxValue&&!store.Current.rewardedMatches.Contains("overflow"),"reward overflow atomic rejection");
            Check(!store.GrantReward("negative",-1)&&!store.GrantReward(" ",20),"invalid rewards rejected");
        }

        static void Skins(string directory)
        {
            var store=ProfileStore.Load(directory); Fund(store,2000);
            Check(store.PurchaseCosmetic("pattern_1",250,out _)&&store.PurchaseCosmetic("sticker_1",100,out _),"skin test unlocks");
            int credits=store.Current.credits;
            var skin=new SkinPreset { weaponId="weapon_ak47",name="  Испытание  ",colorA="ABCDEF",colorB="112233",colorC="FEDCBA",pattern=1,wear=.4f,stickers=new[]{1,1,1,1},patternScale=1.5f,patternRotation=45,stickerRotations=new[]{10f,20f,30f,40f},stickerScales=new[]{.5f,1f,1.25f,1.5f} };
            Check(store.SaveSkin(skin,out _)&&store.Current.credits==credits,"save with repeated unlocked sticker is free");
            var saved=store.SkinFor("weapon_ak47"); string id=saved.id;
            Check(!string.IsNullOrEmpty(id)&&saved.name=="Испытание","skin gets unique identity and trimmed name");
            store=ProfileStore.Load(directory); saved=store.SkinFor("weapon_ak47");
            Check(saved.colorA=="ABCDEF"&&saved.colorB=="112233"&&saved.colorC=="FEDCBA"&&saved.pattern==1&&saved.stickers.SequenceEqual(new[]{1,1,1,1}),"three colors pattern and four stickers survive restart");
            Check(saved.patternScale==1.5f&&saved.patternRotation==45&&saved.stickerRotations[3]==40&&saved.stickerScales[2]==1.25f,"skin transform parameters persist");
            saved.colorA="000000";
            Check(store.SkinFor("weapon_ak47").colorA=="ABCDEF","skin getter returns a copy");
            saved.colorA="XYZ"; Check(!store.SaveSkin(saved,out _),"invalid hex rejected");
            saved.colorA="000000"; saved.pattern=2; Check(!store.SaveSkin(saved,out _),"unowned pattern rejected");
            saved.pattern=1; saved.id=""; saved.name="Копия";
            Check(store.SaveSkin(saved,out _)&&store.Current.skins.Count==2&&store.Current.skins.Count(x=>x.applied)==1,"copy preserves prior preset and one assignment");
            Check(store.ApplySkin(id,out _)&&store.SkinFor("weapon_ak47").id==id,"saved preset can be reapplied");
            Check(store.DeleteSkin(id,out _)&&store.SkinFor("weapon_ak47").colorA=="4B5446"&&store.Current.ownedCosmetics.Contains("sticker_1"),"delete applied preset returns neutral without deleting purchases");
            store.Current.skins.Add(new SkinPreset { id="legacy",name="Старый",weaponId="weapon_awp",pattern=99 });
            Check(store.Save(),"legacy unknown preset preserved");
            store=ProfileStore.Load(directory);
            Check(store.SkinFor("weapon_awp").pattern==0&&store.Current.skins.Any(x=>x.id=="legacy"&&x.pattern==99),"unknown skin elements neutral at runtime while original retained");
            var futurePreset=new SkinPreset { weaponId="weapon_awp",formatVersion=2 };
            Check(!store.SaveSkin(futurePreset,out _),"future skin format rejected");
        }

        static void Recovery(string directory)
        {
            var store=ProfileStore.Load(directory); Fund(store,320);
            Check(store.GrantReward("recovery-reward",100),"recovery main with backup");
            Check(File.Exists(store.FilePath+".bak"),"atomic replacement makes backup");
            File.WriteAllText(store.FilePath,"{broken");
            var recovered=ProfileStore.Load(directory);
            Check(recovered.Current.credits==320&&!recovered.WriteBlocked,"corrupt main recovers exact backup balance");
            Check(Directory.GetFiles(directory,"profile.json.invalid-*.json").Length==1,"corrupt source archived");
            Check(recovered.Save(),"recovered profile can be written");
            Check(JsonConvert.DeserializeObject<GameProfile>(File.ReadAllText(recovered.FilePath+".bak")).credits==320,"recovery save preserves the healthy backup instead of replacing it with malformed main");
            Check(ProfileStore.Load(directory).Current.credits==320,"recovery never regrants start balance");
            Check(recovered.GrantReward("after-recovery",10),"profile usable after recovery");
            File.Delete(recovered.FilePath);
            Check(ProfileStore.Load(directory).Current.credits==320,"missing main recovers backup");
        }

        static void Schema(string directory)
        {
            Directory.CreateDirectory(directory); string path=Path.Combine(directory,"profile.json");
            string future="{\"schemaVersion\":99,\"credits\":123,\"futureField\":true}";
            File.WriteAllText(path,future);
            var newer=ProfileStore.Load(directory);
            Check(newer.WriteBlocked&&!newer.Save()&&!newer.GrantReward("cannot-write",5),"future schema is read-only");
            Check(File.ReadAllText(path)==future,"future schema bytes unchanged");
            File.WriteAllText(path,"{}");
            var missing=ProfileStore.Load(directory);
            Check(missing.WriteBlocked&&!missing.Save()&&File.ReadAllText(path)=="{}","missing schema never masquerades as valid starter profile");
            File.WriteAllText(path,JsonConvert.SerializeObject(new GameProfile { schemaVersion=1,credits=99,primaryId="invalid",pistolId="weapon_awp",ultimateId="absolute_shot",healthLevel=9,speedLevel=-2 }));
            var normalized=ProfileStore.Load(directory);
            Check(!normalized.WriteBlocked&&normalized.Current.credits==99&&normalized.Current.primaryId=="weapon_ak47"&&normalized.Current.pistolId=="weapon_glock"&&normalized.Current.ultimateId=="frag_grenade"&&normalized.Current.healthLevel==5&&normalized.Current.speedLevel==0,"schema one normalizes bounded game fields without changing wallet");
            Check(normalized.SaveSettings(float.NaN,float.PositiveInfinity,300,-1,9,1,true,false,out _),"settings normalize non-finite and out-of-range values");
            Check(normalized.Current.sensitivity==1&&normalized.Current.volume==.7f&&normalized.Current.fov==90&&normalized.Current.uiScale==.8f&&normalized.Current.qualityLevel==2&&normalized.Current.frameLimit==60,"settings use safe bounded defaults");
            normalized.Current.skins.Add(new SkinPreset { weaponId="weapon_ak47" });
            normalized.Current.skins.Add(new SkinPreset { weaponId="weapon_ak47" });
            Check(normalized.Save()&&normalized.Current.skins.Select(x=>x.id).Distinct().Count()==2&&normalized.Current.skins.All(x=>!string.IsNullOrEmpty(x.id))&&normalized.Current.skins.Count(x=>x.applied)==1,"legacy skins receive unique ids and one assignment");
        }

        static void FailedWrites(string directory)
        {
            var store=ProfileStore.Load(directory); Check(store.Save(),"failure seed saved");
            string json=File.ReadAllText(store.FilePath);
            using(var fileLock=new FileStream(store.FilePath,FileMode.Open,FileAccess.Read,FileShare.None)) {
                Check(!store.GrantReward("locked-match",200)&&store.Current.credits==500&&!store.Current.rewardedMatches.Contains("locked-match"),"failed write rolls back reward and id together");
                Check(!store.PurchaseCosmetic("skin_1",300,out _)&&store.Current.credits==500&&!store.Current.ownedCosmetics.Contains("skin_1"),"failed purchase never deducts or unlocks");
                store.Current.credits=900;
                Check(!store.Save()&&store.Current.credits==500,"legacy direct save restores committed memory on failure");
                Check(store.Current.ownedAbilities.Count(x=>x=="frag_grenade")==1,"failed save rollback never duplicates default list entries");
            }
            Check(File.ReadAllText(store.FilePath)==json,"failed replacement leaves main unchanged");
            Check(store.GrantReward("locked-match",200)&&store.Current.credits==700,"retry after storage recovery succeeds exactly once");
            Check(store.GrantReward("locked-match",200)&&store.Current.credits==700,"retry already saved reward remains idempotent");
            using(var fileLock=new FileStream(store.FilePath,FileMode.Open,FileAccess.Read,FileShare.None))
                Check(store.GrantReward("locked-match",200)&&store.Current.credits==700,"already confirmed reward succeeds without rewriting a temporarily locked profile");
        }

        static void SettingsAndBindings(string directory)
        {
            var store=ProfileStore.Load(directory); Fund(store,237);
            var bindings=GameInput.DefaultBindings();
            Check(bindings.Count==18&&bindings.Values.Distinct().Count()==18,"default key map includes every action with unique keys");
            Check(!GameInput.IsValidKey((int)KeyCode.Escape)&&!GameInput.IsValidKey((int)KeyCode.None)&&!GameInput.IsValidKey(int.MaxValue),"Escape and invalid keycodes cannot bind");
            Check(!GameInput.Rebind(bindings,"ultimate",(int)KeyCode.R,false,out string conflict)&&conflict=="reload"&&bindings["ultimate"]==(int)KeyCode.Q,"conflict reports both actions without mutation");
            Check(GameInput.Rebind(bindings,"ultimate",(int)KeyCode.R,true,out _)&&bindings["ultimate"]==(int)KeyCode.R&&bindings["reload"]==(int)KeyCode.Q,"confirmed conflict swaps instead of losing an action");
            Check(!GameInput.Rebind(bindings,"fire",(int)KeyCode.Escape,true,out _)&&!GameInput.Rebind(bindings,"unknown",(int)KeyCode.V,true,out _),"invalid rebinding leaves menu accessible");
            var settings=new GameProfile { credits=99999,healthLevel=5,sensitivity=1.8f,volume=.8f,weaponVolume=.3f,footstepVolume=.4f,effectsVolume=.6f,ambientVolume=.2f,resolutionWidth=1920,resolutionHeight=1080,fullscreen=false,keyBindings=bindings,
                scopeSensitivity=1.7f,viewMotion=.25f,invertY=true,reducedFlashes=true,interfaceVolume=.6f,menuMusicVolume=.15f,crosshairColor="aabb11",crosshairThickness=3.5f };
            Check(store.SaveSettings(settings,out _),"settings transaction succeeds");
            Check(store.Current.credits==237&&store.Current.healthLevel==0,"settings object cannot change economy or progression");
            store=ProfileStore.Load(directory);
            Check(store.Current.keyBindings["ultimate"]==(int)KeyCode.R&&store.Current.keyBindings["reload"]==(int)KeyCode.Q,"key map persists across restart");
            Check(store.Current.weaponVolume==.3f&&store.Current.footstepVolume==.4f&&store.Current.effectsVolume==.6f&&store.Current.ambientVolume==.2f&&store.Current.volume==.8f,"five audio levels persist independently");
            Check(store.Current.resolutionWidth==1920&&store.Current.resolutionHeight==1080&&!store.Current.fullscreen,"resolution and display mode persist");
            Check(store.Current.invertY&&store.Current.reducedFlashes&&store.Current.scopeSensitivity==1.7f&&store.Current.viewMotion==.25f,"camera and flash accessibility settings persist");
            Check(store.Current.interfaceVolume==.6f&&store.Current.menuMusicVolume==.15f,"interface and menu music volume independent persistence");
            Check(store.Current.crosshairColor=="AABB11"&&store.Current.crosshairThickness==3.5f,"crosshair color and thickness persist");
            settings.weaponVolume=float.NaN;settings.footstepVolume=-1;settings.effectsVolume=2;settings.ambientVolume=float.PositiveInfinity;
            settings.scopeSensitivity=float.PositiveInfinity;settings.viewMotion=-5;settings.interfaceVolume=float.NaN;settings.menuMusicVolume=20;settings.crosshairColor="nothex";settings.crosshairThickness=float.NaN;
            settings.resolutionWidth=-1;settings.resolutionHeight=100000;settings.keyBindings=new System.Collections.Generic.Dictionary<string,int> { {"fire",(int)KeyCode.Escape},{"reload",(int)KeyCode.R},{"use",(int)KeyCode.R},{"unknown",int.MaxValue} };
            Check(store.SaveSettings(settings,out _),"malformed settings normalize safely");
            Check(store.Current.resolutionWidth==0&&store.Current.resolutionHeight==0&&store.Current.weaponVolume==1&&store.Current.footstepVolume==0&&store.Current.effectsVolume==1&&store.Current.ambientVolume==.5f,"invalid display and audio values use bounded defaults");
            Check(store.Current.keyBindings.Count==18&&store.Current.keyBindings.Values.Distinct().Count()==18&&store.Current.keyBindings.Values.All(GameInput.IsValidKey),"malformed bindings recover complete unique valid map");
            Check(store.Current.scopeSensitivity==1&&store.Current.viewMotion==0&&store.Current.interfaceVolume==.7f&&store.Current.menuMusicVolume==1&&store.Current.crosshairColor=="DDE6CD"&&store.Current.crosshairThickness==2,"invalid accessibility values safely normalize");
            Check(store.SaveSettings(new GameProfile(),out _)&&store.Current.keyBindings.OrderBy(x=>x.Key).SequenceEqual(GameInput.DefaultBindings().OrderBy(x=>x.Key)),"binding reset restores defaults without resetting wallet");
            Check(store.Current.credits==237,"settings reset never resets wallet");
            Check(!store.Current.invertY&&!store.Current.reducedFlashes&&store.Current.viewMotion==1&&store.Current.scopeSensitivity==1&&store.Current.interfaceVolume==.7f&&store.Current.menuMusicVolume==.3f,"accessibility reset restores baseline");
        }
    }
}
#endif
