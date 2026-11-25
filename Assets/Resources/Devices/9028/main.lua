-- Stage rules belong to Lua. Instance state survives rounds; engine wear resets each round.
local stages = {
    [0] = { name = "挖洞", bonus = 0, durability = 0, node = "Hole" },
    { name = "不锈钢牙", bonus = 2, durability = 5, node = "Steel" },
    { name = "陶瓷牙", bonus = 5, durability = 8, node = "Ceramic" },
    { name = "金刚钻牙", bonus = 20, durability = 20, node = "Diamond" }
}
local visualStage = 0
local function stage(api)
    return math.max(0, math.min(3, math.floor(api:GetInstanceValue("stage"))))
end
local function paint()
    if self == nil then return end
    for i = 0, 3 do self:TrySetRenderersVisible(stages[i].node, i == visualStage) end
end
local function status(api)
    local s = stages[stage(api)]
    api:SetStatus(string.format("种植牙 · %s · 附加倍率 +%d · 耐久 %d/%d", s.name, s.bonus, api.RemainingDurability, s.durability))
end
local function refresh(api)
    visualStage = stage(api)
    status(api)
    paint()
end
function Start() paint() end
function OnCreated(api)
    api:SetInstanceValue("stage", 0)
    api:SetDurabilityLimit(0)
    refresh(api)
end
function OnRoundStart(api) refresh(api) end
function OnOfferAccepted(api) refresh(api) end
function OnOffer(api)
    local current = stage(api)
    if api.OfferedDefinitionId ~= api.DefinitionId or current >= 3 then return end
    local nextStage = current + 1
    api:SetInstanceValue("stage", nextStage)
    api:SetDurabilityLimit(stages[nextStage].durability)
    api:RestoreDurability()
    status(api)
    api:Accept()
end
function OnContact(api)
    local current = stage(api)
    if current == 0 or api.Impact == nil or api.LinkKey ~= 0 then return end
    if not api:Visit(0, 0) or not api:TryUse() then return end
    api:AddScoreMultiplier(1 + stages[current].bonus)
    if api.RemainingDurability == 0 then
        api:SetInstanceValue("stage", 0)
        api:SetDurabilityLimit(0)
    end
    refresh(api)
end
