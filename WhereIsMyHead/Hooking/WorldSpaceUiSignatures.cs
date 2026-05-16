namespace WhereIsMyHead.Hooking;

internal static class WorldSpaceUiSignatures
{
    public const string UpdatePattern =
        "40 53 56 57 48 83 EC 30 33 F6 48 89 6C 24 50 4C 89 74 24 60 48 8B F9 4C 89 7C 24 68 89 B1 ?? ?? ?? ??";

    public const int UpdatePatternLength = 34;
    public const int FunctionMinSize = 0x300;
    public const int FunctionMaxSize = 0x500;
    public const int ValidationWindowBytes = 0x400;
    public const int LocatorThreshold = 8;
}
