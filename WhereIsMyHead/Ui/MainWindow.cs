using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using WhereIsMyHead.Gpu;
using WhereIsMyHead.Hooking;
using WhereIsMyHead.Runtime;

namespace WhereIsMyHead.Ui;

internal sealed class MainWindow
{
    private readonly Configuration configuration;
    private readonly RenderHookController renderHooks;
    private readonly GpuTelemetryService telemetry;
    private readonly DtrVramEntry dtr;
    private readonly PluginLogBuffer logBuffer;
    private bool isOpen;

    public MainWindow(
        Configuration configuration,
        RenderHookController renderHooks,
        GpuTelemetryService telemetry,
        DtrVramEntry dtr,
        PluginLogBuffer logBuffer)
    {
        this.configuration = configuration;
        this.renderHooks = renderHooks;
        this.telemetry = telemetry;
        this.dtr = dtr;
        this.logBuffer = logBuffer;
    }

    public void Open() => this.isOpen = true;

    public void Draw()
    {
        if (!this.isOpen)
            return;

        ImGui.SetNextWindowSize(new Vector2(760, 430), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin(UiText.WindowTitle, ref this.isOpen))
        {
            ImGui.End();
            return;
        }

        this.DrawStatusPanel();
        ImGui.Separator();
        this.DrawTabs();
        ImGui.End();
    }

    private void DrawStatusPanel()
    {
        var status = this.renderHooks.World3D.Status;
        var snapshot = this.telemetry.Snapshot;

        ImGui.Text($"Hook: {status.InstallState}");
        ImGui.Text($"Policy: {status.PolicyState}");
        ImGui.Text($"Diagnostic: {status.DiagnosticCode}");
        ImGui.Text(snapshot.Available
            ? $"VRAM: {snapshot.VramMb} / {snapshot.BudgetMb} MB"
            : $"VRAM: -- ({snapshot.Status})");

        ImGui.TextUnformatted("Internal render policy");
        this.DrawRenderPolicyCheckboxes();

        var showDtr = this.configuration.ShowDtrVram;
        if (ImGui.Checkbox("Show VRAM in server info bar", ref showDtr))
        {
            this.configuration.ShowDtrVram = showDtr;
            this.configuration.Save();
            this.dtr.SetVisible(showDtr);
            this.logBuffer.Push($"DTR VRAM {(showDtr ? "shown" : "hidden")}");
        }

        var autoApply = this.configuration.AutoApplyAfterLogin;
        if (ImGui.Checkbox("Auto apply after login", ref autoApply))
        {
            this.configuration.AutoApplyAfterLogin = autoApply;
            this.configuration.Save();
            this.logBuffer.Push($"Auto apply after login {(autoApply ? "enabled" : "disabled")}");
        }

        var openOnLogin = this.configuration.OpenOnLogin;
        if (ImGui.Checkbox("Open window on login", ref openOnLogin))
        {
            this.configuration.OpenOnLogin = openOnLogin;
            this.configuration.Save();
            this.logBuffer.Push($"Open on login {(openOnLogin ? "enabled" : "disabled")}");
        }

        var showVerbose = this.configuration.ShowVerboseLog;
        if (ImGui.Checkbox("Show verbose log tab", ref showVerbose))
        {
            this.configuration.ShowVerboseLog = showVerbose;
            this.configuration.Save();
            this.logBuffer.Push($"Verbose log tab {(showVerbose ? "shown" : "hidden")}");
        }

        ImGui.SameLine();
        if (ImGui.Button("Manual DXGI Trim"))
        {
            var ok = this.telemetry.RequestTrim(out var message);
            if (ok)
                this.dtr.RecordTrimRequest();
            this.logBuffer.Push(message);
        }

        ImGui.TextWrapped(UiText.TrimHelp);
    }

    private void DrawTabs()
    {
        if (!ImGui.BeginTabBar("WimhTabs"))
            return;

        if (ImGui.BeginTabItem("Summary"))
        {
            this.DrawLogLines(verbose: false);
            ImGui.EndTabItem();
        }

        if (this.configuration.ShowVerboseLog && ImGui.BeginTabItem("Verbose"))
        {
            this.DrawLogLines(verbose: true);
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Diagnostics"))
        {
            var status = this.renderHooks.World3D.Status;
            var game2DStatus = this.renderHooks.Game2DUi.Status;
            var worldSpaceStatus = this.renderHooks.WorldSpaceUi.Status;
            var snapshot = this.telemetry.Snapshot;
            ImGui.Text($"InstallState: {status.InstallState}");
            ImGui.Text($"PolicyState: {status.PolicyState}");
            ImGui.Text($"DiagnosticCode: {status.DiagnosticCode}");
            ImGui.Text($"Game2DUI: {game2DStatus.InstallState} / {game2DStatus.PolicyState} / {game2DStatus.DiagnosticCode}");
            ImGui.Text($"WorldSpaceUI: {worldSpaceStatus.InstallState} / {worldSpaceStatus.PolicyState} / {worldSpaceStatus.DiagnosticCode}");
            ImGui.Text($"Game2DUiEnabled: {this.configuration.Game2DUiEnabled}");
            ImGui.Text($"WorldSpaceUiEnabled: {this.configuration.WorldSpaceUiEnabled}");
            ImGui.Text($"World3DEnabled: {this.configuration.World3DEnabled}");
            ImGui.Text($"ShouldUseWorld3DClearOnly: {status.ShouldUseWorld3DClearOnly}");
            ImGui.TextWrapped($"World3D resolver: {this.renderHooks.World3D.ResolverDiagnostics}");
            ImGui.TextWrapped($"Game2DUI resolver: {this.renderHooks.Game2DUi.ResolverDiagnostics}");
            ImGui.TextWrapped($"WorldSpaceUI resolver: {this.renderHooks.WorldSpaceUi.ResolverDiagnostics}");
            ImGui.Text($"GPU status: {snapshot.Status}");
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void DrawRenderPolicyCheckboxes()
    {
        var game2DUiEnabled = this.configuration.Game2DUiEnabled;
        if (ImGui.Checkbox("Game 2D UI", ref game2DUiEnabled))
        {
            this.configuration.Game2DUiEnabled = game2DUiEnabled;
            this.ApplyRenderPolicy($"Game 2D UI {(game2DUiEnabled ? "ON" : "OFF")}");
        }

        var worldSpaceUiEnabled = this.configuration.WorldSpaceUiEnabled;
        if (ImGui.Checkbox("World-space UI", ref worldSpaceUiEnabled))
        {
            this.configuration.WorldSpaceUiEnabled = worldSpaceUiEnabled;
            this.ApplyRenderPolicy($"World-space UI {(worldSpaceUiEnabled ? "ON" : "OFF")}");
        }

        var world3DEnabled = this.configuration.World3DEnabled;
        if (ImGui.Checkbox("3D world", ref world3DEnabled))
        {
            this.configuration.World3DEnabled = world3DEnabled;
            this.ApplyRenderPolicy($"3D world {(world3DEnabled ? "ON" : "OFF")}");
        }
    }

    private void ApplyRenderPolicy(string logLine)
    {
        this.configuration.Save();
        this.renderHooks.SetPolicy(this.configuration.ToRenderPolicy());
        this.logBuffer.Push(logLine);
    }

    private void DrawLogLines(bool verbose)
    {
        var lines = this.logBuffer.Snapshot();
        if (!verbose && lines.Length > 50)
            lines = lines[^50..];

        if (ImGui.BeginChild("LogLines", new Vector2(0, 180), true))
        {
            foreach (var line in lines)
                ImGui.TextUnformatted(line);
        }

        ImGui.EndChild();
    }
}
