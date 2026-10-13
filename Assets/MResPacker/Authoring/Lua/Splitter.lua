-- 分流器：Lua chooses the three ports; the generic batch owns division and rollback.
-- Range: side outlet angle in degrees. Value/Strength are unused.
function OnContact(api)
    if api.LinkKey ~= 0 or api.Impact ~= nil or api.RemainingDurability == 0 then return end
    if api:HasSplitFrom(api.InstanceId) or not api:Visit(0, 0) then return end
    if api.Range < 1 or api.Range > 80 then return end
    local batch = api:CreateSplitBatch(3)
    if not batch.CanSplit then return end
    local motion = api.Motion
    local source, radius = motion.Source, batch.Radius
    local distance = math.max(source.Radius + radius + 0.02,
        (radius * 2 + 0.02) / (2 * math.sin(math.rad(api.Range) / 2)))
    local center = source.Position + source.Up * (radius + 0.005)
    for i = -1, 1 do
        local direction = source:Rotate(source.Down, i * api.Range)
        if not batch:TryAdd(center + direction * distance, direction * motion.Speed) then return end
    end
    if batch:TryCommit() then
        api:TryUse()
        api:SetStatus("分流器 · 三口均分")
    else
        api:SetStatus("分流器 · 出口受阻，原球保留")
    end
end
