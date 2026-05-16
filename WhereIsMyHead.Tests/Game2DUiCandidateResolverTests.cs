using System;
using WhereIsMyHead.Hooking;
using Xunit;

namespace WhereIsMyHead.Tests;

public sealed class Game2DUiCandidateResolverTests
{
    [Fact]
    public void WrapperPattern_UsesDalamudWildcardBytes()
    {
        var tokens = Game2DUiSignatures.WrapperPattern.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var compactLength = Game2DUiSignatures.WrapperPattern.Replace(" ", string.Empty).Length / 2;

        Assert.Equal(Game2DUiSignatures.WrapperLength, tokens.Length);
        Assert.Equal(Game2DUiSignatures.WrapperLength, compactLength);
        Assert.All(tokens, token => Assert.True(
            token.Length == 2,
            $"Dalamud signatures are parsed after whitespace removal; wildcard bytes must be '??', not '{token}'."));
    }

    [Fact]
    public void CollectFacts_AcceptsCurrentDisableFlagOffset()
    {
        Span<byte> bytes = stackalloc byte[0x80];
        WriteDword(bytes, 0x00, 0x000082D4u);
        WriteDword(bytes, 0x10, 0x00000130u);
        WriteDword(bytes, 0x20, 0x000002C0u);
        WriteDword(bytes, 0x30, 0x00009C98u);
        WriteDword(bytes, 0x40, 0x000060F0u);
        bytes[0x60] = 0xBD;
        bytes[0x61] = 0x0C;
        bytes[0x62] = 0x00;
        bytes[0x63] = 0x00;
        bytes[0x64] = 0x00;

        var facts = Game2DUiCandidateResolver.CollectFacts(bytes);

        Assert.True(facts.HasDisableFlag);
        Assert.True(facts.HasField130);
        Assert.True(facts.HasField2C0);
        Assert.True(facts.HasField9C98);
        Assert.True(facts.HasField60F0);
        Assert.True(facts.HasLayerLoop13);
    }

    private static void WriteDword(Span<byte> bytes, int offset, uint value)
    {
        Span<byte> encoded = stackalloc byte[4];
        BitConverter.TryWriteBytes(encoded, value);
        encoded.CopyTo(bytes[offset..]);
    }
}
