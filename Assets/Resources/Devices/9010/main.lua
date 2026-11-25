-- 矿老板：每座矿独立记录本回合产量，基础价值不足也能产金币。
-- Value: base value loss; Strength: coins per hit; Range: round coin limit.
local roundId, produced = nil, 0

local function sync(api)
    if roundId ~= api.RoundId then
        roundId, produced = api.RoundId, 0
    end
end

local function status(api)
    local limit = math.max(0, math.floor(api.Range))
    api:SetStatus(string.format("矿老板 · 本回合 %d/%d 金币%s", produced, limit,
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
    if not api:Visit(0, 0) or not api:TryUse() then return end
    api:AddBase("BALL_BASE_VALUE", -api.Parameter)
    api:AddCoins(reward)
    produced = produced + reward
    status(api)
end
