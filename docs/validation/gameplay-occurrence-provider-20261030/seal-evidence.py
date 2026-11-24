"""Read-only verification of the frozen run; write its report-side index and small copies."""
import argparse
import datetime as dt
import hashlib
import json
from pathlib import Path
import shutil
import pefile


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--run", type=Path, required=True)
    args = parser.parse_args()
    run = args.run.resolve()
    case = Path(__file__).resolve().parent
    result = json.loads((run / "test-result.json").read_text(encoding="utf-8"))
    if not result["passed"] or result["checks"] != {"passed": 175, "total": 175}:
        raise RuntimeError("The selected run is not the final 175/175 execution.")
    before = json.loads((run / "source-before.json").read_text(encoding="utf-8"))
    after = json.loads((run / "source-after.json").read_text(encoding="utf-8"))
    if before != after:
        raise RuntimeError("Frozen source manifests differ.")
    for row in before:
        if sha(run / "source-snapshot" / row["path"]) != row["sha256"]:
            raise RuntimeError("Current frozen source differs: " + row["path"])
    dll = run / "native/RizToolbox.PlaybackOwnedFixture.dll"
    if sha(dll) != result["nativeDllSha256"]:
        raise RuntimeError("The executed owned native DLL changed.")
    pe = pefile.PE(str(dll))
    exports = [dict(name=row.name.decode("ascii"), ordinal=row.ordinal, rva=row.address)
               for row in pe.DIRECTORY_ENTRY_EXPORT.symbols]
    expected = {"RizPlaybackOwned_Create", "RizPlaybackOwned_Destroy", "RizPlaybackOwned_Read",
                "RizPlaybackOwned_Run", "RizPlaybackOwned_SetClip", "RizPlaybackOwned_WritePitch"}
    if pe.FILE_HEADER.Machine != 0x8664 or pe.OPTIONAL_HEADER.Magic != 0x20b or {row["name"] for row in exports} != expected:
        raise RuntimeError("The actual owned fixture PE identity/exports differ.")
    imports = [dict(module=module.dll.decode("ascii"), symbols=[symbol.name.decode("ascii") if symbol.name else f"ordinal:{symbol.ordinal}"
               for symbol in module.imports]) for module in pe.DIRECTORY_ENTRY_IMPORT]
    evidence = case / "evidence"
    evidence.mkdir(exist_ok=True)
    summary = dict(kind="actual-owned-fixture-pe", observedAt=dt.datetime.now(dt.timezone.utc).isoformat(),
                   path=str(dll), sha256=sha(dll), bytes=dll.stat().st_size,
                   machine=hex(pe.FILE_HEADER.Machine), optionalMagic=hex(pe.OPTIONAL_HEADER.Magic),
                   exports=exports, imports=imports, gameExecuted=False, productionAdmission=False)
    (evidence / "pe-summary.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    small = ["test-result.json", "tests.stdout.log", "tests.stderr.log", "managed-build.stdout.log", "managed-build.stderr.log",
             "source-before.json", "source-after.json", "live-owned-before.json", "live-owned-after.json", "snapshot-registration.json"]
    for name in small:
        shutil.copyfile(run / name, evidence / name)
    files = [run / name for name in small] + [run / "source-snapshot.zip", run / "NuGet.Config", run / "native/build-result.json"]
    files.extend(path for path in (run / "native").iterdir() if path.is_file() and path.name != "build-result.json")
    files.append(evidence / "pe-summary.json")
    static = Path("D:/riz-build-cache-next/gameplay-native-values-static-r1-20261030/evidence-r3")
    files.extend(static / name for name in ["manifest.json", "metadata.json", "Music.Pause.asm", "Music.ReplayScheduled.asm",
                 "Music.ResumeScheduled.asm", "Dsp.GetDspTime.asm", "AudioSource.PlayScheduled.asm", "AudioSource.SetPitch.asm", "Progress.Update.asm"])
    index = dict(kind="offline-controlled-playback-owned-evidence", flavor=None,
                 observedAt=dt.datetime.now(dt.timezone.utc).isoformat(), run=str(run), sourceInputs=len(before),
                 currentFrozenInputsVerified=True, checks=result["checks"], stages=result["stages"],
                 productionAdmission=False, productionReturnAuthority=False, realStockHookCoverage=False,
                 nakedRuntimeWriterCoverage=False, gameExecuted=False, installed=False,
                 artifacts=[dict(source_ref=str(path), bytes=path.stat().st_size, content_hash=sha(path)) for path in files])
    (case / "evidence-index.json").write_text(json.dumps(index, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(dict(run=str(run), sourceInputs=len(before), checks=result["checks"], exports=len(exports),
                         nativeSha256=sha(dll), indexSha256=sha(case / "evidence-index.json")), indent=2))


if __name__ == "__main__":
    main()
