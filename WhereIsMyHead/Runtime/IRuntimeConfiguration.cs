namespace WhereIsMyHead.Runtime;

internal interface IRuntimeConfiguration
{
    bool AutoApplyAfterLogin { get; }

    bool Game2DUiEnabled { get; }

    bool WorldSpaceUiEnabled { get; }

    bool World3DEnabled { get; }

    bool OpenOnLogin { get; }
}
