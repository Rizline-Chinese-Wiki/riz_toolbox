"""Pinned, bounded, read-only LOCAL launch/result/retry inspection.

Derived from ../local-provider-catalog-20261021/inspect-catalog.py.
Never launches, patches, loads into a process, or executes the sample.
"""
import argparse, bisect, hashlib, json, platform, re
from collections import defaultdict
from datetime import datetime, timezone
from pathlib import Path
import capstone, pefile
from capstone.x86 import X86_OP_IMM, X86_OP_MEM, X86_REG_RIP

EXPECTED = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79"
SCOPES = {'InLevelController.SetInsideLevelParams': 3584224, 'InLevelController.Awake': 3610640, 'InLevelSettings.cctor': 3612192, 'NormalMode.ControllerOver': 3926624, 'NormalMode.LevelOver': 3935232, 'NormalMode.Retry': 3937040, 'NormalMode.RetryCoroutine.MoveNext': 3958960, 'LevelOver.Back': 3989536, 'LevelOver.Retry': 3997472, 'LevelOver.SetDetails': 3997872, 'LevelOver.StartPlayLevel.MoveNext': 4030480, 'LevelOver.BackAudioFade': 4002304, 'LevelOver.BackFinal': 4002592, 'SceneManager.DontDestroyOnLoadScene': 4200912, 'SceneManager.StartNextScene': 4202864, 'SceneManager.StartNextSceneEnumerator.MoveNext': 4210416, 'Unity.Object.Instantiate.Object': 22279792}

ap = argparse.ArgumentParser()
ap.add_argument("--game", type=Path, required=True)
ap.add_argument("--dump", type=Path, required=True)
ap.add_argument("--symbols", type=Path, required=True)
ap.add_argument("--out", type=Path, required=True)
a = ap.parse_args()
raw = a.game.read_bytes()
sha = hashlib.sha256(raw).hexdigest()
if sha != EXPECTED:
    raise SystemExit("Unsupported image; no pinned scopes used: " + sha)
pe = pefile.PE(data=raw, fast_load=True)
pe.parse_data_directories(directories=[pefile.DIRECTORY_ENTRY["IMAGE_DIRECTORY_ENTRY_IMPORT"],
                                     pefile.DIRECTORY_ENTRY["IMAGE_DIRECTORY_ENTRY_EXPORT"]])
by = defaultdict(list)
for entry in json.loads(a.symbols.read_text(encoding="utf8"))["ScriptMethod"]:
    by[entry["Address"]].append(entry)
starts = sorted(by)
a.out.mkdir(parents=True, exist_ok=True)
imports = {entry.dll.decode(errors="replace"): [item.name.decode(errors="replace") if item.name else "ordinal:" + str(item.ordinal)
           for item in entry.imports] for entry in getattr(pe, "DIRECTORY_ENTRY_IMPORT", [])}
exports = [{"name": item.name.decode(errors="replace") if item.name else "ordinal:" + str(item.ordinal), "rva": hex(item.address)}
           for item in getattr(getattr(pe, "DIRECTORY_ENTRY_EXPORT", None), "symbols", [])]
categories = {
    "crypto": r"crypt|bcrypt|ncrypt|encrypt|decrypt",
    "network": r"socket|wsa|send|recv|connect|http|internet|dns",
    "file": r"file|directory|findfirst|findnext|readfile|writefile|flush|mapview",
    "process_memory_thread": r"process|thread|virtualalloc|virtualfree|virtualquery|context",
    "registry": r"^reg(open|close|query|set|create|delete)",
    "dynamic_loading": r"loadlibrary|getprocaddress|getmodulehandle",
}
triage = {
    "schemaVersion": 1, "observedAtUtc": datetime.now(timezone.utc).isoformat(),
    "quality": "readable-native-PE-imports-and-exports", "gameAssemblySha256": sha,
    "machine": hex(pe.FILE_HEADER.Machine), "imageBase": hex(pe.OPTIONAL_HEADER.ImageBase),
    "clrDirectorySize": pe.OPTIONAL_HEADER.DATA_DIRECTORY[14].Size,
    "imports": imports, "exports": exports,
    "importCategories": {category: [{"dll": dll, "name": name} for dll, names in imports.items() for name in names
                                    if re.search(pattern, name, re.I)] for category, pattern in categories.items()},
    "tools": {"python": platform.python_version(), "pythonExecutable": __import__("sys").executable,
              "capstone": capstone.__version__, "pefile": pefile.__version__,
              "fallback": "IDA/radare2 unavailable per tool-index; existing pefile/capstone fallback reused"},
    "limitations": ["Imports are static anchors and do not exclude dynamic API loading.",
                    "ScriptMethod names may alias shared/folded code; signatures are not runtime ABI proof.",
                    "No process, UI, runtime method invocation, network access or binary edit."]
}
# Record survey/imports/exports before interpreting function bodies.
(a.out / "triage.json").write_text(json.dumps(triage, indent=2), encoding="utf8")

md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
md.detail = True
text, scopes = [], []
for name, start in SCOPES.items():
    end = starts[bisect.bisect_right(starts, start)]
    if start not in by or end - start > 0x1800:
        raise SystemExit("Unbounded or missing scope: " + name)
    text.append(f"\n=== {name} [{start:08x}, {end:08x}) ===")
    for symbol in by[start]:
        text.append(symbol["Name"] + " :: " + symbol["Signature"])
    instructions, calls = [], []
    for ins in md.disasm(pe.get_data(start, end - start), start):
        row = {"rva": hex(ins.address), "mnemonic": ins.mnemonic, "operands": ins.op_str, "bytes": ins.bytes.hex()}
        note = ""
        if ins.mnemonic in ("call", "jmp") and ins.operands and ins.operands[0].type == X86_OP_IMM:
            dest = ins.operands[0].imm
            names = [s["Name"] for s in by.get(dest, [])]
            row["destinationNames"] = names
            if ins.mnemonic == "call":
                calls.append({"at": hex(ins.address), "to": hex(dest), "names": names})
            if names:
                note = " ; " + " | ".join(names[:5])
        for op in ins.operands:
            if op.type == X86_OP_MEM and op.mem.base == X86_REG_RIP:
                row["ripRelativeRva"] = hex(ins.address + ins.size + op.mem.disp)
        instructions.append(row)
        text.append(f"{ins.address:08x} {ins.mnemonic:9} {ins.op_str}{note}")
    scopes.append({"name": name, "start": hex(start), "end": hex(end), "symbols": by[start],
                   "rangeSha256": hashlib.sha256(pe.get_data(start, end - start)).hexdigest(),
                   "calls": calls, "instructions": instructions})
(a.out / "disassembly.txt").write_text("\n".join(text), encoding="utf8")
(a.out / "boundaries.json").write_text(json.dumps({
    "schemaVersion": 1, "evidenceKind": "static-disassembly", "runtimeAcceptance": False,
    "gameAssemblySha256": sha, "dumpSha256": hashlib.sha256(a.dump.read_bytes()).hexdigest(),
    "scriptJsonSha256": hashlib.sha256(a.symbols.read_bytes()).hexdigest(), "triageRef": "triage.json",
    "scopes": scopes}, indent=2), encoding="utf8")

dump = a.dump.read_text(encoding="utf8").splitlines()
classes = {"LevelMetadata", "ILevelMetadata", "Level", "LevelInfo", "MusicInfo", "IllustrationInfo", "ChartInfo",
           "LevelDifference", "BestScore", "InsideLevelParams", "InsideLevelResult", "JudgeData", "AssetManager",
           "LevelOverControl", "NormalModeInLevelController", "LevelControl", "DomainWrapper", "SaveSystem", "GameManager", "InLevelControl", "InLevelController", "InLevelSettings", "SceneManager", "BottomBar", "TouchBlocker", "LevelListControl", "LevelLoopList", "LevelCardControl",
           "SaveDomain", "SaveData", "<LoadLevel>d__119", "<LoadLevel>d__53"}
excerpt = []
for i, line in enumerate(dump):
    match = re.match(r"public (?:class|struct) ([^ :]+)", line)
    if match and match.group(1) in classes:
        end = i + 1
        while end < len(dump) and dump[end] != "}":
            end += 1
        excerpt.extend(f"{n+1}: {dump[n]}" for n in range(i, min(end+1, len(dump))))
        excerpt.append("")
(a.out / "metadata-excerpts.txt").write_text("\n".join(excerpt), encoding="utf8")
for scope in scopes:
    print(scope["name"], scope["start"], scope["end"])
    for call in scope["calls"]:
        if call["names"]:
            print(" ", call["at"], call["to"], " | ".join(call["names"][:3]))
