-- 黏黏：接触时固定弹珠，滞留 Value 秒，然后沿棋盘下坡方向释放。
-- Parameter maps to game/device.json.Value. Hold and launch are independent capabilities.
function OnContact(api)
    -- Reserve enough remaining releases for already-held balls without spending on admission.
    if api.RemainingDurability >= 0 and api.HeldCount >= api.RemainingDurability then return end
    if not api:TryPrepareTrigger(0) then return end
    api.Motion:TryHold(api.Parameter)
end

function OnHoldEnd(api)
    local useKey = "release:" .. api.BallId
    if not api:CanUseOnce(useKey) then return end
    local motion = api.Motion
    local source = motion.Source
    local speed = api.Hold.EntrySpeed
    local released = motion:TryLaunch(source, source.Down, speed)
    if not released then
        -- The exit is blocked: release downhill in place instead of teleporting into an obstacle.
        released = motion:TrySetVelocity(source.Down * speed)
    end
    if released then api:TryUseOnce(useKey) end
end
