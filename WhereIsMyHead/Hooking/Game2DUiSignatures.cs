namespace WhereIsMyHead.Hooking;

internal static class Game2DUiSignatures
{
    public const string WrapperPattern =
        "48 83 EC 28 48 8B 01 FF 50 40 48 8B C8 48 83 C4 28 E9 ?? ?? ?? ??";

    public const int WrapperLength = 22;
    public const int FunctionMinSize = 0xF0;
    public const int FunctionMaxSize = 0x280;
    public const int ValidationWindowBytes = 0x80;
    public const int LocatorThreshold = 9;
}
