-- 弹珠机关枪：计时、扫射、单发价值和整批耐久均由 Lua 组合。
-- Value: shot interval; Strength: speed; Range: total fan angle.
local radius, mass, period = 0.04, 0.1, 1
local round, active, batch, started = -1, nil, 0, 0
local heading, shotDirection = nil, nil

local function sync(api)
    if round ~= api.RoundId then round, active, batch, started = api.RoundId, nil, 0, 0 end
end

local function angle(api)
    local phase = ((api.Time - started) / period) % 1
    local triangle = phase <= 0.5 and phase * 4 - 1 or 3 - phase * 4
    return triangle * api.Range * 0.5
end

local function status(api)
    api:SetStatus(active == nil and "弹珠机关枪 · 等待弹珠" or "弹珠机关枪 · 连续扫射")
end

function OnTick(api)
    sync(api)
    if active ~= nil and api.HeldCount == 0 then active = nil end
    -- Reset to the placed basis before computing yaw so view rebuilds do not accumulate rotation.
    api:TrySetYaw(0)
    local down = api.Down
    heading = down
    shotDirection = api.Vectors:Rotate(down, api.Up, angle(api))
    api:TrySetYaw(api.Vectors:SignedAngle(api.Forward, shotDirection, api.Up))
    status(api)
end

function OnContact(api)
    sync(api)
    if api.LinkKey ~= 0 or api.Impact ~= nil or api.RemainingDurability == 0 or api.HeldCount > 0 then return end
    if api:HasSplitFrom(api.InstanceId) or api.Value < 1 or api.Parameter <= 0 or api.Parameter > 60 or api.Strength <= 0 then return end
    if api.Range < 1 or api.Range > 90 then return end
    local part = api:CreatePortion(1)
    if not part:TrySetSize(radius, mass) or not part.CanSplit or not api:Visit(0, 0) then return end
    if api.Motion:TryHold(api.Parameter) then
        batch, active, started = batch + 1, api.BallId, api.Time
        heading = heading or api.Motion.Source.Down
        api.Motion:TrySetHeldVisible(false)
        status(api)
    end
end

function OnHoldEnd(api)
    sync(api)
    if active ~= api.BallId then return end
    local useKey = "burst:" .. batch
    if not api:CanUseOnce(useKey) then active = nil; return end
    local source = api.Motion.Source
    local direction = shotDirection or source:Rotate(heading or source.Down, angle(api))
    local part = api:CreatePortion(1)
    -- A large input sphere is briefly restored for this callback; keep the new ball clear of it too.
    local distance = math.max(source.Radius + radius + 0.02,
        api.Vectors:Dot(api.Motion.Position - source.Position, direction) + api.Radius + radius + 0.02)
    local position = source.Position + source.Up * (radius + 0.005) + direction * distance
    local fired = part:TrySetSize(radius, mass) and part:TrySpawn(position, direction * api.Strength)
    if fired and api.Value < 1 then
        if api.Value > 0 then api:TryConsume(false) end -- Discard the remainder only after the final successful shot.
        api:TryUseOnce(useKey)
        active = nil
        status(api)
        return
    end
    -- Failed output keeps the full remainder, retries at the normal cadence, and spends no durability.
    if api.Motion:TryHold(api.Parameter) then api.Motion:TrySetHeldVisible(false)
    else active = nil end
end
