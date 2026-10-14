-- less is more: Lua owns the inverse count formula; the engine supplies a live query.
-- Value: maximum bonus per impact; Strength: reduction per other placed device.
local function bonus(api)
    local count = api:CountPlacedDevices(api.InstanceId)
    local maximum = math.max(0, api.Parameter)
    return math.max(0, maximum - count * math.max(0, api.Strength)), count
end

local function status(api)
    local amount, count = bonus(api)
    api:SetStatus(string.format("less is more · 其他装置 %d · 附加倍率 +%.3g", count, amount))
end

function OnTick(api)
    status(api)
end

function OnContact(api)
    if api.Impact == nil or api.LinkKey ~= 0 then return end
    local amount = bonus(api)
    if amount <= 0 or not api:Visit(0, 0) or not api:TryUse() then return end
    api:AddScoreMultiplier(1 + amount)
    status(api)
end
