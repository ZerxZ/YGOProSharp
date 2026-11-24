namespace WindBot.Game.AI.Lua
{
    /// <summary>
    /// Lua 卡组脚本标准回调函数名称常量表。
    /// 脚本既可在模块返回表（<c>return M</c>）中定义，也可在脚本作用域全局定义。
    /// 未定义的回调将自动回退到 <see cref="DefaultExecutor"/> 的默认实现。
    /// </summary>
    public static class LuaDeckHookNames
    {
        public const string OnInit = "OnInit";
        public const string OnDispose = "OnDispose";

        public const string OnRockPaperScissors = "OnRockPaperScissors";
        public const string OnSelectHand = "OnSelectHand";

        public const string OnBattle = "OnBattle";
        public const string OnSelectAttacker = "OnSelectAttacker";
        public const string OnSelectAttackTarget = "OnSelectAttackTarget";
        public const string OnPreBattleBetween = "OnPreBattleBetween";
        public const string OnSelectBattleReplay = "OnSelectBattleReplay";

        public const string OnPreActivate = "OnPreActivate";
        public const string OnChaining = "OnChaining";
        public const string OnChainSolved = "OnChainSolved";
        public const string OnChainEnd = "OnChainEnd";
        public const string OnSelectChain = "OnSelectChain";

        public const string OnReceivingAnnouce = "OnReceivingAnnouce";
        public const string OnReceivingAnnounceAlias = "OnReceivingAnnounce";
        public const string OnNewPhase = "OnNewPhase";
        public const string OnNewTurn = "OnNewTurn";
        public const string OnDraw = "OnDraw";
        public const string OnPlayerHint = "OnPlayerHint";
        public const string OnHintZone = "OnHintZone";
        public const string OnMove = "OnMove";
        public const string OnPosChange = "OnPosChange";
        public const string OnSummoning = "OnSummoning";
        public const string OnSpSummoning = "OnSpSummoning";
        public const string OnSpSummoned = "OnSpSummoned";

        public const string OnSelectCard = "OnSelectCard";
        public const string OnSelectTribute = "OnSelectTribute";
        public const string OnSelectSum = "OnSelectSum";
        public const string OnSelectFusionMaterial = "OnSelectFusionMaterial";
        public const string OnSelectSynchroMaterial = "OnSelectSynchroMaterial";
        public const string OnSelectXyzMaterial = "OnSelectXyzMaterial";
        public const string OnSelectLinkMaterial = "OnSelectLinkMaterial";
        public const string OnSelectRitualTribute = "OnSelectRitualTribute";
        public const string OnSelectPendulumSummon = "OnSelectPendulumSummon";
        public const string OnCardSorting = "OnCardSorting";

        public const string OnSelectYesNo = "OnSelectYesNo";
        public const string OnSelectOption = "OnSelectOption";
        public const string OnSelectPlace = "OnSelectPlace";
        public const string OnSelectDisfield = "OnSelectDisfield";
        public const string OnSelectPosition = "OnSelectPosition";
        public const string OnSelectBattleDirectAttack = "OnSelectBattleDirectAttack";
        public const string OnSelectMonsterSummonOrSet = "OnSelectMonsterSummonOrSet";
        public const string OnAnnounceCard = "OnAnnounceCard";
    }
}
