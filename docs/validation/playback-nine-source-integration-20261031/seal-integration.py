"""Offline seal of the exact core-r4 nine-member batch; never runs native code.

All formal production/test copies must equal core-r4.  The old exact xdata POC
has its own Program-only overlay and its original failed collector is retained.
Its historical claims are never promoted to current matrix execution evidence.
"""
import argparse
import hashlib
import json
import re
import struct
import uuid
import zipfile
from datetime import datetime, timezone
from pathlib import Path


BASE = Path("D:/riz-build-cache-next")
REPOSITORY = Path(__file__).resolve().parents[3]
CORE_SHA = "d96e595c2b8244d176b3b748f72d265212489f2a845791f4ba9f6b2a647707d0"
PRECAP_SHA = "da05fb89baa662abc2b641d6e2842bada9a166545b616e6c01ade281ee3a9908"
GROUP_SHA = "0f95fffcfc0410b07a3a06f4d99e7152ec9d9612deca4202a9d3f116f81ceb43"
PROGRAM = "tests/riztoolbox.playbacksourcegroup.tests/program.cs"
OLD_COMMANDS_SHA = "ffbb84244c83d97791850748d7fe48bd232a340a2d474fb8e519b5ea2b0c95ef"
CURRENT_COMMANDS_SHA = "6d47c3e89bf50af12489f88ba10f551c712c7d35da2f3610cd65106348c4c666"
EXPECTED = {
    "core": "foundation-nine-source-core-r4-20261031/test-result.json",
    "full": "foundation-nine-source-full-r2-20261031/test-result.json",
    "release": "foundation-nine-source-release-r2-20261031/managed-build-manifest.json",
    "solution": "foundation-nine-source-solution-r1-20261031/test-result.json",
    "sdk": "foundation-nine-source-sdk-r1-20261031/test-result.json",
    "bridge": "playback-nine-source-bridge-r2-20261031/test-result.json",
    "bridgeSymbols": "playback-nine-source-bridge-symbols-r1-20261031/test-result.json",
    "trampolines": "playback-nine-source-trampolines-r2-20261031/test-result.json",
    "trampolineSymbols": "playback-nine-source-trampoline-symbols-r1-20261031/test-result.json",
    "group": "playback-source-nine-group-r3-20261031/test-result.json",
    "mapped": "playback-stock-nine-joint-continuation-proof-r1-20261031/test-result.json",
    "nativeBuild": "playback-stock-native-build-r14-20261031/build-result.json",
    "nativeChecks": "playback-stock-native-checks-r14-full-20261031/test-result.json",
    "nativeSymbols": "playback-stock-native-build-r14-20261031/pe-pdb-source-verification.json",
    "foundationNative": "foundation-private-native-r3-20261031/native-manifest.json",
    "foundationMapped": "foundation-private-mapped-r4-20261031/test-result.json",
    "foundationMachine": "foundation-private-machine-r4-20261031/test-result.json",
    "foundationSeh": "foundation-private-relocated-seh-r1-20261031/test-result.json",
    "foundationProof": "foundation-private-proof-r1-20261031/evidence-review.json",
    "foundationReport": "foundation-private-report-seal-r1-20261031/proof/report-integrity.json",
    "precapReview": "playback-source-precap-xdata-final-r1-20261031/review-result.json",
    "precapFailedCollector": "playback-source-precap-xdata-review-r2-20261031/review-result.json",
    "precapHistoricalAcceptance": "playback-source-precap-xdata-review-r1-20261031/review-result.json",
    "packageProtocol": "playback-nine-package-protocol-r1-20261031/test-result.json",
    "packageParse": "playback-nine-package-protocol-r1-20261031/powershell-parse.json",
}
FALSE_FLAGS = ("gameExecuted", "gameCodeExecuted", "gameDllMainExecuted", "gameDllMainInvoked",
               "gameOriginalOrDllMainExecuted", "dllMainExecuted", "dllMainInvoked", "gameLaunched",
               "gameImageLoaded", "originalInitInvoked", "originalIl2CppInitInvoked", "installed",
               "productionAdmission", "productionAdmissionVerified", "productionAdmissionGranted",
               "productionFirstWriteAdmission", "productionOwnerIssued", "candidatePackageVerified",
               "candidatePacked", "candidateInstalled")


def require(value, message):
    if not value:
        raise ValueError(message)


def read(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def sha(path):
    with Path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def write(path, value):
    Path(path).write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def key(path):
    return str(Path(path).resolve()).casefold()


def relative(path, root):
    try:
        return Path(path).resolve().relative_to(Path(root).resolve()).as_posix().casefold()
    except ValueError:
        raise ValueError(f"Evidence path escaped its declared root: {path}; root={root}") from None


def scope(value, label):
    for flag in FALSE_FLAGS:
        if flag in value:
            require(value[flag] is False, f"{label}: wrong execution/admission scope: {flag}")


class Evidence:
    def __init__(self, worktree_archive=None):
        self.files = {}
        self.stats = {}
        self.references = 0
        self.historical_claims = []
        self.worktree_archive = worktree_archive or {}

    def check(self, path, expected=None, *, base=None, size=None, label="evidence", allow_worktree_archive=True):
        path = Path(path)
        if not path.is_absolute():
            require(base is not None, f"Unbased relative evidence path: {path}")
            path = Path(base) / path
        path = path.resolve()
        original = path
        archived = self.worktree_archive.get(key(path)) if allow_worktree_archive else None
        if archived is not None:
            path = Path(archived["path"]).resolve()
            require(expected is None or expected.lower() == archived["sha256"],
                    f"{label}: the pre-merge archive does not match the original frozen input: {original}")
            expected = archived["sha256"]
        require(path.is_file(), f"{label}: missing evidence: {path}")
        stat = path.stat()
        signature = (stat.st_size, stat.st_mtime_ns)
        identity = key(path)
        if identity not in self.files or self.stats[identity] != signature:
            digest = sha(path)
            if identity in self.files:
                require(self.files[identity]["sha256"] == digest, f"Evidence changed during verification: {path}")
            self.files[identity] = {"path": str(path), "sha256": digest, "bytes": stat.st_size}
            self.stats[identity] = signature
        fact = self.files[identity]
        if archived is not None:
            fact["originalWorktreePath"] = str(original)
        if expected is not None:
            require(isinstance(expected, str) and re.fullmatch(r"[0-9a-fA-F]{64}", expected),
                    f"{label}: invalid SHA-256 fact: {path}")
            require(fact["sha256"] == expected.lower(), f"{label}: changed evidence: {path}")
        if size is not None:
            require(stat.st_size == size, f"{label}: byte count differs: {path}")
        self.references += 1
        return fact

    def collect(self, value, base, label):
        if isinstance(value, list):
            for index, item in enumerate(value):
                self.collect(item, base, f"{label}[{index}]")
        elif isinstance(value, dict):
            pairs = (("path", "sha256"), ("file", "sha256"), ("manifestPath", "sha256"),
                     ("object", "sha256"), ("image", "imageSha256"), ("image", "sha256"),
                     ("pdb", "pdbSha256"), ("coffObject", "coffObjectSha256"), ("source", "sha256"))
            for path_key, hash_key in pairs:
                path, digest = value.get(path_key), value.get(hash_key)
                if not isinstance(path, str) or not isinstance(digest, str):
                    continue
                if (label.startswith("precapFailedCollector.artifacts[") and path_key == "path" and
                        key(path) == key(BASE / "playback-source-precap-xdata-review-r2-20261031/commands.json")):
                    # The failed collector wrote its result before run.py appended
                    # that collector's exit=1 to commands.json. The immutable failed
                    # result remains evidence; final-r1 pins the completed command log.
                    require(digest == OLD_COMMANDS_SHA, "Unexpected superseded command-log claim")
                    current = self.check(path, CURRENT_COMMANDS_SHA, label=label)
                    self.historical_claims.append({"claimIn": label, "path": str(Path(path).resolve()),
                        "recordedHistoricalSha256": digest, "currentFinalizedSha256": current["sha256"],
                        "reason": "Failed collector predated its own exit=1 command-log append; final-r1 pins finalized bytes.",
                        "acceptedAsCurrentPassingProof": False})
                else:
                    self.check(path, digest, base=base,
                               size=value.get("bytes") if path_key in ("path", "file") else None,
                               label=label)
            for name, item in value.items():
                self.collect(item, base, f"{label}.{name}")

    def load_ref(self, fact, label):
        checked = self.check(fact.get("path", fact.get("manifestPath")), fact["sha256"], label=label)
        value = read(checked["path"])
        self.collect(value, Path(fact.get("path", fact.get("manifestPath"))).parent, label)
        return value

    def finish(self):
        for identity, fact in list(self.files.items()):
            stat = Path(fact["path"]).stat()
            require((stat.st_size, stat.st_mtime_ns) == self.stats[identity],
                    f"Evidence changed before seal completion: {fact['path']}")


def last_json(path):
    lines = [line for line in Path(path).read_text(encoding="utf-8-sig").splitlines() if line.strip()]
    require(lines, f"Empty actual execution log: {path}")
    value = json.loads(lines[-1])
    require(isinstance(value, dict), f"Actual execution log has no object result: {path}")
    return value


def zero_exit(evidence, path, expected=0):
    evidence.check(path)
    require(Path(path).read_text(encoding="utf-8-sig").strip() == str(expected),
            f"Actual exit sidecar differs: {path}")


def reference(evidence, fact, path, label):
    require(key(fact.get("path", fact.get("manifestPath"))) == key(path), f"{label}: wrong manifest path")
    evidence.check(path, fact["sha256"], label=label)


def inventory(rows, root):
    result = {}
    for row in rows:
        rel = relative(row["path"], root)
        require(rel not in result, f"Duplicate frozen input: {rel}")
        result[rel] = row["sha256"]
    return result


def archive_matches(evidence, fact, expected, label):
    evidence.check(fact["path"], fact["sha256"], label=label)
    with zipfile.ZipFile(fact["path"]) as archive:
        files = [item for item in archive.infolist() if not item.is_dir()]
        names = [item.filename.replace("\\", "/").casefold() for item in files]
        require(len(names) == len(set(names)) == len(expected), f"{label}: archive inventory differs")
        require(set(names) == set(expected), f"{label}: archive paths differ")
        for item, name in zip(files, names):
            require(hashlib.sha256(archive.read(item)).hexdigest() == expected[name], f"{label}: archive member differs: {name}")


def formal_copy(evidence, value, source_root, core_inputs, core_root, label, field="sources", extras=None):
    rows = value[field]
    actual = inventory(rows, source_root)
    require(all(actual.get(rel) == digest for rel, digest in core_inputs.items()), f"{label}: omitted or overlaid core-r4 input")
    if extras is None:
        require(actual == core_inputs, f"{label}: undeclared source input/overlay")
    else:
        require({rel: digest for rel, digest in actual.items() if rel not in core_inputs} == extras,
                f"{label}: extra inputs differ from declared current worktree sources")
    for row in rows:
        if "source" in row:
            rel = relative(row["path"], source_root)
            require(key(row["source"]) == key(Path(core_root) / rel), f"{label}: copy origin is not exact core-r4")
            evidence.check(row["source"], row["sha256"], label=label)
    require(value.get("sourceOverlayUsed", False) is False and not value.get("overlayInputs") and
            "overlay" not in value and not any(row.get("testOnlyOverlay") for row in rows),
            f"{label}: formal matrix must have no overlays")


def managed_symbols(images, source_root, frozen, label, overlay=None):
    # Independently read each PE CodeView record and the portable PDB #Pdb ID.
    # This is byte parsing only, with no assembly/native loading or execution.
    import pefile
    count = 0
    for row in images:
        require(row["codeViewMatchesPortablePdb"] is True and row["documentChecksumsMatch"] is True,
                f"{label}: PE/PDB/source identity was not established")
        assembly = pefile.PE(row["assembly"]["path"], fast_load=False)
        try:
            records = [entry for entry in assembly.DIRECTORY_ENTRY_DEBUG if entry.struct.Type == 2]
            expected_guid = uuid.UUID(row["codeViewGuid"]).bytes_le
            require(len(records) == 1, f"{label}: ambiguous CodeView identity")
            debug = records[0].struct
            raw = assembly.__data__[debug.PointerToRawData:debug.PointerToRawData + debug.SizeOfData]
            require(raw[:4] == b"RSDS" and raw[4:20] == expected_guid and
                    struct.unpack_from("<I", raw, 20)[0] == row["codeViewAge"], f"{label}: actual PE CodeView differs")
            pdb = Path(row["pdb"]["path"]).read_bytes()
            require(pdb[:4] == b"BSJB", f"{label}: expected a portable PDB")
            offset = (16 + struct.unpack_from("<I", pdb, 12)[0] + 3) & ~3
            streams = struct.unpack_from("<H", pdb, offset + 2)[0]
            offset += 4
            pdb_id = None
            for _ in range(streams):
                start, size = struct.unpack_from("<II", pdb, offset)
                end = pdb.index(b"\0", offset + 8)
                name = pdb[offset + 8:end]
                offset = (end + 4) & ~3
                if name == b"#Pdb":
                    require(size >= 20, f"{label}: truncated portable PDB ID")
                    pdb_id = pdb[start:start + 20]
            require(pdb_id is not None and pdb_id.hex() == row["portablePdbId"].lower() and
                    pdb_id[:16] == expected_guid and int.from_bytes(pdb_id[16:], "little") == row["codeViewStamp"],
                    f"{label}: actual portable PDB ID differs")
        finally:
            assembly.close()
        documents = row["documents"]
        require(row["documentCount"] == len(documents) and row["frozenDocumentCount"] ==
                sum(doc["frozenInput"] is True for doc in documents), f"{label}: document totals differ")
        for doc in documents:
            rel = relative(doc["path"], source_root)
            if doc["frozenInput"]:
                expected = overlay if overlay and rel == PROGRAM else frozen.get(rel)
                require(expected == doc["sha256"] and doc["generatedByBuild"] is False,
                        f"{label}: compiled frozen document differs from declared core/POC scope: {rel}")
            else:
                require(doc["generatedByBuild"] is True and "obj" in Path(rel).parts,
                        f"{label}: undeclared compiled source: {rel}")
        count += len(documents)
    return count


def build_summary(evidence, path, warnings=None):
    evidence.check(path)
    text = Path(path).read_text(encoding="utf-8-sig")
    errors = re.findall(r"^\s*(\d+)\s+(?:Error\(s\)|个错误)\s*$", text, re.M | re.I)
    warning_counts = re.findall(r"^\s*(\d+)\s+(?:Warning\(s\)|个警告)\s*$", text, re.M | re.I)
    require(errors and errors[-1] == "0", f"Actual build log has no zero-error summary: {path}")
    if warnings is not None:
        require(warning_counts and int(warning_counts[-1]) == warnings, f"Actual warning count differs: {path}")
    return text


def native_facts(evidence, symbols, foundation_proof, protocol):
    # Reuse the pinned read-only PE/MSF-PDB/C13 parser without invoking main().
    # No DLL loading, process execution, build, mapping, or matrix rerun occurs.
    import pefile
    parser_path = REPOSITORY / "scripts/Verify-PlaybackSourcePrototype.py"
    evidence.check(parser_path, "03e40f486da3c6f240d5158e0dd90d50f8f469bcdff6a61ac97155e94905d89c")
    namespace = {"__name__": "nine_source_seal_offline_parser", "__file__": str(parser_path)}
    exec(compile(parser_path.read_text(encoding="utf-8-sig"), str(parser_path), "exec"), namespace)
    for rows in (symbols["images"], foundation_proof["nativeImages"]):
        for row in rows:
            require(namespace["image_identity"](Path(row["image"]), Path(row["pdb"])) == row,
                    "Actual native PE/MSF-PDB identity differs from recorded proof")
    for rows in (symbols["compiledObjects"], foundation_proof["compiledObjects"]):
        for row in rows:
            require(namespace["compiled_sources"](Path(row["object"])) == row,
                    "Actual COFF C13 source facts differ from recorded proof")
    image = BASE / "playback-stock-native-build-r14-20261031/RizToolbox.PlaybackStockFixture.dll"
    pe = pefile.PE(str(image))
    try:
        exports = {row.name.decode("ascii"): row.address for row in pe.DIRECTORY_ENTRY_EXPORT.symbols if row.name}
        for row in symbols["stockTargets"]:
            rva = int(row["rva"], 0)
            body = pe.get_data(rva, row["length"])
            require(exports[row["name"]] == rva and exports[row["name"] + "End"] == rva + row["length"] and
                    hashlib.sha256(body).hexdigest() == row["bodySha256"] and body.startswith(bytes.fromhex(row["prefix"])),
                    "Actual native stock-shaped body fact differs")
            functions = [entry.struct for entry in pe.DIRECTORY_ENTRY_EXCEPTION if rva <= entry.struct.BeginAddress < rva + row["length"]]
            require(len(functions) == len(row["runtimeFunctions"]) == row["runtimeFunctionCount"], "Actual native RF count differs")
            for actual, recorded in zip(functions, row["runtimeFunctions"]):
                unwind = bytes.fromhex(recorded["unwindHex"])
                require(actual.BeginAddress - rva == recorded["beginOffset"] and actual.EndAddress - rva == recorded["endOffset"] and
                        actual.UnwindData == int(recorded["unwindRva"], 0) and pe.get_data(actual.UnwindData, len(unwind)) == unwind,
                        "Actual native RF/xdata fact differs")
        require(namespace["executable_dir64"](pe, exports, Path(symbols["executableDir64"]["coffObject"])) == symbols["executableDir64"],
                "Actual native executable DIR64/COFF relocation facts differ")
    finally:
        pe.close()
    helper = pefile.PE(str(BASE / "foundation-private-native-r3-20261031/RizToolbox.JudgeContext.dll"))
    try:
        actual_exports = {row.name.decode("ascii"): row.address for row in helper.DIRECTORY_ENTRY_EXPORT.symbols if row.name}
        require(actual_exports == protocol["actualExports"], "Actual Foundation EAT differs from current package reader proof")
    finally:
        helper.close()


def verify_pre_capture(e, m, paths, frozen, core_root):
    final, failed, old = (m[name] for name in ("precapReview", "precapFailedCollector", "precapHistoricalAcceptance"))
    require(sha(paths["precapReview"]) == PRECAP_SHA, "The exact corrected old-POC review changed")
    reference(e, final["baseCoreProof"], paths["core"], "Exact POC base")
    reference(e, final["supersedesDerivedResult"], paths["precapFailedCollector"], "Preserved failed collector")
    reference(e, final["oldFalseAcceptanceProof"], paths["precapHistoricalAcceptance"], "Preserved historical acceptance")
    require(failed["passed"] is False and failed["rejectedBeforeAnyPrepareOrCommit"] is False,
            "The original namespace-mismatch collector failure must remain visible")
    require(final["collectorCorrection"] == "Match the actual Runtime.UnsupportedSignatureException namespace; no source build or native test rerun.",
            "The collector correction scope changed")
    require(old["falseAcceptanceConfirmed"] is True and old["actualDobbyPrepare"] == old["actualDobbyCommit"] == 9,
            "The historical false-acceptance counterexample disappeared")
    actual = final["actualFinalResult"]
    for name in ("mutation", "rejected", "actualFinalResult"):
        scope(final[name], "Exact POC " + name)
    require(final["buildExitCode"] == final["actualExitCode"] == final["symbolExitCode"] == 0 and
            actual["passed"] is True and actual["checks"] == 15 and
            final["actualDobbyPrepare"] == final["actualDobbyCommit"] ==
            actual["actualDobbyPrepareCount"] == actual["actualDobbyCommitCount"] == 0 and
            final["rejectedBeforeAnyPrepareOrCommit"] is True and actual["rejectedBeforeAnyPrepareOrCommit"] is True and
            actual["nativeEntryOrSehInvokedAfterMutation"] is False,
            "Exact old xdata POC was not rejected before every Prepare/Commit")
    require(final["onlySourceOverlay"].casefold() == PROGRAM and final["sourceFilesCompared"] == 727 and
            final["productionSourceFilesCompared"] == 453 and final["allProductionSourcesMatchCore"] is True and
            final["allNonProgramInputsMatchCore"] is True and final["nativeInputsIdenticalToOldPoc"] is True and
            final["nativeInputsUnchanged"] is True and final["oldEvidenceFilesUnchanged"] is True and
            final["workspaceProductionSourcesUnchanged"] is True, "Exact POC source/native scope differs")
    require(final["rejected"]["exceptionType"] == "RizToolbox.Runtime.UnsupportedSignatureException" and
            final["mutation"]["beforeResolveAndBind"] is True and final["mutation"]["executableBytesChanged"] is False and
            final["mutation"]["diskImageChanged"] is False and final["sehExecuted"] is False,
            "Exact POC mutation/rejection differs")
    comparison_path = Path(final["sourceOverlayComparison"]["path"])
    comparison = read(comparison_path)
    e.check(comparison_path, final["sourceOverlayComparison"]["sha256"])
    reference(e, comparison["baseCoreProof"], paths["core"], "POC comparison core-r4 proof")
    e.collect({k: v for k, v in comparison.items() if k != "sourceBefore"}, comparison_path.parent, "precapComparison")
    changed = comparison["changedSourceFiles"]
    require(len(changed) == 1 and changed[0]["relative"].casefold() == PROGRAM and
            changed[0]["baseSha256"] == frozen[PROGRAM] and changed[0]["reviewSha256"] != frozen[PROGRAM],
            "Exact POC has an undeclared overlay")
    require(comparison["sourceFilesCompared"] == 727 and comparison["productionSourceFilesCompared"] == 453 and
            len(comparison["productionSourceHashes"]) == 453 and comparison["allProductionSourcesMatchCore"] is True and
            all(row["relative"].casefold().startswith("src/") and
                frozen.get(row["relative"].casefold()) == row["baseSha256"] == row["reviewSha256"]
                for row in comparison["productionSourceHashes"]), "POC production-source comparison facts differ")
    review_root = paths["precapFailedCollector"].parent / "source"
    before = comparison["sourceBefore"]
    require(len(before) == 727 and len({row["relative"].casefold() for row in before}) == 727,
            "Exact POC does not enumerate every core input")
    for row in before:
        rel = row["relative"].casefold()
        require(frozen.get(rel) == row["sha256"] and key(row["basePath"]) == key(core_root / rel) and
                key(row["path"]) == key(review_root / rel), "POC baseline row has the wrong origin")
        e.check(row["basePath"], row["sha256"], size=row["bytes"], label="POC immutable baseline")
        e.check(row["path"], changed[0]["reviewSha256"] if rel == PROGRAM else row["sha256"],
                label="POC compiled source")
    baseline_text = paths["precapFailedCollector"].parent / "Program.core-baseline.cs.txt"
    e.check(baseline_text)
    require(baseline_text.read_text(encoding="utf-8-sig") == (core_root / PROGRAM).read_text(encoding="utf-8-sig"),
            "POC baseline text mirror differs beyond line-ending normalization")
    e.check(paths["precapFailedCollector"].parent / "Program.review.cs.txt", changed[0]["reviewSha256"])
    symbol = e.load_ref(final["symbols"]["proof"], "precapSymbols")
    require(symbol["passed"] is True and len(symbol["images"]) == symbol["pePdbPairs"] == 5,
            "Exact POC symbols are incomplete")
    require(managed_symbols(symbol["images"], review_root, frozen, "Exact POC symbols", changed[0]["reviewSha256"]) ==
            symbol["sourceDocuments"] == 456, "Exact POC document count differs")
    original_dir = paths["precapFailedCollector"].parent
    for name in ("build", "normal-nine", "symbols"):
        zero_exit(e, original_dir / (name + ".exit.txt"))
    zero_exit(e, original_dir / "finalize.exit.txt", 1)
    zero_exit(e, paths["precapReview"].parent / "finalize.exit.txt")
    lines = [json.loads(line) for line in (original_dir / "normal-nine.stdout.log").read_text(encoding="utf-8-sig").splitlines()
             if line.lstrip().startswith("{")]
    require(len(lines) == 3 and lines == [final["mutation"], final["rejected"], actual],
            "Exact POC actual log differs from corrected collector")
    commands = read(original_dir / "commands.json")
    require({row["name"]: row["actualExitCode"] for row in commands} ==
            {"prepare": 0, "build": 0, "normal-nine": 0, "symbols": 0, "finalize": 1},
            "The original failed runner command chain changed")
    require(len(e.historical_claims) == 1, "Preserved failed collector lost its explicit superseded command-log claim")
    return {"checks": 15, "pePdbPairs": 5, "sourceDocuments": 456, "sourceFiles": 727,
            "programOverlays": 1, "productionOverlays": 0, "baselineTextMirrorLineEndingsNormalized": True,
            "actualPrepare": 0, "actualCommit": 0,
            "originalCollectorExit": 1, "correctedCollectorExit": 0,
            "includedIn82ModeMatrix": False, "historicalFalseAcceptancePrepareCommit": [9, 9]}


def run(plan_path, output=None, index_path=None, preflight=False, historical_worktree=None):
    plan_path = Path(plan_path).resolve()
    if index_path is not None:
        require(key(index_path) == key(Path(__file__).resolve().parent / "evidence-index.json"),
                "The workspace evidence index must stay in this report package")
    plan = read(plan_path)
    require(set(plan) == set(EXPECTED), "The exact declared proof set differs from this batch")
    paths = {name: Path(path).resolve() for name, path in plan.items()}
    for name, rel in EXPECTED.items():
        require(key(paths[name]) == key(BASE / rel), f"{name}: wrong generation/batch; old dual-member/core-r3 evidence is forbidden")
    missing = [name for name, path in paths.items() if not path.is_file()]
    require(preflight or not missing, f"No seal until all required actual results exist: {', '.join(missing)}")
    if output is not None:
        output = Path(output).resolve()
        require(not output.exists(), "Preserve earlier seal results; use a fresh output directory")
        require(output.parent == BASE.resolve(), "Seal output must be a fresh direct child of D:/riz-build-cache-next")
    archived_inputs = {}
    if historical_worktree is not None:
        archive_path = Path(historical_worktree).resolve()
        archive = read(archive_path)
        require(archive["kind"] == "pre-stock-integration-worktree-archive" and archive["passed"] is True
                and archive["capturedBeforeImplementationWrites"] is True and archive["coreInputCount"] == 727
                and key(archive["repository"]) == key(REPOSITORY) and archive["coreProof"]["sha256"] == CORE_SHA,
                "Only the explicitly captured pre-integration core-r4 checkout may close this historical batch")
        archive_root = archive_path.parent / "before-worktree"
        for row in archive["files"]:
            original = Path(row["originalPath"]).resolve()
            actual = Path(row["archived"]["path"]).resolve()
            require(original.is_relative_to(REPOSITORY) and actual.is_relative_to(archive_root)
                    and original.relative_to(REPOSITORY) == actual.relative_to(archive_root)
                    and key(original) not in archived_inputs, "Historical archive path/identity differs")
            archived_inputs[key(original)] = row["archived"]
    e = Evidence(archived_inputs)
    if historical_worktree is not None:
        e.check(historical_worktree)
    m = {name: read(path) for name, path in paths.items() if path.is_file()}
    historical = {"precapFailedCollector", "precapHistoricalAcceptance"}
    for name, value in m.items():
        if name not in historical:
            require(value.get("passed") is True and value.get("failure") is None, f"{name}: no complete passing terminal result")
        scope(value, name)
        e.collect(value, paths[name].parent, name)
        e.check(paths[name])
    core = m["core"]
    require(sha(paths["core"]) == CORE_SHA and core["snapshotCaptured"] is True and
            core["runnerExitCode"] == 0 and core["child"]["passed"] is True and
            core["child"]["sourceHashesUnchanged"] is True, "The immutable core-r4 batch did not finish")
    core_root = paths["core"].parent / "source-snapshot"
    frozen = inventory(core["snapshotFiles"], core_root)
    live = inventory(core["inputs"], REPOSITORY)
    harness = "scripts/test-enginewaitmanagedchecks.ps1"
    require(set(frozen) == set(live) and len(live) == 727 and
            {rel for rel in live if live[rel] != frozen[rel]} == {harness} and
            live[harness] == "5c1d582c0a98db366a5bf5f2ea2fa0255e7064ea8a86f10e02f40ad56983feda" and
            frozen[harness] == "edb66f48a59dfb67cb700c7ab99a6d6b416f6c5890d294a44f33c891bf6bc903" and
            core["runnerTransformation"] == "Restore this fresh isolated source tree; explicitly exclude compiler-generated obj/bin from before/after source inventory because the snapshot has no enclosing Git repository.",
            "Core-r4 differs from its 727 current worktree inputs beyond the exact declared runner transformation")
    archive_matches(e, core["sourceArchive"], frozen, "Core-r4 archive")
    for name, checks in (("coordinator", 28), ("receiver", 10), ("audio", 1001)):
        log = paths["core"].parent / "checks" / (name + "-tests.log")
        e.check(log)
        require(f"{checks}/{checks}" in log.read_text(encoding="utf-8-sig").splitlines()[-1], f"Core actual {name} log differs")
    for name in ("full", "bridge", "bridgeSymbols", "trampolines", "trampolineSymbols", "group", "mapped"):
        if name in m:
            reference(e, m[name]["sourceProof"], paths["core"], name + " source proof")
    reference(e, m["release"]["sourceSnapshot"], paths["core"], "Release source proof")
    for name in ("bridge", "trampolines", "group", "mapped"):
        if name in m:
            reference(e, m[name]["nativeProof"], paths["nativeBuild"], name + " native proof")
            source_root = (BASE / "playback-stock-nine-joint-resume-r1-20261031/source") if name == "mapped" else paths[name].parent / "source"
            if name == "mapped":
                require(Path(m[name]["sourceDirectory"]).resolve() == source_root.resolve(), "Mapped continuation source root differs")
            formal_copy(e, m[name], source_root, frozen, core_root, name)
            require(m[name]["sourceArchive"]["sha256"] == core["sourceArchive"]["sha256"], f"{name}: snapshot archive differs")
    full = m["full"]
    full_root = paths["full"].parent / "source"
    full_live_extras = {relative(row["path"], REPOSITORY): row["sha256"] for row in full["inputs"]
                        if Path(row["path"]).resolve().is_relative_to(REPOSITORY) and relative(row["path"], REPOSITORY) not in frozen}
    full_extras = {rel: digest for rel, digest in full_live_extras.items() if rel != "scripts/test-foundationsnapshot.ps1"}
    formal_copy(e, full, full_root, frozen, core_root, "27-suite matrix", "compiledSources", full_extras)
    require(len(full_live_extras) == 21 and len(full_extras) == 20 and full["fullManagedMatrix"] is True and len(full["projects"]) == 27 and
            {row["name"] for row in full["projects"]} == set(full["requestedSuites"]), "The current 27-suite set differs")
    for row in full["projects"]:
        require(row["passed"] is True and row["buildExitCode"] == row["testExitCode"] == 0 and row["checks"] > 0,
                "A current managed suite did not actually pass")
        build_summary(e, paths["full"].parent / (row["name"] + "-build.log"))
        log = paths["full"].parent / (row["name"] + "-tests.log")
        e.check(log)
        require(log.read_text(encoding="utf-8-sig").splitlines()[-1].strip() == row["summary"] and
                f"{row['checks']}/{row['checks']}" in row["summary"], "Managed actual summary/count differs")
    require(sum(row["checks"] for row in full["projects"]) == 2465, "Current managed check count differs")
    release = m["release"]
    require(release["buildExitCode"] == 0 and release["sourceHashesUnchanged"] is True and len(release["files"]) == 6,
            "The six current Release assemblies did not build")
    require({Path(row["path"]).name for row in release["files"]} ==
            {"RizToolbox." + name + ".dll" for name in ("Sdk", "Core", "Runtime", "Adapter", "Hosting", "Loader")},
            "The exact six Release assembly identities differ")
    release_sources = [row for row in release["sources"] if Path(row["path"]).resolve().is_relative_to(core_root)]
    require(inventory(release_sources, core_root) == frozen, "Release source list differs from core-r4")
    release_log = build_summary(e, paths["release"].parent / "managed-build.log", 1)
    warnings = [line for line in release_log.splitlines() if re.search(r"\bwarning\s+[A-Z]+\d+:", line)]
    require(warnings and all("warning CS8669:" in line and "NativeEngineTransportProfile.Generated.cs" in line for line in warnings),
            "Release has an unexpected warning")
    for name in ("solution", "sdk"):
        reference(e, m[name]["foundationProof"], paths["full"], name + " full source proof")
    solution = m["solution"]
    require(solution["buildExitCode"] == solution["errors"] == solution["warnings"] == 0 and
            solution["sourceHashesUnchanged"] is True, "Actual solution build differs")
    build_summary(e, solution["log"]["path"], 0)
    sdk = m["sdk"]
    sdk_child = e.load_ref(sdk["result"], "SDK child result")
    require(sdk["runnerExitCode"] == 0 and sdk["checks"] == sdk_child["checksPassed"] == 14 and
            sdk_child["runtimeGameVerification"] is False, "Independent SDK distribution did not pass")
    sdk_log = paths["sdk"].parent / "sdk-distribution.log"
    e.check(sdk_log)
    require("SDK distribution checks: 14/14 passed." in sdk_log.read_text(encoding="utf-8-sig"), "SDK actual log differs")
    e.check(full_root / "artifacts/sdk/RizToolbox.Sdk.1.2.0.nupkg", sdk_child["packageSha256"])
    plugins = list(Path(sdk_child["mods"]).rglob("*.dll"))
    require(len(plugins) == 2, "SDK did not create exactly two independent plugin binaries")
    for path in Path(sdk_child["mods"]).rglob("*"):
        if path.is_file():
            e.check(path)
    bridge = m["bridge"]
    build_summary(e, paths["bridge"].parent / "build.log")
    require(bridge["buildExitCode"] == 0 and {row["mode"] for row in bridge["modes"]} ==
            {"normal", "callback-fault", "deferred-release", "withdraw-inflight"} and len(bridge["modes"]) == 4,
            "The bridge mode set differs")
    for row in bridge["modes"]:
        actual = last_json(row["log"]["path"])
        require(row["actualExitCode"] == 0 and actual["passed"] is True and actual["mode"] == row["mode"] and
                actual["checks"] == row["checks"] and actual["actualDobbyPrepares"] == actual["actualDobbyCommits"] == 8,
                "A bridge actual mode/exit/log differs")
        scope(actual, "bridge " + row["mode"])
    require(bridge["checks"] == sum(row["checks"] for row in bridge["modes"]) == 187, "Bridge totals differ")
    tramp = m["trampolines"]
    build_summary(e, paths["trampolines"].parent / "build.log")
    require(tramp["buildExitCode"] == 0 and len(tramp["modes"]) == 5 and
            {row["mode"] for row in tramp["modes"]} == {"normal", "seh", "dobby-tamper", "thunk-tamper", "unwind-tamper"},
            "The exact trampoline mode set differs")
    for row in tramp["modes"]:
        actual = last_json(row["log"]["path"])
        require(actual == row["actual"] and actual["mode"] == row["mode"] and row["actualExitCode"] == 0 and
                actual["passed"] is True and row["checks"] == actual["checks"] and
                actual["physicalTargets"] == actual["actualDobbyPrepare"] == actual["actualDobbyCommit"] == 8,
                "A trampoline actual mode/exit/log differs")
        scope(actual, "trampolines " + row["mode"])
    require(tramp["checks"] == sum(row["checks"] for row in tramp["modes"]) == 400, "Trampoline totals differ")
    for name, execution, pairs, documents in (("bridgeSymbols", "bridge", 5, 504), ("trampolineSymbols", "trampolines", 3, 183)):
        value = m[name]
        reference(e, value["executionProof"], paths[execution], name + " execution proof")
        require(value["pePdbPairs"] == len(value["images"]) == pairs and
                managed_symbols(value["images"], paths[execution].parent / "source", frozen, name) == value["sourceDocuments"] == documents,
                f"{name}: actual document scope differs")
    precap_summary = verify_pre_capture(e, m, paths, frozen, core_root)
    native, checks, symbols = (m[name] for name in ("nativeBuild", "nativeChecks", "nativeSymbols"))
    require(native["actualExitCode"] == 0 and native["inputsUnchanged"] is True and native["frozenSourcesUnchanged"] is True and
            len(native["translationUnits"]) == 5, "Actual native-r14 build is incomplete")
    native_modes = {"normal", "bits", "nested-aba", "original-seh", "inline-pause", "dormant-withdraw", "foreign-thread",
                    "observer-fault", "journal-overflow", "inflight", "after-inflight", "machine", "worker-withdraw", "target-specific"}
    require(checks["processes"] == len(checks["rows"]) == 14 and {row["mode"] for row in checks["rows"]} == native_modes and
            checks["inputsUnchanged"] is True and checks["frozenInputsUnchanged"] is True, "Native actual 14-process set differs")
    for row in checks["rows"]:
        log = paths["nativeChecks"].parent / (row["mode"] + ".stdout.log")
        actual = last_json(log)
        e.check(log)
        zero_exit(e, log.with_name(row["mode"] + ".exit.txt"))
        require(row["actualExitCode"] == 0 and row["passed"] is True and actual == row["result"] and
                actual["mode"] == row["mode"] and actual["passed"] is True and actual["failures"] == 0 and
                actual["actualDobbyPrepared"] == actual["actualDobbyCommitted"] == 8,
                "A native actual mode/exit/log differs")
        scope(actual, "native " + row["mode"])
    require(checks["checks"] == sum(row["result"]["checks"] for row in checks["rows"]) == 36675, "Native totals differ")
    require(symbols["abiVersion"] == 1 and symbols["frameSize"] == 160 and symbols["infoSize"] == 136 and
            len(symbols["images"]) == 3 and len(symbols["compiledObjects"]) == 5 and len(symbols["stockTargets"]) == 9 and
            all(row["matchesNativePdbIdentity"] is True for row in symbols["images"]), "Native actual PE/PDB/COFF/body scope differs")
    require({key(row["object"]) for row in symbols["compiledObjects"]} ==
            {key(row["object"]) for row in native["translationUnits"]} and
            len([row for row in checks["frozenInputs"] if Path(row["path"]).name == "build-result.json" and
                 row["sha256"] == sha(paths["nativeBuild"])]) == 1,
            "Native compiled-object/build generation differs from the actual runner input")
    for row in symbols["compiledObjects"]:
        require(row["sources"] and all(src["matchesActualCompiledObject"] is True for src in row["sources"]),
                "Native compiled-object source proof is incomplete")
        for src in row["sources"]:
            require(hashlib.new(src["algorithm"], Path(src["path"]).read_bytes()).hexdigest() == src["compiledChecksum"],
                    "Actual COFF source checksum differs")
    foundation = m["foundationNative"]
    require(foundation["buildExitCode"] == 0 and foundation["inputHashesUnchanged"] is True and
            foundation["publicationVersion"] == 7 and len(foundation["expectedExports"]) == foundation["helperExportCount"] == 85 and
            [foundation[name] for name in ("playbackAttestationVersion", "playbackAttestationHeaderBytes", "playbackAttestationRangeBytes", "playbackAttestationOriginalCount")] == [1, 120, 184, 28],
            "Private Foundation native attestation protocol differs")
    archive_matches(e, foundation["sourceArchive"], {row["relativePath"].casefold(): row["sha256"] for row in foundation["sources"]}, "Foundation compilation archive")
    e.load_ref(foundation["actualExportValidation"], "Foundation actual exports")
    fm = m["foundationMapped"]
    require(fm["buildExitCode"] == 0 and len(fm["modes"]) == 7, "Private Foundation mapped process set differs")
    for row in fm["modes"]:
        actual = last_json(paths["foundationMapped"].parent / (row["mode"] + ".stdout.log"))
        require(row["actualExitCode"] == 0 and actual == row["result"] and actual["passed"] is True and actual["failures"] == 0,
                "Private Foundation mapped actual mode/exit/log differs")
        scope(actual, "private Foundation " + row["mode"])
    require(sum(row["result"]["checks"] for row in fm["modes"]) == 2365, "Private Foundation mapped totals differ")
    for name, count in (("foundationMachine", 2226), ("foundationSeh", 17)):
        value = m[name]
        actual = last_json(paths[name].parent / "stdout.log")
        require(value["buildExitCode"] == value["actualExitCode"] == 0 and value["actualHelperObjectsLinked"] is True and
                actual == value["result"] and actual["passed"] is True and actual["checks"] == count and actual["failures"] == 0,
                f"{name}: actual result/log differs")
        scope(actual, name)
    fp = m["foundationProof"]
    require(len(fp["nativeImages"]) == 5 and len(fp["compiledObjects"]) == 31 and len(fp["reviewSources"]) == 115 and
            fp["actualExportCount"] == 85 and fp["allExportsLocalExecutableNonWritable"] is True and
            fp["actualExitCodesVerifiedFromRecordedResults"] is True and fp["stdoutMatchesRecordedResult"] is True and
            fp["coreR3ExecutionResultClaimed"] is False, "Private Foundation independent evidence review differs")
    for field, name in (("nativeManifest", "foundationNative"), ("mappedResult", "foundationMapped"),
                        ("machineResult", "foundationMachine"), ("relocatedResult", "foundationSeh")):
        reference(e, fp[field], paths[name], "Foundation " + field)
    for row in fp["coreR3SourceMatches"]:
        require(live.get(relative(row["path"], REPOSITORY)) == row["sha256"], "Relevant Foundation source differs from current core-r4")
    report = m["foundationReport"]
    require(report["checkedHashRows"] == 433, "Foundation report integrity scope differs")
    e.load_ref(report["index"], "Foundation sealed report index")
    protocol, parse = m["packageProtocol"], m["packageParse"]
    require(protocol["helperExportCount"] == len(protocol["actualExports"]) == 85 and
            set(protocol["actualExports"]) == set(foundation["expectedExports"]) and protocol["archivedCompilationSources"] == 101 and
            [protocol["playbackAttestation"][name] for name in ("playbackAttestationVersion", "playbackAttestationHeaderBytes", "playbackAttestationRangeBytes", "playbackAttestationOriginalCount")] == [1, 120, 184, 28],
            "Package reader protocol evidence differs")
    require(len(parse["files"]) == 4 and all(row["parseErrors"] == 0 for row in parse["files"]), "Actual package PowerShell parsing failed")
    native_facts(e, symbols, fp, protocol)

    def finish_preflight():
        e.check(plan_path, allow_worktree_archive=False)
        e.check(__file__, allow_worktree_archive=False)
        e.finish()
        print(json.dumps({"kind": "nine-source-integration-preflight", "sealWritten": False,
                          "missingTerminalResults": missing, "verifiedUniqueFiles": len(e.files),
                          "coreWorktreeInputs": len(live),
                          "sourceScope": "historical-pre-merge" if historical_worktree is not None else "current-worktree",
                          "currentIntegratedSourceAccepted": False,
                          "knownPreservedHistoricalClaims": e.historical_claims}, ensure_ascii=False))

    if "group" not in m:
        require(preflight, "No current terminal group result")
        finish_preflight()
        return
    group = m["group"]
    require(sha(paths["group"]) == GROUP_SHA, "The exact final core-r4 82-mode group proof changed")
    build_summary(e, paths["group"].parent / "build.log")
    child = group["child"]
    require(child == read(paths["group"].parent / "checks/test-result.json"),
            "The group wrapper differs from its actual independently recorded child result")
    roles = ("pitch", "pause", "replay", "resume", "schedule", "clip", "progress", "audiopause")
    expected_modes = {"precapture-pitch-uw", "precapture-replay-chain-uw", "precapture-gettime-uw", "normal-nine", "adopt-nine",
                      "stream-nine", "withdraw-blocked-nine", "native-thread-fault-nine", "callback-before-nine", "callback-after-nine", "reader-fault-nine", "seh-gettime"}
    expected_modes.update(f"{action}-{role}" for role in roles for action in ("seh", "withdraw"))
    expected_modes.update(f"tamper-{role}-{span}" for role in ("gettime",) + roles for span in ("patch", "tail", "original", "dobby", "unwind", "metadata"))
    require(group["buildExitCode"] == group["testExitCode"] == 0 and child["allPassed"] is True and child["failure"] is None and
            child["processes"] == len(child["rows"]) == len(expected_modes) == 82 and {row["mode"] for row in child["rows"]} == expected_modes and
            group["physicalMemberCount"] == 9 and group["sourceMemberCount"] == 8 and group["sharedSourceHubCount"] == 1,
            "The actual current nine-member/one-hub 82-mode group set differs")
    scope(child, "current group child")
    for row in child["rows"]:
        actual = last_json(row["stdout"]["path"])
        require(row["actualExitCode"] == 0 and actual["mode"] == row["mode"] and actual["passed"] is True and
                actual["checks"] == row["checks"] and all(row.get(k) == v for k, v in actual.items()),
                "A group actual mode/exit/log differs")
        zero_exit(e, Path(row["stdout"]["path"]).with_name(row["mode"] + ".exit.txt"))
        scope(actual, "group " + row["mode"])
        if row["mode"].startswith("precapture-"):
            require(actual["preCaptureRejected"] is True and actual["actualGroupArmed"] is False and
                    actual["actualDobbyPrepareCount"] == actual["actualDobbyCommitCount"] == 0 and actual["nativeEntriesInvoked"] is False,
                    "Current pre-capture refusal must precede every Prepare/Commit")
        else:
            require(actual["actualDobbyPrepareCount"] == actual["actualDobbyCommitCount"] == actual["physicalMemberCount"] == 9 and
                    actual["sourceMemberCount"] == 8 and actual["sharedSourceHubCount"] == 1, "Group lost physical nine-member ownership")
    require(child["actualDobbyPrepareCalls"] == child["actualDobbyCommitCalls"] == 79 * 9 and
            child["checks"] == sum(row["checks"] for row in child["rows"]) == 3404 and child["actualPrivateBrokerAdoptionProcesses"] == 1,
            "Actual group process totals or private broker adoption differ")
    group_documents = managed_symbols(group["managedSymbols"], paths["group"].parent / "source", frozen, "Current group symbols")
    require(len(group["managedSymbols"]) == 5 and group_documents == 456,
            "Current group has no complete actual five PE/PDB/456-document proof")
    if "mapped" not in m:
        require(preflight, "No current terminal mapped result")
        finish_preflight()
        return
    mapped = m["mapped"]
    resumed_root = BASE / "playback-stock-nine-joint-resume-r1-20261031"
    require(Path(mapped["sourceDirectory"]).resolve() == (resumed_root / "source").resolve()
            and mapped["reusedManagedImagesByteIdentical"] is True and mapped["resumedAfterMissingHandle"] is True
            and mapped["priorSuccessfulModesReused"] == ["normal", "pitch-tail", "body", "consumer"]
            and set(mapped["remainingModesActuallyRun"]) == {"foundation-normal", "foundation-outside", "foundation-unwind"},
            "Mapped continuation must preserve the four completed modes and resume exactly three actual missing modes")
    build_summary(e, mapped["buildLog"]["path"])
    zero_exit(e, mapped["buildExit"]["path"])
    original_symbols_path = BASE / "playback-stock-nine-joint-r2-20261031/managed-symbol-source-proof.json"
    original_symbols = read(original_symbols_path)
    e.check(original_symbols_path)
    original_images = {Path(row["assembly"]["path"]).name: row["assembly"] for row in original_symbols}
    require(len(original_images) == 5, "Interrupted batch has no exact original managed image set")
    for image in mapped["managedSymbols"]:
        current = image["assembly"]
        old = original_images.get(Path(current["path"]).name)
        require(old is not None and current["sha256"] == old["sha256"], "Reused mapped modes executed another managed binary")
        e.check(old["path"], old["sha256"])
    reference(e, mapped["foundationProof"], paths["foundationNative"], "Mapped actual Foundation membership")
    mapped_modes = {"normal", "pitch-tail", "body", "consumer", "foundation-normal", "foundation-outside", "foundation-unwind"}
    require(mapped["buildExitCode"] == 0 and mapped["frozenInputsUnchanged"] is True and
            len(mapped["modes"]) == 7 and {row["mode"] for row in mapped["modes"]} == mapped_modes and
            mapped["original44Repeated"] is False and mapped["metadataSynthetic"] is True and mapped["graphSynthetic"] is True,
            "The mapped nine-member joint proof has the wrong scope")
    for row in mapped["modes"]:
        actual = last_json(row["log"]["path"])
        expected_checks = {"normal": 28, "foundation-normal": 32, "foundation-outside": 35,
                           "foundation-unwind": 35}.get(row["mode"], 32)
        require(row["actualExitCode"] == 0 and actual == row["actual"] and actual["mode"] == row["mode"] and
                actual["passed"] is True and actual["checks"] == row["checks"] == expected_checks and
                actual["actualDobbyPrepareCount"] == actual["actualDobbyCommitCount"] == actual["physicalMemberCount"] == 9 and
                [actual[x] for x in ("initialRevision", "commitRevision", "restoredRevision")] == [1, 2, 3] and
                all(actual[x] == 0 for x in ("getTimeOriginalCalls", "sourceEntered", "sourceExited", "sourceBefore", "sourceAfter")) and
                all(actual[x] is False for x in ("nativeAuthorityActiveAfterTermination", "groupActiveAfterTermination", "getTimeActiveAfterTermination", "pitchActiveAfterTermination")),
                "Mapped actual mode/exit/log, restoration or zero-game-call proof differs")
        scope(actual, "mapped " + row["mode"])
        expected_parent = (BASE / "playback-stock-nine-joint-r2-20261031") if row["mode"] in mapped["priorSuccessfulModesReused"] else resumed_root
        require(Path(row["exit"]["path"]).resolve() == (expected_parent / (row["mode"] + ".exit.txt")).resolve(),
                "A mapped mode refers to another execution's exit sidecar")
        zero_exit(e, row["exit"]["path"])
        if row["mode"].startswith("foundation-"):
            require([actual[x] for x in ("foundationEpoch", "foundationOriginalCount", "foundationWriteCount")] == [1, 28, 64],
                    "Mapped group lost its actual private Foundation member")
    require(mapped["checks"] == sum(row["checks"] for row in mapped["modes"]) == 226, "Mapped process totals differ")
    mapped_documents = managed_symbols(mapped["managedSymbols"], resumed_root / "source", frozen, "Mapped symbols")
    require(len(mapped["managedSymbols"]) == 5, "Mapped proof lacks five actual PE/PDB pairs")
    native_files = {Path(row["path"]).name.casefold(): row["sha256"] for row in native["files"]}
    common_helpers = {}
    for name in ("bridge", "trampolines", "group", "mapped"):
        rows = m[name].get("nativeInputs", []) + m[name].get("inputs", [])
        found = set()
        for row in rows:
            basename = Path(row["path"]).name.casefold()
            if basename in ("riztoolbox.playbackstockfixture.dll", "riztoolbox.playbacksourceprototype.dll", "riztoolbox.singlereturn.prototype.dll", "dobby.dll"):
                found.add(basename)
                if basename in native_files:
                    require(row["sha256"] == native_files[basename], f"{name}: wrong native-r14 helper/fixture")
                if basename in common_helpers:
                    require(row["sha256"] == common_helpers[basename], f"{name}: mixed native package generations")
                common_helpers[basename] = row["sha256"]
        require({"riztoolbox.playbacksourceprototype.dll", "dobby.dll"}.issubset(found), f"{name}: missing actual source helper input identity")
    if preflight:
        finish_preflight()
        return
    e.check(plan_path, allow_worktree_archive=False)
    e.check(__file__, allow_worktree_archive=False)
    e.finish()
    summary = {
        "kind": "frozen-nine-source-integration" if historical_worktree is not None else "current-nine-source-integration", "passed": True, "flavor": None,
        "sourceScope": "historical-pre-merge" if historical_worktree is not None else "current-worktree",
        "currentIntegratedSourceAccepted": False,
        "historicalWorktreeManifest": None if historical_worktree is None else {"path": str(historical_worktree), "sha256": sha(historical_worktree)},
        "observedAtUtc": datetime.now(timezone.utc).isoformat(), "sourceProofSha256": CORE_SHA,
        "coreInputs": 727, "currentWorktreeInputs": 0 if historical_worktree is not None else 727,
        "archivedWorktreeInputs": 727 if historical_worktree is not None else 0, "additionalFullWorktreeInputs": len(full_live_extras),
        "additionalFullCopiedWorktreeInputs": len(full_extras), "additionalFullRunnerInputs": 1,
        "transformedRunnerScripts": [{"relativePath": harness, "worktreeSha256": live[harness],
                                      "snapshotSha256": frozen[harness], "reason": core["runnerTransformation"]}],
        "verifiedWorktreeFilesAllScopes": sum(Path(fact["path"]).is_relative_to(REPOSITORY) for fact in e.files.values()),
        "managedSuites": 27, "managedChecks": 2465, "releaseAssemblies": 6, "releaseErrors": 0, "releaseKnownNullableWarnings": 1,
        "solutionErrors": 0, "solutionWarnings": 0, "sdkChecks": 14, "sdkIndependentPlugins": 2,
        "sourceBridgeProcesses": 4, "sourceBridgeChecks": 187, "sourceBridgePePdbPairs": 5, "sourceBridgeDocuments": 504,
        "sourceTrampolineProcesses": 5, "sourceTrampolineChecks": 400, "sourceTrampolinePePdbPairs": 3, "sourceTrampolineDocuments": 183,
        "groupProcesses": 82, "groupChecks": child["checks"], "groupPePdbPairs": 5, "groupDocuments": group_documents,
        "groupPreCaptureProcesses": 3, "groupPreCapturePrepare": 0, "groupPreCaptureCommit": 0,
        "groupOtherProcesses": 79, "groupOtherPrepare": 711, "groupOtherCommit": 711,
        "physicalMemberCount": 9, "sourceMemberCount": 8, "sharedSourceHubCount": 1,
        "mappedProcesses": 7, "mappedChecks": mapped["checks"], "mappedPePdbPairs": 5, "mappedDocuments": mapped_documents,
        "nativeProcesses": 14, "nativeChecks": 36675, "nativePePdbPairs": 3, "nativeCoffObjects": 5, "nativeStockBodies": 9,
        "foundationPrivate": {"helperExports": 85, "version": 1, "headerBytes": 120, "rangeBytes": 184, "originalCount": 28,
                              "mappedProcesses": 7, "mappedChecks": 2365, "machineChecks": 2226, "relocatedSehChecks": 17,
                              "pePdbPairs": 5, "coffObjects": 31, "reviewSources": 115, "reportIntegrityHashRows": 433,
                              "coreR3ExecutionClaimPromoted": False, "productionFirstWriteAdmission": False},
        "exactOldPocReproduction": precap_summary,
        "packageProtocol": {"helperExports": 85, "archivedCompilationSources": 101, "version": 1, "headerBytes": 120,
                            "rangeBytes": 184, "originalCount": 28, "powershellFilesParsed": 4, "parseErrors": 0,
                            "completePackageVerified": False, "candidatePacked": False, "candidateInstalled": False},
        "verifiedUniqueFiles": len(e.files), "verifiedHashReferences": e.references,
        "countsHaveDistinctScopes": True, "formalSourceOverlays": 0, "exactPocProgramOverlays": 1,
        "foundationCompositionAvailable": True, "completeStockProviderImplemented": False, "crossRunHandoffImplemented": False,
        "gameExecuted": False, "gameDllMainExecuted": False, "installed": False, "productionAdmissionVerified": False,
        "realSpeedSeekEnabled": False, "fullB01B19Completed": False,
    }
    require(output is not None, "A fresh output directory is required for a completed seal")
    artifacts = [{"id": "E-" + name.upper(), "path": str(path), "sha256": sha(path),
                  "role": "preserved-historical-not-current-passing-matrix" if name in historical else
                          "separate-exact-old-poc-program-overlay" if name == "precapReview" else "current-declared-scope"}
                 for name, path in paths.items()]
    index = {"schemaVersion": 1, "kind": "nine-source-integration-evidence", "flavor": None,
             "artifacts": artifacts, "verifiedFiles": sorted(e.files.values(), key=lambda fact: fact["path"].casefold()),
             "preservedSupersededHistoricalClaims": e.historical_claims, "countsHaveDistinctScopes": True,
             "indexSelfHashPolicy": "Excluded from its own verifiedFiles; the workspace index records the fresh external seal files.",
             "summary": summary}
    output.mkdir()
    write(output / "test-result.json", summary)
    write(output / "evidence-index.json", index)
    if index_path is not None:
        workspace_index = dict(index)
        workspace_index["sealResult"] = {"path": str(output / "test-result.json"), "sha256": sha(output / "test-result.json")}
        workspace_index["sealEvidenceIndex"] = {"path": str(output / "evidence-index.json"), "sha256": sha(output / "evidence-index.json")}
        write(index_path, workspace_index)
    print(json.dumps(summary, ensure_ascii=False))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--plan", type=Path, required=True)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--index", type=Path)
    parser.add_argument("--preflight", action="store_true", help="Read-only partial validation; never creates a seal or index.")
    parser.add_argument("--historical-worktree", type=Path,
                        help="Exact pre-merge archive; validates core-r4 only and never accepts the newer integrated source.")
    args = parser.parse_args()
    run(args.plan, args.output, args.index, args.preflight, args.historical_worktree)
