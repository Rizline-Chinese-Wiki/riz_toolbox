"""Archive only a root-observed, fully completed owned Kernel/legacy matrix.

This program reads PE/test evidence and existing game files. It never builds,
loads a DLL, starts a child, installs, or writes to the game directory.
"""
from pathlib import Path
import argparse
import hashlib
import json
import shutil
import struct
import zipfile


BATCH = Path(__file__).resolve().parent
REPO = BATCH.parents[2]
DEFAULT_CACHE = Path("D:/RizToolbox-build-cache/early-kernel-final-20261025")
STEPS = ["kernel-publisher-build", "kernel-chain-build", "kernel-chain-73",
         "legacy-publisher-build", "legacy-publisher-57", "legacy-chain-build", "legacy-chain-73"]
EARLY_NAMES = {f"mode-{i}" for i in range(16)} | {
    f"fault-{s}-{k}" for s in range(1, 15) for k in range(4)} | {"normal-debugger"}
FIXED_NAMES = {"normal"} | {f"fault-{s}-{k}" for s in range(1, 15) for k in range(4)}


def require(value, message):
    if not value:
        raise ValueError(message)


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def sha(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def same_path(left, right):
    return Path(left).resolve() == Path(right).resolve()


def safe_relative(value):
    path = Path(value)
    require(not path.is_absolute() and ".." not in path.parts, f"Unsafe archive path: {value}")
    return path.as_posix()


def write(name, value):
    destination = BATCH / name
    text = json.dumps(value, ensure_ascii=False, indent=2) + "\n"
    if destination.exists():
        require(destination.read_text(encoding="utf-8") == text, f"Refuse to replace prior evidence: {name}")
    else:
        destination.write_text(text, encoding="utf-8")


def verify_rows(rows, base, count):
    require(len(rows) == count and len({r["path"] for r in rows}) == count, "Wrong manifest row count")
    for row in rows:
        path = base / safe_relative(row["path"])
        require(path.is_file() and sha(path) == row["sha256"], f"Manifest hash differs: {path}")


def verify_zip(path, rows):
    with zipfile.ZipFile(path) as archive:
        names = archive.namelist()
        require(len(names) == len(rows) and set(names) == {r["path"] for r in rows}, f"Wrong ZIP entries: {path}")
        require(archive.testzip() is None, f"Corrupt ZIP: {path}")
        for row in rows:
            require(hashlib.sha256(archive.read(row["path"])).hexdigest() == row["sha256"],
                    f"ZIP hash differs: {row['path']}")


def make_zip(path, base, rows):
    if not path.exists():
        with zipfile.ZipFile(path, "x", compression=zipfile.ZIP_DEFLATED) as archive:
            for row in rows:
                archive.write(base / row["path"], row["path"])
    verify_zip(path, rows)


def copy_exact_tree(source, destination):
    rows = []
    for path in sorted(source.rglob("*")):
        require(not path.is_symlink(), f"Refuse linked cache entry: {path}")
        if not path.is_file():
            continue
        relative = path.relative_to(source).as_posix()
        target = destination / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        if target.exists():
            require(sha(target) == sha(path), f"Prior raw evidence differs: {target}")
        else:
            shutil.copyfile(path, target)
        rows.append(dict(path=relative, sha256=sha(path), bytes=path.stat().st_size))
    require({p.relative_to(destination).as_posix() for p in destination.rglob("*") if p.is_file()}
            == {r["path"] for r in rows}, "Unexpected raw archive entries")
    verify_rows(rows, destination, len(rows))
    return rows


def held_inputs(aggregate, held_path):
    rows = aggregate["files"]
    require(rows and aggregate["allInputHashesUnchanged"] is True, "Missing held-input result")
    held = read(held_path)
    held = held["files"] if isinstance(held, dict) else held
    by_path = {str(Path(r["path"]).resolve()).lower(): r for r in held}
    require(len(by_path) == len(held) == len(rows), "Ambiguous/missing held inputs")
    for row in rows:
        key = str(Path(row["path"]).resolve()).lower()
        prior = by_path.get(key)
        require(prior is not None and prior["writeAndDeleteDenied"] is True, "Input was not held read-only")
        require(row["beforeSha256"] == row["afterSha256"] == prior["sha256"] == sha(Path(row["path"])),
                f"Held/before/after/current identity differs: {row['path']}")
    return len(rows)


def early_matrix(cache, label, profile):
    cases = cache / label
    aggregate = read(cases / "fixture-result.json")
    require(aggregate["passed"] is True and aggregate["profile"] == profile,
            f"Incomplete {label} aggregate")
    require(aggregate["verifiedCases"] == aggregate["expectedCases"] == aggregate["independentProcesses"] == 73,
            f"Wrong {label} case counts")
    require(aggregate["baselineFirst"] and aggregate["debuggerSupplementIncluded"], "Missing baseline/debugger")
    for key in ["realSystemWinHttpVerified", "gameFirstInstallVerified", "productionV4Issued",
                "allTransitiveLoaderDependenciesSafeVerified", "schedulerClosureVerified",
                "engineCancellationCompleted", "installed", "gameCodeExecuted", "sharedProjectsRebuiltByRunner"]:
        require(aggregate[key] is False, f"Unsupported expanded claim: {label}/{key}")
    records = aggregate["processes"]
    require(len(records) == 73 and {r["name"] for r in records} == EARLY_NAMES, "Wrong physical process selectors")
    values = {}
    for row in aggregate["caseResults"]:
        name = "normal-debugger" if row["debuggerProof"] else (
            f"fault-{row['faultStage']}-{row['faultIndex']}" if row["faultStage"] else f"mode-{row['mode']}")
        require(name not in values, "Duplicate case result")
        values[name] = row
    require(set(values) == EARLY_NAMES, "Missing actual verified case")
    for record in records:
        name = record["name"]
        directory = cases / name
        require(record["started"] and not record["timedOut"] and record["exactOwnedProcess"], "Unfinished/foreign child")
        require(read(directory / "process.json") == record, "Raw process differs from aggregate")
        actual = read(directory / "result.json")
        require(actual == values[name] and actual["passed"] and actual["checks"] > 0, "Raw case differs from aggregate")
        require(actual["actualExitCode"] == record["exitCode"], "Child exit differs from verified mapping")
        require(actual["mode"] == record["mode"] and actual["faultStage"] == record["faultStage"]
                and actual["faultIndex"] == record["faultIndex"], "Wrong child selector association")
        if not record["debugger"]:
            require(actual["ownedChildPid"] == record["pid"], "Non-debug target is not the exact started process")
        final, initial = directory / "mapping-final.bin", directory / "mapping-initial.bin"
        require(final.stat().st_size == initial.stat().st_size == 449120, "Missing actual complete mapping")
        require(sha(final) == actual["mappingSha256"] and sha(initial) == actual["initialMappingSha256"], "Mapping hash differs")
        header = final.read_bytes()[:328]
        magic, version, size, capacity = struct.unpack_from("<4I", header)
        require((magic, version, size, capacity) == ((0x4b465245, 3, 449120, 4096)
                if profile == "engine-kernel" else (0x58465245, 2, 449120, 4096)), "Wrong actual wire profile")
        require(struct.unpack_from("<I", header, 48)[0] == actual["ownedChildPid"], "Actual child identity differs")
        require(len(actual["actualMemoryRows"]) == 4, "Missing independent source memory")
    debug = values["normal-debugger"]["debuggerProof"]
    require(debug["instrumented"] is True and debug["rawJsonlSha256"] == sha(cases / "normal-debugger/debug-events.jsonl"),
            "Missing actual debugger raw hash")
    if profile == "engine-kernel":
        require(values["normal-debugger"]["kernelPeContract"]["passed"], "Missing exact Kernel PE proof")
        for key in ["allDebugRowsMatchedShared", "finalSelectedMappingsUniqueVerified",
                    "allNonselectedMappingsRetiredBeforeProxyVerified", "parentDiskFileIdentityVerified",
                    "hardwareReturnStackAndSlotCleanupVerified", "actualKernelLoadBeforeProxyVerified",
                    "actualPublisherLoadBeforeProxyVerified", "zeroSyntheticKernelInitializationRowsVerified"]:
            require(debug.get(key) is True, f"Missing final debugger contract: {key}")
        raw_debug = [json.loads(line) for line in (cases / "normal-debugger/debug-events.jsonl").read_text(encoding="utf-8-sig").splitlines() if line]
        require(debug["observedRows"] == len(raw_debug), "Incomplete debugger row proof")
        require(debug.get("completeOsLoaderLifetimeClosureVerified") is False, "Expanded OS lifetime claim")
        require(debug["retiredUnloadNotifications"] == sum(r["type"] == 1007 for r in raw_debug), "Wrong actual retired-unload count")
        require(debug["retiredUnloadNotificationsObserved"] is (debug["retiredUnloadNotifications"] > 0),
                "Unobserved retired-unload coverage was fabricated")
    count = held_inputs(aggregate, cases / "held-inputs.json")
    require(aggregate["checks"] == sum(v["checks"] for v in values.values()), "Aggregate assertion sum differs")
    return dict(profile=profile, cases=73, independentAssertions=sum(v["checks"] for v in values.values()),
                heldFiles=count, actualOriginalCalls=sum(v["workload"]["actualOriginalCalls"] for v in values.values()),
                physicalMemorySnapshots=73, debuggerProof=debug, aggregateSha256=sha(cases / "fixture-result.json"))


def fixed_matrix(cache):
    cases = cache / "legacy-publisher-cases"
    aggregate = read(cases / "fixture-result.json")
    require(aggregate["passed"] is True and aggregate["verifiedCases"] == aggregate["expectedCases"] == 57,
            "Incomplete legacy publisher aggregate")
    for key in ["gameExecuted", "productionFirstInstallVerified", "loaderStageVerified", "engineCancellationCompleted", "installed"]:
        require(aggregate[key] is False, f"Unsupported publisher claim: {key}")
    rows = aggregate["caseResults"]
    require(len(rows) == 57 and {r["name"] for r in rows} == FIXED_NAMES, "Wrong publisher selectors")
    totals = dict(hostChecks=0, receiptAssertions=0, unwindAssertions=0, controlledUnwindCases=0)
    for row in rows:
        directory = cases / row["name"]
        process = read(directory / "process.json")
        require(process == row["process"] and process["started"] and not process["timedOut"] and process["exitCode"] == 0,
                "Publisher owned process did not really complete")
        host = read(directory / "child.stdout.log")
        require(host == row["host"] and host["passed"] and host["failures"] == 0 and host["controlledUnwindCases"] == 50,
                "Missing real native publisher host result")
        for key, filename in [("receipt", "receipt-proof.json"), ("unwind", "unwind-proof.json")]:
            value = read(directory / filename)
            require(value == row[key] and value["passed"] and value["checks"] > 0, "Missing independent publisher proof")
        require((directory / "case.receipt.bin").stat().st_size == 21640
                and (directory / "case.prepared.bin").stat().st_size == 21640
                and (directory / "case.memory.bin").stat().st_size == 240, "Missing raw publisher scalar/memory evidence")
        totals["hostChecks"] += host["checks"]
        totals["receiptAssertions"] += row["receipt"]["checks"]
        totals["unwindAssertions"] += row["unwind"]["checks"]
        totals["controlledUnwindCases"] += host["controlledUnwindCases"]
    calls = read(cases / "normal/calls-proof.json")
    require(calls["passed"] and calls["checks"] > 0, "Missing actual normal calls proof")
    return dict(cases=57, heldFiles=held_inputs(aggregate, cases / "held-inputs.json"),
                callsAssertions=calls["checks"], aggregateSha256=sha(cases / "fixture-result.json"), **totals)


def binary_rows(cache):
    sets = {
        "kernel-chain": ["RizKernelOwnedHost.exe", "RizFixedOwnedTargets.dll", "RizKernelOwnedProxy.dll",
                         "RizKernelOwnedRecorder.dll", "RizToolbox.EngineNodeKernel.dll", "RizKernelFixedPublisher.dll", "RizKernelOwnedDebugger.exe"],
        "legacy-publisher": ["RizFixedPublisher.dll", "RizFixedOwnedTargets.dll", "FixedPublisher.Fixture.exe", "RizToolbox.JudgeContext.dll"],
        "legacy-chain": ["RizOwnedHost.exe", "RizFixedOwnedTargets.dll", "RizOwnedProxy.dll", "RizOwnedRecorder.dll",
                         "RizToolbox.JudgeContext.dll", "RizFixedPublisher.dll", "RizOwnedDebugger.exe"],
    }
    result = []
    for group, names in sets.items():
        manifest = read(cache / group / "fixture-build-manifest.json")
        require(len(manifest["files"]) == len(names), "Wrong exact built binary set")
        for name in names:
            rows = [r for r in manifest["files"] if r["name"] == name]
            path = cache / group / name
            require(len(rows) == 1 and same_path(rows[0]["path"], path) and rows[0]["sha256"] == sha(path),
                    f"Completed binary manifest mismatch: {group}/{name}")
            result.append(dict(path=f"{group}/{name}", sha256=sha(path), bytes=path.stat().st_size))
        for row in manifest["sources"]:
            require(sha(Path(row["path"])) == row["sha256"], "Built transitive source drift")
    require(len(result) == 18, "Expected 7+4+7 binary rows")
    require(sha(cache / "kernel-publisher/RizKernelFixedPublisher.dll")
            == sha(cache / "kernel-chain/RizKernelFixedPublisher.dll"), "Publisher copied differently")
    return result


def history_proofs():
    first_path = BATCH / "history/first-attempt/terminal-observation.json"
    first = read(first_path)
    require(first["sessionId"] == 31803 and first["actualTerminalExitCode"] == 1
            and first["kernelNonDebuggerVerified"] == 72 and first["kernelDebuggerExitCode"] == 1
            and first["legacyBranchesStarted"] is False, "First failed attempt was rewritten")
    rows = [dict(path=first_path.relative_to(BATCH).as_posix(), sha256=sha(first_path), result=first)]
    for filename, source, archive, session, children in [
        ("diagnostic-terminal-observation.json", "diagnostic-debugger.cpp", "diagnostic-initial.zip", 49229, [1, 1, 1]),
        ("lifetime-diagnostic-terminal-observation.json", "lifetime-debugger-first.cpp", "lifetime-diagnostic-initial.zip", 96108, [1, 1, 1]),
        ("hardware-diagnostic-terminal-observation.json", "hardware-debugger-first.cpp", "hardware-diagnostic-initial.zip", 52109, [0, 0, 0]),
    ]:
        path = BATCH / "history" / filename
        value = read(path)
        require(value["sessionId"] == session and value["actualDriverTerminalExitCode"] == 0
                and value["actualDiagnosticChildrenExitCodes"] == children, "Diagnostic driver/child outcomes changed")
        source_path, archive_path = BATCH / "history" / source, BATCH / "history" / archive
        require(sha(source_path) == value["sourceSha256"], "Diagnostic source identity differs")
        with zipfile.ZipFile(archive_path) as zipped:
            require(zipped.testzip() is None and len(zipped.namelist()) > 0, "Missing diagnostic original ZIP")
        rows.append(dict(path=path.relative_to(BATCH).as_posix(), sha256=sha(path), result=value,
                         source=source_path.relative_to(BATCH).as_posix(), sourceSha256=sha(source_path),
                         archive=archive_path.relative_to(BATCH).as_posix(), archiveSha256=sha(archive_path)))
    preflight_path = BATCH / "history/final-preflight-terminal-observation.json"
    preflight = read(preflight_path)
    require(preflight["sessionId"] == 69351 and preflight["actualDriverTerminalExitCode"] == 0
            and preflight["actualDiagnosticChildrenExitCodes"] == [0, 0, 0]
            and preflight["passingFormalFixtureClaim"] is False, "Wrong strengthened preflight outcome")
    source_path = REPO / "native/RizToolbox.EarlyFixedFixture/EarlyFixedDebugger.cpp"
    require(sha(source_path) == preflight["sourceSha256"] == "b8e1d6f982066bd92bb1fd78cc123352928a8de1930c60a7f303f831efacbe97",
            "Final debugger differs from the strengthened preflight")
    archive_path = BATCH / "history/final-preflight.zip"
    with zipfile.ZipFile(archive_path) as archive:
        require(archive.testzip() is None and archive.namelist(), "Missing strengthened preflight originals")
    rows.append(dict(path=preflight_path.relative_to(BATCH).as_posix(), sha256=sha(preflight_path), result=preflight,
                     source=source_path.relative_to(REPO).as_posix(), sourceSha256=sha(source_path),
                     archive=archive_path.relative_to(BATCH).as_posix(), archiveSha256=sha(archive_path)))
    return rows


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cache", type=Path, default=DEFAULT_CACHE)
    parser.add_argument("--terminal", type=Path, default=BATCH / "terminal.json")
    parser.add_argument("--game-root", type=Path, default=Path("D:/SteamLibrary/steamapps/common/Rizline"))
    parser.add_argument("--check-only", action="store_true")
    args = parser.parse_args()
    # This gate comes before creating any final archive/output directory.
    terminal = read(args.terminal)
    require(same_path(args.terminal, BATCH / "terminal.json"), "Use this batch's actual terminal record")
    require(type(terminal.get("sessionId")) is int and terminal["sessionId"] > 0
            and type(terminal.get("exitCode")) is int and terminal["exitCode"] == 0
            and terminal.get("terminalExitCodeObserved") is True and terminal.get("rootSoleBuilder") is True
            and same_path(terminal["cache"], args.cache), "Missing root-observed actual successful terminal")
    require(same_path(args.cache, DEFAULT_CACHE), "This archive only accepts the fixed final cache")
    command = read(BATCH / "command-result.json")
    require(command["completed"] is True and same_path(command["cache"], args.cache)
            and [r["name"] for r in command["steps"]] == STEPS
            and all(r["completed"] is True for r in command["steps"]), "Incomplete actual seven-step command")
    require(command["managedFullBuildRerun"] is False and command["installed"] is False
            and command["gameExecuted"] is False, "Expanded execution claim")
    frozen = read(BATCH / "inputs-final-before.json")["files"]
    verify_rows(frozen, REPO, 604)
    verify_zip(BATCH / "source-final-inputs.zip", frozen)
    base = {r["path"]: r["sha256"] for r in read(BATCH.parent / "foundation-kernel-install-20261025/source-final-manifest.json")["files"]}
    final = {r["path"]: r["sha256"] for r in frozen}
    expected_changes = {"native/RizToolbox.EarlyFixedFixture/" + name for name in
                        ["EarlyFixedDebugger.cpp", "EarlyFixedShared.h", "Host.cpp", "Importer.cpp", "ImporterApi.h", "Proxy.cpp", "Recorder.cpp"]}
    expected_changes |= {"native/RizToolbox.FixedPublisher/FixedPublisher.cpp", "native/RizToolbox.FixedPublisher/FixedPublisher.h",
                         "scripts/Build-EarlyFixedFixture.ps1", "scripts/Build-FixedPublisherFixture.ps1", "scripts/Test-EarlyFixedFixture.ps1"}
    require(len(base) == 599 and {p for p in base if base[p] != final.get(p)} == expected_changes, "Unexpected base-source differences")
    require(set(final) - set(base) == {"native/RizToolbox.EarlyFixedFixture/EarlyFixedProfile.h",
            "native/RizToolbox.FixedPublisher/FixedPublisherProfile.h",
            "docs/validation/foundation-early-fixed-20261025/verify-early-fixed.py",
            "docs/validation/foundation-fixed-publisher-20261025/verify-fixed-publisher.py",
            "docs/validation/foundation-early-kernel-20261025/verify-early-kernel.py"}, "Wrong new declarations")
    kernel = early_matrix(args.cache, "kernel-cases", "engine-kernel")
    fixed = fixed_matrix(args.cache)
    legacy = early_matrix(args.cache, "legacy-cases", "legacy")
    binaries = binary_rows(args.cache)
    history = history_proofs()
    old_package = read(BATCH.parent / "foundation-engine-kernel-20261025/package-manifest.json")["files"]
    verify_rows(old_package, REPO / "artifacts/package", 23)
    preservation = read(BATCH.parent / "foundation-kernel-install-20261025/real-game-preservation-check.json")
    game = dict(installedFiles=[], originalFiles=[])
    for key, count in [("installedFiles", 18), ("originalFiles", 4)]:
        require(len(preservation[key]) == count, "Wrong prior game preservation shape")
        for row in preservation[key]:
            actual = sha(args.game_root / safe_relative(row["path"]))
            require(actual == row["expected"] == row["actual"], "Real game or old installation changed")
            game[key].append(dict(path=row["path"], expected=row["expected"], actual=actual))
    game.update(allMatched=True, gameRoot=str(args.game_root.resolve()), realGameExecuted=False, realGameModified=False)
    summary = dict(schemaVersion=1, kind="early-kernel-final-owned-matrix", passed=True,
                   sessionId=terminal["sessionId"], actualTerminalExitCode=0, rootObservedTerminal=True,
                   sourceInputs=604, baseSourceInputs=599, intentionalModifiedInputs=12, addedDeclarations=5,
                   sourceZipSha256=sha(BATCH / "source-final-inputs.zip"), binaryRows=18,
                   kernel=kernel, legacyPublisher=fixed, legacyEarly=legacy, independentProcesses=203,
                   history=history,
                   packageFilesUnchanged=23, coreFullSuitesRerun=False, previousCoreFullSession=23528,
                   wireVersion=3, publisherRecordVersion=2, transportAbiVersion=2, scalarRecordVersion=1,
                   ownedOnly=True, productionV4Issued=False, gameFirstInstallVerified=False,
                   allTransitiveOsDependenciesSafeVerified=False, linuxRuntimeVerified=False,
                   engineCancellationCompleted=False, globalRetirementVerified=False, installed=False, gameExecuted=False)
    if args.check_only:
        print(json.dumps(dict(checkOnly=True, **summary)))
        return
    require(not (BATCH / "final-summary.json").exists(), "Final archive already exists; do not overwrite history")
    raw_rows = copy_exact_tree(args.cache, BATCH / "raw/cache")
    write("source-final-manifest.json", dict(files=frozen, scope="604 declared inputs; no second full managed build"))
    write("binary-final-manifest.json", dict(files=binaries, scope="7 Kernel + 4 legacy-publisher + 7 legacy-chain rows; not a game package"))
    write("raw-final-manifest.json", dict(files=raw_rows, originalCache=str(args.cache.resolve())))
    write("real-game-preservation-check.json", game)
    write("package-preservation-check.json", dict(files=old_package, allMatched=True, unchangedSinceCore23528=True))
    make_zip(BATCH / "final-binaries.zip", BATCH / "raw/cache", binaries)
    make_zip(BATCH / "final-cache-raw.zip", BATCH / "raw/cache", raw_rows)
    write("source-final-archive-check.json", dict(entries=604, archive="source-final-inputs.zip",
          archiveSha256=summary["sourceZipSha256"], manifestSha256=sha(BATCH / "source-final-manifest.json"), allEntryHashesMatched=True))
    write("binary-final-archive-check.json", dict(entries=18, archive="final-binaries.zip",
          archiveSha256=sha(BATCH / "final-binaries.zip"), manifestSha256=sha(BATCH / "binary-final-manifest.json"), allEntryHashesMatched=True))
    write("raw-final-archive-check.json", dict(entries=len(raw_rows), archive="final-cache-raw.zip",
          archiveSha256=sha(BATCH / "final-cache-raw.zip"), manifestSha256=sha(BATCH / "raw-final-manifest.json"), allEntryHashesMatched=True))
    summary.update(binaryZipSha256=sha(BATCH / "final-binaries.zip"), rawZipSha256=sha(BATCH / "final-cache-raw.zip"),
                   rawFiles=len(raw_rows), terminalSha256=sha(args.terminal), commandSha256=sha(BATCH / "command-result.json"),
                   archiverSha256=sha(Path(__file__)), reportGeneratorSha256=sha(BATCH / "write-report.py"))
    write("source-final-integrity-check.json", dict(checked=604, changed=[], matchesFrozenInputAndZip=True))
    write("final-summary.json", summary)
    print(json.dumps(summary))


if __name__ == "__main__":
    main()
