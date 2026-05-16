using System;
using System.Threading;
using WhereIsMyHead.Runtime;
using Xunit;

namespace WhereIsMyHead.Tests;

public sealed class LoginLifecycleControllerTests
{
    [Fact]
    public void Start_WhenAlreadyLoggedIn_InstallsOnceAndAppliesConfiguredPolicy()
    {
        var source = new FakeLoginLifecycleSource { IsLoggedIn = true };
        var world = new FakeWorld3DController();
        var config = new FakeRuntimeConfiguration { World3DEnabled = false, AutoApplyAfterLogin = true };
        using var controller = new LoginLifecycleController(source, world, config, CancellationToken.None);

        controller.Start();
        controller.Start();

        Assert.Equal(1, world.InstallCalls);
        Assert.Equal(RenderPolicy.Default, world.LastPolicy);
    }

    [Fact]
    public void Login_AppliesConfiguredPolicy()
    {
        var source = new FakeLoginLifecycleSource();
        var world = new FakeWorld3DController();
        var config = new FakeRuntimeConfiguration
        {
            Game2DUiEnabled = false,
            WorldSpaceUiEnabled = true,
            World3DEnabled = true,
            AutoApplyAfterLogin = true,
        };
        using var controller = new LoginLifecycleController(source, world, config, CancellationToken.None);
        controller.Start();

        source.RaiseLogin();

        Assert.Equal(1, world.InstallCalls);
        Assert.Equal(new RenderPolicy(
            Game2DUiEnabled: false,
            WorldSpaceUiEnabled: true,
            World3DEnabled: true), world.LastPolicy);
    }

    [Fact]
    public void Login_WhenOpenOnLoginEnabled_OpensWindow()
    {
        var source = new FakeLoginLifecycleSource();
        var world = new FakeWorld3DController();
        var config = new FakeRuntimeConfiguration { OpenOnLogin = true };
        var openCount = 0;
        using var controller = new LoginLifecycleController(source, world, config, CancellationToken.None, () => openCount++);
        controller.Start();

        source.RaiseLogin();

        Assert.Equal(1, openCount);
    }

    [Fact]
    public void Logout_EnablesWorldAndResetsObservation()
    {
        var source = new FakeLoginLifecycleSource();
        var world = new FakeWorld3DController();
        using var controller = new LoginLifecycleController(source, world, new FakeRuntimeConfiguration(), CancellationToken.None);
        controller.Start();

        source.RaiseLogout();

        Assert.Equal(RenderPolicy.AllEnabled, world.LastPolicy);
        Assert.Equal(1, world.ResetCalls);
    }

    [Fact]
    public void TerritoryChanged_AcceptsDalamudUIntTerritoryTypeAndOnlyResetsObservation()
    {
        var source = new FakeLoginLifecycleSource();
        var world = new FakeWorld3DController();
        using var controller = new LoginLifecycleController(source, world, new FakeRuntimeConfiguration(), CancellationToken.None);
        controller.Start();

        source.RaiseTerritoryChanged(0x1_0000);

        Assert.Equal(1, world.ResetCalls);
        Assert.Equal(0, world.InstallCalls);
    }

    [Fact]
    public void CancelledToken_IgnoresLaterEvents()
    {
        var source = new FakeLoginLifecycleSource();
        var world = new FakeWorld3DController();
        using var cts = new CancellationTokenSource();
        using var controller = new LoginLifecycleController(source, world, new FakeRuntimeConfiguration(), cts.Token);
        controller.Start();

        cts.Cancel();
        source.RaiseLogin();
        source.RaiseLogout();
        source.RaiseTerritoryChanged(777);

        Assert.Equal(0, world.InstallCalls);
        Assert.Equal(0, world.ResetCalls);
        Assert.Null(world.LastPolicy);
    }

    private sealed class FakeLoginLifecycleSource : ILoginLifecycleSource
    {
        public event Action? Login;
        public event Action<int, int>? Logout;
        public event Action<uint>? TerritoryChanged;

        public bool IsLoggedIn { get; set; }

        public void RaiseLogin() => this.Login?.Invoke();

        public void RaiseLogout() => this.Logout?.Invoke(0, 0);

        public void RaiseTerritoryChanged(uint territoryType) => this.TerritoryChanged?.Invoke(territoryType);
    }

    private sealed class FakeWorld3DController : IRenderHookController
    {
        public int InstallCalls { get; private set; }
        public int ResetCalls { get; private set; }
        public RenderPolicy? LastPolicy { get; private set; }

        public void InstallIfNeeded() => this.InstallCalls++;

        public void SetPolicy(RenderPolicy policy) => this.LastPolicy = policy;

        public void ResetObservation() => this.ResetCalls++;
    }

    private sealed class FakeRuntimeConfiguration : IRuntimeConfiguration
    {
        public bool AutoApplyAfterLogin { get; init; } = true;
        public bool Game2DUiEnabled { get; init; } = true;
        public bool WorldSpaceUiEnabled { get; init; }
        public bool World3DEnabled { get; init; }
        public bool OpenOnLogin { get; init; }
    }
}
