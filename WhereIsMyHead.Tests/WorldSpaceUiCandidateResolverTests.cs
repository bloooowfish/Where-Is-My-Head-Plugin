using System;
using WhereIsMyHead.Hooking;
using Xunit;

namespace WhereIsMyHead.Tests;

public sealed class WorldSpaceUiCandidateResolverTests
{
    [Fact]
    public void UpdatePattern_UsesDalamudWildcardBytes()
    {
        var tokens = WorldSpaceUiSignatures.UpdatePattern.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var compactLength = WorldSpaceUiSignatures.UpdatePattern.Replace(" ", string.Empty).Length / 2;

        Assert.Equal(WorldSpaceUiSignatures.UpdatePatternLength, tokens.Length);
        Assert.Equal(WorldSpaceUiSignatures.UpdatePatternLength, compactLength);
        Assert.All(tokens, token => Assert.True(
            token.Length == 2,
            $"Dalamud signatures are parsed after whitespace removal; wildcard bytes must be '??', not '{token}'."));
    }
}
