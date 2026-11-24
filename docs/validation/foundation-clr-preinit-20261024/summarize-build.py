"""Record this completed staged build; optionally check it and the separate installed 1021 batch.

No installation, process stop, game launch or UI action. The installed source is verified
from its frozen archive, because the working tree intentionally contains newer changes.
"""
import argparse
import hashlib
import json
import re
import shutil
import zipfile
from datetime import datetime, timezone
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--staged", action="store_true")
parser.add_argument("--native-output", type=Path, default=Path(r"D:\RizToolbox-build-cache\clr-preinit-final-20261024"))
args = parser.parse_args()
root = Path(__file__).resolve().parent
repo = root.parents[2]
installed_batch = root.parent / "foundation-local-protection-host-20261022"
game = Path(r"D:\SteamLibrary\steamapps\common\Rizline")


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def write(name, data):
    (root / name).write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


if not args.staged:
    log = root / "build.log"
    body = log.read_text(encoding="utf-8-sig")
    suites = []
    for line in body.splitlines():
        match = re.fullmatch(r".*?(\d+)/(\d+).*passed\.", line.strip())
        if match:
            suites.append(dict(summary=line.strip(), passed=int(match[1]), total=int(match[2])))
    assert len(suites) == 25 and all(s["passed"] == s["total"] for s in suites)
    assert "Package: " in body and "FAIL " not in body and not re.search(r"error (?:CS|MSB|NU|C|D|LNK)\d+", body)
    fixtures = [json.loads(line) for line in body.splitlines() if line.startswith('{"schemaVersion":1,"kind":"real-windows-x64-native-fixture"')]
    assert len(fixtures) == 2 and all(f["passed"] and f["inFlight"] == 0 for f in fixtures)
    assert fixtures[1]["realDobbyPreparedCommit"]
    native_unwind = read(args.native_output / "initialization-context" / "fixture-result.json")
    assert native_unwind["passed"] and native_unwind["checks"] == 1747 and native_unwind["failures"] == 0
    assert all(native_unwind[key] for key in ["realDobby", "prepareConfigureCommit", "configureRace", "typedStart", "typedGetter", "originalSehPreserved", "originalCppPreserved", "nativeWorker", "callbacksContained"])
    assert not native_unwind["gameRuntimeVerified"]
    assert all(native_unwind[key]["entered"] == native_unwind[key]["exited"] and native_unwind[key]["inFlight"] == 0 for key in ["start", "getter"])
    initialization_probe = read(args.native_output / "initialization-abi" / "native-probe-result.json")
    assert initialization_probe["calls"] == 18 and initialization_probe["worker"] and initialization_probe["rawPlayEdxPoison"]
    assert not initialization_probe["managedTransportVerified"] and not initialization_probe["gameRuntimeVerified"]
    assert "PASS sealed initialization real Windows x64 native ABI, Dobby, worker, recursion and null abort" in body
    assert "PASS native Judge activity covers real Dobby worker, detach, context cleanup and original SEH C++ exits" in body
    assert "PASS native coroutine Start and MoveNext preserve real Dobby ABI, caller, worker, nested publication and SEH C++ unwind" in body
    assert "PASS native coroutine Factory preserves full references and publishes an actual enum before a named Start first MoveNext" in body
    void_cases = [
        "native void0 preserves real Dobby arguments, exact tail-relay caller, foreign recursion and probe snapshots",
        "native void0 keeps original SEH and C++ unwind, worker activity and logical detach inside the physical window",
        "native void0 contains managed capture, after and release failures while calling the native original once",
        "native void0 native callback SEH and C++ faults preserve exact flags, original exceptions and Prepare Configure Commit",
    ]
    assert all("PASS " + name in body for name in void_cases)
    write("native-void-evidence.json", dict(
        source="Completed Runtime suite in build.log; each case contains assertions over actual Windows x64 Dobby/MASM/native helper/fixture behavior.",
        passedCases=void_cases, realDobby=True, nativeOriginalVoid0=True, exactAssemblyCallerThroughTailRelay=True,
        nativeSehAndCppCodeKindPayloadPreserved=True, workerAndLogicalDetachInFlight=True,
        physicalProbeSnapshotPairs=True, managedObserverFailuresContained=True,
        nativeCallbackModes=7, nativeOriginalModes=3, immutablePrepareConfigureCommit=True,
        gameRuntimeVerified=False,
        limitations="No UnityEvent game execution, inline executing-list capture, actual listener leaves, shared-RVA leaf ABI, cancellation or whole-run consumer-end proof."))
    mid = read(args.native_output / "event-mid-context" / "fixture-result.json")
    assert mid["kind"] == "native-event-mid-full-state-fixture"
    assert mid["passed"] and mid["checks"] > 0 and mid["failures"] == 0
    assert mid["entered"] == mid["exited"] and mid["inFlight"] == 0
    assert all(mid[key] for key in ["allGpr", "allXmm", "rflags", "mxcsr", "fpcsr", "stackCanaries", "exactFiveByteCall", "nearTailRelay", "fixtureOnlyPatch"])
    assert not mid["nativeParentVerified"] and not mid["gameRuntimeVerified"]
    write("native-mid-evidence.json", mid)
    joint_cases = [
        "Native UnityEvent joint parent and mid preserve actual caller token empty lists and foreign recursion",
        "Native UnityEvent joint worker tail window and logical detach retain parent physical responsibility",
        "Native UnityEvent joint adoption faults and native SEH Cpp originals preserve exact continuation and cleanup",
        "Native UnityEvent joint next-generation reused context refuses a stale physical identity before adoption",
        "Native UnityEvent joint clean adopted inline scope survives original SEH with paired cleanup and retained roots",
        "Native UnityEvent joint clean adopted inline scope survives original Cpp with paired cleanup and retained roots",
    ]
    assert all("PASS " + name in body for name in joint_cases)
    write("native-parent-mid-evidence.json", dict(
        source="Six named AudioContent cases executed in isolated native-enabled processes in build.log; assertions cover the actual Dobby/MASM/helper/CLR/broker/ledger chain.",
        passedCases=joint_cases, realDobbyParent=True, realNativeMidCall=True, actualReturnOffset=0x140,
        midToManagedBridge=True, brokerCurrentPrivateIdentity=True, ledgerReceivesActualMidFrame=True,
        emptyAndNonemptyLists=True, foreignReceiverAndMethodRecursion=True,
        postMidWorkerParentStillInFlight=True, logicalDetachRetainsParent=True,
        controlledAdoptionFaultRetainsResponsibility=True, nativeSehCppCodeKindPayloadPreserved=True,
        cleanOwnerAdoptsBeforeNativeSehCpp=True, adoptedRootsRetainedAndContextsPairedOnAbnormalExit=True,
        staleCapabilityRejectedBeforeAdoption=True, backendGraphAndRootsControlled=True,
        fixtureOnlyPreInvocationPatch=True, gamePatchPublished=False, gameRuntimeVerified=False,
        limitations="No IL2CPP graph/GC, production resolver or live instruction publication, actual listener invocation/mutation closure, cancellation or whole-run retirement acceptance."))
    admission = read(args.native_output / "void-admission" / "fixture-result.json")
    assert admission["passed"] and admission["failures"] == 0 and admission["checks"] > 0
    assert not admission["initialInstallSafetyVerified"] and not admission["gameRuntimeVerified"]
    assert "PASS Native admission typed receipt and statistics preserve the exact 32 and 40 byte ABI" in body
    assert "PASS Native admission writes and retained unknown outcomes prevent Dispose from opening the gate" in body
    write("native-admission-evidence.json", admission)
    bootstrap = read(args.native_output / "bootstrap-void" / "fixture-result.json")
    assert bootstrap["passed"] and bootstrap["failures"] == 0 and bootstrap["checks"] > 0
    assert not bootstrap["gameRuntimeVerified"]
    assert "PASS Native bootstrap adopted bridge preserves pre-attach physical calls and exact late-attached native mid callbacks" in body
    write("native-bootstrap-evidence.json", bootstrap)
    runtime = read(args.native_output / "runtime-invoke" / "fixture-result.json")
    assert runtime["passed"] and runtime["failures"] == 0 and runtime["originalCalls"] == 12
    assert runtime["entered"] == runtime["exited"] == 12 and runtime["inFlight"] == 0
    assert runtime["afterCalls"] == 8 and runtime["oldUnobservedCalls"] == 2 and runtime["observerFaults"] == 2
    assert not runtime["gameRuntimeVerified"]
    write("native-runtime-invoke-evidence.json", runtime)
    rejection_summary = read(args.native_output / "first-install-rejected" / "fixture-result.json")
    assert rejection_summary["passed"] and rejection_summary["independentProcesses"] == 5 and rejection_summary["writesStarted"] == 0
    rejected = rejection_summary["modes"]
    assert len(rejected) == 5 and all(row["passed"] and row["failures"] == 0 and row["writesStarted"] == 0 for row in rejected)
    write("native-rejection-evidence.json", dict(cases=rejected, passed=True, gameRuntimeVerified=False))
    mapped = read(args.native_output / "first-install-mapped" / "fixture-result.json")
    assert mapped["passed"] and mapped["failures"] == 0 and mapped["phase"] == 3 and mapped["unknown"] == 0 and mapped["writesStarted"] == 3
    assert mapped["mappedImageOnly"] and not mapped["gameCodeExecuted"] and not mapped["gameRuntimeVerified"]
    assert not mapped["gameInitExecuted"] and mapped["transportInvocations"] == 0
    assert mapped["phaseObservation"] == "before-ready-reentry"
    assert mapped["readyReentryRejected"] and mapped["readyReentryStatus"] == 2 and mapped["reentryBytesUnchanged"]
    assert mapped["currentPhase"] == 4 and mapped["currentUnknown"] == 1 and mapped["currentWritesStarted"] == 3
    assert all(mapped[key] for key in ["actualParentRuntimeBytesChanged", "actualMidCallAndRelayVerified", "pdataAndRtlLookupAgreed", "pageProtectionsRestored"])
    assert mapped["repeatedKnownInfoReads"] == 10 and not mapped["firstInstallSafetyVerified"]
    write("mapped-publication-evidence.json", mapped)
    doorstop = read(args.native_output / "doorstop" / "doorstop-manifest.json")
    assert doorstop["upstreamArchiveSha256"] == "a71789ffcb359f6a37c6d2c7bbdd0527231e9db00bf46637693cff88ba7ffb80"
    write("doorstop-evidence.json", doorstop)
    managed = read(args.native_output / "first-install-managed" / "fixture-result.json")
    assert managed["passed"] and managed["independentProcesses"] == 2 and managed["failures"] == 0
    assert not managed["gameRuntimeVerified"] and not managed["firstInstallSafetyVerified"]
    assert {row["mode"] for row in managed["modes"]} == {"happy", "reentry"}
    for row in managed["modes"]:
        assert row["passed"] and row["failures"] == 0 and row["checks"] > 0
        assert all(row[key] for key in ["noProducerRejected", "realNativeProducerReady", "productionRequireReadyVerified", "metadataIdentityControlled", "mappedImageOnly", "diskHashesUnchanged"])
        assert not any(row[key] for key in ["actualIl2CppMetadataResolved", "gameCodeExecuted", "gameInitExecuted", "nativeToClrCallbackVerified", "actualLedgerCaptureVerified", "firstInstallSafetyVerified", "gameRuntimeVerified"])
        assert row["transportInvocations"] == row["managedObserverCallbacks"] == 0 and row["writesStarted"] == 3 and row["voidSlot"] == 31
        if row["mode"] == "happy":
            assert all(row[key] for key in ["productionRuntimeAdoptVerified", "productionBrokerAdoptVerified", "duplicateRuntimeRejected", "wrongTargetRejected", "conflictingMetadataRejected", "doubleSinkSharedEntry", "logicalDetachPreservedEntry"])
        else:
            assert row["originalReentryRejected"] and not row["productionRuntimeAdoptVerified"] and not row["productionBrokerAdoptVerified"]
    write("managed-adoption-evidence.json", managed)
    unknown = read(args.native_output / "first-install-unknown" / "fixture-result.json")
    assert unknown["passed"] and unknown["failures"] == 0 and unknown["checks"] > 0
    assert all(unknown[key] for key in ["unknownAtomicPrecedesEveryRead", "detailWriterPausedDuringReads", "productionFirstInstallSourceExecuted", "fixtureCompileTimeSeam", "nativeTransportsFromRealHelper", "mappedImageOnly"])
    assert unknown["readsWhileWriterPaused"] == unknown["observedUnknownSnapshots"] == 100
    assert unknown["readySnapshotPhase"] == 3 and unknown["readySnapshotUnknown"] == 0 and unknown["writesStarted"] == 3
    assert unknown["reentryStatus"] == 2 and unknown["currentPhase"] == 4 and unknown["currentUnknown"] == 1 and unknown["currentWritesStarted"] == 3
    assert not any(unknown[key] for key in ["gameCodeExecuted", "gameInitExecuted", "firstInstallSafetyVerified", "gameRuntimeVerified"])
    assert unknown["transportInvocations"] == 0
    write("atomic-unknown-evidence.json", unknown)
    init_call = read(args.native_output / "doorstop-init" / "fixture-result.json")
    assert init_call["passed"] and init_call["failures"] == 0 and init_call["checks"] > 0
    assert init_call["independentProcesses"] == 16 and init_call["normalCases"] == 12 and init_call["realFailFastCases"] == 4
    assert init_call["productionSourceExecuted"] and init_call["werNoUi"] and all(row["passed"] for row in init_call["cases"])
    assert not any(init_call[key] for key in ["actualProducerExecuted", "coreClrExecuted", "gameCodeExecuted", "coldStartupVerified"])
    assert all(row["realExitCode"] == 3221227010 for row in init_call["cases"] if "realExitCode" in row)
    write("init-call-evidence.json", init_call)
    total = sum(s["total"] for s in suites)
    write("build-summary.json", dict(completedAtUtc=datetime.now(timezone.utc).isoformat(), projects=len(suites),
        passed=total, total=total, suites=suites, nativeFixtures=fixtures, initializationNativeOnlyProbe=initialization_probe, typedInitializationNativeUnwind=native_unwind, nativeEventMid=mid, nativeVoidAdmission=admission, nativeBootstrapAttach=bootstrap, nativeRuntimeInvoke=runtime, mappedPublication=mapped, managedAdoption=managed, atomicUnknown=unknown, productionInitCall=init_call, logSha256=sha(log),
        nativeAcceptance="Offline helper/Dobby plus production sealed Start/getter and Judge activity native worker/detach/SEH/C++ fixture. Coroutine Start/MoveNext additionally preserve exact assembly return labels through a tail relay/Dobby, synchronous first MoveNext, raw AL/reference returns, worker/detach and original SEH/C++ exits. Managed inner Judge/Hold and Play/Progress/Timeline original unwind, actual game metadata/coroutine ownership, the new native void0 transport and real Dobby fixture additionally verify exact caller, physical probes and original unwind; the Adapter UnityEvent graph/parent metadata and listener metadata and launch/result/menu composition, full launch, active consumer retirement and game acceptance remain separate/unverified. The new standalone native mid fixture covers its own five-byte CALL and register/unwind transport; controlled Hooks/Adapter cases cover physical identity and root adoption. The joint real Dobby parent to native mid CALL to managed bridge/broker and ledger is checked separately in AudioContent; backend graph and roots remain controlled. Concurrent production instruction publication, actual game execution and listener closure remain unverified. This batch additionally verifies native admission after an existing Dobby parent and the real bridge lease. The older admission fixtures do not verify first installation; this batch implements the producer and checks its actual publication separately on a mapped image. This batch additionally verifies a one-time native observer attach with per-invocation snapshots and a CLR bridge adopting a configured slot. The production Doorstop pre-init producer and managed adoption path are implemented and built; mapped-image publication validates bytes without executing game code. This batch additionally executes the real producer-to-CLR RequireReady, runtime observer Attach and broker two-idle-sink adoption on the mapped game image; controlled metadata is explicit, with no original/callback/game execution. The actual shared production C init-call function is checked separately with controlled native originals in 16 processes. A compile-time-only seam verifies 100 GetInfo calls while the real unknown writer is paused after its atomic store, preventing stale Ready. Real supported startup timing, production CLR callbacks, listener/GC closure and game acceptance remain unverified. Staged package is not installed.",
        compilerDiagnosticLines=sorted(set(line.strip() for line in body.splitlines() if re.search(r"\b(?:warning|error) (?:CS|MSB|NU|C|D|LNK)\d+", line, re.I)))))
    files = []
    for directory in ["src", "tests", "examples", "scripts", "native", "templates", "tools", "packaging"]:
        for path in (repo / directory).rglob("*"):
            if path.is_file() and path.suffix in {".cs", ".csproj", ".props", ".targets", ".ps1", ".cpp", ".c", ".h", ".asm", ".json"} and not {"bin", "obj", "out"}.intersection(path.relative_to(repo).parts):
                files.append(dict(path=path.relative_to(repo).as_posix(), sha256=sha(path)))
    path = repo / "native/RizToolbox.Doorstop/RIZLINE-MODIFICATIONS.md"
    files.append(dict(path=path.relative_to(repo).as_posix(), sha256=sha(path)))
    for name in ["Directory.Build.props", "Directory.Build.targets", "RizToolbox.sln", "dependencies.lock.json", "global.json"]:
        if (repo / name).is_file():
            files.append(dict(path=name, sha256=sha(repo / name)))
    write("source-manifest.json", dict(files=sorted(files, key=lambda f: f["path"])))
    shutil.copy2(repo / "artifacts/package-manifest.json", root / "package-manifest.json")
    (root / "native-build").mkdir(exist_ok=True)
    for name in ["native-manifest.json", "build.log", "exports.txt", "unwindinfo.txt", "fixture-result.json", "publication-fixture-result.json"]:
        source = args.native_output / name
        assert source.is_file(), source
        shutil.copy2(source, root / "native-build" / name)
    shutil.copy2(args.native_output / "RizToolbox.JudgeContext.dll", root / "native-build" / "RizToolbox.JudgeContext.dll")
    native_context = root / "native-build" / "initialization-context"
    native_context.mkdir(exist_ok=True)
    for name in ["build.log", "fixture-result.json", "fixture-manifest.json", "InitializationContext.Fixture.exe"]:
        shutil.copy2(args.native_output / "initialization-context" / name, native_context / name)
    mid_output = root / "native-build" / "event-mid-context"
    mid_output.mkdir(exist_ok=True)
    for name in ["build.log", "fixture-result.json", "fixture-manifest.json", "unwindinfo.txt", "disassembly.txt", "EventMidContext.Fixture.exe"]:
        shutil.copy2(args.native_output / "event-mid-context" / name, mid_output / name)
    admission_output = root / "native-build" / "void-admission"
    admission_output.mkdir(exist_ok=True)
    for name in ["build.log", "fixture-result.json", "fixture-manifest.json", "unwindinfo.txt", "VoidAdmission.Fixture.exe"]:
        shutil.copy2(args.native_output / "void-admission" / name, admission_output / name)
    bootstrap_output = root / "native-build" / "bootstrap-void"
    bootstrap_output.mkdir(exist_ok=True)
    for name in ["build.log", "fixture-result.json", "fixture-manifest.json", "unwindinfo.txt", "BootstrapVoid.Fixture.exe"]:
        shutil.copy2(args.native_output / "bootstrap-void" / name, bootstrap_output / name)
    for folder in ["runtime-invoke", "first-install-rejected", "first-install-mapped", "doorstop", "first-install-managed", "first-install-unknown", "doorstop-init"]:
        destination = root / "native-build" / folder
        destination.mkdir(exist_ok=True)
        for source in (args.native_output / folder).iterdir():
            if source.is_file() and source.suffix in {".json", ".log", ".txt", ".exe", ".dll", ".zip"}:
                shutil.copy2(source, destination / source.name)
    initialization = root / "native-build" / "initialization-abi"
    initialization.mkdir(exist_ok=True)
    for name in ["native-manifest.json", "build.log", "exports.txt", "disassembly.txt", "unwindinfo.txt", "native-probe-result.json",
                 "RizToolbox.InitializationAbiFixture.dll", "InitializationAbi.Probe.exe"]:
        source = args.native_output / "initialization-abi" / name
        assert source.is_file(), source
        shutil.copy2(source, initialization / name)
    print(json.dumps(dict(projects=len(suites), passed=total, total=total, sourceFiles=len(files), nativeFixtureChecks=[f["checks"] for f in fixtures])))
else:
    staged = read(root / "package-manifest.json")
    assert len(staged["files"]) == 22
    checks = [dict(path=f["path"], expected=f["sha256"], actual=sha(repo / "artifacts/package" / f["path"])) for f in staged["files"]]
    assert all(f["expected"] == f["actual"] for f in checks)
    sources = read(root / "source-manifest.json")["files"]
    assert all(sha(repo / f["path"]) == f["sha256"] for f in sources)
    write("staging-check.json", dict(checkedAtUtc=datetime.now(timezone.utc).isoformat(), files=checks, allMatched=True, installed=False))
    write("source-integrity-check.json", dict(checked=len(sources), changed=[]))

    previous = read(installed_batch / "package-manifest.json")
    assert len(previous["files"]) == 18
    installed = [dict(path=f["path"], expected=f["sha256"], actual=sha(game / f["path"])) for f in previous["files"]]
    assert all(f["expected"] == f["actual"] for f in installed)
    archive_proof = read(installed_batch / "source-archive-check.json")
    assert sha(installed_batch / "source-inputs.zip") == archive_proof["archiveSha256"]
    assert sha(installed_batch / "source-manifest.json") == archive_proof["manifestSha256"]
    old_sources = read(installed_batch / "source-manifest.json")["files"]
    with zipfile.ZipFile(installed_batch / "source-inputs.zip") as archive:
        assert archive.testzip() is None and set(archive.namelist()) == {f["path"] for f in old_sources}
        assert all(hashlib.sha256(archive.read(f["path"])).hexdigest() == f["sha256"] for f in old_sources)
    write("previous-installation-check.json", dict(checkedAtUtc=datetime.now(timezone.utc).isoformat(),
        batch=installed_batch.name, files=installed, allMatched=True, archivedSourceInputsChecked=len(old_sources),
        scope="Installed 1021 batch and its archived inputs only; working-tree source is the newer staged build."))
    originals = read(installed_batch / "original-game-before.json")["files"]
    assert len(originals) == 3
    original_checks = [dict(path=f["path"], expected=f["sha256"], actual=sha(game / f["path"])) for f in originals]
    assert all(f["expected"] == f["actual"] for f in original_checks)
    write("original-game-check.json", dict(checkedAtUtc=datetime.now(timezone.utc).isoformat(), files=original_checks, allUnchanged=True))
    print(json.dumps(dict(stagedMatched=22, currentSourceInputs=len(sources), installedPreviousBatchMatched=18,
        archivedPreviousInputs=len(old_sources), originalGameFilesUnchanged=3, installedNewBatch=False)))
