using System;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace ZoneZ
{
    [Serializable] public sealed class LobbyPlayer { public int id,team; public string name; }
    [Serializable] public sealed class LobbyMessage
    {
        public string service,name,mode,version,code,message,role,payload,error;
        public int id,selfId,leader,peer,peerId;
        public bool searching,bots;
        public LobbyPlayer[] players;
    }

    [DefaultExecutionOrder(-350)]
    public sealed class LobbyClient : MonoBehaviour
    {
        public bool Connected { get; private set; }
        public bool Connecting { get; private set; }
        public bool InLobby { get; private set; }
        public bool IsLeader => InLobby&&selfId==LeaderId;
        public bool Searching { get; private set; }
        public bool IsBusy => Connected||Connecting;
        public string Status { get; private set; }="";
        public string LastError { get; private set; }="";
        public string Code { get; private set; }="";
        public string ServerAddress { get; private set; }="";
        public string Nickname { get; private set; }="Игрок";
        public int LeaderId { get; private set; }
        public LobbyPlayer[] Players { get; private set; }=Array.Empty<LobbyPlayer>();
        public bool InGame { get; private set; }
        NetworkTransport connection;
        int selfId;
        float started,lastReceived,nextPing;
        GameRoot Root=>GameRoot.Instance;
        readonly JsonSerializerSettings json=new JsonSerializerSettings{MaxDepth=16,TypeNameHandling=TypeNameHandling.None};
        void Awake()
        {
            // A release can ship the real service address once hosting is configured.
            // No account, secret, or server address is invented in an offline build.
            string file=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../online-server.txt"));
            try {if(System.IO.File.Exists(file))ServerAddress=System.IO.File.ReadAllText(file).Trim();}
            catch(System.IO.IOException){Status="Не удалось прочитать адрес сервера.";}
        }
        public void Connect(string endpoint,string nickname)
        {
            if(InGame){LastError="Сначала выйдите из текущего матча.";return;}
            Disconnect();LastError="";
            try {
                if(!Uri.TryCreate(endpoint.Trim(),UriKind.Absolute,out Uri uri)||(uri.Scheme!="tls"&&uri.Scheme!="tcp")||uri.Port<1||uri.Port>65535||string.IsNullOrEmpty(uri.Host))throw new ArgumentException("Адрес: tls://server.example:7780 или tcp://127.0.0.1:7780.");
                Nickname=new string((nickname??"Игрок").Where(c=>!char.IsControl(c)&&c!='<'&&c!='>').Take(20).ToArray());if(string.IsNullOrWhiteSpace(Nickname))Nickname="Игрок";
                ServerAddress=endpoint.Trim();Connecting=true;Status="Подключение к серверу лобби…";
                started=lastReceived=Time.realtimeSinceStartup;connection=new NetworkTransport();connection.Connect(uri.Host,uri.Port,uri.Scheme=="tls");
            }catch(Exception e){Error(e.Message);}
        }
        public void Create(string mode)=>Request(new LobbyMessage{service="create",name=Nickname,mode=mode=="deathmatch"?"deathmatch":"team",version=Root.Network.ContentHash});
        public void Join(string code)=>Request(new LobbyMessage{service="join",name=Nickname,code=(code??"").Trim().ToUpperInvariant(),version=Root.Network.ContentHash});
        public void Search(){if(IsLeader)Request(new LobbyMessage{service="search",searching=true});}
        public void CancelSearch(){if(IsLeader)Request(new LobbyMessage{service="cancel"});}
        public void StartWithBots(){if(IsLeader)Request(new LobbyMessage{service="bots"});}
        public void Leave()
        {
            if(Connected)Request(new LobbyMessage{service="leave"});
            InLobby=Searching=InGame=false;Players=Array.Empty<LobbyPlayer>();Code="";Status=Connected?"Выберите или создайте лобби":"";
        }
        public void Disconnect()
        {
            bool wasGame=InGame;InGame=false;connection?.Dispose();connection=null;Connected=Connecting=InLobby=Searching=false;Players=Array.Empty<LobbyPlayer>();Code="";
            if(wasGame&&Root&&Root.Network)Root.Network.RelayEnded("Соединение с сервером лобби закрыто.");
        }
        void Error(string message)
        {
            Disconnect();LastError=message;Status=message;Root?.Notify(message);
        }
        void Request(LobbyMessage message)
        {
            if(!Connected)return;
            connection.Send(0,JsonConvert.SerializeObject(message,json));
        }
        public void SendGame(int peer,string payload){if(InGame)Request(new LobbyMessage{service="game",peer=peer,payload=payload});}
        void Update()
        {
            if(connection==null)return;
            try {
                int budget=512;
                while(connection!=null&&budget-->0&&connection.TryPoll(out TransportEvent evt)) {
                    if(evt.Kind==TransportEventKind.Error||evt.Kind==TransportEventKind.Disconnected){Error("Сервер лобби недоступен: "+evt.Payload);return;}
                    if(evt.Kind!=TransportEventKind.Data)continue;
                    lastReceived=Time.realtimeSinceStartup;
                    var message=JsonConvert.DeserializeObject<LobbyMessage>(evt.Payload,json);
                    if(message==null)continue;
                    switch(message.service) {
                        case "welcome":selfId=message.id;Connected=true;Connecting=false;Status="Сервер доступен · создайте лобби или введите код";break;
                        case "lobby":
                            InLobby=true;Searching=message.searching;Code=message.code;LeaderId=message.leader;Players=message.players??Array.Empty<LobbyPlayer>();
                            LastError="";Status=Searching?"Ищем соперников… Можно отменить поиск.":"Лобби · пригласите друзей по коду";break;
                        case "start":
                            InLobby=Searching=false;InGame=true;Players=message.players??Array.Empty<LobbyPlayer>();Code=message.code;LeaderId=message.leader;
                            Root.Network.StartRelayed(message.role=="host",message.mode,message.code,Nickname,Players,message.leader,message.bots);Status="Матч начался";break;
                        case "game":if(InGame)Root.Network.ReceiveRelayed(message.peer,message.payload);break;
                        case "peer_left":if(InGame)Root.Network.RelayPeerLeft(message.peer);break;
                        case "ended":
                            InGame=InLobby=Searching=false;Players=Array.Empty<LobbyPlayer>();
                            Root.Network.RelayEnded(message.message??"Матч закрыт создателем.");Status=message.message;break;
                        case "left":InLobby=Searching=false;Players=Array.Empty<LobbyPlayer>();break;
                        case "error":
                            LastError=message.error=="not_found"?"Лобби не найдено. Проверьте код приглашения.":message.error=="version_mismatch"?"У игроков разные версии игры.":message.error=="searching"?"Лидер должен отменить поиск, чтобы пригласить друга.":message.error=="full"?"В лобби нет свободных мест.":"Сервер отклонил запрос: "+message.message;
                            Status=LastError;Root.Notify(LastError);break;
                    }
                }
                float now=Time.realtimeSinceStartup;
                if(Connecting&&now-started>12||Connected&&now-lastReceived>20){Error("Сервер лобби не отвечает. Проверьте адрес и соединение.");return;}
                if(Connected&&now>=nextPing){nextPing=now+5;Request(new LobbyMessage{service="ping"});}
            }catch(Exception e){Debug.LogWarning("Lobby: "+e.Message);Error("Ошибка соединения с лобби: "+e.Message);}
        }
        void OnDestroy()=>Disconnect();
        void OnApplicationQuit()=>Disconnect();
    }
}
