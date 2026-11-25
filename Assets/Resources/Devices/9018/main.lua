-- 复制体：碰撞信息、镜面反射、复制和耐久分别由独立 API 组合。
local round = -1
local copied = {}

function OnContact(api)
    if round ~= api.RoundId then round = api.RoundId; copied = {} end
    if copied[api.BallId] or api:HasCopiedFrom(api.InstanceId) then return end
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
    local direction = vectors:Normalize(vectors:Reflect(incoming, normal))
    local copy = api:CreateCopy()
    if not copy.CanCopy or not copy:TrySetValue(0) then return end
    -- Put the copy ahead of the original and outside the contact surface; physics rejects blocked space.
    local position = motion.Position + direction * (api.Radius + copy.Radius + 0.02) + normal * 0.01
    if copy:TrySpawn(position, direction * hit.IncomingSpeed) then
        copied[api.BallId] = true
        api:TryUse()
    end
end
