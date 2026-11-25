-- 全部星星灯点亮后，开启一个全盘临时倍率效果；参与灯共享截止时间，各自完成扣耐久。
-- Value: duration in seconds (maximum 60); Strength: additive bonus per placed lamp.
local visualOn = false

local function paint()
    if self == nil then return end
    self:TrySetRenderersVisible("Lit", visualOn)
    self:TrySetRenderersVisible("Unlit", not visualOn)
end

local function status(api)
    local on = api:GetRoundValue("lit") == 1
    if on ~= visualOn then visualOn = on; paint() end
    local count = api:CountPlacedDevicesWithRoundValue(api.DefinitionId, "lit", 1)
    local total = api:CountPlacedDevicesOfDefinition(api.DefinitionId)
    local remaining = math.max(0, api:GetRoundValue("endsAt") - api.Time)
    api:SetStatus(string.format("星星灯 · %s · 亮灯 %d/%d · 全盘加成 %.1fs", on and "开" or "关", count, total, remaining))
end

local function sync(api, time)
    local endsAt = api:GetRoundValue("endsAt")
    if endsAt <= 0 then return end
    local complete = (time or api.Time) + 0.0000001 >= endsAt
    if complete or api.Board:GetScoreMultiplierEndTime(api:GetRoundValue("effect")) == 0 then
        api:SetRoundValue("lit", 0)
        api:SetRoundValue("endsAt", 0)
        api:SetRoundValue("effect", 0)
        if complete then api:TryUseOnce("window:" .. api:GetRoundValue("cycle")) end
    end
end

-- Each recipient gets a fresh event context. Only plain numeric state crosses callbacks.
function OnLampWindow(api, effect)
    local endsAt = api.Board:GetScoreMultiplierEndTime(effect)
    if endsAt <= api.Time or api:GetRoundValue("endsAt") > 0 then return end
    api:SetRoundValue("lit", 1)
    api:SetRoundValue("effect", effect)
    api:SetRoundValue("endsAt", endsAt)
    api:SetRoundValue("cycle", api:GetRoundValue("cycle") + 1)
    status(api)
end

-- A replacement presentation binds the same script and restores its remembered appearance.
function Start() paint() end
-- OnTick runs before the engine advances Time; finish on the step that reaches the deadline.
function OnTick(api) sync(api, api.Time + api.DeltaTime); status(api) end

function OnContact(api)
    sync(api)
    if api.LinkKey ~= 0 or api.Impact ~= nil or api.RemainingDurability == 0 then return end
    if api:GetRoundValue("endsAt") > 0 then return end -- All lamps stay on; contacts never stack or refresh the aura.
    if not api:Visit(0, 0) then return end
    api:SetRoundValue("lit", api:GetRoundValue("lit") == 1 and 0 or 1)
    api:MarkTriggered()
    local total = api:CountPlacedDevicesOfDefinition(api.DefinitionId)
    local lit = api:CountPlacedDevicesWithRoundValue(api.DefinitionId, "lit", 1)
    local idle = api:CountPlacedDevicesWithRoundValue(api.DefinitionId, "endsAt", 0)
    if total >= 2 and lit == total and idle == total and api.Parameter > 0 then
        local effect = api.Board:AddScoreMultiplier(math.min(1000, 1 + total * math.max(0, api.Strength)), math.min(60, api.Parameter))
        if effect ~= 0 then api:ForEachPlacedDevice(api.DefinitionId, "OnLampWindow", effect) end
    end
    status(api)
end
