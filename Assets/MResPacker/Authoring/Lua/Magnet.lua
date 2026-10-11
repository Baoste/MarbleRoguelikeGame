function OnField(a)
 if not a.Magnetic or a.Response <= 0 then return end
 local range = a.Range + a.MagneticRadius
 local distance = a.Distance
 if distance < 0.001 or distance >= range then return end
 if not a:TryUse() then return end
 local weight = 1 - distance / range
 local sign = a.ForwardOffset >= 0 and 1 or -1
 a:AddRadialForce(sign * a.Strength * a.Response * weight * weight)
end
