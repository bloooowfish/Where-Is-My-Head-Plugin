using System;

namespace WhereIsMyHead.Gpu;

internal static class ComGuids
{
    public static readonly Guid IidIdxgiDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    public static readonly Guid IidIdxgiDevice3 = new("6007896c-3244-4afd-bf18-a6d3beda5023");
    public static readonly Guid IidIdxgiAdapter3 = new("645967A4-1392-4310-A798-8053CE3E93FD");

    public const int IUnknownReleaseIndex = 2;
    public const int IdxgiDeviceGetAdapterIndex = 7;
    public const int IdxgiDevice3TrimIndex = 17;
    public const int IdxgiAdapter3QueryVideoMemoryInfoIndex = 14;
}

