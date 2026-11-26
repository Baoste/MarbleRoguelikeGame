-- Value is the number of copies; Strength is the angular spacing of the exits.
function OnContact(api)
    local hit = api.Impact
    if hit == nil or api:HasCopiedFrom(api.InstanceId) then return end
    local count = api.Parameter
    if count < 1 or count > 20 or count ~= math.floor(count) then return end
    local motion, source = api.Motion, api.Motion.Source
    local copy = api:CreateCopy(count)
    if not copy.CanCopy or not api:Visit(0, 1) then return end
    for i = 1, count do
        local heading = source:Rotate(source.Down, (i - (count + 1) / 2) * api.Strength)
        local spacing = source.Radius + copy.Radius * (count + 2) + 0.03
        local position = source.Position + source.Up * (copy.Radius + 0.005) + heading * spacing
        if not copy:TryAddSpawn(position, heading * hit.IncomingSpeed) then return end
    end
    if copy:TryCommit() then api:TryUse() end
end
