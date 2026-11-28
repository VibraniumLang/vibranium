#!/usr/bin/env python3
"""Cross-check minimized CS error sets with real, resource-gated SDK app builds."""
import argparse
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import xml.sax.saxutils

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--root", required=True, type=Path)
parser.add_argument("--results", required=True, type=Path)
parser.add_argument("--ids", nargs="*")
args = parser.parse_args()
is_ci = os.environ.get("CI") == "true"
if not is_ci and not shutil.which("heavy"):
    print("heavy is required outside CI; refusing an ungated build", file=sys.stderr)
    raise SystemExit(75)
build_command = ["dotnet", "build"] if is_ci else ["heavy", "dotnet", "build"]
root = args.root.resolve()
results = args.results.resolve()
selected = sorted((results / "repros").glob("*.json"))
if args.ids:
    selected = [p for p in selected if p.stem in args.ids]
if not selected:
    raise SystemExit("No selected repro sidecars")
checks = []
for sidecar in selected:
    data = json.loads(sidecar.read_text())
    expected = sorted({d["Id"] for d in data["Errors"]})
    first = data["Errors"][0]["Id"] if data["Errors"] else None
    with tempfile.TemporaryDirectory(prefix="vib-mutation-sdk-") as temporary:
        scratch = Path(temporary)
        def relative(path):
            return xml.sax.saxutils.escape(os.path.relpath(path, scratch))
        (scratch / "Program.cs").write_text(sidecar.with_suffix(".cs").read_text())
        (scratch / "App.csproj").write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="{relative(root / "Directory.Build.props")}" />
  <Import Project="{relative(root / "Vibranium.App.props")}" />
  <PropertyGroup><OutputType>Exe</OutputType><EnableNETAnalyzers>false</EnableNETAnalyzers></PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="{relative(root / "src/Vibranium.Core/Vibranium.Core.csproj")}" />
    <ProjectReference Include="{relative(root / "src/Vibranium.Std/Vibranium.Std.csproj")}" />
    <ProjectReference Include="{relative(root / "src/Vibranium.Analyzer/Vibranium.Analyzer.csproj")}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>\n''')
        completed = subprocess.run(build_command + ["--disable-build-servers", str(scratch / "App.csproj"), "-c", "Debug", "-v:q"],
            cwd=root, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, check=False)
        if completed.returncode == 75:
            raise SystemExit(75)
        diagnostic_lines = [line for line in completed.stdout.splitlines() if re.search(r"\berror [A-Z]+\d+\b", line)]
        app_lines = [line for line in diagnostic_lines if str(scratch / "App.csproj") in line]
        observed = sorted({match for line in app_lines for match in re.findall(r"\berror (CS\d+)\b", line)})
        infrastructure_failure = any(
            line not in app_lines or re.search(r"\berror (?:MSB|NU|NETSDK)\d+\b|\berror CS0006\b", line)
            for line in diagnostic_lines)
        # The gate keys on the first error. csc stops after declaration errors and skips method-body and emit diagnostics,
        # while the in-process oracle asks for all of them, so the real build may report a subset. Require the first ID and
        # no ID the oracle did not see; an exact set match is recorded separately.
        failure = completed.returncode != 1 or infrastructure_failure or not observed or first not in observed \
            or not set(observed) <= set(expected)
        log = completed.stdout.replace(str(root), "<repo>").replace(temporary, "<scratch>")
        home = str(Path.home())
        log = log.replace(home, "<home>")
        checks.append({"Id": sidecar.stem, "First": first, "Expected": expected, "Observed": observed, "Exact": observed == expected,
                       "ExitCode": completed.returncode, "InfrastructureFailure": infrastructure_failure, "Match": not failure, "Log": log})
        print(sidecar.stem, ("MATCH" if observed == expected else "MATCH-FIRST") if not failure else "MISMATCH", expected, observed, flush=True)
(results / "sdk-crosscheck.json").write_text(json.dumps(checks, indent=2) + "\n")
raise SystemExit(1 if any(not item["Match"] for item in checks) else 0)
