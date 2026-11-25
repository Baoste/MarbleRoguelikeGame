-- 大炮：容量、八向旋转和发射队列均在 Lua。只跨回调保存普通值。
local roundId, firing = nil, false
local fireDirection, nextDistance = nil, 0
local batchNumber, useKey = 0, nil

local function sync(api)
    if roundId ~= api.RoundId then
        roundId, firing, fireDirection, nextDistance = api.RoundId, false, nil, 0
        batchNumber, useKey = 0, nil
    end
    if not firing and api.HeldCount == 0 then useKey = nil end
end

local function status(api)
    api:SetStatus(string.format("大炮 %d/%d · 点击发射", api.HeldCount, math.floor(api.Parameter)))
end

function OnTick(api)
    sync(api)
    local interval = math.max(0.01, api.Range)
    api:TrySetYaw((math.floor((api.Time + 0.0000001) / interval) % 8) * 45)
    status(api)
end

function OnContact(api)
    sync(api)
    if useKey ~= nil or api.RemainingDurability == 0 or api.HeldCount >= math.floor(api.Parameter) then return end
    -- Per-ball admission: multiple arrivals in one step do not share a device-wide cooldown.
    if not api:Visit(0, 1) then return end
    local motion = api.Motion
    local source = motion.Source
    local center = source.Position + source.Up * (api.Radius + 0.005)
    if motion:TryMove(center, motion.Velocity) then motion:TryHoldUntilReleased() end
    status(api)
end

function OnActivate(api)
    sync(api)
    if api.HeldCount == 0 then status(api); return end
    if useKey == nil then batchNumber = batchNumber + 1; useKey = "volley:" .. batchNumber end
    if not api:CanUseOnce(useKey) then return end
    -- Snapshot the currently displayed heading, not the next rotation step.
    firing, fireDirection, nextDistance = true, api.Forward, 0
    api:ReleaseHeld()
    firing, fireDirection = false, nil
    status(api)
end

function OnHoldEnd(api)
    sync(api)
    if not firing then return end
    local motion = api.Motion
    if not api:CanUseOnce(useKey) then return end
    local source = motion.Source
    local distance = math.max(source.Radius + api.Radius + 0.02, nextDistance + api.Radius)
    local position = source.Position + source.Up * (api.Radius + 0.005) + fireDirection * distance
    if motion:TryMove(position, fireDirection * api.Strength) then
        api:TryUseOnce(useKey)
        nextDistance = distance + api.Radius + 0.02
    else
        -- An obstructed shot remains stored for another click, without charging another use.
        motion:TryHoldUntilReleased()
    end
end
