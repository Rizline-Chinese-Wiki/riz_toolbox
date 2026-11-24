"""Inspect built helper's actual machine code/unwind records and freeze fixture evidence."""
from pathlib import Path
from datetime import datetime, timezone
import hashlib
import json
import struct
import capstone
import pefile

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
SOURCE = REPO / 'native' / 'RizToolbox.JudgeContext'
OUT = SOURCE / 'out'
DLL = OUT / 'RizToolbox.JudgeContext.dll'
pe = pefile.PE(str(DLL))
md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
md.detail = True
exports = {s.name.decode(): s.address for s in pe.DIRECTORY_ENTRY_EXPORT.symbols}
assert set(exports) == {
    'RizJudgeContext_Configure', 'RizJudgeContext_GetEntry', 'RizJudgeContext_GetFrameSize',
    'RizJudgeContext_GetStats', 'RizJudgeContext_SetEnabled',
}
get_entry = list(md.disasm(pe.get_data(exports['RizJudgeContext_GetEntry'], 8), exports['RizJudgeContext_GetEntry']))
assert get_entry[0].mnemonic == 'lea' and get_entry[0].op_str.startswith('rax, [rip + ')
operand = get_entry[0].operands[1]
entry = get_entry[0].address + get_entry[0].size + operand.mem.disp
entry_runtime = next(f.struct for f in pe.DIRECTORY_ENTRY_EXCEPTION if f.struct.BeginAddress == entry)
instructions = list(md.disasm(pe.get_data(entry, entry_runtime.EndAddress-entry), entry))
assert (instructions[0].mnemonic, instructions[0].op_str) == ('sub', 'rsp, 0x1c8')
assert instructions[-2].mnemonic == 'add' and instructions[-2].op_str == 'rsp, 0x1c8'
assert instructions[-1].mnemonic == 'ret'
calls = [i for i in instructions if i.mnemonic == 'call']
assert len(calls) == 1
dispatch = calls[0].operands[0].imm
dispatch_runtime = next(f.struct for f in pe.DIRECTORY_ENTRY_EXCEPTION if f.struct.BeginAddress == dispatch)


def unwind(function):
    raw = pe.get_data(function.UnwindData, 4)
    version, flags = raw[0] & 7, raw[0] >> 3
    prologue, slots = raw[1], raw[2]
    data = pe.get_data(function.UnwindData+4, slots*2)
    codes = []
    index = 0
    while index < slots:
        code_offset, opcode = data[index*2:index*2+2]
        operation, info = opcode & 15, opcode >> 4
        index += 1
        row = {'codeOffset': code_offset, 'operation': operation, 'info': info}
        if operation in (4, 8):
            scale = 8 if operation == 4 else 16
            row['stackOffset'] = struct.unpack_from('<H', data, index*2)[0] * scale
            index += 1
        elif operation == 1:
            assert info == 0
            row['allocation'] = struct.unpack_from('<H', data, index*2)[0] * 8
            index += 1
        elif operation == 2:
            row['allocation'] = info*8+8
        else:
            assert operation == 0, operation
        codes.append(row)
    return {'beginRva': hex(function.BeginAddress), 'endRva': hex(function.EndAddress),
            'unwindRva': hex(function.UnwindData), 'version': version, 'flags': flags,
            'prologueSize': prologue, 'slots': slots, 'codes': codes}


entry_unwind = unwind(entry_runtime)
dispatch_unwind = unwind(dispatch_runtime)
assert entry_unwind['version'] == 1 and entry_unwind['flags'] == 0
assert entry_unwind['prologueSize'] < 256
assert {c['info']: c['stackOffset'] for c in entry_unwind['codes'] if c['operation'] == 4} == {
    3: 0x58, 5: 0x68, 6: 0x70, 7: 0x78, 12: 0xa0, 13: 0xa8, 14: 0xb0, 15: 0xb8,
}
assert {c['info']: c['stackOffset'] for c in entry_unwind['codes'] if c['operation'] == 8} == {
    n: 0x120+(n-6)*16 for n in range(6, 16)
}
assert [c['allocation'] for c in entry_unwind['codes'] if c['operation'] == 1] == [0x1c8]
assert dispatch_unwind['flags'] & 2 # real unwind termination handler, not just a stub
fixture = json.loads((OUT / 'fixture-result.json').read_text(encoding='utf-8-sig'))
assert fixture['passed'] and fixture['failures'] == 0 and fixture['inFlight'] == 0
assert fixture['gameRuntimeVerified'] is False
source_hashes = {}
for path in (SOURCE / 'JudgeContext.h', SOURCE / 'JudgeContext.cpp', SOURCE / 'JudgeEntry.asm',
             SOURCE / 'fixture' / 'Fixture.cpp', SOURCE / 'fixture' / 'FixtureCaller.asm',
             REPO / 'scripts' / 'Build-JudgeContext.ps1'):
    source_hashes[str(path.relative_to(REPO)).replace('\\', '/')] = hashlib.sha256(path.read_bytes()).hexdigest()
for name in ('fixture-result.json', 'native-manifest.json', 'exports.txt', 'unwindinfo.txt', 'build.log'):
    (ROOT / name).write_bytes((OUT / name).read_bytes())
proof = {
    'kind': 'native-compiled-machine-code-and-offline-fixture', 'gameRuntimeVerified': False,
    'observedAtUtc': datetime.now(timezone.utc).isoformat(), 'dll': str(DLL),
    'dllSha256': hashlib.sha256(DLL.read_bytes()).hexdigest(),
    'fixtureSha256': hashlib.sha256((OUT / 'JudgeContext.Fixture.exe').read_bytes()).hexdigest(),
    'sourceSha256': source_hashes, 'exports': {k: hex(v) for k,v in exports.items()},
    'entryRva': hex(entry), 'entryUnwind': entry_unwind, 'dispatchUnwind': dispatch_unwind,
    'guardFlags': hex(pe.DIRECTORY_ENTRY_LOAD_CONFIG.struct.GuardFlags),
    'fixture': fixture,
    'entryInstructions': [{'rva': hex(i.address), 'mnemonic': i.mnemonic, 'operands': i.op_str} for i in instructions],
    'limitations': ['not hooked into game', 'no Note identity capability', 'hook publication gate unresolved by this helper',
                    'hardware CET enforcement not established', 'callbacks and library require process-lifetime retention'],
}
target = ROOT / 'native-proof.json'
target.write_text(json.dumps(proof, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
print(json.dumps({'passed': True, 'entryRva': hex(entry), 'frameSize': '0x1a0', 'unwindSlots': entry_unwind['slots'],
                  'fixtureCases': len(fixture['cases']), 'gameRuntimeVerified': False,
                  'proofSha256': hashlib.sha256(target.read_bytes()).hexdigest()}))
