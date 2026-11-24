"""Run the real relocated V7 package verifier against isolated, rehashed malformed candidates.

Copies package bytes only. Never loads a native image or writes into the source game/candidate.
"""
from __future__ import annotations

import argparse
import base64
import importlib.util
import io
import json
import shutil
import time
import zipfile
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--candidate-directory", type=Path, required=True)
    parser.add_argument("--game-directory", type=Path, required=True)
    parser.add_argument("--legacy-v6-helper", type=Path, required=True)
    parser.add_argument("--output-directory", type=Path, required=True)
    args = parser.parse_args()
    verifier_path = Path(__file__).with_name("verify-engine-transport-production-package.py").resolve(strict=True)
    spec = importlib.util.spec_from_file_location("single_v7_package_verifier", verifier_path)
    verifier = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(verifier)

    candidate = args.candidate_directory.resolve(strict=True)
    game = args.game_directory.resolve(strict=True)
    legacy = verifier.ordinary(args.legacy_v6_helper)
    output = args.output_directory.resolve()
    verifier.need(args.output_directory.is_absolute() and not output.exists(), "A fresh absolute result directory is required")
    for original in (candidate, game):
        verifier.need(output != original and original not in output.parents and output not in original.parents,
                      "The isolated result directory overlaps the source candidate or real game")
    output.mkdir(parents=True)
    staged = output / "relocated-candidate"
    results = []
    originals = {}
    source_rows = []
    game_rows = []
    report = dict(schemaVersion=1, kind="single-v7-relocated-package-negative-checks", passed=False,
                  nativeExecuted=False, gameExecuted=False, installed=False, realGameMutated=False,
                  sourceCandidateMutated=False, productionAdmissionGranted=False, cases=results)

    def text_json(path, value):
        path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")

    def stage_path(relative):
        path = staged / relative
        verifier.need(path.resolve().is_relative_to(staged), "A negative mutation escaped the isolated candidate")
        return path

    def write(relative, data):
        path = stage_path(relative)
        if relative not in originals:
            originals[relative] = path.read_bytes()
        path.write_bytes(data)

    def mutate_json(relative, action):
        value = json.loads(stage_path(relative).read_text(encoding="utf-8-sig"))
        action(value)
        write(relative, (json.dumps(value, indent=2) + "\n").encode())

    def rehash(*relatives):
        def update(manifest):
            for relative in relatives:
                collection, name = relative.split("/", 1)
                rows = manifest["files" if collection == "package" else "evidence"]
                matches = [row for row in rows if row["path"] == name]
                verifier.need(len(matches) == 1, "A mutated package role is not unique")
                actual = verifier.row(stage_path(relative))
                matches[0].update(sha256=actual["sha256"], bytes=actual["bytes"])
        mutate_json("transport-candidate-package-manifest.json", update)

    def mutate_evidence(name, action):
        relative = "evidence/" + name
        mutate_json(relative, action)
        rehash(relative)

    def mutate_archive(relative, entry_name, transform):
        with zipfile.ZipFile(stage_path(relative)) as archive:
            entries = [(entry, archive.read(entry)) for entry in archive.infolist()]
        verifier.need(sum(entry.filename == entry_name for entry, _ in entries) == 1, "No exact archived input to mutate")
        memory = io.BytesIO()
        with zipfile.ZipFile(memory, "w") as archive:
            for entry, data in entries:
                changed = transform(data) if entry.filename == entry_name else data
                if changed is not None:
                    archive.writestr(entry, changed)
        write(relative, memory.getvalue())

    def bind_helper_hash():
        relative = "package/BepInEx/plugins/RizToolbox/RizToolbox.JudgeContext.dll"
        helper = verifier.row(stage_path(relative))
        def exports(value):
            value["helper"]["sha256"] = helper["sha256"]
        mutate_json("evidence/operation-export-validation.json", exports)
        def build(value):
            rows = [row for row in value["files"] if Path(row["path"]).name == "RizToolbox.JudgeContext.dll"]
            verifier.need(len(rows) == 1, "No unique actual helper build row")
            rows[0].update(sha256=helper["sha256"], bytes=helper["bytes"])
            value["actualExportValidation"]["sha256"] = verifier.row(stage_path("evidence/operation-export-validation.json"))["sha256"]
        mutate_json("evidence/operation-native-build.json", build)
        build_hash = verifier.row(stage_path("evidence/operation-native-build.json"))["sha256"]
        mutate_json("evidence/operation-managed-proof.json", lambda value: value.update(helperSha256=helper["sha256"]))
        mutate_json("evidence/tween-admission-native-result.json", lambda value: value.update(
            helperSha256=helper["sha256"], nativeBuildManifestSha256=build_hash))
        def gate(value):
            value["helper"]["sha256"] = helper["sha256"]
            value["nativeBuildManifest"]["sha256"] = build_hash
            value["result"]["sha256"] = verifier.row(stage_path("evidence/tween-admission-native-result.json"))["sha256"]
        mutate_json("evidence/tween-admission-native-proof.json", gate)
        rehash(relative, "evidence/operation-export-validation.json", "evidence/operation-native-build.json",
               "evidence/operation-managed-proof.json", "evidence/tween-admission-native-result.json", "evidence/tween-admission-native-proof.json")

    def case(name, mutate=None, reason=None):
        started = time.monotonic()
        item = dict(name=name, passed=False, outerHashesRebound=mutate is not None, nativeExecuted=False)
        try:
            if mutate:
                mutate()
            if reason is None:
                actual = verifier.validate_package(staged, game)
                verifier.need(actual["operationPublication"]["publicationVersion"] == 7, "A real V7 positive result is required")
                item["validation"] = actual
            else:
                failure = None
                try:
                    verifier.validate_package(staged, game)
                except (ValueError, KeyError) as error:
                    failure = str(error)
                verifier.need(failure is not None and reason in failure, f"Expected exact deep V7 rejection containing {reason!r}; actual={failure!r}")
                item["reason"] = failure
                item["rejected"] = True
            item["passed"] = True
            print("PASS " + name, flush=True)
        except Exception as error:
            item["error"] = f"{type(error).__name__}: {error}"
            print("FAIL " + name + ": " + item["error"], flush=True)
        finally:
            directory = output / "cases" / name
            directory.mkdir(parents=True)
            item["mutations"] = []
            for relative, original in originals.items():
                path = stage_path(relative)
                changed = path.read_bytes()
                saved = directory / relative
                saved.parent.mkdir(parents=True, exist_ok=True)
                saved.write_bytes(changed)
                item["mutations"].append(dict(path=relative, beforeSha256=verifier.digest(original),
                                               afterSha256=verifier.digest(changed), bytes=len(changed)))
                path.write_bytes(original)
            originals.clear()
            item["elapsedSeconds"] = round(time.monotonic() - started, 3)
            text_json(directory / "result.json", item)
            results.append(item)

    try:
        verifier.validate_package(candidate, game)
        source_rows = [verifier.row(path) for path in sorted(candidate.rglob("*")) if path.is_file()]
        game_rows = [verifier.row(game / name) for name in ("GameAssembly.dll", "UnityPlayer.dll", "Rizline.exe")]
        report["inputs"] = source_rows + [verifier.row(verifier_path), verifier.row(Path(__file__).resolve()), verifier.row(legacy)] + game_rows
        shutil.copytree(candidate, staged)
        case("actual-current-V7-relocated-package")
        verifier.need(results[-1]["passed"], "The real positive V7 package must pass before malformed-candidate tests")

        case("old-V6-manifest-with-all-current-payload-bytes",
             lambda: mutate_json("transport-candidate-package-manifest.json", lambda value: value.update(
                 operationPublicationVersion=6, operationPublicationBytes=2112, operationExpectedWrites=57)), "exclusive V7 helper")
        legacy_image = verifier.Image(legacy)
        verifier.need(set(name for name in legacy_image.exports() if name.startswith("RizFirstInstall_GetInfo")) == {"RizFirstInstall_GetInfoV6"},
                      "The legacy negative must use an actual V6-only native helper")
        def old_helper():
            write("package/BepInEx/plugins/RizToolbox/RizToolbox.JudgeContext.dll", legacy.read_bytes())
            bind_helper_hash()
        case("actual-V6-helper-with-rebound-provenance", old_helper, "Exact final EAT contract differs")

        def missing_export():
            relative = "package/BepInEx/plugins/RizToolbox/RizToolbox.JudgeContext.dll"
            data = bytearray(stage_path(relative).read_bytes())
            image = verifier.Image(stage_path(relative))
            exports = image.pe.DIRECTORY_ENTRY_EXPORT.struct
            found = []
            for index in range(exports.NumberOfNames):
                rva = image.pe.get_dword_at_rva(exports.AddressOfNames + index * 4)
                if image.pe.get_string_at_rva(rva) == b"RizTweenSingleEventContext_ReadCurrent":
                    found.append(image.pe.get_offset_from_rva(rva))
            verifier.need(len(found) == 1, "No actual Single ReadCurrent EAT name to remove")
            data[found[0]] = ord("X")
            write(relative, bytes(data)); bind_helper_hash()
        case("missing-one-actual-Single-export-with-rebound-helper-hashes", missing_export, "Exact final EAT contract differs")

        def remove_compiled_input():
            name = "native/RizToolbox.JudgeContext/TweenSingleEventPublication.inl"
            mutate_archive("evidence/operation-native-sources.zip", name, lambda _: None)
            def build(value):
                value["sources"] = [row for row in value["sources"] if row["relativePath"] != name]
                value["sourceArchive"]["sha256"] = verifier.row(stage_path("evidence/operation-native-sources.zip"))["sha256"]
            mutate_json("evidence/operation-native-build.json", build)
            rehash("evidence/operation-native-build.json", "evidence/operation-native-sources.zip")
        case("missing-compiled-Single-publication-input-with-rebound-archive", remove_compiled_input, "omits compiled Single producer inputs")

        def change_span_declaration():
            name = "native/RizToolbox.JudgeContext/TweenSingleEventPublication.inl"
            changed = None
            def transform(data):
                nonlocal changed
                verifier.need(b"0xbc1087" in data, "Actual Single span declaration missing")
                changed = data.replace(b"0xbc1087", b"0xbc1088", 1)
                return changed
            mutate_archive("evidence/operation-native-sources.zip", name, transform)
            def build(value):
                rows = [row for row in value["sources"] if row["relativePath"] == name]
                verifier.need(len(rows) == 1, "No exact helper compiled source declaration")
                rows[0].update(sha256=verifier.digest(changed), bytes=len(changed))
                value["sourceArchive"]["sha256"] = verifier.row(stage_path("evidence/operation-native-sources.zip"))["sha256"]
            mutate_json("evidence/operation-native-build.json", build)
            mutate_json("evidence/operation-profile-verification.json", lambda value: value["singleEventPublisher"].update(sha256=verifier.digest(changed)))
            rehash("evidence/operation-native-build.json", "evidence/operation-native-sources.zip", "evidence/operation-profile-verification.json")
        case("changed-compiled-Single-span-with-rebound-profile-and-archive", change_span_declaration, "fixed seven-span/4096-byte contract")
        case("missing-actual-Single-section-with-rebound-result",
             lambda: mutate_evidence("operation-native-result.json", lambda value: value.pop("singleEventPublication")), "omits the Single publication section")
        for field, changed in (("attemptedWrites", 6), ("completedWrites", 6), ("publishedMask", 63), ("parentEpoch", 0)):
            case("changed-native-Single-" + field,
                 lambda field=field, changed=changed: mutate_evidence("operation-native-result.json", lambda value: value["singleEventPublication"].update({field: changed})),
                 "epoch" if field == "parentEpoch" else "native write counts")
        case("changed-native-Single-block-verification-extent",
             lambda: mutate_evidence("operation-native-result.json", lambda value: value.update(singleBlockBytesVerified=64)), "complete-block seal")
        case("changed-independent-seven-span-profile",
             lambda: mutate_evidence("operation-profile-verification.json", lambda value: value["singleEventSpans"][0].update(rva=verifier.SINGLE_SPAN_RVAS[0] + 1)), "actual Single source span")
        def omit_mode(value):
            removed = [item for item in value["tamperModes"] if item["mode"] == "tamper-single-span-0-byte-0"]
            verifier.need(len(removed) == 1, "No exact actual Single byte mode")
            value["tamperModes"] = [item for item in value["tamperModes"] if item not in removed]
            value["independentProcesses"] -= 1; value["allProcessChecks"] -= removed[0]["checks"]
        case("omitted-Single-byte-process-with-rebound-totals", lambda: mutate_evidence("operation-native-result.json", omit_mode), "182 actual")
        def wrong_offset(value):
            matches = [item for item in value["tamperModes"] if item["mode"] == "tamper-single-block-byte-4095"]
            verifier.need(len(matches) == 1, "No exact sampled Single block case")
            matches[0]["tamperedSingleBlockOffset"] = 4094
        case("sampled-block-offset-substituted-with-rebound-result", lambda: mutate_evidence("operation-native-result.json", wrong_offset), "exact source byte")
        case("sampled-block-faults-misrepresented-as-exhaustive",
             lambda: mutate_evidence("operation-native-result.json", lambda value: value.update(singleBlockTamperIsExhaustive=True)), "distinguish full seal")
        def temporary_unknown(value):
            next(item for item in value["tamperModes"] if item["mode"] == "tamper-single-span-0-byte-0")["singlePermanentUnknownAfterRestore"] = False
        case("Single-owner-incorrectly-recovers-after-input-restoration", lambda: mutate_evidence("operation-native-result.json", temporary_unknown), "permanent Unknown")
        case("missing-linked-object-partial-publication-proof", lambda: mutate_evidence("operation-native-result.json", lambda value: value.pop("singlePartialPublication")),
             "linked-object Single partial-publication evidence")
        def changed_embedded_raw(value):
            value["singlePartialPublication"]["resultRawBase64"] = base64.b64encode(b"{}").decode("ascii")
        case("changed-inner-original-partial-evidence-with-rebound-outer-hash", lambda: mutate_evidence("operation-native-result.json", changed_embedded_raw),
             "Embedded original partial-publication evidence bytes changed")
        def modify_partial_original(value, name, mutate):
            proof = value["singlePartialPublication"]
            original = json.loads(base64.b64decode(proof[name + "RawBase64"]).decode("utf-8-sig"))
            mutate(original)
            raw = (json.dumps(original, indent=2) + "\n").encode("utf-8")
            proof[name + "RawBase64"] = base64.b64encode(raw).decode("ascii")
            proof[name + "Sha256"] = verifier.digest(raw)
        def changed_partial_writes(value):
            modify_partial_original(value, "result", lambda inner: inner["singlePartialWriteModes"][0].update(singleCompletedWrites=7))
        case("partial-completed-write-count-forged-with-rebound-original-and-outer-hashes", lambda: mutate_evidence("operation-native-result.json", changed_partial_writes),
             "partial publication lost its observed masks")
        def changed_linked_object(value):
            def change(inner):
                row = next(row for row in inner["files"] if Path(row["path"]).name == "TweenSingleEventContext.obj")
                row["sha256"] = "0" * 64
            modify_partial_original(value, "manifest", change)
        case("partial-linked-object-substitution-with-rebound-original-and-outer-hashes", lambda: mutate_evidence("operation-native-result.json", changed_linked_object),
             "linked an object from a different actual helper build")
        def changed_partial_source(value):
            row = next(row for row in value["singlePartialPublication"]["sourceFiles"] if row["relativePath"].endswith("/TweenSingleEventPublication.inl"))
            row["sha256"] = "0" * 64
        case("partial-producer-source-substitution-with-rebound-outer-hash", lambda: mutate_evidence("operation-native-result.json", changed_partial_source),
             "helper's independently archived native bytes")
        def clr_parent(value):
            value["modes"][0]["singleEventPublication"]["parentEpoch"] += 1
        case("CLR-Single-parent-substituted-with-rebound-proof", lambda: mutate_evidence("operation-managed-proof.json", clr_parent), "parent epoch")
        for field in ("ProductionV7Verified", "SingleEventPublicationVerified", "SingleEventMemoryVerified", "SingleEventReadonlyAdoption"):
            case("CLR-missing-" + field,
                 lambda field=field: mutate_evidence("operation-managed-proof.json", lambda value: value["modes"][0]["operationAdoption"].update({field: False})),
                 "Single readonly adoption")
        case("CLR-incomplete-Single-memory-extent", lambda: mutate_evidence("operation-managed-proof.json", lambda value: value["modes"][0]["singleEventPublication"].update(blockBytesVerified=64)),
             "actual CLR Single adoption")
        case("CLR-initial-observation-phase-substituted", lambda: mutate_evidence("operation-managed-proof.json", lambda value: value["modes"][0]["singleEventPublication"].update(observationPhase="after-adoption")),
             "actual CLR Single adoption")
        def bridge_capacity(value):
            next(item for item in value["modes"] if item["mode"] == "happy")["singleEventBridgeAdoption"]["parentCapacity"] = 32
        case("CLR-shared-Single-observer-capacity-substituted", lambda: mutate_evidence("operation-managed-proof.json", bridge_capacity),
             "happy CLR Single bridge adoption")
        def reentry_bridge(value):
            happy = next(item for item in value["modes"] if item["mode"] == "happy")
            reentry = next(item for item in value["modes"] if item["mode"] == "reentry")
            reentry["singleEventBridgeAdoption"] = happy["singleEventBridgeAdoption"]
        case("CLR-reentry-incorrectly-claims-observer-adoption", lambda: mutate_evidence("operation-managed-proof.json", reentry_bridge),
             "reentry refusal cannot claim")
        case("positive-after-all-isolated-mutations-restored")
        report["sourceCandidateUnchanged"] = all(verifier.row(Path(row["path"]))["sha256"] == row["sha256"] for row in source_rows)
        report["sourceCandidateMutated"] = not report["sourceCandidateUnchanged"]
        report["realGameFilesUnchanged"] = all(verifier.row(Path(row["path"]))["sha256"] == row["sha256"] for row in game_rows)
        report["realGameMutated"] = not report["realGameFilesUnchanged"]
        report["inputHashesUnchanged"] = all(verifier.row(Path(row["path"]))["sha256"] == row["sha256"] for row in report["inputs"])
        report["passed"] = len(results) == 32 and all(item["passed"] for item in results) and report["inputHashesUnchanged"]
    except Exception as error:
        report["failure"] = f"{type(error).__name__}: {error}"
    finally:
        report["checks"] = len(results)
        report["passedChecks"] = sum(item["passed"] for item in results)
        text_json(output / "test-result.json", report)
    print(f"{report['passedChecks']}/{report['checks']} actual V7 relocated package checks passed; no native execution or game mutation.", flush=True)
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
