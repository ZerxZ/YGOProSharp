-- =============================================================================
-- ExampleOldSchool.lua
-- YGOProSharp.WindBot 泛用 Lua 卡组执行器示例（xmake 风格声明式 DSL + Lua 5.4）
--
-- 运行示例（通过 CLI 自动使用 NLua 5.4 运行时）：
--   dotnet run --project YGOProSharp.Cli/YGOProSharp.Cli.csproj -c Release -- \
--       windbot DbPath=cards.cdb Host=127.0.0.1 Port=7911 Deck=ExampleOldSchool
-- =============================================================================

local CardId = {
    -- 怪兽
    AncientGearGolem = 83104731,
    Frostosaurus = 6631034,
    AlexandriteDragon = 43096270,
    GeneWarpedWarwolf = 69247929,
    GearGolemTheMovingFortress = 30190809,
    EvilswarmHeliotrope = 77542832,
    LusterDragon = 11091375,
    InsectKnight = 35052053,
    ArchfiendSoldier = 49881766,

    -- 魔法
    HeavyStorm = 19613556,
    DarkHole = 53129443,
    Raigeki = 12580477,
    HammerShot = 26412047,
    Fissure = 66788016,
    SwordsOfRevealingLight = 72302403,
    DoubleSummon = 43422537,

    -- 陷阱
    MirrorForce = 44095762,
    DimensionalPrison = 70342110,
}

-- 脚本状态（每局独立上下文，不会与其他 Bot 实例串状态）
local last_double_summon_turn = -1

deck("ExampleOldSchool")
    -- 关联默认 .ydk 卡组名（可被命令行 DeckFile=... 覆盖）
    set_deckfile("AI_OldSchool")
    set_level("Easy")
    set_description("xmake 风格 Lua 卡组执行器完整示例（基于经典 OldSchool 卡组）")
    set_author("YGOProSharp")

    -- 1. 复用 DefaultExecutor 内置通用判定规则
    add_default("Activate", CardId.HeavyStorm, "DefaultHeavyStorm")
    add_default("SpellSet", "DefaultSpellSet")
    add_default("Activate", CardId.DarkHole, "DefaultDarkHole")
    add_default("Activate", CardId.Raigeki, "DefaultRaigeki")
    add_default("Activate", CardId.HammerShot, "DefaultHammerShot")

    -- 2. 无条件发动（省略回调即有条件可执行时直接触发）
    on_activate(CardId.Fissure)

    -- 3. 绑定自定义 Lua 函数名（支持先声明规则、在文件底部定义函数，严格保留优先级顺序）
    on_activate(CardId.SwordsOfRevealingLight, "should_activate_swords")
    on_activate(CardId.DoubleSummon, "should_activate_double_summon")

    -- 4. 怪兽召唤规则：支持单卡默认规则、批量卡号注册（类似 xmake add_files）
    add_default("Summon", CardId.AncientGearGolem, "DefaultMonsterSummon")
    add_default("Summon", CardId.Frostosaurus, "DefaultMonsterSummon")
    on_monster_set(CardId.GearGolemTheMovingFortress)
    add_executors("SummonOrSet", {
        CardId.AlexandriteDragon,
        CardId.GeneWarpedWarwolf,
        CardId.EvilswarmHeliotrope,
        CardId.LusterDragon,
        CardId.InsectKnight,
        CardId.ArchfiendSoldier,
    })

    -- 5. 表示形式与坑卡
    add_default("Repos", "DefaultMonsterRepos")
    add_default("Activate", CardId.MirrorForce, "DefaultTrap")
    add_default("Activate", CardId.DimensionalPrison, "DefaultTrap")

    -- 6. 生命周期与决策钩子
    on_init(function(ctx)
        ctx:LogInfo("ExampleOldSchool Lua deck initialized.")
    end)

    on_new_turn(function(ctx)
        ctx:LogDebug("ExampleOldSchool new turn: " .. tostring(ctx.Duel.Turn))
    end)

    on_select_yesno(function(ctx, desc)
        -- 返回 nil 表示不接管，自动回退 DefaultExecutor 默认实现
        return nil
    end)

    on_select_card(function(ctx, cards, min, max, hint, cancelable)
        -- 示例：仅在必选单卡且候选非空时示范返回 Lua 数组（其余场景返回 nil 交由默认逻辑）
        return nil
    end)

    on_dispose(function(ctx)
        ctx:LogDebug("ExampleOldSchool Lua deck disposed.")
    end)
deck_end()

-- =============================================================================
-- 具名规则实现（在 deck_end() 后定义，DSL 会按书写顺序延迟绑定）
-- =============================================================================

function should_activate_swords(ctx)
    local enemyMonsters = ctx.EnemyField:GetMonsters()
    for i = 0, enemyMonsters.Count - 1 do
        local monster = enemyMonsters[i]
        if monster:IsFacedown() then
            return true
        end
    end
    return ctx.Util:IsOneEnemyBetter(true)
end

function should_activate_double_summon(ctx)
    if last_double_summon_turn == ctx.Duel.Turn then
        return false
    end

    local summonables = ctx.Duel.MainPhase.SummonableCards
    if summonables.Count == 0 then
        return false
    end

    -- 统计手牌中的怪兽数量，确保二重召唤有足够怪兽可通召
    local monstersInHand = 0
    local hasLevel5TributeTarget = false
    local hand = ctx.BotField.Hand
    for i = 0, hand.Count - 1 do
        local c = hand[i]
        if c:IsMonster() then
            monstersInHand = monstersInHand + 1
            if c.Level > 4 and c.Level < 6 then
                hasLevel5TributeTarget = true
            end
        end
    end

    if summonables.Count == 1 and summonables[0].Level < 5 and not hasLevel5TributeTarget then
        return false
    end

    if monstersInHand <= 1 then
        return false
    end

    last_double_summon_turn = ctx.Duel.Turn
    return true
end
