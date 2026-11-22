using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace Vibranium.Tests;

// CI check for docs/RULES.md, the rule ledger. Format and heuristics are documented in the ledger's
// "How to read and maintain this ledger" section; RuleLedger below is the single parser and validator.
public sealed class RuleLedgerTests
{
    private static string RulesPath => Path.Combine(Round11AuditAssertions.RepoRoot, "docs", "RULES.md");
    private static string SpecPath => Path.Combine(Round11AuditAssertions.RepoRoot, "docs", "SPEC.md");

    private static RuleLedger.Report Validate() => RuleLedger.Validate(
        File.ReadAllText(RulesPath), File.ReadAllText(SpecPath), RuleLedger.TestExistsInAssembly(typeof(RuleLedgerTests).Assembly));

    [Fact]
    public void Ledger_ParsesWithUniqueWellFormedRuleIds() =>
        AssertNoProblems(Validate(), RuleLedger.Category.Format);

    [Fact]
    public void Ledger_EveryTestableRuleHasRejectAndAcceptWitnessesForEveryFrontend() =>
        AssertNoProblems(Validate(), RuleLedger.Category.Coverage);

    [Fact]
    public void Ledger_EveryNamedWitnessTestExistsInTheTestAssembly() =>
        AssertNoProblems(Validate(), RuleLedger.Category.Existence);

    [Fact]
    public void Ledger_EveryRuleQuoteStillOccursInSpec() =>
        AssertNoProblems(Validate(), RuleLedger.Category.StaleQuote);

    [Fact]
    public void Ledger_EveryNormativeSpecUnitIsCoveredByARule() =>
        AssertNoProblems(Validate(), RuleLedger.Category.UncoveredSpec);

    [Fact]
    public void Ledger_PendingRulesAreOnlyTheApprovedCutsC10ToC20()
    {
        var report = Validate();
        AssertNoProblems(report, RuleLedger.Category.Pending);
        Assert.All(report.Rules.Where(r => r.Kind == "pending"), r => Assert.Matches(@"^R1-cut-c(1\d|20)-[a-z0-9-]+$", r.Id));
    }

    [Fact]
    public void Ledger_HasTheExpectedShape()
    {
        var report = Validate();
        Assert.True(report.Rules.Count >= 200, $"only {report.Rules.Count} rules parsed");
        Assert.True(report.SpecUnits.Count >= 200, $"only {report.SpecUnits.Count} SPEC units found");
        Assert.Contains(report.Rules, r => r.Id == "R3.3-return-provenance" && r.Kind == "frontend");
        Assert.Contains(report.Rules, r => r.Id == "R2.2-ctor-this" && r.Kind == "frontend");
    }

    // The validator itself is checked against synthetic ledgers, so a broken parser cannot pass silently.
    private const string SyntheticSpec = """
        # Title

        ## 1. Section

        - **Alpha:** Alpha things are banned (`VIB000`).
        - Beta things are fine.

        | Code | Rule |
        |---|---|
        | `VIB000` | Gamma row |

        ## 6. Layout

        - Not normative.
        """;

    private static Func<string, bool> Exists(params string[] names) => name => names.Contains(name, StringComparer.Ordinal);

    [Fact]
    public void Validator_AcceptsACompleteSyntheticLedger()
    {
        const string rules = """
            ### R1-alpha — alpha
            - Quote (§1): Alpha things are banned (VIB000).
            - Expect: VIB000
            - Reject: `T.C.RejectAll` [analyzer, stage0, self]
            - Accept: `T.C.AcceptA` [analyzer]
            - Accept: `T.C.AcceptRest` [stage0, self]

            ### R1-beta — beta
            - Quote (§1): Beta things … fine.
            - Quote (§1): VIB000 | Gamma row
            - Kind: runtime
            - Accept: `T.C.AcceptAll` [analyzer, stage0, self]
            """;
        var report = RuleLedger.Validate(rules, SyntheticSpec, Exists("T.C.RejectAll", "T.C.AcceptA", "T.C.AcceptRest", "T.C.AcceptAll"));
        Assert.Empty(report.Problems);
        Assert.Equal(3, report.SpecUnits.Count);
    }

    [Fact]
    public void Validator_ReportsMissingWitnessesUnknownTestsStaleQuotesAndUncoveredSpec()
    {
        const string rules = """
            ### R1-alpha — alpha
            - Quote (§1): Alpha things are banned
            - Expect: VIB000
            - Reject: `T.C.RejectAll` [analyzer, stage0, self]
            - Accept: `T.C.Missing` [analyzer, stage0]

            ### R1-ghost — quote no longer in SPEC
            - Quote (§1): Delta things are banned
            - Kind: prose
            - Reason: example

            ### R1-alpha — duplicate id
            - Kind: prose
            """;
        var report = RuleLedger.Validate(rules, SyntheticSpec, Exists("T.C.RejectAll"));
        Assert.Contains(report.Problems, p => p.Kind == RuleLedger.Category.Coverage && p.Text.Contains("R1-alpha accept self", StringComparison.Ordinal));
        Assert.Contains(report.Problems, p => p.Kind == RuleLedger.Category.Existence && p.Text.Contains("T.C.Missing", StringComparison.Ordinal));
        Assert.Contains(report.Problems, p => p.Kind == RuleLedger.Category.StaleQuote && p.Text.Contains("R1-ghost", StringComparison.Ordinal));
        Assert.Contains(report.Problems, p => p.Kind == RuleLedger.Category.UncoveredSpec && p.Text.Contains("Beta things are fine.", StringComparison.Ordinal));
        Assert.Contains(report.Problems, p => p.Kind == RuleLedger.Category.Format && p.Text.Contains("duplicate rule id R1-alpha", StringComparison.Ordinal));
        Assert.Contains(report.Problems, p => p.Kind == RuleLedger.Category.Format && p.Text.Contains("R1-alpha", StringComparison.Ordinal) && p.Text.Contains("Reason", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_AcceptsNaWithReasonAndRejectsNaWithoutReasonOrTbdOutsidePending()
    {
        const string rules = """
            ### R1-alpha — alpha
            - Quote (§1): Alpha things are banned
            - Quote (§1): Beta things are fine.
            - Quote (§1): Gamma row
            - Expect: VIB000
            - Reject: `T.C.R` [stage0, self]
            - N/A reject [analyzer]: the SDK honours pragma suppressions by design
            - Accept: TBD-pending-integration [analyzer, stage0, self]
            - N/A accept [self]:
            """;
        var report = RuleLedger.Validate(rules, SyntheticSpec, Exists("T.C.R"));
        Assert.DoesNotContain(report.Problems, p => p.Text.Contains("R1-alpha reject analyzer", StringComparison.Ordinal));
        Assert.Contains(report.Problems, p => p.Kind == RuleLedger.Category.Pending && p.Text.Contains("TBD-pending-integration", StringComparison.Ordinal));
        Assert.Contains(report.Problems, p => p.Kind == RuleLedger.Category.Format && p.Text.Contains("N/A", StringComparison.Ordinal) && p.Text.Contains("reason", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_ResolvesWitnessNamesByReflection()
    {
        var exists = RuleLedger.TestExistsInAssembly(typeof(RuleLedgerTests).Assembly);
        Assert.True(exists("Vibranium.Tests.RuleLedgerTests.Validator_ResolvesWitnessNamesByReflection"));
        Assert.True(exists("Vibranium.Tests.Round11CallActivationMatrixTests.EarlierArgumentLivesThroughInvocation"));
        Assert.False(exists("Vibranium.Tests.RuleLedgerTests.NoSuchTest"));
        Assert.False(exists("Vibranium.Tests.RuleLedgerTests.Validate"));
        Assert.False(exists("Vibranium.Tests.NoSuchClass.Test"));
    }

    private static void AssertNoProblems(RuleLedger.Report report, RuleLedger.Category category)
    {
        var problems = report.Problems.Where(p => p.Kind == category).Select(p => p.Text).ToList();
        Assert.True(problems.Count == 0, $"{problems.Count} {category} problem(s) in docs/RULES.md:\n" + string.Join("\n", problems));
    }
}

internal static class RuleLedger
{
    internal enum Category { Format, Coverage, Existence, StaleQuote, UncoveredSpec, Pending }

    internal sealed record Problem(Category Kind, string Text);

    internal sealed record Witness(string Side, string Test, IReadOnlyList<string> Frontends);

    internal sealed record NotApplicable(string Side, IReadOnlyList<string> Frontends, string Reason);

    internal sealed record Rule(
        string Id,
        int Line,
        string Kind,
        IReadOnlyList<string> Quotes,
        IReadOnlyList<Witness> Witnesses,
        IReadOnlyList<NotApplicable> NotApplicable,
        IReadOnlyList<string> Evidence,
        string? Expect,
        string? Reason,
        string? Source);

    internal sealed record SpecUnit(int Line, string Text);

    internal sealed record Report(IReadOnlyList<Rule> Rules, IReadOnlyList<SpecUnit> SpecUnits, IReadOnlyList<Problem> Problems);

    internal static readonly string[] Frontends = ["analyzer", "stage0", "self"];

    internal const string Pending = "TBD-pending-integration";

    private static readonly string[] Kinds = ["frontend", "runtime", "emitter", "repo", "prose", "pending"];

    private static readonly Regex Heading = new(@"^### (?<id>\S+)(?: — .+)?$", RegexOptions.CultureInvariant);
    private static readonly Regex IdShape = new(@"^R\d+(?:\.\d+)?-[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant);
    private static readonly Regex QuoteLine = new(@"^- Quote \((?<sec>[^)]*)\): (?<text>.+)$", RegexOptions.CultureInvariant);
    private static readonly Regex WitnessLine = new(@"^- (?<side>Reject|Accept): (?:`(?<test>[A-Za-z_][A-Za-z0-9_.]*)`|(?<tbd>TBD-pending-integration)) \[(?<fe>[a-z0-9, ]+)\](?<note>.*)$", RegexOptions.CultureInvariant);
    private static readonly Regex NaLine = new(@"^- N/A (?<side>reject|accept) \[(?<fe>[a-z0-9, ]+)\]:(?<reason>.*)$", RegexOptions.CultureInvariant);
    private static readonly Regex EvidenceLine = new(@"^- Evidence: `(?<test>[A-Za-z_][A-Za-z0-9_.]*)`$", RegexOptions.CultureInvariant);
    private static readonly Regex FieldLine = new(@"^- (?<name>[A-Za-z/ ]+?)(?: \([^)]*\))?(?: \[[^\]]*\])?:(?: |$)", RegexOptions.CultureInvariant);

    private static readonly HashSet<string> KnownFields = new(StringComparer.Ordinal)
    {
        "Quote", "Source", "Kind", "Expect", "Reason", "Evidence", "Reject", "Accept", "N/A reject", "N/A accept",
        "Note", "Reject shape", "Accept shape", "See also",
    };

    // Text normalization shared by quotes and SPEC units: Markdown code and emphasis markers are dropped and runs of
    // whitespace collapse to one space, so a quote survives re-formatting but not a change of wording.
    internal static string Normalize(string text) =>
        Regex.Replace(text.Replace("`", "", StringComparison.Ordinal).Replace("*", "", StringComparison.Ordinal), @"\s+", " ").Trim();

    // A normative SPEC unit is every non-blank line before the "## 6." heading that is not a heading, a horizontal
    // rule, a table separator row or a table header row (the row right above a separator).
    internal static List<SpecUnit> SpecUnits(string spec)
    {
        string[] lines = spec.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var units = new List<SpecUnit>();
        static bool Separator(string line) => Regex.IsMatch(line.Trim(), @"^\|[\s:|-]+\|$");
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.StartsWith("## 6.", StringComparison.Ordinal)) break;
            if (line.Length == 0 || line.StartsWith('#') || line == "---" || Separator(line)) continue;
            if (line.StartsWith('|') && i + 1 < lines.Length && Separator(lines[i + 1])) continue;
            units.Add(new SpecUnit(i + 1, Normalize(line)));
        }
        return units;
    }

    internal static bool QuoteMatches(string quote, string unit)
    {
        int position = 0;
        foreach (string fragment in quote.Split('…').Select(f => f.Trim()).Where(f => f.Length > 0))
        {
            int found = unit.IndexOf(fragment, position, StringComparison.Ordinal);
            if (found < 0) return false;
            position = found + fragment.Length;
        }
        return position > 0;
    }

    internal static Func<string, bool> TestExistsInAssembly(Assembly assembly) => name =>
    {
        int dot = name.LastIndexOf('.');
        if (dot <= 0) return false;
        var type = assembly.GetType(name[..dot], throwOnError: false);
        return type is not null && type
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Any(m => m.Name == name[(dot + 1)..] && m.GetCustomAttributes(typeof(FactAttribute), inherit: true).Length > 0);
    };

    internal static Report Validate(string rulesText, string specText, Func<string, bool> testExists)
    {
        var problems = new List<Problem>();
        var rules = Parse(rulesText, problems);
        var units = SpecUnits(specText);
        var covered = new HashSet<int>();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            string at = $"{rule.Id} (RULES.md line {rule.Line})";
            if (!seen.Add(rule.Id)) problems.Add(new(Category.Format, $"duplicate rule id {rule.Id} at RULES.md line {rule.Line}"));
            if (!IdShape.IsMatch(rule.Id)) problems.Add(new(Category.Format, $"{at}: id does not match R<section>-<slug>"));
            if (!Kinds.Contains(rule.Kind)) problems.Add(new(Category.Format, $"{at}: unknown Kind '{rule.Kind}'"));

            foreach (string quote in rule.Quotes)
            {
                var hits = units.Where(u => QuoteMatches(quote, u.Text)).ToList();
                if (hits.Count == 0) problems.Add(new(Category.StaleQuote, $"{at}: quote not found in SPEC §0–§5: \"{quote}\""));
                foreach (var hit in hits) covered.Add(hit.Line);
            }

            if (rule.Kind == "pending")
            {
                if (rule.Source is null || !Regex.IsMatch(rule.Source, @"^cut C(1\d|20)$"))
                    problems.Add(new(Category.Pending, $"{at}: a pending rule needs 'Source: cut C10' … 'cut C20'"));
            }
            else
            {
                if (rule.Quotes.Count == 0) problems.Add(new(Category.Format, $"{at}: needs at least one '- Quote (§…): …' line"));
                foreach (var witness in rule.Witnesses.Where(w => w.Test == Pending))
                    problems.Add(new(Category.Pending, $"{at}: {Pending} is allowed only in Kind: pending rules"));
            }

            foreach (var na in rule.NotApplicable.Where(n => n.Reason.Length == 0))
                problems.Add(new(Category.Format, $"{at}: N/A {na.Side} line needs a reason"));
            foreach (string frontend in rule.Witnesses.SelectMany(w => w.Frontends).Concat(rule.NotApplicable.SelectMany(n => n.Frontends)).Distinct())
                if (!Frontends.Contains(frontend)) problems.Add(new(Category.Format, $"{at}: unknown frontend '{frontend}'"));

            foreach (string test in rule.Witnesses.Select(w => w.Test).Concat(rule.Evidence).Where(t => t != Pending).Distinct())
                if (!testExists(test)) problems.Add(new(Category.Existence, $"{at}: test {test} is not a [Fact]/[Theory] method in the test assembly"));

            switch (rule.Kind)
            {
                case "prose":
                    if (string.IsNullOrWhiteSpace(rule.Reason)) problems.Add(new(Category.Format, $"{at}: a prose rule needs 'Reason: …'"));
                    if (rule.Witnesses.Count > 0 || rule.Evidence.Count > 0) problems.Add(new(Category.Format, $"{at}: a prose rule lists no witnesses"));
                    break;
                case "repo":
                    if (rule.Evidence.Count == 0) problems.Add(new(Category.Coverage, $"{at}: a repo rule needs 'Evidence: `Test`'"));
                    break;
                case "frontend":
                    if (string.IsNullOrWhiteSpace(rule.Expect)) problems.Add(new(Category.Format, $"{at}: a frontend rule needs 'Expect: …'"));
                    RequireWitnesses(rule, "reject", Frontends, problems);
                    RequireWitnesses(rule, "accept", Frontends, problems);
                    break;
                case "runtime":
                    RequireWitnesses(rule, "accept", Frontends, problems);
                    break;
                case "emitter":
                    RequireWitnesses(rule, "accept", ["stage0", "self"], problems);
                    break;
                case "pending":
                    RequireWitnesses(rule, "reject", Frontends, problems);
                    RequireWitnesses(rule, "accept", Frontends, problems);
                    break;
            }
        }

        foreach (var unit in units.Where(u => !covered.Contains(u.Line)))
            problems.Add(new(Category.UncoveredSpec, $"SPEC.md line {unit.Line} is not covered by any rule quote: \"{(unit.Text.Length > 160 ? unit.Text[..160] + "…" : unit.Text)}\""));

        return new Report(rules, units, problems);
    }

    private static void RequireWitnesses(Rule rule, string side, string[] frontends, List<Problem> problems)
    {
        foreach (string frontend in frontends)
        {
            bool witnessed = rule.Witnesses.Any(w => w.Side == side && w.Frontends.Contains(frontend));
            bool excused = rule.NotApplicable.Any(n => n.Side == side && n.Frontends.Contains(frontend) && n.Reason.Length > 0);
            if (!witnessed && !excused)
                problems.Add(new(Category.Coverage, $"{rule.Id} {side} {frontend}: no witness and no 'N/A {side} [{frontend}]: reason' (RULES.md line {rule.Line})"));
        }
    }

    private static List<Rule> Parse(string text, List<Problem> problems)
    {
        var rules = new List<Rule>();
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        int i = 0;
        while (i < lines.Length)
        {
            var heading = Heading.Match(lines[i]);
            if (!heading.Success)
            {
                if (lines[i].StartsWith("### ", StringComparison.Ordinal)) problems.Add(new(Category.Format, $"RULES.md line {i + 1}: malformed rule heading"));
                i++;
                continue;
            }
            string id = heading.Groups["id"].Value;
            int line = i + 1;
            string kind = "frontend";
            string? expect = null, reason = null, source = null;
            var quotes = new List<string>();
            var witnesses = new List<Witness>();
            var notApplicable = new List<NotApplicable>();
            var evidence = new List<string>();
            for (i++; i < lines.Length && !lines[i].StartsWith("#", StringComparison.Ordinal); i++)
            {
                string l = lines[i];
                if (!l.StartsWith("- ", StringComparison.Ordinal)) continue;
                var field = FieldLine.Match(l);
                if (!field.Success || !KnownFields.Contains(field.Groups["name"].Value))
                {
                    problems.Add(new(Category.Format, $"{id}: unknown field at RULES.md line {i + 1}: {l}"));
                    continue;
                }
                string value = l[field.Length..].Trim();
                Match m;
                switch (field.Groups["name"].Value)
                {
                    case "Quote":
                        m = QuoteLine.Match(l);
                        if (m.Success) quotes.Add(Normalize(m.Groups["text"].Value));
                        else problems.Add(new(Category.Format, $"{id}: malformed quote at RULES.md line {i + 1}"));
                        break;
                    case "Kind": kind = value; break;
                    case "Expect": expect = value; break;
                    case "Reason": reason = value; break;
                    case "Source": source = value; break;
                    case "Evidence":
                        m = EvidenceLine.Match(l);
                        if (m.Success) evidence.Add(m.Groups["test"].Value);
                        else problems.Add(new(Category.Format, $"{id}: malformed evidence at RULES.md line {i + 1}"));
                        break;
                    case "Reject":
                    case "Accept":
                        m = WitnessLine.Match(l);
                        if (m.Success)
                        {
                            string test = m.Groups["tbd"].Success ? Pending : m.Groups["test"].Value;
                            witnesses.Add(new(m.Groups["side"].Value.ToLowerInvariant(), test, SplitFrontends(m.Groups["fe"].Value)));
                        }
                        else problems.Add(new(Category.Format, $"{id}: malformed witness at RULES.md line {i + 1}: {l}"));
                        break;
                    case "N/A reject":
                    case "N/A accept":
                        m = NaLine.Match(l);
                        if (m.Success) notApplicable.Add(new(m.Groups["side"].Value, SplitFrontends(m.Groups["fe"].Value), m.Groups["reason"].Value.Trim()));
                        else problems.Add(new(Category.Format, $"{id}: malformed N/A line at RULES.md line {i + 1}"));
                        break;
                }
            }
            rules.Add(new Rule(id, line, kind, quotes, witnesses, notApplicable, evidence, expect, reason, source));
        }
        return rules;
    }

    private static string[] SplitFrontends(string list) =>
        list.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
