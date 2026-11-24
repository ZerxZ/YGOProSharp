-- =============================================================================
-- ExampleBlueEyes.lua
-- 青眼白龙英文默认对话（按语言拆分维护：中文见 ExampleBlueEyes.zh-CN.lua / ExampleBlueEyes.zh-TW.lua）
-- =============================================================================

dialog("ExampleBlueEyes")
    includes("ExampleBlueEyes.common.lua")
    set_locale("en-US")
    set_description("Blue-Eyes character Lua dialog (English default)")
    set_facedown_monster_name("monster")

    add_welcome(
        "Hmph. Ready to witness the power of Blue-Eyes White Dragon?",
        "Show me your strongest deck, duelist!"
    )
    add_duelstart({
        "Let the duel begin! Hear the roar of Blue-Eyes!",
        "Let's see how many turns you can survive!"
    })
    add_newturn("My turn! I draw!", "Draw! Victory draws closer!")
    add_endturn("I end my turn.", "My turn is over. Make your move.")
    add_directattack("{0}, direct attack!", "{0}, strike their life points directly!")
    add_attack("{0}, crush their {1}!", "{0}, destroy {1}!")
    add_activate("I activate {0}!", "Witness the power of {0}!")
    add_summon("Come forth, {0}!", "Descend, {0}!")
    add_setmonster("I set a monster face-down.", "Attack it if you dare.")
    add_chaining("Not so fast! I chain {0}!", "Just as planned! I activate {0}!")
    add_surrender("Hmph... You win this time.")
    add_custom("Blue-Eyes special line: {0}")

    on_summon("format_blue_eyes_summon_en")

    on_directattack(function(ctx, attacker)
        if attacker ~= nil and string.find(attacker, "Blue-Eyes", 1, true) ~= nil then
            return attacker .. ", direct attack! Burst Stream of Destruction!!"
        end
        return nil
    end)

    on_newturn(function(ctx)
        if ctx.Turn == 1 then
            return "Turn 1, draw! Let the legend of Blue-Eyes begin!"
        end
        return nil
    end)
dialog_end()

---@param ctx LuaDialogContext
---@param monster string
---@return string|nil
function format_blue_eyes_summon_en(ctx, monster)
    if monster == nil then
        return nil
    end
    if string.find(monster, "Blue-Eyes White Dragon", 1, true) ~= nil then
        return "Come forth, the ultimate engine of destruction — " .. monster .. "!"
    end
    if string.find(monster, "Blue-Eyes", 1, true) ~= nil then
        return "Descend with the pride of Blue-Eyes, " .. monster .. "!"
    end
    return nil
end
