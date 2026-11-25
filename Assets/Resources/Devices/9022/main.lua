-- 故障装置：0 重置当前累计装置倍率，其余沿用倍率减一的累加规则。
local factors = { 0, 1, 2, 3, 5, 7, 11, 13 }
local round, last = -1, nil

local function sync(api)
    if round ~= api.RoundId then round, last = api.RoundId, nil end
end

local function status(api)
    if last == nil then api:SetStatus("故障装置 · 等待弹珠")
    elseif last == 0 then api:SetStatus("故障装置 · 累计倍率归零")
    else api:SetStatus(string.format("故障装置 · 本次倍率 ×%d", last)) end
end

function OnTick(api)
    sync(api)
    status(api)
end

function OnContact(api)
    sync(api)
    if api.Impact == nil or api.LinkKey ~= 0 then return end
    if not api:Visit(0, 0) or not api:TryUse() then return end
    local index = math.floor(api:RandomRange(0, #factors)) + 1
    last = factors[index]
    if last == 0 then api:SetDeviceScoreMultiplier(0)
    else api:AddScoreMultiplier(last) end
    api:MarkTriggered() -- 抽到 1 或已归零的球再次抽到 0，同样是一次成功处理。
    status(api)
end
