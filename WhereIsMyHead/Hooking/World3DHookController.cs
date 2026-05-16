using System;
using System.Runtime.InteropServices;
using Dalamud.Hooking;
using WhereIsMyHead.Runtime;

namespace WhereIsMyHead.Hooking;

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate nuint TaskRenderGraphicsRenderDelegate(nint manager);

internal sealed class World3DHookController : IRenderHookController, IDisposable
{
    private readonly object gate = new();
    private readonly World3DObservation observation = new();
    private Hook<TaskRenderGraphicsRenderDelegate>? hook;
    private TaskRenderGraphicsRenderDelegate? detour;
    private TaskRenderGraphicsRenderDelegate? original;
    private bool disposed;

    [ThreadStatic]
    private static bool reentrancyGuard;

    public HookStatus Status { get; private set; } =
        new(InstallState.NotScanned, PolicyState.DisabledByPolicy, DiagnosticCode.None);

    public RenderPolicy Policy { get; private set; } = RenderPolicy.Default;

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
            var decision = World3DCandidateResolver.Resolve(PluginServices.SigScanner);
            PluginServices.Log.Information("World3D resolver: {Diagnostics}", decision.Diagnostics);
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

            var localDetour = new TaskRenderGraphicsRenderDelegate(this.Detour);
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
                    InstallState = InstallState.Resolved,
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
            this.Policy = policy;
            this.Status = this.Status with
            {
                PolicyState = policy.World3DEnabled ? PolicyState.Enabled : PolicyState.DisabledByPolicy,
            };
        }
    }

    public void ResetObservation()
    {
        lock (this.gate)
        {
            this.observation.Reset();
            this.Status = this.Status with
            {
                InstallState = this.hook is null ? InstallState.NotScanned : InstallState.Resolved,
                DiagnosticCode = DiagnosticCode.None,
            };
        }
    }

    public void Dispose()
    {
        Hook<TaskRenderGraphicsRenderDelegate>? localHook;
        lock (this.gate)
        {
            if (this.disposed)
                return;

            this.disposed = true;
            this.Policy = RenderPolicy.AllEnabled;
            this.Status = this.Status with { PolicyState = PolicyState.Enabled };
            localHook = this.hook;
            this.hook = null;
        }

        localHook?.Dispose();
    }

    private nuint Detour(nint manager)
    {
        var reentered = reentrancyGuard;
        reentrancyGuard = true;

        try
        {
            bool useClearOnly;
            lock (this.gate)
            {
                if (this.disposed)
                    return this.original?.Invoke(manager) ?? 0;

                this.observation.Record(NativeMethods.GetCurrentThreadId(), (nuint)manager, reentered);
                var installState = this.observation.DecideInstallState();
                this.Status = this.Status with
                {
                    InstallState = installState,
                    DiagnosticCode = this.observation.DiagnosticCode,
                };

                useClearOnly = manager != 0 && this.Status.ShouldUseWorld3DClearOnly;
            }

            using var guard = new ManagerFlagGuard(manager, useClearOnly);
            return this.original?.Invoke(manager) ?? 0;
        }
        catch (Exception ex)
        {
            this.Quarantine(DiagnosticCode.DetourException, ex);
            return this.original?.Invoke(manager) ?? 0;
        }
        finally
        {
            reentrancyGuard = false;
        }
    }

    private void Quarantine(DiagnosticCode code, Exception ex)
    {
        PluginServices.Log.Error(ex, "World3D hook quarantined: {Code}", code);
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
