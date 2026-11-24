using System;

namespace WindBot.Game.AI.Lua
{
    /// <summary>
    /// WindBot Lua 对话事件键名与回调函数名常量表。
    /// </summary>
    public static class LuaDialogHookNames
    {
        // 事件标准键名（与 DialogsData JSON 字段保持一致）
        public const string Welcome = "welcome";
        public const string DeckError = "deckerror";
        public const string DuelStart = "duelstart";
        public const string NewTurn = "newturn";
        public const string EndTurn = "endturn";
        public const string DirectAttack = "directattack";
        public const string Attack = "attack";
        public const string OnDirectAttack = "ondirectattack";
        public const string Activate = "activate";
        public const string Summon = "summon";
        public const string SetMonster = "setmonster";
        public const string Chaining = "chaining";
        public const string Surrender = "surrender";
        public const string Custom = "custom";
        public const string FacedownMonsterName = "facedownmonstername";

        // 生命周期与回调钩子名（兼容模块返回式与全局函数式）
        public const string OnInit = "OnInit";
        public const string OnDispose = "OnDispose";
        public const string OnMessage = "OnMessage";
        public const string OnWelcome = "OnWelcome";
        public const string OnDeckError = "OnDeckError";
        public const string OnDuelStart = "OnDuelStart";
        public const string OnNewTurn = "OnNewTurn";
        public const string OnEndTurn = "OnEndTurn";
        public const string OnDirectAttackHook = "OnDirectAttack";
        public const string OnAttack = "OnAttack";
        public const string OnReceiveDirectAttack = "OnOnDirectAttack";
        public const string OnActivate = "OnActivate";
        public const string OnSummon = "OnSummon";
        public const string OnSetMonster = "OnSetMonster";
        public const string OnChaining = "OnChaining";
        public const string OnSurrender = "OnSurrender";
        public const string OnCustom = "OnCustom";

        /// <summary>
        /// 将事件别名（支持 JSON 键名及 snake_case 写法）标准化为统一事件键名。
        /// </summary>
        public static bool TryNormalizeEventKey(string rawKey, out string normalized)
        {
            if (string.IsNullOrWhiteSpace(rawKey))
            {
                normalized = null;
                return false;
            }

            string compact = rawKey.Trim().Replace("_", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
            switch (compact)
            {
                case Welcome:
                    normalized = Welcome;
                    return true;
                case DeckError:
                    normalized = DeckError;
                    return true;
                case DuelStart:
                    normalized = DuelStart;
                    return true;
                case NewTurn:
                    normalized = NewTurn;
                    return true;
                case EndTurn:
                    normalized = EndTurn;
                    return true;
                case DirectAttack:
                    normalized = DirectAttack;
                    return true;
                case Attack:
                    normalized = Attack;
                    return true;
                case OnDirectAttack:
                case "receivedirectattack":
                    normalized = OnDirectAttack;
                    return true;
                case Activate:
                    normalized = Activate;
                    return true;
                case Summon:
                    normalized = Summon;
                    return true;
                case SetMonster:
                    normalized = SetMonster;
                    return true;
                case Chaining:
                    normalized = Chaining;
                    return true;
                case Surrender:
                    normalized = Surrender;
                    return true;
                case Custom:
                    normalized = Custom;
                    return true;
                default:
                    normalized = null;
                    return false;
            }
        }

        /// <summary>
        /// 返回标准事件键名对应的模块/全局钩子函数名。
        /// </summary>
        public static string GetHookNameForEvent(string eventKey)
        {
            return eventKey switch
            {
                Welcome => OnWelcome,
                DeckError => OnDeckError,
                DuelStart => OnDuelStart,
                NewTurn => OnNewTurn,
                EndTurn => OnEndTurn,
                DirectAttack => OnDirectAttackHook,
                Attack => OnAttack,
                OnDirectAttack => OnReceiveDirectAttack,
                Activate => OnActivate,
                Summon => OnSummon,
                SetMonster => OnSetMonster,
                Chaining => OnChaining,
                Surrender => OnSurrender,
                Custom => OnCustom,
                _ => null
            };
        }
    }
}
