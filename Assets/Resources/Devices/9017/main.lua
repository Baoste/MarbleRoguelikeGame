-- Value: interval; Strength: throw speed; Range: blast radius.
-- Flight, blast layout and wear rules belong to this script, not the engine.
local flightSeconds, scatterSpeed, pieces = 1, 6, 8
local roundId, nextThrow = nil, 0
local flights = {}
local blast = nil

function OnTick(api)
    if roundId ~= api.RoundId then
        roundId, nextThrow, flights, blast = api.RoundId, api.Time + api.Parameter, {}, nil
    end
    if api.RemainingDurability == 0 or api.Parameter < 0.1 or api.Parameter > 60 then return end
    if api.Time + 0.0000001 < nextThrow then return end
    nextThrow = api.Time + api.Parameter
    local direction = api.Vectors:Rotate(api.Forward, api.Up, api:RandomRange(0, 360))
    local position = api.Position + api.Up * 0.12 + direction * (api.Radius + 0.12)
    local id = api:TrySpawnVisual("BombTemplate", position, direction * api.Strength, flightSeconds, "OnFuseEnd")
    if id ~= 0 then
        flights[id] = { up = api.Up, forward = api.Forward }
        api:SetStatus("炸弹 · 投掷中")
    end
end

function OnFuseEnd(api)
    local info = api.Transient
    if info == nil or roundId ~= api.RoundId then return end
    local flight = flights[info.Id]
    flights[info.Id] = nil
    if flight == nil or api.RemainingDurability == 0 then return end
    -- Keep the final explosion active for the entire synchronous query, including its visual flash.
    if not api:TryKeepActive(0.2) or not api:TryUse() then return end
    api:TrySpawnVisual("BlastTemplate", info.Position, info.Velocity * 0, 0.18, "")
    blast = { center = info.Position, up = flight.up, forward = flight.forward, affected = 0 }
    api:ForEachBallInRadius(info.Position, api.Range, "OnBlastBall")
    api:SetStatus(string.format("炸弹 · 炸散%d颗", blast.affected))
    blast = nil
end

function OnBlastBall(api)
    if blast == nil or api:HasSplitFrom(api.InstanceId) then return end
    local batch = api:CreateSplitBatch(pieces)
    if not batch.CanSplit then return end
    local position = api.Motion.Position
    local away = api.Vectors:ProjectOnPlane(position - blast.center, blast.up)
    if api.Vectors:Dot(away, away) < 0.0000001 then away = blast.forward end
    away = api.Vectors:Normalize(away)
    local distance = math.max(api.Radius + batch.Radius + 0.02,
        (batch.Radius * 2 + 0.02) / (2 * math.sin(math.pi / pieces)))
    -- Keep the smaller balls at their new radius above the same local board surface.
    local center = position + blast.up * (batch.Radius - api.Radius)
    for i = 0, pieces - 1 do
        local direction = api.Vectors:Rotate(away, blast.up, i * 360 / pieces)
        if not batch:TryAdd(center + direction * distance, direction * scatterSpeed) then return end
    end
    if batch:TryCommit() then blast.affected = blast.affected + 1 end
end
