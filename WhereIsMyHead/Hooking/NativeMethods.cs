using System.Runtime.InteropServices;

namespace WhereIsMyHead.Hooking;

internal static partial class NativeMethods
{
    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();
}

