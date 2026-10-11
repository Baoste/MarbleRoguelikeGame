function OnContact(a)
if not a:Visit(0, 1) or not a:TryUse() then return end
-- Pass the full factor; the engine adds ScoreMultiplier - 1 to the shared bonus.
a:AddScoreMultiplier(a.ScoreMultiplier)
end
