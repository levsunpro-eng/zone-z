using UnityEngine;

namespace ZoneZ
{
    public sealed partial class GameUI
    {
        string lobbyEndpoint="", lobbyJoinCode="", lobbyFormError="";
        bool lobbyFieldsLoaded;
        Vector2 lobbyPlayersScroll;
        bool LobbyMatch => NetworkActive&&root.Network.UsesRelay;

        void LobbyPlay()
        {
            Rect form=new Rect(ox+352,322,744,489), help=new Rect(ox+1118,322,422,489);
            Fill(form,panel); Fill(new Rect(form.x,form.y,form.width,3),green); Fill(help,panel);
            var lobby=root.Lobby;
            if(lobby!=null&&!lobbyFieldsLoaded) {
                lobbyEndpoint=lobby.ServerAddress??"";
                if(!string.IsNullOrWhiteSpace(lobby.Nickname)) networkNickname=lobby.Nickname;
                lobbyFieldsLoaded=true;
            }
            if(NetworkConnecting) {
                Text(new Rect(form.x+26,form.y+27,692,46),"ПОДКЛЮЧЕНИЕ К МАТЧУ",heading,gold);
                Text(new Rect(form.x+26,form.y+112,692,95),root.Network.Status,heading);
                Text(new Rect(form.x+26,form.y+246,692,88),"Лобби подготовило сервер. Дождитесь подключения всех участников.",label,muted);
                if(Button(new Rect(form.x+26,form.y+424,692,44),"Отменить подключение")) { CancelPendingConnection(); lobby?.Disconnect(); }
            } else if(lobby==null||!lobby.Connected) LobbyConnection(form);
            else if(!lobby.InLobby) LobbyChoose(form);
            else LobbyRoom(form);
            LobbyHelp(help);
        }

        void LobbyConnection(Rect r)
        {
            var lobby=root.Lobby;
            bool connecting=lobby!=null&&lobby.Connecting;
            Text(new Rect(r.x+26,r.y+27,692,46),"ЛОББИ С ДРУЗЬЯМИ",heading,gold);
            Text(new Rect(r.x+26,r.y+89,692,54),"Подключитесь к общей службе, пригласите друзей по коду, затем запускайте поиск соперников.",label,muted);
            bool previous=GUI.enabled; GUI.enabled=previous&&!connecting;
            Text(new Rect(r.x+26,r.y+162,692,26),"ВАШ НИК",small,muted);
            networkNickname=TextInput(new Rect(r.x+26,r.y+193,692,43),networkNickname,20,"lobby-nickname");
            Text(new Rect(r.x+26,r.y+259,692,26),"АДРЕС СЛУЖБЫ ЛОББИ",small,muted);
            lobbyEndpoint=TextInput(new Rect(r.x+26,r.y+290,692,43),lobbyEndpoint,180,"lobby-endpoint");
            GUI.enabled=previous;
            string error=LobbyError();
            string status=connecting?lobby.Status:!string.IsNullOrEmpty(error)?error:string.IsNullOrWhiteSpace(lobbyEndpoint)?"Сервер ещё не настроен. Для игры через интернет нужен опубликованный сервер лобби.":"Адрес службы должен быть одинаковым у вас и у друзей.";
            Text(new Rect(r.x+26,r.y+349,692,60),status,small,!string.IsNullOrEmpty(error)?new Color(1,.6f,.4f):muted);
            if(connecting) {
                if(Button(new Rect(r.x+26,r.y+424,692,44),"Отменить подключение")) lobby.Disconnect();
            } else {
                if(Button(new Rect(r.x+26,r.y+424,486,44),"Подключиться к службе",true,lobby!=null&&!root.Store.WriteBlocked&&!string.IsNullOrWhiteSpace(lobbyEndpoint))) ConnectLobby();
                if(Button(new Rect(r.x+530,r.y+424,188,44),"Назад")) LeaveNetworkForm();
            }
        }

        void ConnectLobby()
        {
            lobbyFormError=""; networkNickname=networkNickname.Trim(); lobbyEndpoint=lobbyEndpoint.Trim();
            if(string.IsNullOrEmpty(networkNickname)) { lobbyFormError="Введите ник для списка друзей."; return; }
            if(string.IsNullOrEmpty(lobbyEndpoint)) { lobbyFormError="Укажите адрес опубликованного сервера лобби."; return; }
            CancelPendingConnection();
            root.Lobby.Connect(lobbyEndpoint,networkNickname);
        }

        void LobbyChoose(Rect r)
        {
            var lobby=root.Lobby;
            Text(new Rect(r.x+26,r.y+27,692,46),"СОБЕРИТЕ ДРУЗЕЙ",heading,gold);
            Text(new Rect(r.x+26,r.y+82,692,42),"Создайте своё лобби или введите код приглашения.",label,muted);
            if(Button(new Rect(r.x+26,r.y+141,339,44),"Командный бой",networkTeam)) networkTeam=true;
            if(Button(new Rect(r.x+379,r.y+141,339,44),"Deathmatch",!networkTeam)) networkTeam=false;
            if(Button(new Rect(r.x+26,r.y+201,692,44),"Создать лобби",true)) { lobbyFormError=""; lobby.Create(networkTeam?"team_elimination":"deathmatch"); }
            Fill(new Rect(r.x+26,r.y+270,692,1),muted*.25f);
            Text(new Rect(r.x+26,r.y+287,692,26),"КОД ЛОББИ ДРУГА",small,muted);
            lobbyJoinCode=TextInput(new Rect(r.x+26,r.y+318,470,44),lobbyJoinCode,32,"lobby-join-code");
            if(Button(new Rect(r.x+512,r.y+318,206,44),"Войти по коду",false,!string.IsNullOrWhiteSpace(lobbyJoinCode))) { lobbyFormError=""; lobby.Join(lobbyJoinCode.Trim()); }
            LobbyStatus(new Rect(r.x+26,r.y+378,692,35));
            if(Button(new Rect(r.x+26,r.y+424,692,44),"Отключиться от службы")) lobby.Disconnect();
        }

        void LobbyRoom(Rect r)
        {
            var lobby=root.Lobby;
            Text(new Rect(r.x+26,r.y+27,355,43),"ЛОББИ · "+lobby.Code,heading,gold);
            if(Button(new Rect(r.x+408,r.y+25,150,41),"Копировать код")) { GUIUtility.systemCopyBuffer=lobby.Code; Notify("Код лобби скопирован"); }
            if(Button(new Rect(r.x+573,r.y+25,145,41),"Приглашение")) {
                GUIUtility.systemCopyBuffer="ЗОНА Z — присоединяйся к лобби!\nСлужба: "+lobby.ServerAddress+"\nКод лобби: "+lobby.Code;
                Notify("Адрес службы и код лобби скопированы");
            }
            Text(new Rect(r.x+26,r.y+83,692,31),(lobby.IsLeader?"Вы — лидер лобби":"Лидер лобби управляет поиском")+" · Игроков: "+(lobby.Players?.Length??0),small,muted);
            var players=lobby.Players;
            int count=players?.Length??0;
            Rect list=new Rect(r.x+26,r.y+127,692,154);
            Fill(list,card);
            lobbyPlayersScroll=BeginListScroll(list,lobbyPlayersScroll,new Rect(0,0,668,Mathf.Max(150,count*38)));
            for(int i=0;i<count;i++) {
                var player=players[i];
                if(player==null) continue;
                if(i%2==0) Fill(new Rect(0,i*38,666,37),new Color(.15f,.18f,.15f,.55f));
                Text(new Rect(13,i*38+6,482,29),player.name, label);
                Text(new Rect(499,i*38+7,152,28),player.id==lobby.LeaderId?"◆ ЛИДЕР":"В ЛОББИ",small,player.id==lobby.LeaderId?gold:green);
            }
            if(count==0) Text(new Rect(13,13,638,35),"Участники подключаются…",small,muted);
            EndListScroll(ref lobbyPlayersScroll);
            LobbyStatus(new Rect(r.x+26,r.y+295,692,39));
            if(lobby.Searching) {
                if(Button(new Rect(r.x+26,r.y+344,692,44),lobby.IsLeader?"Отменить поиск соперников":"Идёт поиск · дождитесь лидера",true,lobby.IsLeader)) lobby.CancelSearch();
            } else {
                if(Button(new Rect(r.x+26,r.y+344,692,44),lobby.IsLeader?"Найти соперников":"Поиск запускает лидер лобби",true,lobby.IsLeader)) { lobbyFormError=""; lobby.Search(); }
            }
            if(Button(new Rect(r.x+26,r.y+407,436,42),"Отдельный матч с ботами",false,lobby.IsLeader&&!lobby.Searching)) { lobbyFormError=""; lobby.StartWithBots(); }
            if(Button(new Rect(r.x+478,r.y+407,240,42),"Покинуть лобби")) { lobbyFormError=""; lobby.Leave(); }
            Text(new Rect(r.x+26,r.y+458,692,24),"Поиск подбирает людей. Боты добавляются только в отдельном режиме.",small,muted);
        }

        string LobbyError() => !string.IsNullOrEmpty(lobbyFormError)?lobbyFormError:root.Lobby?.LastError;

        void LobbyStatus(Rect r)
        {
            string error=LobbyError();
            bool searching=root.Lobby!=null&&root.Lobby.Searching;
            string status=!string.IsNullOrEmpty(error)?error:root.Lobby?.Status;
            if(string.IsNullOrEmpty(status)) status=searching?"Ищем соперников среди игроков на этой службе…":"Готово к игре";
            Text(r,status,small,!string.IsNullOrEmpty(error)?new Color(1,.6f,.4f):searching?gold:green);
        }

        void LobbyHelp(Rect r)
        {
            Text(new Rect(r.x+26,r.y+26,370,64),"ДРУЗЬЯ → ЛОББИ → БОЙ",heading,gold);
            Text(new Rect(r.x+26,r.y+108,370,76),"01  ПРИГЛАСИТЕ ДРУЗЕЙ\nОтправьте им адрес службы и код своего лобби.",label);
            Text(new Rect(r.x+26,r.y+205,370,85),"02  НАЙДИТЕ СОПЕРНИКОВ\nЛидер запускает поиск. Матч начнётся, когда соберутся игроки.",label);
            Text(new Rect(r.x+26,r.y+311,370,68),"03  БОТЫ — ОТДЕЛЬНО\nТренировка не подменяет поиск людей.",label);
            Fill(new Rect(r.x+26,r.y+397,370,1),muted*.3f);
            Text(new Rect(r.x+26,r.y+412,370,62),"Адрес сервиса: tls://ваш-сервер:7780\nЛокальная проверка: tcp://127.0.0.1:7780\nСнаряжение выберите до входа в матч.",small,muted);
        }
    }
}
