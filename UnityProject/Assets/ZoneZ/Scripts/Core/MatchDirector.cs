using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ZoneZ
{
    public enum MatchPhase { Menu,Preparation,Playing,RoundEnd,Results }
    [DefaultExecutionOrder(-200)]
    public sealed class MatchDirector : MonoBehaviour
    {
        public MatchPhase Phase=MatchPhase.Menu;
        public string ModeId="team";
        public bool IsTeamMode => ModeId!="deathmatch";
        public List<Actor> Actors=new List<Actor>();
        public Actor Local;
        public float TimeRemaining,PreparationRemaining,MatchTime;
        public int RoundNumber,Tick;
        public int[] RoundWins=new int[2];
        public string WinnerText="";
        public string RoundReason="";
        public int EarnedCredits;
        public string MatchId;
        public readonly List<string> KillFeed=new List<string>();
        public readonly List<float> KillFeedTimes=new List<float>();
        public int Seed;
        readonly Dictionary<Actor,float> respawns=new Dictionary<Actor,float>();
        float phaseTimer;
        int nextLootWave,roundStartTick;
        bool roundResolutionPending;
        System.Random random;
        readonly Queue<Action> damageBatch=new Queue<Action>();
        bool resolvingDamage;
        GameRoot Root=>GameRoot.Instance;
        public bool TryQueueDamage(Action apply)
        {
            if(!Application.isPlaying || !Time.inFixedTimeStep || resolvingDamage)return false;
            damageBatch.Enqueue(apply);return true;
        }
        public void FlushDamage()
        {
            if(Phase!=MatchPhase.Playing){damageBatch.Clear();return;}
            resolvingDamage=true;
            try {while(damageBatch.Count>0)damageBatch.Dequeue()();}
            finally {resolvingDamage=false;}
            if(roundResolutionPending)LateUpdate();
        }
        public void StartMatch(string modeId,int participantCount=0,int teamZeroCount=12)
        {
            Cleanup(); ModeId=modeId=="deathmatch"?"deathmatch":"team";
            MatchId=Guid.NewGuid().ToString("N"); Seed=Environment.TickCount; random=new System.Random(Seed);
            EarnedCredits=0; RoundWins=new int[2]; RoundNumber=0; Tick=0; MatchTime=0; WinnerText=""; KillFeed.Clear();KillFeedTimes.Clear();
            int count=participantCount>0?Mathf.Clamp(participantCount,1,IsTeamMode?24:20):IsTeamMode?24:20;
            var primary=Root.Catalog.Weapons.Where(w=>w.Slot=="primary").ToList();
            var pistol=Root.Catalog.Weapons.Where(w=>w.Slot=="pistol").ToList();
            var unlocked=Root.Profile.ownedAbilities.Where(id=>Root.Catalog.Ability(id)!=null).ToList();
            for(int i=0;i<count;i++) {
                var go=new GameObject("Combatant"); go.transform.SetParent(transform);
                var actor=go.AddComponent<Actor>(); int team=IsTeamMode?(i<teamZeroCount?0:1):i;
                actor.Initialize(this,i,i==0?"Вы":BotNames[(i-1)%BotNames.Length],team,i==0);
                bool progression=IsTeamMode && !(Root.Network && Root.Network.IsActive);
                actor.MaxHP=100+(progression?Root.Profile.healthLevel*4:0);
                actor.SpeedMultiplier=1+(progression?Root.Profile.speedLevel*.02f:0);
                actor.DamageMultiplier=1+(progression?Root.Profile.strengthLevel*.02f:0);
                actor.Ability=go.AddComponent<AbilityController>();
                string ability=i==0?Root.Profile.ultimateId:unlocked.Count>0?unlocked[random.Next(unlocked.Count)]:"frag_grenade";
                actor.Ability.Initialize(actor,ability);
                if(i!=0)actor.Weapon.EquipLoadout(primary[random.Next(primary.Count)].Id,pistol[random.Next(pistol.Count)].Id);
                Actors.Add(actor); if(i==0) Local=actor;
            }
            BeginRound();
        }
        static readonly string[] BotNames={"Кедр","Рысь","Сокол","Шторм","Вектор","Гром","Атлас","Иней","Ворон","Тень","Маяк","Барс","Каскад","Пепел","Титан","Ветер","Рубеж","Кобра","Филин","Орион","Скат","Кварц","Дозор"};

        void BeginRound()
        {
            RoundNumber++; Phase=MatchPhase.Preparation; PreparationRemaining=15; TimeRemaining=IsTeamMode?180:600;
            roundResolutionPending=false; respawns.Clear(); ClearTransient();
            foreach(var actor in Actors) {
                actor.Ability.ResetForSpawn(false);
                if(actor.Human)actor.Weapon.EquipLoadout(Root.Profile.primaryId,Root.Profile.pistolId);
                Vector3 spawn=IsTeamMode?Root.World.SpawnPoint((actor.Team+(RoundNumber-1))%2,actor.Id%12,false):SafeDeathmatchSpawn(actor);
                actor.Respawn(spawn,Quaternion.LookRotation(new Vector3(-spawn.x,0,-spawn.z).normalized));
            }
            Root.SetPaused(false);
        }
        void BeginCombat()
        {
            Phase=MatchPhase.Playing; nextLootWave=3600; roundStartTick=Tick;
            if(!IsTeamMode) { foreach(var a in Actors)a.SpawnProtectedUntil=CombatClock.AfterSeconds(MatchTime,2); SpawnLootWave(); }
            Root.SetPaused(false); Root.Notify(IsTeamMode?"Раунд начался":"Deathmatch · 10 минут");
            if(IsTeamMode&&(!Actors.Any(a=>a.Alive&&a.Team==0)||!Actors.Any(a=>a.Alive&&a.Team==1)))roundResolutionPending=true;
        }
        void FixedUpdate()
        {
            if(Root.SimulationPaused || Root.Network && Root.Network.IsClient)return;
            if(Phase==MatchPhase.Preparation) {
                PreparationRemaining=Mathf.Max(0,(Mathf.RoundToInt(PreparationRemaining*60)-1)/60f);
                if(PreparationRemaining<=0)BeginCombat(); return;
            }
            if(Phase==MatchPhase.RoundEnd) { phaseTimer=(Mathf.RoundToInt(phaseTimer*60)-1)/60f; if(phaseTimer<=0) { if(RoundNumber>=3||RoundWins.Any(w=>w>=2)) FinishMatch(); else BeginRound(); } return; }
            if(Phase!=MatchPhase.Playing)return;
            Tick++; MatchTime=Tick/60f; TimeRemaining=Mathf.Max(0,((IsTeamMode?10800:36000)-(Tick-roundStartTick))/60f);
            // End before later FixedUpdate callbacks can fire or respawn at the excluded deadline.
            if(TimeRemaining<=0){if(IsTeamMode)EndTeamRound();else FinishMatch();return;}
            foreach(var actor in respawns.Keys.ToArray()) if(respawns[actor]<=MatchTime) {
                if(actor.Human)actor.Weapon.EquipLoadout(Root.Profile.primaryId,Root.Profile.pistolId);
                Vector3 spawn=SafeDeathmatchSpawn(actor); actor.Respawn(spawn,Quaternion.LookRotation(new Vector3(-spawn.x,0,-spawn.z).normalized)); respawns.Remove(actor); if(actor.Human)Root.SetPaused(false);
            }
            if(!IsTeamMode) {
                int elapsed=Tick-roundStartTick;
                if(elapsed>=nextLootWave && nextLootWave<36000) { SpawnLootWave();nextLootWave+=3600; }
            }
        }
        void LateUpdate()
        {
            if(!roundResolutionPending||Phase!=MatchPhase.Playing)return;
            roundResolutionPending=false;
            if(IsTeamMode)EndTeamRound();else FinishMatch();
        }
        public bool IsEnemy(Actor a,Actor b) => a && b && a!=b && (!IsTeamMode || a.Team!=b.Team);
        public void OnDeath(Actor victim,Actor killer,bool ultimate)
        {
            if(Phase!=MatchPhase.Playing)return;
            victim.Deaths++;
            if(killer && IsEnemy(killer,victim)) {
                killer.Kills++; killer.Score+=100; killer.Ability.RegisterKill(ultimate);
                var assists=victim.DamageHistory.Where(x=>x.Attacker && x.Attacker!=killer && IsEnemy(x.Attacker,victim) && MatchTime-x.Time<=10).GroupBy(x=>x.Attacker).Where(g=>g.Sum(x=>x.Amount)>=20);
                foreach(var assist in assists)assist.Key.Assists++;
            } else if(!IsTeamMode && (!killer || killer==victim)) victim.Score-=100;
            KillFeed.Insert(0,(killer?killer.DisplayName:"Окружение")+(ultimate?"  ◆  ":"  ›  ")+victim.DisplayName);
            KillFeedTimes.Insert(0,MatchTime);
            if(KillFeed.Count>5){KillFeed.RemoveAt(5);KillFeedTimes.RemoveAt(5);}
            victim.Ability.ResetForSpawn(!IsTeamMode);
            if(!IsTeamMode)respawns[victim]=CombatClock.AfterSeconds(MatchTime,3);
            if(victim.Human)Root.SetPaused(false);
            if(IsTeamMode && (!Actors.Any(a=>a.Alive&&a.Team==0)||!Actors.Any(a=>a.Alive&&a.Team==1)))roundResolutionPending=true;
        }
        void EndTeamRound()
        {
            int[] alive={Actors.Count(a=>a.Alive&&a.Team==0),Actors.Count(a=>a.Alive&&a.Team==1)};
            int winner=alive[0]==alive[1]?-1:alive[0]>alive[1]?0:1;
            RoundReason=alive[0]==0||alive[1]==0?"Уничтожение команды":"Больше выживших по окончании времени";
            if(winner<0) {
                float a=Actors.Where(x=>x.Alive&&x.Team==0).Sum(x=>x.HP/x.MaxHP);
                float b=Actors.Where(x=>x.Alive&&x.Team==1).Sum(x=>x.HP/x.MaxHP);
                if(Mathf.Abs(a-b)>.00001f){winner=a>b?0:1;RoundReason="Больше оставшегося здоровья";}
                else RoundReason="Полное равенство";
            }
            if(winner>=0)RoundWins[winner]++;
            WinnerText=winner<0?"Ничья раунда":winner==0?"Ваш отряд выиграл раунд":"Противники выиграли раунд";
            Phase=MatchPhase.RoundEnd; phaseTimer=5; ClearTransient();Root.SetPaused(false);Root.Notify(WinnerText);
        }
        void FinishMatch()
        {
            if(Phase==MatchPhase.Results)return;
            Phase=MatchPhase.Results;ClearTransient();
            foreach(var actor in Actors)actor.Ability.ResetForSpawn(false);
            if(IsTeamMode) {
                int result=RoundWins[0].CompareTo(RoundWins[1]);
                WinnerText=result>0?"ПОБЕДА":result<0?"ПОРАЖЕНИЕ":"НИЧЬЯ";
                EarnedCredits=RewardRules.Team(result,Local.Kills,Local.Assists,Rank(Local));
            } else {
                int rank=Rank(Local);WinnerText=rank==1?"ПОБЕДА":$"МЕСТО {rank}";
                EarnedCredits=RewardRules.Deathmatch(Local.Kills,rank);
            }
            if(Root.Network && Root.Network.IsActive) EarnedCredits=0;
            else if(!Root.Store.GrantReward(MatchId,EarnedCredits))Root.Notify("Не удалось записать награду. Проверьте доступ к папке сохранений.");
            Root.SetPaused(false);
        }
        public List<Actor> Standings() => IsTeamMode?Actors.OrderByDescending(a=>a.Kills).ThenByDescending(a=>a.Assists).ThenBy(a=>a.Deaths).ToList():Actors.OrderByDescending(a=>a.Score).ThenBy(a=>a.Deaths).ToList();
        public int Rank(Actor who)
        {
            if(IsTeamMode)return 1+Actors.Count(a=>a.Team==who.Team && (a.Kills>who.Kills || a.Kills==who.Kills && (a.Assists>who.Assists || a.Assists==who.Assists&&a.Deaths<who.Deaths)));
            return 1+Actors.Count(a=>a.Score>who.Score || a.Score==who.Score && a.Deaths<who.Deaths);
        }
        public float RespawnRemaining(Actor actor)=>Root.Network && Root.Network.IsClient?Root.Network.ReplicaRespawn(actor.Id):respawns.TryGetValue(actor,out float when)?Mathf.Max(0,when-MatchTime):0;
        Vector3 SafeDeathmatchSpawn(Actor who)
        {
            Vector3 best=Root.World.DeathmatchSpawns[0],fallback=best;
            float bestScore=float.NegativeInfinity,farthest=-1;
            foreach(Vector3 point in Root.World.DeathmatchSpawns.OrderBy(_=>random.Next())) {
                float nearest=1000;int visible=0;
                foreach(var enemy in Actors) {
                    if(!enemy.Alive||enemy==who)continue;
                    float distance=Vector3.Distance(point,enemy.transform.position);nearest=Mathf.Min(nearest,distance);
                    Vector3 line=enemy.EyePosition-(point+Vector3.up*1.6f);
                    if(distance<60&&!Physics.RaycastAll(point+Vector3.up*1.6f,line.normalized,line.magnitude,~0,QueryTriggerInteraction.Ignore).Any(h=>!h.collider.GetComponentInParent<Actor>()))visible++;
                }
                if(nearest>farthest){farthest=nearest;fallback=point;}
                // Occupancy is a hard constraint: an unseen occupied point must never
                // beat a free point simply because that point is visible to enemies.
                // The map has 24 separated points for at most 19 other living actors.
                if(nearest<2)continue;
                float score=Mathf.Min(nearest,60)-visible*35;
                if(score>bestScore){bestScore=score;best=point;}
            } return float.IsNegativeInfinity(bestScore)?fallback:best;
        }
        void SpawnLootWave()
        {
            foreach(var loot in FindObjectsByType<LootPickup>(FindObjectsSortMode.None))Destroy(loot.gameObject);
            var points=Root.World.LootPoints.OrderBy(_=>random.Next()).Take(6).ToArray();
            string[] ids={"medkit","protective_field","haste","fast_reload","stabilizer","vampirism"};
            ids=ids.OrderBy(_=>random.Next()).ToArray();for(int i=0;i<points.Length;i++)LootPickup.Create(ids[i],points[i],this,CombatClock.AfterSeconds(MatchTime,45));
        }
        void ClearTransient()
        {
            damageBatch.Clear();
            foreach(var p in FindObjectsByType<AbilityProjectile>(FindObjectsSortMode.None))Destroy(p.gameObject);
            foreach(var p in FindObjectsByType<LootPickup>(FindObjectsSortMode.None))Destroy(p.gameObject);
        }
        void Cleanup()
        {
            ClearTransient();respawns.Clear();
            foreach(var actor in Actors)if(actor){actor.gameObject.SetActive(false);Destroy(actor.gameObject);}
            Actors.Clear(); Local=null;
        }
        public void CreateReplicaMatch(string mode,string id)
        {
            Cleanup();ModeId=mode;MatchId=id;Phase=MatchPhase.Preparation;EarnedCredits=0;
            RoundWins=new int[2];KillFeed.Clear();KillFeedTimes.Clear();
        }
        public void DropNetworkActor(Actor actor)
        {
            respawns.Remove(actor);Actors.Remove(actor);
            if(Phase==MatchPhase.Playing&&IsTeamMode&&(!Actors.Any(a=>a.Alive&&a.Team==0)||!Actors.Any(a=>a.Alive&&a.Team==1)))roundResolutionPending=true;
        }
        public void ReturnToMenu(){Root.Network?.Stop();Phase=MatchPhase.Menu;Cleanup();Root.SetPaused(false);}
    }

    // Actors and projectiles submit all damage before deaths resolve for this 60 Hz step.
    [DefaultExecutionOrder(1000)]
    public sealed class DamageBatchEnd : MonoBehaviour
    {
        public MatchDirector Match;
        void FixedUpdate(){if(Match)Match.FlushDamage();}
    }
}
