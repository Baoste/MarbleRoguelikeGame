-- 弹弹：在棋盘平面内，以镜面反射方向为中心随机偏转 Value 度。
-- Collision data, vector math, randomness and motion remain independent engine APIs.
function OnContact(api)
    local hit = api.Impact
    if hit == nil then return end
    local motion = api.Motion
    local source = motion.Source
    local vectors = api.Vectors
    local incoming = vectors:ProjectOnPlane(hit.IncomingVelocity, source.Up)
    local normal = vectors:ProjectOnPlane(hit.Normal, source.Up)
    if incoming.sqrMagnitude < 0.000000000001 or normal.sqrMagnitude < 0.000000000001 then return end
    normal = vectors:Normalize(normal)
    if vectors:Dot(incoming, normal) >= 0 then return end

    local reflected = vectors:Normalize(vectors:Reflect(incoming, normal))
    local spread = math.min(90, math.max(0, api.Parameter))
    local angle = vectors:SignedAngle(normal, reflected, source.Up)
    -- Only sample directions leaving the surface, even for a grazing collision.
    local minimum = math.max(-spread, -90 - angle)
    local maximum = math.min(spread, 90 - angle)
    if minimum > maximum or not api:Trigger(0) then return end
    local offset = api:RandomRange(minimum, maximum)
    local direction = source:Rotate(reflected, offset)
    motion:TrySetVelocity(direction * hit.IncomingSpeed)
end
