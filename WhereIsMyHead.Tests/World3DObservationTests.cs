using WhereIsMyHead.Hooking;
using WhereIsMyHead.Runtime;
using Xunit;

namespace WhereIsMyHead.Tests;

public sealed class World3DObservationTests
{
    [Fact]
    public void StableSingleThreadSingleManager_ArmsAfterThreshold()
    {
        var observation = new World3DObservation();

        for (var i = 0; i < World3DObservation.ArmFrameThreshold; i++)
            observation.Record(threadId: 123, manager: 0x1000, reentered: false);

        Assert.Equal(InstallState.Armed, observation.DecideInstallState());
    }

    [Fact]
    public void MultipleManagers_Quarantines()
    {
        var observation = new World3DObservation();

        observation.Record(123, 0x1000, false);
        observation.Record(123, 0x2000, false);

        Assert.Equal(InstallState.Quarantined, observation.DecideInstallState());
        Assert.Equal(DiagnosticCode.ManagerInstability, observation.DiagnosticCode);
    }

    [Fact]
    public void Reentrancy_Quarantines()
    {
        var observation = new World3DObservation();

        observation.Record(123, 0x1000, true);

        Assert.Equal(InstallState.Quarantined, observation.DecideInstallState());
        Assert.Equal(DiagnosticCode.ReentrancyDetected, observation.DiagnosticCode);
    }

    [Fact]
    public void Reset_ClearsInstabilityAndReturnsToObservation()
    {
        var observation = new World3DObservation();
        observation.Record(123, 0x1000, false);
        observation.Record(456, 0x1000, false);
        Assert.Equal(InstallState.Quarantined, observation.DecideInstallState());

        observation.Reset();
        observation.Record(123, 0x1000, false);

        Assert.Equal(InstallState.Resolved, observation.DecideInstallState());
        Assert.Equal(DiagnosticCode.None, observation.DiagnosticCode);
    }
}

