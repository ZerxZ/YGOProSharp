---@diagnostic disable: undefined-global, lowercase-global

-- 最小卡片脚本模板。
-- 这个文件只用于说明 Lua API 调用方式，不会被 YGOProSharp runtime 自动加载。

local s, id = GetID()

---@param c Card
function s.initial_effect(c)
    local e1 = Effect.CreateEffect(c)
    e1:SetDescription(aux.Stringid(id, 0))
    e1:SetType(EFFECT_TYPE_SINGLE)
    e1:SetCode(EFFECT_UPDATE_ATTACK)
    e1:SetProperty(EFFECT_FLAG_SINGLE_RANGE)
    e1:SetRange(LOCATION_MZONE)
    e1:SetValue(500)
    c:RegisterEffect(e1)
end
