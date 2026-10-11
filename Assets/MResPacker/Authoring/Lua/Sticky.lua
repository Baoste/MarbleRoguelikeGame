-- 黏黏：接触时固定弹珠，滞留 Value 秒，然后沿棋盘下坡方向释放。
-- Parameter maps to game/device.json.Value. Hold and launch are independent capabilities.
function OnContact(api)
    if not api:Trigger(0) then return end
    api.Motion:TryHold(api.Parameter)
end

function OnHoldEnd(api)
    local motion = api.Motion
    local source = motion.Source
    local speed = api.Hold.EntrySpeed
    if not motion:TryLaunch(source, source.Down, speed) then
        -- The exit is blocked: release downhill in place instead of teleporting into an obstacle.
        motion:TrySetVelocity(source.Down * speed)
    end
end
