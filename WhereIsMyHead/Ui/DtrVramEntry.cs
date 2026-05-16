using System;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using WhereIsMyHead.Gpu;
using WhereIsMyHead.Hooking;
using WhereIsMyHead.Runtime;

namespace WhereIsMyHead.Ui;

internal sealed class DtrVramEntry : IDisposable
{
    private const string Title = "WhereIsMyHead.Vram";
    private readonly IDtrBarEntry entry;
    private readonly GpuTelemetryService telemetry;
    private readonly World3DHookController world3D;
    private GpuTelemetrySnapshot lastSnapshot;
    private bool worldSuppressed;
    private DateTime? lastTrimUtc;
    private bool disposed;

    public DtrVramEntry(GpuTelemetryService telemetry, World3DHookController world3D, Configuration configuration)
    {
        this.telemetry = telemetry;
        this.world3D = world3D;
        this.entry = PluginServices.DtrBar.Get(Title);
        this.entry.Shown = configuration.ShowDtrVram;
        telemetry.SnapshotChanged += this.OnSnapshotChanged;
        PluginServices.Framework.Update += this.OnFrameworkUpdate;
        this.Update(telemetry.Snapshot, force: true);
    }

    public void SetVisible(bool visible)
    {
        if (!this.disposed)
            this.entry.Shown = visible;
    }

    public void SetWorldSuppressed(bool suppressed)
    {
        if (this.worldSuppressed == suppressed)
            return;

        this.worldSuppressed = suppressed;
        this.UpdateTooltip();
    }

    public void RecordTrimRequest()
    {
        this.lastTrimUtc = DateTime.UtcNow;
        this.UpdateTooltip();
    }

    public void Dispose()
    {
        if (this.disposed)
            return;

        this.disposed = true;
        this.telemetry.SnapshotChanged -= this.OnSnapshotChanged;
        PluginServices.Framework.Update -= this.OnFrameworkUpdate;
        this.entry.Remove();
    }

    private void OnSnapshotChanged(GpuTelemetrySnapshot snapshot) => this.Update(snapshot, force: false);

    private void OnFrameworkUpdate(IFramework framework)
    {
        _ = framework;
        this.SetWorldSuppressed(this.world3D.Status.ShouldUseWorld3DClearOnly);
    }

    private void Update(GpuTelemetrySnapshot snapshot, bool force)
    {
        if (this.disposed || (!force && snapshot.Equals(this.lastSnapshot)))
            return;

        this.lastSnapshot = snapshot;
        var text = snapshot.Available
            ? $"VRAM: {snapshot.VramMb} / {snapshot.BudgetMb} MB"
            : "VRAM: --";
        this.entry.Text = new SeStringBuilder().Append(text).Build();
        this.UpdateTooltip();
    }

    private void UpdateTooltip()
    {
        if (this.disposed)
            return;

        var builder = new SeStringBuilder();
        builder.Append(this.lastSnapshot.Status);
        if (this.worldSuppressed)
            builder.Append("\n3D world OFF");
        if (this.lastTrimUtc is { } trimTime)
            builder.Append($"\nLast trim request: {trimTime:HH:mm:ss} UTC");
        this.entry.Tooltip = builder.Build();
    }
}
