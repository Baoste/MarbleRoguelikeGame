using System;

namespace MarblesECS
{
    internal sealed class DeviceLuaException : Exception
    {
        internal DeviceLuaException(string message, Exception inner = null) : base(message, inner) { }
    }
}
