using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Vibranium.Mutation;

internal sealed record SourceProgram(string Origin, string Group, string Source);

/// <summary>One single-span edit of a base program: <see cref="Apply"/> rebuilds the mutant on demand.</summary>
internal sealed record Mutation(string Operator, int Position, int Length, string Replacement)
{
    public string Apply(string source) => string.Concat(source.AsSpan(0, Position), Replacement, source.AsSpan(Position + Length));
}

/// <summary>
/// One SDK error. <paramref name="ZeroSafe"/> is set only for CS0649: whether the never-assigned field's type is zero-safe by
/// Stage0's own predicate. It is null when not computed, and the CS0649 key then stays CS0649 (triage only).
/// </summary>
internal sealed record CsError(string Id, int Start, int Line, int Column, string Message, bool WarningAsError, string Promotion,
    bool? ZeroSafe = null);

internal sealed record FrontendResult(string Status, string[] Ids, int ExitCode, string? Failure = null);

internal sealed record Finding(int Index, string Origin, string Group, string Operator, string Hash, string FirstCsId,
    CsError[] Errors, FrontendResult Self, FrontendResult Stage0, FrontendResult Analyzer,
    string[] Cuts, string Severity, string[] SafetyIds, string ErrorKind, string Path, int SourceBytes);

internal sealed record MemorySample(int Findings, long ManagedHeapMb, long WorkingSetMb);

internal sealed record NativeEvidence(string Id, string Path, string Source, string Build, string Run, int BuildExit, int RunExit, string Excerpt);

internal sealed record GrowthPoint(int Mutants, int Ids, int SelfAcceptIds, int DefectIds, int SelfAccepts);

internal sealed class Statistics
{
    public int SeedsDiscovered { get; set; }
    public int SeedsValid { get; set; }
    public int SeedsInvalid { get; set; }
    public int SeedsSelfRejected { get; set; }
    public int BaseSelfFailures { get; set; }
    public int ReproFailures { get; set; }
    public int Candidates { get; set; }
    public int CandidatePool { get; set; }
    public int ValidMutantsDiscarded { get; set; }
    public int DuplicateMutants { get; set; }
    public int KeptMutants { get; set; }
    public int BaselineProbes { get; set; }
    public double ElapsedSeconds { get; set; }
    public long PeakWorkingSetMb { get; set; }
    public long CgroupPeakMb { get; set; }
    public List<MemorySample> Memory { get; set; } = [];
    public Dictionary<string, int> ValidSeedsByGroup { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> KeptByGroup { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> KeptByOperator { get; set; } = new(StringComparer.Ordinal);
    public List<GrowthPoint> Growth { get; set; } = [];
}

internal static class Files
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static void WriteJson<T>(string path, T value) => File.WriteAllText(path, JsonSerializer.Serialize(value, Json) + "\n");
    public static string HashFile(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    public static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
}
