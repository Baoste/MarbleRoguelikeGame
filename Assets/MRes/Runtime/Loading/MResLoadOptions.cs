namespace MRes
{
    public sealed class MResLoadOptions
    {
        public bool ExecuteLua = true;
        public bool ActivateRoot = true;
        /// <summary>Optional caller-owned script; disposing the visual only detaches it.</summary>
        public MResLuaScript SharedScript;
    }
}
