-- 淘金热：容量、等待、随机结果和耐久均由 Lua 组合；引擎只负责通用能力。
-- Value: trip seconds; Strength: coins with returned ball; Range: coins without ball.
local returnWeight, coinsWeight, lossWeight = 1, 1, 1
local roundId, activeBall, endsAt, trip = nil, nil, 0, 0
local lastResult = "等待弹珠"

local function syncRound(api)
    if roundId ~= api.RoundId then
        roundId, activeBall, endsAt, trip = api.RoundId, nil, 0, 0
        lastResult = "等待弹珠"
    end
end

local function checkCancelled(api)
    if activeBall ~= nil and api.HeldCount == 0 then
        activeBall, endsAt, lastResult = nil, 0, "运输取消 · 等待弹珠"
    end
end

local function status(api)
    if activeBall ~= nil then
        api:SetStatus(string.format("淘金热 · 运输中 %.1fs", math.max(0, endsAt - api.Time)))
    else
        api:SetStatus("淘金热 · " .. lastResult)
    end
end

function OnTick(api)
    syncRound(api)
    checkCancelled(api)
    status(api)
end

function OnContact(api)
    syncRound(api)
    checkCancelled(api)
    if activeBall ~= nil or api.HeldCount > 0 or api.LinkKey ~= 0 or api.RemainingDurability == 0 then return end
    if api.Parameter <= 0 or api.Parameter > 60 or api.Strength < 0 or api.Range < 0 then return end
    if not api:Visit(0, 0) then return end
    if api.Motion:TryHold(api.Parameter) then
        api.Motion:TrySetHeldVisible(false)
        trip, activeBall, endsAt = trip + 1, api.BallId, api.Time + api.Parameter
        status(api)
    end
end

function OnHoldEnd(api)
    syncRound(api)
    if activeBall ~= api.BallId then return end
    local useKey = "trip:" .. trip
    activeBall, endsAt = nil, 0
    lastResult = "运输取消 · 等待弹珠"
    if not api:CanUseOnce(useKey) then status(api); return end
    local result = api:RandomRange(0, returnWeight + coinsWeight + lossWeight)
    if result < returnWeight then
        local source, speed = api.Motion.Source, api.Hold.EntrySpeed
        local released = api.Motion:TryLaunch(source, source.Down, speed)
        if not released then released = api.Motion:TrySetVelocity(source.Down * speed) end
        if released then
            api:TryUseOnce(useKey)
            api:AddCoins(math.floor(api.Strength))
            lastResult = string.format("弹珠返回 · +%d 金币", math.floor(api.Strength))
        end
    elseif api:TryConsume(false) then
        api:TryUseOnce(useKey)
        if result < returnWeight + coinsWeight then
            api:AddCoins(math.floor(api.Range))
            lastResult = string.format("矿车返回 · +%d 金币", math.floor(api.Range))
        else
            lastResult = "一无所获"
        end
    end
    status(api)
end
