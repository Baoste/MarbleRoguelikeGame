using System;

namespace MRes
{
    public class MResException : Exception
    {
        public MResException(string message, Exception inner = null) : base(message, inner) { }
    }
    public sealed class MResFormatException : MResException
    {
        public MResFormatException(string message, Exception inner = null) : base(message, inner) { }
    }
    public sealed class MResMissingResourceException : MResException
    {
        public MResMissingResourceException(string message) : base(message) { }
    }
    public sealed class MResShaderException : MResException
    {
        public MResShaderException(string message) : base(message) { }
    }
    public sealed class MResInstantiationException : MResException
    {
        public MResInstantiationException(string message, Exception inner = null) : base(message, inner) { }
    }
    public sealed class MResLuaException : MResException
    {
        public MResLuaException(string message, Exception inner = null) : base(message, inner) { }
    }
}
