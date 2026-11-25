-- 造血工厂：减半、取整与回血换算由 Lua 决定，血量上限由引擎维护。
-- Value: retained value ratio; Strength: blood per remaining value.
function OnContact(api)
    if api.Impact == nil or api.LinkKey ~= 0 then return end
    if api.Value <= 0 or api.Parameter <= 0 or api.Parameter >= 1 or api.Strength <= 0 then return end
    local remaining = math.floor(api.Value * api.Parameter)
    if not api:Visit(0, 0) or not api:TryUse() then return end
    -- Replace the computed value without applying old value modifiers a second time.
    api:SetCurrent("BALL_BASE_VALUE", remaining)
    local restored = api:RestoreBlood(api.Value * api.Strength)
    api:SetStatus(string.format("造血工厂 · 价值 %.0f · 回复 %.1f 血", api.Value, restored))
end
