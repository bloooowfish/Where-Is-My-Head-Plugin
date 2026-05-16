using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using WhereIsMyHead.Runtime;

namespace WhereIsMyHead.Hooking;

internal readonly record struct World3DValidatorFacts(
    bool CallerOk,
    bool SizeOk,
    bool HasManagerGlobalRef,
    bool HasManagerRenderState3834C,
    bool HasClearOnlyFlag38358,
    bool HasModelRenderer10AF8,
    bool HasTerrainRenderer29D40,
    bool HasMinusOneCompare)
{
    public static World3DValidatorFacts Valid3DWorld => new(
        CallerOk: true,
        SizeOk: true,
        HasManagerGlobalRef: true,
        HasManagerRenderState3834C: true,
        HasClearOnlyFlag38358: true,
        HasModelRenderer10AF8: true,
        HasTerrainRenderer29D40: true,
        HasMinusOneCompare: true);
}

internal readonly record struct World3DCandidate(
    nint ThunkAddress,
    nint BodyAddress,
    nint ManagerGlobalAddress,
    bool ThunkPatternOk,
    bool BodyInText,
    bool ManagerGlobalInModule,
    bool SizeOk,
    bool HasManagerRenderState3834C,
    bool HasClearOnlyFlag38358,
    bool HasModelRenderer10AF8,
    bool HasTerrainRenderer29D40,
    bool HasMinusOneCompare,
    int? ScoreOverride = null)
{
    public int Score => this.ScoreOverride ??
        (this.ThunkPatternOk ? 2 : 0) +
        (this.ManagerGlobalInModule ? 2 : 0) +
        (this.BodyInText ? 2 : 0) +
        (this.SizeOk ? 2 : 0) +
        (this.ManagerGlobalAddress != 0 ? 1 : 0) +
        (this.HasManagerRenderState3834C ? 1 : 0) +
        (this.HasClearOnlyFlag38358 ? 4 : 0) +
        (this.HasModelRenderer10AF8 ? 3 : 0) +
        (this.HasTerrainRenderer29D40 ? 3 : 0) +
        (this.HasMinusOneCompare ? 1 : 0);

    public World3DValidatorFacts Facts => new(
        CallerOk: this.ThunkPatternOk && this.BodyInText && this.ManagerGlobalInModule,
        SizeOk: this.SizeOk,
        HasManagerGlobalRef: this.ManagerGlobalAddress != 0 && this.ManagerGlobalInModule,
        HasManagerRenderState3834C: this.HasManagerRenderState3834C,
        HasClearOnlyFlag38358: this.HasClearOnlyFlag38358,
        HasModelRenderer10AF8: this.HasModelRenderer10AF8,
        HasTerrainRenderer29D40: this.HasTerrainRenderer29D40,
        HasMinusOneCompare: this.HasMinusOneCompare);

    public static World3DCandidate ValidForTests(nint bodyAddress, int scoreBonus = 0, int? scoreOverride = null) =>
        new(
            ThunkAddress: 0x1000,
            BodyAddress: bodyAddress,
            ManagerGlobalAddress: 0x5000,
            ThunkPatternOk: true,
            BodyInText: true,
            ManagerGlobalInModule: true,
            SizeOk: true,
            HasManagerRenderState3834C: true,
            HasClearOnlyFlag38358: true,
            HasModelRenderer10AF8: true,
            HasTerrainRenderer29D40: true,
            HasMinusOneCompare: true,
            ScoreOverride: scoreOverride ?? (scoreBonus == 0 ? null : 21 + scoreBonus));
}

internal readonly record struct World3DResolverDecision(
    InstallState State,
    nint Address,
    int Score,
    DiagnosticCode DiagnosticCode,
    string Diagnostics = "");

internal static unsafe class World3DCandidateResolver
{
    private static readonly byte[][] MinusOneComparePatterns =
    [
        [0x83, 0xF8, 0xFF],
        [0x83, 0xF9, 0xFF],
        [0x83, 0xFA, 0xFF],
        [0x83, 0xFB, 0xFF],
        [0x83, 0xB8, 0x3C, 0x03, 0x04, 0x00, 0xFF],
        [0x83, 0xB9, 0x3C, 0x03, 0x04, 0x00, 0xFF],
        [0x83, 0xBA, 0x3C, 0x03, 0x04, 0x00, 0xFF],
        [0x83, 0xBB, 0x3C, 0x03, 0x04, 0x00, 0xFF],
        [0x83, 0xBD, 0x3C, 0x03, 0x04, 0x00, 0xFF],
        [0x83, 0xBE, 0x3C, 0x03, 0x04, 0x00, 0xFF],
        [0x83, 0xBF, 0x3C, 0x03, 0x04, 0x00, 0xFF],
        [0x41, 0x83, 0xBD, 0x4C, 0x83, 0x03, 0x00, 0xFF],
    ];

    public static nint ResolveRelativeAddress(nint instruction, nint nextInstruction, int rel32)
    {
        _ = instruction;
        return nextInstruction + rel32;
    }

    public static nint ResolveAnalysisAddress(nint reportedHit, nint searchBase, nint moduleBase) =>
        reportedHit - (moduleBase - searchBase);

    public static bool Validate3DWorldFacts(World3DValidatorFacts facts) =>
        facts.CallerOk &&
        facts.HasManagerGlobalRef &&
        facts.HasManagerRenderState3834C &&
        facts.HasClearOnlyFlag38358 &&
        facts.HasModelRenderer10AF8 &&
        facts.HasTerrainRenderer29D40 &&
        facts.HasMinusOneCompare;

    public static World3DResolverDecision ChooseBestCandidate(IReadOnlyList<World3DCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            return new(
                InstallState.Quarantined,
                Address: 0,
                Score: 0,
                DiagnosticCode.PatternNotFound,
                "reason=PatternNotFound hits=0");
        }

        var bestIndex = 0;
        var bestScore = candidates[0].Score;
        var bestAddress = candidates[0].BodyAddress;
        var hasDistinctTopAddress = false;

        for (var i = 1; i < candidates.Count; i++)
        {
            var score = candidates[i].Score;
            if (score > bestScore)
            {
                bestIndex = i;
                bestScore = score;
                bestAddress = candidates[i].BodyAddress;
                hasDistinctTopAddress = false;
            }
            else if (score == bestScore && candidates[i].BodyAddress != bestAddress)
            {
                hasDistinctTopAddress = true;
            }
        }

        if (hasDistinctTopAddress)
        {
            return new(
                InstallState.Ambiguous,
                Address: 0,
                Score: bestScore,
                DiagnosticCode.PatternAmbiguous,
                $"reason=PatternAmbiguous bestScore={bestScore}");
        }

        var best = candidates[bestIndex];
        if (best.BodyAddress == 0)
        {
            return new(
                InstallState.Quarantined,
                Address: 0,
                Score: bestScore,
                DiagnosticCode.RuntimeValidationFailed,
                DescribeFailure("BodyAddressZero", best, bestScore));
        }

        if (bestScore <= World3DSignatures.LocatorThreshold)
        {
            return new(
                InstallState.Quarantined,
                Address: 0,
                Score: bestScore,
                DiagnosticCode.RuntimeValidationFailed,
                DescribeFailure("ScoreBelowThreshold", best, bestScore));
        }

        if (!Validate3DWorldFacts(best.Facts))
        {
            return new(
                InstallState.Quarantined,
                Address: 0,
                Score: bestScore,
                DiagnosticCode.RuntimeValidationFailed,
                DescribeFailure("ValidationFactsMissing", best, bestScore));
        }

        return new(
            InstallState.Resolved,
            Address: best.BodyAddress,
            Score: bestScore,
            DiagnosticCode.None,
            DescribeFailure("Resolved", best, bestScore));
    }

    public static World3DResolverDecision Resolve(ISigScanner sigScanner)
    {
        var hits = sigScanner.ScanAllText(World3DSignatures.TaskRenderGraphicsRenderPattern);
        if (hits.Length == 0)
        {
            return new(InstallState.Quarantined, 0, 0, DiagnosticCode.PatternNotFound, "reason=PatternNotFound hits=0");
        }

        var moduleStart = sigScanner.SearchBase;
        var moduleEnd = sigScanner.SearchBase + sigScanner.Module.ModuleMemorySize;
        var textStart = sigScanner.TextSectionBase;
        var textEnd = sigScanner.TextSectionBase + sigScanner.TextSectionSize;
        var scanToRealDelta = sigScanner.Module.BaseAddress - sigScanner.SearchBase;
        var candidates = hits
            .Select(hit => AnalyzeThunkCandidate(
                ResolveAnalysisAddress(hit, sigScanner.SearchBase, sigScanner.Module.BaseAddress),
                moduleStart,
                moduleEnd,
                textStart,
                textEnd,
                scanToRealDelta))
            .ToArray();
        var decision = ChooseBestCandidate(candidates);
        return decision with
        {
            Diagnostics =
                $"hits={hits.Length} isCopy={sigScanner.IsCopy} searchBase=0x{sigScanner.SearchBase:X} " +
                $"moduleBase=0x{sigScanner.Module.BaseAddress:X} {decision.Diagnostics}",
        };
    }

    private static World3DCandidate AnalyzeThunkCandidate(
        nint thunkAddress,
        nint moduleStart,
        nint moduleEnd,
        nint textStart,
        nint textEnd,
        nint scanToRealDelta)
    {
        var realThunkAddress = thunkAddress + scanToRealDelta;
        if (!ContainsRange(textStart, textEnd, thunkAddress, World3DSignatures.ThunkLength))
            return default(World3DCandidate) with { ThunkAddress = realThunkAddress };

        var thunk = new ReadOnlySpan<byte>((void*)thunkAddress, World3DSignatures.ThunkLength);
        var thunkPatternOk = thunk[0] == 0x48 && thunk[1] == 0x8B && thunk[2] == 0x0D && thunk[7] == 0xE9;
        if (!thunkPatternOk)
            return default(World3DCandidate) with { ThunkAddress = realThunkAddress };

        var managerGlobalScanAddress = ResolveRelativeAddress(thunkAddress + 3, thunkAddress + 7, BitConverter.ToInt32(thunk.Slice(3, 4)));
        var managerGlobalInModule = ContainsAddress(moduleStart, moduleEnd, managerGlobalScanAddress) &&
                                    !ContainsAddress(textStart, textEnd, managerGlobalScanAddress);

        var bodyScanAddress = ResolveRelativeAddress(thunkAddress + 7, thunkAddress + World3DSignatures.ThunkLength, BitConverter.ToInt32(thunk.Slice(8, 4)));
        var bodyInText = ContainsAddress(textStart, textEnd, bodyScanAddress);
        var bodySize = bodyInText ? EstimateFunctionSizeFromRet(bodyScanAddress, textEnd) : 0;
        var sizeOk = bodySize >= World3DSignatures.FunctionMinSize && bodySize <= World3DSignatures.FunctionMaxSize;
        var facts = CollectFacts(
            bodyScanAddress,
            textEnd,
            bodySize,
            thunkPatternOk && bodyInText && managerGlobalInModule);

        return new(
            realThunkAddress,
            bodyScanAddress + scanToRealDelta,
            managerGlobalScanAddress + scanToRealDelta,
            thunkPatternOk,
            bodyInText,
            managerGlobalInModule,
            sizeOk && facts.SizeOk,
            facts.HasManagerRenderState3834C,
            facts.HasClearOnlyFlag38358,
            facts.HasModelRenderer10AF8,
            facts.HasTerrainRenderer29D40,
            facts.HasMinusOneCompare);
    }

    private static World3DValidatorFacts CollectFacts(nint bodyAddress, nint textEnd, int estimatedSize, bool callerOk)
    {
        if (!callerOk)
            return default;

        var sizeOk = estimatedSize >= World3DSignatures.FunctionMinSize &&
                     estimatedSize <= World3DSignatures.FunctionMaxSize;
        var scanSize = estimatedSize == 0 || estimatedSize > World3DSignatures.ValidationWindowBytes
            ? World3DSignatures.ValidationWindowBytes
            : estimatedSize;

        if (!ContainsRange(bodyAddress, textEnd, bodyAddress, scanSize))
            return new(callerOk, sizeOk, true, false, false, false, false, false);

        var bytes = new ReadOnlySpan<byte>((void*)bodyAddress, scanSize);
        return CollectFacts(bytes, callerOk, sizeOk);
    }

    internal static World3DValidatorFacts CollectFacts(ReadOnlySpan<byte> bytes, bool callerOk, bool sizeOk) =>
        new(
            callerOk,
            sizeOk,
            HasManagerGlobalRef: true,
            HasManagerRenderState3834C: ContainsDword(bytes, (uint)World3DConstants.RenderStateOffset),
            HasClearOnlyFlag38358: ContainsDword(bytes, (uint)World3DConstants.ClearOnlyFlagOffset),
            HasModelRenderer10AF8: ContainsDword(bytes, 0x00010AF8u),
            HasTerrainRenderer29D40: ContainsDword(bytes, 0x00029D40u),
            HasMinusOneCompare: ContainsMinusOneCompare(bytes));

    private static bool ContainsAddress(nint start, nint end, nint address) =>
        address >= start && address < end;

    private static bool ContainsRange(nint start, nint end, nint address, int length) =>
        length >= 0 && address >= start && address + length <= end;

    internal static int EstimateFunctionSizeFromRet(ReadOnlySpan<byte> bytes)
    {
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] == 0xC3)
                return i + 1;

            if (bytes[i] == 0xC2 && i + 2 < bytes.Length)
                return i + 3;
        }

        return 0;
    }

    private static int EstimateFunctionSizeFromRet(nint bodyAddress, nint textEnd)
    {
        var maxBytes = Math.Min((int)(textEnd - bodyAddress), World3DSignatures.FunctionMaxSize + 1);
        if (maxBytes <= 0)
            return 0;

        var bytes = new ReadOnlySpan<byte>((void*)bodyAddress, maxBytes);
        return EstimateFunctionSizeFromRet(bytes);
    }

    private static bool ContainsDword(ReadOnlySpan<byte> bytes, uint value)
    {
        Span<byte> needle = stackalloc byte[4];
        BitConverter.TryWriteBytes(needle, value);
        return bytes.IndexOf(needle) >= 0;
    }

    private static bool ContainsMinusOneCompare(ReadOnlySpan<byte> bytes)
    {
        if (ContainsDword(bytes, 0xFFFFFFFFu))
            return true;

        foreach (var pattern in MinusOneComparePatterns)
        {
            if (bytes.IndexOf(pattern) >= 0)
                return true;
        }

        return false;
    }

    private static string DescribeFailure(string reason, World3DCandidate candidate, int score)
    {
        var facts = candidate.Facts;
        var missingFacts = MissingFacts(facts).ToArray();
        var missing = missingFacts.Length == 0 ? "none" : string.Join(",", missingFacts);
        return
            $"reason={reason} score={score} thunk=0x{candidate.ThunkAddress:X} body=0x{candidate.BodyAddress:X} " +
            $"managerGlobal=0x{candidate.ManagerGlobalAddress:X} missing={missing} " +
            $"facts={DescribeFacts(facts)}";
    }

    private static string DescribeFacts(World3DValidatorFacts facts) =>
        $"{nameof(World3DValidatorFacts.CallerOk)}={facts.CallerOk};" +
        $"{nameof(World3DValidatorFacts.SizeOk)}={facts.SizeOk};" +
        $"{nameof(World3DValidatorFacts.HasManagerGlobalRef)}={facts.HasManagerGlobalRef};" +
        $"{nameof(World3DValidatorFacts.HasManagerRenderState3834C)}={facts.HasManagerRenderState3834C};" +
        $"{nameof(World3DValidatorFacts.HasClearOnlyFlag38358)}={facts.HasClearOnlyFlag38358};" +
        $"{nameof(World3DValidatorFacts.HasModelRenderer10AF8)}={facts.HasModelRenderer10AF8};" +
        $"{nameof(World3DValidatorFacts.HasTerrainRenderer29D40)}={facts.HasTerrainRenderer29D40};" +
        $"{nameof(World3DValidatorFacts.HasMinusOneCompare)}={facts.HasMinusOneCompare}";

    private static IEnumerable<string> MissingFacts(World3DValidatorFacts facts)
    {
        if (!facts.CallerOk)
            yield return nameof(World3DValidatorFacts.CallerOk);
        if (!facts.HasManagerGlobalRef)
            yield return nameof(World3DValidatorFacts.HasManagerGlobalRef);
        if (!facts.HasManagerRenderState3834C)
            yield return nameof(World3DValidatorFacts.HasManagerRenderState3834C);
        if (!facts.HasClearOnlyFlag38358)
            yield return nameof(World3DValidatorFacts.HasClearOnlyFlag38358);
        if (!facts.HasModelRenderer10AF8)
            yield return nameof(World3DValidatorFacts.HasModelRenderer10AF8);
        if (!facts.HasTerrainRenderer29D40)
            yield return nameof(World3DValidatorFacts.HasTerrainRenderer29D40);
        if (!facts.HasMinusOneCompare)
            yield return nameof(World3DValidatorFacts.HasMinusOneCompare);
    }
}
