using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

namespace ZoneZ
{
    // Only the host runs the simulation. Clients send controls, never positions or damage.
    [DefaultExecutionOrder(-300)]
    public sealed class NetworkSession : MonoBehaviour
    {
        public const int ProtocolVersion=1;
        public bool IsHost { get; private set; }
        public bool IsClient { get; private set; }
        public bool IsConnecting { get; private set; }
        public bool IsActive => IsHost || IsClient;
        public string Status { get; private set; }="";
        public string LastError { get; private set; }="";
        public string RoomCode { get; private set; }="";
        public string LocalAddress { get; private set; }="";
        public int Port { get; private set; }=7777;
        public int HumanCount => IsHost ? 1+peers.Values.Count(p=>p.actor!=null) : Root.Match.Actors.Count(a=>a.Human||a.RemoteControlled);
        public int SnapshotsReceived { get; private set; }
        public int CommandsReceived { get; private set; }
        public bool UsesRelay => relayed;
        public bool TestInputOverride;
        public string ContentHash { get; private set; }
        sealed class Peer
        {
            public Actor actor;
            public Actor reserved;
            public float connected,seen,window;
            public float rejectAt;
            public int sequence=-1,messages,team=-1;
        }
        sealed class Pose
        {
            public Vector3 from,target;
            public float at;
        }
        NetworkTransport transport;
        bool relayed;
        bool fillBots=true;
        readonly Dictionary<int,Peer> peers=new Dictionary<int,Peer>();
        readonly Dictionary<int,Pose> poses=new Dictionary<int,Pose>();
        readonly Dictionary<int,float> respawns=new Dictionary<int,float>();
        readonly Dictionary<int,GameObject> loot=new Dictionary<int,GameObject>(), projectiles=new Dictionary<int,GameObject>();
        readonly JsonSerializerSettings json=new JsonSerializerSettings { MaxDepth=32,TypeNameHandling=TypeNameHandling.None,FloatParseHandling=FloatParseHandling.Double };
        string nickname,joinCode;
        int localId=-1,sequence;
        float lastReceive,nextSnapshot,nextInput,nextHeartbeat,connectStarted;
        NetworkCommand pending;
        GameRoot Root => GameRoot.Instance;

        void Awake()
        {
            Application.runInBackground=true;
            var balance=Resources.LoadAll<TextAsset>("Balance").OrderBy(a=>a.name).Select(a=>a.name+":"+a.text);
            using(var hash=SHA256.Create())ContentHash=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes("zonez-net-1|"+string.Join("|",balance)))).Replace("-","");
        }
        static string Name(string value)
        {
            string result=new string((value??"").Where(c=>!char.IsControl(c)&&c!='<'&&c!='>').Take(20).ToArray()).Trim();
            return result.Length==0?"Игрок":result;
        }
        static string Code(string value)=>new string((value??"").Trim().ToUpperInvariant().Where(char.IsLetterOrDigit).Take(24).ToArray());
        static string RandomCode()
        {
            byte[] bytes=new byte[8];using(var rng=RandomNumberGenerator.Create())rng.GetBytes(bytes);
            const string alphabet="ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            return new string(bytes.Select(b=>alphabet[b%alphabet.Length]).ToArray());
        }
        public bool Host(string playerName,string modeId,int port,string roomCode)
        {
            if(IsActive || IsConnecting || Root.Match.Phase!=MatchPhase.Menu)return false;
            if(port<1024||port>65535){LastError="Порт должен быть от 1024 до 65535.";return false;}
            try {
                transport=new NetworkTransport();transport.Listen(port);
                IsHost=true;IsClient=false;fillBots=true;Port=port;RoomCode=Code(roomCode);if(RoomCode.Length==0)RoomCode=RandomCode();
                nickname=Name(playerName);LastError="";Status="Сервер открыт · ожидаем друзей";localId=0;sequence=0;
                LocalAddress=LocalAddresses();nextSnapshot=0;
                Root.Match.StartMatch(modeId);Root.Match.Local.DisplayName=nickname;Root.SetPaused(false);
                Debug.Log("ZONEZ_NETWORK_HOST_READY port="+Port);
                return true;
            } catch(Exception e){Stop();LastError="Не удалось открыть сервер: "+e.Message;Status=LastError;return false;}
        }
        public void Join(string playerName,string address,int port,string roomCode)
        {
            if(IsActive||IsConnecting||Root.Match.Phase!=MatchPhase.Menu)return;
            if(port<1024||port>65535||string.IsNullOrWhiteSpace(address)){LastError="Укажите адрес сервера и порт от 1024 до 65535.";return;}
            nickname=Name(playerName);joinCode=Code(roomCode);RoomCode=joinCode;Port=port;LocalAddress=address.Trim();
            LastError="";Status="Подключение к "+LocalAddress+":"+port+"…";IsConnecting=true;
            connectStarted=lastReceive=Time.realtimeSinceStartup;sequence=0;pending=null;localId=-1;
            try {transport=new NetworkTransport();transport.Connect(LocalAddress,port);}
            catch(Exception e){Fail("Не удалось подключиться: "+e.Message);}
        }
        public void Stop(string reason="")
        {
            if(relayed){relayed=false;Root.Lobby?.Leave();}
            transport?.Dispose();transport=null;peers.Clear();IsHost=IsClient=IsConnecting=false;
            pending=null;localId=-1;poses.Clear();respawns.Clear();ClearObjects(loot);ClearObjects(projectiles);
            if(!string.IsNullOrEmpty(reason))Status=reason;
            if(Root)Root.SetPaused(false);
        }
        void Fail(string reason)
        {
            Stop(reason);LastError=reason;
            if(Root && Root.Match)Root.Match.ReturnToMenu();
            Root?.Notify(reason);
        }
        void OnDestroy()=>Stop();
        void OnApplicationQuit()=>Stop();
        public float ReplicaRespawn(int id)=>respawns.TryGetValue(id,out float value)?Mathf.Max(0,value-Time.realtimeSinceStartup):0;
        static string LocalAddresses()
        {
            try {var addresses=Dns.GetHostAddresses(Dns.GetHostName()).Where(a=>a.AddressFamily==AddressFamily.InterNetwork&&!IPAddress.IsLoopback(a)).Select(a=>a.ToString()).ToArray();return addresses.Length>0?string.Join(" / ",addresses):"127.0.0.1";}
            catch{return "127.0.0.1";}
        }
        void SendPayload(int peer,string payload){if(relayed)Root.Lobby.SendGame(peer,payload);else transport?.Send(peer,payload);}
        void Send(int peer,NetworkMessage message)=>SendPayload(peer,JsonConvert.SerializeObject(message,json));
        public void StartRelayed(bool host,string mode,string code,string name,LobbyPlayer[] players,int leader,bool bots)
        {
            Stop();Root.Match.ReturnToMenu();relayed=true;fillBots=bots;nickname=Name(name);RoomCode=joinCode=code;
            LocalAddress=Root.Lobby.ServerAddress;Port=0;LastError="";sequence=0;pending=null;
            connectStarted=lastReceive=Time.realtimeSinceStartup;nextSnapshot=nextInput=nextHeartbeat=0;
            if(host) {
                IsHost=true;localId=0;Root.Match.StartMatch(mode,bots?0:players.Length,bots?12:players.Count(p=>p.team==0));Root.Match.Local.DisplayName=nickname;
                if(!bots)foreach(var actor in Root.Match.Actors)if(!actor.Human){var brain=actor.GetComponent<BotBrain>();if(brain)brain.enabled=false;}
                var reserved=new HashSet<int>{0};
                foreach(var player in players)if(player.id!=leader) {
                    Actor slot=Root.Match.Actors.FirstOrDefault(a=>!reserved.Contains(a.Id)&&(!Root.Match.IsTeamMode||a.Team==player.team));
                    if(slot)reserved.Add(slot.Id);
                    peers[player.id]=new Peer{connected=lastReceive,seen=lastReceive,window=lastReceive,team=player.team,reserved=slot};
                }
                Status="Матч через сервер лобби";
            } else {
                IsConnecting=true;localId=-1;Status="Входим в найденный матч…";
                Send(0,new NetworkMessage{type="hello",protocol=ProtocolVersion,content=ContentHash,nickname=nickname,code=joinCode,primary=Root.Profile.primaryId,pistol=Root.Profile.pistolId,ability=Root.Profile.ultimateId});
            }
        }
        public void ReceiveRelayed(int peer,string payload)=>Handle(new TransportEvent{Kind=TransportEventKind.Data,PeerId=peer,Payload=payload},Time.realtimeSinceStartup);
        public void RelayPeerLeft(int peer){if(IsHost)RemovePeer(peer);}
        public void RelayEnded(string reason){if(relayed){relayed=false;Fail(reason);}}
        void Update()
        {
            if(transport==null&&!relayed)return;
            float now=Time.realtimeSinceStartup;
            try {
                int processed=0;
                while(transport!=null && processed++<512 && transport.TryPoll(out TransportEvent evt))Handle(evt,now);
                if(transport==null&&!relayed)return;
                if(IsHost) {
                    foreach(var entry in peers.ToArray())if(now-entry.Value.seen>(entry.Value.actor?15:8)||entry.Value.actor==null&&now-entry.Value.connected>8||entry.Value.rejectAt>0&&now>=entry.Value.rejectAt)RemovePeer(entry.Key);
                    if(now>=nextSnapshot){nextSnapshot=now+1f/15;BroadcastSnapshot();}
                } else {
                    if(now-lastReceive>15 || IsConnecting&&now-connectStarted>12){Fail("Сервер не отвечает. Проверьте адрес, код, порт и доступ через интернет/VPN.");return;}
                    if(IsClient && now>=nextInput && pending!=null) {
                        nextInput=now+1f/30;pending.sequence=++sequence;
                        Send(0,new NetworkMessage{type="input",input=pending});
                        pending.jump=pending.firePressed=pending.fireReleased=pending.alt=pending.reload=pending.use=pending.ultimate=false;pending.slot=-1;
                    }
                    if(now>=nextHeartbeat){nextHeartbeat=now+2;Send(0,new NetworkMessage{type="ping"});}
                    Interpolate(now);
                }
            } catch(Exception e){Debug.LogWarning("Network session: "+e.Message);Fail("Соединение прервано: некорректные данные или ошибка сети.");}
        }
        void Handle(TransportEvent evt,float now)
        {
            if(evt.Kind==TransportEventKind.Connected) {
                if(IsHost)peers[evt.PeerId]=new Peer{connected=now,seen=now,window=now};
                else Send(0,new NetworkMessage{type="hello",protocol=ProtocolVersion,content=ContentHash,nickname=nickname,code=joinCode,primary=Root.Profile.primaryId,pistol=Root.Profile.pistolId,ability=Root.Profile.ultimateId});
                return;
            }
            if(evt.Kind==TransportEventKind.Disconnected||evt.Kind==TransportEventKind.Error) {
                if(IsHost){if(evt.PeerId>0)RemovePeer(evt.PeerId);else if(evt.Kind==TransportEventKind.Error)Fail("Ошибка сервера: "+evt.Payload);}
                else Fail("Связь с сервером потеряна. Создатель мог закрыть матч. "+(evt.Kind==TransportEventKind.Error?evt.Payload:""));
                return;
            }
            if(evt.Kind!=TransportEventKind.Data)return;
            if(IsHost && evt.Payload.Length>4096){RemovePeer(evt.PeerId);return;}
            NetworkMessage message;
            try {message=JsonConvert.DeserializeObject<NetworkMessage>(evt.Payload,json);}
            catch {if(IsHost)RemovePeer(evt.PeerId);else Fail("Сервер прислал повреждённый пакет.");return;}
            if(message==null)return;
            if(IsHost) {
                if(!peers.TryGetValue(evt.PeerId,out Peer peer))return;
                if(now-peer.window>=1){peer.window=now;peer.messages=0;}
                if(++peer.messages>100){RemovePeer(evt.PeerId);return;}
                peer.seen=now;
                if(peer.rejectAt>0)return;
                if(peer.actor==null) {
                    if(message.type=="hello")Accept(evt.PeerId,peer,message);
                    else if(message.type!="ping")RemovePeer(evt.PeerId);
                    return;
                }
                if(message.type=="input" && Valid(message.input) && message.input.sequence>peer.sequence) {
                    peer.sequence=message.input.sequence;peer.actor.GetComponent<PlayerMotor>()?.ApplyNetworkCommand(message.input);CommandsReceived++;
                } else if(message.type!="ping" && message.type!="input")RemovePeer(evt.PeerId);
            } else {
                lastReceive=now;
                if(message.type=="reject"){Fail(message.reason??"Сервер отказал в подключении.");return;}
                if(message.type=="welcome" && IsConnecting && message.protocol==ProtocolVersion && message.content==ContentHash) {
                    if(message.actorId<0||message.actorId>=24){Fail("Некорректный номер игрока.");return;}
                    localId=message.actorId;IsConnecting=false;IsClient=true;Status=relayed?"Матч через сервер лобби":"Подключено · "+LocalAddress+":"+Port;
                }
                if(message.type=="snapshot" && IsClient && message.snapshot!=null)ApplySnapshot(message.snapshot);
            }
        }
        void Accept(int peerId,Peer peer,NetworkMessage message)
        {
            string error=null;
            if(message.protocol!=ProtocolVersion||message.content!=ContentHash)error="Разные версии игры. Передайте друзьям одну и ту же сборку.";
            else if(Code(message.code)!=RoomCode)error="Неверный код комнаты.";
            else if(Root.Match.Phase==MatchPhase.Results)error="Матч уже завершён. Создатель должен открыть новый.";
            if(error!=null){Send(peerId,new NetworkMessage{type="reject",reason=error});peer.rejectAt=Time.realtimeSinceStartup+.3f;return;}
            var match=Root.Match;
            int team=peer.team>=0?peer.team:match.Actors.Count(a=>(a.Human||a.RemoteControlled)&&a.Team==0)<=match.Actors.Count(a=>(a.Human||a.RemoteControlled)&&a.Team==1)?0:1;
            var actor=peer.reserved?peer.reserved:match.Actors.FirstOrDefault(a=>!a.Human&&!a.RemoteControlled&&(!match.IsTeamMode||a.Team==team))??match.Actors.FirstOrDefault(a=>!a.Human&&!a.RemoteControlled);
            if(!actor){Send(peerId,new NetworkMessage{type="reject",reason="В матче нет свободных мест."});peer.rejectAt=Time.realtimeSinceStartup+.3f;return;}
            actor.SetNetworkRole(false,true);actor.DisplayName=Name(message.nickname);peer.actor=actor;
            string primary=Root.Catalog.Weapon(message.primary)?.Slot=="primary"?message.primary:"weapon_ak47";
            string pistol=Root.Catalog.Weapon(message.pistol)?.Slot=="pistol"?message.pistol:"weapon_glock";
            actor.Weapon.EquipLoadout(primary,pistol);
            actor.Ability.Initialize(actor,Root.Catalog.Ability(message.ability)!=null?message.ability:"frag_grenade");actor.Ability.ResetForSpawn(false);
            Send(peerId,new NetworkMessage{type="welcome",protocol=ProtocolVersion,content=ContentHash,actorId=actor.Id});
            Send(peerId,new NetworkMessage{type="snapshot",snapshot=CaptureSnapshot()});
            Status="Сервер открыт · игроков "+HumanCount;Root.Notify(actor.DisplayName+" подключился");
            Debug.Log("ZONEZ_NETWORK_JOIN actor="+actor.Id);
        }
        void RemovePeer(int id)
        {
            if(peers.TryGetValue(id,out Peer peer)) {
                Actor departed=peer.actor?peer.actor:peer.reserved;
                if(departed){
                    if(relayed&&!fillBots){departed.Kill(null,false);Root.Match.DropNetworkActor(departed);departed.gameObject.SetActive(false);Destroy(departed.gameObject);}
                    else {departed.DisplayName="Бот "+departed.Id;departed.SetNetworkRole(false,false);}
                }
                peers.Remove(id);
            }
            transport?.Disconnect(id);if(IsHost)Status="Сервер открыт · игроков "+HumanCount;
        }
        static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        static bool Valid(NetworkCommand c)=>c!=null&&c.sequence>0&&Finite(c.moveX)&&Finite(c.moveY)&&Finite(c.yaw)&&Finite(c.pitch)&&Mathf.Abs(c.moveX)<=1&&Mathf.Abs(c.moveY)<=1&&Mathf.Abs(c.yaw)<1000000&&Mathf.Abs(c.pitch)<=89&&c.slot>=-1&&c.slot<4;
        public void SubmitInput(NetworkCommand input)
        {
            if(!IsClient||input==null)return;
            if(pending!=null) {
                input.jump|=pending.jump;input.firePressed|=pending.firePressed;input.fireReleased|=pending.fireReleased;
                input.alt|=pending.alt;input.reload|=pending.reload;input.use|=pending.use;input.ultimate|=pending.ultimate;
                if(input.slot<0)input.slot=pending.slot;
            }
            pending=input;
        }
        void BroadcastSnapshot()
        {
            if(!peers.Values.Any(p=>p.actor))return;
            string payload=JsonConvert.SerializeObject(new NetworkMessage{type="snapshot",snapshot=CaptureSnapshot()},json);
            foreach(var entry in peers)if(entry.Value.actor)SendPayload(entry.Key,payload);
        }
        public NetworkSnapshot CaptureSnapshot()
        {
            var m=Root.Match;
            return new NetworkSnapshot {
                matchId=m.MatchId,mode=m.ModeId,phase=(int)m.Phase,tick=m.Tick,round=m.RoundNumber,time=m.MatchTime,remaining=m.TimeRemaining,preparation=m.PreparationRemaining,
                wins=m.RoundWins,winner=m.WinnerText,roundReason=m.RoundReason,feed=m.KillFeed.ToArray(),feedTimes=m.KillFeedTimes.ToArray(),
                actors=m.Actors.Select(a=>new NetworkActorState{id=a.Id,team=a.Team,name=a.DisplayName,human=a.Human||a.RemoteControlled,alive=a.Alive,crouch=a.IsCrouching,
                    x=a.transform.position.x,y=a.transform.position.y,z=a.transform.position.z,yaw=a.transform.eulerAngles.y,pitch=Mathf.DeltaAngle(0,a.Aim.localEulerAngles.x),vx=a.CurrentVelocity.x,vy=a.CurrentVelocity.y,vz=a.CurrentVelocity.z,
                    hp=a.HP,maxHp=a.MaxHP,armor=a.Armor,protection=a.SpawnProtectedUntil,kills=a.Kills,deaths=a.Deaths,assists=a.Assists,score=a.Score,respawn=m.RespawnRemaining(a),weapon=a.Weapon.CaptureNetworkState(),ability=a.Ability.CaptureNetworkState()}).ToArray(),
                loot=FindObjectsByType<LootPickup>(FindObjectsSortMode.None).Where(p=>p.gameObject.activeInHierarchy).Select(p=>new NetworkObjectState{key=p.GetInstanceID(),kind=p.Id,x=p.Anchor.x,y=p.Anchor.y-.6f,z=p.Anchor.z,expires=p.Expires}).ToArray(),
                projectiles=FindObjectsByType<AbilityProjectile>(FindObjectsSortMode.None).Select(p=>new NetworkObjectState{key=p.GetInstanceID(),kind=p.AbilityId,x=p.transform.position.x,y=p.transform.position.y,z=p.transform.position.z,rx=p.transform.eulerAngles.x,ry=p.transform.eulerAngles.y,rz=p.transform.eulerAngles.z}).ToArray()
            };
        }
        void ApplySnapshot(NetworkSnapshot s)
        {
            if(s.actors==null||s.actors.Length<1||s.actors.Length>24||s.wins==null||s.wins.Length!=2||s.phase<1||s.phase>4||s.actors.Select(a=>a.id).Distinct().Count()!=s.actors.Length||!s.actors.Any(a=>a.id==localId))throw new InvalidOperationException("snapshot");
            var m=Root.Match;bool first=m.MatchId!=s.matchId;var oldPhase=m.Phase;
            if(first){m.CreateReplicaMatch(s.mode,s.matchId);poses.Clear();}
            foreach(var gone in m.Actors.Where(a=>!s.actors.Any(state=>state.id==a.Id)).ToArray()) {poses.Remove(gone.Id);m.Actors.Remove(gone);gone.gameObject.SetActive(false);Destroy(gone.gameObject);}
            m.Phase=(MatchPhase)s.phase;m.Tick=s.tick;m.RoundNumber=s.round;m.MatchTime=s.time;m.TimeRemaining=s.remaining;m.PreparationRemaining=s.preparation;m.RoundWins=s.wins;m.WinnerText=s.winner;m.RoundReason=s.roundReason;m.EarnedCredits=0;
            m.KillFeed.Clear();m.KillFeed.AddRange((s.feed??Array.Empty<string>()).Take(5));m.KillFeedTimes.Clear();m.KillFeedTimes.AddRange((s.feedTimes??Array.Empty<float>()).Take(5));
            foreach(var state in s.actors) {
                var actor=m.Actors.Find(a=>a.Id==state.id);
                if(!actor) {
                    var go=new GameObject("NetworkActor_"+state.id);go.transform.SetParent(m.transform);actor=go.AddComponent<Actor>();
                    actor.Initialize(m,state.id,state.name,state.team,state.id==localId);actor.Ability=go.AddComponent<AbilityController>();actor.Ability.Initialize(actor,"frag_grenade");m.Actors.Add(actor);
                }
                bool local=state.id==localId;
                if(actor.Human!=local||actor.RemoteControlled!=(state.human&&!local))actor.SetNetworkRole(local,state.human&&!local);
                if(local)m.Local=actor;
                actor.DisplayName=state.name;actor.MaxHP=state.maxHp;actor.Kills=state.kills;actor.Deaths=state.deaths;actor.Assists=state.assists;actor.Score=state.score;
                Vector3 target=new Vector3(state.x,state.y,state.z);bool snap=first||actor.Alive!=state.alive||Vector3.Distance(actor.transform.position,target)>5;
                actor.ApplyReplicaState(snap?target:actor.transform.position,state.yaw,state.pitch,state.alive,state.hp,state.armor,state.crouch,new Vector3(state.vx,state.vy,state.vz),state.protection);
                poses[state.id]=new Pose{from=actor.transform.position,target=target,at=Time.realtimeSinceStartup};
                actor.Ability.ApplyNetworkState(state.ability);actor.Weapon.ApplyNetworkState(state.weapon);
                respawns[state.id]=Time.realtimeSinceStartup+state.respawn;
            }
            if(m.IsTeamMode && m.Phase==MatchPhase.Results) {int result=s.wins[m.Local.Team].CompareTo(s.wins[1-m.Local.Team]);m.WinnerText=result>0?"ПОБЕДА":result<0?"ПОРАЖЕНИЕ":"НИЧЬЯ";}
            else if(!m.IsTeamMode && m.Phase==MatchPhase.Results)m.WinnerText=m.Rank(m.Local)==1?"ПОБЕДА":"МЕСТО "+m.Rank(m.Local);
            else if(m.IsTeamMode&&m.Phase==MatchPhase.RoundEnd)m.WinnerText="Раунд завершён · "+s.wins[0]+" : "+s.wins[1];
            SyncObjects(s.loot??Array.Empty<NetworkObjectState>(),loot,true);SyncObjects(s.projectiles??Array.Empty<NetworkObjectState>(),projectiles,false);
            if(first||oldPhase!=m.Phase)Root.SetPaused(false);
            SnapshotsReceived++;
        }
        void Interpolate(float now)
        {
            foreach(var actor in Root.Match.Actors)if(poses.TryGetValue(actor.Id,out Pose pose)) {
                bool enabled=actor.Motor&&actor.Motor.enabled;if(enabled)actor.Motor.enabled=false;
                actor.transform.position=Vector3.Lerp(pose.from,pose.target,Mathf.Clamp01((now-pose.at)*15));
                if(enabled)actor.Motor.enabled=true;
            }
        }
        void SyncObjects(NetworkObjectState[] states,Dictionary<int,GameObject> objects,bool isLoot)
        {
            if(states.Length>(isLoot?12:100))throw new InvalidOperationException("objects");
            var keys=new HashSet<int>(states.Select(s=>s.key));
            foreach(var key in objects.Keys.Where(k=>!keys.Contains(k)).ToArray()){if(objects[key]){objects[key].SetActive(false);Destroy(objects[key]);}objects.Remove(key);}
            foreach(var state in states) {
                if(!objects.TryGetValue(state.key,out GameObject go)||!go) {
                    if(isLoot) {
                        LootPickup.Create(state.kind,new Vector3(state.x,state.y,state.z),Root.Match,state.expires);
                        go=FindObjectsByType<LootPickup>(FindObjectsSortMode.None).Where(l=>!objects.Values.Contains(l.gameObject)).Last().gameObject;
                    } else {
                        go=new GameObject("Network projectile");
                        EquipmentModels.BuildProjectile(go.transform,state.kind);
                    }
                    objects[state.key]=go;
                }
                if(!isLoot)go.transform.SetPositionAndRotation(new Vector3(state.x,state.y,state.z),Quaternion.Euler(state.rx,state.ry,state.rz));
            }
        }
        void ClearObjects(Dictionary<int,GameObject> objects){foreach(var go in objects.Values)if(go){go.SetActive(false);Destroy(go);}objects.Clear();}
    }
}
