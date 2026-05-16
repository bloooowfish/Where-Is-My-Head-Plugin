using System;
using System.Collections.Generic;
using System.Threading;
using Dalamud.Plugin.Services;

namespace WhereIsMyHead.Runtime;

internal interface ILoginLifecycleSource
{
    event Action? Login;
    event Action<int, int>? Logout;
    event Action<uint>? TerritoryChanged;

    bool IsLoggedIn { get; }
}

internal sealed class DalamudLoginLifecycleSource(IClientState clientState) : ILoginLifecycleSource
{
    private readonly Dictionary<Action<int, int>, IClientState.LogoutDelegate> logoutHandlers = [];

    public event Action? Login
    {
        add => clientState.Login += value!;
        remove => clientState.Login -= value!;
    }

    public event Action<int, int>? Logout
    {
        add
        {
            if (value is null)
                return;

            IClientState.LogoutDelegate wrapper = (type, code) => value(type, code);
            this.logoutHandlers[value] = wrapper;
            clientState.Logout += wrapper;
        }

        remove
        {
            if (value is null)
                return;

            if (this.logoutHandlers.Remove(value, out var wrapper))
                clientState.Logout -= wrapper;
        }
    }

    public event Action<uint>? TerritoryChanged
    {
        add => clientState.TerritoryChanged += value!;
        remove => clientState.TerritoryChanged -= value!;
    }

    public bool IsLoggedIn => clientState.IsLoggedIn;
}

internal sealed class LoginLifecycleController : IDisposable
{
    private readonly ILoginLifecycleSource source;
    private readonly IRenderHookController renderHooks;
    private readonly IRuntimeConfiguration config;
    private readonly CancellationToken token;
    private readonly Action? openWindow;
    private bool started;
    private bool disposed;

    public LoginLifecycleController(
        ILoginLifecycleSource source,
        IRenderHookController renderHooks,
        IRuntimeConfiguration config,
        CancellationToken token,
        Action? openWindow = null)
    {
        this.source = source;
        this.renderHooks = renderHooks;
        this.config = config;
        this.token = token;
        this.openWindow = openWindow;
    }

    public void Start()
    {
        if (this.disposed || this.started)
            return;

        this.started = true;
        this.source.Login += this.OnLogin;
        this.source.Logout += this.OnLogout;
        this.source.TerritoryChanged += this.OnTerritoryChanged;

        if (this.source.IsLoggedIn)
            this.ApplyLoggedInPolicy();
    }

    public void Dispose()
    {
        if (this.disposed)
            return;

        this.disposed = true;
        if (!this.started)
            return;

        this.source.Login -= this.OnLogin;
        this.source.Logout -= this.OnLogout;
        this.source.TerritoryChanged -= this.OnTerritoryChanged;
    }

    private bool IsAlive => !this.disposed && !this.token.IsCancellationRequested;

    private void OnLogin()
    {
        if (!this.IsAlive)
            return;

        this.ApplyLoggedInPolicy();
    }

    private void OnLogout(int type, int code)
    {
        _ = type;
        _ = code;
        if (!this.IsAlive)
            return;

        this.renderHooks.SetPolicy(RenderPolicy.AllEnabled);
        this.renderHooks.ResetObservation();
    }

    private void OnTerritoryChanged(uint territoryType)
    {
        _ = territoryType;
        if (!this.IsAlive)
            return;

        this.renderHooks.ResetObservation();
    }

    private void ApplyLoggedInPolicy()
    {
        if (!this.IsAlive)
            return;

        this.renderHooks.InstallIfNeeded();
        if (this.config.AutoApplyAfterLogin)
            this.renderHooks.SetPolicy(new RenderPolicy(
                this.config.Game2DUiEnabled,
                this.config.WorldSpaceUiEnabled,
                this.config.World3DEnabled));
        if (this.config.OpenOnLogin)
            this.openWindow?.Invoke();
    }
}
