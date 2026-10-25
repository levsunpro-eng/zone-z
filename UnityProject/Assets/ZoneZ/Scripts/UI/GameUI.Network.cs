using System;
using UnityEngine;

namespace ZoneZ
{
    public sealed partial class GameUI
    {
        bool networkOnline, networkDirect, networkHost=true, networkTeam=true;
        string networkNickname="Игрок", networkAddress="", networkPort="7777";
        string networkHostCode=Guid.NewGuid().ToString("N").Substring(0,6).ToUpperInvariant();
        string networkJoinCode="", networkFormError="";
        int networkAddressIndex;
        bool NetworkActive => root.Network!=null&&root.Network.IsActive;
        bool NetworkConnecting => root.Network!=null&&root.Network.IsConnecting;

        void CancelPendingConnection()
        {
            if(NetworkConnecting) root.Network.Stop("Подключение отменено");
        }

        void LeaveNetworkForm()
        {
            CancelPendingConnection();
            root.Lobby?.Disconnect();
            networkOnline=false; networkFormError=""; focusedButton=0;
        }

        void SelectNetworkService(bool direct)
        {
            if(networkDirect==direct) return;
            CancelPendingConnection();
            if(direct) root.Lobby?.Disconnect();
            networkDirect=direct; networkFormError=""; focusedButton=0;
        }

        void NetworkPlay()
        {
            Rect form=new Rect(ox+352,322,744,489), help=new Rect(ox+1118,322,422,489);
            Fill(form,panel); Fill(new Rect(form.x,form.y,form.width,3),green); Fill(help,panel);
            bool connecting=NetworkConnecting;
            if(Button(new Rect(form.x+26,form.y+22,339,44),"Создать матч",networkHost,!connecting)) { networkHost=true; networkFormError=""; }
            if(Button(new Rect(form.x+379,form.y+22,339,44),"Подключиться",!networkHost,!connecting)) { networkHost=false; networkFormError=""; }
            bool previous=GUI.enabled; GUI.enabled=previous&&!connecting;
            Text(new Rect(form.x+26,form.y+84,692,26),"ВАШ НИК",small,muted);
            networkNickname=TextInput(new Rect(form.x+26,form.y+115,692,43),networkNickname,20,"network-nickname");
            if(networkHost) {
                Text(new Rect(form.x+26,form.y+177,170,26),"TCP-ПОРТ",small,muted);
                networkPort=TextInput(new Rect(form.x+26,form.y+208,170,43),networkPort,5,"network-port");
                Text(new Rect(form.x+214,form.y+177,504,26),"КОД КОМНАТЫ",small,muted);
                networkHostCode=TextInput(new Rect(form.x+214,form.y+208,504,43),networkHostCode,32,"network-host-code");
                Text(new Rect(form.x+26,form.y+270,692,26),"РЕЖИМ",small,muted);
                if(Button(new Rect(form.x+26,form.y+301,339,44),"Командный бой · 12 × 12",networkTeam)) networkTeam=true;
                if(Button(new Rect(form.x+379,form.y+301,339,44),"Deathmatch · 20 игроков",!networkTeam)) networkTeam=false;
            } else {
                Text(new Rect(form.x+26,form.y+177,504,26),"АДРЕС СЕРВЕРА (IP ИЛИ ИМЯ)",small,muted);
                networkAddress=TextInput(new Rect(form.x+26,form.y+208,504,43),networkAddress,128,"network-address");
                Text(new Rect(form.x+548,form.y+177,170,26),"TCP-ПОРТ",small,muted);
                networkPort=TextInput(new Rect(form.x+548,form.y+208,170,43),networkPort,5,"network-port");
                Text(new Rect(form.x+26,form.y+270,692,26),"КОД КОМНАТЫ ОТ СОЗДАТЕЛЯ",small,muted);
                networkJoinCode=TextInput(new Rect(form.x+26,form.y+301,692,44),networkJoinCode,32,"network-join-code");
            }
            GUI.enabled=previous;
            string error=!string.IsNullOrEmpty(networkFormError)?networkFormError:root.Network?.LastError;
            string status=connecting?root.Network.Status:!string.IsNullOrEmpty(error)?error:networkHost?"После запуска: 15 секунд подготовки, свободные места займут боты.":"Режим задаёт создатель. Можно подключиться к уже начавшемуся бою.";
            Text(new Rect(form.x+26,form.y+362,692,48),status,small,!string.IsNullOrEmpty(error)&&!connecting?new Color(1,.6f,.4f):muted);
            if(connecting) {
                if(Button(new Rect(form.x+26,form.y+424,692,44),"Отменить подключение",true)) CancelPendingConnection();
            } else {
                if(Button(new Rect(form.x+26,form.y+424,486,44),root.Store.WriteBlocked?"Восстановите профиль":networkHost?"Запустить сервер и войти":"Войти в матч",true,!root.Store.WriteBlocked)) StartNetworkGame();
                if(Button(new Rect(form.x+530,form.y+424,188,44),"Назад")) LeaveNetworkForm();
            }
            Text(new Rect(help.x+26,help.y+26,370,45),"ИГРА ИЗ РАЗНЫХ ДОМОВ",heading,gold);
            Text(new Rect(help.x+26,help.y+87,370,103),"Создатель передаёт друзьям адрес, порт и код комнаты. Его игра должна оставаться запущенной.",label);
            Text(new Rect(help.x+26,help.y+211,370,107),"ИНТЕРНЕТ\nПубличный IPv4 и проброс выбранного TCP-порта (обычно 7777) — либо общая VPN-сеть и адрес VPN.",small,muted);
            Text(new Rect(help.x+26,help.y+333,370,50),"ОДНА СЕТЬ WI-FI / LAN\nИспользуйте локальный IP создателя.",small,muted);
            Fill(new Rect(help.x+26,help.y+402,370,1),muted*.3f);
            Text(new Rect(help.x+26,help.y+416,370,55),"Код — пароль комнаты, не поиск сервера. В сетевых матчах кредиты не начисляются.",small,gold);
        }

        void StartNetworkGame()
        {
            networkFormError="";
            root.Lobby?.Disconnect();
            if(root.Network==null) { networkFormError="Сеть пока недоступна. Перезапустите игру."; return; }
            networkNickname=networkNickname.Trim();
            if(string.IsNullOrEmpty(networkNickname)) { networkFormError="Введите ник для таблицы игроков."; return; }
            if(!int.TryParse(networkPort,out int port)||port<1024||port>65535) { networkFormError="Укажите TCP-порт от 1024 до 65535."; return; }
            if(networkHost) {
                networkHostCode=networkHostCode.Trim();
                if(string.IsNullOrEmpty(networkHostCode)) { networkFormError="Задайте код комнаты и передайте его друзьям."; return; }
                root.Network.Host(networkNickname,networkTeam?"team_elimination":"deathmatch",port,networkHostCode);
            } else {
                networkAddress=networkAddress.Trim(); networkJoinCode=networkJoinCode.Trim();
                if(string.IsNullOrEmpty(networkAddress)) { networkFormError="Введите IP-адрес или имя сервера друга."; return; }
                if(string.IsNullOrEmpty(networkJoinCode)) { networkFormError="Введите код комнаты от создателя."; return; }
                root.Network.Join(networkNickname,networkAddress,port,networkJoinCode);
            }
        }

        void NetworkDetails(Rect r)
        {
            if(!NetworkActive) return;
            var network=root.Network;
            if(network.UsesRelay) {
                Text(new Rect(r.x,r.y,r.width,26),"МАТЧ ЧЕРЕЗ СЕРВЕР ЛОББИ",small,gold);
                Text(new Rect(r.x,r.y+33,r.width,39),(network.Status??"В сети")+" · Игроков: "+network.HumanCount,small,green);
                Text(new Rect(r.x,r.y+85,r.width,49),root.Lobby?.ServerAddress??"Служба лобби",label);
                Text(new Rect(r.x,r.y+149,r.width,75),network.IsHost?"Ваш компьютер проводит матч. Оставьте игру запущенной до конца боя. Новые приглашения доступны в лобби перед следующим матчем.":"Матч продолжается при открытом меню. Новых друзей можно пригласить в лобби перед следующим матчем.",small,muted);
                return;
            }
            string address=network.LocalAddress;
            string[] addresses=(address??"").Split(new[]{" / "},StringSplitOptions.RemoveEmptyEntries);
            bool multiple=network.IsHost&&addresses.Length>1;
            if(multiple) { networkAddressIndex%=addresses.Length; address=addresses[networkAddressIndex]; }
            string endpoint=string.IsNullOrEmpty(address)?"Адрес определяется…":(network.IsHost?"IP в вашей сети: ":"Сервер: ")+address+":"+network.Port;
            Text(new Rect(r.x,r.y,r.width,26),network.IsHost?"СЕРВЕР НА ВАШЕМ КОМПЬЮТЕРЕ":"ПОДКЛЮЧЕНИЕ К СЕРВЕРУ",small,gold);
            Text(new Rect(r.x,r.y+33,r.width,39),(network.Status??"В сети")+" · Игроков: "+network.HumanCount,small,green);
            Text(new Rect(r.x,r.y+80,r.width-(multiple?354:178),38),endpoint,label);
            if(multiple&&Button(new Rect(r.xMax-342,r.y+77,166,38),"IP "+(networkAddressIndex+1)+" / "+addresses.Length+" · сменить")) networkAddressIndex=(networkAddressIndex+1)%addresses.Length;
            if(Button(new Rect(r.xMax-166,r.y+77,166,38),"Скопировать IP",false,!string.IsNullOrEmpty(address))) { GUIUtility.systemCopyBuffer=address; Notify("Адрес скопирован. Порт: "+network.Port); }
            Text(new Rect(r.x,r.y+129,r.width-178,38),"Код: "+network.RoomCode,label,gold);
            if(Button(new Rect(r.xMax-166,r.y+126,166,38),"Скопировать код")) { GUIUtility.systemCopyBuffer=network.RoomCode; Notify("Код комнаты скопирован"); }
            Text(new Rect(r.x,r.y+177,r.width,47),network.IsHost?"Для интернета нужен публичный IPv4 + проброс TCP-порта или адрес общей VPN. Код не заменяет адрес.":"Меню не останавливает матч. Соединение зависит от компьютера создателя.",small,muted);
        }

        void OnlineLoadout(Rect r)
        {
            Fill(r,card);
            var actor=root.Match.Local;
            Text(new Rect(r.x+20,r.y+18,r.width-40,31),"СНАРЯЖЕНИЕ НА ЭТОТ МАТЧ",small,gold);
            Text(new Rect(r.x+20,r.y+62,r.width-40,51),Catalog.Weapon(Profile.primaryId).Name+"   /   "+Catalog.Weapon(Profile.pistolId).Name,heading);
            Text(new Rect(r.x+20,r.y+126,r.width-40,45),"Ульта: "+Catalog.Ability(actor.Ability.EquippedId).Name,label);
            Text(new Rect(r.x+20,r.y+184,r.width-40,54),"Сменить снаряжение можно в главном меню перед следующим подключением.",small,muted);
        }
    }
}
