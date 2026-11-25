-- A marble receives this device family's effects once, even across different teapots.
-- Value: extra Rush seconds; Strength: friction increase; Range: bounce decrease.
function OnContact(api)
    if api.Impact == nil or api.LinkKey ~= 0 then return end
    -- Shared definition key lives outside positive target IDs and negative int32 pair IDs.
    local visitKey = -(4294967296 + api.DefinitionId)
    if not api:Visit(visitKey, 1, true) or not api:TryUse() then return end
    api:SetCurrent("BALL_FRICTION", api:Get("BALL_FRICTION") * (1 + math.max(0, api.Strength)))
    api:SetCurrent("BALL_BOUNCE", api:Get("BALL_BOUNCE") * (1 - math.min(1, math.max(0, api.Range))))
    api.RushDurationBonus = math.min(3600, api.RushDurationBonus + math.max(0, api.Parameter))
    api:SetStatus(string.format("降降火 · 摩擦 %.3g · 弹性 %.3g · 累满 RUSH 时 +%.3gs",
        api:Get("BALL_FRICTION"), api:Get("BALL_BOUNCE"), api.RushDurationBonus))
end
