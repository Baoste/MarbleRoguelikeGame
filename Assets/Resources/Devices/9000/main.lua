-- Package-level behaviour. The source prefab contains no Lua or game components.
-- OnContact is supplied by the game's device bridge (see Docs/Device_Lua_API.md).
function OnContact(api)
    if not api:Trigger(0) then return end
    local motion = api.Motion
    local source = motion.Source
    motion:TryLaunch(source, source.Forward, math.max(motion.Speed, api.Strength))
end
