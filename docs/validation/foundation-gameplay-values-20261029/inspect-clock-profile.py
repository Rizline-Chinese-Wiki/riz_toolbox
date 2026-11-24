"""Independent pinned PE + metadata producer/consumer evidence. Never executes the game or changes old evidence."""
from datetime import datetime, timezone
from pathlib import Path
import argparse
import hashlib
import json
import re
import struct
import capstone
import pefile

parser = argparse.ArgumentParser()
parser.add_argument("--output", required=True)
args = parser.parse_args()
destination = Path(args.output)
if destination.exists():
    raise SystemExit("A fresh evidence file is required.")
root = Path(__file__).resolve().parents[3]
binary = Path(r"D:/SteamLibrary/steamapps/common/Rizline/GameAssembly.dll")
dump_path = Path(r"C:/Users/71957/Desktop/绿豆诡计v2/output/il2cpp_output/dump.cs")
sha = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79"
data = binary.read_bytes()
assert hashlib.sha256(data).hexdigest() == sha
dump = dump_path.read_text(encoding="utf-8")
assert hashlib.sha256(dump_path.read_bytes()).hexdigest() == "092cdb96914ccb7af3f31d9d2e703e00dd6ddff63fd559f11ac6b42e6635c1cb"
pe = pefile.PE(data=data, fast_load=True)
pe.parse_data_directories(directories=[pefile.DIRECTORY_ENTRY["IMAGE_DIRECTORY_ENTRY_IMPORT"], pefile.DIRECTORY_ENTRY["IMAGE_DIRECTORY_ENTRY_EXPORT"]])
decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
decoder.detail = True
bounds = {
    "Progress.Update": (0x400fe0, 0x4016a0), "Progress.RealTimeToChartTime": (0x400c80, 0x400ef0),
    "Progress.ChartTimeToRealTime": (0x400400, 0x400670), "Chart.ChartDelay": (0x3e5670, 0x3e5690),
    "MusicPlayer.GetTime": (0x3fbb10, 0x3fbd90), "Progress.Play": (0x400700, 0x400810),
    "Progress.Constructor": (0x4016a0, 0x4016d0), "NoteControl.Update": (0x3fcd60, 0x3fcf70),
    "LineControl.Start": (0x3ef530, 0x3ef570), "LevelControl.Start": (0x3ea580, 0x3ecf20)
}
functions = {name: list(decoder.disasm(pe.get_data(start, end - start), start)) for name, (start, end) in bounds.items()}
checks = []
def expect(function, address, mnemonic, operands):
    instruction = next(i for i in functions[function] if i.address == address)
    assert (instruction.mnemonic, instruction.op_str) == (mnemonic, operands), (function, hex(address), instruction.mnemonic, instruction.op_str)
    checks.append({"function": function, "rva": hex(address), "mnemonic": mnemonic, "operands": operands})
def constant(function, address, expected, size=8):
    instruction = next(i for i in functions[function] if i.address == address)
    operand = next(o for o in instruction.operands if o.type == capstone.x86.X86_OP_MEM and o.mem.base == capstone.x86.X86_REG_RIP)
    target = instruction.address + instruction.size + operand.mem.disp
    value = struct.unpack("<d" if size == 8 else "<f", pe.get_data(target, size))[0]
    assert value == expected, (function, hex(address), value, expected)
    checks.append({"function": function, "rva": hex(address), "constantRva": hex(target), "value": value})
def class_body(name, namespace):
    match = re.search(r"// Namespace: " + re.escape(namespace) + r"\npublic class " + re.escape(name) + r"(?: : [^\n]+)?\n\{(.*?)\n\}", dump, re.S)
    assert match, (name, namespace)
    return match.group(1)
metadata = {}
for name, namespace, rows in [
    ("LevelInformation", "InLevel", ["InLevel.ProgressControl progressControl; // 0x28", "InLevel.Chart chart; // 0x88", "System.Single speed; // 0x98", "System.Single offset; // 0x9C", "System.Single nowTime; // 0xB0", "System.Single nowRealTime; // 0xB4", "System.Single nowSyncedTime; // 0xB8"]),
    ("ProgressControl", "InLevel", ["System.Double _offset; // 0x48", "System.Double _nowTime; // 0x50", "System.Boolean timeReady; // 0x65", "System.Single RealTimeToChartTime(System.Double thisRealTime); // RVA: 0x400C80", "System.Single ChartTimeToRealTime(System.Double thisNowTime); // RVA: 0x400400"]),
    ("Chart", "InLevel", ["System.Int32 chartDelayMs; // 0x14", "System.Single bPM; // 0x30", "System.Collections.Generic.List<InLevel.KeyPoint> bpmShifts; // 0x38", "System.Single get_ChartDelay(); // RVA: 0x3E5670"]),
    ("KeyPoint", "InLevel", ["System.Single time; // 0x10", "System.Single value; // 0x14", "System.Single floorPosition; // 0x1C"]),
    ("MUGMusicPlayer", "Pigeon.MUGToolkit.Core", ["System.Single GetTime(); // RVA: 0x3FBB10", "System.Void Pause(); // RVA: 0x3FBD90", "System.Void ReplayScheduled(System.Single delay); // RVA: 0x3FBE30", "System.Void ResumeScheduled(System.Single delay); // RVA: 0x3FC060"])
]:
    body = class_body(name, namespace)
    assert all(row in body for row in rows), name
    metadata[name] = rows
    if name in ("ProgressControl", "MUGMusicPlayer"):
        assert not re.search(r"\b(?:SetSpeed|Seek)\(", body), name

expect("Progress.Update", 0x4011d7, "movss", "xmm0, dword ptr [rax + 0x9c]")
expect("Progress.Update", 0x4011e2, "movsd", "qword ptr [rbx + 0x48], xmm0")
expect("Progress.Update", 0x4011f4, "cmp", "byte ptr [rax + 0xc0], 0")
expect("Progress.Update", 0x4011fd, "cmp", "byte ptr [rbx + 0x42], 0")
expect("Progress.Update", 0x401201, "je", "0x40125c")
expect("Progress.Update", 0x401203, "cmp", "byte ptr [rbx + 0x65], 0")
expect("Progress.Update", 0x401207, "je", "0x40125c")
expect("Progress.Update", 0x401218, "call", "0x3fbb10")
expect("Progress.Update", 0x401224, "subsd", "xmm1, qword ptr [rbx + 0x48]")
expect("Progress.Update", 0x401253, "addsd", "xmm1, xmm6")
expect("Progress.Update", 0x401257, "movsd", "qword ptr [rbx + 0x50], xmm1")
expect("Progress.Update", 0x401272, "movss", "dword ptr [rax + 0xb4], xmm0")
expect("Progress.Update", 0x401296, "movss", "dword ptr [rax + 0xb8], xmm0")
expect("Progress.Update", 0x4012a7, "call", "0x400c80")
expect("Progress.Update", 0x4012b5, "movss", "dword ptr [rdi + 0xb0], xmm0")
expect("MusicPlayer.GetTime", 0x3fbc18, "call", "0x3f7840")
expect("MusicPlayer.GetTime", 0x3fbc3f, "subsd", "xmm6, xmm0")
expect("MusicPlayer.GetTime", 0x3fbc43, "call", "0x15119a0")
expect("MusicPlayer.GetTime", 0x3fbd3a, "movups", "xmmword ptr [rbx + 0x18], xmm0")
expect("MusicPlayer.GetTime", 0x3fbd57, "call", "0x15119a0")
expect("NoteControl.Update", 0x3fcd87, "mov", "ecx, dword ptr [rax + 0x98]")
expect("NoteControl.Update", 0x3fcd8d, "mov", "dword ptr [rbx + 0x40], ecx")
expect("LineControl.Start", 0x3ef541, "mov", "eax, dword ptr [rax + 0x98]")
expect("LineControl.Start", 0x3ef547, "mov", "dword ptr [rcx + 0x48], eax")
expect("LevelControl.Start", 0x3ecc45, "call", "0x400400")
expect("LevelControl.Start", 0x3ecc53, "movss", "dword ptr [rdi + 0x34], xmm0")
expect("LevelControl.Start", 0x3ecd93, "call", "0x400400")
expect("LevelControl.Start", 0x3ecda1, "movss", "dword ptr [rbx + 0x38], xmm0")
for name, address in [("Progress.RealTimeToChartTime", 0x400ed5), ("Progress.RealTimeToChartTime", 0x400e90), ("Progress.ChartTimeToRealTime", 0x40064e), ("Progress.ChartTimeToRealTime", 0x40060d)]:
    constant(name, address, 60)
constant("Chart.ChartDelay", 0x3e5678, 1000, 4)
constant("Progress.Update", 0x4011bb, -99)
assert struct.unpack("<d", pe.get_data(0x4016a2, 8))[0] == -100
profile_path = root / "src/RizToolbox.Adapter/Gameplay/GameplayClockNativeProfile.cs"
profile = profile_path.read_text(encoding="utf-8")
profile_bodies = re.findall(r'\((0x[0-9a-f]+), (\d+), "([0-9a-f]{64})"\)', profile)
assert len(profile_bodies) == 5
for start, length, expected in profile_bodies:
    assert hashlib.sha256(pe.get_data(int(start, 16), int(length))).hexdigest() == expected
proof = {
    "kind": "pinned-static-only", "observedAtUtc": datetime.now(timezone.utc).isoformat(), "binary": str(binary), "sha256": sha,
    "dump": str(dump_path), "dumpSha256": hashlib.sha256(dump_path.read_bytes()).hexdigest(),
    "tools": {"pefile": pefile.__version__, "capstone": capstone.__version__, "ida": "Unavailable in the local tool index; existing Python/pefile/Capstone only; no tool installation or GUI."},
    "E-imports": [{"dll": item.dll.decode(), "count": len(item.imports)} for item in pe.DIRECTORY_ENTRY_IMPORT],
    "E-exports": {"count": len(pe.DIRECTORY_ENTRY_EXPORT.symbols)}, "metadata": metadata,
    "instructionAndConstantChecks": checks, "runtimeProfileBodies": [{"rva": start, "bytes": int(length), "sha256": expected} for start, length, expected in profile_bodies],
    "units": {"nowTime": "piecewise BPM chart coordinate", "nowRealTime": "float(progress seconds)", "nowSyncedTime": "same progress-seconds producer, no independent latency correction proven",
              "progressSeconds": "playing && timeReady && !levelOver: MusicPlayer.GetTime()-_offset; paused/nonplaying held; levelOver may advance with DeltaTime",
              "chartFormulaEmpty": "seconds*bPM/60", "chartFormulaSegment": "key.time+(seconds-key.floorPosition)*key.value*bPM/60",
              "secondsFormulaEmpty": "chartCoordinate*60/bPM", "secondsFormulaSegment": "key.floorPosition+(chartCoordinate-key.time)*60/key.value/bPM",
              "chartDelay": "chartDelayMs/1000; not added by conversion a second time", "offset": "actual progress _offset subtraction; initialization threshold -99, initial sentinel -100",
              "musicPlayerClock": "game DspTimeService-startPlayTime with AudioSource.time fallback/origin correction; getter can write its origin and is not invoked by clock reads",
              "speedField": "copied by NoteControl and LineControl geometry consumers; no synchronized playback-rate transaction inferred"},
    "runtimeSecondsMappingVerified": False, "hostDspRateVerified": False, "latencyVerified": False,
    "nativeSetSpeedSupported": False, "nativeSeekSupported": False,
    "sourceSha256": {str(path.relative_to(root)): hashlib.sha256(path.read_bytes()).hexdigest() for path in [profile_path, root / "src/RizToolbox.Sdk/GameplayContracts.cs", root / "src/RizToolbox.Adapter/Gameplay/GameplayNativeBackend.cs", root / "src/RizToolbox.Adapter/Gameplay/GameplayPreparedControl.cs", root / "src/RizToolbox.Adapter/Gameplay/GameplayService.cs"]},
    "functions": {name: {"begin": hex(bounds[name][0]), "end": hex(bounds[name][1]), "sha256": hashlib.sha256(pe.get_data(bounds[name][0], bounds[name][1] - bounds[name][0])).hexdigest(),
                          "instructions": [{"rva": hex(i.address), "mnemonic": i.mnemonic, "operands": i.op_str} for i in instructions]} for name, instructions in functions.items()}
}
destination.parent.mkdir(parents=True, exist_ok=True)
destination.write_text(json.dumps(proof, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"passed": True, "functions": len(functions), "metadataClasses": len(metadata), "instructionAndConstantChecks": len(checks), "runtimeProfileBodies": len(profile_bodies), "proofSha256": hashlib.sha256(destination.read_bytes()).hexdigest(), "runtimeSecondsMappingVerified": False, "nativeValueControlsSupported": False}))
