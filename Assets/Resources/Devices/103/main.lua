-- 传送门：只改变位置，世界速度向量、旋转和弹珠自身物理属性保持不变。
-- Local fields are evaluated at the destination in the next physics substep.
function OnContact(api)
    if api.PairId == 0 or api.TransportLockedUntil > api.Time then return end
    local motion = api.Motion
    local exit = motion:FindPartner()
    if exit == nil then return end

    -- Pick a clear position in the destination board plane without rotating velocity.
    local offsetDirection = api.Vectors:Normalize(api.Vectors:ProjectOnPlane(motion.Velocity, exit.Up))
    if offsetDirection.sqrMagnitude < 0.000000000001 then offsetDirection = exit.Forward end
    local position = exit.Position + exit.Up * (api.Radius + 0.005)
        + offsetDirection * (exit.Radius + api.Radius + 0.02)
    if not api:Trigger(-api.PairId) then return end
    if motion:TrySetPosition(position) then
        api.TransportLockedUntil = api.Time + 0.5
    end
end
