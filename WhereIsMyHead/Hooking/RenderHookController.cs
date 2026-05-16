using System;
using WhereIsMyHead.Runtime;

namespace WhereIsMyHead.Hooking;

internal sealed class RenderHookController : IRenderHookController, IDisposable
{
    public World3DHookController World3D { get; } = new();
    public Game2DUiHookController Game2DUi { get; } = new();
    public WorldSpaceUiHookController WorldSpaceUi { get; } = new();

    public void InstallIfNeeded()
    {
        this.World3D.InstallIfNeeded();
        this.Game2DUi.InstallIfNeeded();
        this.WorldSpaceUi.InstallIfNeeded();
    }

    public void SetPolicy(RenderPolicy policy)
    {
        this.World3D.SetPolicy(policy);
        this.Game2DUi.SetPolicy(policy);
        this.WorldSpaceUi.SetPolicy(policy);
    }

    public void ResetObservation()
    {
        this.World3D.ResetObservation();
        this.Game2DUi.ResetObservation();
        this.WorldSpaceUi.ResetObservation();
    }

    public void Dispose()
    {
        this.WorldSpaceUi.Dispose();
        this.Game2DUi.Dispose();
        this.World3D.Dispose();
    }
}
