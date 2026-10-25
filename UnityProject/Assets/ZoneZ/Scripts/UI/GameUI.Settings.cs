using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ZoneZ
{
    public sealed partial class GameUI
    {
        int qualityLevel, frameLimit;
        bool fullscreen, vSync;
        int settingsTab, resolutionWidth, resolutionHeight;
        float weaponVolume, footstepVolume, effectsVolume, ambientVolume;
        float scopeSensitivity, viewMotion, interfaceVolume, menuMusicVolume, crosshairThickness;
        bool invertY, reducedFlashes;
        string crosshairColor;
        Dictionary<string,int> keyBindings;
        string bindingAction, bindingConflict;
        int proposedKey;
        bool resetBindings;
        float captureStarted, screenConfirmUntil;
        int previousScreenWidth, previousScreenHeight;
        FullScreenMode previousScreenMode;
        GameProfile pendingSettings;
        Vector2Int[] resolutionChoices;

        void Settings(float x,float y,float width)
        {
            if(!settingsLoaded) {
                sensitivity=Profile.sensitivity; volume=Profile.volume; fov=Profile.fov; textScale=Profile.uiScale;
                qualityLevel=Profile.qualityLevel; frameLimit=Profile.frameLimit; fullscreen=Profile.fullscreen; vSync=Profile.vSync;
                weaponVolume=Profile.weaponVolume; footstepVolume=Profile.footstepVolume; effectsVolume=Profile.effectsVolume; ambientVolume=Profile.ambientVolume;
                resolutionWidth=Profile.resolutionWidth; resolutionHeight=Profile.resolutionHeight;
                keyBindings=new Dictionary<string,int>(Profile.keyBindings);
                scopeSensitivity=Profile.scopeSensitivity; viewMotion=Profile.viewMotion; interfaceVolume=Profile.interfaceVolume; menuMusicVolume=Profile.menuMusicVolume;
                invertY=Profile.invertY; reducedFlashes=Profile.reducedFlashes; crosshairColor=Profile.crosshairColor; crosshairThickness=Profile.crosshairThickness;
                settingsLoaded=true;
            }
            Text(new Rect(x,y,width,50),"Настройки",heading);
            Text(new Rect(x,y+53,width,36),"Изменения сохраняются по кнопке. Esc всегда открывает паузу или закрывает диалог.",small,muted);
            float left=x, right=x+width*.52f, half=width*.48f;
            string[] tabs={"Экран","Камера","Звук","Управление","Доступность"};
            for(int i=0;i<tabs.Length;i++) if(Button(new Rect(x+i*width/tabs.Length,y+105,width/tabs.Length-10,39),tabs[i],settingsTab==i)) settingsTab=i;
            if(settingsTab==0) {
                string[] quality={"Низкое","Среднее","Высокое"};
                if(Button(new Rect(left,y+178,half,52),"Качество: "+quality[qualityLevel])) qualityLevel=(qualityLevel+1)%quality.Length;
                if(Button(new Rect(right,y+178,half,52),"Лимит кадров: "+frameLimit+" FPS")) frameLimit=frameLimit==60?120:frameLimit==120?144:60;
                if(Button(new Rect(left,y+251,half,52),fullscreen?"Экран: безрамочный":"Экран: окно")) fullscreen=!fullscreen;
                if(Button(new Rect(right,y+251,half,52),"Вертикальная синхронизация: "+(vSync?"вкл.":"выкл."))) vSync=!vSync;
                Text(new Rect(x,y+334,width,32),"Разрешение рендера / окна",label);
                if(Button(new Rect(x,y+382,57,47),"‹")) CycleResolution(-1);
                string resolution=resolutionWidth==0?"Авто · родное разрешение монитора":resolutionWidth+" × "+resolutionHeight;
                Text(new Rect(x+75,y+392,width-150,37),resolution,center,gold);
                if(Button(new Rect(x+width-57,y+382,57,47),"›")) CycleResolution(1);
                Text(new Rect(x,y+467,width,65),"Текущее изображение: "+Screen.width+" × "+Screen.height+". Новый режим нужно подтвердить за 15 секунд; без подтверждения вернётся прежний.",small,muted);
            } else if(settingsTab==1) {
                SettingSlider(new Rect(left,y+183,half,65),"Чувствительность мыши",ref sensitivity,.1f,4,sensitivity.ToString("0.00"));
                SettingSlider(new Rect(right,y+183,half,65),"Множитель мыши в прицеле",ref scopeSensitivity,.1f,3,scopeSensitivity.ToString("0.00"));
                SettingSlider(new Rect(left,y+291,half,65),"Поле зрения по вертикали",ref fov,50,90,Mathf.RoundToInt(fov)+"°");
                SettingSlider(new Rect(right,y+291,half,65),"Покачивание оружия",ref viewMotion,0,1,Mathf.RoundToInt(viewMotion*100)+"%");
                if(Button(new Rect(left,y+405,half,52),"Инверсия оси Y: "+(invertY?"вкл.":"выкл."))) invertY=!invertY;
                Text(new Rect(right,y+405,half,120),"Ускорение мыши не применяется. Поле зрения при увеличении задаётся оружием. Покачивание влияет только на вид оружия, сохраняя боевую отдачу.",small,muted);
            } else if(settingsTab==2) {
                SettingSlider(new Rect(left,y+164,half,65),"Общая громкость",ref volume,0,1,Mathf.RoundToInt(volume*100)+"%");
                SettingSlider(new Rect(right,y+164,half,65),"Оружие",ref weaponVolume,0,1,Mathf.RoundToInt(weaponVolume*100)+"%");
                SettingSlider(new Rect(left,y+257,half,65),"Шаги",ref footstepVolume,0,1,Mathf.RoundToInt(footstepVolume*100)+"%");
                SettingSlider(new Rect(right,y+257,half,65),"Эффекты и способности",ref effectsVolume,0,1,Mathf.RoundToInt(effectsVolume*100)+"%");
                SettingSlider(new Rect(left,y+350,half,65),"Окружение",ref ambientVolume,0,1,Mathf.RoundToInt(ambientVolume*100)+"%");
                SettingSlider(new Rect(right,y+350,half,65),"Интерфейс",ref interfaceVolume,0,1,Mathf.RoundToInt(interfaceVolume*100)+"%");
                SettingSlider(new Rect(left,y+443,half,65),"Музыка меню",ref menuMusicVolume,0,1,Mathf.RoundToInt(menuMusicVolume*100)+"%");
                Text(new Rect(right,y+443,half,83),"Общая громкость умножается на каждый канал. В бою музыка отключена.",small,muted);
            } else if(settingsTab==3) {
                int rows=(GameInput.Actions.Length+1)/2;
                for(int i=0;i<GameInput.Actions.Length;i++) {
                    string action=GameInput.Actions[i]; float sx=i<rows?left:right, sy=y+163+(i%rows)*40;
                    Text(new Rect(sx,sy+6,half*.57f,29),GameInput.ActionName(action),small);
                    if(Button(new Rect(sx+half*.60f,sy,half*.40f,34),GameInput.KeyLabel((KeyCode)keyBindings[action]))) {
                        bindingAction=action; bindingConflict=null; captureStarted=Time.unscaledTime;
                    }
                }
                Text(new Rect(x,y+530,width,28),"Esc зарезервирован для меню. При конфликте можно обменять назначения двух действий.",small,muted);
            } else {
                SettingSlider(new Rect(left,y+183,half,65),"Масштаб текста",ref textScale,.8f,1.5f,Mathf.RoundToInt(textScale*100)+"%");
                SettingSlider(new Rect(right,y+183,half,65),"Толщина прицела",ref crosshairThickness,1,4,crosshairThickness.ToString("0.0"));
                Text(new Rect(left,y+292,169,40),"Цвет прицела #",label);
                crosshairColor=TextInput(new Rect(left+180,y+285,176,43),crosshairColor??"",6,"crosshair-color").ToUpperInvariant();
                string[] colors={"DDE6CD","F5E267","65D9F1","EB8DD4","FFFFFF"};
                for(int i=0;i<colors.Length;i++) {
                    Rect r=new Rect(right+i*half/5,y+285,half/5-8,43);
                    ColorUtility.TryParseHtmlString("#"+colors[i],out Color swatch);
                    if(Button(r,(i+1).ToString())) crosshairColor=colors[i];
                    Fill(new Rect(r.x+8,r.yMax-9,r.width-16,5),swatch);
                }
                if(Button(new Rect(left,y+371,half,52),"Уменьшенные вспышки: "+(reducedFlashes?"вкл.":"выкл."))) reducedFlashes=!reducedFlashes;
                if(ColorUtility.TryParseHtmlString("#"+crosshairColor,out Color color)) {
                    float cx=right+half*.5f, cy=y+397;
                    Fill(new Rect(cx-25,cy-crosshairThickness*.5f,17,crosshairThickness),color); Fill(new Rect(cx+8,cy-crosshairThickness*.5f,17,crosshairThickness),color);
                    Fill(new Rect(cx-crosshairThickness*.5f,cy-25,crosshairThickness,17),color); Fill(new Rect(cx-crosshairThickness*.5f,cy+8,crosshairThickness,17),color);
                }
                Text(new Rect(x,y+461,width,73),"Союзники: ◆ имя · Разведка: [ РАЗВЕДКА ]. Состояния дополнены текстом.\nTab / Shift+Tab — переход по кнопкам · Enter — выбрать · Esc — назад.",small,muted);
            }
            if(Button(new Rect(x,y+578,half,49),"Сохранить настройки",true)) SaveSettingsDraft();
            if(Button(new Rect(right,y+578,half,49),settingsTab==3?"Сбросить раскладку":"Вернуть исходные настройки")) { resetBindings=settingsTab==3; confirmReset=true; }
        }
        void ApplyRuntimeSettings(bool applyScreen=true)
        {
            AudioListener.volume=Profile.volume;
            int level=Profile.qualityLevel;
            QualitySettings.globalTextureMipmapLimit=2-level;
            QualitySettings.shadows=level==0?UnityEngine.ShadowQuality.HardOnly:UnityEngine.ShadowQuality.All;
            if(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline) {
                pipeline.msaaSampleCount=level==0?1:level==1?2:4;
                pipeline.renderScale=level==0?.8f:1;
                pipeline.shadowDistance=level==0?35:level==1?65:95;
            }
            QualitySettings.vSyncCount=Profile.vSync?1:0;
            Application.targetFrameRate=Profile.frameLimit;
            if(applyScreen) SetDisplay(Profile.resolutionWidth,Profile.resolutionHeight,Profile.fullscreen);
            if(root.Match&&root.Match.Local&&root.Match.Local.ViewCamera&&(!root.Match.Local.Weapon||!root.Match.Local.Weapon.Scoped)) root.Match.Local.ViewCamera.fieldOfView=Profile.fov;
        }
        static void SetDisplay(int width,int height,bool full)
        {
            if(width<=0||height<=0) { width=Screen.currentResolution.width; height=Screen.currentResolution.height; }
            if(width<=0||height<=0) { width=Screen.width; height=Screen.height; }
            Screen.SetResolution(width,height,full?FullScreenMode.FullScreenWindow:FullScreenMode.Windowed);
        }
        void CycleResolution(int delta)
        {
            if(resolutionChoices==null) {
                var choices=Screen.resolutions.Select(r=>new Vector2Int(r.width,r.height)).Where(r=>r.x>=640&&r.y>=360).Distinct().OrderBy(r=>r.x).ThenBy(r=>r.y).ToList();
                var current=new Vector2Int(Screen.width,Screen.height); if(!choices.Contains(current)) choices.Add(current);
                var saved=new Vector2Int(resolutionWidth,resolutionHeight); if(saved.x>0&&!choices.Contains(saved)) choices.Add(saved);
                choices.Insert(0,Vector2Int.zero); resolutionChoices=choices.ToArray();
            }
            int at=Array.FindIndex(resolutionChoices,r=>r.x==resolutionWidth&&r.y==resolutionHeight);
            var next=resolutionChoices[(Mathf.Max(0,at)+delta+resolutionChoices.Length)%resolutionChoices.Length];
            resolutionWidth=next.x; resolutionHeight=next.y;
        }
        GameProfile SettingsSnapshot() => new GameProfile {
            sensitivity=sensitivity,volume=volume,fov=fov,uiScale=textScale,qualityLevel=qualityLevel,frameLimit=frameLimit,fullscreen=fullscreen,vSync=vSync,
            weaponVolume=weaponVolume,footstepVolume=footstepVolume,effectsVolume=effectsVolume,ambientVolume=ambientVolume,
            resolutionWidth=resolutionWidth,resolutionHeight=resolutionHeight,keyBindings=new Dictionary<string,int>(keyBindings),
            scopeSensitivity=scopeSensitivity,viewMotion=viewMotion,interfaceVolume=interfaceVolume,menuMusicVolume=menuMusicVolume,
            invertY=invertY,reducedFlashes=reducedFlashes,crosshairColor=crosshairColor,crosshairThickness=crosshairThickness
        };
        void SaveSettingsDraft()
        {
            if(crosshairColor==null||crosshairColor.Length!=6||!crosshairColor.All(Uri.IsHexDigit)) { Notify("Цвет прицела должен содержать 6 символов HEX"); return; }
            var settings=SettingsSnapshot();
            bool displayChanged=settings.resolutionWidth!=Profile.resolutionWidth||settings.resolutionHeight!=Profile.resolutionHeight||settings.fullscreen!=Profile.fullscreen;
            if(displayChanged) {
                previousScreenWidth=Screen.width; previousScreenHeight=Screen.height; previousScreenMode=Screen.fullScreenMode;
                pendingSettings=settings; screenConfirmUntil=Time.unscaledTime+15;
                SetDisplay(settings.resolutionWidth,settings.resolutionHeight,settings.fullscreen);
            } else CommitSettings(settings);
        }
        bool CommitSettings(GameProfile settings)
        {
            if(!root.Store.SaveSettings(settings,out string error)) { Notify(error); return false; }
            ApplyRuntimeSettings(false); Notify("Настройки сохранены"); return true;
        }
        void RevertDisplay()
        {
            if(pendingSettings==null) return;
            Screen.SetResolution(previousScreenWidth,previousScreenHeight,previousScreenMode);
            pendingSettings=null; screenConfirmUntil=0;
            resolutionWidth=Profile.resolutionWidth; resolutionHeight=Profile.resolutionHeight; fullscreen=Profile.fullscreen;
            Notify("Прежний режим экрана восстановлен. Остальные изменения остаются черновиком.");
        }
        void DisplayConfirmation()
        {
            Modal("Сохранить новый режим экрана?","Текущее изображение: "+Screen.width+" × "+Screen.height+". Автоматический возврат через "+Mathf.CeilToInt(screenConfirmUntil-Time.unscaledTime)+" с. Esc вернёт прежний режим.");
            if(Button(new Rect(ox+390,511,397,53),"Подтвердить",true)) {
                if(CommitSettings(pendingSettings)) { pendingSettings=null; screenConfirmUntil=0; }
                else {
                    string error=root.Store.LastError; RevertDisplay(); Notify(error);
                }
            }
            if(Button(new Rect(ox+810,511,400,53),"Вернуть прежний")) RevertDisplay();
        }
        void CaptureBindingEvent()
        {
            if(bindingAction==null||bindingConflict!=null||Time.unscaledTime-captureStarted<.2f) return;
            Event e=Event.current;
            // The cancel button must remain clickable without assigning Mouse0.
            Rect cancel=new Rect(ox+610,511,380,53);
            if(e.type==EventType.MouseDown&&cancel.Contains(e.mousePosition)) return;
            int code=e.type==EventType.MouseDown?(int)KeyCode.Mouse0+e.button:e.type==EventType.KeyDown?(int)e.keyCode:0;
            if(code==0) return;
            if(code==(int)KeyCode.Escape) { bindingAction=null; e.Use(); return; }
            if(!GameInput.IsValidKey(code)) { Notify("Эта клавиша недоступна для назначения"); e.Use(); return; }
            if(GameInput.Rebind(keyBindings,bindingAction,code,false,out string conflict)) {
                Notify(GameInput.ActionName(bindingAction)+": "+GameInput.KeyLabel((KeyCode)code)); bindingAction=null;
            } else { proposedKey=code; bindingConflict=conflict; }
            e.Use();
        }
        void BindingDialog()
        {
            if(bindingConflict==null) {
                Modal("Назначение: "+GameInput.ActionName(bindingAction),"Нажмите клавишу или кнопку мыши. Esc отменяет ввод и всегда остаётся доступным для меню.");
                if(Button(new Rect(ox+610,511,380,53),"Отмена",true)) bindingAction=null;
            } else {
                Modal("Клавиша уже назначена",GameInput.KeyLabel((KeyCode)proposedKey)+" используется для «"+GameInput.ActionName(bindingConflict)+"». Поменять клавиши действий «"+GameInput.ActionName(bindingAction)+"» и «"+GameInput.ActionName(bindingConflict)+"» местами?");
                if(Button(new Rect(ox+390,511,397,53),"Поменять местами",true)) {
                    GameInput.Rebind(keyBindings,bindingAction,proposedKey,true,out _); bindingAction=null; bindingConflict=null;
                }
                if(Button(new Rect(ox+810,511,400,53),"Отмена")) { bindingAction=null; bindingConflict=null; }
            }
        }
        void SettingSlider(Rect r,string name,ref float value,float min,float max,string caption)
        {
            Text(new Rect(r.x,r.y,r.width-95,34),name,label);
            Text(new Rect(r.xMax-86,r.y,86,32),caption,label,gold);
            if(Button(new Rect(r.x,r.y+39,34,27),"−")) value=Mathf.Max(min,value-(max-min)*.025f);
            value=Slider(new Rect(r.x+46,r.y+48,r.width-92,16),value,min,max);
            if(Button(new Rect(r.xMax-34,r.y+39,34,27),"+")) value=Mathf.Min(max,value+(max-min)*.025f);
        }
        float Slider(Rect r,float value,float min,float max) {
            value=GUI.HorizontalSlider(r,value,min,max,sliderTrack,sliderThumb);
            float progress=Mathf.InverseLerp(min,max,value);
            Fill(new Rect(r.x,r.y+6,r.width,4),new Color(.29f,.35f,.31f));
            Fill(new Rect(r.x,r.y+6,(r.width-16)*progress+8,4),gold);
            Fill(new Rect(r.x+(r.width-16)*progress,r.y,16,16),foreground);
            return value;
        }
        void Pause()
        {
            Fill(new Rect(0,-2000,uiW,5000),new Color(.02f,.035f,.027f,.88f));
            if(pauseSettings) {
                Fill(new Rect(ox+238,85,1124,730),panel); Settings(ox+265,106,1070);
                if(Button(new Rect(ox+1260,101,75,36),"Назад")) pauseSettings=false;
                return;
            }
            if(NetworkActive) {
                Rect online=new Rect(ox+434,120,732,652); Fill(online,panel);
                Text(new Rect(online.x+33,online.y+28,666,60),"МЕНЮ МАТЧА",title,gold);
                Text(new Rect(online.x+33,online.y+101,666,46),"Бой продолжается — вы остаётесь на карте",label,muted);
                NetworkDetails(new Rect(online.x+33,online.y+174,666,224));
                if(Button(new Rect(online.x+33,online.y+440,666,57),"Вернуться в бой",true)) root.SetPaused(false);
                if(Button(new Rect(online.x+33,online.y+517,666,45),"Настройки")) { pauseSettings=true; settingsLoaded=false; }
                if(Button(new Rect(online.x+33,online.y+580,666,45),root.Network.IsHost?"Закрыть сервер и выйти":"Отключиться и выйти")) exitConfirm=true;
            } else {
                Rect r=new Rect(ox+536,233,528,440); Fill(r,panel);
                Text(new Rect(r.x+33,r.y+28,462,60),"ПАУЗА",title,gold);
                Text(new Rect(r.x+33,r.y+105,462,46),"Локальный матч остановлен",label,muted);
                if(Button(new Rect(r.x+33,r.y+178,462,57),"Продолжить",true)) root.SetPaused(false);
                if(Button(new Rect(r.x+33,r.y+254,462,57),"Настройки")) { pauseSettings=true; settingsLoaded=false; }
                if(Button(new Rect(r.x+33,r.y+330,462,57),"Выйти в меню")) exitConfirm=true;
            }
        }
        void Modal(string headingText,string description)
        {
            Fill(new Rect(0,-2000,uiW,5000),new Color(.015f,.025f,.02f,.86f));
            Fill(new Rect(ox+359,294,882,305),panel);
            Text(new Rect(ox+390,318,820,55),headingText,heading,gold);
            Text(new Rect(ox+390,387,820,88),description,label);
        }
        void ExitConfirmation()
        {
            string reason=NetworkActive?(root.Network.IsHost?"Вы создали сервер. Выход закроет матч для всех друзей и отключит их от игры.":"Вы отключитесь от сервера. Остальные игроки продолжат матч."):"Матч не завершён — награда не начислится. Купленные предметы и настройки сохранятся.";
            Modal(NetworkActive&&root.Network.IsHost?"Закрыть сервер для всех?":"Выйти из матча?",reason);
            if(Button(new Rect(ox+390,511,397,53),"Остаться",true)) exitConfirm=false;
            if(Button(new Rect(ox+810,511,400,53),"Выйти в главное меню")) { exitConfirm=false; pauseSettings=false; root.Match.ReturnToMenu(); }
        }
        void DraftConfirmation()
        {
            Modal("Есть несохранённый пресет","Сохраните и примените изменения либо оставьте прежнее оформление оружия.");
            if(Button(new Rect(ox+390,508,258,60),"Сохранить",true)&&SaveDraft(false)) {
                Action action=pendingNavigation; pendingNavigation=null; action?.Invoke();
            }
            if(Button(new Rect(ox+670,508,260,60),"Не сохранять")) {
                dirty=false; draft=root.Store.SkinFor(selectedWeapon); previewKey=null;
                Action action=pendingNavigation; pendingNavigation=null; action?.Invoke();
            }
            if(Button(new Rect(ox+952,508,258,60),"Продолжить правку")) pendingNavigation=null;
        }
        void DeleteConfirmation()
        {
            Modal("Удалить пресет?","«"+deleteCandidate.name+"» будет удалён. Купленные узоры и наклейки останутся. Применённый скин вернётся к базовому виду.");
            if(Button(new Rect(ox+390,511,397,53),"Отмена",true)) deleteCandidate=null;
            if(Button(new Rect(ox+810,511,400,53),"Удалить пресет")) {
                string id=deleteCandidate.id;
                if(root.Store.DeleteSkin(id,out string error)) {
                    if(draft!=null&&draft.id==id) { draft=root.Store.SkinFor(selectedWeapon); dirty=false; }
                    deleteCandidate=null; previewKey=null; Notify("Пресет удалён");
                } else Notify(error);
            }
        }
        void PresetConflict()
        {
            Modal("Это имя уже используется","«"+conflictingDraft.name+"»: перезаписать существующий вариант или сохранить отдельную копию с новым ID?");
            if(Button(new Rect(ox+390,508,258,60),"Перезаписать",true)) {
                var value=conflictingDraft.Copy(); value.id=conflictingId;
                if(CommitDraft(value)) FinishPresetConflict();
            }
            if(Button(new Rect(ox+670,508,260,60),"Сохранить копию")) {
                var value=conflictingDraft.Copy(); value.id="";
                if(CommitDraft(value)) FinishPresetConflict();
            }
            if(Button(new Rect(ox+952,508,258,60),"Отмена")) conflictingDraft=null;
        }
        void FinishPresetConflict()
        {
            conflictingDraft=null;
            Action navigation=pendingNavigation; pendingNavigation=null; navigation?.Invoke();
        }
        void ResetConfirmation()
        {
            Modal(resetBindings?"Сбросить раскладку?":"Сбросить настройки?",resetBindings?"Все действия вернутся к исходным клавишам WASD, ЛКМ / ПКМ, R, E, F, Q и 1–4. Другие настройки сохранятся.":"Камера, звук, масштаб текста, экран и раскладка вернутся к исходным значениям. Покупки и скины сохранятся.");
            if(Button(new Rect(ox+390,511,397,53),"Отмена",true)) confirmReset=false;
            if(Button(new Rect(ox+810,511,400,53),"Сбросить")) {
                keyBindings=GameInput.DefaultBindings();
                if(!resetBindings) {
                    sensitivity=1; volume=.7f; fov=60; textScale=1; qualityLevel=1; frameLimit=60; fullscreen=true; vSync=false;
                    weaponVolume=footstepVolume=effectsVolume=1; ambientVolume=.5f; resolutionWidth=resolutionHeight=0;
                    scopeSensitivity=viewMotion=1; interfaceVolume=.7f; menuMusicVolume=.3f; invertY=reducedFlashes=false; crosshairColor="DDE6CD"; crosshairThickness=2;
                }
                confirmReset=false; Notify("Исходные значения выбраны. Нажмите «Сохранить настройки».");
            }
        }
        void SkinParameters()
        {
            Fill(new Rect(0,-2000,uiW,5000),new Color(.015f,.025f,.02f,.88f));
            Fill(new Rect(ox+322,111,956,675),panel);
            Text(new Rect(ox+353,133,892,48),"Узор и наклейки",heading,gold);
            Text(new Rect(ox+353,190,892,49),"Оформление ограничено цветовой зоной. Купленная наклейка может повторяться в четырёх слотах.",small,muted);
            float before=draft.patternScale;
            SettingSlider(new Rect(ox+353,262,423,69),"Масштаб узора",ref draft.patternScale,.5f,2,draft.patternScale.ToString("0.00"));
            if(before!=draft.patternScale) DirtySkin();
            before=draft.patternRotation;
            SettingSlider(new Rect(ox+805,262,440,69),"Поворот узора",ref draft.patternRotation,-180,180,draft.patternRotation.ToString("0")+"°");
            if(before!=draft.patternRotation) DirtySkin();
            for(int i=0;i<4;i++) {
                float y=367+i*78; before=draft.stickerScales[i];
                SettingSlider(new Rect(ox+353,y,423,65),"Наклейка "+(i+1)+" · размер",ref draft.stickerScales[i],.5f,1.5f,draft.stickerScales[i].ToString("0.00"));
                if(before!=draft.stickerScales[i]) DirtySkin();
                before=draft.stickerRotations[i];
                SettingSlider(new Rect(ox+805,y,440,65),"Поворот "+(i+1),ref draft.stickerRotations[i],-180,180,draft.stickerRotations[i].ToString("0")+"°");
                if(before!=draft.stickerRotations[i]) DirtySkin();
            }
            if(Button(new Rect(ox+353,716,892,43),"Вернуться к пресету",true)) skinParameters=false;
        }
    }
}
