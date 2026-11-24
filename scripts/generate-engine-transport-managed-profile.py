"""Freeze independent five-image facts for the real owned Native-to-CLR test.

Reads final candidate files and build manifests only. It never loads a PE, runs
an executable, changes a production profile, or grants game startup admission.
The parent must pass the SHA256 of this new JSON to the unmanaged CLR entry.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import re
import struct
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
HELPER = REPO / "scripts/generate-engine-transport-profile.py"
MODULES = ("RizToolbox.EngineBootstrap.dll", "RizToolbox.EngineNodeKernel.dll",
           "RizToolbox.TransportOwnedTarget.dll", "RizTransportOwnedHost.exe", "winhttp.dll")
FIXED_RVAS = (0x792240, 0x78F840, 0x78F680, 0x793840, 0x44D630, 0x447AD0,
              0x447C30, 0x447D60, 0x447750, 0x78F5E0, 0x78FC50, 0x5A5260, 0x5DA510)


def require(value: bool, reason: str) -> None:
    if not value:
        raise ValueError(reason)


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def read_file(path: Path) -> bytes:
    require(path.is_absolute() and path.is_file() and not path.is_symlink(), f"Ordinary absolute file required: {path}")
    return path.read_bytes()


def digest_row(row: dict, frozen: dict[str, tuple[Path, bytes]]) -> tuple[Path, bytes]:
    path = Path(row["path"])
    data = read_file(path)
    require(bool(re.fullmatch(r"[0-9a-f]{64}", row["sha256"])) and sha(data) == row["sha256"], f"Manifest digest differs: {path}")
    if "bytes" in row:
        require(len(data) == row["bytes"], f"Manifest file size differs: {path}")
    key = str(path.resolve(strict=True)).casefold()
    if key in frozen:
        require(frozen[key][1] == data, f"Input identity changed: {path}")
    else:
        frozen[key] = (path, data)
    return path, data


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--candidate-directory", type=Path, required=True)
    parser.add_argument("--output-json", type=Path, required=True)
    args = parser.parse_args()
    candidate = args.candidate_directory
    require(candidate.is_absolute() and candidate.is_dir() and not candidate.is_symlink(), "Absolute ordinary candidate directory required.")
    candidate = candidate.resolve(strict=True)
    output = args.output_json
    require(output.is_absolute() and not output.exists(), "Preserve previous output; use a fresh absolute JSON path.")
    output = output.resolve()
    game = Path("D:/SteamLibrary/steamapps/common/Rizline").resolve()
    require(output != game and game not in output.parents and candidate != game and game not in candidate.parents,
            "Owned facts and outputs must remain outside the game installation.")
    frozen: dict[str, tuple[Path, bytes]] = {}
    generator = Path(__file__).resolve()
    for path in (generator, HELPER):
        frozen[str(path).casefold()] = (path, read_file(path))
    manifest_path = candidate / "owned-candidate-build.json"
    manifest_bytes = read_file(manifest_path)
    frozen[str(manifest_path).casefold()] = (manifest_path, manifest_bytes)
    manifest = json.loads(manifest_bytes)
    require(manifest["schemaVersion"] == 1 and manifest["kind"] == "engine-transport-owned-candidate-build"
            and manifest["passed"] is True and manifest["managedHost"] is True
            and manifest["buildExitCode"] == 0 and manifest["sourceHashesUnchanged"] is True,
            "A successful final managed-host candidate build manifest is required.")
    require(all(manifest[name] is False for name in ("fixtureExecuted", "gameExecuted", "installed", "productionAdmissionVerified")),
            "A candidate build cannot claim execution or production admission.")
    files: dict[str, tuple[Path, bytes]] = {}
    for row in manifest["files"]:
        path, data = digest_row(row, frozen)
        require(path.parent.resolve(strict=True) == candidate and path.name.casefold() not in files,
                "Candidate manifest files must be unique direct children of the frozen build directory.")
        files[path.name.casefold()] = (path, data)
    for row in manifest["inputs"]:
        digest_row(row, frozen)
    require(all(name.casefold() in files for name in (*MODULES, "native-profile.json", "EngineTransportProfile.Generated.h")),
            "Final five linked PE files and native profile provenance are required.")
    profile_path, profile_bytes = files["native-profile.json"]
    profile = json.loads(profile_bytes)
    require(profile["schemaVersion"] == 1 and profile["kind"] == "engine-transport-native-compile-facts"
            and profile["profile"] == "owned" and profile["nativeProfile"] == 1 and profile["sourceHashesUnchanged"] is True,
            "An independently generated owned native compile profile is required.")
    for row in profile["sources"]:
        digest_row(row, frozen)
    _, header = digest_row(profile["generatedHeader"], frozen)
    require(header == files["enginetransportprofile.generated.h"][1], "Native compile header differs from final candidate manifest.")
    spec = importlib.util.spec_from_file_location("riz_transport_profile_reader", HELPER)
    require(spec is not None and spec.loader is not None, "Native disk-facts reader is unavailable.")
    helper = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(helper)
    source = helper.facts()
    images = [helper.Image(candidate / name) for name in MODULES]
    require(all(image.record["delayImportRva"] == image.record["delayImportBytes"] == 0 for image in images), "Delay imports cannot supplement the frozen ordinary graph.")
    require(all(bool(image.pe.FILE_HEADER.Characteristics & 0x2000) == (index != 3) for index, image in enumerate(images)), "Final EXE/DLL roles differ.")
    compiled = profile["images"]
    require(len(compiled) == 5 and compiled[0]["module"] == MODULES[0] and compiled[0]["hashPolicy"] == 1
            and compiled[0]["sha256"] == "00" * 32, "Bootstrap self observation must be replaced by its independently frozen final hash.")
    for index, image in enumerate(images):
        require(image.data == files[MODULES[index].casefold()][1], "Final PE bytes differ from candidate manifest.")
        if index:
            require(image.record == compiled[index], f"Final image differs from native compile identity: {image.path}")
    bootstrap_exports = images[0].exports()
    require(len(bootstrap_exports) == 4 and {entry.name.decode("ascii") if entry.name else None for entry in bootstrap_exports} == set(helper.BOOTSTRAP_NAMES), "Bootstrap requires exactly four direct V5 exports.")
    require(len(images[1].exports()) == 35 and {entry.name.decode("ascii") if entry.name else None for entry in images[1].exports()} == set(helper.KERNEL_NAMES), "Kernel requires exactly thirty-five exports.")
    # The reader represents RF triples as tuples; manifests store JSON arrays.
    target = json.loads(json.dumps(helper.target_facts(images[2], source, True)))
    require(target == profile["targetFacts"], "Final target's thirteen bodies, twenty-seven unwind records, marker or owned loader stubs differ from native compile facts.")
    require(tuple(row["beginRva"] for row in target["functions"]) == FIXED_RVAS, "Fixed thirteen function RVA order differs.")
    # The generated native profile intentionally had zero self RVAs before link.
    # Resolve all forty-four ordinary ILT/IAT/EAT tuples from the final five PEs.
    edges = []
    groups = ((3, 2, ("UnityMain2",)), (2, 4, helper.WINHTTP),
              (4, 0, helper.BOOTSTRAP_NAMES), (0, 1, helper.KERNEL_NAMES))
    for src, dst, symbols in groups:
        imports = images[src].imported(MODULES[dst], symbols)
        for symbol in symbols:
            lookup, iat = imports[symbol]
            edges.append(dict(index=len(edges), source=src, destination=dst, symbol=symbol,
                              lookupRva=lookup, iatRva=iat, exportRva=images[dst].exported(symbol)))
    require(len(edges) == len(profile["edges"]) == 44, "Complete final ordinary graph requires forty-four edges.")
    for actual, prior in zip(edges, profile["edges"]):
        require(all(actual[key] == prior[key] for key in ("index", "source", "destination", "symbol")), "Native graph membership/order changed.")
        for key in ("lookupRva", "iatRva", "exportRva"):
            require(prior[key] == 0 or prior[key] == actual[key], "A native non-self compile edge changed after final link.")
    payload = b"".join(struct.pack("<IIIII", *(row[key] for key in ("source", "destination", "lookupRva", "iatRva", "exportRva")))
                       + row["symbol"].encode("ascii") + b"\0" for row in edges)
    # The target build manifest independently excludes original game loader code.
    owned_manifests = []
    for row in manifest["inputs"]:
        path = Path(row["path"])
        if path.name == "owned-target-image.json":
            owned = json.loads(read_file(path))
            require(owned["kind"] == "engine-transport-owned-target-image" and owned["passed"] is True
                    and owned["inputHashesUnchanged"] is True and owned["loaderEntrypointsOwned"] is True
                    and owned["originalCodeOutsideSelectedBodiesRemoved"] is True
                    and owned["unlistedRawBytesVerified"] is True
                    and owned["gameDiskPatched"] is False and owned["gameExecuted"] is False
                    and owned["output"]["sha256"] == images[2].record["sha256"], "Owned target source manifest is incomplete or differs from the final target.")
            owned_manifests.append(dict(path=str(path), sha256=row["sha256"]))
    require(len(owned_manifests) == 1, "Exactly one independent owned target generation manifest is required.")
    for image in images:
        image.unchanged()
    for path, data in frozen.values():
        require(read_file(path) == data, f"A frozen input changed during generation: {path}")
    result = dict(schemaVersion=1, kind="engine-transport-owned-managed-build-facts", nativeProfile=1,
                  candidateDirectory=str(candidate), candidateManifest=dict(path=str(manifest_path), sha256=sha(manifest_bytes)),
                  nativeProfileManifest=dict(path=str(profile_path), sha256=sha(profile_bytes)), ownedTargetManifest=owned_manifests[0],
                  images=[dict(index=index, **image.record) for index, image in enumerate(images)],
                  functionRvas=list(FIXED_RVAS), functions=target["functions"], fixedContractSha256=target["fixedContractSha256"],
                  ownedMarkerRva=target["ownedMarkerRva"], ownedMarkerHex=target["ownedMarkerHex"],
                  edges=edges, dependencyGraphSha256=sha(payload), sourceHashesUnchanged=True,
                  generator=dict(path=str(generator), sha256=sha(frozen[str(generator).casefold()][1])),
                  productionAdmissionGranted=False, gameExecuted=False, installed=False)
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("xb") as stream:
        stream.write((json.dumps(result, indent=2, ensure_ascii=False) + "\n").encode("utf-8"))
    print(json.dumps(dict(kind=result["kind"], output=str(output), sha256=sha(output.read_bytes()), images=5,
                          functions=13, edges=44, productionAdmissionGranted=False)))


if __name__ == "__main__":
    main()
