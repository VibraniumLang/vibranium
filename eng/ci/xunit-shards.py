#!/usr/bin/env python3
"""Partition VSTest/xUnit discovery by class and verify every shard's filter."""

import argparse
from collections import Counter, defaultdict
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET


DISCOVERY_HEADER = "The following Tests are available:"
DISCOVERY_STATUS = ("Test run for ", "No test matches the given testcase filter ", "No test is available in ")
TRX_NAMESPACE = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
IDENTIFIER = r"[^\W\d]\w*"
CLASS_PART = rf"{IDENTIFIER}(?:`[0-9]+)?"
QUALIFIED_CLASS = rf"(?:{IDENTIFIER}\.)*{CLASS_PART}(?:\+{CLASS_PART})*"
TEST_NAME = re.compile(rf"(?P<class>{QUALIFIED_CLASS})\.{IDENTIFIER}")


class ShardError(ValueError):
    """Discovery or a filter disagrees with the shard plan."""


def class_from_generic_signature(signature):
    # Keep the original class spelling for VSTest. Only dots outside type
    # arguments can separate the class from its method.
    depth = 0
    class_end = -1
    stripped = []
    for index, char in enumerate(signature):
        if char == "<":
            if not index or not (signature[index - 1].isalnum() or signature[index - 1] == "_"):
                return None
            depth += 1
        elif char == ">":
            if not depth or signature[index - 1] == "<" or signature[index - 1].isspace():
                return None
            depth -= 1
            if not depth and signature[index + 1:index + 2] not in ("", ".", "+"):
                return None
        elif depth:
            if not (char.isalnum() or char in "_`.+, []?"):
                return None
        else:
            if char == ".":
                class_end = index
            stripped.append(char)
    if not depth and TEST_NAME.fullmatch("".join(stripped)):
        return signature[:class_end]
    return None


def test_class(display_name):
    signature = display_name.partition("(")[0]
    match = TEST_NAME.fullmatch(signature)
    if match is not None:
        return match["class"]
    cls = class_from_generic_signature(signature) if "<" in signature else None
    if cls is None:
        raise ShardError(f"Invalid discovered test name: {display_name!r}")
    return cls


def parse_discovery(output):
    by_class = defaultdict(Counter)
    in_tests = False
    try:
        text = output.decode("utf-8")
    except UnicodeDecodeError as error:
        raise ShardError("VSTest discovery output is not UTF-8") from error
    # splitlines() (and subprocess universal newline translation) can split
    # a single Theory display name on characters that are not record breaks.
    for line in text.replace("\r\n", "\n").split("\n"):
        if line == DISCOVERY_HEADER:
            in_tests = True
        elif not in_tests or not line.strip(" \t") or line.startswith(DISCOVERY_STATUS):
            # The solution can interleave assembly banners and no-match notices
            # with other assemblies' rows. These do not end the listing.
            continue
        elif not line.startswith((" ", "\t")):
            raise ShardError(f"Unexpected line in VSTest discovery: {line!r}")
        else:
            display_name = line.strip(" \t")
            # xUnit may print identical names for separate Theory rows.
            by_class[test_class(display_name)][display_name] += 1
    if not by_class:
        raise ShardError("No tests discovered; expected an indented VSTest list")
    return by_class


def partition(by_class, shard_count):
    if not 1 <= shard_count <= len(by_class):
        raise ShardError(f"--shards must be between 1 and {len(by_class)}")
    classes = sorted(by_class)
    # VSTest's substring operator ignores case and has no start anchor.
    for cls in classes:
        for other in classes:
            if cls != other and f"{cls}.".casefold() in f"{other}.".casefold():
                raise ShardError(f"Unsafe class substring filter: {cls!r} matches {other!r}")
    counts = {cls: sum(by_class[cls].values()) for cls in classes}
    shards = [[] for _ in range(shard_count)]
    loads = [0] * shard_count
    for cls in sorted(classes, key=lambda name: (-counts[name], name)):
        index = min(range(shard_count), key=lambda i: (loads[i], i))
        shards[index].append(cls)
        loads[index] += counts[cls]
    return [sorted(shard) for shard in shards]


def test_filter(classes):
    return "|".join(f"FullyQualifiedName~{cls}." for cls in classes)


def combine(by_class):
    tests = Counter()
    for counts in by_class.values():
        tests.update(counts)
    return tests


def verify_shard(number, expected, observed):
    if not observed:
        raise ShardError(f"Shard {number}: empty discovery")
    if observed.keys() != expected.keys():
        missing = sorted(expected.keys() - observed.keys())
        leaked = sorted(observed.keys() - expected.keys())
        raise ShardError(f"Shard {number}: class mismatch (missing {missing}, leaked {leaked})")
    expected_tests, observed_tests = combine(expected), combine(observed)
    if observed_tests != expected_tests:
        missing = sum((expected_tests - observed_tests).values())
        unexpected = sum((observed_tests - expected_tests).values())
        raise ShardError(f"Shard {number}: test multiset mismatch ({missing} missing, {unexpected} unexpected)")
    return observed_tests


def discover(command, *filter_args):
    # Concurrent project discovery can interleave VSTest rows on the same line.
    result = subprocess.run(
        [*command, "-m:1", "--list-tests", *filter_args],
        env={**os.environ, "DOTNET_CLI_UI_LANGUAGE": "en-US", "VSLANG": "1033"},
        stdout=subprocess.PIPE,
        check=False,
    )
    if result.returncode:
        print(result.stdout.decode("utf-8", errors="replace"), file=sys.stderr, end="")
        raise SystemExit(exit_status(result.returncode))
    return parse_discovery(result.stdout)


def exit_status(returncode):
    return returncode if returncode >= 0 else 128 - returncode


def verify_trx(results_dir, expected_count):
    files = sorted(results_dir.rglob("*.trx"))
    if not files:
        raise ShardError("VSTest did not write any TRX results")
    counts = Counter()
    for path in files:
        try:
            root = ET.parse(path).getroot()
            counters = root.findall(f"{TRX_NAMESPACE}ResultSummary/{TRX_NAMESPACE}Counters")
            if root.tag != f"{TRX_NAMESPACE}TestRun" or len(counters) != 1:
                raise ValueError("expected one TRX ResultSummary/Counters element")
            for key in ("total", "failed", "error"):
                value = counters[0].get(key, "")
                if not re.fullmatch(r"[0-9]+", value):
                    raise ValueError(f"invalid or missing {key} counter")
                counts[key] += int(value)
        except (ET.ParseError, ValueError) as error:
            raise ShardError(f"Invalid TRX results in {path.name}: {error}") from error
    # VSTest's total includes skipped tests; executed alone would undercount them.
    if counts["total"] != expected_count or counts["failed"] or counts["error"]:
        raise ShardError(
            f"TRX count mismatch: expected {expected_count}, got {counts['total']} total, "
            f"{counts['failed']} failed, {counts['error']} errors"
        )
    print(f"Verified TRX: {counts['total']} total (expected {expected_count}), 0 failed, 0 errors, {len(files)} file(s)")


def run_shard(command, filter_args, extra, expected_count):
    with tempfile.TemporaryDirectory(prefix="vib-xunit-trx-") as directory:
        result = subprocess.run(
            [*command, *filter_args, "--logger", "trx", "--results-directory", directory, *extra], check=False
        )
        if result.returncode:
            return exit_status(result.returncode)
        verify_trx(Path(directory), expected_count)
    return 0


def expect_shard_error(check, message):
    try:
        check()
    except ShardError as error:
        assert message in str(error), str(error)
    else:
        raise AssertionError(f"Expected an error containing {message!r}")


def self_test_trx():
    def trx(total, *, executed=None, failed=0, error=0):
        return f'''<TestRun xmlns="{TRX_NAMESPACE[1:-1]}">
  <ResultSummary>
    <Counters total="{total}" executed="{total if executed is None else executed}" failed="{failed}" error="{error}" />
  </ResultSummary>
</TestRun>'''

    with tempfile.TemporaryDirectory(prefix="vib-xunit-trx-test-") as directory:
        directory = Path(directory)
        expect_shard_error(lambda: verify_trx(directory, 2), "did not write any TRX")
        first = directory / "first.trx"
        for xml, message in (
            (trx(0), "got 0 total"),
            (trx(1), "got 1 total"),
            (trx(3), "got 3 total"),
            (trx(2, failed=1), "1 failed"),
            (trx(2, error=1), "1 errors"),
            (trx(-2), "invalid or missing total"),
            (trx(2).replace(' error="0"', ""), "invalid or missing error"),
            ("<broken", "Invalid TRX results"),
            ("<TestRun />", "expected one TRX ResultSummary/Counters"),
        ):
            first.write_text(xml, encoding="utf-8")
            expect_shard_error(lambda: verify_trx(directory, 2), message)
        first.write_text(trx(2), encoding="utf-8")
        verify_trx(directory, 2)
        first.write_text(trx(1), encoding="utf-8")
        (directory / "second.trx").write_text(trx(1, executed=0), encoding="utf-8")
        verify_trx(directory, 2)


def self_test_cli():
    from contextlib import redirect_stderr, redirect_stdout
    from io import StringIO
    from unittest.mock import patch

    solution = ["--solution", "unused.slnx"]
    run_args = ["run", "--shards", "1", "--shard", "1", *solution]
    cases = [
        (["plan", "--shards", "0", *solution], "--shards must be at least 1"),
        (["verify", "--shards", "0", *solution], "--shards must be at least 1"),
        (["run", "--shards", "0", "--shard", "1", *solution], "--shards must be at least 1"),
    ]
    cases.extend(
        ([*run_args, "--", arg], "run manages --results-directory")
        for arg in ("--results-directory", "--results-directory=other", "--Results-Directory:other")
    )
    with patch.object(subprocess, "run") as run:
        for args, message in cases:
            with redirect_stderr(StringIO()) as stderr:
                try:
                    main(args)
                except SystemExit as error:
                    assert error.code == 2, error.code
                else:
                    raise AssertionError(f"Invalid command line was accepted: {args!r}")
            assert message in stderr.getvalue(), stderr.getvalue()
        run.assert_not_called()
        for returncode, status in ((1, 1), (-15, 143)):
            run.return_value = subprocess.CompletedProcess([], returncode)
            assert run_shard(["dotnet", "test"], [], [], 2) == status

        command = ["dotnet", "test", "--disable-build-servers", "unused.slnx", "--no-build"]
        listing = f"{DISCOVERY_HEADER}\n    Example.Tests.Fact\n".encode("utf-8")
        discovery = subprocess.CompletedProcess([], 0, stdout=listing)
        for runtime_option in ("-r", "--runtime"):
            run.reset_mock()
            run.side_effect = [discovery, discovery, subprocess.CompletedProcess([], 1)]
            with redirect_stdout(StringIO()):
                assert main([*run_args, "--", runtime_option, "linux-x64"]) == 1
            unfiltered, filtered, execution = [call.args[0] for call in run.call_args_list]
            assert unfiltered == [*command, "-m:1", "--list-tests"], unfiltered
            assert filtered == [*unfiltered, "--filter", "FullyQualifiedName~Example.Tests."], filtered
            assert "-m:1" not in execution and "--list-tests" not in execution, execution
            assert execution[-2:] == [runtime_option, "linux-x64"], execution


def self_test():
    foo = "Example.FooTests"
    twin = "Example.FooTwinTests"
    nested = "Example.Outer+Inner"
    truncated = f'{foo}.Theory(arg: "same…")'
    fixture = f"""Test run for example.dll
{DISCOVERY_HEADER}
    {truncated}
    {truncated}
    {nested}.Fact

Test run for other.dll
{DISCOVERY_HEADER}
    {twin}.First
    {twin}.Second
"""
    by_class = parse_discovery(fixture.replace("\n", "\r\n").encode("utf-8"))
    assert by_class[foo] == Counter({truncated: 2})
    assert partition(by_class, 2) == [[foo, nested], [twin]]
    assert partition(dict(reversed(list(by_class.items()))), 2) == partition(by_class, 2)
    assert test_filter([foo, nested]) == f"FullyQualifiedName~{foo}.|FullyQualifiedName~{nested}."
    expected = {foo: by_class[foo], nested: by_class[nested]}
    assert verify_shard(1, expected, expected) == Counter({truncated: 2, f"{nested}.Fact": 1})
    for separator in "\u2028\u2029\x85\x0b\x0c\x1c\x1d\x1e\r":
        row = f'{foo}.Theory(arg: "left{separator}right")'
        data = f"{DISCOVERY_HEADER}\n    {row}\n    {twin}.After\n"
        assert parse_discovery(data.encode("utf-8")) == {
            foo: Counter({row: 1}), twin: Counter({f"{twin}.After": 1}),
        }
    generic_rows = {
        "Example.class+Nested.Identity<Int32>(input: 4)": "Example.class+Nested",
        'Example.class+Nested.Identity<String>(input: "four")': "Example.class+Nested",
        "Example.Extra.Pair<List<Int32>, String[]>(input: null)": "Example.Extra",
        "Example.Box<Int32>.Method<System.Int32, System.String>(x: 4, y: null)": "Example.Box<Int32>",
        "Example.Box`1.OpenGenericClass": "Example.Box`1",
        "Example.Box`1+Nested`2.M<Nullable<Int64>[]>(input: null)": "Example.Box`1+Nested`2",
    }
    for display_name, cls in generic_rows.items():
        assert test_class(display_name) == cls
    interleaved = f"""Test run for example.dll
{DISCOVERY_HEADER}
    {foo}.Fact
Test run for other.dll
{DISCOVERY_HEADER}
No test matches the given testcase filter `Foo` in other.dll
    {twin}.After
"""
    assert set(parse_discovery(interleaved.encode("utf-8"))) == {foo, twin}
    failures = [
        (lambda: parse_discovery(b"no header"), "No tests"),
        (lambda: parse_discovery(b"\xff"), "not UTF-8"),
        (lambda: parse_discovery(f"{DISCOVERY_HEADER}\n    malformed".encode()), "Invalid discovered test name"),
        (lambda: parse_discovery(f"{DISCOVERY_HEADER}\n    {foo}.Fact\n\nraw junk\n    {twin}.After".encode()),
         "Unexpected line in VSTest discovery"),
        (lambda: test_class("Method<Int32>(input: 4)"), "Invalid discovered test name"),
        (lambda: test_class("Example.Invalid|Class.M<Int32>(input: 4)"), "Invalid discovered test name"),
        (lambda: test_class("Example.Box<I|J>.M<Int32>(input: 4)"), "Invalid discovered test name"),
        (lambda: test_class("Example.Class.M<List<Int32>(input: 4)"), "Invalid discovered test name"),
        (lambda: test_class("Example.Class.M<Int32>More(input: 4)"), "Invalid discovered test name"),
        (lambda: partition(by_class, 0), "--shards must be"),
        (lambda: partition(by_class, 4), "--shards must be"),
        (lambda: partition({**by_class, f"Sub.{foo.lower()}": by_class[foo]}, 2), "Unsafe class substring"),
        (lambda: verify_shard(1, expected, {}), "empty discovery"),
        (lambda: verify_shard(1, expected, {nested: by_class[nested]}), "missing"),
        (lambda: verify_shard(1, expected, by_class), "leaked"),
        (lambda: verify_shard(1, expected, {**expected, foo: Counter({truncated: 1})}), "1 missing, 0 unexpected"),
        (lambda: verify_shard(1, expected, {**expected, foo: Counter({truncated: 3})}), "0 missing, 1 unexpected"),
    ]
    for check, message in failures:
        expect_shard_error(check, message)
    self_test_trx()
    self_test_cli()
    print("xUnit shard self-test passed")


def main(argv):
    parser = argparse.ArgumentParser(description=__doc__)
    modes = parser.add_subparsers(dest="mode", required=True)
    modes.add_parser("self-test")
    for mode in ("plan", "verify", "run"):
        command_parser = modes.add_parser(mode)
        command_parser.add_argument("--shards", type=int, required=True)
        command_parser.add_argument("--solution", required=True)
        command_parser.add_argument("--configuration")
        if mode == "run":
            command_parser.add_argument("--shard", type=int, required=True)

    split = argv.index("--") if "--" in argv else len(argv)
    args = parser.parse_args(argv[:split])
    extra = argv[split + 1:]
    if split < len(argv) and args.mode != "run":
        parser.error("Extra dotnet test arguments are only supported by run")
    if args.mode == "self-test":
        self_test()
        return 0
    if args.shards < 1:
        parser.error("--shards must be at least 1")
    if args.mode == "run":
        if not 1 <= args.shard <= args.shards:
            parser.error(f"--shard must be between 1 and {args.shards}")
        for arg in extra:
            if arg == "--":  # Inline RunSettings follow this separator.
                break
            if re.match(r"--results-directory(?:[=:]|$)", arg, re.IGNORECASE):
                parser.error("run manages --results-directory for TRX verification")

    command = ["dotnet", "test", "--disable-build-servers", args.solution, "--no-build"]
    if args.configuration:
        command += ["--configuration", args.configuration]
    full = discover(command)
    shards = partition(full, args.shards)
    full_tests = combine(full)
    print(f"Discovered {sum(full_tests.values())} tests across {len(full)} classes", flush=True)
    seen_classes = set()
    seen_tests = Counter()
    for number, classes in enumerate(shards, 1):
        if args.mode == "run" and number != args.shard:
            continue
        expected = {cls: full[cls] for cls in classes}
        count = sum(combine(expected).values())
        print(f"Shard {number}/{args.shards}: {count} tests in {len(classes)} classes", flush=True)
        if args.mode == "plan":
            for cls in classes:
                print(f"  {sum(expected[cls].values()):4} {cls}")
            continue
        filter_args = ["--filter", test_filter(classes)]
        observed = discover(command, *filter_args)
        tests = verify_shard(number, expected, observed)
        if args.mode == "run":
            return run_shard(command, filter_args, extra, tests.total())
        if seen_classes.intersection(observed):
            raise ShardError(f"Shard {number}: class already seen in an earlier shard")
        seen_classes.update(observed)
        seen_tests.update(tests)

    if args.mode == "verify":
        if seen_tests != full_tests or seen_classes != full.keys():
            raise ShardError("Combined shards do not match full test discovery")
        print(f"Verified {args.shards} non-empty shards: exact test multiset, 0 missing, 0 duplicates, 0 leakage")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main(sys.argv[1:]))
    except (ShardError, OSError) as error:
        sys.exit(f"xunit-shards: {error}")
