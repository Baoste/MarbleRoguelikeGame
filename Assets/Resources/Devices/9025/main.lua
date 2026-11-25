-- 雪糕机：每个实例独立记录本回合有效触发次数，仅保存普通数值。
-- Value: initial full multiplier; Strength: reduction after each successful hit.
local roundId, uses = nil, 0

local function sync(api)
    if roundId ~= api.RoundId then
        roundId, uses = api.RoundId, 0
    end
end

local function multiplier(api)
    return math.max(1, api.Parameter - uses * api.Strength)
end

local function status(api)
    local amount = multiplier(api)
    api:SetStatus(string.format("雪糕机 · 已触发 %d 次 · 下次 ×%.3g%s", uses, amount,
        amount <= 1 and " · 本回合已用完" or ""))
end

function OnTick(api)
    sync(api)
    status(api)
end

function OnContact(api)
    sync(api)
    if api.Impact == nil or api.LinkKey ~= 0 then return end
    local amount = multiplier(api)
    if amount <= 1 or not api:Visit(0, 0) or not api:TryUse() then return end
    api:AddScoreMultiplier(amount)
    uses = uses + 1
    status(api)
end
