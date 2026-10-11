function OnContact(a)
if not a:Visit(0, 1) or not a:TryUse() then return end
a:AddRushChance(a.RushChanceAdd)
end
