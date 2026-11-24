"""Bounded static PE evidence only. Does not map/load/execute target images."""
from pathlib import Path
import bisect, hashlib, json, struct, sys
import capstone, pefile

HERE = Path(__file__).resolve().parent
IMAGE = Path(r"D:/SteamLibrary/steamapps/common/Rizline/UnityPlayer.dll")
GAME = Path(r"D:/SteamLibrary/steamapps/common/Rizline/GameAssembly.dll")
data = IMAGE.read_bytes()
pe = pefile.PE(data=data, fast_load=True)
base = pe.OPTIONAL_HEADER.ImageBase
directory = pe.OPTIONAL_HEADER.DATA_DIRECTORY[3]
pdata = pe.get_data(directory.VirtualAddress, directory.Size)
functions = [struct.unpack_from("<III", pdata, i) for i in range(0, len(pdata)-11, 12)]
starts = [row[0] for row in functions]
md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)

def primary(row):
    for _ in range(12):
        header = pe.get_data(row[2], 4)
        if not (header[0] >> 3) & 4:
            return row
        row = struct.unpack("<III", pe.get_data(row[2] + 4 + ((header[2]+1) & ~1)*2, 12))
    raise ValueError("Unwind chain exceeds research bound")

def region(entry, label):
    root = primary(functions[bisect.bisect_right(starts, entry)-1])
    group = [row for row in functions if primary(row) == root]
    lo, hi = min(row[0] for row in group), max(row[1] for row in group)
    body = pe.get_data(lo, hi-lo)
    instructions = list(md.disasm(body, lo))
    assert instructions and sum(item.size for item in instructions) == len(body)
    return dict(label=label, beginRva=hex(lo), endRva=hex(hi),
                bodySha256=hashlib.sha256(body).hexdigest(), prefix32=body[:32].hex(),
                bodyBytes=body.hex(), runtimeFunctions=[dict(begin=hex(a), end=hex(b), unwind=hex(u),
                    unwindHeader=pe.get_data(u,4).hex()) for a,b,u in group],
                instructions=[dict(rva=hex(i.address), bytes=i.bytes.hex(), op=i.mnemonic, args=i.op_str) for i in instructions])

name_base, function_base, count = 0x18db7a0, 0x18d4ae0, 3479
registrations = []
for index in (2237, 2282, 2283, 2292, 2293, 2294, 2295):
    name_rva = struct.unpack("<Q", pe.get_data(name_base+8*index,8))[0]-base
    target = struct.unpack("<Q", pe.get_data(function_base+8*index,8))[0]-base
    name = pe.get_data(name_rva,120).split(b"\0")[0].decode("ascii")
    registrations.append(dict(index=index, name=name, nameRva=hex(name_rva),
        namePointerRva=hex(name_base+8*index), functionPointerRva=hex(function_base+8*index), targetRva=hex(target)))

selected = [
    (0xfd7bc0, "Icall parallel-table registration loop"),
    (0x100770, "StopCoroutine(string) binding"), (0x100d20, "StopAllCoroutines binding"),
    (0x1029f0, "StartCoroutineManaged2 binding and managed wrapper m_Ptr publication"),
    (0x102c60, "StopCoroutineManaged binding"), (0x102e70, "StopCoroutineFromEnumeratorManaged binding"),
    (0x792240, "Native IEnumerator start, 0x88 node, owner list registration and inline first execution"),
    (0x793840, "Exact native node detach and scheduler deletion"),
    (0x793a60, "Enumerator-to-native-node match and detach"),
    (0x78f5e0, "Scheduled callback owner check then execution"),
    (0x78f680, "Native node reference release, final cleanup and optional free"),
    (0x78f770, "Coroutine managed wrapper release: not cancellation completion"),
    (0x78f840, "Native coroutine execution reference and deferred exit"),
    (0x78fa30, "Native continuation scheduler registration"),
    (0x447ad0, "Scheduler tree erase and cleanup callback"),
    (0x775ad0, "Script reference constructor: handle/type plus cached actual managed reference"),
]
result = dict(schemaVersion=1, kind="fixed-unityplayer-cancellation-engine-research",
    scope="Bounded static disk reading only; no LoadLibrary, native execution, game, installation, EngineCancelled receipt or first-publication proof.",
    tools=dict(python=sys.version, pefile=pefile.__version__, capstone=capstone.__version__, idaCallableUsed=False),
    inputs=[dict(path=str(path), bytes=path.stat().st_size, sha256=hashlib.sha256(path.read_bytes()).hexdigest()) for path in (IMAGE,GAME)],
    pe=dict(machine=hex(pe.FILE_HEADER.Machine), imageBase=hex(base), imageSize=hex(pe.OPTIONAL_HEADER.SizeOfImage),
        sections=[dict(name=s.Name.decode().rstrip("\0"), rva=hex(s.VirtualAddress), rawBytes=s.SizeOfRawData) for s in pe.sections]),
    icallTable=dict(nameBaseRva=hex(name_base), functionBaseRva=hex(function_base), count=count,
        registrationFunctionRva="0xfd7bc0", rows=registrations),
    functions=[region(entry,label) for entry,label in selected],
    acceptedBoundaries=dict(engineCancellationDynamicallyVerified=False, actualIcallResolutionVerified=False,
        schedulerFullyEnumerated=False, executionDrainVerified=False, finalCleanupVerified=False,
        gameCodeExecuted=False, gameRuntimeVerified=False, firstInstallSafetyVerified=False))
out = HERE/"cancellation-engine-evidence.json"
out.write_text(json.dumps(result,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
print(json.dumps(dict(output=str(out), functions=len(result["functions"]), registrations=len(registrations),
    sha256=hashlib.sha256(out.read_bytes()).hexdigest(), gameCodeExecuted=False)))
