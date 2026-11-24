"""Bounded file-only S2 handoff extraction and append-only r2 report sealing."""
import hashlib
import json
import sys
from pathlib import Path

import pefile

CASE = Path(__file__).resolve().parent
REPO = CASE.parents[2]
ROOT = Path("D:/riz-build-cache-next/playback-stock-metadata-r1-20261031")
OLD = Path("D:/riz-build-cache-next/gameplay-native-values-static-r1-20261030/evidence-r3")
IMAGE = ROOT / "GameAssembly.dll"
EXPECTED = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79"
TARGETS = [("Music.Pause", 6), ("Music.ReplayScheduled", 6), ("Music.ResumeScheduled", 6),
    ("Progress.Play", 7), ("AudioSource.Pause", 6), ("AudioSource.PlayScheduled", 6), ("AudioSource.SetClip", 6)]


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def row(path):
    return {"path": str(path.resolve()), "bytes": path.stat().st_size, "sha256": sha(path)}


def write(path, value):
    assert not path.exists(), path
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")


def main():
    assert sha(IMAGE) == EXPECTED
    pe = pefile.PE(str(IMAGE))
    bodies = json.loads((ROOT / "evidence/bodies.json").read_text())
    targets = []
    for name, candidate_length in TARGETS:
        body = bodies[name]
        start, end = int(body["rva"], 16), int(body["rva"], 16) + body["bytes"]
        assert hashlib.sha256(pe.get_data(start, body["bytes"])).hexdigest() == body["sha256"]
        functions = []
        for entry in pe.DIRECTORY_ENTRY_EXCEPTION:
            rf = entry.struct
            if not start <= rf.BeginAddress < end:
                continue
            head = pe.get_data(rf.UnwindData, 4)
            flags = head[0] >> 3
            size = (4 + 2 * head[2] + 3) & ~3
            size += 12 if flags & 4 else (4 if flags & 3 else 0)
            functions.append({"begin": hex(rf.BeginAddress), "end": hex(rf.EndAddress), "unwindRva": hex(rf.UnwindData),
                "unwindHex": pe.get_data(rf.UnwindData, size).hex(), "version": head[0] & 7, "flags": flags,
                "prologSize": head[1], "codeSlots": head[2]})
        targets.append({"name": name, "rva": body["rva"], "length": body["bytes"], "bodySha256": body["sha256"],
            "prefix24": pe.get_data(start, 24).hex(), "candidateCopiedLength": candidate_length,
            "candidateIsActualDobbyObservation": False, "actualDobbyPreparePerformed": False, "runtimeFunctions": functions})
    source_paths = [IMAGE, ROOT / "evidence/bodies.json", ROOT / "manifest.json", ROOT / "static-result.json",
        OLD / "functions.json", OLD / "Music.Pause.asm", OLD / "Music.ReplayScheduled.asm",
        OLD / "Music.ResumeScheduled.asm", OLD / "Progress.Play.asm", OLD / "AudioSource.PlayScheduled.asm",
        ROOT / "evidence/AudioSource.SetClip.asm", ROOT / "evidence/metadata-snippets.json",
        REPO / "src/RizToolbox.Runtime/Hooks/NativePlaybackSetPitchTrampolineUnwind.cs",
        REPO / "src/RizToolbox.Runtime/Hooks/NativePlaybackTrampolineUnwind.cs",
        REPO / "src/RizToolbox.Runtime/Hooks/ExactHookBroker.Playback.Group.cs",
        REPO / "src/RizToolbox.Runtime/Hooks/PlaybackSourceGroupTargets.cs",
        REPO / "src/RizToolbox.Runtime/NativeRuntime.PlaybackHooks.cs",
        REPO / "src/RizToolbox.Adapter/Gameplay/NativePlaybackStockCodeProfile.cs",
        Path(__file__).resolve(), Path(sys.executable), Path(pefile.__file__)]
    source_rows = [row(path) for path in source_paths]
    write(CASE / "next-source-hooks-evidence.json", {"kind": "bounded-static-next-source-hooks", "flavor": None,
        "inputs": source_rows, "targets": targets, "targetCount": len(targets), "gameCodeExecuted": False,
        "gameDllMainExecuted": False, "actualDobbyPrepareCount": 0, "newDisassemblyPerformed": False,
        "nativeSourceModified": False, "productionAdmission": False})
    added = [CASE / name for name in ["NEXT-SOURCE-HOOKS.md", "next-source-hooks-evidence.json",
                                      "Seal-Next-Source-Hooks.py", "Verify-Report-r2.py"]]
    write(CASE / "artifact-index-r2.json", {"kind": "append-only-native-artifact-fixity", "revision": "r2-S2-handoff",
        "previousArtifactIndex": row(CASE / "artifact-index.json"), "previousReportSeal": row(CASE / "native-report-integrity.json"),
        "addedFiles": [row(path) for path in added], "staticInputs": source_rows, "selfHashExcluded": True})
    write(CASE / "native-report-integrity-r2.json", {"kind": "append-only-native-report-fixity", "revision": "r2-S2-handoff",
        "selfHashExcluded": True, "previousReportSeal": row(CASE / "native-report-integrity.json"),
        "files": [row(path) for path in added + [CASE / "artifact-index-r2.json"]],
        "gameCodeExecuted": False, "nativeTestsRerun": False, "productionAdmission": False})
    print(json.dumps({"passed": True, "staticTargets": 7, "runtimeFunctions": sum(len(item["runtimeFunctions"]) for item in targets),
        "actualDobbyPrepareCount": 0, "nativeTestsRerun": False, "previousSealPreserved": True,
        "artifactIndexR2Sha256": sha(CASE / "artifact-index-r2.json"), "integrityR2Sha256": sha(CASE / "native-report-integrity-r2.json")}))


if __name__ == "__main__":
    main()
