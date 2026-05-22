---@diagnostic disable: undefined-global, lowercase-global

-- 触发效果示例：展示 condition / target / operation 的典型签名。
-- 示例不会保证真实卡片平衡或完整可用，只用于 API 调用参考。

local s, id = GetID()

---@param c Card
function s.initial_effect(c)
    local e1 = Effect.CreateEffect(c)
    e1:SetDescription(aux.Stringid(id, 0))
    e1:SetCategory(CATEGORY_DRAW)
    e1:SetType(EFFECT_TYPE_SINGLE + EFFECT_TYPE_TRIGGER_O)
    e1:SetCode(EVENT_SUMMON_SUCCESS)
    e1:SetProperty(EFFECT_FLAG_DELAY)
    e1:SetCondition(s.drawcon)
    e1:SetTarget(s.drawtg)
    e1:SetOperation(s.drawop)
    c:RegisterEffect(e1)
end

---@param e Effect
---@param tp Player
---@param eg Group
---@param ep Player
---@param ev integer
---@param re Effect
---@param r Reason
---@param rp Player
---@return boolean
function s.drawcon(e, tp, eg, ep, ev, re, r, rp)
    return Duel.GetTurnPlayer() == tp
end

---@param e Effect
---@param tp Player
---@param eg Group
---@param ep Player
---@param ev integer
---@param re Effect
---@param r Reason
---@param rp Player
---@param chk integer
---@return boolean|nil
function s.drawtg(e, tp, eg, ep, ev, re, r, rp, chk)
    if chk == 0 then
        return Duel.IsPlayerCanDraw(tp, 1)
    end
    Duel.SetOperationInfo(0, CATEGORY_DRAW, nil, 0, tp, 1)
end

---@param e Effect
---@param tp Player
---@param eg Group
---@param ep Player
---@param ev integer
---@param re Effect
---@param r Reason
---@param rp Player
function s.drawop(e, tp, eg, ep, ev, re, r, rp)
    Duel.Draw(tp, 1, REASON_EFFECT)
end
