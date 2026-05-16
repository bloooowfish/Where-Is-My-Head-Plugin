using System;
using System.Runtime.InteropServices;
using Dalamud.Hooking;
using WhereIsMyHead.Runtime;

namespace WhereIsMyHead.Hooking;

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate void Game2DUiDrawDelegate(nint uiContext);

internal sealed class Game2DUiHookController : IRenderHookController, IDisposable
{
    private readonly object gate = new();
    private Hook<Game2DUiDrawDelegate>? hook;
    private Game2DUiDrawDelegate? detour;
    private Game2DUiDrawDelegate? original;
    private bool disposed;

    public HookStatus Status { get; private set; } =
        new(InstallState.NotScanned, PolicyState.Enabled, DiagnosticCode.None);

    public string ResolverDiagnostics { get; private set; } = "Not scanned.";

    public void InstallIfNeeded()
    {
        lock (this.gate)
        {
            if (this.disposed || this.hook is not null)
                return;
        }

        try
        {
            var decision = Game2DUiCandidateResolver.Resolve(PluginServices.SigScanner);
            PluginServices.Log.Information("Game2DUI resolver: {Diagnostics}", decision.Diagnostics);
            if (decision.State != InstallState.Resolved || decision.Address == 0)
            {
                lock (this.gate)
                {
                    this.ResolverDiagnostics = decision.Diagnostics;
                    this.Status = this.Status with
                    {
                        InstallState = decision.State,
                        DiagnosticCode = decision.DiagnosticCode,
                    };
                }

                return;
            }

            var localDetour = new Game2DUiDrawDelegate(this.Detour);
            var localHook = PluginServices.Interop.HookFromAddress(decision.Address, localDetour);
            var localOriginal = localHook.Original;
            try
            {
                localHook.Enable();
            }
            catch (Exception ex)
            {
                localHook.Dispose();
                this.Quarantine(DiagnosticCode.EnableHookFailed, ex);
                return;
            }

            lock (this.gate)
            {
                if (this.disposed)
                {
                    localHook.Dispose();
                    return;
                }

                this.detour = localDetour;
                this.hook = localHook;
                this.original = localOriginal;
                this.ResolverDiagnostics = decision.Diagnostics;
                this.Status = this.Status with
                {
                    InstallState = InstallState.Armed,
                    DiagnosticCode = DiagnosticCode.None,
                };
            }
        }
        catch (Exception ex)
        {
            this.Quarantine(DiagnosticCode.CreateHookFailed, ex);
        }
    }

    public void SetPolicy(RenderPolicy policy)
    {
        lock (this.gate)
        {
            this.Status = this.Status with
            {
                PolicyState = policy.Game2DUiEnabled ? PolicyState.Enabled : PolicyState.DisabledByPolicy,
            };
        }
    }

    public void ResetObservation()
    {
        lock (this.gate)
        {
            this.Status = this.Status with
            {
                InstallState = this.hook is null ? InstallState.NotScanned : InstallState.Armed,
                DiagnosticCode = DiagnosticCode.None,
            };
        }
    }

    public void Dispose()
    {
        Hook<Game2DUiDrawDelegate>? localHook;
        lock (this.gate)
        {
            if (this.disposed)
                return;

            this.disposed = true;
            this.Status = this.Status with { PolicyState = PolicyState.Enabled };
            localHook = this.hook;
            this.hook = null;
        }

        localHook?.Dispose();
    }

    private void Detour(nint uiContext)
    {
        try
        {
            bool skip;
            lock (this.gate)
            {
                if (this.disposed)
                {
                    this.original?.Invoke(uiContext);
                    return;
                }

                this.Status = this.Status with
                {
                    InstallState = InstallState.Armed,
                    DiagnosticCode = DiagnosticCode.None,
                };
                skip = this.Status.ShouldSkipGame2DUiDraw;
            }

            if (!skip)
                this.original?.Invoke(uiContext);
        }
        catch (Exception ex)
        {
            this.Quarantine(DiagnosticCode.DetourException, ex);
            this.original?.Invoke(uiContext);
        }
    }

    private void Quarantine(DiagnosticCode code, Exception ex)
    {
        PluginServices.Log.Error(ex, "Game2DUI hook quarantined: {Code}", code);
        lock (this.gate)
        {
            this.Status = this.Status with
            {
                InstallState = InstallState.Quarantined,
                DiagnosticCode = code,
            };
        }
    }
}
