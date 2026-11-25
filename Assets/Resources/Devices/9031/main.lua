-- Number probabilities and settlement rewards are Lua policy, not engine device IDs.
local colors = {
    {1,.78,.1}, {.1,.3,.9}, {.9,.1,.12}, {.55,.15,.8},
    {1,.4,.05}, {.1,.65,.25}, {.5,.07,.12}, {.035,.035,.035}
}
local function status(api)
    if api.HeldCount > 0 then api:SetStatus("台球 · 吞入中")
    elseif api:GetRoundValue("readyAt") > api.Time then
        api:SetStatus(string.format("台球 · 冷却 %.1fs", api:GetRoundValue("readyAt") - api.Time))
    else api:SetStatus("台球 · 等待入球") end
end
function OnTick(api) status(api) end
function OnContact(api)
    if api.LinkKey ~= 0 or api.HeldCount > 0 or api:GetRoundValue("readyAt") > api.Time + 0.0000001 then return end
    if not api:Visit(0, 0) or not api.Motion:TryHold(api.Parameter) then return end
    local number = api:Roll(.2) and 8 or math.floor(api:RandomRange(1, 8))
    local c = colors[number]
    api:SetScoreRushContribution(number == 8 and -100 or number * 10)
    api:SetAppearance(tostring(number), c[1], c[2], c[3])
    api.Motion:TrySetHeldVisible(false)
    status(api)
end
function OnHoldEnd(api)
    local key = "release:" .. (api:GetRoundValue("releases") + 1)
    local source = api.Motion.Source
    if api:CanUseOnce(key) and api.Motion:TryLaunch(source, source.Down, math.max(api.Strength, api.Hold.EntrySpeed)) then
        api:TryUseOnce(key)
        api:SetRoundValue("releases", api:GetRoundValue("releases") + 1)
        api:SetRoundValue("readyAt", api.Time + api.Range)
    else
        api.Motion:TryHold(api.Parameter)
        api.Motion:TrySetHeldVisible(false)
    end
    status(api)
end
