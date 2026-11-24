-- =============================================================================
-- ExampleBlueEyes.zh-TW.lua
-- 青眼白龍繁體中文對話（當 BotDialog=ExampleBlueEyes 且 BotLocale=zh-TW 時自動命中）
-- =============================================================================

dialog("ExampleBlueEyes")
    includes("ExampleBlueEyes.common.lua")
    set_locale("zh-TW")
    set_description("青眼白龍角色 Lua 對話（繁體中文）")
    set_facedown_monster_name("裡側怪獸")

    add_welcome(
        "哼，準備好見證青眼白龍的力量了嗎？",
        "作為決鬥者，拿出你最強的牌組來吧！"
    )
    add_duelstart({
        "決鬥開始！青眼的咆哮即將響徹全場！",
        "讓我看看你能撐過幾個回合！"
    })
    add_newturn("我的回合，抽牌！", "抽牌！勝利正在逼近！")
    add_endturn("回合結束，輪到你了。", "我的回合結束了。")
    add_directattack("{0}，直接攻擊！", "{0}，直擊對手的生命值！")
    add_attack("{0}，粉碎那隻{1}！", "{0}，消滅{1}！")
    add_activate("我發動{0}！", "見識一下{0}的效果吧！")
    add_summon("出來吧，{0}！", "降臨吧，{0}！")
    add_setmonster("我裡側覆蓋一隻怪獸。", "有膽量就攻過來試試吧。")
    add_chaining("休想得逞！連鎖發動{0}！", "正合我意，發動{0}！")
    add_surrender("哼……這次算你贏了。")
    add_custom("青眼專屬台詞：{0}")

    on_summon("format_blue_eyes_summon_zh_tw")

    on_directattack(function(ctx, attacker)
        if attacker ~= nil and string.find(attacker, "青眼", 1, true) ~= nil then
            return attacker .. "，直接攻擊！毀滅的爆裂疾風彈！！"
        end
        return nil
    end)

    on_newturn(function(ctx)
        if ctx.Turn == 1 then
            return "第一回合，抽牌！讓青眼的傳說就此開始！"
        end
        return nil
    end)
dialog_end()

---@param ctx LuaDialogContext
---@param monster string
---@return string|nil
function format_blue_eyes_summon_zh_tw(ctx, monster)
    if monster == nil then
        return nil
    end
    if string.find(monster, "青眼白龍", 1, true) ~= nil then
        return "出現吧，最強之龍——" .. monster .. "！"
    end
    if string.find(monster, "青眼", 1, true) ~= nil then
        return "降臨吧，繼承青眼之力的" .. monster .. "！"
    end
    return nil
end
