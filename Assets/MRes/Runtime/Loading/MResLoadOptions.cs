namespace MRes
{
    public sealed class MResLoadOptions
    {
        public bool ExecuteLua = true;
        public bool ActivateRoot = true;
        /// <summary>Headless mode keeps meshes, renderers, colliders and Lua, but skips GPU materials/textures.</summary>
        public bool LoadRenderingResources = true;
        /// <summary>Optional caller-owned script; disposing the visual only detaches it.</summary>
        public MResLuaScript SharedScript;
    }
}
