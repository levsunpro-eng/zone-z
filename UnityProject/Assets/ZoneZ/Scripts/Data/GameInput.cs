using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ZoneZ
{
    public static class GameInput
    {
        public static readonly string[] Actions = { "forward", "backward", "left", "right", "walk", "crouch", "jump", "fire", "alternate", "reload", "use", "inspect", "ultimate", "slot1", "slot2", "slot3", "slot4", "scoreboard" };
        static readonly string[] Names = { "Вперёд", "Назад", "Влево", "Вправо", "Ходьба (удерживать)", "Приседание (удерживать)", "Прыжок", "Огонь", "Прицел / альтернативный огонь", "Перезарядка", "Подобрать лут", "Осмотр оружия", "Ультимативная способность", "Основное оружие", "Пистолет", "Нож", "Zeus", "Таблица счёта (удерживать)" };
        static readonly KeyCode[] Keys = { KeyCode.W, KeyCode.S, KeyCode.A, KeyCode.D, KeyCode.LeftShift, KeyCode.LeftControl, KeyCode.Space, KeyCode.Mouse0, KeyCode.Mouse1, KeyCode.R, KeyCode.E, KeyCode.F, KeyCode.Q, KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Tab };

        public static Dictionary<string,int> DefaultBindings() => Actions.Select((action,i)=>new {action,key=(int)Keys[i]}).ToDictionary(x=>x.action,x=>x.key);
        public static bool IsValidKey(int key) => key!=(int)KeyCode.None&&key!=(int)KeyCode.Escape&&Enum.IsDefined(typeof(KeyCode),key);
        public static bool IsAction(string action) => Array.IndexOf(Actions,action)>=0;
        public static string ActionName(string action) { int i=Array.IndexOf(Actions,action); return i>=0?Names[i]:action; }
        public static KeyCode Key(string action)
        {
            int i=Array.IndexOf(Actions,action); if(i<0) return KeyCode.None;
            var bindings=GameRoot.Instance?.Profile?.keyBindings;
            return bindings!=null&&bindings.TryGetValue(action,out int code)&&IsValidKey(code)?(KeyCode)code:Keys[i];
        }
        public static bool Down(string action) { var key=Key(action); return key!=KeyCode.None&&Input.GetKeyDown(key); }
        public static bool Held(string action) { var key=Key(action); return key!=KeyCode.None&&Input.GetKey(key); }
        public static bool Up(string action) { var key=Key(action); return key!=KeyCode.None&&Input.GetKeyUp(key); }
        public static string Label(string action) => KeyLabel(Key(action));
        public static string KeyLabel(KeyCode key)
        {
            if(key>=KeyCode.Alpha0&&key<=KeyCode.Alpha9) return ((int)key-(int)KeyCode.Alpha0).ToString();
            switch(key) {
                case KeyCode.Mouse0:return "ЛКМ"; case KeyCode.Mouse1:return "ПКМ"; case KeyCode.Mouse2:return "Средняя кнопка";
                case KeyCode.Mouse3:return "Кнопка мыши 4"; case KeyCode.Mouse4:return "Кнопка мыши 5";
                case KeyCode.LeftShift:return "Левый Shift"; case KeyCode.RightShift:return "Правый Shift";
                case KeyCode.LeftControl:return "Левый Ctrl"; case KeyCode.RightControl:return "Правый Ctrl";
                case KeyCode.LeftAlt:return "Левый Alt"; case KeyCode.RightAlt:return "Правый Alt";
                case KeyCode.Space:return "Пробел"; case KeyCode.UpArrow:return "↑"; case KeyCode.DownArrow:return "↓";
                case KeyCode.LeftArrow:return "←"; case KeyCode.RightArrow:return "→"; case KeyCode.Return:return "Enter";
                case KeyCode.None:return "Не назначено"; default:return key.ToString();
            }
        }

        public static Dictionary<string,int> NormalizeBindings(Dictionary<string,int> values)
        {
            var normalized=new Dictionary<string,int>(); var used=new HashSet<int>();
            foreach(string action in Actions)
                if(values!=null&&values.TryGetValue(action,out int key)&&IsValidKey(key)&&used.Add(key)) normalized[action]=key;
            for(int i=0;i<Actions.Length;i++) if(!normalized.ContainsKey(Actions[i])) {
                int key=(int)Keys[i];
                if(used.Contains(key)) key=Keys.Select(x=>(int)x).First(x=>!used.Contains(x));
                normalized[Actions[i]]=key; used.Add(key);
            }
            return normalized;
        }

        // Conflicting assignments swap keys, so every action remains accessible.
        public static bool Rebind(Dictionary<string,int> bindings,string action,int key,bool replace,out string conflict)
        {
            conflict=null;
            if(bindings==null||!IsAction(action)||!IsValidKey(key)) return false;
            conflict=bindings.FirstOrDefault(pair=>pair.Key!=action&&pair.Value==key).Key;
            if(conflict!=null&&!replace) return false;
            int previous=bindings.TryGetValue(action,out int current)?current:(int)Keys[Array.IndexOf(Actions,action)];
            if(conflict!=null) bindings[conflict]=previous;
            bindings[action]=key; return true;
        }
    }
}
