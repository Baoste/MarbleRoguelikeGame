-- 摇钱树：只计进入次数，不改变弹珠运动；每批产出才扣耐久。
-- Value: entries per payout; Strength: coins per payout. Range is unused.
local roundId, progress, payouts = nil, 0, 0

local function sync(api)
    if roundId ~= api.RoundId then
        roundId, progress, payouts = api.RoundId, 0, 0
    end
end

local function status(api)
    api:SetStatus(string.format("摇钱树 · %d/%d · 已产出 %d 金币", progress,
        math.max(1, math.floor(api.Parameter)), payouts * math.floor(api.Strength)))
end

function OnTick(api)
    sync(api)
    status(api)
end

function OnContact(api)
    sync(api)
    if api.LinkKey ~= 0 or api.Impact ~= nil or api.RemainingDurability == 0 then return end
    local required, reward = math.floor(api.Parameter), math.floor(api.Strength)
    if required < 1 or required > 10000 or reward < 1 then return end
    if not api:Visit(0, 0) then return end
    progress = progress + 1
    api:MarkTriggered() -- Successful Lua-only accumulation, independent of durability.
    if progress >= required and api:TryUse() then
        api:AddCoins(reward)
        progress, payouts = progress - required, payouts + 1
    end
    status(api)
end
