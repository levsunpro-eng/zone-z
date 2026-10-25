using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ZoneZ
{
    public sealed partial class GameUI : MonoBehaviour
    {
        enum Page { Play, Armory, Upgrades, Abilities, Skins, Settings }
        GameRoot root;
        Page page;
        readonly string[] pages = { "Играть", "Арсенал", "Прокачка", "Ультимейты", "Мастерская скинов", "Настройки" };
        readonly Color ink = new Color(.055f,.073f,.069f), panel = new Color(.095f,.12f,.109f,.96f), card = new Color(.12f,.151f,.13f,.95f);
        readonly Color gold = new Color(.86f,.76f,.52f), foreground = new Color(.96f,.97f,.93f), muted = new Color(.76f,.81f,.77f), green = new Color(.67f,.83f,.58f);
        GUIStyle label, small, title, heading, button, input, center, big, sliderTrack, sliderThumb;
        Font font;
        Texture2D white;
        float fontScale=-1, uiW, ox;
        Vector2 arsenalScroll, abilityScroll, presetScroll;
        string selectedWeapon, toast;
        float toastUntil;
        int filter;
        SkinPreset draft;
        bool dirty, exitConfirm, pauseSettings, settingsLoaded;
        float sensitivity, volume, fov, textScale;
        Camera previewCamera;
        RenderTexture previewTexture;
        Transform previewPivot;
        WeaponPresentation preview;
        string previewKey;
        float yaw=35, zoom=1, rebuildAfter, previewAspect=3;
        Bounds previewBounds=new Bounds(Vector3.zero,Vector3.one);
        Vector3[] previewPoints=Array.Empty<Vector3>();
        Vector3 fittedPreviewTarget;
        Quaternion fittedPreviewPose;
        float fittedPreviewDistance, fittedPreviewYaw=float.NaN, fittedPreviewAspect;
        bool dragging;
        bool daylightPreview;
        Action pendingNavigation;
        SkinPreset deleteCandidate;
        SkinPreset conflictingDraft;
        string conflictingId;
        bool skinParameters, confirmReset;
        int focusedButton, buttonIndex, buttonCount;
        bool keyboardNavigation;
        bool listScrollActive, lastModal;
        Rect? scrollFocus;
        float listViewportHeight;
        int focusBeforeModal;
        Page modalOriginPage;
        MatchPhase modalOriginPhase;
        readonly string[] patterns={"Без узора","Полосы","Камуфляж","Геометрия"};
        readonly string[] stickers={"Нет","Зона Z","Череп","Молния"};
        readonly string[] skins={"Базовый","Лесник","Песчаный","Ночной","Золотой"};
        GameProfile Profile => root.Profile;
        GameCatalog Catalog => root.Catalog;
        public void Initialize(GameRoot owner) {
            root=owner;
            string[] args=Environment.GetCommandLineArgs();
            ApplyRuntimeSettings(Array.IndexOf(args,"-screen-fullscreen")<0&&Array.IndexOf(args,"-screen-width")<0&&Array.IndexOf(args,"-screen-height")<0);
            if(!string.IsNullOrEmpty(root.Store.LastError)) Notify(root.Store.LastError);
        }
        public void Notify(string message) { toast=message; toastUntil=Time.unscaledTime+4.5f; }

        void Update()
        {
            if(root==null) root=GameRoot.Instance;
            if(root==null || root.Match==null) return;
            if(pendingSettings!=null&&Time.unscaledTime>=screenConfirmUntil) RevertDisplay();
            if(Input.GetKeyDown(KeyCode.Escape)) {
                if(pendingSettings!=null) RevertDisplay();
                else if(bindingAction!=null) { bindingAction=null; bindingConflict=null; }
                else if(conflictingDraft!=null) conflictingDraft=null;
                else if(pendingNavigation!=null) pendingNavigation=null;
                else if(deleteCandidate!=null) deleteCandidate=null;
                else if(skinParameters) skinParameters=false;
                else if(confirmReset) confirmReset=false;
                else if(exitConfirm) exitConfirm=false;
                else if(pauseSettings) pauseSettings=false;
                else if(root.Match.Phase==MatchPhase.Menu) {
                    if(page==Page.Play&&networkOnline) LeaveNetworkForm();
                    else ChangePage(Page.Play);
                }
                else if(root.Match.Phase!=MatchPhase.Results) root.SetPaused(!root.Paused);
            }
            bool cursor=root.Paused || root.Match.Phase==MatchPhase.Menu || root.Match.Phase==MatchPhase.Results || root.Match.Phase==MatchPhase.Preparation ||
                (root.Match.Local && !root.Match.Local.Alive && !root.Match.IsTeamMode);
            Cursor.lockState=cursor?CursorLockMode.None:CursorLockMode.Locked; Cursor.visible=cursor;
            bool show=root.Match.Phase==MatchPhase.Menu && (page==Page.Armory || page==Page.Skins);
            if(previewCamera) previewCamera.enabled=show;
            if(show) { if(string.IsNullOrEmpty(selectedWeapon)) selectedWeapon=Profile.primaryId; UpdatePreview(); }
        }

        void OnGUI()
        {
            if(root==null || root.Catalog==null || root.Store==null || root.Match==null) return;
            Styles();
            float scale=Mathf.Min(Screen.width/1600f,Screen.height/900f);
            if(scale<=0) return;
            uiW=Screen.width/scale; ox=(uiW-1600)/2;
            Matrix4x4 old=GUI.matrix;
            GUI.matrix=Matrix4x4.TRS(new Vector3(0,(Screen.height-900*scale)/2,0),Quaternion.identity,Vector3.one*scale);
            GUI.depth=-10;
            buttonIndex=0;
            CaptureBindingEvent();
            bool modal=pendingSettings!=null || bindingAction!=null || exitConfirm || conflictingDraft!=null || pendingNavigation!=null || deleteCandidate!=null || skinParameters || confirmReset;
            if(modal!=lastModal) {
                if(modal) { focusBeforeModal=focusedButton; modalOriginPage=page; modalOriginPhase=root.Match.Phase; focusedButton=0; }
                else { focusedButton=page==modalOriginPage&&root.Match.Phase==modalOriginPhase?focusBeforeModal:0; keyboardNavigation=true; }
                GUI.FocusControl(null); lastModal=modal;
            }
            bool navigating=root.Match.Phase!=MatchPhase.Playing || root.Paused || (root.Match.Local && !root.Match.Local.Alive && !root.Match.IsTeamMode);
            if(navigating && bindingAction==null && Event.current.type==EventType.KeyDown && Event.current.keyCode==KeyCode.Tab) {
                focusedButton=!keyboardNavigation?(Event.current.shift?Mathf.Max(0,buttonCount-1):0):(focusedButton+(Event.current.shift?-1:1)+Mathf.Max(1,buttonCount))%Mathf.Max(1,buttonCount);
                keyboardNavigation=true; GUI.FocusControl(null); Event.current.Use();
            }
            if(Event.current.type==EventType.MouseDown) keyboardNavigation=false;
            GUI.enabled=!modal;
            if(root.Match.Phase==MatchPhase.Menu) Menu();
            else if(root.Match.Phase==MatchPhase.Results) Results();
            else {
                Hud();
                if(root.Match.Phase==MatchPhase.Preparation) Preparation();
                if(GameInput.Held("scoreboard") && !root.Paused) Scoreboard(new Rect(ox+190,104,1220,688));
                if(root.Match.Phase==MatchPhase.RoundEnd) {
                    Fill(new Rect(ox+440,298,720,190),panel);
                    Text(new Rect(ox+472,320,656,54),root.Match.WinnerText??"Раунд завершён",heading);
                    Text(new Rect(ox+472,380,656,40),root.Match.RoundReason,label,muted);
                    Text(new Rect(ox+472,428,656,36),root.Match.RoundNumber>=3||root.Match.RoundWins.Any(w=>w>=2)?"Подведение итогов матча…":"Следующий раунд скоро начнётся",small,muted);
                }
                if(root.Paused) Pause();
            }
            GUI.enabled=true;
            if(pendingSettings!=null) DisplayConfirmation();
            else if(bindingAction!=null) BindingDialog();
            else if(exitConfirm) ExitConfirmation();
            else if(conflictingDraft!=null) PresetConflict();
            else if(pendingNavigation!=null) DraftConfirmation();
            else if(deleteCandidate!=null) DeleteConfirmation();
            else if(skinParameters) SkinParameters();
            else if(confirmReset) ResetConfirmation();
            if(!string.IsNullOrEmpty(toast) && Time.unscaledTime<toastUntil) {
                bool combatToast=root.Match.Phase!=MatchPhase.Menu&&root.Match.Phase!=MatchPhase.Results&&!root.Paused;
                float toastY=root.Match.Phase==MatchPhase.Results?724:112;
                Rect toastRect=combatToast?new Rect(uiW*.5f-270,112,540,36):new Rect(ox+410,toastY,780,64);
                Fill(toastRect,ink); Fill(new Rect(toastRect.x,toastRect.y,3,toastRect.height),gold);
                Text(new Rect(toastRect.x+15,toastRect.y+(combatToast?5:10),toastRect.width-30,toastRect.height-(combatToast?10:20)),toast,small);
            }
            buttonCount=buttonIndex;
            if(focusedButton>=buttonCount) focusedButton=0;
            GUI.matrix=old;
        }

        void Styles()
        {
            float size=Profile.uiScale;
            if(label!=null && Mathf.Approximately(fontScale,size)) return;
            fontScale=size;
            if(!white) { white=new Texture2D(1,1); white.SetPixel(0,0,Color.white); white.Apply(); }
            if(!font) font=Font.CreateDynamicFontFromOSFont(new[]{"Segoe UI","Arial"},18);
            label=Style(18); small=Style(15); title=Style(48,FontStyle.Bold); heading=Style(27,FontStyle.Bold); big=Style(54,FontStyle.Bold);
            center=Style(18,FontStyle.Bold); center.alignment=TextAnchor.MiddleCenter;
            button=new GUIStyle(center){padding=new RectOffset(10,10,4,4)};
            input=new GUIStyle(GUI.skin.textField){font=font,fontSize=Mathf.RoundToInt(18*size),padding=new RectOffset(12,12,3,3),alignment=TextAnchor.MiddleLeft,wordWrap=false};
            input.normal.textColor=foreground; input.focused.textColor=foreground; input.hover.textColor=foreground; input.active.textColor=foreground;
            sliderTrack=new GUIStyle {fixedHeight=16, padding=new RectOffset(0,0,0,0)};
            sliderThumb=new GUIStyle {fixedWidth=16,fixedHeight=16};
        }
        GUIStyle Style(int size,FontStyle weight=FontStyle.Normal) => new GUIStyle {
            font=font,fontSize=Mathf.RoundToInt(size*fontScale),fontStyle=weight,normal={textColor=Color.white},
            wordWrap=true,clipping=TextClipping.Clip,alignment=TextAnchor.UpperLeft
        };
        void Fill(Rect r,Color c) { Color old=GUI.color; GUI.color=c; GUI.DrawTexture(r,white); GUI.color=old; }
        GUIStyle Fit(GUIStyle style,Rect r,string text) {
            if(style.CalcHeight(new GUIContent(text??""),r.width)<=r.height) return style;
            var fit=new GUIStyle(style);
            while(fit.fontSize>13 && fit.CalcHeight(new GUIContent(text??""),r.width)>r.height) fit.fontSize--;
            return fit;
        }
        void Text(Rect r,string text,GUIStyle style=null,Color? color=null) {
            Color old=GUI.color, oldContent=GUI.contentColor; bool oldEnabled=GUI.enabled;
            // Apply one tint only. A second style tint darkens text in linear rendering.
            GUI.color=color??foreground; GUI.contentColor=Color.white; GUI.enabled=true;
            GUI.Label(r,text??"",Fit(style??label,r,text));
            GUI.color=old; GUI.contentColor=oldContent; GUI.enabled=oldEnabled;
        }
        bool Button(Rect r,string text,bool accent=false,bool enabled=true)
        {
            bool hover=r.Contains(Event.current.mousePosition);
            bool canFocus=enabled&&GUI.enabled;
            int index=canFocus?buttonIndex++:-1;
            bool focused=keyboardNavigation&&index==focusedButton&&index>=0;
            Fill(r,!enabled?new Color(.12f,.13f,.12f):accent?(hover?new Color(.86f,.77f,.54f):gold):(hover?new Color(.23f,.28f,.23f):card));
            bool previous=GUI.enabled; GUI.enabled=previous&&enabled;
            bool clicked=GUI.Button(r,GUIContent.none,GUIStyle.none);
            Text(r,text,button,!enabled?muted:accent?ink:foreground);
            if(focused) {
                if(listScrollActive) scrollFocus=r;
                Fill(new Rect(r.x,r.y,r.width,2),foreground); Fill(new Rect(r.x,r.yMax-2,r.width,2),foreground);
                if(Event.current.type==EventType.KeyDown && (Event.current.keyCode==KeyCode.Return || Event.current.keyCode==KeyCode.KeypadEnter)) { clicked=true; Event.current.Use(); }
            }
            GUI.enabled=previous;
            if(clicked) { focusedButton=index; AudioFX.InterfaceClick(); }
            return clicked;
        }
        string TextInput(Rect r,string value,int maxLength,string control)
        {
            int index=GUI.enabled?buttonIndex++:-1;
            bool focused=keyboardNavigation&&index==focusedButton&&index>=0;
            bool clicked=GUI.enabled&&Event.current.type==EventType.MouseDown&&r.Contains(Event.current.mousePosition);
            GUI.SetNextControlName(control);
            if(focused) {
                GUI.FocusControl(control);
                if(listScrollActive) scrollFocus=r;
            }
            string result=GUI.TextField(r,value??"",maxLength,Fit(input,r,value));
            if(focused) { Fill(new Rect(r.x,r.y,r.width,2),gold); Fill(new Rect(r.x,r.yMax-2,r.width,2),gold); }
            if(clicked) focusedButton=index;
            return result;
        }
        Vector2 BeginListScroll(Rect viewport,Vector2 scroll,Rect content,bool alwaysVertical=false)
        {
            listScrollActive=true; scrollFocus=null; listViewportHeight=viewport.height;
            return GUI.BeginScrollView(viewport,scroll,content,false,alwaysVertical);
        }
        void EndListScroll(ref Vector2 scroll)
        {
            GUI.EndScrollView(); listScrollActive=false;
            if(!scrollFocus.HasValue) return;
            Rect focus=scrollFocus.Value;
            if(focus.yMin<scroll.y) scroll.y=focus.yMin;
            else if(focus.yMax>scroll.y+listViewportHeight) scroll.y=focus.yMax-listViewportHeight;
            scroll.y=Mathf.Max(0,scroll.y); scrollFocus=null;
        }
        string N(int n)=>n.ToString("N0").Replace(","," ");
        string Clock(float s) { int t=Mathf.Max(0,Mathf.CeilToInt(s)); return (t/60).ToString("00")+":"+(t%60).ToString("00"); }
        void ChangePage(Page value) {
            NavigateFromDraft(()=>{
                page=value; if(value==Page.Settings) settingsLoaded=false;
                if(string.IsNullOrEmpty(selectedWeapon)) selectedWeapon=Profile.primaryId;
                focusedButton=0;
            },value!=Page.Skins);
        }
        void NavigateFromDraft(Action action,bool leaving=true) { if(page==Page.Skins&&dirty&&leaving) pendingNavigation=action; else action(); }
        void PageTitle(string name,string subtitle) {
            Text(new Rect(ox+352,157,1188,56),name,heading); Text(new Rect(ox+352,204,1188,44),subtitle,small,muted);
        }
        void Menu()
        {
            Fill(new Rect(0,-2000,uiW,5000),new Color(.035f,.055f,.04f,.80f));
            Fill(new Rect(ox,0,318,900),new Color(.04f,.067f,.047f,.96f));
            Text(new Rect(ox+48,42,260,69),"ЗОНА Z",title); Text(new Rect(ox+51,111,240,32),"ПОСЛЕДНЯЯ СМЕНА",small,gold);
            Text(new Rect(ox+353,57,580,42),"ЗАБРОШЕННЫЙ ЗООПАРК",small,muted);
            Text(new Rect(ox+1190,49,350,42),root.Store.WriteBlocked?"ПРОФИЛЬ НЕДОСТУПЕН":N(Profile.credits)+"  КРЕДИТОВ",heading,gold);
            Fill(new Rect(ox+352,116,1192,1),muted*.35f);
            for(int i=0;i<pages.Length;i++) if(Button(new Rect(ox+30,213+i*72,258,58),pages[i],(int)page==i)) ChangePage((Page)i);
            Text(new Rect(ox+49,734,226,65),"ЛОКАЛЬНО И ПО СЕТИ\nДРУЗЬЯ + БОТЫ",small,muted);
            if(Button(new Rect(ox+49,824,224,36),"Выйти из игры")) NavigateFromDraft(()=>Application.Quit());
            switch(page) {
                case Page.Play: Play(); break; case Page.Armory: Armory(); break; case Page.Upgrades: Upgrades(); break;
                case Page.Abilities: Abilities(); break; case Page.Skins: SkinEditor(); break; case Page.Settings: Settings(ox+352,180,1188); break;
            }
            Text(new Rect(ox+352,846,1190,49),root.Store.WriteBlocked?root.Store.LastError:GameInput.Label("forward")+"/"+GameInput.Label("backward")+"/"+GameInput.Label("left")+"/"+GameInput.Label("right")+" движение · "+GameInput.Label("fire")+" огонь · "+GameInput.Label("reload")+" перезарядка · "+GameInput.Label("ultimate")+" ульта · "+GameInput.Label("use")+" лут · "+GameInput.Label("scoreboard")+" счёт · Esc пауза",small,muted);
        }
        void Play()
        {
            PageTitle("Выберите бой",networkOnline?(networkDirect?"Прямое подключение к серверу друга. Снаряжение выберите до входа.":"Соберите друзей в лобби и найдите соперников. Игры с ботами запускаются отдельно."):"Одна карта. Два способа пережить последнюю смену.");
            if(Button(new Rect(ox+352,257,280,45),"Локально · с ботами",!networkOnline)) LeaveNetworkForm();
            if(Button(new Rect(ox+646,257,280,45),"Онлайн · с друзьями",networkOnline)) { networkOnline=true; focusedButton=0; }
            if(networkOnline) {
                if(Button(new Rect(ox+951,257,287,45),"Лобби и поиск",!networkDirect)) SelectNetworkService(false);
                if(Button(new Rect(ox+1253,257,287,45),"По адресу",networkDirect)) SelectNetworkService(true);
                if(networkDirect) NetworkPlay(); else LobbyPlay();
                return;
            }
            Text(new Rect(ox+951,265,589,32),"Награды и прокачка сохраняются в профиле",small,muted);
            ModeCard(new Rect(ox+352,321,575,342),true); ModeCard(new Rect(ox+949,321,591,342),false);
            Fill(new Rect(ox+352,683,1188,124),panel);
            Text(new Rect(ox+379,697,1134,27),"ВАШЕ СНАРЯЖЕНИЕ",small,muted);
            Text(new Rect(ox+379,727,548,39),Catalog.Weapon(Profile.primaryId).Name+"   /   "+Catalog.Weapon(Profile.pistolId).Name,heading);
            Text(new Rect(ox+973,727,540,39),Catalog.Ability(Profile.ultimateId).Name,label,gold);
            Text(new Rect(ox+379,775,1096,26),"Всё оружие бесплатно · Нож и Zeus всегда с собой · Заряд ульты зарабатывается в бою",small,muted);
        }
        void ModeCard(Rect r,bool team)
        {
            Fill(r,panel); Fill(new Rect(r.x,r.y,r.width,4),team?green:gold);
            Text(new Rect(r.x+27,r.y+28,r.width-54,35),team?"01 / КОМАНДНЫЙ БОЙ":"02 / DEATHMATCH",small,team?green:gold);
            Text(new Rect(r.x+27,r.y+77,r.width-54,65),team?"12 × 12":"КАЖДЫЙ ЗА СЕБЯ",team?big:heading);
            Text(new Rect(r.x+27,r.y+153,r.width-54,83),team?"До трёх раундов по 3 минуты.\nУничтожьте команду противника.\nВозрождение в следующем раунде.":"20 участников · 10 минут.\n100 очков за убийство.\nУсиления на карте каждую минуту.",label);
            Text(new Rect(r.x+27,r.y+241,r.width-54,36),team?"ПРОКАЧКА ДЕЙСТВУЕТ":"ПРОКАЧКА ОТКЛЮЧЕНА",small,muted);
            if(Button(new Rect(r.x+27,r.y+291,r.width-54,43),root.Store.WriteBlocked?"Восстановите профиль":"Начать матч с ботами",true,!root.Store.WriteBlocked)) {
                CancelPendingConnection();
                root.Lobby?.Disconnect();
                root.Match.StartMatch(team?"team_elimination":"deathmatch");
            }
        }
        void Armory()
        {
            PageTitle("Арсенал","34 огнестрельных оружия, Zeus и нож. Всё доступно с первого матча.");
            string[] filters={"Всё","Пистолеты","ПП","Автоматы","Снайперские","Дробовики","Пулемёты","Спецоружие"};
            string[] categories={"","pistol","smg","rifle","sniper","shotgun","machinegun","special"};
            for(int i=0;i<filters.Length;i++) if(Button(new Rect(ox+352+i*149,257,141,38),filters[i],filter==i)) { filter=i; arsenalScroll=Vector2.zero; }
            var list=Catalog.Weapons.Where(w=>filter==0 || (filter==7?(w.Slot=="knife"||w.Slot=="zeus"):w.Category==categories[filter])).ToList();
            arsenalScroll=BeginListScroll(new Rect(ox+352,310,345,510),arsenalScroll,new Rect(0,0,321,list.Count*51),true);
            for(int i=0;i<list.Count;i++) if(Button(new Rect(0,i*51,319,44),list[i].Name,selectedWeapon==list[i].Id)) { selectedWeapon=list[i].Id; draft=null; previewKey=null; }
            EndListScroll(ref arsenalScroll);
            WeaponSpec spec=Catalog.Weapon(selectedWeapon)??Catalog.Weapon(Profile.primaryId);
            Preview(new Rect(ox+726,310,814,278));
            Text(new Rect(ox+747,322,650,42),spec.Name,heading);
            Stat(new Rect(ox+728,610,180,81),"УРОН",spec.Pellets>1?spec.Damage+" × "+spec.Pellets:spec.Damage.ToString("0"));
            Stat(new Rect(ox+935,610,180,81),"МАГАЗИН",spec.Slot=="knife"?"—":spec.Magazine.ToString());
            Stat(new Rect(ox+1142,610,180,81),"ВЫСТР. / МИН",(60/spec.FireInterval).ToString("0"));
            Stat(new Rect(ox+1349,610,191,81),"ПЕРЕЗАРЯДКА",spec.Slot=="knife"?"—":spec.ReloadSeconds.ToString("0.0")+" с");
            string modes=spec.HasScope?"Прицел · ":spec.HasBurst?"Отсечка · ":spec.HasSilencer?"Глушитель · ":"";
            Text(new Rect(ox+728,704,809,68),modes+"Отдача: "+spec.Recoil.ToString("0.0")+" · Разброс: "+spec.Spread.ToString("0.000")+"\nУрон зависит от дистанции, брони и места попадания. Дерево простреливается.",small,muted);
            bool selected=spec.Id==Profile.primaryId || spec.Id==Profile.pistolId, equippable=spec.Slot=="primary" || spec.Slot=="pistol";
            if(Button(new Rect(ox+728,780,394,43),!equippable?"Доступно всегда":selected?"Уже в снаряжении":"Взять в снаряжение",true,equippable&&!selected))
                Notify(root.Store.SelectWeapon(spec.Id,out string error)?spec.Name+" выбрано":error);
            if(Button(new Rect(ox+1140,780,400,43),"Настроить скин")) { draft=root.Store.SkinFor(spec.Id); ChangePage(Page.Skins); }
        }
        void Stat(Rect r,string name,string value) {
            Fill(r,panel); Text(new Rect(r.x+14,r.y+10,r.width-28,27),name,small,muted); Text(new Rect(r.x+14,r.y+39,r.width-28,35),value,heading);
        }
        void Upgrades()
        {
            PageTitle("Станьте сильнее","Прокачка действует только в командном бою. В Deathmatch характеристики базовые.");
            string[] ids={"health","speed","strength"}, names={"Здоровье","Скорость","Сила"};
            int[] levels={Profile.healthLevel,Profile.speedLevel,Profile.strengthLevel};
            string[] descriptions={"Больше здоровья в начале каждого раунда. +4 HP за уровень.","Быстрее перемещайтесь с любым оружием. +2% за уровень.","Больше урона обычным оружием. +2% за уровень. Не усиливает ульты."};
            for(int i=0;i<3;i++) {
                Rect r=new Rect(ox+352+i*403,285,382,466); Fill(r,panel);
                Text(new Rect(r.x+25,r.y+26,330,50),names[i],heading);
                Text(new Rect(r.x+25,r.y+110,330,80),i==0?(100+levels[i]*4)+" HP":"+"+levels[i]*2+"%",big,gold);
                for(int pip=0;pip<5;pip++) Fill(new Rect(r.x+26+pip*64,r.y+218,53,7),pip<levels[i]?gold:muted*.3f);
                Text(new Rect(r.x+25,r.y+250,330,100),descriptions[i],label,muted);
                int price=Catalog.UpgradeCost(levels[i]);
                string action=price<0?"Максимальный уровень":Profile.credits<price?"Не хватает "+N(price-Profile.credits):"Улучшить · "+N(price)+" кр.";
                if(Button(new Rect(r.x+25,r.y+385,330,54),action,true,price>=0&&Profile.credits>=price))
                    Notify(root.Store.Upgrade(ids[i],levels[i],out string error)?names[i]+": уровень повышен":error);
            }
            Text(new Rect(ox+352,777,1188,60),"Максимум: 120 HP · +10% скорости · +10% урона. Покупки сохраняются между матчами.",label,muted);
        }
        void Abilities()
        {
            PageTitle("Ультимейты","Купите навсегда, выберите одну способность и заработайте заряд серией убийств за 20 секунд.");
            abilityScroll=BeginListScroll(new Rect(ox+352,261,1188,570),abilityScroll,new Rect(0,0,1160,700));
            for(int i=0;i<Catalog.Abilities.Count;i++) {
                AbilitySpec a=Catalog.Abilities[i]; bool owned=Profile.ownedAbilities.Contains(a.Id), selected=Profile.ultimateId==a.Id;
                Rect r=new Rect((i%2)*584,(i/2)*140,566,126); Fill(r,panel);
                if(selected) Fill(new Rect(r.x,r.y,3,r.height),gold);
                Text(new Rect(r.x+18,r.y+13,346,37),a.Name,label,selected?gold:foreground);
                Text(new Rect(r.x+18,r.y+52,346,40),AbilityDescription(a.Id),small,muted);
                Text(new Rect(r.x+18,r.y+94,346,25),a.KillsRequired+" убийства за 20 секунд",small,green);
                if(Button(new Rect(r.x+384,r.y+30,163,64),selected?"Выбрана":owned?"Выбрать":N(a.Cost)+" кр.",selected||owned,!selected&&(owned||Profile.credits>=a.Cost))) {
                    string error; bool success=owned?root.Store.SelectAbility(a.Id,out error):root.Store.PurchaseAbility(a.Id,out error);
                    Notify(success?(owned?"Выбрана: ":"Открыта: ")+a.Name:error);
                }
            }
            EndListScroll(ref abilityScroll);
        }
        string AbilityDescription(string id)
        {
            switch(id) {
                case "frag_grenade":return "Граната · до 120 урона · радиус 5 м"; case "adrenaline":return "+35% скорости на 8 секунд";
                case "medical_pulse":return "Мгновенно восстановить 60 HP"; case "armor_shield":return "Щит на 75 урона · до 10 секунд";
                case "recon_pulse":return "Показать врагов в 25 м · 5 секунд"; case "emp_grenade":return "Снять усиления и блокировать ульту";
                case "underbarrel":return "Две гранаты по 80 урона · 3 м"; case "laser_burst":return "Два точных луча по 120 урона";
                case "rocket_launcher":return "Ракета · до 180 урона · 6 м"; case "absolute_shot":return "Любая пуля убивает · 6 секунд"; default:return "";
            }
        }
        void SkinEditor()
        {
            PageTitle("Мастерская скинов","Три цвета, один узор, четыре наклейки. Внешний вид не меняет характеристики.");
            if(draft==null || draft.weaponId!=selectedWeapon) draft=root.Store.SkinFor(selectedWeapon);
            float x=ox+352, right=ox+968;
            if(Button(new Rect(x,259,48,40),"‹")) CycleWeapon(-1);
            Text(new Rect(x+63,265,451,32),Catalog.Weapon(selectedWeapon).Name,label);
            if(Button(new Rect(x+534,259,48,40),"›")) CycleWeapon(1);
            Text(new Rect(x,317,130,34),"Имя пресета",small,muted);
            string name=TextInput(new Rect(x+143,310,439,40),draft.name??"",40,"skin-name");
            if(name!=draft.name) { draft.name=name; dirty=true; }
            EditColor(x,372,"Корпус",0); EditColor(x,426,"Цевьё",1); EditColor(x,480,"Детали",2);
            Text(new Rect(x,549,120,35),"Узор",small,muted);
            if(Button(new Rect(x+133,540,268,42),patterns[draft.pattern])) { draft.pattern=(draft.pattern+1)%4; DirtySkin(); }
            CosmeticButton(new Rect(x+417,540,165,42),"pattern_"+draft.pattern,draft.pattern==0);
            for(int i=0;i<4;i++) {
                float sx=x+(i%2)*303,sy=602+(i/2)*63;
                if(Button(new Rect(sx,sy,167,47),(i+1)+" · "+stickers[draft.stickers[i]])) { draft.stickers[i]=(draft.stickers[i]+1)%4; DirtySkin(); }
                CosmeticButton(new Rect(sx+173,sy,108,47),"sticker_"+draft.stickers[i],draft.stickers[i]==0);
            }
            Text(new Rect(x,733,119,30),"Износ",small,muted);
            float wear=Slider(new Rect(x+132,738,350,18),draft.wear,0,1);
            if(!Mathf.Approximately(wear,draft.wear)) { draft.wear=wear; DirtySkin(); }
            Text(new Rect(x+501,729,81,35),Mathf.RoundToInt(draft.wear*100)+"%",label);
            if(Button(new Rect(x,786,281,44),"Сохранить и применить",true)) SaveDraft(false);
            if(Button(new Rect(x+301,786,281,44),"Сохранить копию")) SaveDraft(true);
            Preview(new Rect(right,259,572,293));
            Text(new Rect(right+18,269,536,34),dirty?"ПРЕДПРОСМОТР · НЕ СОХРАНЁН":"ПРЕДПРОСМОТР",small,gold);
            if(Button(new Rect(right,561,274,35),"Узор и наклейки…")) skinParameters=true;
            if(Button(new Rect(right+291,561,281,35),"Базовая палитра")) ApplyBase(0);
            for(int i=1;i<=4;i++) {
                string id="skin_"+i; bool owned=Profile.ownedCosmetics.Contains(id);
                if(Button(new Rect(right+(i-1)*144,608,137,65),skins[i]+"\n"+(owned?"Применить":N(Catalog.CosmeticCost(id))+" кр."))) {
                    if(owned) ApplyBase(i);
                    else if(root.Store.PurchaseCosmetic(id,Catalog.CosmeticCost(id),out string error)) { ApplyBase(i); Notify("Скин приобретён"); }
                    else Notify(error);
                }
            }
            Text(new Rect(right,695,572,27),"ВАШИ ПРЕСЕТЫ",small,muted);
            var saved=Profile.skins.Where(s=>s.weaponId==selectedWeapon).ToList();
            presetScroll=BeginListScroll(new Rect(right,731,572,99),presetScroll,new Rect(0,0,548,Mathf.Max(90,saved.Count*46)));
            if(saved.Count==0) Text(new Rect(0,5,530,70),"Сохраните первый пресет. Его цвета и наклейки появятся на оружии в бою.",small,muted);
            for(int i=0;i<saved.Count;i++) {
                SkinPreset selected=saved[i];
                if(Button(new Rect(0,i*46,302,39),selected.name+(selected.applied?" · ✓":""),selected.id==draft.id)) {
                    NavigateFromDraft(()=>{
                        string problem=root.Store.SkinProblem(selected);
                        if(problem!=null) { Notify(problem+". Исходный пресет сохранён."); return; }
                        draft=selected.Copy(); dirty=false; previewKey=null;
                    });
                }
                if(Button(new Rect(309,i*46,139,39),"Применить",false,!selected.applied)) {
                    NavigateFromDraft(()=> { if(root.Store.ApplySkin(selected.id,out string error)) { draft=root.Store.SkinFor(selectedWeapon); dirty=false; previewKey=null; Notify("Пресет применён"); } else Notify(error); });
                }
                if(Button(new Rect(455,i*46,91,39),"Удалить")) deleteCandidate=selected;
            }
            EndListScroll(ref presetScroll);
        }
        void EditColor(float x,float y,string name,int zone)
        {
            string value=zone==0?draft.colorA:zone==1?draft.colorB:draft.colorC;
            Text(new Rect(x,y+8,115,35),name,small,muted);
            if(ColorUtility.TryParseHtmlString("#"+value,out Color color)) Fill(new Rect(x+133,y,47,41),color);
            string text=TextInput(new Rect(x+193,y,159,41),value,6,"skin-color-"+zone).ToUpperInvariant();
            if(text!=value) { SetColor(zone,text); DirtySkin(); }
            string[] palette={"48583C","B9A278","20282B","DBD4B9","86523E"};
            for(int i=0;i<palette.Length;i++) {
                Rect r=new Rect(x+367+i*44,y+5,36,32);
                ColorUtility.TryParseHtmlString("#"+palette[i],out Color swatch); Fill(r,swatch);
                if(GUI.Button(r,"",GUIStyle.none)) { SetColor(zone,palette[i]); DirtySkin(); }
            }
        }
        void SetColor(int zone,string value) { if(zone==0) draft.colorA=value; else if(zone==1) draft.colorB=value; else draft.colorC=value; }
        void CosmeticButton(Rect r,string id,bool free)
        {
            bool owned=free||Profile.ownedCosmetics.Contains(id); int cost=free?0:Catalog.CosmeticCost(id);
            if(Button(r,owned?"Открыто":N(cost)+" кр.",false,!owned&&cost>=0))
                Notify(root.Store.PurchaseCosmetic(id,cost,out string error)?"Элемент открыт навсегда":error);
        }
        void ApplyBase(int style)
        {
            string[][] colors={new[]{"4B5446","20272C","BFA470"},new[]{"3F543C","263328","9EA86D"},new[]{"AF9671","5F5141","E5D3A0"},new[]{"283B49","171E29","729BAD"},new[]{"A9803D","27261F","DCC28B"}};
            draft.baseStyle=style; draft.colorA=colors[style][0]; draft.colorB=colors[style][1]; draft.colorC=colors[style][2]; DirtySkin();
        }
        void DirtySkin() { dirty=true; rebuildAfter=Time.unscaledTime+.12f; previewKey=null; }
        void CycleWeapon(int delta)
        {
            NavigateFromDraft(()=>{
                int i=Catalog.Weapons.FindIndex(w=>w.Id==selectedWeapon);
                selectedWeapon=Catalog.Weapons[(i+delta+Catalog.Weapons.Count)%Catalog.Weapons.Count].Id;
                draft=root.Store.SkinFor(selectedWeapon); dirty=false; previewKey=null;
            });
        }
        bool SaveDraft(bool copy)
        {
            SkinPreset value=draft.Copy(); if(copy) value.id="";
            if(!copy) {
                SkinPreset collision=Profile.skins.FirstOrDefault(s=>s.weaponId==value.weaponId&&s.id!=value.id&&string.Equals(s.name?.Trim(),value.name?.Trim(),StringComparison.OrdinalIgnoreCase));
                if(collision!=null) { conflictingDraft=value; conflictingId=collision.id; return false; }
            }
            return CommitDraft(value);
        }
        bool CommitDraft(SkinPreset value)
        {
            if(root.Store.SaveSkin(value,out string error)) { draft=root.Store.SkinFor(selectedWeapon); dirty=false; Notify("Скин сохранён и применён"); return true; }
            Notify(error); return false;
        }
        void Preview(Rect r)
        {
            Fill(r,panel);
            Rect imageRect=new Rect(r.x+15,r.y+45,r.width-30,r.height-106);
            previewAspect=imageRect.width/imageRect.height;
            if(previewTexture) GUI.DrawTexture(imageRect,previewTexture,ScaleMode.StretchToFill,false);
            Text(new Rect(r.x+18,r.yMax-58,r.width-36,25),"Перетащите: вращать · колесо: масштаб",small,muted);
            if(Button(new Rect(r.x+18,r.yMax-34,121,27),"Осмотр")) preview?.Inspect();
            if(Button(new Rect(r.x+152,r.yMax-34,173,27),daylightPreview?"Дневной свет":"Студийный свет")) {
                daylightPreview=!daylightPreview;
                foreach(var studioLight in root.World.PreviewAnchor.GetComponentsInChildren<Light>()) {
                    studioLight.color=daylightPreview?new Color(1,.88f,.68f):new Color(.91f,.96f,1);
                    studioLight.intensity=daylightPreview?3:4;
                }
            }
            if(Button(new Rect(r.xMax-139,r.yMax-43,121,29),"Сбросить вид")) { yaw=35; zoom=1; }
            if(!GUI.enabled) { dragging=false; return; }
            Event e=Event.current;
            Rect rotateArea=new Rect(r.x,r.y+42,r.width,Mathf.Max(0,r.height-110));
            if(rotateArea.Contains(e.mousePosition)&&e.type==EventType.MouseDown&&e.button==0) dragging=true;
            if(e.type==EventType.MouseUp) dragging=false;
            if(dragging&&e.type==EventType.MouseDrag) { yaw+=e.delta.x*.8f; e.Use(); }
            if(r.Contains(e.mousePosition)&&e.type==EventType.ScrollWheel) { zoom=Mathf.Clamp(zoom+e.delta.y*.045f,.6f,1.7f); e.Use(); }
        }
        void UpdatePreview()
        {
            if(!root.World || !root.World.PreviewAnchor) return;
            if(!previewPivot) {
                previewPivot=new GameObject("Weapon preview pivot").transform; previewPivot.SetParent(root.World.PreviewAnchor,false);
                previewCamera=new GameObject("Weapon preview camera").AddComponent<Camera>();
                previewCamera.clearFlags=CameraClearFlags.SolidColor; previewCamera.backgroundColor=panel; previewCamera.fieldOfView=33;
                previewCamera.nearClipPlane=.025f; previewCamera.farClipPlane=12; previewCamera.cullingMask=1<<30;
                previewTexture=new RenderTexture(1024,576,24){name="Weapon preview"}; previewTexture.Create(); previewCamera.targetTexture=previewTexture;
            }
            SkinPreset skin=page==Page.Skins&&draft!=null?draft:root.Store.SkinFor(selectedWeapon);
            string key=selectedWeapon+":"+Newtonsoft.Json.JsonConvert.SerializeObject(skin);
            if(previewKey!=key&&Time.unscaledTime>=rebuildAfter) {
                if(preview) Destroy(preview.gameObject);
                preview=WeaponModelFactory.Create(previewPivot,Catalog.Weapon(selectedWeapon),skin,false);
                if(preview) {
                    Layer(preview.gameObject,30);
                    // Keep the visible geometry in weapon space. Rotated box corners leave
                    // excessive empty space around long, thin weapons.
                    var renderers=preview.GetComponentsInChildren<MeshRenderer>().Where(item=>item.enabled&&item.gameObject.activeInHierarchy).ToArray();
                    var points=new List<Vector3>();
                    bool hasBounds=false; Bounds bounds=new Bounds();
                    foreach(var renderer in renderers) {
                        var filter=renderer.GetComponent<MeshFilter>();
                        Vector3[] vertices;
                        if(filter&&filter.sharedMesh&&filter.sharedMesh.isReadable) vertices=filter.sharedMesh.vertices;
                        else {
                            Bounds local=renderer.localBounds; vertices=new Vector3[8];
                            for(int i=0;i<8;i++) vertices[i]=local.center+Vector3.Scale(local.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                        }
                        foreach(Vector3 vertex in vertices) {
                            Vector3 point=preview.transform.InverseTransformPoint(renderer.transform.TransformPoint(vertex));
                            points.Add(point);
                            if(!hasBounds) { bounds=new Bounds(point,Vector3.zero); hasBounds=true; } else bounds.Encapsulate(point);
                        }
                    }
                    previewPoints=points.ToArray(); fittedPreviewYaw=float.NaN;
                    previewBounds=hasBounds?bounds:new Bounds(Vector3.zero,Vector3.one);
                }
                previewKey=key;
            }
            previewPivot.localRotation=Quaternion.Euler(-9,yaw,0);
            previewCamera.aspect=previewAspect;
            Vector3 direction=new Vector3(1.05f,.32f,-1.35f).normalized;
            Quaternion view=Quaternion.LookRotation(-direction), inverseView=Quaternion.Inverse(view);
            if(!preview) return;
            if(fittedPreviewYaw!=yaw||!Mathf.Approximately(fittedPreviewAspect,previewAspect)||fittedPreviewPose!=preview.transform.localRotation) {
                Vector3 target=preview.transform.TransformPoint(previewBounds.center);
                Vector2 min=new Vector2(float.PositiveInfinity,float.PositiveInfinity), max=new Vector2(float.NegativeInfinity,float.NegativeInfinity);
                foreach(Vector3 vertex in previewPoints) {
                    Vector3 point=inverseView*(preview.transform.TransformPoint(vertex)-target);
                    min=Vector2.Min(min,new Vector2(point.x,point.y)); max=Vector2.Max(max,new Vector2(point.x,point.y));
                }
                if(previewPoints.Length>0) target+=view*new Vector3((min.x+max.x)*.5f,(min.y+max.y)*.5f,0);
                float tangent=Mathf.Tan(previewCamera.fieldOfView*.5f*Mathf.Deg2Rad), distance=.3f;
                foreach(Vector3 vertex in previewPoints) {
                    Vector3 point=inverseView*(preview.transform.TransformPoint(vertex)-target);
                    distance=Mathf.Max(distance,Mathf.Abs(point.x)/(tangent*previewAspect)-point.z,Mathf.Abs(point.y)/tangent-point.z);
                }
                fittedPreviewTarget=target; fittedPreviewDistance=distance*1.07f;
                fittedPreviewYaw=yaw; fittedPreviewAspect=previewAspect; fittedPreviewPose=preview.transform.localRotation;
            }
            previewCamera.transform.SetPositionAndRotation(fittedPreviewTarget+direction*fittedPreviewDistance*zoom,view);
        }
        static void Layer(GameObject item,int layer) { item.layer=layer; foreach(Transform child in item.transform) Layer(child.gameObject,layer); }
        void OnDestroy() {
            if(minimapAtlas != null && minimapAtlas.Texture) Destroy(minimapAtlas.Texture);
            if(previewCamera) Destroy(previewCamera.gameObject);
            if(previewPivot) Destroy(previewPivot.gameObject);
            if(previewTexture) { previewTexture.Release(); Destroy(previewTexture); }
            if(white) Destroy(white);
            if(font) Destroy(font);
        }
    }
}
