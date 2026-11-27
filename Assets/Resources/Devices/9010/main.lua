-- 矿老板：每座矿独立记录本回合产量，基础价值不足也能产血肉残料。
-- Value: base value loss; Strength: scrap per hit; Range: round scrap limit.
local roundId, produced = nil, 0

local function sync(api)
    if roundId ~= api.RoundId then
        roundId, produced = api.RoundId, 0
    end
end

local function status(api)
    local limit = math.max(0, math.floor(api.Range))
    api:SetStatus(string.format("矿老板 · 本回合 %d/%d 血肉残料%s", produced, limit,
        produced >= limit and " · 已采完" or ""))
end

function OnTick(api)
    sync(api)
    status(api)
end

function OnContact(api)
    sync(api)
    if api.Impact == nil or api.LinkKey ~= 0 then return end
    local reward, limit = math.floor(api.Strength), math.floor(api.Range)
    if api.Parameter < 0 or reward <= 0 or limit <= produced then return end
    -- The last payout respects the limit even when authoring a non-divisible cap.
    reward = math.min(reward, limit - produced)
    if api.RemainingDurability == 0 or not api:Visit(0, 0) then return end
    local credited = api:AddBiomassScrap(reward)
    if credited == 0 then return end
    api:TryUse()
    api:AddBase("BALL_BASE_VALUE", -api.Parameter)
    produced = produced + credited
    status(api)
end
