namespace WhereIsMyHead.Hooking;

internal static class World3DSignatures
{
    public const string TaskRenderGraphicsRenderPattern = "48 8B 0D ?? ?? ?? ?? E9 ?? ?? ?? ??";
    public const int ThunkLength = 12;
    public const int FunctionMinSize = 0x300;
    public const int FunctionMaxSize = 0xA00;
    public const int ValidationWindowBytes = 0x180;
    public const int LocatorThreshold = 9;
}
