using System.Diagnostics.CodeAnalysis;

namespace Vibranium.Self;

public struct ManifestBundle
{
    public Vec<byte> CoreNativeIr;
    public Vec<byte> StdNativeIr;
    public bool HasStdUnit;
    public bool IsLibraryOnly;
    public StringBuf TrustedCoreManifest;
    public StringBuf TrustedStdManifest;

    public ManifestBundle(int dummy)
    {
        CoreNativeIr = new Vec<byte>();
        StdNativeIr = new Vec<byte>();
        HasStdUnit = false;
        IsLibraryOnly = false;
        TrustedCoreManifest = new StringBuf(128);
        TrustedStdManifest = new StringBuf(128);
    }
}

public static class ManifestLoader
{
    private const int MaxSourceBytes = 16 * 1024 * 1024;

    public static bool LoadInputs(
        in Vec<ByteSlice> inputSlices,
        in BytePool cliPool,
        ReadOnlySpan<byte> sysroot,
        bool includeStd,
        ref SourceTable sources,
        ref ManifestBundle bundle,
        ref StringBuf errMsg)
    {
        var visitedManifests = new StrMap<int>(16);
        var visitingManifests = new StrMap<int>(16);
        var seenSources = new StrMap<int>(64);

        // Resolve the runtime trust boundary before traversing user-controlled
        // manifests. Unit names and native directives cannot establish trust.
        ReadOnlySpan<byte> firstInput = cliPool.Get(inputSlices[0]);
        var selectedCore = new StringBuf(128);
        var selectedStd = new StringBuf(128);
        if (!FindSysrootManifests(firstInput, sysroot, ref selectedCore, ref selectedStd) ||
            !PathText.Normalize(selectedCore.AsUtf8(), ref bundle.TrustedCoreManifest))
        {
            errMsg.Append("vib: unable to locate Vibranium.Core/vib.manifest (pass --sysroot <dir>)"u8);
            return false;
        }
        if (selectedStd.Length > 0 && !PathText.Normalize(selectedStd.AsUtf8(), ref bundle.TrustedStdManifest))
        {
            errMsg.Append("vib: invalid selected Vibranium.Std manifest path"u8);
            return false;
        }

        if (inputSlices.Count == 1)
        {
            ReadOnlySpan<byte> singleInput = cliPool.Get(inputSlices[0]);
            var resolvedManifest = new StringBuf(128);
            if (TryResolveAsManifestPath(singleInput, ref resolvedManifest))
            {
                int rootUnitKind = UnitKinds.App;
                if (!VisitManifest(
                    resolvedManifest.AsUtf8(),
                    includeStd,
                    isRoot: true,
                    ref rootUnitKind,
                    ref visitedManifests,
                    ref visitingManifests,
                    ref seenSources,
                    ref sources,
                    ref bundle,
                    ref errMsg))
                {
                    return false;
                }
                if (rootUnitKind == UnitKinds.Core || rootUnitKind == UnitKinds.Std)
                {
                    bundle.IsLibraryOnly = true;
                }
                return EnsureCoreRuntimeLoaded(singleInput, sysroot, includeStd, ref visitedManifests, ref visitingManifests, ref seenSources, ref sources, ref bundle, ref errMsg);
            }
        }

        if (!EnsureCoreRuntimeLoaded(firstInput, sysroot, includeStd, ref visitedManifests, ref visitingManifests, ref seenSources, ref sources, ref bundle, ref errMsg))
        {
            return false;
        }

        for (int i = 0; i < inputSlices.Count; i++)
        {
            ReadOnlySpan<byte> rawPath = cliPool.Get(inputSlices[i]);
            if (!LoadSingleNonManifestInput(rawPath, ref seenSources, ref sources, ref errMsg))
            {
                return false;
            }
        }

        return true;
    }

    private static bool EnsureCoreRuntimeLoaded(
        ReadOnlySpan<byte> hintInput,
        ReadOnlySpan<byte> sysroot,
        bool includeStd,
        ref StrMap<int> visitedManifests,
        ref StrMap<int> visitingManifests,
        ref StrMap<int> seenSources,
        ref SourceTable sources,
        ref ManifestBundle bundle,
        ref StringBuf errMsg)
    {
        if (bundle.CoreNativeIr.Count > 0 && (!includeStd || bundle.HasStdUnit))
        {
            return true;
        }

        var coreManifest = new StringBuf(128);
        var stdManifest = new StringBuf(128);
        coreManifest.Append(bundle.TrustedCoreManifest.AsUtf8());
        stdManifest.Append(bundle.TrustedStdManifest.AsUtf8());

        if (bundle.CoreNativeIr.Count == 0)
        {
            int dummyKind = UnitKinds.Core;
            if (!VisitManifest(
                coreManifest.AsUtf8(),
                includeStd: false,
                isRoot: false,
                ref dummyKind,
                ref visitedManifests,
                ref visitingManifests,
                ref seenSources,
                ref sources,
                ref bundle,
                ref errMsg))
            {
                return false;
            }
        }

        if (includeStd && !bundle.HasStdUnit && stdManifest.Length > 0)
        {
            int dummyStd = UnitKinds.Std;
            if (!VisitManifest(
                stdManifest.AsUtf8(),
                includeStd: true,
                isRoot: false,
                ref dummyStd,
                ref visitedManifests,
                ref visitingManifests,
                ref seenSources,
                ref sources,
                ref bundle,
                ref errMsg))
            {
                return false;
            }
        }

        return true;
    }

    private static bool FindSysrootManifests(
        ReadOnlySpan<byte> hintInput,
        ReadOnlySpan<byte> sysroot,
        ref StringBuf coreManifest,
        ref StringBuf stdManifest)
    {
        if (sysroot.Length > 0)
        {
            if (TryProbeSysrootBase(sysroot, ref coreManifest, ref stdManifest))
            {
                return true;
            }
        }

        var hintDir = new StringBuf(128);
        GetParentDir(hintInput, ref hintDir);
        if (WalkUpForSysroot(hintDir.AsUtf8(), ref coreManifest, ref stdManifest))
        {
            return true;
        }

        return WalkUpForSysroot("."u8, ref coreManifest, ref stdManifest);
    }

    private static bool WalkUpForSysroot(
        ReadOnlySpan<byte> startDir,
        ref StringBuf coreManifest,
        ref StringBuf stdManifest)
    {
        var cur = new StringBuf(128);
        cur.Append(startDir);
        var next = new StringBuf(128);
        for (int depth = 0; depth < 8; depth++)
        {
            if (TryProbeSysrootBase(cur.AsUtf8(), ref coreManifest, ref stdManifest))
            {
                return true;
            }
            next.Clear();
            JoinPath(cur.AsUtf8(), ".."u8, ref next);
            var norm = new StringBuf(128);
            if (!PathText.Normalize(next.AsUtf8(), ref norm))
            {
                break;
            }
            if (Bytes.Eq(norm.AsUtf8(), cur.AsUtf8()))
            {
                break;
            }
            cur.Clear();
            cur.Append(norm.AsUtf8());
        }
        return false;
    }

    private static bool TryProbeSysrootBase(
        ReadOnlySpan<byte> baseDir,
        ref StringBuf coreManifest,
        ref StringBuf stdManifest)
    {
        var candCore = new StringBuf(128);
        JoinPath(baseDir, "Vibranium.Core/vib.manifest"u8, ref candCore);
        if (FileExists(candCore.AsUtf8()))
        {
            coreManifest.Clear();
            coreManifest.Append(candCore.AsUtf8());
            stdManifest.Clear();
            var candStd = new StringBuf(128);
            JoinPath(baseDir, "Vibranium.Std/vib.manifest"u8, ref candStd);
            if (FileExists(candStd.AsUtf8()))
            {
                stdManifest.Append(candStd.AsUtf8());
            }
            return true;
        }

        candCore.Clear();
        JoinPath(baseDir, "src/Vibranium.Core/vib.manifest"u8, ref candCore);
        if (FileExists(candCore.AsUtf8()))
        {
            coreManifest.Clear();
            coreManifest.Append(candCore.AsUtf8());
            stdManifest.Clear();
            var candStd = new StringBuf(128);
            JoinPath(baseDir, "src/Vibranium.Std/vib.manifest"u8, ref candStd);
            if (FileExists(candStd.AsUtf8()))
            {
                stdManifest.Append(candStd.AsUtf8());
            }
            return true;
        }

        return false;
    }

    private static bool TryResolveAsManifestPath(ReadOnlySpan<byte> inputPath, ref StringBuf outManifest)
    {
        if (EndsWith(inputPath, ".manifest"u8))
        {
            return PathText.Normalize(inputPath, ref outManifest) && FileExists(outManifest.AsUtf8());
        }

        if (EndsWith(inputPath, ".csproj"u8))
        {
            var projDir = new StringBuf(128);
            GetParentDir(inputPath, ref projDir);
            var cand = new StringBuf(128);
            JoinPath(projDir.AsUtf8(), "vib.manifest"u8, ref cand);
            if (PathText.Normalize(cand.AsUtf8(), ref outManifest) && FileExists(outManifest.AsUtf8()))
            {
                return true;
            }
            return false;
        }

        if (IsDirectoryPath(inputPath))
        {
            var cand = new StringBuf(128);
            JoinPath(inputPath, "vib.manifest"u8, ref cand);
            if (PathText.Normalize(cand.AsUtf8(), ref outManifest) && FileExists(outManifest.AsUtf8()))
            {
                return true;
            }
        }

        return false;
    }

    private static bool LoadSingleNonManifestInput(
        ReadOnlySpan<byte> rawPath,
        ref StrMap<int> seenSources,
        ref SourceTable sources,
        ref StringBuf errMsg)
    {
        var target = new StringBuf(128);
        if (EndsWith(rawPath, ".csproj"u8))
        {
            var projDir = new StringBuf(128);
            GetParentDir(rawPath, ref projDir);
            if (Bytes.Eq(LastSegment(rawPath), "Vibranium.Std.csproj"u8))
            {
                return true;
            }
            JoinPath(projDir.AsUtf8(), "Program.cs"u8, ref target);
        }
        else if (IsDirectoryPath(rawPath))
        {
            JoinPath(rawPath, "Program.cs"u8, ref target);
        }
        else
        {
            target.Append(rawPath);
        }

        var norm = new StringBuf(128);
        if (!PathText.Normalize(target.AsUtf8(), ref norm))
        {
            errMsg.Append("vib: invalid source path: "u8);
            errMsg.Append(rawPath);
            return false;
        }

        return LoadSourceFile(norm.AsUtf8(), rawPath, UnitKinds.App, ref seenSources, ref sources, ref errMsg);
    }

    private static bool VisitManifest(
        ReadOnlySpan<byte> manifestPath,
        bool includeStd,
        bool isRoot,
        ref int outUnitKind,
        ref StrMap<int> visited,
        ref StrMap<int> visiting,
        ref StrMap<int> seenSources,
        ref SourceTable sources,
        ref ManifestBundle bundle,
        ref StringBuf errMsg)
    {
        var normManifest = new StringBuf(128);
        if (!PathText.Normalize(manifestPath, ref normManifest))
        {
            errMsg.Append("vib: invalid manifest path: "u8);
            errMsg.Append(manifestPath);
            return false;
        }

        if (visited.Contains(normManifest.AsUtf8()))
        {
            outUnitKind = visited.Get(normManifest.AsUtf8()).Unwrap();
            return true;
        }

        if (visiting.Contains(normManifest.AsUtf8()))
        {
            errMsg.Append("vib: cyclic 'use' dependency in manifest: "u8);
            errMsg.Append(normManifest.AsUtf8());
            return false;
        }
        visiting.Set(normManifest.AsUtf8(), 1);

        if (Fs.ReadAllBytes(normManifest.AsUtf8(), MaxSourceBytes).IsErr(out var manifestBytes, out var readErr))
        {
            errMsg.Append("vib: failed to read manifest '"u8);
            errMsg.Append(normManifest.AsUtf8());
            errMsg.Append("': "u8);
            errMsg.Append(readErr.Error.KindName());
            return false;
        }

        var baseDir = new StringBuf(128);
        GetParentDir(normManifest.AsUtf8(), ref baseDir);

        int unitKind = UnitKinds.App;
        var itemPool = new BytePool(512);
        var useSlices = new Vec<ByteSlice>();
        var sourceSlices = new Vec<ByteSlice>();
        var nativeSlices = new Vec<ByteSlice>();

        ReadOnlySpan<byte> text = manifestBytes.AsReadOnlySpan();
        int pos = 0;
        if (text.Length >= 3 && text[0] == 0xEF && text[1] == 0xBB && text[2] == 0xBF)
        {
            pos = 3;
        }

        int lineNo = 0;
        while (pos < text.Length)
        {
            lineNo += 1;
            int lineEnd = pos;
            while (lineEnd < text.Length && text[lineEnd] != (byte)'\n')
            {
                lineEnd += 1;
            }
            int contentEnd = lineEnd;
            if (contentEnd > pos && text[contentEnd - 1] == (byte)'\r')
            {
                contentEnd -= 1;
            }
            ReadOnlySpan<byte> line = Bytes.TrimAscii(text.Slice(pos, contentEnd - pos));
            pos = lineEnd + 1;

            if (line.Length == 0 || line[0] == (byte)'#')
            {
                continue;
            }

            int sp = -1;
            for (int k = 0; k < line.Length; k++)
            {
                if (line[k] == (byte)' ' || line[k] == (byte)'\t')
                {
                    sp = k;
                    break;
                }
            }
            if (sp <= 0)
            {
                errMsg.Append(normManifest.AsUtf8());
                errMsg.AppendAscii((byte)'(');
                errMsg.AppendInt(lineNo);
                errMsg.Append(",1): invalid manifest directive"u8);
                return false;
            }

            ReadOnlySpan<byte> kw = line.Slice(0, sp);
            ReadOnlySpan<byte> arg = Bytes.TrimAscii(line.Slice(sp + 1, line.Length - (sp + 1)));
            if (arg.Length == 0)
            {
                errMsg.Append(normManifest.AsUtf8());
                errMsg.AppendAscii((byte)'(');
                errMsg.AppendInt(lineNo);
                errMsg.Append(",1): empty manifest argument"u8);
                return false;
            }

            if (Bytes.Eq(kw, "unit"u8))
            {
                if (Bytes.Eq(arg, "core"u8)) unitKind = UnitKinds.Core;
                else if (Bytes.Eq(arg, "std"u8)) unitKind = UnitKinds.Std;
                else if (Bytes.Eq(arg, "app"u8)) unitKind = UnitKinds.App;
                else
                {
                    errMsg.Append("vib: unknown unit kind: "u8);
                    errMsg.Append(arg);
                    return false;
                }
            }
            else if (Bytes.Eq(kw, "use"u8))
            {
                useSlices.Push(itemPool.Add(arg));
            }
            else if (Bytes.Eq(kw, "source"u8))
            {
                sourceSlices.Push(itemPool.Add(arg));
            }
            else if (Bytes.Eq(kw, "managed"u8))
            {
                // Intentionally skipped in native compiler
            }
            else if (Bytes.Eq(kw, "native"u8))
            {
                nativeSlices.Push(itemPool.Add(arg));
            }
            else
            {
                errMsg.Append("vib: unknown manifest directive: "u8);
                errMsg.Append(kw);
                return false;
            }
        }

        bool trustedCore = Bytes.Eq(normManifest.AsUtf8(), bundle.TrustedCoreManifest.AsUtf8());
        bool trustedStd = bundle.TrustedStdManifest.Length > 0 && Bytes.Eq(normManifest.AsUtf8(), bundle.TrustedStdManifest.AsUtf8());
        if ((unitKind == UnitKinds.Core && !trustedCore) || (unitKind == UnitKinds.Std && !trustedStd))
        {
            errMsg.Append("error VIB000: Only the selected sysroot manifests may declare core or std units."u8);
            return false;
        }
        if (nativeSlices.Count > 0 && unitKind != UnitKinds.Core && unitKind != UnitKinds.Std)
        {
            errMsg.Append("error VIB000: Native fragments are permitted only in the selected sysroot manifests."u8);
            return false;
        }

        for (int i = 0; i < useSlices.Count; i++)
        {
            ReadOnlySpan<byte> useRel = itemPool.Get(useSlices[i]);
            var depJoined = new StringBuf(128);
            JoinPath(baseDir.AsUtf8(), useRel, ref depJoined);
            if (!EndsWith(useRel, ".manifest"u8))
            {
                var depWithFile = new StringBuf(128);
                JoinPath(depJoined.AsUtf8(), "vib.manifest"u8, ref depWithFile);
                depJoined.Clear();
                depJoined.Append(depWithFile.AsUtf8());
            }
            int depKind = UnitKinds.App;
            if (!VisitManifest(
                depJoined.AsUtf8(),
                includeStd,
                isRoot: false,
                ref depKind,
                ref visited,
                ref visiting,
                ref seenSources,
                ref sources,
                ref bundle,
                ref errMsg))
            {
                return false;
            }
        }

        visiting.Remove(normManifest.AsUtf8());
        visited.Set(normManifest.AsUtf8(), unitKind);
        outUnitKind = unitKind;

        if (!includeStd && !isRoot && unitKind == UnitKinds.Std)
        {
            return true;
        }

        if (unitKind == UnitKinds.Std)
        {
            bundle.HasStdUnit = true;
        }

        for (int i = 0; i < nativeSlices.Count; i++)
        {
            ReadOnlySpan<byte> natRel = itemPool.Get(nativeSlices[i]);
            var natPath = new StringBuf(128);
            JoinPath(baseDir.AsUtf8(), natRel, ref natPath);
            var natNorm = new StringBuf(128);
            if (!PathText.Normalize(natPath.AsUtf8(), ref natNorm))
            {
                errMsg.Append("vib: invalid native fragment path: "u8);
                errMsg.Append(natRel);
                return false;
            }
            if (Fs.ReadAllBytes(natNorm.AsUtf8(), MaxSourceBytes).IsErr(out var natBytes, out var natErr))
            {
                errMsg.Append("vib: failed to read native fragment '"u8);
                errMsg.Append(natNorm.AsUtf8());
                errMsg.Append("': "u8);
                errMsg.Append(natErr.Error.KindName());
                return false;
            }
            if (unitKind == UnitKinds.Core)
            {
                AppendNormalizedLf(natBytes.AsReadOnlySpan(), ref bundle.CoreNativeIr);
            }
            else if (unitKind == UnitKinds.Std)
            {
                AppendNormalizedLf(natBytes.AsReadOnlySpan(), ref bundle.StdNativeIr);
            }
        }

        if (unitKind != UnitKinds.Core)
        {
            for (int i = 0; i < sourceSlices.Count; i++)
            {
                ReadOnlySpan<byte> srcRel = itemPool.Get(sourceSlices[i]);
                var srcJoined = new StringBuf(128);
                JoinPath(baseDir.AsUtf8(), srcRel, ref srcJoined);
                var srcNorm = new StringBuf(128);
                if (!PathText.Normalize(srcJoined.AsUtf8(), ref srcNorm))
                {
                    errMsg.Append("vib: invalid source path: "u8);
                    errMsg.Append(srcRel);
                    return false;
                }
                if (!LoadSourceFile(srcNorm.AsUtf8(), srcNorm.AsUtf8(), unitKind, ref seenSources, ref sources, ref errMsg))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool LoadSourceFile(
        ReadOnlySpan<byte> diskPath,
        ReadOnlySpan<byte> displayPath,
        int unitKind,
        ref StrMap<int> seenSources,
        ref SourceTable sources,
        ref StringBuf errMsg)
    {
        if (seenSources.Get(diskPath) is Some<int> existing)
        {
            SourceFile loaded = sources.Files[existing.Value];
            // A source also supplied by the application remains application
            // source, regardless of which manifest happened to visit it first.
            if (unitKind == UnitKinds.App && loaded.UnitKind != UnitKinds.App)
            {
                loaded.UnitKind = UnitKinds.App;
                sources.Files[existing.Value] = loaded;
            }
            return true;
        }

        if (Fs.ReadAllBytes(diskPath, MaxSourceBytes).IsErr(out var rawBytes, out var err))
        {
            errMsg.Append("vib: failed to read '"u8);
            errMsg.Append(displayPath);
            errMsg.Append("': "u8);
            errMsg.Append(err.Error.KindName());
            return false;
        }

        int sourceId = sources.AddFile(displayPath, rawBytes.AsReadOnlySpan(), unitKind);
        seenSources.Set(diskPath, sourceId);
        return true;
    }

    private static void AppendNormalizedLf(ReadOnlySpan<byte> src, ref Vec<byte> dst)
    {
        int start = 0;
        if (src.Length >= 3 && src[0] == 0xEF && src[1] == 0xBB && src[2] == 0xBF)
        {
            start = 3;
        }
        for (int i = start; i < src.Length; i++)
        {
            byte b = src[i];
            if (b == (byte)'\r' && i + 1 < src.Length && src[i + 1] == (byte)'\n')
            {
                continue;
            }
            dst.Push(b);
        }
        if (dst.Count > 0 && dst[dst.Count - 1] != (byte)'\n')
        {
            dst.Push((byte)'\n');
        }
    }

    private static bool FileExists(ReadOnlySpan<byte> path)
    {
        if (File.OpenRead(path).IsErr(out var opened, out _))
        {
            return false;
        }
        using var f = opened;
        return true;
    }

    private static bool IsDirectoryPath(ReadOnlySpan<byte> path)
    {
        var trimmed = new StringBuf(128);
        int len = path.Length;
        while (len > 1 && path[len - 1] == (byte)'/')
        {
            len -= 1;
        }
        if (!Utf8.IsValid(path.Slice(0, len)))
        {
            return false;
        }
        trimmed.Append(path.Slice(0, len));
        if (File.OpenRead(trimmed.AsUtf8()).IsErr(out var opened, out var err))
        {
            return err.Error.Kind == IoErrorKind.IsADirectory;
        }
        using var f = opened;
        return false;
    }

    public static void GetParentDir(ReadOnlySpan<byte> path, ref StringBuf outDir)
    {
        outDir.Clear();
        int len = path.Length;
        while (len > 1 && path[len - 1] == (byte)'/')
        {
            len -= 1;
        }
        int lastSlash = -1;
        for (int i = len - 1; i >= 0; i--)
        {
            if (path[i] == (byte)'/')
            {
                lastSlash = i;
                break;
            }
        }
        if (lastSlash < 0)
        {
            outDir.AppendAscii((byte)'.');
        }
        else if (lastSlash == 0)
        {
            outDir.AppendAscii((byte)'/');
        }
        else
        {
            outDir.Append(path.Slice(0, lastSlash));
        }
    }

    public static void JoinPath(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right, ref StringBuf outJoined)
    {
        outJoined.Clear();
        if (right.Length > 0 && right[0] == (byte)'/')
        {
            outJoined.Append(right);
            return;
        }
        if (left.Length == 0 || Bytes.Eq(left, "."u8))
        {
            outJoined.Append(right);
            return;
        }
        outJoined.Append(left);
        if (left[left.Length - 1] != (byte)'/')
        {
            outJoined.AppendAscii((byte)'/');
        }
        outJoined.Append(right);
    }

    public static bool EndsWith(ReadOnlySpan<byte> text, ReadOnlySpan<byte> suffix)
    {
        if (suffix.Length > text.Length) return false;
        return Bytes.Eq(text.Slice(text.Length - suffix.Length, suffix.Length), suffix);
    }

    private static ReadOnlySpan<byte> LastSegment(ReadOnlySpan<byte> path)
    {
        int lastSlash = -1;
        for (int i = path.Length - 1; i >= 0; i--)
        {
            if (path[i] == (byte)'/')
            {
                lastSlash = i;
                break;
            }
        }
        return lastSlash < 0 ? path : path.Slice(lastSlash + 1, path.Length - (lastSlash + 1));
    }
}
