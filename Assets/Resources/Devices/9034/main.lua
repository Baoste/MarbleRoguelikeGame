function OnContact(api)
    if api.Impact == nil or api.Parameter <= 0 or not api:Visit(0, 0) then return end
    local before = api.Value
    api:SetCurrent("BALL_BASE_VALUE", before + api.Parameter)
    if api.Value ~= before then api:TryUse() end
end
