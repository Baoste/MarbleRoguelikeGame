-- Each accepted ball receives a permanent Rush contribution; hold and launch stay independent.
local cooldownSeconds = 5

local function status(api)
    if api.HeldCount > 0 then
        api:SetStatus(string.format("有锅气 · 翻炒 %d 颗 · 每球贡献 +%.0f", api.HeldCount, api.Strength))
    elseif api:GetRoundValue("readyAt") > api.Time then
        api:SetStatus(string.format("有锅气 · 冷却 %.1fs", api:GetRoundValue("readyAt") - api.Time))
    else
        api:SetStatus("有锅气 · 等待入锅")
    end
end
local function finishIfEmpty(api)
    if api.HeldCount == 0 and api:GetRoundValue("cooking") ~= 0 then
        api:SetRoundValue("cooking", 0)
        api:SetRoundValue("readyAt", api.Time + cooldownSeconds)
    end
end
function OnTick(api)
    finishIfEmpty(api)
    status(api)
end
function OnContact(api)
    if api.LinkKey ~= 0 or api:GetRoundValue("readyAt") > api.Time + 0.0000001 then return end
    if api.RemainingDurability >= 0 and api.HeldCount >= api.RemainingDurability then return end
    if not api:Visit(0, 0) then return end
    if api.Motion:TryHold(api.Parameter) then
        api:SetRoundValue("cooking", 1)
        api:AddRushContribution(math.max(0, api.Strength))
        status(api)
    end
end
function OnHoldEnd(api)
    local serial = api:GetRoundValue("releases") + 1
    local key = "release:" .. serial
    if api:CanUseOnce(key) then
        local motion, source = api.Motion, api.Motion.Source
        local angle = api:RandomRange(-math.min(89, math.max(0, api.Range)), math.min(89, math.max(0, api.Range)))
        local direction = api.Vectors:Rotate(-source.Down, source.Up, angle)
        local speed = api.Hold.EntrySpeed
        local launched = motion:TryLaunch(source, direction, speed)
        if not launched then launched = motion:TrySetVelocity(direction * speed) end
        if launched and api:TryUseOnce(key) then api:SetRoundValue("releases", serial) end
    end
    finishIfEmpty(api)
    status(api)
end
