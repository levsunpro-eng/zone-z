using System;
using System.Linq;
using UnityEngine;

namespace ZoneZ.Editor
{
    public static class CoreValidation
    {
        public static void Run()
        {
            var data=GameCatalog.Load();
            Check(data.Weapons.Count==36,"36 weapon entries");Check(data.Weapons.Count(w=>w.Slot=="primary"||w.Slot=="pistol")==34,"34 firearms");
            Check(data.Abilities.Count==10,"10 ultimates");Check(data.Weapon("weapon_ak47").Damage==36,"AK47 source damage");
            foreach(var w in data.Weapons) { Check(w.FireInterval>0 && w.Range>0 && w.MoveSpeed>0,w.Id+" valid timing/range");Check(w.Damage>0,w.Id+" positive damage"); }
            var charge=new KillCharge();
            Check(!charge.Register(10,3,true,false),"first kill starts window");
            Check(!charge.Register(700,3,true,false),"second kill");
            Check(charge.Deadline==1210,"fixed window does not extend");
            Check(charge.Register(1210,3,true,false),"inclusive exact 20 second boundary");
            Check(charge.Ready&&!charge.Register(1211,3,true,false),"one stored charge");
            charge.Reset(true);Check(charge.Ready,"DM death preserves ready");
            charge.Reset(false);Check(!charge.Ready&&charge.Count==0,"round resets charge");
            charge.Register(0,3,true,false);charge.Register(800,3,true,false);charge.Register(1201,3,true,false);
            Check(charge.Count==1&&charge.Deadline==2401,"expired kill starts new window");
            charge.Register(1210,3,false,false);Check(charge.Count==1,"ultimate kills excluded");
            charge.Register(1220,3,true,true);Check(charge.Count==1,"active ability blocks charging");
            charge.Reset(false);Check(charge.Count==0,"death resets unfinished series");
            Check(RewardRules.Team(1,10,4,1)==620,"team win reward");
            Check(RewardRules.Team(0,0,0,4)==200,"draw reward");Check(RewardRules.Team(-1,2,1,2)==255,"team defeat reward");
            Check(RewardRules.Deathmatch(50,1)==670,"deathmatch kill cap");Check(RewardRules.Deathmatch(0,4)==180,"completion reward");
            Debug.Log("ZONEZ_CORE_VALIDATION_OK");
        }
        public static void Check(bool condition,string label){if(!condition)throw new InvalidOperationException("Core validation: "+label);}
    }
}
