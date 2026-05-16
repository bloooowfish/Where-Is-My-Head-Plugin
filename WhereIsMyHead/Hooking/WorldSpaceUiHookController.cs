using System;
using System.Runtime.InteropServices;
using Dalamud.Hooking;
using WhereIsMyHead.Runtime;

namespace WhereIsMyHead.Hooking;

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate byte WorldSpaceUiUpdateDelegate(nint ui3DModule);

internal sealed unsafe class WorldSpaceUiHookController : IRenderHookController, IDisposable
{
    private const int NameplateCountOffset = 0x14CD8;
    private const int AllianceCountOffset = 0x14E70;
    private const int PartyCountOffset = 0x15348;
    private const int TargetCountOffset = 0x15570;
    private const int TargetPtrOffset = 0x15578;
    private const int UnknownCountOffset = 0x15E80;
    private const int FieldMarkerBaseOffset = 0x17444;
    private const int FieldMarkerStride = 0x20;
    private const int FieldMarkerCountOffset = 0x17428;
    private const int FieldMarkerCursorOffset = 0x1742C;
    private const int FieldMarkerCount = 8;

    private readonly object gate = new();
    private Hook<WorldSpaceUiUpdateDelegate>? hook;
    private WorldSpaceUiUpdateDelegate? detour;
    private WorldSpaceUiUpdateDelegate? original;
    private bool disposed;

    public HookStatus Status { get; private set; } =
        new(InstallState.NotScanned, PolicyState.DisabledByPolicy, DiagnosticCode.None);

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
            var decision = WorldSpaceUiCandidateResolver.Resolve(PluginServices.SigScanner);
            PluginServices.Log.Information("WorldSpaceUI resolver: {Diagnostics}", decision.Diagnostics);
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

            var localDetour = new WorldSpaceUiUpdateDelegate(this.Detour);
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
                PolicyState = policy.WorldSpaceUiEnabled ? PolicyState.Enabled : PolicyState.DisabledByPolicy,
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
        Hook<WorldSpaceUiUpdateDelegate>? localHook;
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

    private byte Detour(nint ui3DModule)
    {
        try
        {
            bool skip;
            lock (this.gate)
            {
                if (this.disposed)
                    return this.original?.Invoke(ui3DModule) ?? 0;

                this.Status = this.Status with
                {
                    InstallState = InstallState.Armed,
                    DiagnosticCode = DiagnosticCode.None,
                };
                skip = this.Status.ShouldSkipWorldSpaceUiUpdate;
            }

            if (!skip)
                return this.original?.Invoke(ui3DModule) ?? 0;

            ResetWorldSpaceUiState(ui3DModule);
            return 0;
        }
        catch (Exception ex)
        {
            this.Quarantine(DiagnosticCode.DetourException, ex);
            return this.original?.Invoke(ui3DModule) ?? 0;
        }
    }

    private static void ResetWorldSpaceUiState(nint ui3DModule)
    {
        if (ui3DModule == 0)
            return;

        var baseAddress = (byte*)ui3DModule;
        ZeroDword(baseAddress, NameplateCountOffset);
        ZeroDword(baseAddress, AllianceCountOffset);
        ZeroDword(baseAddress, PartyCountOffset);
        ZeroDword(baseAddress, TargetCountOffset);
        *(ulong*)(baseAddress + TargetPtrOffset) = 0;
        ZeroDword(baseAddress, UnknownCountOffset);
        ZeroDword(baseAddress, FieldMarkerCountOffset);
        ZeroDword(baseAddress, FieldMarkerCursorOffset);

        for (var i = 0; i < FieldMarkerCount; i++)
            *(baseAddress + FieldMarkerBaseOffset + (FieldMarkerStride * i)) = 0;
    }

    private static void ZeroDword(byte* baseAddress, int offset) =>
        *(uint*)(baseAddress + offset) = 0;

    private void Quarantine(DiagnosticCode code, Exception ex)
    {
        PluginServices.Log.Error(ex, "WorldSpaceUI hook quarantined: {Code}", code);
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
