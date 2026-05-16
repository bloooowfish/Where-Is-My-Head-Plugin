using Xunit;
using WhereIsMyHead.Runtime;

namespace WhereIsMyHead.Tests;

public sealed class RenderPolicyTests
{
    [Fact]
    public void DefaultPolicy_MatchesExistingMonitorDefaults()
    {
        var policy = RenderPolicy.Default;

        Assert.True(policy.Game2DUiEnabled);
        Assert.False(policy.WorldSpaceUiEnabled);
        Assert.False(policy.World3DEnabled);
    }

    [Fact]
    public void ArmedAndDisabledByPolicy_IsSuppressed()
    {
        var status = new HookStatus(InstallState.Armed, PolicyState.DisabledByPolicy, DiagnosticCode.None);

        Assert.True(status.ShouldUseWorld3DClearOnly);
    }

    [Fact]
    public void ArmedAndDisabledByPolicy_SkipsGame2DUiDraw()
    {
        var status = new HookStatus(InstallState.Armed, PolicyState.DisabledByPolicy, DiagnosticCode.None);

        Assert.True(status.ShouldSkipGame2DUiDraw);
    }

    [Fact]
    public void ArmedAndDisabledByPolicy_SkipsWorldSpaceUiUpdate()
    {
        var status = new HookStatus(InstallState.Armed, PolicyState.DisabledByPolicy, DiagnosticCode.None);

        Assert.True(status.ShouldSkipWorldSpaceUiUpdate);
    }

    [Fact]
    public void Quarantined_NeverSuppresses()
    {
        var status = new HookStatus(InstallState.Quarantined, PolicyState.DisabledByPolicy, DiagnosticCode.ThreadInstability);

        Assert.False(status.ShouldUseWorld3DClearOnly);
        Assert.False(status.ShouldSkipGame2DUiDraw);
        Assert.False(status.ShouldSkipWorldSpaceUiUpdate);
    }
}
