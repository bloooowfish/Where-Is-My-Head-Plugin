namespace WhereIsMyHead.Runtime;

internal enum InstallState : uint
{
    NotScanned = 0,
    Resolved,
    Ambiguous,
    ValidationFailed,
    Quarantined,
    HookFailed,
    Armed,
}

internal enum PolicyState : uint
{
    Enabled = 0,
    DisabledByPolicy,
}

internal enum DiagnosticCode : uint
{
    None = 0,
    PatternNotFound,
    PatternAmbiguous,
    RuntimeValidationFailed,
    ThreadInstability,
    ManagerInstability,
    ReentrancyDetected,
    CreateHookFailed,
    EnableHookFailed,
    DetourException,
}

internal readonly record struct HookStatus(
    InstallState InstallState,
    PolicyState PolicyState,
    DiagnosticCode DiagnosticCode)
{
    public bool ShouldUseWorld3DClearOnly =>
        this.InstallState == InstallState.Armed &&
        this.PolicyState == PolicyState.DisabledByPolicy;

    public bool ShouldSkipGame2DUiDraw =>
        this.InstallState == InstallState.Armed &&
        this.PolicyState == PolicyState.DisabledByPolicy;

    public bool ShouldSkipWorldSpaceUiUpdate =>
        this.InstallState == InstallState.Armed &&
        this.PolicyState == PolicyState.DisabledByPolicy;
}
