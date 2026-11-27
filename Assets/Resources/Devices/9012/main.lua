-- 猪猪存钱罐：按撞击时持有的血肉残料计算倍率，不花费血肉残料。
-- Value: scrap per tier; Strength: multiplier bonus per tier.
local function multiplier(api)
    if api.Parameter <= 0 or api.Strength <= 0 then return 1 end
    return 1 + math.floor(api.BiomassScrapBalance / api.Parameter) * api.Strength
end

local function status(api)
    api:SetStatus(string.format("猪猪存钱罐 · 当前撞击 ×%.3g", multiplier(api)))
end

function OnTick(api)
    status(api)
end

function OnContact(api)
    if api.Impact == nil or api.LinkKey ~= 0 then return end
    local factor = multiplier(api)
    if factor <= 1 then return end
    if not api:Visit(0, 0) or not api:TryUse() then return end
    api:MultiplyScore(factor)
    status(api)
end
