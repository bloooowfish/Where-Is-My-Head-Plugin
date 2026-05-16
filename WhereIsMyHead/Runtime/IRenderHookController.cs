namespace WhereIsMyHead.Runtime;

internal interface IRenderHookController
{
    void InstallIfNeeded();

    void SetPolicy(RenderPolicy policy);

    void ResetObservation();
}
