-- 黑洞：充能、激活时机、持续时间和衰减公式均由 Lua 决定。
-- Only plain Lua data survives callbacks. OnTick runs at the start of each simulation step.
local chargeRequired = 10
local roundId, charge, endsAt = nil, 0, 0
local active = false

local function sync(api)
    if roundId ~= api.RoundId then
        roundId, charge, endsAt, active = api.RoundId, 0, 0, false
    end
    if active and api.Time >= endsAt then
        charge, endsAt, active = 0, 0, false
    end
end

local function status(api)
    if active then
        api:SetStatus(string.format("黑洞引力 %.1fs", math.max(0, endsAt - api.Time)))
    elseif charge >= chargeRequired then
        api:SetStatus("黑洞已充能 · 点击释放")
    else
        api:SetStatus(string.format("黑洞充能 %d/%d", charge, chargeRequired))
    end
end

function OnTick(api)
    sync(api)
    status(api)
end

function OnActivate(api)
    sync(api)
    if not active and charge >= chargeRequired and api.Parameter > 0 then
        active, endsAt = true, api.Time + api.Parameter
    end
    status(api)
end

function OnContact(api)
    sync(api)
    if active then
        api:TrySettle(api.Value * api.ScoreMultiplier)
    elseif charge < chargeRequired then
        api:Consume(false)
        charge = charge + 1
    end
    status(api)
end

function OnField(api)
    if not active then return end
    -- Range is the HALF-STRENGTH distance, not an outer cutoff. All ordinary balls are affected.
    local ratio = api.Distance / api.Range
    api:AddRadialForce(api.Strength / (1 + ratio * ratio))
end
