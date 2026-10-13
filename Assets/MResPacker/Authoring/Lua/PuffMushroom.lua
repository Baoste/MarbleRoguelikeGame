-- 大喷菇：eight equal shares, spread uphill in the board plane.
-- Range: half fan angle in degrees; Value/Strength are unused.
local outlets = 8

function OnContact(api)
    if api.LinkKey ~= 0 or api.Impact ~= nil or api.RemainingDurability == 0 then return end
    if api:HasSplitFrom(api.InstanceId) or not api:Visit(0, 0) then return end
    if api.Range < 1 or api.Range > 90 then return end
    local batch = api:CreateSplitBatch(outlets)
    if not batch.CanSplit then return end
    local motion = api.Motion
    local source, radius = motion.Source, batch.Radius
    local step = api.Range * 2 / (outlets - 1)
    local distance = math.max(source.Radius + radius + 0.02,
        (radius * 2 + 0.02) / (2 * math.sin(math.rad(step) / 2)))
    local center = source.Position + source.Up * (radius + 0.005)
    local uphill = source.Down * -1
    for i = 0, outlets - 1 do
        local direction = source:Rotate(uphill, -api.Range + i * step)
        if not batch:TryAdd(center + direction * distance, direction * motion.Speed) then return end
    end
    if batch:TryCommit() then
        api:TryUse()
        api:SetStatus("大喷菇 · 八口齐发")
    else
        api:SetStatus("大喷菇 · 出口受阻，原球保留")
    end
end
