-- =============================================================================
-- ExampleBlueEyes.lua
-- YGOProSharp.WindBot 泛用 Lua 卡组执行器示例：青眼白龙（Blue-Eyes）
--
-- 运行示例（CLI 启动服务端并默认内置该 Lua 机器人）：
--   dotnet run --project YGOProSharp.Cli/YGOProSharp.Cli.csproj -c Release -- `
--       server RootPath=YGOProSharp.Cli/bin/Release/net10.0 ScriptDirectory=script `
--       DatabaseFile=cards.cdb BanlistFile=lflist.conf Port=7911 ClientVersion=0x1362 `
--       MasterRule=5 NoCheckDeck=true AddBot=true BotName=BlueEyesLua BotDeck=ExampleBlueEyes
-- =============================================================================

local CardId = {
    -- 主卡组怪兽
    WhiteDragon = 89631139,                  -- 青眼白龙
    AlternativeWhiteDragon = 38517737,       -- 青眼亚白龙
    DragonSpiritOfWhite = 45467446,          -- 白灵龙
    WhiteStoneOfAncients = 71039903,         -- 太古的白石
    WhiteStoneOfLegend = 79814787,           -- 传说的白石
    SageWithEyesOfBlue = 8240199,            -- 青色眼睛的贤士
    EffectVeiler = 97268402,                 -- 效果遮蒙者

    -- 魔法卡
    GalaxyCyclone = 5133471,                 -- 银河旋风
    HarpiesFeatherDuster = 18144506,         -- 羽毛扫
    ReturnOfTheDragonLords = 6853254,        -- 复活之福音
    PotOfDesires = 35261759,                 -- 强欲而贪欲之壶
    TradeIn = 38120068,                      -- 抵价购物
    CardsOfConsonance = 39701395,            -- 调和的宝札
    DragonShrine = 41620959,                 -- 龙之灵庙
    MelodyOfAwakeningDragon = 48800175,      -- 龙觉醒旋律
    SoulCharge = 54447022,                   -- 灵魂补充
    MonsterReborn = 83764718,                -- 死者苏生
    SilversCry = 87025064,                   -- 银龙的轰咆

    -- 额外卡组
    Giganticastle = 63422098,                -- 幻层守护者 阿尔玛得斯 / 巨岩掌
    AzureEyesSilverDragon = 40908371,        -- 苍眼银龙
    BlueEyesSpiritDragon = 59822133,         -- 青眼精灵龙
    GalaxyEyesDarkMatterDragon = 58820923,   -- 银河眼暗物质龙
    GalaxyEyesCipherBladeDragon = 2530830,   -- 银河眼光波刃龙
    GalaxyEyesFullArmorPhotonDragon = 39030163, -- 银河眼重铠光子龙
    GalaxyEyesPrimePhotonDragon = 31801517,  -- 银河眼光子龙皇
    GalaxyEyesCipherDragon = 18963306,       -- 银河眼光波龙
    HopeHarbingerDragonTitanicGalaxy = 63767246, -- No.38 希望魁龙 银河巨神
    SylvanPrincessprite = 33909817,          -- 森罗的姬芽宫
}

local announced_first_turn = false

deck("ExampleBlueEyes")
    set_deckfile("AI_BlueEyes")
    set_level("Normal")
    set_description("Lua 版青眼白龙卡组（Blue-Eyes）")
    set_author("YGOProSharp")

    -- 1. 清场与后场破坏
    add_default("Activate", CardId.HarpiesFeatherDuster, "DefaultHarpiesFeatherDusterFirst")
    add_default("Activate", CardId.GalaxyCyclone, "DefaultGalaxyCyclone")
    on_activate(CardId.HarpiesFeatherDuster)

    -- 2. 检索与滤抽魔法
    on_activate(CardId.DragonShrine, "be_dragon_shrine")
    on_summon(CardId.SageWithEyesOfBlue, "be_sage_summon")
    on_activate(CardId.MelodyOfAwakeningDragon, "be_melody")
    on_activate(CardId.CardsOfConsonance, "be_cards_of_consonance")
    on_activate(CardId.TradeIn, "be_trade_in")
    add_default("Activate", CardId.PotOfDesires, "DefaultPotOfDesires")

    -- 3. 特殊召唤青眼亚白龙
    on_spsummon(CardId.AlternativeWhiteDragon, "be_alternative_spsummon")

    -- 4. 苏生魔法
    on_activate(CardId.ReturnOfTheDragonLords, "be_reborn")
    on_activate(CardId.SilversCry, "be_reborn")
    on_activate(CardId.MonsterReborn, "be_reborn")

    -- 5. 怪兽效果
    on_activate(CardId.AlternativeWhiteDragon, "be_alternative_effect")
    on_activate(CardId.SageWithEyesOfBlue)
    on_activate(CardId.WhiteStoneOfAncients, "be_ancient_stone_effect")
    on_activate(CardId.DragonSpiritOfWhite, "be_spirit_of_white_effect")
    on_activate(CardId.BlueEyesSpiritDragon, "be_spirit_dragon_effect")
    on_activate(CardId.AzureEyesSilverDragon, "be_reborn")
    on_activate(CardId.HopeHarbingerDragonTitanicGalaxy)
    on_activate(CardId.GalaxyEyesCipherDragon)
    on_activate(CardId.GalaxyEyesFullArmorPhotonDragon)
    on_activate(CardId.GalaxyEyesCipherBladeDragon)
    on_activate(CardId.GalaxyEyesDarkMatterDragon)
    on_activate(CardId.GalaxyEyesPrimePhotonDragon)
    on_activate(CardId.SylvanPrincessprite)

    -- 6. 通常召唤白石 / 贤士用于同调或检索
    on_summon(CardId.WhiteStoneOfAncients, "be_stone_summon")
    on_summon(CardId.WhiteStoneOfLegend, "be_stone_summon")

    -- 7. 额外卡组展开
    on_spsummon(CardId.BlueEyesSpiritDragon)
    on_spsummon(CardId.HopeHarbingerDragonTitanicGalaxy)
    on_spsummon(CardId.GalaxyEyesCipherDragon)
    on_spsummon(CardId.GalaxyEyesFullArmorPhotonDragon)
    on_spsummon(CardId.GalaxyEyesCipherBladeDragon)
    on_spsummon(CardId.GalaxyEyesDarkMatterDragon)
    on_spsummon(CardId.GalaxyEyesPrimePhotonDragon)
    on_spsummon(CardId.Giganticastle)
    on_spsummon(CardId.SylvanPrincessprite)

    -- 8. 兜底铺场与表示形式
    on_activate(CardId.SoulCharge, "be_soul_charge")
    add_default("Repos", "DefaultMonsterRepos")
    on_monster_set(CardId.WhiteStoneOfLegend)
    on_monster_set(CardId.WhiteStoneOfAncients)
    add_default("SpellSet", "DefaultSpellSet")

    -- 9. 生命周期钩子（对局内聊天提示，方便在 MDPro3 直接确认 Lua 脚本生效）
    on_init(function(ctx)
        announced_first_turn = false
        ctx:LogInfo("ExampleBlueEyes Lua deck initialized (AI_BlueEyes.ydk).")
    end)

    on_new_turn(function(ctx)
        if not announced_first_turn then
            announced_first_turn = true
            ctx.AI.Game:Chat("[Lua:ExampleBlueEyes] 青眼白龙 Lua AI 已启动！")
        end
        ctx:LogDebug("ExampleBlueEyes turn " .. tostring(ctx.Duel.Turn))
    end)

    on_select_card(function(ctx, cards, min, max, hint, cancelable)
        -- 龙觉醒旋律从卡组检索时，优先拿 1 青眼白龙 + 青眼亚白龙
        if max == 2 and cards ~= nil and cards.Count > 0 and cards[0].Location == CardLocation.Deck then
            local picked = {}
            local needWhite = not ctx.BotField:HasInHand(CardId.WhiteDragon)
            for i = 0, cards.Count - 1 do
                local c = cards[i]
                if needWhite and c:IsCode(CardId.WhiteDragon) then
                    table.insert(picked, c)
                    needWhite = false
                elseif c:IsCode(CardId.AlternativeWhiteDragon) and #picked < max then
                    table.insert(picked, c)
                end
            end
            if #picked >= min then
                return picked
            end
        end
        return nil
    end)

    on_dispose(function(ctx)
        ctx:LogDebug("ExampleBlueEyes Lua deck disposed.")
    end)
deck_end()

-- =============================================================================
-- 青眼白龙规则实现
-- =============================================================================

function be_dragon_shrine(ctx)
    ctx.AI:SelectCard(
        CardId.DragonSpiritOfWhite,
        CardId.WhiteDragon,
        CardId.WhiteStoneOfAncients,
        CardId.WhiteStoneOfLegend
    )
    if not ctx.BotField:HasInHand(CardId.WhiteDragon) then
        ctx.AI:SelectNextCard(CardId.WhiteStoneOfLegend)
    else
        ctx.AI:SelectNextCard(
            CardId.WhiteStoneOfAncients,
            CardId.DragonSpiritOfWhite,
            CardId.WhiteStoneOfLegend
        )
    end
    return true
end

function be_sage_summon(ctx)
    ctx.AI:SelectCard(
        CardId.WhiteStoneOfAncients,
        CardId.WhiteStoneOfLegend,
        CardId.EffectVeiler
    )
    return true
end

function be_melody(ctx)
    ctx.AI:SelectCard(
        CardId.WhiteStoneOfAncients,
        CardId.DragonSpiritOfWhite,
        CardId.WhiteStoneOfLegend,
        CardId.GalaxyCyclone,
        CardId.EffectVeiler,
        CardId.TradeIn,
        CardId.SageWithEyesOfBlue
    )
    return true
end

function be_cards_of_consonance(ctx)
    if not ctx.BotField:HasInHand(CardId.WhiteDragon) or ctx.BotField:HasInHand(CardId.TradeIn) then
        ctx.AI:SelectCard(CardId.WhiteStoneOfLegend)
    else
        ctx.AI:SelectCard(CardId.WhiteStoneOfAncients)
    end
    return true
end

local function count_code(cards, code)
    if cards == nil then
        return 0
    end
    local count = 0
    for i = 0, cards.Count - 1 do
        if cards[i]:IsCode(code) then
            count = count + 1
        end
    end
    return count
end

function be_trade_in(ctx)
    if ctx.BotField:HasInHand(CardId.DragonSpiritOfWhite) then
        ctx.AI:SelectCard(CardId.DragonSpiritOfWhite)
        return true
    end
    if count_code(ctx.BotField.Hand, CardId.WhiteDragon) >= 2 then
        ctx.AI:SelectCard(CardId.WhiteDragon)
        return true
    end
    if count_code(ctx.BotField.Hand, CardId.AlternativeWhiteDragon) >= 2 then
        ctx.AI:SelectCard(CardId.AlternativeWhiteDragon)
        return true
    end
    return false
end

function be_alternative_spsummon(ctx)
    if ctx.BotField:HasInHand(CardId.WhiteDragon) then
        ctx.AI.Game:Chat("[Lua:ExampleBlueEyes] 展示手牌青眼白龙，特殊召唤青眼亚白龙！")
        return true
    end
    return false
end

function be_alternative_effect(ctx)
    local target = ctx.Util:GetProblematicEnemyMonster(ctx.CurrentCard:GetDefensePower())
    if target == nil then
        target = ctx.Util:GetBestEnemyMonster(false, true)
    end
    if target ~= nil then
        ctx.AI:SelectCard(target)
        return true
    end
    return false
end

function be_reborn(ctx)
    ctx.AI:SelectCard(
        CardId.WhiteDragon,
        CardId.DragonSpiritOfWhite,
        CardId.BlueEyesSpiritDragon,
        CardId.AzureEyesSilverDragon,
        CardId.AlternativeWhiteDragon
    )
    return true
end

function be_ancient_stone_effect(ctx)
    ctx.AI:SelectCard(
        CardId.DragonSpiritOfWhite,
        CardId.WhiteDragon
    )
    return true
end

function be_spirit_of_white_effect(ctx)
    if ctx.CurrentCard.Location == CardLocation.MonsterZone then
        local spellTarget = ctx.Util:GetBestEnemySpell(false)
        if spellTarget ~= nil then
            ctx.AI:SelectCard(spellTarget)
            return true
        end
        if ctx.BotField:HasInHand(CardId.WhiteDragon) and ctx.Duel.Player == 1 then
            return true
        end
    end
    return false
end

function be_spirit_dragon_effect(ctx)
    if ctx.Duel.Player == 1 and ctx.Util:IsChainTarget(ctx.CurrentCard) then
        ctx.AI:SelectCard(CardId.AzureEyesSilverDragon)
        return true
    end
    return false
end

function be_stone_summon(ctx)
    -- 场上有青眼/8星怪兽时通召 1 星调整准备同调 9 星精灵龙
    if ctx.BotField:HasInMonstersZone(CardId.WhiteDragon)
        or ctx.BotField:HasInMonstersZone(CardId.AlternativeWhiteDragon)
        or ctx.BotField:HasInMonstersZone(CardId.DragonSpiritOfWhite) then
        return true
    end
    return false
end

function be_soul_charge(ctx)
    if ctx.BotField.LifePoints > 3000 and count_code(ctx.BotField.Graveyard, CardId.WhiteDragon) >= 2 then
        return true
    end
    return false
end
