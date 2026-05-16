using System;
using WhereIsMyHead.Hooking;
using WhereIsMyHead.Runtime;
using Xunit;

namespace WhereIsMyHead.Tests;

public sealed class World3DCandidateResolverTests
{
    [Fact]
    public void TaskRenderGraphicsRenderPattern_UsesDalamudWildcardBytes()
    {
        var tokens = World3DSignatures.TaskRenderGraphicsRenderPattern.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var compactLength = World3DSignatures.TaskRenderGraphicsRenderPattern.Replace(" ", string.Empty).Length / 2;

        Assert.Equal(World3DSignatures.ThunkLength, tokens.Length);
        Assert.Equal(World3DSignatures.ThunkLength, compactLength);
        Assert.All(tokens, token => Assert.True(
            token.Length == 2,
            $"Dalamud signatures are parsed after whitespace removal; wildcard bytes must be '??', not '{token}'."));
    }

    [Fact]
    public void ResolveAnalysisAddress_ConvertsRealHitBackToCopiedSearchAddress()
    {
        var searchBase = (nint)0x1000_0000;
        var moduleBase = (nint)0x7000_0000;
        var realHit = moduleBase + 0x1234;

        var analysisAddress = World3DCandidateResolver.ResolveAnalysisAddress(realHit, searchBase, moduleBase);

        Assert.Equal(searchBase + 0x1234, analysisAddress);
    }

    [Theory]
    [InlineData(nameof(World3DValidatorFacts.CallerOk))]
    [InlineData(nameof(World3DValidatorFacts.HasManagerGlobalRef))]
    [InlineData(nameof(World3DValidatorFacts.HasManagerRenderState3834C))]
    [InlineData(nameof(World3DValidatorFacts.HasClearOnlyFlag38358))]
    [InlineData(nameof(World3DValidatorFacts.HasModelRenderer10AF8))]
    [InlineData(nameof(World3DValidatorFacts.HasTerrainRenderer29D40))]
    [InlineData(nameof(World3DValidatorFacts.HasMinusOneCompare))]
    public void Validate3DWorldFacts_RequiresEverySafetyFact(string missingFact)
    {
        var facts = World3DValidatorFacts.Valid3DWorld;
        facts = missingFact switch
        {
            nameof(World3DValidatorFacts.CallerOk) => facts with { CallerOk = false },
            nameof(World3DValidatorFacts.HasManagerGlobalRef) => facts with { HasManagerGlobalRef = false },
            nameof(World3DValidatorFacts.HasManagerRenderState3834C) => facts with { HasManagerRenderState3834C = false },
            nameof(World3DValidatorFacts.HasClearOnlyFlag38358) => facts with { HasClearOnlyFlag38358 = false },
            nameof(World3DValidatorFacts.HasModelRenderer10AF8) => facts with { HasModelRenderer10AF8 = false },
            nameof(World3DValidatorFacts.HasTerrainRenderer29D40) => facts with { HasTerrainRenderer29D40 = false },
            nameof(World3DValidatorFacts.HasMinusOneCompare) => facts with { HasMinusOneCompare = false },
            _ => throw new InvalidOperationException(missingFact),
        };

        Assert.False(World3DCandidateResolver.Validate3DWorldFacts(facts));
    }

    [Fact]
    public void Validate3DWorldFacts_AllowsUnreliableRetScanWhenRenderManagerMarkersMatch()
    {
        var facts = World3DValidatorFacts.Valid3DWorld with { SizeOk = false };

        Assert.True(World3DCandidateResolver.Validate3DWorldFacts(facts));
    }

    [Fact]
    public void ChooseBestCandidate_AllowsSameAddressTie()
    {
        var first = World3DCandidate.ValidForTests(bodyAddress: 0x2000);
        var second = World3DCandidate.ValidForTests(bodyAddress: 0x2000);

        var decision = World3DCandidateResolver.ChooseBestCandidate([first, second]);

        Assert.Equal(InstallState.Resolved, decision.State);
        Assert.Equal(0x2000, decision.Address);
    }

    [Fact]
    public void ChooseBestCandidate_RejectsDistinctTopAddressTieAsAmbiguous()
    {
        var first = World3DCandidate.ValidForTests(bodyAddress: 0x2000);
        var second = World3DCandidate.ValidForTests(bodyAddress: 0x3000);

        var decision = World3DCandidateResolver.ChooseBestCandidate([first, second]);

        Assert.Equal(InstallState.Ambiguous, decision.State);
        Assert.Equal(0, decision.Address);
    }

    [Fact]
    public void ChooseBestCandidate_QuarantinesScoreAtThreshold()
    {
        var candidate = World3DCandidate.ValidForTests(
            bodyAddress: 0x2000,
            scoreOverride: World3DSignatures.LocatorThreshold);

        var decision = World3DCandidateResolver.ChooseBestCandidate([candidate]);

        Assert.Equal(InstallState.Quarantined, decision.State);
        Assert.Equal(0, decision.Address);
    }

    [Fact]
    public void ChooseBestCandidate_QuarantinesWhenValidationFails()
    {
        var candidate = World3DCandidate.ValidForTests(bodyAddress: 0x2000) with
        {
            HasClearOnlyFlag38358 = false,
        };

        var decision = World3DCandidateResolver.ChooseBestCandidate([candidate]);

        Assert.Equal(InstallState.Quarantined, decision.State);
        Assert.Equal(DiagnosticCode.RuntimeValidationFailed, decision.DiagnosticCode);
    }

    [Fact]
    public void ChooseBestCandidate_ReportsFailedValidationFacts()
    {
        var candidate = World3DCandidate.ValidForTests(bodyAddress: 0x2000) with
        {
            HasClearOnlyFlag38358 = false,
            HasModelRenderer10AF8 = false,
        };

        var decision = World3DCandidateResolver.ChooseBestCandidate([candidate]);

        Assert.Contains(
            $"missing={nameof(World3DValidatorFacts.HasClearOnlyFlag38358)},{nameof(World3DValidatorFacts.HasModelRenderer10AF8)}",
            decision.Diagnostics);
        Assert.Contains($"{nameof(World3DValidatorFacts.HasMinusOneCompare)}=True", decision.Diagnostics);
    }

    [Fact]
    public void ChooseBestCandidate_PrefersCurrentRenderManagerMarkersOverRenderTargetManagerShape()
    {
        var renderTargetManager = World3DCandidate.ValidForTests(bodyAddress: 0x3000) with
        {
            HasManagerRenderState3834C = false,
            HasClearOnlyFlag38358 = false,
            HasModelRenderer10AF8 = false,
            HasTerrainRenderer29D40 = false,
            HasMinusOneCompare = false,
        };
        var renderManager = World3DCandidate.ValidForTests(bodyAddress: 0x2000) with
        {
            SizeOk = false,
        };

        var decision = World3DCandidateResolver.ChooseBestCandidate([renderTargetManager, renderManager]);

        Assert.Equal(InstallState.Resolved, decision.State);
        Assert.Equal(0x2000, decision.Address);
    }

    [Fact]
    public void ChooseBestCandidate_PrefersActualRenderBodyOverUpdateGraphicsPass()
    {
        var updateGraphicsPass = World3DCandidate.ValidForTests(bodyAddress: 0x3000) with
        {
            HasClearOnlyFlag38358 = false,
            HasModelRenderer10AF8 = false,
            HasTerrainRenderer29D40 = false,
        };
        var actualRenderBody = World3DCandidate.ValidForTests(bodyAddress: 0x2000) with
        {
            SizeOk = false,
        };

        var decision = World3DCandidateResolver.ChooseBestCandidate([updateGraphicsPass, actualRenderBody]);

        Assert.Equal(InstallState.Resolved, decision.State);
        Assert.Equal(0x2000, decision.Address);
    }

    [Fact]
    public void CollectFacts_AcceptsCurrentRenderDrawBodyMarkers()
    {
        Span<byte> bytes = stackalloc byte[0x180];
        WriteDword(bytes, 0x10, 0x0003834Cu);
        WriteDword(bytes, 0x30, 0x00038358u);
        WriteDword(bytes, 0xA0, 0x00029D40u);
        WriteDword(bytes, 0x140, 0x00010AF8u);
        ReadOnlySpan<byte> minusOneCompare = [0x41, 0x83, 0xBD, 0x4C, 0x83, 0x03, 0x00, 0xFF];
        minusOneCompare.CopyTo(bytes[0x20..]);

        var facts = World3DCandidateResolver.CollectFacts(bytes, callerOk: true, sizeOk: false);

        Assert.True(facts.CallerOk);
        Assert.False(facts.SizeOk);
        Assert.True(facts.HasManagerGlobalRef);
        Assert.True(facts.HasManagerRenderState3834C);
        Assert.True(facts.HasClearOnlyFlag38358);
        Assert.True(facts.HasModelRenderer10AF8);
        Assert.True(facts.HasTerrainRenderer29D40);
        Assert.True(facts.HasMinusOneCompare);
    }

    [Fact]
    public void EstimateFunctionSizeFromRet_RecognizesRetImmediate()
    {
        Span<byte> bytes = stackalloc byte[0x40];
        bytes.Fill(0x90);
        bytes[0x20] = 0xC2;
        bytes[0x21] = 0x10;
        bytes[0x22] = 0x00;

        var size = World3DCandidateResolver.EstimateFunctionSizeFromRet(bytes);

        Assert.Equal(0x23, size);
    }

    private static void WriteDword(Span<byte> bytes, int offset, uint value)
    {
        Span<byte> encoded = stackalloc byte[4];
        BitConverter.TryWriteBytes(encoded, value);
        encoded.CopyTo(bytes[offset..]);
    }
}
