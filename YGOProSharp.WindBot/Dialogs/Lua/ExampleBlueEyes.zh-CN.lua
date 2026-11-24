-- =============================================================================
-- ExampleBlueEyes.zh-CN.lua
-- 青眼白龙简体中文对话（当 BotDialog=ExampleBlueEyes 且 BotLocale=zh-CN 时自动命中）
-- =============================================================================

dialog("ExampleBlueEyes")
    includes("ExampleBlueEyes.common.lua")
    set_locale("zh-CN")
    set_description("青眼白龙角色 Lua 对话（简体中文）")
    set_facedown_monster_name("里侧怪兽")

    add_welcome(
        "哼，准备好见证青眼白龙的力量了吗？",
        "作为决斗者，拿出你最强的卡组来吧！"
    )
    add_duelstart({
        "决斗开始！青眼的咆哮即将响彻全场！",
        "让我看看你能撑过几个回合！"
    })
    add_newturn("我的回合，抽卡！", "抽卡！胜利的齿轮已经开始转动！")
    add_endturn("回合结束，轮到你了。", "我的回合结束了，放马过来吧。")
    add_directattack("{0}，直接攻击！", "{0}，直击对手的生命值！")
    add_attack("{0}，粉碎那只{1}！", "{0}，消灭{1}！")
    add_activate("我发动{0}！", "见识一下{0}的效果吧！")
    add_summon("出来吧，{0}！", "降临吧，{0}！")
    add_setmonster("我里侧覆盖一只怪兽。", "有胆量就攻过来试试吧。")
    add_chaining("休想得逞！连锁发动{0}！", "正合我意，发动{0}！")
    add_surrender("哼……这次算你赢了。")
    add_custom("青眼专属台词：{0}")

    on_summon("format_blue_eyes_summon_zh_cn")

    on_directattack(function(ctx, attacker)
        if attacker ~= nil and string.find(attacker, "青眼", 1, true) ~= nil then
            return attacker .. "，直接攻击！毁灭的爆裂疾风弹！！"
        end
        return nil
    end)

    on_newturn(function(ctx)
        if ctx.Turn == 1 then
            return "第一回合，抽卡！让青眼的传说就此开始！"
        end
        return nil
    end)
dialog_end()

---@param ctx LuaDialogContext
---@param monster string
---@return string|nil
function format_blue_eyes_summon_zh_cn(ctx, monster)
    if monster == nil then
        return nil
    end
    if string.find(monster, "青眼白龙", 1, true) ~= nil then
        return "出现吧，最强之龙——" .. monster .. "！"
    end
    if string.find(monster, "青眼", 1, true) ~= nil then
        return "降临吧，继承青眼之力的" .. monster .. "！"
    end
    return nil
end
