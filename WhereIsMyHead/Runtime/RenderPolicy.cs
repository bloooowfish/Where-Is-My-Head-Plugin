namespace WhereIsMyHead.Runtime;

internal readonly record struct RenderPolicy(
    bool Game2DUiEnabled,
    bool WorldSpaceUiEnabled,
    bool World3DEnabled)
{
    public static RenderPolicy Default => new(
        Game2DUiEnabled: true,
        WorldSpaceUiEnabled: false,
        World3DEnabled: false);

    public static RenderPolicy AllEnabled => new(
        Game2DUiEnabled: true,
        WorldSpaceUiEnabled: true,
        World3DEnabled: true);
}
