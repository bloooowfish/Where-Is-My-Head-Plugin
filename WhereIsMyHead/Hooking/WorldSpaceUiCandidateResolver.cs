using System;
using System.Linq;
using Dalamud.Plugin.Services;

namespace WhereIsMyHead.Hooking;

internal static unsafe class WorldSpaceUiCandidateResolver
{
    private static ReadOnlySpan<byte> UpdatePrefix =>
    [
        0x40, 0x53,
        0x56,
        0x57,
        0x48, 0x83, 0xEC, 0x30,
        0x33, 0xF6,
        0x48, 0x89, 0x6C, 0x24, 0x50,
        0x4C, 0x89, 0x74, 0x24, 0x60,
        0x48, 0x8B, 0xF9,
        0x4C, 0x89, 0x7C, 0x24, 0x68,
        0x89, 0xB1,
    ];

    public static InternalHookResolverDecision Resolve(ISigScanner sigScanner)
    {
        var hits = sigScanner.ScanAllText(WorldSpaceUiSignatures.UpdatePattern);
        if (hits.Length == 0)
            return new(WhereIsMyHead.Runtime.InstallState.Quarantined, 0, 0, WhereIsMyHead.Runtime.DiagnosticCode.PatternNotFound, "reason=PatternNotFound hook=WorldSpaceUI hits=0");

        var context = InternalHookSupport.GetAddressContext(sigScanner);
        var candidates = hits
            .Select(hit => AnalyzeFunctionCandidate(
                InternalHookSupport.ResolveAnalysisAddress(hit, sigScanner.SearchBase, sigScanner.Module.BaseAddress),
                context.TextStart,
                context.TextEnd,
                context.ScanToRealDelta))
            .ToArray();
        var decision = InternalHookSupport.ChooseBestCandidate(candidates, WorldSpaceUiSignatures.LocatorThreshold, "WorldSpaceUI");
        return decision with { Diagnostics = InternalHookSupport.PrefixDiagnostics(sigScanner, hits.Length, decision) };
    }

    private static InternalHookCandidate AnalyzeFunctionCandidate(
        nint functionAddress,
        nint textStart,
        nint textEnd,
        nint scanToRealDelta)
    {
        if (!InternalHookSupport.ContainsRange(textStart, textEnd, functionAddress, UpdatePrefix.Length))
            return default;

        var prefix = new ReadOnlySpan<byte>((void*)functionAddress, UpdatePrefix.Length);
        var patternOk = prefix.SequenceEqual(UpdatePrefix);
        if (!patternOk)
            return default;

        var bodyInText = InternalHookSupport.ContainsAddress(textStart, textEnd, functionAddress);
        var estimatedSize = bodyInText
            ? InternalHookSupport.EstimateFunctionSizeFromRet(functionAddress, textEnd, WorldSpaceUiSignatures.FunctionMaxSize)
            : 0;
        var hasValidationWindow = InternalHookSupport.ContainsRange(
            textStart,
            textEnd,
            functionAddress,
            WorldSpaceUiSignatures.ValidationWindowBytes);
        var sizeOk = (estimatedSize >= WorldSpaceUiSignatures.FunctionMinSize &&
                      estimatedSize <= WorldSpaceUiSignatures.FunctionMaxSize) ||
                     hasValidationWindow;
        var facts = bodyInText
            ? CollectFacts(functionAddress, textStart, textEnd, estimatedSize, hasValidationWindow)
            : default;
        var callerOk = patternOk && bodyInText;
        var fieldsOk = facts.HasZeroingBlock;
        var loopOk = facts.HasUpdateGameObjectsCall && facts.HasFieldMarkerLoop;
        var score =
            (patternOk ? 2 : 0) +
            (bodyInText ? 2 : 0) +
            (sizeOk ? 2 : 0) +
            (facts.HasZeroingBlock ? 2 : 0) +
            (facts.HasUpdateGameObjectsCall ? 1 : 0) +
            (facts.HasFieldMarkerLoop ? 1 : 0);

        return new(
            callerOk && sizeOk && fieldsOk && loopOk ? functionAddress + scanToRealDelta : 0,
            score,
            callerOk,
            sizeOk,
            fieldsOk,
            loopOk);
    }

    private static WorldSpaceUiFacts CollectFacts(
        nint bodyAddress,
        nint textStart,
        nint textEnd,
        int estimatedSize,
        bool hasValidationWindow)
    {
        var scanSize = hasValidationWindow
            ? WorldSpaceUiSignatures.ValidationWindowBytes
            : estimatedSize == 0 || estimatedSize > WorldSpaceUiSignatures.ValidationWindowBytes
                ? WorldSpaceUiSignatures.ValidationWindowBytes
                : estimatedSize;
        if (!InternalHookSupport.ContainsRange(textStart, textEnd, bodyAddress, scanSize))
            return default;

        var bytes = new ReadOnlySpan<byte>((void*)bodyAddress, scanSize);
        var hasZeroingBlock =
            InternalHookSupport.ContainsDword(bytes, 0x00014CD8u) &&
            InternalHookSupport.ContainsDword(bytes, 0x00014E70u) &&
            InternalHookSupport.ContainsDword(bytes, 0x00017428u);
        var hasUpdateCall = HasForwardTextCall(bodyAddress, textStart, textEnd, bytes[..Math.Min(bytes.Length, 0x80)]);
        ReadOnlySpan<byte> fieldMarkerLoopLimit = stackalloc byte[] { 0x83, 0xFE, 0x08, 0x72 };
        var hasFieldMarkerLoop =
            InternalHookSupport.ContainsDword(bytes, 0x00017444u) &&
            bytes.IndexOf(fieldMarkerLoopLimit) >= 0;

        return new(hasZeroingBlock, hasUpdateCall, hasFieldMarkerLoop);
    }

    private static bool HasForwardTextCall(nint bodyAddress, nint textStart, nint textEnd, ReadOnlySpan<byte> bytes)
    {
        for (var i = 0; i + 5 <= bytes.Length; i++)
        {
            if (bytes[i] != 0xE8)
                continue;

            var target = InternalHookSupport.ResolveRelativeAddress(bodyAddress + i + 5, BitConverter.ToInt32(bytes.Slice(i + 1, 4)));
            if (InternalHookSupport.ContainsAddress(textStart, textEnd, target) && target > bodyAddress)
                return true;
        }

        return false;
    }

    private readonly record struct WorldSpaceUiFacts(
        bool HasZeroingBlock,
        bool HasUpdateGameObjectsCall,
        bool HasFieldMarkerLoop);
}
