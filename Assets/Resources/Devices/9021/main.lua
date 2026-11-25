-- 草皮：只保存首次碰撞的普通值；每次生成都创建新的有效 API 请求。
local round, first, value, multiplier, deadline = -1, nil, 0, 1, 0

local function sync(api)
    if round ~= api.RoundId then round, first = api.RoundId, nil end
    if first ~= nil and api.Time > deadline then first = nil end
end

local function status(api)
    if first == nil then api:SetStatus("草皮 · 等待弹珠")
    else api:SetStatus(string.format("草皮 · 等待另一颗 %.1fs", math.max(0, deadline - api.Time))) end
end

function OnTick(api)
    sync(api)
    status(api)
end

function OnContact(api)
    sync(api)
    if api.LinkKey ~= 0 or api.Impact == nil or api.RemainingDurability == 0 then return end
    if api:HasCopiedFrom(api.InstanceId) or api.Parameter <= 0 or api.Strength <= 0 then return end
    if first == api.BallId or not api:Visit(0, 0) then return end
    if first == nil then
        first, value, multiplier = api.BallId, api.Value, api.DeviceScoreMultiplier
        deadline = api.Time + api.Parameter
        status(api)
        return
    end

    local copy = api:CreateCopy()
    if not copy.CanCopy or not copy:TrySetValue(value / 2 + api.Value / 2)
        or not copy:TrySetDeviceScoreMultiplier(multiplier / 2 + api.DeviceScoreMultiplier / 2) then return end
    local source = api.Motion.Source
    local direction = source.Down
    -- Keep clear of the device and the second input ball, including unusually large balls.
    local distance = math.max(source.Radius + copy.Radius + 0.02,
        api.Vectors:Dot(api.Motion.Position - source.Position, direction) + api.Radius + copy.Radius + 0.02)
    local position = source.Position + source.Up * (copy.Radius + 0.005) + direction * distance
    if copy:TrySpawn(position, direction * api.Strength) then
        api:TryUse()
        first = nil
        status(api)
    end
end
