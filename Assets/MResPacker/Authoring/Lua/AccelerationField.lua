-- 加速力场：几何区域与恒力是独立的基础 API。
-- The engine integrates force over the physics substep and divides by ball mass.
function OnField(api)
    if api:OverlapsBox("Field") and api:TryUse() then
        api:TryAddForce(api.Forward * api.Strength)
    end
end
