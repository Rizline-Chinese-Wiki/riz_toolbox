"""Read the actual current helper EAT, independently of dumpbin text."""
import argparse
import hashlib
import json
from pathlib import Path

import pefile


def verify(image_path, expected):
    data = image_path.read_bytes()
    pe = pefile.PE(data=data, fast_load=True)
    pe.parse_data_directories(directories=[0])
    if pe.FILE_HEADER.Machine != 0x8664 or pe.OPTIONAL_HEADER.Magic != 0x20B:
        raise ValueError("The current helper must be native AMD64 PE32+")
    exports = pe.DIRECTORY_ENTRY_EXPORT
    symbols = list(exports.symbols)
    names = [entry.name.decode("ascii") if entry.name else None for entry in symbols]
    if (not all(names) or len(set(names)) != len(names) or set(names) != set(expected)
            or len(names) != len(expected) or exports.struct.NumberOfFunctions != len(names)):
        raise ValueError("The complete actual helper EAT differs from the declared current exports")
    if set(name for name in names if name.startswith("RizFirstInstall_GetInfo")) != {"RizFirstInstall_GetInfoV7"}:
        raise ValueError("Only the current V7 first-install publication is allowed")
    gate_exports = {"RizTweenAdmission_Configure", "RizTweenAdmission_Claim", "RizTweenAdmission_Read",
                    "RizTweenAdmission_Seal", "RizTweenAdmission_Unregister", "RizTweenAdmission_GetInfo"}
    if {name for name in names if name.startswith("RizTweenAdmission_")} != gate_exports:
        raise ValueError("The dedicated Tween admission surface is incomplete or contains a fixture bypass")
    if {name for name in names if name.startswith("RizTweenSourceContext_")} != {
        "RizTweenSourceContext_GetEntry", "RizTweenSourceContext_GetInfo", "RizTweenSourceContext_ReadRecord"}:
        raise ValueError("The Tween source surface is incomplete or exports a configuration bypass")
    if not {"RizFirstInstall_GetTweenSourceInfoV1", "RizFirstInstall_BindTweenSourceMetadata"}.issubset(names):
        raise ValueError("Actual source publication and post-init metadata binding are required")
    if {name for name in names if name.startswith("RizTweenSourceReferences_")} != {
        "RizTweenSourceReferences_GetInfo", "RizTweenSourceReferences_ReadCurrentNotice"}:
        raise ValueError("The synchronous source reference surface is incomplete or exports a private attachment bypass")
    if {name for name in names if name.startswith("RizTweenRetirement_")} != {
        "RizTweenRetirement_Acquire", "RizTweenRetirement_Verify", "RizTweenRetirement_VerifyReferenceRoots",
        "RizTweenRetirement_UnregisterBatch", "RizTweenRetirement_Release", "RizTweenRetirement_GetInfo"}:
        raise ValueError("The checked Tween retirement surface is incomplete or contains a fixture bypass")
    if "RizFirstInstall_AttachTweenSourceReferencesV1" not in names:
        raise ValueError("Actual FirstInstall readiness must gate the once-only source reference observer")
    if {name for name in names if name.startswith("RizTweenSingleEventContext_")} != {
        "RizTweenSingleEventContext_GetMidEntry", "RizTweenSingleEventContext_GetPreEntry", "RizTweenSingleEventContext_GetPostEntry",
        "RizTweenSingleEventContext_Attach", "RizTweenSingleEventContext_GetInfo", "RizTweenSingleEventContext_ReadCurrent"} or len(names) != 88:
        raise ValueError("The current attested V7 helper must contain six Single entries/readers and exactly 88 total exports")
    if "RizJudgeContext_ReadConfigurationV1" not in names:
        raise ValueError("The current Judge owner requires its read-only actual native configuration reader")
    if "RizCoroutineContext_ReadConfigurationV1" not in names:
        raise ValueError("The current Coroutine owner requires its read-only actual native configuration reader")
    if {name for name in names if "PlaybackAttest" in name} != {
        "RizFirstInstall_GetPlaybackAttestationV1", "RizFirstInstall_ReadPlaybackAttestedRangeV1",
        "RizFirstInstall_ReadPlaybackAttestedRangesV1"}:
        raise ValueError("The private FirstInstall attestation must expose only its current read/verify operations")
    for entry in symbols:
        if entry.forwarder or not any(section.VirtualAddress <= entry.address < section.VirtualAddress + section.Misc_VirtualSize
                                      and section.Characteristics & 0xE0000000 == 0x60000000 for section in pe.sections):
            raise ValueError("Every helper export must point to actual local RX image code")
    return dict(schemaVersion=1, kind="judge-context-actual-export-validation", passed=True,
                helper=dict(path=str(image_path.resolve()), sha256=hashlib.sha256(data).hexdigest()),
                exports=sorted(names), publicationVersion=7, operationCount=26, expectedWrites=64, tweenSourceCount=22,
                playbackAttestationVersion=1, playbackAttestationHeaderBytes=120, playbackAttestationRangeBytes=184,
                playbackAttestedOriginalCount=28, playbackAttestedWriteCount=64, helperExportCount=88,
                gameExecuted=False, installed=False)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--image", type=Path, required=True)
    parser.add_argument("--expected-json", type=Path, required=True)
    parser.add_argument("--output-json", type=Path, required=True)
    args = parser.parse_args()
    if args.output_json.exists():
        raise SystemExit("Preserve earlier validations; use a fresh output")
    result = verify(args.image, json.loads(args.expected_json.read_text(encoding="utf-8-sig")))
    args.output_json.write_text(json.dumps(result, indent=2), encoding="utf-8")
    print("Actual helper PE exports verified; V7 is the only first-install reader.")
