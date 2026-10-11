-- 吸吸：Lua 负责范围、聚集位置、共同截止时间和释放方向。
-- Only plain data survives callbacks; no API, Motion or endpoint is retained.
local roundId, endsAt = nil, 0
local entries, slots = {}, {}
local batchNumber, batchKey = 0, nil

local function resetRound(api)
    if roundId ~= api.RoundId then
        roundId, endsAt = api.RoundId, 0
        entries, slots = {}, {}
        batchNumber, batchKey = 0, nil
    end
end

-- Reserve a compact, non-overlapping slot, accounting for different ball sizes.
local function findSlot(api)
    local source = api.Motion.Source
    local radius = api.Radius
    local spacing = radius * 2 + 0.02
    local right = source:Rotate(source.Forward, 90)
    for ring = 0, 12 do
        for z = -ring, ring do
            for x = -ring, ring do
                if math.max(math.abs(x), math.abs(z)) == ring then
                    local offset = right * (x * spacing) + source.Forward * (z * spacing)
                    if offset.magnitude + radius <= api.Range * 0.75 then
                        local position = source.Position + source.Up * (radius + 0.005) + offset
                        local free = true
                        for _, slot in ipairs(slots) do
                            if (slot.position - position).magnitude < slot.radius + radius + 0.01 then
                                free = false
                                break
                            end
                        end
                        if free then return { position = position, radius = radius } end
                    end
                end
            end
        end
    end
    return nil
end

local function release(api, entry)
    -- Keep the separated cluster positions: launching everyone at one exit would overlap them.
    if api:CanUseOnce(entry.useKey) and api.Motion:TrySetVelocity(api.Motion.Source.Down * entry.speed) then
        api:TryUseOnce(entry.useKey)
        -- Complete every member, including balls still approaching when the last durability is spent.
        api:TryKeepActive(math.max(0.001, api.DeltaTime))
    end
    entries[api.BallId] = nil
end

function OnBallUpdate(api)
    resetRound(api)
    local motion = api.Motion
    local entry = entries[api.BallId]
    if entry == nil then
        if api.RemainingDurability == 0 then return end
        local offset = motion.Position - motion.Source.Position
        if api.Vectors:ProjectOnPlane(offset, motion.Source.Up).magnitude > api.Range then return end
        if math.abs(api.Vectors:Dot(offset, motion.Source.Up)) > api.Radius + 0.15 then return end
        if api.Parameter <= 0 or api.Strength <= 0 then return end

        local starting = api.Time + 0.0000001 >= endsAt
        if starting then slots = {} end
        local slot = findSlot(api)
        if slot == nil then return end
        if starting then
            if not api:TryPrepareTrigger(0) then return end
            endsAt = api.Time + math.min(60, api.Parameter)
            batchNumber = batchNumber + 1
            batchKey = "batch:" .. batchNumber
        elseif not api:Visit(0, 1) then
            return
        end
        slots[#slots + 1] = slot
        entry = { speed = motion.Speed, position = slot.position, endsAt = endsAt, useKey = batchKey }
        entries[api.BallId] = entry
    end

    local remaining = entry.endsAt - api.Time
    if remaining <= 0.0000001 then
        release(api, entry)
        return
    end
    local delta = entry.position - motion.Position
    local distance = delta.magnitude
    if distance <= math.max(0.01, api.Strength * api.DeltaTime) then
        -- Moving checks solid obstacles. Holding and releasing are independent operations.
        if motion:TryMove(entry.position, motion.Velocity) and motion:TryHold(remaining) then return end
    end
    motion:TrySetVelocity(api.Vectors:Normalize(delta) * math.min(api.Strength, distance / api.DeltaTime))
end

function OnHoldEnd(api)
    resetRound(api)
    local entry = entries[api.BallId]
    if entry ~= nil then release(api, entry) end
end
