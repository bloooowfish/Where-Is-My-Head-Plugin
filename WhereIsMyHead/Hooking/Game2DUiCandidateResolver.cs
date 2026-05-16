using System;
using System.Linq;
using Dalamud.Plugin.Services;

namespace WhereIsMyHead.Hooking;

internal static unsafe class Game2DUiCandidateResolver
{
    private static ReadOnlySpan<byte> WrapperPrefix =>
    [
        0x48, 0x83, 0xEC, 0x28,
        0x48, 0x8B, 0x01,
        0xFF, 0x50, 0x40,
        0x48, 0x8B, 0xC8,
        0x48, 0x83, 0xC4, 0x28,
    ];

    public static InternalHookResolverDecision Resolve(ISigScanner sigScanner)
    {
        var hits = sigScanner.ScanAllText(Game2DUiSignatures.WrapperPattern);
        if (hits.Length == 0)
            return new(WhereIsMyHead.Runtime.InstallState.Quarantined, 0, 0, WhereIsMyHead.Runtime.DiagnosticCode.PatternNotFound, "reason=PatternNotFound hook=Game2DUI hits=0");

        var context = InternalHookSupport.GetAddressContext(sigScanner);
        var candidates = hits
            .Select(hit => AnalyzeThunkCandidate(
                InternalHookSupport.ResolveAnalysisAddress(hit, sigScanner.SearchBase, sigScanner.Module.BaseAddress),
                context.TextStart,
                context.TextEnd,
                context.ScanToRealDelta))
            .ToArray();
        var decision = InternalHookSupport.ChooseBestCandidate(candidates, Game2DUiSignatures.LocatorThreshold, "Game2DUI");
        return decision with { Diagnostics = InternalHookSupport.PrefixDiagnostics(sigScanner, hits.Length, decision) };
    }

    private static InternalHookCandidate AnalyzeThunkCandidate(
        nint thunkAddress,
        nint textStart,
        nint textEnd,
        nint scanToRealDelta)
    {
        if (!InternalHookSupport.ContainsRange(textStart, textEnd, thunkAddress, Game2DUiSignatures.WrapperLength))
            return default;

        var wrapper = new ReadOnlySpan<byte>((void*)thunkAddress, Game2DUiSignatures.WrapperLength);
        var thunkPatternOk = wrapper[..WrapperPrefix.Length].SequenceEqual(WrapperPrefix) && wrapper[17] == 0xE9;
        if (!thunkPatternOk)
            return default;

        var bodyScanAddress = InternalHookSupport.ResolveRelativeAddress(
            thunkAddress + Game2DUiSignatures.WrapperLength,
            BitConverter.ToInt32(wrapper.Slice(18, 4)));
        var bodyInText = InternalHookSupport.ContainsAddress(textStart, textEnd, bodyScanAddress);
        var bodySize = bodyInText
            ? InternalHookSupport.EstimateFunctionSizeFromRet(bodyScanAddress, textEnd, Game2DUiSignatures.FunctionMaxSize)
            : 0;
        var sizeOk = bodySize >= Game2DUiSignatures.FunctionMinSize && bodySize <= Game2DUiSignatures.FunctionMaxSize;
        var facts = bodyInText
            ? CollectFacts(bodyScanAddress, textEnd, bodySize)
            : default;
        var callerOk = thunkPatternOk && bodyInText;
        var fieldsOk = facts.HasDisableFlag &&
                       facts.HasField130 &&
                       facts.HasField2C0 &&
                       facts.HasField9C98 &&
                       facts.HasField60F0;
        var loopOk = facts.HasLayerLoop13;
        var score =
            (thunkPatternOk ? 2 : 0) +
            (bodyInText ? 2 : 0) +
            (sizeOk ? 2 : 0) +
            (facts.HasDisableFlag ? 1 : 0) +
            (facts.HasField130 ? 1 : 0) +
            (facts.HasField2C0 ? 1 : 0) +
            (facts.HasField9C98 ? 1 : 0) +
            (facts.HasField60F0 ? 1 : 0) +
            (loopOk ? 1 : 0);

        return new(
            callerOk && sizeOk && fieldsOk && loopOk ? bodyScanAddress + scanToRealDelta : 0,
            score,
            callerOk,
            sizeOk,
            fieldsOk,
            loopOk);
    }

    private static Game2DUiFacts CollectFacts(nint bodyAddress, nint textEnd, int estimatedSize)
    {
        var scanSize = estimatedSize == 0 || estimatedSize > Game2DUiSignatures.ValidationWindowBytes
            ? Game2DUiSignatures.ValidationWindowBytes
            : estimatedSize;
        if (!InternalHookSupport.ContainsRange(bodyAddress, textEnd, bodyAddress, scanSize))
            return default;

        var bytes = new ReadOnlySpan<byte>((void*)bodyAddress, scanSize);
        return CollectFacts(bytes);
    }

    internal static Game2DUiFacts CollectFacts(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> layerLoop13 = stackalloc byte[] { 0xBD, 0x0C, 0x00, 0x00, 0x00 };
        return new(
            InternalHookSupport.ContainsDword(bytes, 0x000082D4u),
            InternalHookSupport.ContainsDword(bytes, 0x00000130u),
            InternalHookSupport.ContainsDword(bytes, 0x000002C0u),
            InternalHookSupport.ContainsDword(bytes, 0x00009C98u),
            InternalHookSupport.ContainsDword(bytes, 0x000060F0u),
            bytes.IndexOf(layerLoop13) >= 0);
    }

    internal readonly record struct Game2DUiFacts(
        bool HasDisableFlag,
        bool HasField130,
        bool HasField2C0,
        bool HasField9C98,
        bool HasField60F0,
        bool HasLayerLoop13);
}
