function OnContact(api)
    if api.Impact == nil or api.ScoreMultiplier == 1 or api.RemainingDurability == 0
        or not api:Visit(0, 0) then return end
    if api:TryMultiplyScore(api.ScoreMultiplier) then api:TryUse() end
end
