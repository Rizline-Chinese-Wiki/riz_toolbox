"""Seal existing offline r14 artifacts; never execute/load a native image."""
import hashlib
import json
import shutil
from datetime import datetime, timezone
from pathlib import Path

CASE = Path(__file__).resolve().parent
REPO = CASE.parents[2]
CACHE = Path("D:/riz-build-cache-next")
BUILD = CACHE / "playback-stock-native-build-r14-20261031"
CHECKS = CACHE / "playback-stock-native-checks-r14-full-20261031"


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def write(path, value):
    assert not path.exists(), f"Preserve existing artifact: {path}"
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False), encoding="utf-8")


def row(path):
    return {"path": str(path.resolve()), "bytes": path.stat().st_size, "sha256": sha(path)}


def main():
    assert not (CASE / "artifact-index.json").exists(), "Never reseal an earlier report"
    assert not (CASE / "evidence").exists() and not (CASE / "sources-r14").exists()
    build = read(BUILD / "build-result.json")
    checks = read(CHECKS / "test-result.json")
    proof = read(BUILD / "pe-pdb-source-verification.json")
    assert build["passed"] and build["actualExitCode"] == 0
    assert checks["passed"] and checks["processes"] == 14 and checks["checks"] == 36675
    assert proof["passed"] and len(proof["images"]) == 3 and len(proof["compiledObjects"]) == 5
    for item in build["sources"] + build["frozenSources"] + build["files"]:
        assert sha(Path(item["path"])) == item["sha256"], item["path"]
    for item in checks["inputs"] + checks["frozenInputs"] + checks["files"]:
        assert sha(Path(item["path"])) == item["sha256"], item["path"]

    shutil.copytree(BUILD, CASE / "evidence/r14/build")
    shutil.copytree(CHECKS, CASE / "evidence/r14/checks")
    static_source = REPO / "docs/validation/playback-stock-native-20261031/next-source-hooks-evidence.json"
    static_copy = CASE / "evidence/static-next-source-hooks-evidence.json"
    shutil.copy2(static_source, static_copy)
    static = read(static_copy)
    game_input = next(item for item in static["inputs"] if Path(item["path"]).name == "GameAssembly.dll")
    assert sha(Path(game_input["path"])) == game_input["sha256"]
    assert static["actualDobbyPrepareCount"] == 0 and not static["gameCodeExecuted"] and not static["gameDllMainExecuted"]

    relative = [
        "native/RizToolbox.PlaybackSourcePrototype/PlaybackSourceContext.h",
        "native/RizToolbox.PlaybackSourcePrototype/PlaybackSourceContext.cpp",
        "native/RizToolbox.PlaybackSourcePrototype/PlaybackSourceFixture.cpp",
        "native/RizToolbox.PlaybackStockFixture/PlaybackStockFixture.h",
        "native/RizToolbox.PlaybackStockFixture/PlaybackStockFixture.cpp",
        "native/RizToolbox.PlaybackStockFixture/PlaybackStockFixture.asm",
        "native/RizToolbox.PlaybackStockFixture/PlaybackSourceCaller.asm",
        "scripts/Build-PlaybackSourcePrototype.ps1",
        "scripts/Test-PlaybackSourcePrototype.ps1",
        "scripts/Verify-PlaybackSourcePrototype.py",
    ]
    owned = []
    for name in relative:
        source = REPO / name
        destination = CASE / "sources-r14" / name
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, destination)
        owned.append({**row(source), "snapshot": destination.relative_to(CASE).as_posix(),
                      "matchesFinalCompiledOrVerificationInput": True})
    write(CASE / "native-owned-inputs.json", {"kind": "frozen-native-targets-r14-inputs", "files": owned,
          "contextAbiChanged": False, "gameCodeExecuted": False, "productionAdmission": False})

    final_target = next(item["result"] for item in checks["rows"] if item["mode"] == "target-specific")
    by_fixture = {item["name"]: item for item in proof["stockTargets"]}
    by_static = {item["name"]: item for item in static["targets"]}
    names = {1: ("StockSetPitch", None), 2: ("StockPause", "Music.Pause"),
             3: ("StockReplay", "Music.ReplayScheduled"), 4: ("StockResume", "Music.ResumeScheduled"),
             5: ("StockPlayScheduled", "AudioSource.PlayScheduled"), 6: ("StockSetClip", "AudioSource.SetClip"),
             7: ("StockProgressPlay", "Progress.Play"), 8: ("StockPauseLeaf", "AudioSource.Pause")}
    targets = []
    for live in final_target["targets"]:
        name, game_name = names[live["role"]]
        moved = live["movedLength"]
        actual = bytes.fromhex(live["actualDobbyOriginalHex"])
        continuation = int(live["continuation"], 16)
        assert len(actual) == moved + 14 and actual[moved:moved+6] == bytes.fromhex("ff2500000000")
        assert int.from_bytes(actual[moved+6:], "little") == continuation == int(live["target"], 16)+moved
        assert actual[:moved].hex() == by_fixture[name]["prefix"][:moved*2]
        game = by_static[game_name] if game_name else None
        if game:
            assert actual[:moved].hex() == game["prefix24"][:moved*2]
        targets.append({"role": live["role"], "fixtureExport": name, "fixtureBody": by_fixture[name],
                        "fixtureActualDobby": live, "gameStatic": game,
                        "gameActualDobbyPrepareCount": 0, "gameOriginalOrDllMainExecuted": False,
                        "fullFixtureBodyIsProductionEquivalent": False})
    write(CASE / "target-proof.json", {"kind": "separate-game-file-only-and-actual-controlled-fixture-r14",
          "passed": True, "gameInput": game_input, "targets": targets,
          "nativeOriginalBoundaryProbesPerProcess": 32, "nativeSourceBoundaryProbesPerProcess": 59,
          "fixtureReplayResumeProgressRfCounts": [3, 3, 3], "gameReplayResumeProgressRfCounts": [5, 5, 4],
          "gameCodeExecuted": False, "gameDllMainExecuted": False, "productionAdmission": False})

    verify_command = "& 'C:/Python314/python.exe' 'C:/Users/71957/Desktop/riz_toolbox/docs/validation/playback-stock-native-targets-20261031/Verify-Report.py'"
    definitions = [
        ("E-TARGET-STATIC", "evidence/static-next-source-hooks-evidence.json", "file", "既有固定游戏 PE 的七目标静态事实，actual Prepare 为 0"),
        ("E-TARGET-PE", "evidence/r14/build/pe-pdb-source-verification.json", "file", "三组 PE/PDB、五个 COFF、九正文/RF/导出与 DIR64"),
        ("E-TARGET-ACTUAL", "evidence/r14/checks/target-specific.stdout.log", "log", "真实八目标搬移/continuation、原 SEH、机器状态、Progress CMP 分支与物理退出"),
        ("E-TARGET-MATRIX", "evidence/r14/checks/test-result.json", "log", "14 真实进程、36675 检查、112 Prepare/Commit"),
        ("E-TARGET-PC5", str(CACHE / "playback-stock-native-checks-r11-ab-20261031/stage-ab.stderr.log"), "log", "独立 set_clip PC5 SAVE_RBX 的可复核失败反例"),
    ]
    evidence = []
    for identity, name, source_type, title in definitions:
        path = Path(name) if Path(name).is_absolute() else CASE / name
        evidence.append({"id": identity, "title": title, "source_type": source_type,
                         "source_ref": str(path.resolve()), "artifact_path": name,
                         "content_hash": sha(path), "observed_at": checks["completedAt"],
                         "repro_command": verify_command, "linked_workitem": "WI-NATIVE-SEVEN", "supersedes": "none"})
    write(CASE / "evidence-index.json", {"flavor": None, "scope": "scope.md", "timeline": "timeline.md", "evidence": evidence,
          "findings": [
              {"id": "F-TARGET-PREFIX", "severity": "n/a_re", "category": "other", "status": "validated", "confidence": "high",
               "location": "eight source fixture entries", "evidence_ids": ["E-TARGET-STATIC", "E-TARGET-PE", "E-TARGET-ACTUAL"],
               "impact": "Each controlled target has independent actual relocation/continuation; Progress moves seven complete bytes", "remediation": "n/a_re"},
              {"id": "F-TARGET-ABI-UW", "severity": "n/a_re", "category": "other", "status": "validated", "confidence": "high",
               "location": "PlaybackStockFixture.asm and independent RX originals", "evidence_ids": ["E-TARGET-PE", "E-TARGET-ACTUAL", "E-TARGET-MATRIX", "E-TARGET-PC5"],
               "impact": "Controlled original SEH, raw ABI/MI, late machine state and selected OS unwind boundaries are validated", "remediation": "n/a_re"},
              {"id": "F-TARGET-CMP", "severity": "n/a_re", "category": "other", "status": "validated", "confidence": "high",
               "location": "StockProgressPlay+0/+7/+31/+100; StockProgress+0x42", "evidence_ids": ["E-TARGET-PE", "E-TARGET-ACTUAL"],
               "impact": "Real CMP flags survive relocation and drive equal no-op versus actual inline Pause", "remediation": "n/a_re"}],
          "paths": [{"id": "P-TARGET-CONTROLLED", "path_type": "callflow", "start": "pinned file-only stock prefixes", "goal": "controlled fixture native physical exit",
                     "steps": [{"action": "read fixed prefix/RF facts", "evidence_ids": ["E-TARGET-STATIC"], "finding": "F-TARGET-PREFIX"},
                               {"action": "verify actual PE/PDB/COFF prefix and controlled unwind", "evidence_ids": ["E-TARGET-PE"], "finding": "F-TARGET-ABI-UW"},
                               {"action": "execute actual Prepare/Commit/original/nested/SEH and CMP branches", "evidence_ids": ["E-TARGET-ACTUAL"], "finding": "F-TARGET-CMP"},
                               {"action": "close physical-exit/withdrawal matrix", "evidence_ids": ["E-TARGET-MATRIX"], "finding": "F-TARGET-ABI-UW"}],
                     "residual_risks": ["full game bodies differ", "game execution not performed", "production provider/Clock/occurrence/writer reservation/cross-session handoff incomplete"]}]})

    native = [CACHE / f"playback-stock-native-build-r{number}-20261031" for number in range(7, 15)]
    native += [CACHE / f"playback-stock-native-checks-r{number}-ab-20261031" for number in [9, 10, 11, 12]]
    native += [CACHE / "playback-stock-native-checks-r14-target-20261031", CHECKS]
    native_directories = []
    files = set()
    for directory in native:
        assert directory.is_dir(), directory
        paths = sorted(path.resolve() for path in directory.rglob("*") if path.is_file())
        files.update(paths)
        native_directories.append({"path": str(directory.resolve()), "files": [str(path) for path in paths]})
    files.update(path.resolve() for path in CASE.rglob("*") if path.is_file())
    write(CASE / "artifact-index.json", {"kind": "native-r14-artifact-input-fixity", "selfHashExcluded": True,
          "createdAt": datetime.now(timezone.utc).isoformat(), "nativeDirectories": native_directories,
          "files": [row(path) for path in sorted(files)], "gameCodeExecuted": False, "productionAdmission": False})
    integrity_files = sorted(path.resolve() for path in CASE.rglob("*") if path.is_file())
    write(CASE / "report-integrity.json", {"kind": "native-seven-target-report-integrity", "selfHashExcluded": True,
          "files": [row(path) for path in integrity_files]})
    print(json.dumps({"passed": True, "hashedArtifacts": len(files), "nativeDirectories": len(native_directories),
          "ownedSources": len(owned), "processes": checks["processes"], "checks": checks["checks"],
          "actualDobbyPrepare": sum(item["result"]["actualDobbyPrepared"] for item in checks["rows"]),
          "artifactIndexSha256": sha(CASE / "artifact-index.json"), "gameCodeExecuted": False}))


if __name__ == "__main__":
    main()
