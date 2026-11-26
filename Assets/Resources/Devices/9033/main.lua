-- Value is the retained fraction; Strength is the number of scrap units returned.
function OnContact(api)
    if api.Impact == nil or api.Parameter < 0 or api.Parameter > 1 or api.Strength < 1 then return end
    if not api:Visit(0, 0) or not api:TryUse() then return end
    api:SetCurrent("BALL_BASE_VALUE", api.Value * api.Parameter)
    api:AddBiomassScrap(api.Strength)
end
