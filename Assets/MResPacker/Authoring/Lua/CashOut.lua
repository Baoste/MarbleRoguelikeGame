-- 赚赚：判断历史、计算金币和选择出口均由 Lua 组合。
function OnContact(api)
    if api.LinkKey ~= 0 or api.RemainingDurability == 0 then return end
    local hasOther = api.HasTriggeredOtherDevice
    if not api:Visit(0, 0) then return end
    if hasOther then
        -- Value already includes current base-value modifiers. Do not multiply by 2.
        local coins = math.floor(api.Value * api.DeviceScoreMultiplier + 0.5)
        if api:TryConsume(false) then
            api:TryUse()
            api:AddCoins(coins)
            api:SetStatus(string.format("赚赚 · 返还 %.0f 金币", coins))
        end
    else
        local motion = api.Motion
        local source = motion.Source
        if motion:TryLaunch(source, source.Down, motion.Speed) then
            api:TryUse()
            api:SetStatus("赚赚 · 首次经过，向下放行")
        end
    end
end
