---@diagnostic disable: undefined-global, lowercase-global

-- 过滤与选择示例：展示 Card / Group / Duel 的协作方式。
-- `filter` 函数通常作为参数传给 Duel.SelectMatchingCard 或 Group.Filter。

local s, id = GetID()

---@param c Card
function s.initial_effect(c)
    local e1 = Effect.CreateEffect(c)
    e1:SetDescription(aux.Stringid(id, 0))
    e1:SetCategory(CATEGORY_TOHAND)
    e1:SetType(EFFECT_TYPE_IGNITION)
    e1:SetRange(LOCATION_MZONE)
    e1:SetTarget(s.thtg)
    e1:SetOperation(s.thop)
    c:RegisterEffect(e1)
end

---@param c Card
---@return boolean
function s.thfilter(c)
    return c:IsAbleToHand() and c:IsType(TYPE_MONSTER)
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
function s.thtg(e, tp, eg, ep, ev, re, r, rp, chk)
    if chk == 0 then
        return Duel.IsExistingMatchingCard(s.thfilter, tp, LOCATION_GRAVE, 0, 1, nil)
    end
    Duel.SetOperationInfo(0, CATEGORY_TOHAND, nil, 1, tp, LOCATION_GRAVE)
end

---@param e Effect
---@param tp Player
---@param eg Group
---@param ep Player
---@param ev integer
---@param re Effect
---@param r Reason
---@param rp Player
function s.thop(e, tp, eg, ep, ev, re, r, rp)
    local g = Duel.SelectMatchingCard(tp, s.thfilter, tp, LOCATION_GRAVE, 0, 1, 1, nil)
    if g:GetCount() > 0 then
        Duel.SendtoHand(g, nil, REASON_EFFECT)
        Duel.ConfirmCards(1 - tp, g)
    end
end
