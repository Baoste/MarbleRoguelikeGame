-- more is more：数量查询由引擎提供；排除自身、每件增量和上限由 Lua 决定。
-- Value: maximum bonus per impact; Strength: bonus per other placed device.
local function bonus(api)
    local count = api:CountPlacedDevices(api.InstanceId)
    if api.Parameter <= 0 or api.Strength <= 0 then return 0, count end
    return math.min(api.Parameter, count * api.Strength), count
end

local function status(api)
    local amount, count = bonus(api)
    api:SetStatus(string.format("more is more · 其他装置 %d · 附加倍率 +%.3g", count, amount))
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
