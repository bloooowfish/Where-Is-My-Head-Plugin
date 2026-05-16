using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using WhereIsMyHead.Runtime;

namespace WhereIsMyHead.Hooking;

internal readonly record struct InternalHookCandidate(
    nint TargetAddress,
    int Score,
    bool CallerOk,
    bool SizeOk,
    bool FieldsOk,
    bool LoopOk);

internal readonly record struct InternalHookResolverDecision(
    InstallState State,
    nint Address,
    int Score,
    DiagnosticCode DiagnosticCode,
    string Diagnostics = "");

internal static unsafe class InternalHookSupport
{
    public static nint ResolveRelativeAddress(nint nextInstruction, int rel32) =>
        nextInstruction + rel32;

    public static nint ResolveAnalysisAddress(nint reportedHit, nint searchBase, nint moduleBase) =>
        reportedHit - (moduleBase - searchBase);

    public static int EstimateFunctionSizeFromRet(ReadOnlySpan<byte> bytes)
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

    public static int EstimateFunctionSizeFromRet(nint bodyAddress, nint textEnd, int maxSize)
    {
        var maxBytes = Math.Min((int)(textEnd - bodyAddress), maxSize + 1);
        if (maxBytes <= 0)
            return 0;

        var bytes = new ReadOnlySpan<byte>((void*)bodyAddress, maxBytes);
        return EstimateFunctionSizeFromRet(bytes);
    }

    public static bool ContainsAddress(nint start, nint end, nint address) =>
        address >= start && address < end;

    public static bool ContainsRange(nint start, nint end, nint address, int length) =>
        length >= 0 && address >= start && address + length <= end;

    public static bool ContainsDword(ReadOnlySpan<byte> bytes, uint value)
    {
        Span<byte> needle = stackalloc byte[4];
        BitConverter.TryWriteBytes(needle, value);
        return bytes.IndexOf(needle) >= 0;
    }

    public static InternalHookResolverDecision ChooseBestCandidate(
        IReadOnlyList<InternalHookCandidate> candidates,
        int threshold,
        string label)
    {
        if (candidates.Count == 0)
            return new(InstallState.Quarantined, 0, 0, DiagnosticCode.PatternNotFound, $"reason=PatternNotFound hook={label} hits=0");

        var bestIndex = 0;
        var bestScore = candidates[0].Score;
        var bestAddress = candidates[0].TargetAddress;
        var hasDistinctTopAddress = false;

        for (var i = 1; i < candidates.Count; i++)
        {
            var score = candidates[i].Score;
            if (score > bestScore)
            {
                bestIndex = i;
                bestScore = score;
                bestAddress = candidates[i].TargetAddress;
                hasDistinctTopAddress = false;
            }
            else if (score == bestScore && candidates[i].TargetAddress != bestAddress)
            {
                hasDistinctTopAddress = true;
            }
        }

        if (hasDistinctTopAddress)
        {
            return new(
                InstallState.Ambiguous,
                0,
                bestScore,
                DiagnosticCode.PatternAmbiguous,
                $"reason=PatternAmbiguous hook={label} bestScore={bestScore}");
        }

        var best = candidates[bestIndex];
        if (best.TargetAddress == 0 || bestScore <= threshold)
        {
            return new(
                InstallState.Quarantined,
                0,
                bestScore,
                DiagnosticCode.RuntimeValidationFailed,
                DescribeFailure("RuntimeValidationFailed", label, best, bestScore));
        }

        return new(
            InstallState.Resolved,
            best.TargetAddress,
            bestScore,
            DiagnosticCode.None,
            DescribeFailure("Resolved", label, best, bestScore));
    }

    public static (nint ModuleStart, nint ModuleEnd, nint TextStart, nint TextEnd, nint ScanToRealDelta) GetAddressContext(ISigScanner sigScanner) =>
        (
            sigScanner.SearchBase,
            sigScanner.SearchBase + sigScanner.Module.ModuleMemorySize,
            sigScanner.TextSectionBase,
            sigScanner.TextSectionBase + sigScanner.TextSectionSize,
            sigScanner.Module.BaseAddress - sigScanner.SearchBase);

    public static string PrefixDiagnostics(ISigScanner sigScanner, int hitCount, InternalHookResolverDecision decision) =>
        $"hits={hitCount} isCopy={sigScanner.IsCopy} searchBase=0x{sigScanner.SearchBase:X} " +
        $"moduleBase=0x{sigScanner.Module.BaseAddress:X} {decision.Diagnostics}";

    private static string DescribeFailure(string reason, string label, InternalHookCandidate candidate, int score)
    {
        var missing = MissingFacts(candidate).ToArray();
        var missingText = missing.Length == 0 ? "none" : string.Join(",", missing);
        return
            $"reason={reason} hook={label} score={score} target=0x{candidate.TargetAddress:X} " +
            $"missing={missingText} facts=CallerOk={candidate.CallerOk};SizeOk={candidate.SizeOk};" +
            $"FieldsOk={candidate.FieldsOk};LoopOk={candidate.LoopOk}";
    }

    private static IEnumerable<string> MissingFacts(InternalHookCandidate candidate)
    {
        if (!candidate.CallerOk)
            yield return nameof(candidate.CallerOk);
        if (!candidate.SizeOk)
            yield return nameof(candidate.SizeOk);
        if (!candidate.FieldsOk)
            yield return nameof(candidate.FieldsOk);
        if (!candidate.LoopOk)
            yield return nameof(candidate.LoopOk);
    }
}
