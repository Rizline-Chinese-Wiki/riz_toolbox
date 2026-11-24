"""Offline fixity, actual exit, PE/PDB/COFF and source-archive review.

Does not load any reviewed executable or DLL. Every output directory must be new.
Historical runs are preserved and indexed, never promoted to current-source proof.
"""
import argparse
from collections import Counter
from datetime import datetime, timezone
import hashlib
import importlib.util
import json
from pathlib import Path
import struct
import sys
import zipfile


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def record(path):
    path = path.resolve()
    return {"path": str(path), "bytes": path.stat().st_size, "sha256": digest(path)}


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def check_rows(rows):
    for row in rows:
        assert digest(Path(row["path"])) == row["sha256"], row["path"]


def pdb_streams(path):
    data = path.read_bytes()
    assert data[:32] == b"Microsoft C/C++ MSF 7.00\r\n\x1aDS\0\0\0"
    block_size, _, _, size, _, block_map = struct.unpack_from("<6I", data, 32)
    count = (size + block_size - 1) // block_size
    blocks = struct.unpack_from(f"<{count}I", data, block_map * block_size)
    directory = b"".join(data[b * block_size:(b + 1) * block_size] for b in blocks)[:size]
    count = struct.unpack_from("<I", directory)[0]
    sizes = struct.unpack_from(f"<{count}I", directory, 4)
    cursor, streams = 4 + count * 4, []
    for size in sizes:
        count = 0 if size == 0xffffffff else (size + block_size - 1) // block_size
        blocks = struct.unpack_from(f"<{count}I", directory, cursor)
        cursor += count * 4
        streams.append(b"".join(data[b * block_size:(b + 1) * block_size] for b in blocks)[:size])
    return streams


def linked_pdb_objects(pdb, objects):
    """Match linked PDB DBI module C13 checksums to the actual COFF checksums.

    Name offsets change during linking, so compare the full multiset of checksum
    algorithm/digest pairs for each exact object path, not the name-table offsets.
    """
    streams = pdb_streams(pdb)
    dbi = streams[3]
    module_size = struct.unpack_from("<I", dbi, 24)[0]
    cursor, found = 64, {}
    wanted = {str(Path(row["object"]).resolve()).casefold(): row for row in objects}
    while cursor < 64 + module_size:
        stream, symbols, c11, c13 = struct.unpack_from("<HIII", dbi, cursor + 34)
        end = dbi.index(b"\0", cursor + 64)
        module = dbi[cursor + 64:end].decode("utf-8")
        end2 = dbi.index(b"\0", end + 1)
        object_name = dbi[end + 1:end2].decode("utf-8")
        key = str(Path(object_name).resolve()).casefold() if object_name else ""
        if key in wanted:
            assert key not in found
            compiled = wanted[key]
            expected = Counter((row["algorithm"], row["compiledChecksum"]) for row in compiled["sources"])
            actual, at = Counter(), symbols + c11
            limit = at + c13
            while at + 8 <= limit:
                kind, length = struct.unpack_from("<II", streams[stream], at)
                payload = streams[stream][at + 8:at + 8 + length]
                assert at + 8 + length <= limit
                if kind == 0xf4:
                    offset = 0
                    while offset + 6 <= len(payload):
                        _, size, algorithm = struct.unpack_from("<IBB", payload, offset)
                        value = payload[offset + 6:offset + 6 + size]
                        assert len(value) == size
                        actual[({1: "md5", 2: "sha1", 3: "sha256"}[algorithm], value.hex())] += 1
                        offset = (offset + 6 + size + 3) & ~3
                at = (at + 8 + length + 3) & ~3
            assert actual == expected, (pdb, object_name)
            found[key] = {"object": object_name, "module": module, "moduleStream": stream,
                          "c13Bytes": c13, "sourceChecksumCount": sum(actual.values()),
                          "pdbC13MatchesActualCoff": True, "objectSha256": compiled["sha256"]}
        cursor = (end2 + 1 + 3) & ~3
    assert set(found) == set(wanted), (pdb, set(wanted) - set(found))
    return {"pdb": str(pdb), "pdbSha256": digest(pdb), "objects": list(found.values())}


def actual_output(directory, result):
    lines = [line for line in (directory / "stdout.log").read_text(encoding="utf-8-sig").splitlines()
             if line.startswith("{")]
    assert len(lines) == 1 and json.loads(lines[0]) == result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    assert args.output.is_absolute() and not args.output.exists(), "Use a fresh absolute output directory"
    args.output.mkdir(parents=True)
    root = Path(__file__).resolve().parents[3]
    cache = Path("D:/riz-build-cache-next")
    native = cache / "foundation-private-native-r3-20261031"
    mapped = cache / "foundation-private-mapped-r4-20261031"
    machine = cache / "foundation-private-machine-r4-20261031"
    relocated = cache / "foundation-private-relocated-seh-r1-20261031"
    core = cache / "foundation-nine-source-core-r3-20261031"
    parser_path = root / "scripts/Verify-PlaybackSourcePrototype.py"
    dependencies = [record(Path(__file__)), record(parser_path), record(Path(sys.executable))]
    spec = importlib.util.spec_from_file_location("review_native_base", parser_path)
    base = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(base)
    dependencies.append(record(Path(base.pefile.__file__)))

    manifest = read(native / "native-manifest.json")
    assert manifest["passed"] and manifest["buildExitCode"] == 0 and manifest["inputHashesUnchanged"]
    assert manifest["publicationVersion"] == 7 and manifest["operationCount"] == 26 and manifest["expectedWrites"] == 64
    assert not manifest["gameExecuted"] and not manifest["installed"] and not manifest["fixtureExecuted"]
    check_rows(manifest["sources"] + manifest["files"] + [manifest["sourceArchive"], manifest["actualExportValidation"]])
    with zipfile.ZipFile(manifest["sourceArchive"]["path"]) as archive:
        assert len(archive.namelist()) == len(manifest["sources"]) == 101
        for row in manifest["sources"]:
            assert hashlib.sha256(archive.read(row["relativePath"])).hexdigest() == row["sha256"]

    export_image = base.pefile.PE(str(native / "RizToolbox.JudgeContext.dll"))
    eat = export_image.DIRECTORY_ENTRY_EXPORT
    assert eat.struct.NumberOfFunctions == eat.struct.NumberOfNames == len(eat.symbols) == 85
    assert {row.name.decode("ascii") for row in eat.symbols} == set(manifest["expectedExports"])
    for row in eat.symbols:
        section = export_image.get_section_by_rva(row.address)
        assert not row.forwarder and section and section.Characteristics & 0x20000000
        assert not section.Characteristics & 0x80000000
    assert not any(b"Fixture" in row.name for row in eat.symbols)
    export_image.close()

    mapped_result = read(mapped / "test-result.json")
    assert mapped_result["passed"] and mapped_result["buildExitCode"] == 0
    assert (mapped_result["headerBytes"], mapped_result["rangeBytes"], mapped_result["originalCount"], mapped_result["writeCount"]) == (120, 184, 28, 64)
    assert not any(mapped_result[key] for key in ("gameCodeExecuted", "originalInitInvoked", "dllMainInvoked", "installed", "productionFirstWriteAdmission"))
    check_rows(mapped_result["sources"] + mapped_result["inputs"])
    assert [(row["mode"], row["actualExitCode"], row["result"]["checks"]) for row in mapped_result["modes"]] == [
        ("normal", 0, 383), ("prewrite-foreign", 0, 12), ("patch", 0, 394), ("original", 0, 394),
        ("unwind", 0, 394), ("outside", 0, 394), ("continuation", 0, 394)]
    for row in mapped_result["modes"]:
        lines = [line for line in (mapped / (row["mode"] + ".stdout.log")).read_text(encoding="utf-8-sig").splitlines() if line.startswith("{")]
        assert len(lines) == 1 and json.loads(lines[0]) == row["result"]
        assert row["result"]["passed"] and row["result"]["failures"] == 0
        assert not any(row["result"][key] for key in ("gameCodeExecuted", "originalInitInvoked", "dllMainInvoked"))

    owned_results = []
    for directory, checks in [(machine, 2226), (relocated, 17)]:
        result = read(directory / "test-result.json")
        assert result["passed"] and result["buildExitCode"] == result["actualExitCode"] == 0
        assert result["actualHelperObjectsLinked"] and len(result["objects"]) == 21
        assert result["compileTimeFixtureMacro"] == "RIZ_FIRST_INSTALL_ATTESTATION_FIXTURE"
        assert not any(result[key] for key in ("productionOwnerIssued", "gameCodeExecuted", "gameImageLoaded", "installed"))
        assert result["result"]["passed"] and result["result"]["checks"] == checks and result["result"]["failures"] == 0
        check_rows(result["sources"] + result["objects"] + [result["dobby"]])
        actual_output(directory, result["result"])
        owned_results.append(result)
    assert {key: owned_results[0]["result"][key] for key in ("actualPrepareCount", "actualCommitCount", "actualMachineUnwindContexts", "actualOriginalSeh", "originalBodyCalls", "entryCalls")} == {
        "actualPrepareCount": 28, "actualCommitCount": 28, "actualMachineUnwindContexts": 127,
        "actualOriginalSeh": 28, "originalBodyCalls": 112, "entryCalls": 84}
    assert owned_results[1]["result"]["actualFaultsInsideNormalized"] == 1 and owned_results[1]["result"]["faultOffset"] == 4

    all_objects = [base.compiled_sources(path) for directory in [native, mapped, machine, relocated] for path in sorted(directory.glob("*.obj"))]
    pairs = [(native, "RizToolbox.JudgeContext.dll", "RizToolbox.JudgeContext.pdb"),
             (native, "JudgeContext.Fixture.exe", "JudgeContext.Fixture.pdb"),
             (mapped, "FirstInstallPlaybackAttestation.Fixture.exe", "fixture.pdb"),
             (machine, "FirstInstallPlaybackMachine.Fixture.exe", "fixture.pdb"),
             (relocated, "FirstInstallPlaybackRelocatedFault.Fixture.exe", "fixture.pdb")]
    images, bindings = [], []
    helper_objects = [row for row in all_objects if Path(row["object"]).parent == native and Path(row["object"]).stem not in ("Fixture", "FixtureCaller")]
    for directory, image, pdb in pairs:
        images.append(base.image_identity(directory / image, directory / pdb))
        objects = [row for row in all_objects if Path(row["object"]).parent == directory]
        if image == "RizToolbox.JudgeContext.dll":
            objects = helper_objects
        elif image == "JudgeContext.Fixture.exe":
            objects = [row for row in objects if Path(row["object"]).stem in ("Fixture", "FixtureCaller")]
        elif directory in (machine, relocated):
            objects += [row for row in helper_objects if Path(row["object"]).name != "FirstInstall.obj"]
        bindings.append(linked_pdb_objects(directory / pdb, objects))

    managed_paths = sorted((root / "src/RizToolbox.Runtime/Hooks").glob("NativeFirstInstall*.cs")) + [
        root / "src/RizToolbox.Runtime/Hooks/ExactHookBroker.Playback.Group.cs",
        root / "src/RizToolbox.Runtime/Hooks/NativePlaybackExecutableSeal.cs",
        root / "src/RizToolbox.Runtime/Hooks/NativePlaybackExecutableSeal.File.cs"]
    frozen = {str(Path(row["path"]).resolve()).casefold(): row for row in read(core / "worktree-source-before.json")}
    core_matches = []
    for path in managed_paths:
        row = frozen[str(path.resolve()).casefold()]
        assert digest(path) == row["sha256"]
        snapshot = core / "source-snapshot" / path.relative_to(root)
        assert digest(snapshot) == row["sha256"]
        core_matches.append({**record(path), "coreSnapshot": str(snapshot), "coreSnapshotMatches": True})

    review_sources = {Path(row["path"]).resolve() for row in manifest["sources"]}
    for result in [mapped_result] + owned_results:
        review_sources.update(Path(row["path"]).resolve() for row in result["sources"])
    review_sources.update(managed_paths)
    review_sources.add(Path(__file__).resolve())
    source_rows = [record(path) for path in sorted(review_sources)]
    archive_path = args.output / "reviewed-source-snapshot.zip"
    with zipfile.ZipFile(archive_path, "w", zipfile.ZIP_DEFLATED) as archive:
        for row in source_rows:
            path = Path(row["path"])
            archive.write(path, path.relative_to(root).as_posix())
    with zipfile.ZipFile(archive_path) as archive:
        for row in source_rows:
            assert hashlib.sha256(archive.read(Path(row["path"]).relative_to(root).as_posix())).hexdigest() == row["sha256"]
    check_rows(source_rows + dependencies)

    failures = read(cache / "foundation-private-machine-r1-20261031/test-result.json")
    assert not failures["passed"] and failures["actualExitCode"] == 1 and failures["result"]["failures"] == 29
    assert failures["result"]["actualMachineUnwindContexts"] == 28
    assert "C3536" in (cache / "foundation-private-mapped-r1-20261031/build.log").read_text(encoding="utf-8-sig")
    historical = []
    current_names = {directory.name for directory in (native, mapped, machine, relocated)}
    for directory in sorted(cache.glob("foundation-private-*-20261031")):
        if not directory.is_dir() or directory == args.output:
            continue
        row = {"directory": str(directory), "role": "current-validated-input" if directory.name in current_names else "historical-preserved-not-current-proof", "files": []}
        for path in sorted(directory.rglob("*")):
            if path.is_file():
                row["files"].append(record(path))
        historical.append(row)
    result = {"schemaVersion": 1, "kind": "private-attestation-offline-evidence-review", "passed": True,
              "observedAtUtc": datetime.now(timezone.utc).isoformat(), "actualProcessExecutionByThisReview": False,
              "gameCodeExecuted": False, "gameDllMainInvoked": False, "originalIl2CppInitInvoked": False,
              "installed": False, "productionAdmission": False, "candidatePackageVerified": False,
              "nativeManifest": record(native / "native-manifest.json"), "nativeSourceCount": len(manifest["sources"]),
              "nativeArchive": record(Path(manifest["sourceArchive"]["path"])), "nativeArchiveSourcesMatch": True,
              "actualExportCount": 85, "allExportsLocalExecutableNonWritable": True,
              "mappedResult": record(mapped / "test-result.json"), "mappedProcesses": 7, "mappedChecks": 2365,
              "machineResult": record(machine / "test-result.json"), "machineChecks": 2226,
              "relocatedResult": record(relocated / "test-result.json"), "relocatedChecks": 17,
              "actualExitCodesVerifiedFromRecordedResults": True, "stdoutMatchesRecordedResult": True,
              "nativeImages": images, "compiledObjects": all_objects, "linkedPdbObjects": bindings,
              "imageSurveys": [base.image_survey(directory / image) for directory, image, _ in pairs],
              "coreR3SourceMatches": core_matches, "coreR3ExecutionResultClaimed": False,
              "reviewSources": source_rows, "reviewSourceArchive": record(archive_path),
              "directories": historical, "verificationInputs": dependencies,
              "pythonVersion": sys.version, "pefileVersion": base.pefile.__version__}
    output = args.output / "evidence-review.json"
    output.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"passed": True, "sourceCount": len(source_rows), "coffObjects": len(all_objects),
                      "pePdbPairs": len(images), "pdbObjectBindings": sum(len(row["objects"]) for row in bindings),
                      "coreR3Sources": len(core_matches), "indexedDirectories": len(historical),
                      "output": str(output), "sha256": digest(output)}))


if __name__ == "__main__":
    main()
