"""Verify owned fixed-original bytes against the pinned game on disk; load neither image."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import struct

import capstone
import pefile


PINNED_SHA = "43fb0aea1b80c74963dae88ec7fc1c3c1993d893ce94d12972000fcc9b653ad1"
FUNCTIONS = (
    ("Register", 0x44D630, 15), ("Remove", 0x447AD0, 14),
    ("Consume", 0x447C30, 14), ("Pump", 0x447D60, 15),
    ("Drain", 0x447750, 14), ("Callback", 0x78F5E0, 17),
    ("Yield", 0x78FC50, 15), ("Dispatch", 0x5A5260, 14),
    ("Batch", 0x5DA510, 14),
)


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def absolute(value: str) -> Path:
    path = Path(value)
    if not path.is_absolute():
        raise ValueError(f"Absolute path required: {value}")
    return path.resolve(strict=True)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--directory", required=True)
    parser.add_argument("--image", default="D:/SteamLibrary/steamapps/common/Rizline/UnityPlayer.dll")
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    directory, image = absolute(args.directory), absolute(args.image)
    output = Path(args.output)
    if not output.is_absolute() or output.exists() or not output.parent.is_dir():
        raise ValueError("Use an absolute nonexistent output file in an existing directory.")
    executable = directory / "EngineTransportOriginal.Fixture.exe"
    manifest_path = directory / "transport-original-fixture-build.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
    result = dict(schemaVersion=1, kind="engine-transport-original-disk-verification", passed=False,
                  checks=0, diskOnly=True, dllLoaded=False, fixtureExecuted=False,
                  gameExecuted=False, sourcePatched=False, productionAdmissionVerified=False,
                  inputs=[], originals=[], failure=None)

    def require(value: bool, reason: str) -> None:
        result["checks"] += 1
        if not value:
            raise ValueError(reason)

    try:
        require(manifest.get("kind") == "engine-transport-original-fixture-build"
                and manifest.get("buildSucceeded") is True and manifest.get("buildExitCode") == 0
                and manifest.get("sourceHashesUnchanged") is True, "Native build did not complete.")
        rows = [row for row in manifest["files"] if row["name"] == executable.name]
        require(len(rows) == 1 and rows[0]["sha256"] == sha(executable)
                and rows[0]["bytes"] == executable.stat().st_size, "Fixture differs from its build.")
        require(sha(image) == PINNED_SHA, "UnityPlayer differs from the pinned disk image.")
        for path in (image, executable, manifest_path, Path(__file__).resolve()):
            result["inputs"].append(dict(path=str(path), sha256=sha(path), bytes=path.stat().st_size))
        source = pefile.PE(str(image), fast_load=True)
        owned = pefile.PE(str(executable), fast_load=False)
        require(source.FILE_HEADER.Machine == owned.FILE_HEADER.Machine == 0x8664, "Use AMD64 images.")
        exports = {entry.name.decode("ascii"): entry.address for entry in owned.DIRECTORY_ENTRY_EXPORT.symbols
                   if entry.name and not entry.forwarder}
        expected = {f"{family}{name}{suffix}" for family in ("RizEngineTransportOriginal", "RtTransportBaseline")
                    for name, _, _ in FUNCTIONS for suffix in ("", "PrefixEnd", "End")}
        require(set(exports) == expected, "Owned EXE must export exactly 54 original/baseline markers.")
        directory_entry = owned.OPTIONAL_HEADER.DATA_DIRECTORY[3]
        table = owned.get_data(directory_entry.VirtualAddress, directory_entry.Size)
        require(len(table) == directory_entry.Size and len(table) % 12 == 0, "Malformed owned .pdata table.")
        functions = [struct.unpack_from("<III", table, at) for at in range(0, len(table), 12)]
        md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
        md.detail = True
        slots = []
        for index, (name, source_rva, length) in enumerate(FUNCTIONS):
            symbol = "RizEngineTransportOriginal" + name
            begin, prefix_end, end = (exports[symbol + suffix] for suffix in ("", "PrefixEnd", "End"))
            require(prefix_end - begin == length and end - prefix_end == 10, f"{name}: wrong prefix/tail size.")
            prefix = owned.get_data(begin, length)
            require(prefix == source.get_data(source_rva, length), f"{name}: prefix differs from the actual pinned source.")
            baseline_name = "RtTransportBaseline" + name
            baseline, baseline_prefix_end, baseline_end = (exports[baseline_name + suffix]
                for suffix in ("", "PrefixEnd", "End"))
            require(baseline_prefix_end - baseline == length and baseline_end > baseline_prefix_end
                    and owned.get_data(baseline, length) == prefix,
                    f"{name}: actual baseline must execute exactly the same pinned prefix bytes.")
            instructions = list(md.disasm(prefix, begin))
            require(sum(ins.size for ins in instructions) == length, f"{name}: prefix does not end on an instruction boundary.")
            tail = owned.get_data(prefix_end, 10)
            code = list(md.disasm(tail, prefix_end))
            scratch = "r10" if name == "Callback" else "r11"
            mov_prefix = bytes.fromhex("4c8b15" if scratch == "r10" else "4c8b1d")
            jump = bytes.fromhex("41ffe2" if scratch == "r10" else "41ffe3")
            require(tail[:3] == mov_prefix and tail[7:] == jump and len(code) == 2,
                    f"{name}: tail must preserve inputs/RAX/RFLAGS and use its proved scratch register.")
            require(code[0].mnemonic == "mov" and code[0].size == 7 and code[1].mnemonic == "jmp"
                    and code[1].op_str == scratch and code[1].size == 3,
                    f"{name}: unexpected register transfer encoding.")
            slot = prefix_end + 7 + struct.unpack_from("<i", tail, 3)[0]
            require(slot % 8 == 0, f"{name}: continuation slot is unaligned.")
            sections = [section for section in owned.sections
                        if section.VirtualAddress <= slot and slot + 8 <= section.VirtualAddress + section.Misc_VirtualSize]
            require(len(sections) == 1 and bool(sections[0].Characteristics & 0x80000000)
                    and not bool(sections[0].Characteristics & 0x20000000), f"{name}: slot must be writable data, not code.")
            slots.append(slot)
            entries = [row for row in functions if row[0] == begin]
            require(len(entries) == 1 and entries[0][1] == end, f"{name}: owned RF bounds differ from actual markers.")
            unwind_rva = entries[0][2]
            header = owned.get_data(unwind_rva, 4)
            require(len(header) == 4 and header[0] & 7 == 1 and header[0] >> 3 == 0,
                    f"{name}: expected independent ordinary unwind information.")
            require(header[1] == end - begin, f"{name}: transfer PCs must remain in the recorded prolog.")
            codes = owned.get_data(unwind_rva + 4, header[2] * 2)
            require(len(codes) == header[2] * 2 and header[2] > 0, f"{name}: unwind codes are absent or truncated.")
            result["originals"].append(dict(name=name, sourceRva=source_rva, begin=begin, prefixEnd=prefix_end,
                baselineBegin=baseline, baselinePrefixEnd=baseline_prefix_end, baselineEnd=baseline_end,
                end=end, prefixHex=prefix.hex(), tailHex=tail.hex(), scratch=scratch, continuationSlotRva=slot,
                runtimeFunction=entries[0], unwindHeader=header.hex(), unwindCodes=codes.hex()))
        require(all(slot == slots[0] + 8 * index for index, slot in enumerate(slots)),
                "Continuation entries do not follow the exact nine-channel order.")
        for row in result["inputs"]:
            require(sha(Path(row["path"])) == row["sha256"], "Input changed while inspecting the disk images.")
        result["passed"] = True
    except Exception as error:
        result["failure"] = str(error)
        raise
    finally:
        output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(dict(passed=True, checks=result["checks"], originals=len(result["originals"]), output=str(output))))


if __name__ == "__main__":
    main()
