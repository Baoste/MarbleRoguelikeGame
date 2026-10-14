-- 大胃袋：喂食规则属于 Lua，库存消耗与回合状态提交由通用引擎 API 执行。
-- Value: initial bonus; Strength: bonus per rarity tier (common=1 ... legendary=4).
local tiers = { common = 1, rare = 2, epic = 3, legendary = 4 }

local function bonus(api)
    return math.max(0, api.Parameter) + api:GetRoundValue("bonus")
end

local function status(api)
    local amount = bonus(api)
    api:SetStatus(string.format("大胃袋 · 本回合成长 +%.3g · 碰撞倍率 ×%.3g", amount, math.min(1000, 1 + amount)))
end

function OnOffer(api)
    local tier = tiers[api.OfferedRarity]
    if tier == nil or api.Strength <= 0 then return end
    api:SetRoundValue("bonus", api:GetRoundValue("bonus") + tier * api.Strength)
    status(api)
    api:Accept()
end

function OnTick(api)
    status(api)
end

function OnContact(api)
    if api.Impact == nil or api.LinkKey ~= 0 then return end
    local amount = bonus(api)
    if amount <= 0 or not api:Visit(0, 0) or not api:TryUse() then return end
    api:AddScoreMultiplier(math.min(1000, 1 + amount))
    status(api)
end
