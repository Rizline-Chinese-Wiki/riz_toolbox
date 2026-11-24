"""Check the closed Hold List.Clear pin against the stock result and PE on disk.

Run with Python, capstone and pefile. The DLL is only read as bytes.
"""

import argparse
import hashlib
import json
from pathlib import Path
import re

import capstone
import pefile


ROOT = Path(__file__).resolve().parents[1]
PROFILE = ROOT / "src/RizToolbox.Adapter/Gameplay/NativeSeekHoldNativeProfile.cs"
PRIOR_EVIDENCE = ROOT / "work/seek-hold-state-native-20261110/hold-profile-static-evidence.json"
PRIOR_EVIDENCE_SHA256 = "04929e45e751b93740f775c470e4aaf7d5da71dbf40685280d07df7ad36c5562"
STOCK_RESULT_SHA256 = "41ee14d84e0d70c2dd82eba0a0f9ce4d58c9baec938ae76727c0ae4ae1fb284d"


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def require(condition, problem):
    if not condition:
        raise SystemExit(problem)


def read_pinned(path, expected_hash):
    data = path.read_bytes()
    require(sha256(data) == expected_hash, f"Input SHA-256 differs for {path}")
    return data


def verify_stock_result(path):
    observation = json.loads(read_pinned(path, STOCK_RESULT_SHA256))
    result = observation["result"]
    error = result["error"]
    require(observation["action"] == "seek" and observation["stage"] == "execute"
            and result["success"] is False and error["code"] == 5
            and error["targetId"] == "gameplay.controller/2/Seek", "Expected the stock Seek refusal")
    match = re.fullmatch(
        r"UnsupportedSignature: Hold List\.Clear entry RVA (0x[0-9a-f]+) differs from pinned RVA "
        r"(0x[0-9a-f]+) \(slot=(\d+)\)\.", error["message"])
    require(match is not None, "The stock refusal does not identify the closed Clear MethodInfo")
    return int(match[1], 16), int(match[2], 16), int(match[3])


def verify_clear_body(pe, functions, clear_rva, array_clear_rva):
    # This body has no stack frame or unwind record. The last instruction is
    # the reachable empty-list return, followed by alignment padding.
    require(not any(start <= clear_rva < end for start, end in functions.items()),
            "Clear entered a PE unwind span")
    previous_end = max(end for end in functions.values() if end <= clear_rva)
    next_start = min(start for start in functions if start > clear_rva)
    require(previous_end == clear_rva - 1 and pe.get_data(previous_end, 1) == b"\xcc"
            and next_start == clear_rva + 0x30, "Clear's adjacent function boundaries changed")
    end = clear_rva + 0x22
    code = pe.get_data(clear_rva, end - clear_rva)
    require(pe.get_data(end, next_start - end) == b"\xcc" * (next_start - end),
            "Clear has extra instructions after the return")
    decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
    instructions = list(decoder.disasm(code, clear_rva))
    require(b"".join(instruction.bytes for instruction in instructions) == code, "Incomplete Clear decode")
    require([(i.address - clear_rva, i.mnemonic, i.op_str) for i in instructions] == [
        (0x00, "mov", "r8d, dword ptr [rcx + 0x18]"),
        (0x04, "inc", "dword ptr [rcx + 0x1c]"),
        (0x07, "mov", "dword ptr [rcx + 0x18], 0"),
        (0x0e, "test", "r8d, r8d"),
        (0x11, "jle", hex(end - 1)),
        (0x13, "mov", "rcx, qword ptr [rcx + 0x10]"),
        (0x17, "xor", "r9d, r9d"),
        (0x1a, "xor", "edx, edx"),
        (0x1c, "jmp", hex(array_clear_rva)),
        (0x21, "ret", ""),
    ], "Unexpected Clear size/version, branch or array-clear tail call")
    return code


def verify_profile(clear_rva, previous_rva, slot, code, array_clear):
    source = PROFILE.read_text(encoding="utf-8-sig")
    pins = [(name, int(rva, 16), int(size), digest) for name, rva, size, digest in re.findall(
        r'new\("([^"]+)", (0x[0-9a-f]+), (\d+), "([0-9a-f]{64})"\)', source)]
    require([pin for pin in pins if pin[1] == clear_rva] == [
        ("ClosedNoteList.Clear", clear_rva, len(code), sha256(code))], "Full Clear body pin differs from disk")
    require(all(pin[1] != previous_rva for pin in pins), "The rejected Clear candidate remains pinned")
    require([pin for pin in pins if pin[0] == "System.Array.Clear"] == [
        ("System.Array.Clear", int(array_clear["rva"], 16), array_clear["length"], array_clear["sha256"])],
        "System.Array.Clear tail pin differs from its existing body evidence")
    for binding in (
        f"private const int ClearRva = {clear_rva:#x};",
        f"private const ushort ClearSlot = {slot};",
        'InspectListMethod("Clear", ClearRva, Array.Empty<string>(), ClearSlot)',
        "VerifyMethod(_clear, ClearRva, ClearSlot)",
        "InspectListMethod(expected.Name, pinnedRva, expected.ParameterTypes.ToArray(), pinnedSlot)",
        "if (entry.Rva != pinnedRva)",
        "if (pinnedSlot is ushort expectedSlot && entry.MethodInfoSlot != expectedSlot)",
        "SHA256.HashData(bytes)",
    ):
        require(binding in source, f"Hold profile lacks the required Clear binding {binding!r}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("binary", type=Path, help="Pinned GameAssembly.dll")
    parser.add_argument("stock_result", type=Path, help="Captured r20 stock Seek result JSON")
    parser.add_argument("--prior-evidence", type=Path, default=PRIOR_EVIDENCE,
                        help="Pinned historical Hold profile evidence when testing a detached tree")
    args = parser.parse_args()

    clear_rva, previous_rva, slot = verify_stock_result(args.stock_result)
    prior = json.loads(read_pinned(args.prior_evidence, PRIOR_EVIDENCE_SHA256))
    array_clear = next(body for body in prior["candidateBodies"] if body["name"] == "System.Array.Clear")
    array_clear_rva = int(array_clear["rva"], 16)
    binary_hash = prior["inputs"]["binary"]["sha256"]
    pe = pefile.PE(data=read_pinned(args.binary, binary_hash), fast_load=True)
    pe.parse_data_directories(directories=[pefile.DIRECTORY_ENTRY["IMAGE_DIRECTORY_ENTRY_EXCEPTION"]])
    functions = {row.struct.BeginAddress: row.struct.EndAddress for row in pe.DIRECTORY_ENTRY_EXCEPTION}
    require(functions[array_clear_rva] == array_clear_rva + array_clear["length"]
            and sha256(pe.get_data(array_clear_rva, array_clear["length"])) == array_clear["sha256"],
            "The complete System.Array.Clear tail body differs from disk")
    code = verify_clear_body(pe, functions, clear_rva, array_clear_rva)
    verify_profile(clear_rva, previous_rva, slot, code, array_clear)
    print(json.dumps(dict(passed=True, gameAssemblySha256=binary_hash, stockResultSha256=STOCK_RESULT_SHA256,
                          clearRva=hex(clear_rva), methodInfoSlot=slot, bodyLength=len(code), bodySha256=sha256(code),
                          nonEmptyTailRva=hex(array_clear_rva), tailBodyLength=array_clear["length"])))


if __name__ == "__main__":
    main()
