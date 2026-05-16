using WhereIsMyHead.Hooking;
using Xunit;

namespace WhereIsMyHead.Tests;

public sealed unsafe class ManagerFlagGuardTests
{
    [Fact]
    public void ClearOnlyFlagOffset_MatchesCurrentRenderManagerClearOnlyFlag()
    {
        Assert.Equal(0x38358, World3DConstants.ClearOnlyFlagOffset);
    }

    [Fact]
    public void RenderStateOffset_MatchesCurrentRenderManagerRenderState()
    {
        Assert.Equal(0x3834C, World3DConstants.RenderStateOffset);
    }

    [Fact]
    public void Dispose_RestoresPreviousByte()
    {
        var memory = stackalloc byte[World3DConstants.ClearOnlyFlagOffset + 1];
        memory[World3DConstants.ClearOnlyFlagOffset] = 7;

        using (new ManagerFlagGuard((nint)memory, arm: true))
            Assert.Equal(1, memory[World3DConstants.ClearOnlyFlagOffset]);

        Assert.Equal(7, memory[World3DConstants.ClearOnlyFlagOffset]);
    }

    [Fact]
    public void ObserveOnlyNestedGuard_SuspendsOuterClearOnlyThenRestoresIt()
    {
        var memory = stackalloc byte[World3DConstants.ClearOnlyFlagOffset + 1];
        memory[World3DConstants.ClearOnlyFlagOffset] = 7;

        using (new ManagerFlagGuard((nint)memory, arm: true))
        {
            Assert.Equal(1, memory[World3DConstants.ClearOnlyFlagOffset]);

            using (new ManagerFlagGuard((nint)memory, arm: false))
                Assert.Equal(7, memory[World3DConstants.ClearOnlyFlagOffset]);

            Assert.Equal(1, memory[World3DConstants.ClearOnlyFlagOffset]);
        }

        Assert.Equal(7, memory[World3DConstants.ClearOnlyFlagOffset]);
    }

    [Fact]
    public void ObserveOnlyNestedGuard_DoesNothingForDifferentManager()
    {
        var outer = stackalloc byte[World3DConstants.ClearOnlyFlagOffset + 1];
        var other = stackalloc byte[World3DConstants.ClearOnlyFlagOffset + 1];
        outer[World3DConstants.ClearOnlyFlagOffset] = 7;
        other[World3DConstants.ClearOnlyFlagOffset] = 9;

        using (new ManagerFlagGuard((nint)outer, arm: true))
        {
            using (new ManagerFlagGuard((nint)other, arm: false))
                Assert.Equal(9, other[World3DConstants.ClearOnlyFlagOffset]);

            Assert.Equal(1, outer[World3DConstants.ClearOnlyFlagOffset]);
        }

        Assert.Equal(7, outer[World3DConstants.ClearOnlyFlagOffset]);
    }

    [Fact]
    public void ObserveOnlyNestedGuard_DoesNothingIfByteWasExternallyModified()
    {
        var memory = stackalloc byte[World3DConstants.ClearOnlyFlagOffset + 1];
        memory[World3DConstants.ClearOnlyFlagOffset] = 7;

        using (new ManagerFlagGuard((nint)memory, arm: true))
        {
            memory[World3DConstants.ClearOnlyFlagOffset] = 3;

            using (new ManagerFlagGuard((nint)memory, arm: false))
                Assert.Equal(3, memory[World3DConstants.ClearOnlyFlagOffset]);
        }

        Assert.Equal(3, memory[World3DConstants.ClearOnlyFlagOffset]);
    }
}
