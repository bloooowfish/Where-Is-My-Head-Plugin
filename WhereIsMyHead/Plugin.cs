using System;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using WhereIsMyHead.Gpu;
using WhereIsMyHead.Hooking;
using WhereIsMyHead.Runtime;
using WhereIsMyHead.Ui;

namespace WhereIsMyHead;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/wimh";

    [PluginService] private static IDalamudPluginInterface PluginInterface { get; set; } = null!;
    [PluginService] private static IPluginLog Log { get; set; } = null!;
    [PluginService] private static ICommandManager Commands { get; set; } = null!;
    [PluginService] private static IGameInteropProvider Interop { get; set; } = null!;
    [PluginService] private static ISigScanner SigScanner { get; set; } = null!;
    [PluginService] private static IFramework Framework { get; set; } = null!;
    [PluginService] private static IDtrBar DtrBar { get; set; } = null!;
    [PluginService] private static IClientState ClientState { get; set; } = null!;

    private readonly CancellationTokenSource lifetimeCts = new();
    private readonly Configuration configuration;
    private readonly PluginLogBuffer logBuffer;
    private readonly RenderHookController renderHooks;
    private readonly GpuTelemetryService telemetry;
    private readonly DtrVramEntry dtr;
    private readonly MainWindow mainWindow;
    private readonly LoginLifecycleController loginLifecycle;
    private bool disposed;

    public Plugin()
    {
        PluginServices.Initialize(
            PluginInterface,
            Log,
            Commands,
            Interop,
            SigScanner,
            Framework,
            DtrBar,
            ClientState);

        var uiBuilder = PluginInterface.UiBuilder;

        this.configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        this.logBuffer = new PluginLogBuffer();
        this.renderHooks = new RenderHookController();
        this.renderHooks.SetPolicy(this.configuration.ToRenderPolicy());
        this.telemetry = new GpuTelemetryService(this.lifetimeCts.Token);
        this.dtr = new DtrVramEntry(this.telemetry, this.renderHooks.World3D, this.configuration);
        this.mainWindow = new MainWindow(this.configuration, this.renderHooks, this.telemetry, this.dtr, this.logBuffer);
        this.loginLifecycle = new LoginLifecycleController(
            new DalamudLoginLifecycleSource(ClientState),
            this.renderHooks,
            this.configuration,
            this.lifetimeCts.Token,
            this.mainWindow.Open);

        Commands.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = UiText.CommandHelp,
        });

        uiBuilder.OpenConfigUi += this.mainWindow.Open;
        uiBuilder.OpenMainUi += this.mainWindow.Open;
        uiBuilder.Draw += this.mainWindow.Draw;

        this.logBuffer.Push("Plugin loaded.");
        _ = this.InitializeAsync();
    }

    public void Dispose()
    {
        if (this.disposed)
            return;

        this.disposed = true;
        this.lifetimeCts.Cancel();

        PluginServices.Commands.RemoveHandler(CommandName);
        PluginServices.UiBuilder.OpenConfigUi -= this.mainWindow.Open;
        PluginServices.UiBuilder.OpenMainUi -= this.mainWindow.Open;
        PluginServices.UiBuilder.Draw -= this.mainWindow.Draw;

        this.loginLifecycle.Dispose();
        this.dtr.Dispose();
        this.telemetry.Dispose();
        this.renderHooks.SetPolicy(RenderPolicy.AllEnabled);
        this.renderHooks.Dispose();
        this.configuration.Save();
        this.lifetimeCts.Dispose();
    }

    private bool IsAlive => !this.disposed && !this.lifetimeCts.IsCancellationRequested;

    private async Task InitializeAsync()
    {
        await this.telemetry.InitializeAsync().ConfigureAwait(false);
        if (!this.IsAlive)
            return;

        this.loginLifecycle.Start();
        this.logBuffer.Push("Runtime lifecycle started.");
    }

    private void OnCommand(string command, string arguments)
    {
        _ = command;
        _ = arguments;
        this.mainWindow.Open();
    }
}
