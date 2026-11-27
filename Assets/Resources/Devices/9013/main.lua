-- 赚赚：判断历史、计算血肉残料和选择出口均由 Lua 组合。
function OnContact(api)
    if api.LinkKey ~= 0 or api.RemainingDurability == 0 then return end
    local hasOther = api.HasTriggeredOtherDevice
    if not api:Visit(0, 0) then return end
    if hasOther then
        -- Include the temporary board value bonus without storing it in the marble's base value.
        local scrap = math.floor(api.EffectiveValue * api.EffectiveDeviceScoreMultiplier + 0.5)
        if api:TryConsume(false) then
            api:TryUse()
            local credited = api:AddBiomassScrap(scrap)
            api:SetStatus(string.format("赚赚 · 返还 %.0f 血肉残料", credited))
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
