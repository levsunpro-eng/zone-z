using System;
using System.Linq;
using UnityEngine;

namespace ZoneZ
{
    public sealed partial class GameUI
    {
        Vector2 loadoutPrimaryScroll, loadoutPistolScroll, scoreScroll;
        int previousStreak;

        void Hud()
        {
            var match=root.Match; var actor=match.Local;
            if(!actor) return;
            Minimap();
            float mid=uiW*.5f;
            Fill(new Rect(mid-240,20,480,86),new Color(.03f,.05f,.045f,.88f));
            Text(new Rect(mid-228,29,456,26),match.IsTeamMode?"КОМАНДНЫЙ БОЙ  ·  РАУНД "+match.RoundNumber+" / 3":"DEATHMATCH  ·  КАЖДЫЙ ЗА СЕБЯ",small,muted);
            Text(new Rect(mid-74,58,148,43),Clock(match.TimeRemaining),heading);
            if(match.IsTeamMode) {
                int ownTeam=actor.Team;
                Text(new Rect(mid-216,59,138,36),match.RoundWins[ownTeam]+" : "+match.RoundWins[1-ownTeam],heading,gold);
                Text(new Rect(mid+90,60,138,36),match.Actors.Count(a=>a.Alive&&a.Team==ownTeam)+" × "+match.Actors.Count(a=>a.Alive&&a.Team!=ownTeam)+" живы",small,green);
            } else {
                Text(new Rect(mid-216,61,138,33),actor.Score+" очков",small,gold);
                Text(new Rect(mid+88,61,144,33),"Место "+match.Rank(actor),small,green);
            }
            var ability=actor.Ability; var weapon=actor.Weapon;
            Fill(new Rect(28,798,260,76),new Color(.03f,.05f,.045f,.76f));
            Text(new Rect(41,805,234,34),Mathf.CeilToInt(actor.HP)+" / "+Mathf.CeilToInt(actor.MaxHP)+" HP",heading,actor.HP<30?new Color(1,.48f,.36f):foreground);
            Meter(new Rect(42,841,232,3),actor.HP/Mathf.Max(1,actor.MaxHP),green);
            Text(new Rect(41,849,234,23),"Броня "+Mathf.CeilToInt(actor.Armor)+"  ·  Щит "+Mathf.CeilToInt(actor.Shield),small,muted);
            if(weapon&&weapon.Spec!=null) {
                Fill(new Rect(uiW-308,798,280,76),new Color(.03f,.05f,.045f,.76f));
                string gun=ability&&ability.HasTemporaryWeapon?Catalog.Ability(ability.EquippedId).Name:weapon.Spec.Name;
                Text(new Rect(uiW-294,803,252,25),gun,label);
                string ammo=ability&&ability.HasTemporaryWeapon?ability.RemainingShots+" выстр.":weapon.Slot=="knife"?"—":weapon.Ammo+" / "+weapon.Reserve;
                Text(new Rect(uiW-294,831,133,37),ammo,heading,gold);
                string mode=ability&&ability.HasTemporaryWeapon?"Временное · "+GameInput.Label("fire"):weapon.BurstMode?"Очередь":weapon.Spec.Automatic?"Автоматический":"Одиночный";
                if(weapon.Slot=="zeus") mode=weapon.ZeusRechargeRemaining>0?"Готов через "+weapon.ZeusRechargeRemaining.ToString("0.0")+" с":"Заряд готов";
                if(weapon.Slot=="knife") mode=GameInput.Label("fire")+" / "+GameInput.Label("alternate");
                Text(new Rect(uiW-156,837,114,30),weapon.Reloading?"Перезарядка…":mode,small,muted);
                if(weapon.Reloading) Meter(new Rect(uiW-294,869,252,3),weapon.ReloadProgress,gold);
            }
            if(ability) {
                AbilitySpec spec=Catalog.Ability(ability.EquippedId);
                Fill(new Rect(28,732,370,56),new Color(.03f,.05f,.045f,.76f));
                Text(new Rect(41,735,344,24),GameInput.Label("ultimate")+"  ·  "+spec.Name,label,gold);
                string state=ability.Blocked?"ЭМИ · "+ability.BlockedRemaining.ToString("0.0")+" с":ability.ActiveRemaining>0?"Действует · "+ability.ActiveRemaining.ToString("0.0")+" с":ability.Ready?"ГОТОВО · нажмите "+GameInput.Label("ultimate"):ability.Streak+" / "+spec.KillsRequired+" · "+(ability.Streak>0?ability.StreakRemaining.ToString("0.0")+" с":"нужна серия за 20 с");
                Text(new Rect(41,761,344,23),state,small,ability.Ready?green:muted);
                Meter(new Rect(42,785,342,2),ability.Ready?1:(float)ability.Streak/spec.KillsRequired,ability.Ready?green:gold);
                if(previousStreak>0&&ability.Streak==0&&!ability.Ready&&actor.Alive&&ability.ActiveRemaining<=0) Notify("Серия завершена");
                previousStreak=ability.Streak;
                float y=690;
                if(actor.IsSpawnProtected) { Effect(28,y,"ЗАЩИТА ПОЯВЛЕНИЯ",(actor.SpawnProtectedUntil-match.MatchTime).ToString("0.0")+" с",green); y-=38; }
                if(!string.IsNullOrEmpty(ability.LootId)) { Effect(28,y,"ЛУТ · "+LootPickup.DisplayName(ability.LootId),ability.LootRemaining.ToString("0.0")+" с",gold); y-=38; }
                if(ability.ActiveRemaining>0) { Effect(28,y,"УЛЬТА · "+spec.Name,ability.ActiveRemaining.ToString("0.0")+" с",green); y-=38; }
                if(ability.Blocked) Effect(28,y,"БЛОКИРОВКА ЭМИ",ability.BlockedRemaining.ToString("0.0")+" с",new Color(1,.6f,.36f));
            }
            if(actor.Alive&&match.Phase==MatchPhase.Playing&&!root.Paused) {
                Crosshair(actor,mid);
                string prompt=LootPickup.PromptFor(actor);
                if(prompt!=null) {
                    Fill(new Rect(mid-275,535,550,49),panel);
                    Text(new Rect(mid-258,546,516,32),prompt,center,gold);
                }
                WorldMarkers(actor);
            }
            if(match.KillFeed.Count>0) {
                for(int i=0;i<match.KillFeed.Count;i++) {
                    if(i<match.KillFeedTimes.Count&&match.MatchTime-match.KillFeedTimes[i]>=8) continue;
                    string text=match.KillFeed[i];
                    Actor killer=match.Actors.FirstOrDefault(a=>text.StartsWith(a.DisplayName+" ",StringComparison.Ordinal));
                    Color c=killer==actor?gold:killer&&match.IsTeamMode&&killer.Team==actor.Team?green:foreground;
                    float feedY=CurrentMinimapPanel().yMax+16+i*38;
                    Fill(new Rect(uiW-425,feedY,397,32),new Color(.03f,.05f,.045f,.8f));
                    Text(new Rect(uiW-410,feedY+5,366,25),text,small,c);
                }
            }
            if(!actor.Alive&&match.Phase==MatchPhase.Playing) {
                if(match.IsTeamMode) {
                    Fill(new Rect(mid-340,589,680,102),panel);
                    Text(new Rect(mid-320,604,640,37),"Ожидание следующего раунда",heading);
                    Text(new Rect(mid-320,650,640,29),GameInput.Label("fire")+" / "+GameInput.Label("alternate")+" — сменить союзника · "+GameInput.Label("scoreboard")+" — счёт",small,muted);
                } else DeathLoadout();
            }
            Text(new Rect(28,24,420,28),GameInput.Label("scoreboard")+" — счёт · Esc — "+(NetworkActive?"меню матча":"пауза"),small,muted);
            if(NetworkActive) Text(new Rect(28,61,420,45),(root.Network.IsHost?"ВЫ — СЕРВЕР":"В СЕТИ")+" · Игроков: "+root.Network.HumanCount+(LobbyMatch?"\nСтатус соединения — в меню Esc":"\nАдрес и код комнаты — в меню Esc"),small,green);
        }

        void Meter(Rect r,float progress,Color color) { Fill(r,new Color(.2f,.24f,.21f)); Fill(new Rect(r.x,r.y,r.width*Mathf.Clamp01(progress),r.height),color); }
        void Effect(float x,float y,string title,string remaining,Color color) {
            Fill(new Rect(x,y,336,32),new Color(.03f,.05f,.045f,.76f));
            Text(new Rect(x+12,y+4,245,25),title,small,color); Text(new Rect(x+267,y+4,58,25),remaining,small);
        }
        void Crosshair(Actor actor,float mid) {
            var gun=actor.Weapon; if(!gun) return;
            float y=450, gap=gun.Scoped?4:Mathf.Clamp(5+gun.CurrentInaccuracy*170,5,33);
            float thickness=Profile.crosshairThickness;
            if(!ColorUtility.TryParseHtmlString("#"+Profile.crosshairColor,out Color crosshair)) crosshair=foreground;
            if(gun.Scoped) {
                Fill(new Rect(mid-thickness*.5f,285,thickness,330),crosshair);
                Fill(new Rect(mid-195,y-thickness*.5f,390,thickness),crosshair);
            } else {
                Fill(new Rect(mid-gap-10,y-thickness*.5f,10,thickness),crosshair); Fill(new Rect(mid+gap,y-thickness*.5f,10,thickness),crosshair);
                Fill(new Rect(mid-thickness*.5f,y-gap-10,thickness,10),crosshair); Fill(new Rect(mid-thickness*.5f,y+gap,thickness,10),crosshair);
            }
            bool kill=root.Match.MatchTime-gun.KillAt<.28f, hit=root.Match.MatchTime-gun.HitAt<.16f;
            if(kill||hit) {
                Color color=kill?gold:Color.white;
                Line(new Vector2(mid-7,y-7),new Vector2(mid-14,y-14),color,2);
                Line(new Vector2(mid+7,y-7),new Vector2(mid+14,y-14),color,2);
                Line(new Vector2(mid-7,y+7),new Vector2(mid-14,y+14),color,2);
                Line(new Vector2(mid+7,y+7),new Vector2(mid+14,y+14),color,2);
            }
        }
        void Line(Vector2 a,Vector2 b,Color color,float width) {
            Matrix4x4 before=GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(b.y-a.y,b.x-a.x)*Mathf.Rad2Deg,a);
            Fill(new Rect(a.x,a.y-width*.5f,Vector2.Distance(a,b),width),color); GUI.matrix=before;
        }
        void WorldMarkers(Actor local) {
            Camera camera=local.ViewCamera; if(!camera||!camera.isActiveAndEnabled) return;
            if(root.Match.IsTeamMode) foreach(var a in root.Match.Actors) {
                if(a==local||!a.Alive||a.Team!=local.Team||Vector3.Distance(a.transform.position,local.transform.position)>45) continue;
                Marker(camera,a.transform.position+Vector3.up*2.1f,"◆ "+a.DisplayName,green);
            }
            if(local.Ability&&local.Ability.EquippedId=="recon_pulse") foreach(var mark in local.Ability.Revealed) {
                if(mark.Key&&mark.Key.Alive) Marker(camera,mark.Value+Vector3.up*2.1f,"[ РАЗВЕДКА ]",gold);
            }
        }
        void Marker(Camera camera,Vector3 position,string text,Color color) {
            Vector3 screen=camera.WorldToScreenPoint(position); if(screen.z<=0) return;
            float scale=Mathf.Min(Screen.width/1600f,Screen.height/900f);
            float x=screen.x/scale, y=(Screen.height-screen.y-(Screen.height-900*scale)/2)/scale;
            if(x<40||x>uiW-40||y<115||y>690) return;
            Text(new Rect(x-110,y-12,220,28),text,center,color);
        }

        void Preparation() {
            Fill(new Rect(ox+290,170,1020,554),panel);
            Text(new Rect(ox+316,189,720,50),NetworkActive?"Подготовка к сетевому бою":"Выберите снаряжение",heading);
            Text(new Rect(ox+1075,190,207,50),Mathf.CeilToInt(root.Match.PreparationRemaining)+" с",heading,gold);
            Text(new Rect(ox+316,244,968,48),"Бой начнётся автоматически. Ульта: "+Catalog.Ability(root.Match.Local.Ability.EquippedId).Name,small,muted);
            if(NetworkActive) {
                OnlineLoadout(new Rect(ox+316,303,968,274));
                Text(new Rect(ox+336,598,928,48),LobbyMatch?"Матч найден. Участники лобби подключаются к общему бою.\nEsc — меню матча и состояние соединения.":"Свободные места занимают боты. Друзья могут войти позже.\nEsc — адрес сервера и код комнаты для приглашения.",small,green);
            } else LoadoutChooser(new Rect(ox+316,303,968,350));
            Text(new Rect(ox+316,673,968,30),NetworkActive?"Сетевой матч · Кредиты не начисляются":root.Match.IsTeamMode?"Прокачка действует · Нож и Zeus всегда с собой":"Прокачка отключена · 100 HP · Базовая скорость и урон",small,gold);
        }
        void LoadoutChooser(Rect r) {
            float width=(r.width-22)/2;
            Text(new Rect(r.x,r.y,width,29),"ОСНОВНОЕ ОРУЖИЕ",small,gold);
            Text(new Rect(r.x+width+22,r.y,width,29),"ПИСТОЛЕТ",small,gold);
            LoadoutColumn(new Rect(r.x,r.y+37,width,r.height-37),"primary",ref loadoutPrimaryScroll);
            LoadoutColumn(new Rect(r.x+width+22,r.y+37,width,r.height-37),"pistol",ref loadoutPistolScroll);
        }
        void LoadoutColumn(Rect r,string slot,ref Vector2 scroll) {
            var choices=Catalog.Weapons.Where(w=>w.Slot==slot).ToList();
            scroll=BeginListScroll(r,scroll,new Rect(0,0,r.width-23,choices.Count*43));
            for(int i=0;i<choices.Count;i++) {
                var item=choices[i]; bool chosen=item.Id==(slot=="primary"?Profile.primaryId:Profile.pistolId);
                if(Button(new Rect(0,i*43,r.width-25,36),item.Name,chosen,!chosen)) {
                    if(root.Store.SelectWeapon(item.Id,out string error)) {
                        if(root.Match.Phase==MatchPhase.Preparation&&root.Match.Local) root.Match.Local.Weapon.EquipLoadout(Profile.primaryId,Profile.pistolId);
                    } else Notify(error);
                }
            }
            EndListScroll(ref scroll);
        }
        void DeathLoadout() {
            Fill(new Rect(ox+385,235,830,362),panel);
            Text(new Rect(ox+408,253,784,45),"Возрождение через "+root.Match.RespawnRemaining(root.Match.Local).ToString("0.0")+" с",heading,gold);
            string cause=root.Match.KillFeed.FirstOrDefault(s=>s.EndsWith(" "+root.Match.Local.DisplayName,StringComparison.Ordinal));
            Text(new Rect(ox+408,306,784,29),cause??(NetworkActive?"Вы погибли. Ожидание возрождения на сервере.":"Вы погибли. Выберите оружие для следующего появления."),small,muted);
            if(NetworkActive) {
                Text(new Rect(ox+408,363,784,51),Catalog.Weapon(Profile.primaryId).Name+"   /   "+Catalog.Weapon(Profile.pistolId).Name,heading);
                Text(new Rect(ox+408,434,784,70),"Снаряжение сохранится при возрождении. Выбрать другое можно в главном меню перед следующим подключением.",label,muted);
            } else LoadoutChooser(new Rect(ox+408,348,784,224));
        }

        void Scoreboard(Rect r) {
            Fill(r,panel); Text(new Rect(r.x+22,r.y+17,r.width-44,40),root.Match.IsTeamMode?"ТАБЛИЦА МАТЧА · МЕСТА ВНУТРИ ОТРЯДА":"ТАБЛИЦА МАТЧА · ОБЩИЙ РЕЙТИНГ",heading);
            Rect body=new Rect(r.x+22,r.y+76,r.width-44,r.height-98);
            if(root.Match.IsTeamMode) {
                float w=(body.width-24)/2;
                int ownTeam=root.Match.Local?root.Match.Local.Team:0;
                TeamScore(new Rect(body.x,body.y,w,body.height),ownTeam,"ВАШ ОТРЯД");
                TeamScore(new Rect(body.x+w+24,body.y,w,body.height),1-ownTeam,"ПРОТИВНИКИ");
            } else {
                ScoreHeader(new Rect(body.x,body.y,body.width-23,28),false);
                var list=root.Match.Standings();
                if(root.Match.Phase!=MatchPhase.Results) {
                    float row=Mathf.Min(29,(body.height-35)/Mathf.Max(1,list.Count));
                    for(int i=0;i<list.Count;i++) ScoreRow(new Rect(body.x,body.y+35+i*row,body.width-23,row-1),list[i],false);
                } else {
                    if(Event.current.type==EventType.KeyDown&&(Event.current.keyCode==KeyCode.UpArrow||Event.current.keyCode==KeyCode.DownArrow)) {
                        scoreScroll.y=Mathf.Clamp(scoreScroll.y+(Event.current.keyCode==KeyCode.DownArrow?58:-58),0,Mathf.Max(0,list.Count*29-(body.height-60)));
                        Event.current.Use();
                    }
                    scoreScroll=GUI.BeginScrollView(new Rect(body.x,body.y+35,body.width,body.height-60),scoreScroll,new Rect(0,0,body.width-20,list.Count*29));
                    for(int i=0;i<list.Count;i++) ScoreRow(new Rect(0,i*29,body.width-23,28),list[i],false);
                    GUI.EndScrollView();
                    Text(new Rect(body.x,body.yMax-22,body.width,22),"Колесо мыши / ↑ ↓ — прокрутка таблицы",small,muted);
                }
            }
        }
        void TeamScore(Rect r,int team,string caption) {
            Text(new Rect(r.x+8,r.y,r.width-16,29),caption,small,root.Match.Local&&team==root.Match.Local.Team?green:gold);
            ScoreHeader(new Rect(r.x,r.y+31,r.width,28),true);
            var actors=root.Match.Standings().Where(a=>a.Team==team).ToList();
            float row=Mathf.Min(36,(r.height-67)/12);
            for(int i=0;i<actors.Count;i++) ScoreRow(new Rect(r.x,r.y+67+i*row,r.width,row-2),actors[i],true);
        }
        void ScoreHeader(Rect r,bool team) {
            Text(new Rect(r.x+8,r.y,r.width*.53f-12,r.height),"МЕСТО / ИМЯ",small,muted);
            Text(new Rect(r.x+r.width*.56f,r.y,r.width*.12f,r.height),team?"Уб.":"Убийства",small,muted);
            Text(new Rect(r.x+r.width*.71f,r.y,r.width*.12f,r.height),team?"См.":"Смерти",small,muted);
            Text(new Rect(r.x+r.width*.85f,r.y,r.width*.14f,r.height),team?"Пом.":"Очки",small,muted);
        }
        void ScoreRow(Rect r,Actor actor,bool team) {
            if(actor==root.Match.Local) Fill(r,new Color(.31f,.29f,.18f,.94f));
            else if(actor.Id%2==0) Fill(r,new Color(.13f,.16f,.14f,.65f));
            Color color=actor.Alive||root.Match.Phase==MatchPhase.Results?foreground:muted;
            Text(new Rect(r.x+8,r.y+2,r.width*.53f-12,r.height-3),root.Match.Rank(actor)+".  "+actor.DisplayName+(actor==root.Match.Local?"  [ВЫ]":actor.RemoteControlled?"  · игрок":"  · бот"),small,color);
            Text(new Rect(r.x+r.width*.56f,r.y+2,r.width*.12f,r.height-3),actor.Kills.ToString(),small,color);
            Text(new Rect(r.x+r.width*.71f,r.y+2,r.width*.12f,r.height-3),actor.Deaths.ToString(),small,color);
            Text(new Rect(r.x+r.width*.85f,r.y+2,r.width*.14f,r.height-3),(team?actor.Assists:actor.Score).ToString(),small,color);
        }
        void Results() {
            Fill(new Rect(0,-2000,uiW,5000),new Color(.03f,.05f,.04f,.96f));
            var match=root.Match; var local=match.Local; if(!local) return;
            bool online=NetworkActive;
            bool saved=!online&&Profile.rewardedMatches.Contains(match.MatchId);
            Text(new Rect(ox+60,51,1478,65),match.WinnerText, title,gold);
            Text(new Rect(ox+60,132,1478,41),(match.IsTeamMode?"Командный бой · "+match.RoundWins[local.Team]+" : "+match.RoundWins[1-local.Team]:"Deathmatch · место "+match.Rank(local))+"  ·  Матч завершён",label,muted);
            Fill(new Rect(ox+60,212,343,500),panel);
            Text(new Rect(ox+83,233,297,40),"ЛИЧНЫЙ РЕЗУЛЬТАТ",small,gold);
            Text(new Rect(ox+83,292,297,95),local.Kills+" убийств\n"+local.Deaths+" смертей · "+local.Assists+" помощей",label);
            if(online) {
                Text(new Rect(ox+83,402,297,132),"СЕТЕВОЙ МАТЧ\nКредиты за игру с друзьями не начисляются.",label,muted);
                Text(new Rect(ox+83,555,297,50),"Без награды",heading,gold);
                Text(new Rect(ox+83,617,297,71),"Баланс: "+N(Profile.credits)+" кр.",small,muted);
            } else {
                int rank=match.Rank(local);
                int outcome=match.IsTeamMode?match.RoundWins[0]>match.RoundWins[1]?300:match.RoundWins[0]<match.RoundWins[1]?150:200:180;
                int killReward=match.IsTeamMode?local.Kills*20:Mathf.Min(local.Kills,30)*8;
                int assistReward=match.IsTeamMode?local.Assists*5:0;
                int place=match.EarnedCredits-outcome-killReward-assistReward;
                Text(new Rect(ox+83,402,297,132),(match.IsTeamMode?"Исход: ":"Завершение: ")+outcome+" кр.\nУбийства: "+killReward+" кр.\nПомощи: "+assistReward+" кр.\nМесто "+rank+": "+place+" кр.",small,muted);
                Text(new Rect(ox+83,555,297,50),"+ "+N(match.EarnedCredits)+" кр.",heading,gold);
                Text(new Rect(ox+83,617,297,71),saved?"Награда сохранена\nБаланс: "+N(Profile.credits)+" кр.":"Награда ожидает сохранения",small,saved?green:new Color(1,.6f,.35f));
            }
            Scoreboard(new Rect(ox+434,212,1106,500));
            if(!online&&!saved) {
                if(Button(new Rect(ox+60,738,343,53),"Повторить сохранение",true)) Notify(root.Store.GrantReward(match.MatchId,match.EarnedCredits)?"Награда сохранена":root.Store.LastError);
                Text(new Rect(ox+434,744,1106,52),"Сначала сохраните награду. Повторная попытка не начислит её дважды.",small,muted);
            }
            if(online) {
                Text(new Rect(ox+434,741,1106,50),LobbyMatch?"Чтобы играть ещё, вернитесь в лобби и снова запустите поиск соперников.":root.Network.IsHost?"Чтобы начать новый сетевой бой, вернитесь в меню и создайте матч.":"Чтобы играть ещё, вернитесь в меню и подключитесь к новому матчу друга.",small,muted);
                if(Button(new Rect(ox+60,810,440,48),root.Network.IsHost?"Закрыть сервер и выйти":"Отключиться и выйти",true)) {
                    if(root.Network.IsHost) exitConfirm=true;
                    else match.ReturnToMenu();
                }
            } else {
                if(Button(new Rect(ox+60,810,343,48),"Повторить режим",true,saved)) match.StartMatch(match.ModeId);
                if(Button(new Rect(ox+434,810,343,48),"Главное меню",false,saved)) match.ReturnToMenu();
            }
        }
    }
}
