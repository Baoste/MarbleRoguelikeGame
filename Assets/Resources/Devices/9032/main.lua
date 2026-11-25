-- 超大杯全冰美式：Value 为保留价值比例，Strength 为额外 RUSH 贡献。
function OnContact(api)
    if api.Impact == nil or api.LinkKey ~= 0 then return end
    if api.Parameter < 0 or api.Parameter > 1 or api.Strength <= 0 then return end
    local remaining = api.Value * api.Parameter
    if not api:Visit(0, 0) or not api:TryUse() then return end
    -- 保留小数；只转换基础价值，不把临时 RUSH +5 写入永久价值。
    api:SetCurrent("BALL_BASE_VALUE", remaining)
    api:AddRushContribution(api.Strength)
    api:SetStatus(string.format("全冰美式 · 价值 %.2f · RUSH 贡献 +%.0f", api.Value, api.Strength))
end
