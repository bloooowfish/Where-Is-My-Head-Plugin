using System;
using Dalamud.Configuration;
using WhereIsMyHead.Runtime;

namespace WhereIsMyHead;

[Serializable]
internal sealed class Configuration : IPluginConfiguration, IRuntimeConfiguration
{
    public int Version { get; set; } = 1;
    public bool OpenOnLogin { get; set; }
    public bool AutoApplyAfterLogin { get; set; } = true;
    public bool Game2DUiEnabled { get; set; } = true;
    public bool WorldSpaceUiEnabled { get; set; }
    public bool World3DEnabled { get; set; }
    public bool ShowDtrVram { get; set; } = true;
    public bool ShowVerboseLog { get; set; }

    public RenderPolicy ToRenderPolicy() => new(
        this.Game2DUiEnabled,
        this.WorldSpaceUiEnabled,
        this.World3DEnabled);

    public void Save() => PluginServices.PluginInterface.SavePluginConfig(this);
}
