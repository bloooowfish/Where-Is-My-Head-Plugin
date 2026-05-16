using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Interface;

namespace WhereIsMyHead;

internal static class PluginServices
{
    internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    internal static IPluginLog Log { get; private set; } = null!;
    internal static IUiBuilder UiBuilder => PluginInterface.UiBuilder;
    internal static ICommandManager Commands { get; private set; } = null!;
    internal static IGameInteropProvider Interop { get; private set; } = null!;
    internal static ISigScanner SigScanner { get; private set; } = null!;
    internal static IFramework Framework { get; private set; } = null!;
    internal static IDtrBar DtrBar { get; private set; } = null!;
    internal static IClientState ClientState { get; private set; } = null!;

    internal static void Initialize(
        IDalamudPluginInterface pluginInterface,
        IPluginLog log,
        ICommandManager commands,
        IGameInteropProvider interop,
        ISigScanner sigScanner,
        IFramework framework,
        IDtrBar dtrBar,
        IClientState clientState)
    {
        PluginInterface = pluginInterface;
        Log = log;
        Commands = commands;
        Interop = interop;
        SigScanner = sigScanner;
        Framework = framework;
        DtrBar = dtrBar;
        ClientState = clientState;
    }
}
