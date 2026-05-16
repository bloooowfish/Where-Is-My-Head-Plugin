using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;

namespace WhereIsMyHead.Gpu;

internal readonly record struct GpuTelemetrySnapshot(
    bool Available,
    uint VramMb,
    uint BudgetMb,
    string Status);

internal sealed class GpuTelemetryService : IDisposable
{
    private const int DxgiMemorySegmentGroupLocal = 0;
    private readonly CancellationToken token;
    private readonly Stopwatch pollTimer = Stopwatch.StartNew();
    private nint dxgiDevice3;
    private nint adapter3;
    private bool disposed;
    private bool unavailableLogged;
    private GpuTelemetrySnapshot snapshot = new(false, 0, 0, "Waiting for UI device.");

    public GpuTelemetryService(CancellationToken token)
    {
        this.token = token;
        PluginServices.Framework.Update += this.OnFrameworkUpdate;
    }

    public event Action<GpuTelemetrySnapshot>? SnapshotChanged;

    public GpuTelemetrySnapshot Snapshot => this.snapshot;

    public async Task InitializeAsync()
    {
        try
        {
            await PluginServices.UiBuilder.RunWhenUiPrepared(
                () =>
                {
                    if (this.disposed || this.token.IsCancellationRequested)
                        return false;

                    this.InitializeFromDeviceHandle(PluginServices.UiBuilder.DeviceHandle);
                    return true;
                },
                runInFrameworkThread: true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            this.SetUnavailable("GPU telemetry unavailable.");
            this.LogUnavailableOnce(ex);
        }
    }

    public unsafe bool RequestTrim(out string message)
    {
        if (this.disposed || this.dxgiDevice3 == 0)
        {
            message = "IDXGIDevice3 is unavailable.";
            return false;
        }

        var vtbl = *(nint**)this.dxgiDevice3;
        var trim = (delegate* unmanaged[Stdcall]<nint, void>)vtbl[ComGuids.IdxgiDevice3TrimIndex];
        trim(this.dxgiDevice3);
        message = "DXGI Trim requested. Driver may defer actual memory release.";
        return true;
    }

    public void Dispose()
    {
        if (this.disposed)
            return;

        this.disposed = true;
        PluginServices.Framework.Update -= this.OnFrameworkUpdate;
        this.Release(ref this.adapter3);
        this.Release(ref this.dxgiDevice3);
    }

    private unsafe void InitializeFromDeviceHandle(nint deviceHandle)
    {
        if (deviceHandle == 0)
        {
            this.SetUnavailable("D3D11 device is not available.");
            return;
        }

        nint dxgiDevice = 0;
        nint adapter = 0;
        try
        {
            var iidDxgiDevice = ComGuids.IidIdxgiDevice;
            if (Marshal.QueryInterface(deviceHandle, in iidDxgiDevice, out dxgiDevice) < 0 || dxgiDevice == 0)
            {
                this.SetUnavailable("IDXGIDevice is unavailable.");
                return;
            }

            var iidDxgiDevice3 = ComGuids.IidIdxgiDevice3;
            if (Marshal.QueryInterface(deviceHandle, in iidDxgiDevice3, out this.dxgiDevice3) < 0)
                this.dxgiDevice3 = 0;

            var vtbl = *(nint**)dxgiDevice;
            var getAdapter = (delegate* unmanaged[Stdcall]<nint, nint*, int>)vtbl[ComGuids.IdxgiDeviceGetAdapterIndex];
            if (getAdapter(dxgiDevice, &adapter) < 0 || adapter == 0)
            {
                this.SetUnavailable("DXGI adapter is unavailable.");
                return;
            }

            var iidAdapter3 = ComGuids.IidIdxgiAdapter3;
            if (Marshal.QueryInterface(adapter, in iidAdapter3, out this.adapter3) < 0 || this.adapter3 == 0)
            {
                this.SetUnavailable("IDXGIAdapter3 is unavailable.");
                return;
            }

            this.PollTelemetry(force: true);
        }
        finally
        {
            this.Release(ref adapter);
            this.Release(ref dxgiDevice);
        }
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        _ = framework;
        if (this.disposed || this.token.IsCancellationRequested)
            return;

        if (this.pollTimer.Elapsed < TimeSpan.FromSeconds(1))
            return;

        this.pollTimer.Restart();
        this.PollTelemetry(force: false);
    }

    private unsafe void PollTelemetry(bool force)
    {
        if (this.adapter3 == 0)
            return;

        try
        {
            DxgiQueryVideoMemoryInfo info = default;
            var vtbl = *(nint**)this.adapter3;
            var query = (delegate* unmanaged[Stdcall]<nint, uint, int, DxgiQueryVideoMemoryInfo*, int>)vtbl[ComGuids.IdxgiAdapter3QueryVideoMemoryInfoIndex];
            if (query(this.adapter3, 0, DxgiMemorySegmentGroupLocal, &info) < 0)
            {
                this.SetUnavailable("QueryVideoMemoryInfo failed.");
                return;
            }

            var next = new GpuTelemetrySnapshot(
                Available: true,
                VramMb: ToMb(info.CurrentUsage),
                BudgetMb: ToMb(info.Budget),
                Status: "OK");
            this.SetSnapshot(next, force);
        }
        catch (Exception ex)
        {
            this.SetUnavailable("GPU telemetry query failed.");
            this.LogUnavailableOnce(ex);
        }
    }

    private void SetUnavailable(string status) =>
        this.SetSnapshot(new GpuTelemetrySnapshot(false, 0, 0, status), force: false);

    private void SetSnapshot(GpuTelemetrySnapshot next, bool force)
    {
        if (!force && this.snapshot.Equals(next))
            return;

        this.snapshot = next;
        this.SnapshotChanged?.Invoke(next);
    }

    private void LogUnavailableOnce(Exception ex)
    {
        if (this.unavailableLogged)
            return;

        this.unavailableLogged = true;
        PluginServices.Log.Warning(ex, "GPU telemetry initialization failed.");
    }

    private unsafe void Release(ref nint comObject)
    {
        if (comObject == 0)
            return;

        var value = comObject;
        comObject = 0;
        var vtbl = *(nint**)value;
        var release = (delegate* unmanaged[Stdcall]<nint, uint>)vtbl[ComGuids.IUnknownReleaseIndex];
        _ = release(value);
    }

    private static uint ToMb(ulong bytes) => (uint)(bytes / 1024UL / 1024UL);

    [StructLayout(LayoutKind.Sequential)]
    private struct DxgiQueryVideoMemoryInfo
    {
        public ulong Budget;
        public ulong CurrentUsage;
        public ulong AvailableForReservation;
        public ulong CurrentReservation;
    }
}
