-- 大石碎胸口：蓄力、环形布局、发射与冷却均由 Lua 组合通用 API。
-- Value: required hits; Strength: launch speed; Range: cooldown seconds.
local projectileCount, projectileValue = 16, 1
local roundId, charge, cooldownUntil, nextAttemptAt = nil, 0, 0, 0

local function sync(api)
    if roundId ~= api.RoundId then
        roundId, charge, cooldownUntil, nextAttemptAt = api.RoundId, 0, 0, 0
    end
end

local function status(api)
    if api.Time + 0.0000001 < cooldownUntil then
        api:SetStatus(string.format("大石碎胸口 · 冷却 %.1fs", cooldownUntil - api.Time))
    elseif charge >= math.floor(api.Parameter) then
        api:SetStatus("大石碎胸口 · 蓄力完成，等待发射")
    else
        api:SetStatus(string.format("大石碎胸口 · 蓄力 %d/%d", charge, math.floor(api.Parameter)))
    end
end

local function fire(api, position, forward, up, sourceRadius)
    if charge < math.floor(api.Parameter) or api.RemainingDurability == 0 or api.Time + 0.0000001 < nextAttemptAt then return end
    nextAttemptAt = api.Time + 0.1
    local batch = api:CreateSpawnBatch()
    local radius = batch.Radius
    -- Adjacent sphere centers are separated by a chord, not just by the source radius.
    local distance = math.max(sourceRadius + radius + 0.02, (radius * 2 + 0.02) / (2 * math.sin(math.pi / projectileCount)))
    local center = position + up * (radius + 0.005)
    for i = 0, projectileCount - 1 do
        local direction = api.Vectors:Rotate(forward, up, i * 360 / projectileCount)
        if not batch:TryAdd(center + direction * distance, direction * api.Strength, projectileValue) then return end
    end
    if batch:TryCommit() then
        api:TryUse()
        charge, cooldownUntil = 0, api.Time + api.Range
    end
end

function OnTick(api)
    sync(api)
    if api.Parameter >= 1 and api.Strength > 0 and api.Range >= 0 and api.Time + 0.0000001 >= cooldownUntil then
        fire(api, api.Position, api.Forward, api.Up, api.Radius)
    end
    status(api)
end

function OnContact(api)
    sync(api)
    if api.Impact == nil or api.LinkKey ~= 0 or api.SpawnSourceId == api.InstanceId then return end
    if api.Parameter < 1 or api.Strength <= 0 or api.Range < 0 or api.RemainingDurability == 0 then return end
    if api.Time + 0.0000001 < cooldownUntil then return end
    if charge < math.floor(api.Parameter) then
        if not api:Visit(0, 0) then return end
        charge = charge + 1
        api:MarkTriggered() -- A successful Lua-only charge counts even before the later volley.
    end
    local source = api.Motion.Source
    fire(api, source.Position, source.Forward, source.Up, source.Radius)
    status(api)
end
