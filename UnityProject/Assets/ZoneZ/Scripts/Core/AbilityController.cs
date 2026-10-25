using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ZoneZ
{
    [Serializable]
    public sealed class AbilityNetworkState
    {
        public string equipped, loot;
        public bool ready;
        public int streak, shots;
        public float streakRemaining, activeUntil, blockedUntil, ultShield, lootShield, lootUntil;
        public int[] revealedIds;
        public NetworkVector3[] revealedPositions;
    }

    public sealed class AbilityController : MonoBehaviour
    {
        public Actor Owner;
        public string EquippedId;
        public bool Ready=>Owner && Owner.IsNetworkReplica ? replicaReady : charge.Ready;
        public int Streak=>Owner && Owner.IsNetworkReplica ? replicaStreak : charge.Count;
        public float StreakRemaining=>Owner && Owner.IsNetworkReplica ? Mathf.Max(0, replicaStreakUntil-Now) : Streak>0?Mathf.Max(0,(charge.Deadline-Owner.Match.Tick)/60f):0;
        public float ActiveRemaining=>Mathf.Max(0,activeUntil-Now);
        public bool AbsoluteActive=>ActiveRemaining>0 && EquippedId=="absolute_shot";
        public bool Blocked=>Now<blockedUntil;
        public float BlockedRemaining=>Mathf.Max(0,blockedUntil-Now);
        public float Shield=>Mathf.Max(ActiveRemaining>0?ultShield:0,LootActive?lootShield:0);
        public float SpeedFactor=>Mathf.Max(ActiveRemaining>0&&EquippedId=="adrenaline"?1.35f:1,LootActive&&lootId=="haste"?1.25f:1);
        public float ReloadFactor=>LootActive&&lootId=="fast_reload"?.7f:1;
        public float StabilityFactor=>LootActive&&lootId=="stabilizer"?.75f:1;
        public bool HasTemporaryWeapon=>ActiveRemaining>0 && shots>0 && (EquippedId=="underbarrel"||EquippedId=="laser_burst"||EquippedId=="rocket_launcher");
        public string LootId=>LootActive?lootId:null;
        public float LootRemaining=>Mathf.Max(0,lootUntil-Now);
        public int RemainingShots=>shots;
        public readonly Dictionary<Actor,Vector3> Revealed=new Dictionary<Actor,Vector3>();
        readonly KillCharge charge=new KillCharge();
        float activeUntil,blockedUntil,ultShield,lootShield,lootUntil,nextFire,nextReveal;
        int shots;
        string lootId;
        bool replicaReady;
        int replicaStreak;
        float replicaStreakUntil;
        float Now=>Owner&&Owner.Match?Owner.Match.MatchTime:0;
        bool LootActive=>lootUntil>Now;
        AbilitySpec Spec=>GameRoot.Instance.Catalog.Ability(EquippedId);
        public void Initialize(Actor owner,string equipped) { Owner=owner; EquippedId=GameRoot.Instance.Catalog.Ability(equipped)!=null?equipped:"frag_grenade"; }
        public void ResetForSpawn(bool preserveReady)
        {
            charge.Reset(preserveReady);activeUntil=blockedUntil=lootUntil=0;ultShield=lootShield=0;shots=0;lootId=null;Revealed.Clear();
        }

        public AbilityNetworkState CaptureNetworkState()
        {
            var revealed = Revealed.Where(pair => pair.Key).ToArray();
            return new AbilityNetworkState {
                equipped = EquippedId, loot = lootId, ready = Ready, streak = Streak, shots = shots,
                streakRemaining = StreakRemaining, activeUntil = activeUntil, blockedUntil = blockedUntil,
                ultShield = ultShield, lootShield = lootShield, lootUntil = lootUntil,
                revealedIds = revealed.Select(pair => pair.Key.Id).ToArray(),
                revealedPositions = revealed.Select(pair => (NetworkVector3)pair.Value).ToArray()
            };
        }

        public void ApplyNetworkState(AbilityNetworkState state)
        {
            if (state == null || !Owner || !Owner.IsNetworkReplica) return;
            EquippedId = state.equipped; lootId = state.loot; replicaReady = state.ready;
            replicaStreak = state.streak; replicaStreakUntil = Now + state.streakRemaining; shots = state.shots;
            activeUntil = state.activeUntil; blockedUntil = state.blockedUntil;
            ultShield = state.ultShield; lootShield = state.lootShield; lootUntil = state.lootUntil;
            Revealed.Clear();
            if (state.revealedIds == null || state.revealedPositions == null) return;
            int count = Mathf.Min(state.revealedIds.Length, state.revealedPositions.Length);
            for (int i = 0; i < count; i++)
            {
                int id = state.revealedIds[i];
                var actor = Owner.Match.Actors.FirstOrDefault(candidate => candidate && candidate.Id == id);
                if (actor) Revealed[actor] = state.revealedPositions[i];
            }
        }

        void Update()
        {
            if(!Owner || Owner.IsNetworkReplica || !Owner.Match || Owner.Match.Phase!=MatchPhase.Playing || GameRoot.Instance.SimulationPaused)return;
            charge.Expire(Owner.Match.Tick);
            if(ActiveRemaining<=0)Revealed.Clear();
            if(!Owner.Alive)return;
        }
        void FixedUpdate()
        {
            if(!Owner||Owner.IsNetworkReplica||!Owner.Alive||!Owner.Match||Owner.Match.Phase!=MatchPhase.Playing||GameRoot.Instance.SimulationPaused)return;
            if(ActiveRemaining>0 && EquippedId=="recon_pulse" && Now>=nextReveal) {
                RefreshReveal();nextReveal=CombatClock.AfterSeconds(Now,1);
            }
        }
        void RefreshReveal()
        {
            Revealed.Clear();
            foreach(var actor in Owner.Match.Actors)if(actor.Alive&&Owner.Match.IsEnemy(Owner,actor)&&Vector3.Distance(Owner.transform.position,actor.transform.position)<=25)Revealed[actor]=actor.transform.position;
        }
        public void RegisterKill(bool fromUltimate)
        {
            if (Owner.IsNetworkReplica) return;
            if(charge.Register(Owner.Match.Tick,Spec.KillsRequired,Owner.Alive&&!fromUltimate,ActiveRemaining>0)&&Owner.Human)GameRoot.Instance.Notify(Spec.Name+" готова · "+GameInput.Label("ultimate"));
        }
        public bool Activate()
        {
            if (Owner.IsNetworkReplica) return false;
            if(!Ready||Blocked||!Owner.Alive||Owner.Match.Phase!=MatchPhase.Playing||GameRoot.Instance.SimulationPaused) {
                if(Owner.Human && !GameRoot.Instance.Paused)GameRoot.Instance.Notify(!Owner.Alive?"Нельзя использовать после смерти":Blocked?"Блокировка ЭМИ":!Ready?"Нужна серия убийств":"Дождитесь начала раунда");
                return false;
            }
            if(EquippedId=="medical_pulse"&&Owner.HP>=Owner.MaxHP){if(Owner.Human)GameRoot.Instance.Notify("Здоровье уже полное");return false;}
            charge.Consume();Owner.CancelSpawnProtection();activeUntil=CombatClock.AfterSeconds(Now,Spec.Duration);shots=Spec.Shots;nextFire=Now;nextReveal=Now;
            switch(EquippedId) {
                case "frag_grenade": case "emp_grenade":
                    activeUntil=CombatClock.AfterSeconds(Now,2);AbilityProjectile.Launch(Owner,Owner.EyePosition,Owner.Aim.forward*14+Vector3.up*3,Spec,2,true); break;
                case "medical_pulse":Owner.Heal(60);AbilityEffects.Pulse(Owner.transform.position+Vector3.up,Color.green,1);break;
                case "armor_shield":ultShield=75;AbilityEffects.Pulse(Owner.transform.position+Vector3.up,Color.cyan,1.3f);break;
                case "recon_pulse":RefreshReveal();nextReveal=CombatClock.AfterSeconds(Now,1);AbilityEffects.Pulse(Owner.transform.position+Vector3.up,new Color(.95f,.6f,.15f),1);break;
                default:AbilityEffects.Pulse(Owner.transform.position+Vector3.up,new Color(.95f,.6f,.15f),1);break;
            }
            if(Owner.Human)GameRoot.Instance.Notify(Spec.Name+(HasTemporaryWeapon?" · "+GameInput.Label("fire"):""));
            return true;
        }
        public bool FireTemporary(Vector3 origin,Vector3 direction)
        {
            if(Owner.IsNetworkReplica || !HasTemporaryWeapon || Now<nextFire || !Owner.Alive)return false;
            nextFire=CombatClock.AfterSeconds(Now,.65f);shots--;Owner.CancelSpawnProtection();
            if(EquippedId=="laser_burst")FireLaser(origin,direction);
            else AbilityProjectile.Launch(Owner,origin,direction*(EquippedId=="rocket_launcher"?32:23),Spec,-1,EquippedId=="underbarrel");
            if(shots<=0)activeUntil=Now;
            return true;
        }
        void FireLaser(Vector3 origin,Vector3 direction)
        {
            var hit=CombatTrace.Cast(origin,direction,60,Owner,true);
            if(hit.Target)hit.Target.ApplyDamage(Mathf.Max(1,120*hit.Retention),Owner,true,false,"chest",2);
            AbilityEffects.Beam(origin,hit.Point,new Color(1,.2f,.08f),.045f,.17f);
            AbilityEffects.Sound(origin,600,.18f);
        }
        public float AbsorbDamage(float damage)
        {
            if (Owner.IsNetworkReplica) return damage;
            float absorbed=Mathf.Min(damage,Shield);
            if(ActiveRemaining>0)ultShield=Mathf.Max(0,ultShield-absorbed);
            if(EquippedId=="armor_shield"&&ultShield<=0)activeUntil=Now;
            if(LootActive)lootShield=Mathf.Max(0,lootShield-absorbed);
            return damage-absorbed;
        }
        public void OnDealtHealthDamage(float amount,bool ultimate)
        {
            if (Owner.IsNetworkReplica) return;
            // Absolute kills do not report HP damage; ordinary and explosive HP loss can heal.
            if(Owner.Alive&&LootActive&&lootId=="vampirism")Owner.HP=Mathf.Min(100,Owner.HP+amount*.25f);
        }
        public void ApplyLoot(string id)
        {
            if (Owner.IsNetworkReplica) return;
            if(id=="medkit"){Owner.HP=Mathf.Min(100,Owner.HP+35);return;}
            lootId=id;lootUntil=CombatClock.AfterSeconds(Now,id=="protective_field"?15:id=="vampirism"?10:12);lootShield=id=="protective_field"?40:0;
            if(Owner.Human)GameRoot.Instance.Notify(LootPickup.DisplayName(id));
        }
        public void ApplyEMP()
        {
            if (Owner.IsNetworkReplica) return;
            activeUntil=lootUntil=0;ultShield=lootShield=0;shots=0;lootId=null;Revealed.Clear();blockedUntil=CombatClock.AfterSeconds(Now,5);
            if(Owner.Human)GameRoot.Instance.Notify("ЭМИ · способности заблокированы на 5 секунд");
        }
    }

    public sealed class AbilityProjectile : MonoBehaviour
    {
        Actor owner;
        AbilitySpec spec;
        Vector3 velocity;
        float detonateAt,spawnAt;
        bool gravity,done;
        public int OwnerId => owner ? owner.Id : -1;
        public string AbilityId => spec?.Id;
        public static void Launch(Actor owner,Vector3 origin,Vector3 velocity,AbilitySpec spec,float fuse,bool gravity)
        {
            if (!owner || owner.IsNetworkReplica) return;
            // A launch point inside a solid is invalid, including wood: projectiles never penetrate it.
            foreach(var c in Physics.OverlapSphere(origin,.002f,~0,QueryTriggerInteraction.Ignore))
                if(!c.GetComponentInParent<Actor>() && (c.ClosestPoint(origin)-origin).sqrMagnitude<.000001f)return;
            var obj=new GameObject(spec.Name);obj.transform.position=origin;
            EquipmentModels.BuildProjectile(obj.transform,spec.Id);
            var p=obj.AddComponent<AbilityProjectile>();p.owner=owner;p.velocity=velocity;p.spec=spec;p.gravity=gravity;p.spawnAt=owner.Match.MatchTime;p.detonateAt=fuse>0?CombatClock.AfterSeconds(p.spawnAt,fuse):-1;
            if(spec.Id=="rocket_launcher")obj.transform.rotation=Quaternion.FromToRotation(Vector3.up,velocity.normalized);
        }
        void FixedUpdate()
        {
            if(done||!owner||owner.IsNetworkReplica||owner.Match.Phase!=MatchPhase.Playing||GameRoot.Instance.SimulationPaused)return;
            float now=owner.Match.MatchTime;
            if(detonateAt>=0&&now>=detonateAt){Explode();return;}
            float dt=Time.fixedDeltaTime;if(gravity)velocity+=Physics.gravity*dt;
            Vector3 step=velocity*dt;
            foreach(var hit in Physics.SphereCastAll(transform.position,.07f,step.normalized,step.magnitude,~0,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance)) {
                var actor=hit.collider.GetComponentInParent<Actor>();if(actor==owner)continue;
                transform.position=hit.point+hit.normal*.09f;
                if(detonateAt<0){Explode();return;}
                velocity=Vector3.Reflect(velocity,hit.normal)*.46f;return;
            }
            transform.position+=step;
        }
        void Explode()
        {
            if(done)return;done=true;
            bool emp=spec.Id=="emp_grenade";Vector3 center=transform.position;
            AbilityEffects.Pulse(center,emp?Color.cyan:new Color(1,.46f,.07f),spec.Radius);
            AbilityEffects.Sound(center,emp?230:55,.5f);
            foreach(var actor in owner.Match.Actors.ToArray()) {
                if(!actor.Alive||actor!=owner&&!owner.Match.IsEnemy(owner,actor))continue;
                Vector3 target=actor.transform.position+Vector3.up;float distance=Vector3.Distance(center,target);
                if(distance>=spec.Radius||!Visible(center,target,actor))continue;
                if(emp) {
                    if(actor.IsSpawnProtected)continue;
                    Action apply=()=>{if(actor.Alive&&!actor.IsSpawnProtected)actor.Ability.ApplyEMP();};
                    if(!owner.Match.TryQueueDamage(apply))apply();
                }
                else actor.ApplyDamage(spec.Damage*(1-distance/spec.Radius),owner,true,false,"chest",1);
            }
            Destroy(gameObject);
        }
        static bool Visible(Vector3 origin,Vector3 target,Actor victim)
        {
            Vector3 d=target-origin;
            foreach(var hit in Physics.RaycastAll(origin,d.normalized,d.magnitude,~0,QueryTriggerInteraction.Ignore)) {
                if(hit.collider.GetComponentInParent<Actor>())continue;
                return false;
            }return true;
        }
    }

    public sealed class LootPickup : MonoBehaviour
    {
        public string Id;
        MatchDirector match;
        float expires;
        public float Expires => expires;
        public Vector3 Anchor => anchor;
        Vector3 anchor;
        public static void Create(string id,Vector3 point,MatchDirector match,float expires)
        {
            var go=new GameObject("Loot_"+id);go.transform.position=point+Vector3.up*.6f;
            var l=go.AddComponent<LootPickup>();l.Id=id;l.match=match;l.expires=expires;l.anchor=go.transform.position;
            EquipmentModels.BuildLoot(go.transform,id);
            Color color=id=="medkit"?new Color(.8f,.13f,.16f):id=="protective_field"?Color.cyan:id=="haste"?Color.yellow:id=="fast_reload"?new Color(1,.5f,.1f):id=="stabilizer"?Color.green:Color.magenta;
            var ring=new GameObject("Ring").AddComponent<LineRenderer>();ring.transform.SetParent(go.transform,false);ring.useWorldSpace=false;ring.loop=true;ring.positionCount=24;ring.startWidth=ring.endWidth=.025f;ring.sharedMaterial=AbilityEffects.Material(color);
            for(int i=0;i<24;i++){float a=i*Mathf.PI*2/24;ring.SetPosition(i,new Vector3(Mathf.Cos(a)*.42f,-.2f,Mathf.Sin(a)*.42f));}
        }
        bool Available=>gameObject.activeInHierarchy && match && match.Phase==MatchPhase.Playing && match.MatchTime<expires;
        void Update(){if(match.MatchTime>=expires){gameObject.SetActive(false);Destroy(gameObject);return;}transform.position=anchor+Vector3.up*Mathf.Sin(Time.time*2)*.12f;transform.Rotate(0,Time.deltaTime*35,0);}
        public static void TryNearest(Actor actor)
        {
            if(!actor||actor.IsNetworkReplica||!actor.Alive||actor.Match.IsTeamMode||actor.Match.Phase!=MatchPhase.Playing||GameRoot.Instance.SimulationPaused)return;
            var pickup=FindNearby(actor,1.5f,true);
            if(!pickup)return;
            actor.Ability.ApplyLoot(pickup.Id);pickup.gameObject.SetActive(false);Destroy(pickup.gameObject);
        }
        public static LootPickup FindNearby(Actor actor,float radius,bool requireAim)
        {
            if(!actor||!actor.Alive||actor.Match.IsTeamMode)return null;
            var candidates=FindObjectsByType<LootPickup>(FindObjectsSortMode.None)
                .Where(p=>p.Available && p.match==actor.Match && Vector3.Distance(p.transform.position,actor.transform.position+Vector3.up)<=radius)
                .OrderBy(p=>Vector3.Distance(p.transform.position,actor.EyePosition));
            foreach(var p in candidates) {
                if(p.Id=="medkit"&&actor.HP>=100)continue;
                Vector3 delta=p.transform.position-actor.EyePosition;
                if(requireAim&&Vector3.Dot(actor.Aim.forward,delta.normalized)<.5f)continue;
                if(Physics.RaycastAll(actor.EyePosition,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore).Any(h=>!h.collider.GetComponentInParent<Actor>()))continue;
                return p;
            }
            return null;
        }
        public static string DisplayName(string id)
        {
            switch(id){case "medkit":return "Аптечка";case "protective_field":return "Защитное поле";case "haste":return "Ускорение";case "fast_reload":return "Быстрая перезарядка";case "stabilizer":return "Стабилизатор";case "vampirism":return "Вампиризм";default:return id;}
        }
        public static string PromptFor(Actor actor)
        {
            var p=FindNearby(actor,1.5f,true);
            return p?GameInput.Label("use")+" · "+DisplayName(p.Id):null;
        }
    }

    public sealed class AbilityEffects : MonoBehaviour
    {
        float until,grow;
        Vector3 startScale;
        static readonly Dictionary<Color,Material> materials=new Dictionary<Color,Material>();
        public static Material Material(Color color)
        {
            if(materials.TryGetValue(color,out var m))return m;
            m=new Material(Shader.Find("Universal Render Pipeline/Unlit")??Shader.Find("Unlit/Color"));m.color=color;materials[color]=m;return m;
        }
        public static void Pulse(Vector3 position,Color color,float radius)
        {
            if(GameRoot.Instance?.Profile?.reducedFlashes??false)color*=.45f;
            // Rings remain readable without an opaque sphere hiding the whole firefight.
            var go=new GameObject("AbilityRing");go.transform.position=position;
            var lr=go.AddComponent<LineRenderer>();lr.sharedMaterial=Material(color);lr.startWidth=.06f;lr.endWidth=.015f;lr.useWorldSpace=false;lr.loop=true;lr.positionCount=40;
            for(int i=0;i<40;i++){float a=i*Mathf.PI*2/40;lr.SetPosition(i,new Vector3(Mathf.Cos(a),.1f,Mathf.Sin(a)));}
            var fx=go.AddComponent<AbilityEffects>();fx.until=Time.time+.4f;fx.grow=radius;fx.startScale=Vector3.one*.12f;
        }
        public static void Beam(Vector3 from,Vector3 to,Color color,float width,float life)
        {
            if(GameRoot.Instance?.Profile?.reducedFlashes??false){color*=.45f;life=Mathf.Min(life,.05f);width*=.6f;}
            var go=new GameObject("Beam");var line=go.AddComponent<LineRenderer>();line.sharedMaterial=Material(color);line.startWidth=width;line.endWidth=width*.5f;line.positionCount=2;line.SetPosition(0,from);line.SetPosition(1,to);Destroy(go,life);
        }
        public static void Sound(Vector3 position,float frequency,float duration)
        {
            int samples=(int)(22050*duration);var data=new float[samples];var rng=new System.Random(71);
            for(int i=0;i<samples;i++){float t=i/22050f;data[i]=(Mathf.Sin(t*frequency*Mathf.PI*2)*.3f+(float)(rng.NextDouble()-.5)*.7f)*Mathf.Exp(-t*9);}
            var clip=AudioClip.Create("Ability",samples,1,22050,false);clip.SetData(data,0);
            var go=new GameObject("AbilitySound");go.transform.position=position;var source=go.AddComponent<AudioSource>();source.clip=clip;source.spatialBlend=1;source.minDistance=3;source.maxDistance=70;source.volume=.8f*(GameRoot.Instance?.Profile?.effectsVolume??1);source.Play();Destroy(go,duration+.1f);Destroy(clip,duration+.2f);
        }
        void Update(){float remaining=until-Time.time;if(remaining<=0){Destroy(gameObject);return;}transform.localScale=Vector3.Lerp(startScale,Vector3.one*grow,1-remaining/.4f);}
    }
}
