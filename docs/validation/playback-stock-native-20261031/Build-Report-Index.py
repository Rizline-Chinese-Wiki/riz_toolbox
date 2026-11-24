"""Seal the already collected report and record its Evidence/Finding/Path graph."""
import hashlib
import json
from pathlib import Path

CASE = Path(__file__).resolve().parent


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def write(path, value):
    assert not path.exists(), path
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")


def main():
    verify = "& 'C:/Python314/python.exe' 'C:/Users/71957/Desktop/riz_toolbox/docs/validation/playback-stock-native-20261031/Verify-Report.py'"
    rows = [
        ("E-SOURCE", "Ten frozen owned inputs", "native-owned-inputs.json", "10 current source/script hashes and identical snapshots; helper protocol unchanged"),
        ("E-BUILD", "Actual fresh r6 build", "evidence/r6/build/build-result.json", "passed=true, actualExitCode=0, inputs/frozenSources unchanged, five translation units"),
        ("E-SYMBOL", "Actual PE/PDB/COFF/C13 identity", "evidence/r6/build/native-symbol-source-proof.json", "3 PE/PDB pairs, 5 objects with actual C13 source checksums, two fixed fixture bodies"),
        ("E-IMPORTS", "Actual static import/export inventory", "evidence/r6/build/native-symbol-source-proof.json", "Four imageSurveys preserve full imports/exports; Dobby has no PDB provenance claim"),
        ("E-DIR64", "COFF ADDR64 and executable PE DIR64", "evidence/r6/build/native-symbol-source-proof.json", ".text$mn offset0x740 ADDR64 symbol28 StockSetPitch; .text RVA0x1e70 DIR64 type10, outside runtime function ranges"),
        ("E-RUN", "Thirteen actual r6 processes", "evidence/r6/checks/test-result.json", "13 processes, 27037 checks, all child actualExitCode=0, each Dobby Prepared=Committed=8"),
        ("E-EXITS", "All original input/output and raw exit fixity", "artifact-index.json", "11 retained native directories, all raw logs/exit/input/output hashes; no self hash"),
        ("E-RELOCATION", "Actual relocated executable anchor", "evidence/r6/checks/normal.stdout.log", "base0x7ff8f2b00000 != disk preferred0x180000000; anchor RVA0x1e70 value0x7ff8f2b019b0"),
        ("E-AFTER", "No premature physical receipt in Returned", "evidence/r6/checks/after-inflight.stdout.log", "207 checks; Returned observer remains InFlight=1, worker cannot obtain physical receipt; final InFlight=0"),
        ("E-SEH", "Original write-then-SEH remains original", "evidence/r6/checks/original-seh.stdout.log", "287 checks; Entered=Exited=4, OriginalReturned=1, Abnormal=3; original codes0xe2400011/12 received by caller"),
        ("E-WITHDRAW", "Worker withdrawal retains entered responsibility", "evidence/r6/checks/worker-withdraw.stdout.log", "6212 checks; 301 original calls, armed in-flight receipt retained; later300 withdrawn calls add no persistent rows"),
        ("E-OVERFLOW", "Bounded journal does not overwrite", "evidence/r6/checks/journal-overflow.stdout.log", "5062 checks; 257 actual attempts, final InFlight0; sticky Unknown and256 prior rows retained"),
        ("E-BITS", "Raw argument and incoming method forwarding", "evidence/r6/checks/bits.stdout.log", "779 checks; five ABI, raw float/double/ref/bool/MI forwarding; mutated copied frame rejected"),
        ("E-NORMAL", "Actual nested stock-shaped schedule/reset/restore", "evidence/r6/checks/normal.stdout.log", "531 checks; 13 actual calls; test observer restores pitch2 after real stock-shaped reset1, no formal provider"),
        ("E-INLINE", "Actual Boolean inline pause leaf", "evidence/r6/checks/inline-pause.stdout.log", "227 checks; Boolean entry plus Pause leaf, MUG-shaped Pause count0, physical parent retained"),
        ("E-MACHINE", "Independent MASM caller state", "evidence/r6/checks/machine.stdout.log", "490 checks; eight nonvolatile GPR and XMM6-XMM15; RFLAGS claim limited to DF0"),
        ("E-HISTORY", "All failed and prior scopes retained", "artifact-index.json", "r1/r2 export failure, r3 old20028, r4 old26920 without executableDIR64, r5 verifier-assumption failure are separately retained"),
        ("E-R5", "R5 actual failing processes", "evidence/r5-failed/checks/test-result.json", "13 actual exit1, 26998 checks, two newly added preferred-base assertions fail in every process; no overwritten result"),
    ]
    evidence = [{"id": ident, "title": title, "observed_at": "2026-10-31", "source_type": "file",
        "source_ref": str(CASE / name), "content_hash": sha(CASE / name), "artifact_path": name,
        "repro_command": verify, "raw_excerpt": excerpt, "linked_workitem": "N2-native-prototype", "supersedes": "none"}
        for ident, title, name, excerpt in rows]
    definitions = [
        ("F-CURRENT", "Actual native callback authority", ["E-SOURCE", "E-BITS", "E-RUN"], "PlaybackSourceContext.cpp:Enter/Notify/VerifyCurrent", "Copied data does not issue a new occurrence"),
        ("F-EXIT", "Physical completion follows all callbacks", ["E-SOURCE", "E-AFTER", "E-SEH"], "PlaybackSourceContext.cpp:Exit; after-inflight", "Responsibility cannot drain on OriginalReturned callback alone"),
        ("F-JOURNAL", "Bounded armed-only responsibility and sticky withdrawal", ["E-SOURCE", "E-WITHDRAW", "E-OVERFLOW"], "PlaybackSourceContext.cpp:Enter/SetArmed/ReadReceipt", "Dormant/withdrawn calls forward without new persistent rows; old armed rows remain"),
        ("F-ABI", "Typed native arguments and specified original SEH survive", ["E-SOURCE", "E-BITS", "E-MACHINE", "E-SEH"], "Entry*; StockFixtureInvoke; PlaybackSourceCaller.asm", "Claim is limited to actual tested raw args, nonvolatile state and specified SEH codes"),
        ("F-STOCK", "Actual nested native fixture flow", ["E-SOURCE", "E-NORMAL", "E-INLINE"], "PlaybackStockFixture.cpp; PlaybackSourceFixture.cpp:Observe/Normal", "Controlled native lane produces actual schedule/reset/pause facts; restoration remains a test observer"),
        ("F-DIR64", "Executable absolute relocation has static and dynamic proof", ["E-SOURCE", "E-DIR64", "E-RELOCATION", "E-RUN"], "PlaybackStockFixture.asm:StockExecutableDir64Anchor; CheckExecutableDir64", "Provides a real executable DIR64 fixture without changing callable bodies or unwind"),
        ("F-IDENTITY", "Native artifacts and actual compiled inputs are pinned", ["E-BUILD", "E-SYMBOL", "E-SOURCE", "E-EXITS"], "build-r6; checks-r6; PE/PDB/COFF/C13", "This result verifies the ten frozen owned files, not a future changed tree"),
        ("F-BOUNDARY", "Prototype scope remains unavailable for production authority", ["E-SOURCE", "E-SYMBOL", "E-HISTORY", "E-R5"], "scope.md; owned fixture; retained historical manifests", "Eight owned wrappers do not prove real game writers, nine-member group, formal current provider or FirstInstall"),
    ]
    findings = [{"id": ident, "title": title, "severity": "n/a_re", "category": "other", "status": "validated",
        "evidence_ids": ids, "location": location, "impact": impact, "confidence": "high", "repro_steps": [verify],
        "remediation": "n/a: pure implementation/verification prototype", "optional_attack": None}
        for ident, title, ids, location, impact in definitions]
    paths = [{"id": "P-NATIVE", "title": "Actual source responsibility", "path_type": "callflow", "start": "Owned native caller",
        "goal": "Exact physical receipt after actual source completion", "steps": [
            {"action": "Pin native artifacts and commit eight actual source hooks", "evidence": ["E-BUILD", "E-SYMBOL", "E-RUN"], "finding": "F-IDENTITY"},
            {"action": "Enter typed TLS scope, advance fences, preserve raw args", "evidence": ["E-SOURCE", "E-BITS"], "finding": "F-CURRENT"},
            {"action": "Forward original and actual nested leaves or original SEH", "evidence": ["E-NORMAL", "E-INLINE", "E-SEH"], "finding": "F-STOCK"},
            {"action": "End observer/TLS authority in native finally, then publish/release exact receipt", "evidence": ["E-AFTER", "E-WITHDRAW", "E-SOURCE"], "finding": "F-EXIT"}],
        "residual_risks": "No real GameAssembly stock graph, lower engine writer, IL2CPP roots/run, formal current provider or production FirstInstall authority"},
        {"id": "P-DIR64", "title": "Executable relocation provenance", "path_type": "callflow", "start": "ASM DQ OFFSET StockSetPitch",
        "goal": "Actual nonpreferred MEM_IMAGE .text anchor matches actual target", "steps": [
            {"action": "Assemble ADDR64 symbol relocation in executable .text$mn", "evidence": ["E-SOURCE", "E-DIR64"], "finding": "F-DIR64"},
            {"action": "Link preferred absolute value and exact PE DIR64 entry outside function ranges", "evidence": ["E-DIR64", "E-SYMBOL"], "finding": "F-DIR64"},
            {"action": "Read preferred from frozen disk header, compare actual RX anchor after nonpreferred load", "evidence": ["E-RELOCATION", "E-RUN"], "finding": "F-DIR64"}],
        "residual_risks": "Group full-file/relocation validation is a separate package; this path supplies the real fixture only"}]
    write(CASE / "evidence-index.json", {"flavor": None, "revision": "r6", "scope": "scope.md", "timeline": "timeline.md",
        "evidence": evidence, "findings": findings, "paths": paths})
    files = [path for path in CASE.iterdir() if path.is_file() and path.name != "native-report-integrity.json"]
    write(CASE / "native-report-integrity.json", {"kind": "native-report-fixity", "revision": "r6", "selfHashExcluded": True,
        "files": [{"path": str(path), "bytes": path.stat().st_size, "sha256": sha(path)} for path in sorted(files)],
        "artifactIndexSha256": sha(CASE / "artifact-index.json"), "gameCodeExecuted": False, "productionAdmission": False})
    print(json.dumps({"passed": True, "evidence": len(evidence), "findings": len(findings), "paths": len(paths),
        "reportFilesSealed": len(files), "integritySha256": sha(CASE / "native-report-integrity.json")}))


if __name__ == "__main__":
    main()
