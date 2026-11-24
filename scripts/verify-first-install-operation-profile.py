"""Independently compare the latest operation profile with the fixed PE.

Reads files only. A matching profile is not startup admission or evidence that
the mapped game functions have executed. Use a fresh evidence path each run.
"""
import argparse
import hashlib
import json
import re
import struct
from pathlib import Path

import capstone
import pefile


def digest(value):
    return hashlib.sha256(value).hexdigest()


def verify(game_path, profile_path):
    game = game_path.read_bytes()
    if digest(game) != "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79":
        raise ValueError("Unsupported GameAssembly image")
    profile_bytes = profile_path.read_bytes()
    profile = profile_bytes.decode("utf-8-sig")
    companion_path = profile_path.with_name("FirstInstallTweenProfile.h")
    companion_bytes = companion_path.read_bytes()
    if '#include "FirstInstallTweenProfile.h"' not in profile:
        raise ValueError("The current profile must compile its declared Tween companion")
    arrays_source = profile + "\n" + companion_bytes.decode("utf-8-sig")
    pe = pefile.PE(data=game, fast_load=True)
    if pe.FILE_HEADER.Machine != 0x8664 or pe.OPTIONAL_HEADER.Magic != 0x20B:
        raise ValueError("The fixed image is not AMD64 PE32+")
    count_match = re.search(r"OperationCount\s*=\s*(\d+),\s*ExpectedWrites\s*=\s*(\d+)", profile)
    if not count_match or tuple(map(int, count_match.groups())) != (26, 64):
        raise ValueError("The latest operation profile must declare 26 operations and 64 writes")
    arrays = {}
    for name, values in re.findall(r"unsigned char\s+(Operation\d+(?:Continuation\d+)?(?:Bytes|Unwind))\[\]\s*\{([^}]*)\}", arrays_source):
        if name in arrays:
            raise ValueError("Duplicate operation byte array")
        arrays[name] = bytes(int(value.strip(), 0) for value in values.split(",") if value.strip())
    row_pattern = r"\{\s*(\d+),\s*(\d+),\s*(\d+),\s*(0x[0-9a-fA-F]+),\s*(0x[0-9a-fA-F]+),\s*(0x[0-9a-fA-F]+),\s*(?:RizFirstInstallTweenProfile::)?Operation(\d+)Bytes,\s*sizeof\((?:RizFirstInstallTweenProfile::)?Operation\7Bytes\),\s*(?:RizFirstInstallTweenProfile::)?Operation\7Unwind,\s*sizeof\((?:RizFirstInstallTweenProfile::)?Operation\7Unwind\)\s*\}"
    rows = [tuple(int(value, 0) for value in match) for match in re.findall(row_pattern, profile)]
    if len(rows) != 26 or [row[0] for row in rows] != list(range(26)):
        raise ValueError("The latest profile lacks the complete ordered operation table")
    expected_rvas = (0x15640A0, 0x15630E0, 0x1563C50, 0x1563B80, 0x15631A0, 0x1563B40,
                     0x154DED0, 0x154DF90, 0x154E280, 0x154E110, 0x154E050, 0x1550EE0,
                     0x154E890, 0x154E930, 0x153D900, 0x153D7F0, 0x153D6C0, 0x153D750,
                     0x153D700, 0x153D7A0, 0x3956F0, 0x396BE0, 0x396E40, 0x398D60, 0x396CF0, 0xBC0FF0)
    expected_kinds = (1, 1, 2, 0, 0, 0, 1, 1, 2, 0, 0, 2, 1, 1, 1, 1, 0, 1, 1, 1, 0, 1, 1, 0, 0, 3)
    exception = pe.OPTIONAL_HEADER.DATA_DIRECTORY[3]
    if not exception.VirtualAddress or exception.Size % 12:
        raise ValueError("No complete static runtime-function table")
    pdata = pe.get_data(exception.VirtualAddress, exception.Size)
    runtime_functions = {}
    for offset in range(0, len(pdata), 12):
        function = struct.unpack_from("<III", pdata, offset)
        runtime_functions.setdefault(function[0], []).append(function)
    decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
    slots = [31, 31, 31, 31]
    facts = []
    for identity, kind, slot, begin, end, unwind, array_id in rows:
        if (begin, kind, slot, array_id) != (expected_rvas[identity], expected_kinds[identity], slots[kind], identity):
            raise ValueError(f"Operation {identity} does not have its exact latest RVA/kind/slot")
        slots[kind] -= 1
        if runtime_functions.get(begin) != [(begin, end, unwind)]:
            raise ValueError(f"Operation {identity} lacks its unique complete actual static RF")
        observed_bytes = arrays[f"Operation{identity}Bytes"]
        body_bytes = end - begin
        # The existing preparation contract requires a 32-byte probe even for
        # the one 31-byte leaf. Its extra byte must be real int3 padding, not an
        # invented extension of the function or an unchecked following body.
        if len(observed_bytes) != max(32, body_bytes) or pe.get_data(begin, len(observed_bytes)) != observed_bytes:
            raise ValueError(f"Operation {identity} complete declared observation differs from the real image")
        if observed_bytes[body_bytes:] != b"\xcc" * (len(observed_bytes) - body_bytes):
            raise ValueError(f"Operation {identity} observation extends into non-padding bytes")
        body = observed_bytes[:body_bytes]
        unwind_bytes = arrays[f"Operation{identity}Unwind"]
        if pe.get_data(begin, len(body)) != body:
            raise ValueError(f"Operation {identity} full body differs from the real image")
        if pe.get_data(unwind, len(unwind_bytes)) != unwind_bytes or len(unwind_bytes) < 4:
            raise ValueError(f"Operation {identity} unwind bytes differ from the real image")
        flags = unwind_bytes[0] >> 3
        expected_size = (4 + unwind_bytes[2] * 2 + 3) & ~3
        expected_size += 12 if flags & 4 else 4 if flags & 3 else 0
        language_tail = len(unwind_bytes) - expected_size
        if ((unwind_bytes[0] & 7) != 1 or flags & ~7 or (flags & 4 and flags & 3)
                or language_tail not in ((0, 4) if flags & 3 else (0,))):
            raise ValueError(f"Operation {identity} complete unwind encoding differs")
        if flags & 3:
            handler = struct.unpack_from("<I", unwind_bytes, expected_size - 4)[0]
            if not any(section.VirtualAddress <= handler < section.VirtualAddress + max(section.Misc_VirtualSize, section.SizeOfRawData)
                       and section.Characteristics & 0x20000000 for section in pe.sections):
                raise ValueError(f"Operation {identity} unwind handler leaves executable image sections")
        instructions = list(decoder.disasm(body, begin))
        decoded_bytes = sum(instruction.size for instruction in instructions)
        if (identity < 20 and decoded_bytes != len(body)) or decoded_bytes < min(32, len(body)):
            raise ValueError(f"Operation {identity} has undecoded instruction bytes")
        # Full Tween RF byte extents contain inline switch tables. Their bytes
        # are all compared and hashed; linear decoding is only an entry diagnostic.
        facts.append(dict(id=identity, kind=kind, slot=slot, rva=begin, endRva=end,
                          unwindRva=unwind, bodyBytes=len(body), bodySha256=digest(body),
                          profileObservationBytes=len(observed_bytes), followingInt3Bytes=len(observed_bytes)-len(body),
                          unwindBytes=len(unwind_bytes), unwindSha256=digest(unwind_bytes),
                          copiedLanguageHeaderBytes=language_tail, leadingLinearInstructions=len(instructions),
                          leadingLinearBytes=decoded_bytes, remainingBytes=len(body) - decoded_bytes))
    # The Single Invoke primary RF ends before its listener dispatch loop. All
    # three adjacent chained RFs must be compiled into the immutable profile;
    # checking only the 122-byte entry cannot establish the function identity.
    continuation_pattern = r"\{\s*(\d+),\s*(\d+),\s*(0x[0-9a-fA-F]+),\s*(0x[0-9a-fA-F]+),\s*(0x[0-9a-fA-F]+),\s*Operation25Continuation(\d+)Bytes,\s*sizeof\(Operation25Continuation\6Bytes\),\s*Operation25Continuation\6Unwind,\s*sizeof\(Operation25Continuation\6Unwind\)\s*\}"
    continuation_rows = [tuple(int(value, 0) for value in match) for match in re.findall(continuation_pattern, profile)]
    expected_continuations = ((25, 0, 0xBC106A, 0xBC1255, 0x1D4E3B4, 0),
                              (25, 1, 0xBC1255, 0xBC1271, 0x1D4E3D8, 1),
                              (25, 2, 0xBC1271, 0xBC1277, 0x1D4E3FC, 2))
    if tuple(continuation_rows) != expected_continuations or not re.search(r"OperationContinuationCount\s*=\s*3\s*;", profile):
        raise ValueError("The Single event profile omits its complete ordered CHAININFO continuation table")
    continuation_facts = []
    parent = facts[25]
    previous_end = parent["endRva"]
    for operation, index, begin, end, unwind, array_id in continuation_rows:
        body = arrays[f"Operation25Continuation{array_id}Bytes"]
        unwind_bytes = arrays[f"Operation25Continuation{array_id}Unwind"]
        if (previous_end != begin or end - begin != len(body) or pe.get_data(begin, len(body)) != body
                or runtime_functions.get(begin) != [(begin, end, unwind)]):
            raise ValueError(f"Single continuation {index} full body or unique static RF differs")
        chain_offset = (4 + unwind_bytes[2] * 2 + 3) & ~3
        expected_parent = (parent["rva"], parent["endRva"], parent["unwindRva"])
        if (unwind_bytes[0] != 0x21 or len(unwind_bytes) != chain_offset + 12
                or pe.get_data(unwind, len(unwind_bytes)) != unwind_bytes
                or struct.unpack_from("<III", unwind_bytes, chain_offset) != expected_parent):
            raise ValueError(f"Single continuation {index} complete CHAININFO bytes or parent differs")
        continuation_facts.append(dict(operationId=operation, index=index, rva=begin, endRva=end,
                                       unwindRva=unwind, bodyBytes=len(body), bodySha256=digest(body),
                                       unwindBytes=len(unwind_bytes), unwindSha256=digest(unwind_bytes),
                                       parentRva=expected_parent[0], parentEndRva=expected_parent[1],
                                       parentUnwindRva=expected_parent[2]))
        previous_end = end
    ordered = sorted(facts + continuation_facts, key=lambda row: row["rva"])
    if any(left["endRva"] > right["rva"] for left, right in zip(ordered, ordered[1:])):
        raise ValueError("Operation source bodies overlap")
    single_publisher_path = profile_path.with_name("TweenSingleEventPublication.inl")
    single_publisher_bytes = single_publisher_path.read_bytes()
    single_publisher = single_publisher_bytes.decode("utf-8-sig")
    span_rvas = (0xbc1087, 0xbc1107, 0xbc110d, 0xbc115d, 0xbc1162, 0xbc121b, 0xbc1221)
    span_widths = (9, 6, 9, 5, 9, 6, 9)
    originals = ("660f1f840000000000", "ff9098010000", "ffc68bcee97affffff", "e84ed49800",
                 "ffc68bcee925ffffff", "ff9078010000", "ffc68bcee966feffff")
    for name, expected in (("singleSpanRva", span_rvas), ("singleSpanWidth", span_widths)):
        match = re.search(name + r"\[7\]\s*\{([^}]*)\}", single_publisher)
        if not match or tuple(int(value.strip(), 0) for value in match[1].split(",")) != expected:
            raise ValueError("The actual Single publisher omits the exact seven source declarations")
    single_spans = []
    for index, (rva, width, original) in enumerate(zip(span_rvas, span_widths, originals)):
        body = pe.get_data(rva, width)
        if body != bytes.fromhex(original) or not 0xbc106a <= rva < rva + width <= 0xbc1255:
            raise ValueError("A Single span differs from the complete pinned function observation")
        single_spans.append(dict(index=index, rva=rva, width=width, bytesSha256=digest(body)))
    return dict(schemaVersion=1, kind="latest-first-install-operation-profile-verification", passed=True,
                gameAssembly=dict(path=str(game_path.resolve()), sha256=digest(game)),
                profile=dict(path=str(profile_path.resolve()), sha256=digest(profile_bytes)),
                additionalProfiles=[dict(fileName=companion_path.name, sha256=digest(companion_bytes))],
                singleEventPublisher=dict(fileName=single_publisher_path.name, sha256=digest(single_publisher_bytes)),
                singleEventSpans=single_spans,
                operationCount=26, expectedWrites=64, operations=facts,
                operationContinuations=continuation_facts, singleEventFunctionBytes=previous_end - parent["rva"],
                capstoneVersion=capstone.__version__, pefileVersion=pefile.__version__,
                gameExecuted=False, installed=False, productionAdmissionVerified=False,
                limitations=["Full body, declared static unwind/header bytes and role checks only; language-specific handler tables are not interpreted.",
                             "Full Tween RF extents include inline switch tables; linear decoding is not a complete control-flow proof.",
                             "This does not prove first execution exclusion or actual hook callbacks."])


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--game-assembly", type=Path, required=True)
    parser.add_argument("--profile", type=Path, required=True)
    parser.add_argument("--output-json", type=Path, required=True)
    args = parser.parse_args()
    if args.output_json.exists():
        raise SystemExit("Preserve prior evidence; choose a fresh output path")
    result = verify(args.game_assembly, args.profile)
    args.output_json.parent.mkdir(parents=True, exist_ok=True)
    args.output_json.write_text(json.dumps(result, indent=2), encoding="utf-8")
    print("Verified 26 primary operation RFs and all 3 Single CHAININFO continuations (647-byte function), declared unwind/header bytes, roles and slots; game not executed.")
