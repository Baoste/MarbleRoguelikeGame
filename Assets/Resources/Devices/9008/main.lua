-- 沙包：每个库存实例独立连击；只跨回调保存普通数字。
-- Value: base bonus; Range: initial timeout; Strength: minimum timeout.
local roundId, combo, endsAt = nil, 0, 0

local function sync(api)
    if roundId ~= api.RoundId then
        roundId, combo, endsAt = api.RoundId, 0, 0
    end
    if combo > 0 and api.Time + 0.0000001 >= endsAt then
        combo, endsAt = 0, 0
    end
end

local function status(api)
    if combo == 0 then
        api:SetStatus("沙包 · 连击 0")
    else
        api:SetStatus(string.format("沙包 · 连击 %d · 剩余 %.1fs", combo, math.max(0, endsAt - api.Time)))
    end
end

function OnTick(api)
    sync(api)
    status(api)
end

function OnContact(api)
    sync(api)
    -- Count real collision entries, not trigger/link contacts or continuous resting contact.
    if api.Impact == nil or api.LinkKey ~= 0 then return end
    if api.Parameter <= 0 or api.Range <= 0 or api.Strength <= 0 then return end
    -- No shared cooldown: different balls hitting in one step each count once.
    if not api:TryUse() then return end
    combo = combo + 1
    endsAt = api.Time + math.max(api.Strength, api.Range / math.sqrt(combo))
    api:AddBase("BALL_BASE_VALUE", combo * api.Parameter)
    status(api)
end
