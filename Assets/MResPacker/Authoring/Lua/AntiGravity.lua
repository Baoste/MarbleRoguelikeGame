-- 反重力：独立组合上坡出射和限时重力倍率；引擎不保存装置专属效果。
function OnContact(api)
    if not api:Trigger(0) then return end
    local motion = api.Motion
    local source = motion.Source
    -- Up 是棋盘法向；-Down 才是棋盘上的上方。
    if motion:TryLaunch(source, -source.Down, motion.Speed) then
        motion:TrySetGravityScale(-1, api.Parameter, true)
    end
end
