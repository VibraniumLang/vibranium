using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Vibranium.Conformance;

/// <summary>
/// Validates the in-process SDK oracle against a real `dotnet build` of generated programs placed
/// inside the repository (so Directory.Build.props, .editorconfig and Vibranium.App.props apply,
/// with Core/Std project references and the Analyzer as an analyzer reference).
/// </summary>
internal static class SdkCrossCheck
{
    public static int Write(Toolchain tc, ulong seed, int count, string dir)
    {
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
        Directory.CreateDirectory(dir);
        var sln = new StringBuilder("<Solution>\n");
        for (int i = 0; i < count; i++)
        {
            var (gp, _) = Campaign.Make(seed, i, 0, 0);
            string pd = Path.Combine(dir, "p" + i);
            Directory.CreateDirectory(pd);
            string rel = Path.GetRelativePath(pd, tc.RepoRoot).Replace('\\', '/');
            File.WriteAllText(Path.Combine(pd, "p" + i + ".csproj"), $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                  </PropertyGroup>
                  <Import Project="{rel}/Vibranium.App.props" />
                  <ItemGroup>
                    <ProjectReference Include="{rel}/src/Vibranium.Core/Vibranium.Core.csproj" />
                    <ProjectReference Include="{rel}/src/Vibranium.Std/Vibranium.Std.csproj" />
                    <ProjectReference Include="{rel}/src/Vibranium.Analyzer/Vibranium.Analyzer.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(pd, "Program.cs"), Printer.Print(gp.Ast));
            sln.Append($"  <Project Path=\"p{i}/p{i}.csproj\" />\n");
        }
        sln.Append("</Solution>\n");
        File.WriteAllText(Path.Combine(dir, "sdkcheck.slnx"), sln.ToString());
        Console.WriteLine($"wrote {count} projects to {dir}");
        return 0;
    }

    private static readonly Regex BuildLine = new(@"^(?<file>[^(]+)\((?<l>\d+),(?<c>\d+)\): (?<sev>error|warning) (?<id>[A-Z]+\d+): (?<msg>.*?) \[(?<proj>[^\]]+\.csproj)", RegexOptions.Compiled);

    public static int Compare(Toolchain tc, string dir, string logPath)
    {
        var real = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        // build-infrastructure failures (missing references, MSBuild errors) invalidate the comparison
        var infra = File.ReadLines(logPath).Where(l => l.Contains("error CS0006", StringComparison.Ordinal) || l.Contains("error MSB", StringComparison.Ordinal) || l.Contains("error NU", StringComparison.Ordinal)).ToList();
        if (infra.Count > 0)
        {
            Console.WriteLine("sdk-compare: INFRASTRUCTURE FAILURE in the real build (comparison not meaningful):");
            foreach (var l in infra.Distinct().Take(5))
            {
                Console.WriteLine("  " + l.Trim());
            }
            return 2;
        }
        foreach (var line in File.ReadLines(logPath))
        {
            var m = BuildLine.Match(line.Trim());
            if (!m.Success)
            {
                continue;
            }
            string proj = Path.GetFileNameWithoutExtension(m.Groups["proj"].Value);
            if (!real.TryGetValue(proj, out var set))
            {
                real[proj] = set = new SortedSet<string>(StringComparer.Ordinal);
            }
            set.Add(m.Groups["id"].Value);
        }
        var sdk = new SdkOracle(tc);
        int agree = 0;
        int styleOnly = 0;
        int disagree = 0;
        foreach (var pd in Directory.GetDirectories(dir).OrderBy(d => int.Parse(Path.GetFileName(d)[1..], System.Globalization.CultureInfo.InvariantCulture)))
        {
            string name = Path.GetFileName(pd);
            string file = Path.Combine(pd, "Program.cs");
            var r = sdk.Check(File.ReadAllText(file), file);
            var mine = new SortedSet<string>(r.Errors.Concat(r.Warnings).Concat(r.Analyzer).Select(d => d.Id), StringComparer.Ordinal);
            var theirs = real.GetValueOrDefault(name) ?? new SortedSet<string>(StringComparer.Ordinal);
            var theirsNoStyle = new SortedSet<string>(theirs.Where(id => !id.StartsWith("IDE", StringComparison.Ordinal)), StringComparer.Ordinal);
            string verdict;
            if (mine.SetEquals(theirs))
            {
                agree++;
                verdict = "AGREE";
            }
            else if (mine.SetEquals(theirsNoStyle))
            {
                styleOnly++;
                verdict = "AGREE-EXCEPT-STYLE";
            }
            else
            {
                disagree++;
                verdict = "DISAGREE";
            }
            Console.WriteLine($"{name}\t{verdict}\tinproc={{{string.Join(',', mine)}}}\tdotnet-build={{{string.Join(',', theirs)}}}");
        }
        Console.WriteLine($"sdk-compare: agree={agree} agree-except-IDE-style={styleOnly} disagree={disagree}");
        return disagree == 0 ? 0 : 1;
    }
}
