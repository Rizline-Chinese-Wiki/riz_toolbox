"""Fixed steam-25977441 transport facts: disk PE only; no game loading or writes.

Usage: python inspect-production-transport.py --image ABS --output-directory FRESH_ABS
Only the selected 13 function bodies are disassembled. Global .pdata traversal
resolves CHAININFO ownership; it does not search unrelated instructions/callers.
The emitted header describes original bytes and proposed bounded replay plans.
It cannot authorize Configure, relay publication, source writes, or production.
"""
import argparse
import bisect
import hashlib
import json
import os
from pathlib import Path
import struct
import sys
import traceback

import capstone
from capstone import x86
import pefile

IMAGE_SHA = '43fb0aea1b80c74963dae88ec7fc1c3c1993d893ce94d12972000fcc9b653ad1'
IMAGE_BYTES = 31006120
IMAGE_BASE = 0x180000000
IMAGE_SIZE = 0x1e65000
# label, begin, end(exclusive), RF count, full body SHA, shortest >=14 prefix
FUNCTIONS = [
    ('NodeStart', 0x792240, 0x7929bf, 1, '6a24dc40b10797a33529428b7d82821f1cb646a72d9108cccb884bfe2f8d7721', '48895c2408488974241848897c2420'),
    ('NodeExecute', 0x78f840, 0x78fa2e, 1, '1ffd5505fa6c15a9315524ad28f9846b8ba4ef04cbf9cf5c0a807fbd21a997f6', '405553574156488d6c24884881ec78010000'),
    ('NodeRelease', 0x78f680, 0x78f768, 3, '771128974dd1a81b49b7f6a9743a492a752cf30aa42bd95073df111cf159d1a3', '40534883ec20ff4960488bd983796000'),
    ('NodeDetach', 0x793840, 0x793a5b, 1, '942344fdd9c188c5e9b8371d6e03ce8fb8f944bbfcd520cadd3ef5715dc5edf7', '48896c2420574883ec20488bf933ed'),
    ('MRegister', 0x44d630, 0x44d756, 1, 'ce57348a38b6eb9041cece746e038dddd5c676721f39c4c2f857669c03b74f20', '48895c2408488974241048897c2418'),
    ('MRemove', 0x447ad0, 0x447c26, 1, '3586c1418372fd581f21df83addb7ec59256794fc52374d0fc55593a732758ff', '48895c2408574883ec20488b4140'),
    ('MConsume', 0x447c30, 0x447d60, 1, '400ac94b4e996135f4a5602be15a0bcebf3f123e8f650b4dbb003544b6225abb', '4883ec28488b4140488bd1493bc0'),
    ('MPump', 0x447d60, 0x447faf, 3, 'cafab343388f190607027203284728c3ce672cfe89115a50257e0faae66e3d7c', '8954241053555741554883ec788bea'),
    ('MDrain', 0x447750, 0x4478ac, 3, '6cdea79f34ee8ddcec81015abe94c299df9732c54327e10cc0ba94040b9f435d', '40554883ec304883793800488be9'),
    ('MCallback', 0x78f5e0, 0x78f673, 1, '0cb5566bcbbe2510032fe522b586bd1940f621ba1e41ba2c93dd2a67098beafc', '4c8bdc4881ec98000000488bc248394a58'),
    ('NYield', 0x78fc50, 0x79014b, 3, '938b7a48664df2e0b0195fa2075f130f285a9498e0ce87693269dc2c6db3696f', '48895c2408488974241848897c2420'),
    ('NDispatch', 0x5a5260, 0x5a5343, 1, '7143eab8a6af6b584a341b1b41857b7ce9f74a4108987508ca803bdab1d11ccf', '40574881ec500100004883791000'),
    ('NBatch', 0x5da510, 0x5da6db, 7, '01f8cbd43685808116a8c8e34fe4cf7053de54595988ae8ade76676b351d6657', '48895c241856415641574883ec40'),
]
# family 0 Inline/1 Scheduler/2 Wait, family index, name, observation PC,
# minimal span, logical continuation, branch destination, exact minimal bytes.
# RIP-MOV windows also receive exact operand checks below.
SITES = [
    (0, 0, 'InlineBegin', 0x7924cd, 7, 0x7924d4, 0, 'ff4b60488b4b68'),
    (0, 1, 'InlineEndFree', 0x79257d, 5, 0x79299e, 0x79299e, 'e91c040000'),
    (0, 2, 'InlineEndRetained', 0x79259d, 5, 0x79299e, 0x79299e, 'e9fc030000'),
    (1, 0, 'DrainRowBegin', 0x447787, 6, 0x44778d, 0x4477f1, 'f64354407464'),
    (1, 1, 'DrainRowEnd', 0x447866, 5, 0x44786b, 0, '48837d3800'),
    (2, 0, 'PublishBegin', 0x790069, 10, 0x790073, 0, '895f28488d056df5ffff'),
    (2, 1, 'OldReferenceDecision', 0x79008d, 6, 0x790093, 0x790098, '74094883c108'),
    (2, 2, 'OldReferenceReturned', 0x790098, 8, 0x7900a0, 0, '49897e78f0ff470c'),
    (2, 3, 'PublishCommitted', 0x7900a0, 5, 0x79012e, 0x79012e, 'e989000000'),
    (2, 4, 'DetachOwnerBegin', 0x7938b3, 10, 0x7938bd, 0, '896828488b3543035201'),
    (2, 5, 'DetachNestedOwnerBegin', 0x793983, 10, 0x79398d, 0, '896828488b3573025201'),
    (2, 6, 'ReleaseClearBegin', 0x78f6f5, 7, 0x78f6fc, 0, '89782848897810'),
    (2, 7, 'ReleaseFieldsCleared', 0x78f704, 8, 0x78f70c, 0, '488b4b784883c108'),
    (2, 8, 'ReleaseReferenceReturned', 0x78f711, 8, 0x78f719, 0, '48897b7848397b10'),
    (2, 9, 'StartClearBegin', 0x792527, 7, 0x79252e, 0, '89782848897810'),
    (2, 10, 'StartFieldsCleared', 0x792536, 8, 0x79253e, 0, '488b4b784883c108'),
    (2, 11, 'StartReferenceReturned', 0x792543, 9, 0x79254c, 0, '48897b7848837b1000'),
    (2, 12, 'DispatchOwnerResult', 0x5a527c, 5, 0x5a5281, 0x5a528b, '4885c0740a'),
    (2, 13, 'DispatchCallbackArmed', 0x5a5281, 7, 0x5a5288, 0, '488b5720488bc8'),
    (2, 14, 'DispatchCleanupDecision', 0x5a528f, 5, 0x5a5294, 0x5a529a, '4885c07406'),
    (2, 15, 'DispatchClearReached', 0x5a529a, 8, 0x5a52a2, 0, '48c7471000000000'),
    (2, 16, 'BatchObjectBegin', 0x5da63b, 6, 0x5da641, 0x5da64c, '4c397510740b'),
    (2, 17, 'BatchObjectEnd', 0x5da660, 10, 0x5da66a, 0, '48ffc7483bbea0010000'),
]
# logical PC, physical begin/end, original JE/zero target, zero-only replay
# begin/end/continuation. Nonzero continues at the physical block end.
EXPANSIONS = [
    (0x7938b3, 0x7938aa, 0x7938bd, 0x7938b1, 0x7938b6, 0x7938b6, 0x7938bd, 0x7938bd),
    (0x78f6f5, 0x78f6f0, 0x78f6fc, 0x78f6f3, 0x78f715, 0x78f715, 0x78f719, 0x78f719),
    (0x792527, 0x792522, 0x79252e, 0x792525, 0x792547, 0x792547, 0x79254c, 0x79254c),
]
PRESERVED_CALLS = [
    ('OldWaitReference', 0x790093, 'e86823bbff', 0x790098, 0x342400, True),
    ('ReleaseWaitReference', 0x78f70c, 'e8ef2cbbff', 0x78f711, 0x342400, True),
    ('StartWaitReference', 0x79253e, 'e8bdfebaff', 0x792543, 0x342400, True),
    ('DispatchCallback', 0x5a5288, 'ff5710', 0x5a528b, 0, True),
    ('DispatchCleanup', 0x5a5298, 'ffd0', 0x5a529a, 0, True),
    ('BatchCleanup', 0x5da645, 'ff5518', 0x5da648, 0, True),
    ('BatchWaitReference', 0x5da65b, 'e8a07dd6ff', 0x5da660, 0x342400, False),
    ('DispatchOwnerLookup', 0x5a5277, 'e8c4d2baff', 0x5a527c, 0x152540, False),
]
FALSE_BOUNDARIES = {
    'gameRuntimeVerified': False, 'gameExecuted': False, 'installed': False,
    'productionAdmissionVerified': False, 'productionFirstInstallAuthorized': False,
    'completeStartupAdmissionKnown': False, 'sourceWritesPerformed': False,
    'relaysInstalled': False, 'runtimeUnwindVerified': False,
    'allIndirectCallersClosed': False, 'allWritersVerified': False,
    'allDispatchersVerified': False, 'futureProducersClosed': False,
    'cancellationVerified': False, 'engineCancelled': False,
}


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


class Checks:
    def __init__(self):
        self.count = 0
        self.last = None

    def require(self, condition, label):
        self.count += 1
        self.last = label
        if not condition:
            raise ValueError(label)


def file_identity(path):
    path = Path(path).resolve(strict=True)
    raw = path.read_bytes()
    return {'path': str(path), 'bytes': len(raw), 'sha256': sha(raw)}


def atomic_new(path, raw):
    if path.exists():
        raise ValueError('Refusing to overwrite output: ' + str(path))
    temporary = path.with_name(path.name + '.writing')
    with temporary.open('xb') as stream:
        stream.write(raw)
        stream.flush()
        os.fsync(stream.fileno())
    if temporary.read_bytes() != raw:
        raise ValueError('Output byte readback differs: ' + str(path))
    # This invocation newly and exclusively created the output directory.
    temporary.rename(path)


class DiskImage:
    def __init__(self, source, check):
        self.source, self.check = source, check
        self.raw = source.read_bytes()
        check.require(len(self.raw) == IMAGE_BYTES, 'Fixed input file byte count')
        check.require(sha(self.raw) == IMAGE_SHA, 'Fixed input whole-file SHA256')
        self.pe = pefile.PE(data=self.raw, fast_load=True)
        p = self.pe
        check.require(p.FILE_HEADER.Machine == 0x8664, 'AMD64 machine')
        check.require(p.FILE_HEADER.Characteristics & 0x2000 != 0, 'Input is DLL')
        check.require(p.OPTIONAL_HEADER.Magic == 0x20b, 'PE32+')
        check.require(p.OPTIONAL_HEADER.ImageBase == IMAGE_BASE, 'Fixed preferred base')
        check.require(p.OPTIONAL_HEADER.SizeOfImage == IMAGE_SIZE, 'Fixed image size')
        check.require(p.FILE_HEADER.TimeDateStamp == 0x68d678af, 'Fixed COFF timestamp')
        check.require(p.OPTIONAL_HEADER.AddressOfEntryPoint == 0x17e7c08, 'Fixed original entry RVA')
        self.directory = p.OPTIONAL_HEADER.DATA_DIRECTORY[3]
        check.require((self.directory.VirtualAddress, self.directory.Size) == (0x1d21000, 0x112b00), 'Fixed exception directory identity')
        data = self.read(self.directory.VirtualAddress, self.directory.Size)
        check.require(len(data) % 12 == 0 and len(data) != 0, 'Bounded whole RUNTIME_FUNCTION table')
        self.rows = [struct.unpack_from('<III', data, at) for at in range(0, len(data), 12)]
        check.require(self.rows == sorted(self.rows), 'Sorted .pdata tuples')
        check.require(len(set(self.rows)) == len(self.rows), 'Unique .pdata tuples')
        for low, high, unwind in self.rows:
            check.require(0 <= low < high <= IMAGE_SIZE and 0 < unwind < IMAGE_SIZE, 'Bounded RUNTIME_FUNCTION tuple')
        self.row_set = set(self.rows)
        self.starts = [row[0] for row in self.rows]
        self.unwinds = {}
        self.roots = {}
        for row in self.rows:
            self.root(row)

    def read(self, rva, size, rx=False):
        c = self.check
        c.require(isinstance(rva, int) and isinstance(size, int) and size > 0 and 0 <= rva < rva + size <= IMAGE_SIZE, 'Bounded nonempty RVA span')
        section = self.pe.get_section_by_rva(rva)
        c.require(section is not None, 'RVA belongs to a real section')
        delta = rva - section.VirtualAddress
        c.require(delta >= 0 and delta + size <= section.SizeOfRawData, 'RVA span has full file backing')
        offset = section.PointerToRawData + delta
        c.require(0 <= offset < offset + size <= len(self.raw), 'RVA span within disk bytes')
        if rx:
            c.require(section.Characteristics & 0x20000000 != 0 and section.Characteristics & 0x80000000 == 0, 'Original code is executable and nonwritable')
        return self.raw[offset:offset + size]

    def unwind(self, rva):
        if rva in self.unwinds:
            return self.unwinds[rva]
        c = self.check
        vf, prolog, slots, frame = self.read(rva, 4)
        version, flags = vf & 7, vf >> 3
        c.require(version in (1, 2) and flags & ~7 == 0, 'Known bounded unwind header')
        c.require(not (flags & 4 and flags & 3), 'CHAININFO has no handler flags')
        encoded = 4 + ((slots + 1) & ~1) * 2
        size = encoded + (12 if flags & 4 else 4 if flags & 3 else 0)
        raw = self.read(rva, size)
        chain = list(struct.unpack_from('<III', raw, encoded)) if flags & 4 else [0, 0, 0]
        handler = struct.unpack_from('<I', raw, encoded)[0] if flags & 3 else 0
        if flags & 4:
            c.require(tuple(chain) in self.row_set, 'CHAININFO names exact .pdata tuple')
        if flags & 3:
            self.read(handler, 1, rx=True)
        value = {'rva': rva, 'byteCount': size, 'version': version, 'flags': flags,
                 'prologBytes': prolog, 'codeSlots': slots, 'frameRegister': frame & 15,
                 'frameOffsetBytes': (frame >> 4) * 16, 'chain': chain,
                 'handlerRva': handler, 'raw': raw.hex(), 'sha256': sha(raw),
                 'handlerSpecificDataDecoded': False}
        self.unwinds[rva] = value
        return value

    def root(self, row):
        if row in self.roots:
            return self.roots[row]
        current, visited = row, []
        for _ in range(16):
            self.check.require(current not in visited, 'No CHAININFO cycle')
            visited.append(current)
            if current in self.roots:
                root = self.roots[current]
                break
            data = self.unwind(current[2])
            if not data['flags'] & 4:
                root = current
                break
            current = tuple(data['chain'])
        else:
            raise ValueError('CHAININFO exceeds depth 16')
        for item in visited:
            self.roots[item] = root
        return root

    def chain(self, row):
        result, visited = [], set()
        for _ in range(16):
            self.check.require(row not in visited, 'Selected CHAININFO has no cycle')
            visited.add(row)
            result.append({'runtimeFunction': list(row), 'unwindRva': row[2]})
            data = self.unwind(row[2])
            if not data['flags'] & 4:
                return result
            row = tuple(data['chain'])
        raise ValueError('Selected CHAININFO exceeds depth 16')

    def containing(self, rva):
        i = bisect.bisect_right(self.starts, rva) - 1
        self.check.require(i >= 0 and self.rows[i][0] <= rva < self.rows[i][1], 'Selected PC has exact .pdata owner')
        return self.rows[i]


def decode_unwind_codes(value, check):
    """Validate selected slot stream; preserve actual SET_FPREG operation info."""
    raw, count = bytes.fromhex(value['raw']), value['codeSlots']
    result, slot = [], 0
    while slot < count:
        offset, opbyte = raw[4 + slot * 2:6 + slot * 2]
        opcode, info = opbyte & 15, opbyte >> 4
        check.require(offset <= value['prologBytes'] or value['version'] == 2, 'Unwind code offset bounded by prolog')
        if opcode == 1:
            check.require(info in (0, 1), 'ALLOC_LARGE encoding')
            additional = 1 if info == 0 else 2
        elif opcode in (4, 8):
            additional = 1
        elif opcode in (5, 9):
            additional = 2
        elif opcode in (0, 2, 3, 10):
            additional = 0
        elif opcode in (6, 7) and value['version'] == 2:
            # Preserve version-2 epilog slot without a new semantic claim.
            additional = 0
        else:
            raise ValueError('Unsupported selected unwind opcode ' + str(opcode))
        check.require(slot + additional < count, 'Unwind operand slots fully encoded')
        if opcode == 3:
            check.require(value['frameRegister'] != 0 and info in (0, value['frameRegister']), 'SET_FPREG actual frame register descriptor')
        if opcode == 10:
            check.require(info <= 1, 'PUSH_MACHFRAME descriptor')
        result.append({'slot': slot, 'codeOffset': offset, 'opcode': opcode,
                       'rawOperationInfo': info, 'operandSlots': additional,
                       'raw': raw[4 + slot * 2:6 + (slot + additional) * 2].hex()})
        slot += 1 + additional
    check.require(slot == count, 'Complete selected unwind slot stream')
    return result


def instruction_fact(ins):
    reads, writes = ins.regs_access()
    memories = []
    for operand in ins.operands:
        if operand.type == capstone.CS_OP_MEM:
            memories.append({'base': ins.reg_name(operand.mem.base),
                             'index': ins.reg_name(operand.mem.index),
                             'scale': operand.mem.scale, 'displacement': operand.mem.disp,
                             'access': operand.access, 'size': operand.size})
    control = ins.group(capstone.CS_GRP_CALL) or ins.group(capstone.CS_GRP_JUMP)
    direct = ins.operands[0].imm if control and ins.operands and ins.operands[0].type == capstone.CS_OP_IMM else None
    rip_targets = [ins.address + ins.size + operand.mem.disp for operand in ins.operands
                   if operand.type == capstone.CS_OP_MEM and operand.mem.base == x86.X86_REG_RIP]
    return {'rva': ins.address, 'size': ins.size, 'bytes': ins.bytes.hex(),
            'mnemonic': ins.mnemonic, 'operands': ins.op_str,
            'registerReads': [ins.reg_name(reg) for reg in reads],
            'registerWrites': [ins.reg_name(reg) for reg in writes],
            'memoryOperands': memories, 'eflagsMetadata': ins.eflags,
            'isCall': ins.group(capstone.CS_GRP_CALL),
            'isJump': ins.group(capstone.CS_GRP_JUMP),
            'directTargetRva': direct, 'ripAddressRvas': rip_targets,
            'returnPcRva': ins.address + ins.size if ins.group(capstone.CS_GRP_CALL) else None}


def inspect(image, check):
    md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
    md.detail = True
    md.skipdata = False
    functions, decoded, ins_objects, selected_unwinds = [], {}, {}, {}
    text = ['Fixed UnityPlayer disk disassembly; only 13 selected complete functions.',
            'No game execution, relay publication, runtime-unwind or production admission.']
    for kind, (name, begin, end, expected_count, expected_sha, prefix_hex) in enumerate(FUNCTIONS):
        root = image.root(image.containing(begin))
        group = [row for row in image.rows if image.roots[row] == root]
        check.require(len(group) == expected_count, name + ' exact RF count')
        merged = []
        for low, high, _ in sorted(group):
            if merged and low <= merged[-1][1]:
                merged[-1][1] = max(merged[-1][1], high)
            else:
                merged.append([low, high])
        check.require(merged == [[begin, end]], name + ' complete CHAININFO body extent')
        body = image.read(begin, end - begin, rx=True)
        check.require(sha(body) == expected_sha, name + ' full body SHA256')
        instructions = list(md.disasm(body, begin))
        check.require(instructions and sum(item.size for item in instructions) == len(body), name + ' every body byte decoded')
        position, prefix_end = begin, None
        rows = []
        text.append('\n## ' + name + f' {begin:08x}..{end:08x} bodySHA={sha(body)}')
        for ins in instructions:
            check.require(ins.address == position, name + ' contiguous instruction decoding')
            position += ins.size
            if prefix_end is None and position - begin >= 14:
                prefix_end = position
            fact = instruction_fact(ins)
            rows.append(fact)
            check.require(ins.address not in decoded, 'Selected complete functions do not overlap')
            decoded[ins.address], ins_objects[ins.address] = fact, ins
            text.append(f'{ins.address:08x} {ins.bytes.hex():<32} {ins.mnemonic:<10} {ins.op_str}')
        check.require(position == end, name + ' exact final instruction boundary')
        expected_prefix = bytes.fromhex(prefix_hex)
        check.require(prefix_end == begin + len(expected_prefix) and body[:len(expected_prefix)] == expected_prefix,
                      name + ' exact shortest whole >=14 prefix')
        check.require(prefix_end in {item.address for item in instructions}, name + ' continuation is real body boundary')
        metadata = []
        for row in group:
            chain = image.chain(row)
            for link in chain:
                unwind = image.unwind(link['unwindRva'])
                if unwind['rva'] not in selected_unwinds:
                    value = dict(unwind)
                    value['operations'] = decode_unwind_codes(unwind, check)
                    selected_unwinds[value['rva']] = value
            metadata.append({'beginRva': row[0], 'endRva': row[1], 'unwindRva': row[2], 'chain': chain})
        functions.append({'index': kind, 'name': name, 'beginRva': begin, 'endRva': end,
                          'bodyBytes': len(body), 'body': body.hex(), 'bodySha256': sha(body),
                          'prefixBytes': len(expected_prefix), 'prefix': expected_prefix.hex(),
                          'prefixSha256': sha(expected_prefix), 'continuationRva': prefix_end,
                          'primaryRuntimeFunction': list(root), 'runtimeFunctions': metadata,
                          'instructions': rows,
                          'returnCarrier': ['AL', 'EAX', 'AL', 'void'][kind] if kind < 4 else 'full raw RAX; source-language type unproved'})

    def owner(rva):
        matches = [f['index'] for f in functions if f['beginRva'] <= rva < f['endRva']]
        check.require(len(matches) == 1, 'Exactly one selected full body owns PC')
        return matches[0]

    def window(rva, size):
        begin, position = rva, rva
        rows = []
        while position < begin + size:
            check.require(position in decoded, 'Window starts on decoded instruction boundary')
            row = decoded[position]
            rows.append(row)
            position += row['size']
        check.require(position == begin + size, 'Window ends on whole instruction boundary')
        index = owner(begin)
        check.require(owner(position - 1) == index, 'Window stays within complete selected function')
        raw = image.read(begin, size, rx=True)
        rf = image.containing(begin)
        check.require(position <= rf[1], 'Physical window stays in one source RUNTIME_FUNCTION')
        return {'beginRva': begin, 'endRva': position, 'byteCount': size,
                'bytes': raw.hex(), 'sha256': sha(raw), 'functionIndex': index,
                'runtimeFunction': list(rf), 'runtimeFunctionChain': image.chain(rf),
                'instructions': rows}

    expansions = {row[0]: row for row in EXPANSIONS}
    sites, physical = [], []
    for function in functions:
        fact = window(function['beginRva'], function['prefixBytes'])
        fact.update({'index': len(physical), 'role': 'TypedEntry', 'roleIndex': function['index'],
                     'observationRva': function['beginRva']})
        physical.append(fact)
    for index, (family, family_index, name, pc, span, continuation, branch, fixed) in enumerate(SITES):
        logical = window(pc, span)
        if fixed is not None:
            check.require(logical['bytes'] == fixed, name + ' independent exact window bytes')
        if name in ('DetachOwnerBegin', 'DetachNestedOwnerBegin'):
            instructions = logical['instructions']
            check.require(len(instructions) == 2 and instructions[0]['bytes'] == '896828', name + ' exact owner store')
            second = ins_objects[instructions[1]['rva']]
            check.require(second.mnemonic == 'mov' and second.operands[0].type == capstone.CS_OP_REG
                          and second.operands[0].reg == x86.X86_REG_RSI and len(instructions[1]['ripAddressRvas']) == 1,
                          name + ' exact original RIP MOV instruction')
        check.require(continuation in decoded, name + ' original continuation boundary')
        if branch:
            branches = [row for row in logical['instructions'] if row['isJump']]
            check.require(len(branches) == 1 and branches[0]['directTargetRva'] == branch,
                          name + ' fixed branch destination')
            check.require(branch in decoded, name + ' branch destination is selected instruction boundary')
        expanded = expansions.get(pc)
        start, finish = (expanded[1], expanded[2]) if expanded else (pc, pc + span)
        patch = window(start, finish - start)
        patch.update({'index': len(physical), 'role': 'SideSite', 'roleIndex': index, 'observationRva': pc})
        physical.append(patch)
        sites.append({'index': index, 'family': family, 'familyIndex': family_index,
                      'name': name, 'observationRva': pc, 'logicalWindow': logical,
                      'patchRva': start, 'patchSpanBytes': finish - start,
                      'physicalWindowIndex': patch['index'],
                      'physicalContinuationRva': finish if expanded else continuation,
                      'logicalContinuationRva': continuation, 'branchTargetRva': branch,
                      'expandedPhysicalBlock': bool(expanded), 'relayReturnPcKnown': False,
                      'relayCodeGenerated': False, 'runtimeUnwindVerified': False})
    check.require(len(sites) == 23 and len(physical) == 36, 'Exact 23 logical and 36 physical source windows')
    ordered = sorted(physical, key=lambda row: row['beginRva'])
    for left, right in zip(ordered, ordered[1:]):
        check.require(left['endRva'] <= right['beginRva'], 'All proposed physical source windows are nonoverlapping')

    def interior_edges(windows):
        result = []
        for row in decoded.values():
            target = row['directTargetRva']
            if target is None:
                continue
            for candidate in windows:
                if candidate['beginRva'] < target < candidate['endRva']:
                    check.require(target in decoded, 'Direct interior edge targets real instruction boundary')
                    result.append({'branchRva': row['rva'], 'branchBytes': row['bytes'],
                                   'mnemonic': row['mnemonic'], 'targetRva': target,
                                   'targetWindowBeginRva': candidate['beginRva'],
                                   'targetWindowEndRva': candidate['endRva'],
                                   'targetOffset': target - candidate['beginRva']})
        return sorted(result, key=lambda row: (row['branchRva'], row['targetRva']))

    min_windows = [window(f['beginRva'], f['prefixBytes']) for f in functions]
    min_windows.extend(site['logicalWindow'] for site in sites)
    hazards = interior_edges(min_windows)
    expected_edges = {(0x7938b1, 0x7938b6, 0x7938b3),
                      (0x78f6f3, 0x78f715, 0x78f711),
                      (0x792525, 0x792547, 0x792543)}
    check.require(len(hazards) == 3 and {(h['branchRva'], h['targetRva'], h['targetWindowBeginRva']) for h in hazards} == expected_edges,
                  'Exact three covered-tail hazards in bounded selected direct-edge set')
    bypasses = []
    for index, (pc, start, finish, branch_pc, zero_target, replay_begin, replay_end, zero_continue) in enumerate(EXPANSIONS):
        row = decoded[branch_pc]
        check.require(row['mnemonic'] == 'je' and row['directTargetRva'] == zero_target,
                      'Expanded block original zero JE semantics')
        check.require(row['bytes'] == ('7403' if index == 0 else '7420'), 'Expanded block exact original branch bytes')
        block = window(start, finish - start)
        check.require(block['byteCount'] == (19 if index == 0 else 12), 'Exact expanded physical span')
        replay = window(replay_begin, replay_end - replay_begin)
        if index != 0:
            check.require(len(replay['instructions']) == 1 and replay['instructions'][0]['mnemonic'] == 'cmp', 'Zero path is CMP-only replay')
            check.require(replay['bytes'] == ('48397b10' if index == 1 else '48837b1000'), 'Exact zero-only CMP bytes')
        check.require(zero_continue in decoded and finish in decoded, 'Both expanded relay path continuations are original boundaries')
        bypasses.append({'index': index, 'observationRva': pc, 'physicalBlock': block,
                         'originalBranchRva': branch_pc, 'originalBranchBytes': row['bytes'],
                         'originalZeroTargetRva': zero_target, 'zeroReplay': replay,
                         'zeroContinuationRva': zero_continue, 'nonzeroContinuationRva': finish,
                         'nonzeroProbeBeforeRva': pc, 'zeroSkipsProbe': True,
                         'zeroSkipsOwnerOrNodeWaitStore': True,
                         'zeroSkipsReferenceReturnedProbe': index != 0,
                         'commonRipMovReplayed': index == 0,
                         'relayControlFlowImplementationVerified': False})
    physical_hazards = interior_edges(physical)
    check.require(len(physical_hazards) == 3 and {(h['branchRva'], h['targetRva']) for h in physical_hazards}
                  == {(row[3], row[4]) for row in EXPANSIONS}, 'Physical interior edges all require explicit three relay CFG rewrites')

    calls = []
    for name, pc, raw, returned, target, required in PRESERVED_CALLS:
        row = decoded[pc]
        check.require(row['isCall'] and row['bytes'] == raw and row['returnPcRva'] == returned,
                      name + ' exact original CALL and return PC')
        check.require(row['directTargetRva'] == (target or None), name + ' original direct/indirect target')
        check.require(returned in decoded, name + ' return PC original instruction boundary')
        check.require(all(pc + row['size'] <= p['beginRva'] or pc >= p['endRva'] for p in physical),
                      name + ' CALL remains outside every proposed source write')
        calls.append({'name': name, 'callRva': pc, 'bytes': raw, 'byteCount': row['size'],
                      'returnPcRva': returned, 'directTargetRva': target or None,
                      'operands': row['operands'], 'requiredSixCallSet': required,
                      'mustRemainAtOriginalSourceRva': True, 'returnSlotRewritePermitted': False})

    callback = functions[9]
    r10_aliases = {'r10', 'r10d', 'r10w', 'r10b'}
    r11_aliases = {'r11', 'r11d', 'r11w', 'r11b'}
    callback_r11_uses = []
    for row in callback['instructions']:
        ins = ins_objects[row['rva']]
        used = set(row['registerReads']) | set(row['registerWrites'])
        for operand in ins.operands:
            if operand.type == capstone.CS_OP_REG:
                used.add(ins.reg_name(operand.reg))
            elif operand.type == capstone.CS_OP_MEM:
                used.add(ins.reg_name(operand.mem.base))
                used.add(ins.reg_name(operand.mem.index))
        check.require(not (used & r10_aliases), 'Callback entire body has no explicit/implicit/memory R10 alias use')
        if used & r11_aliases:
            callback_r11_uses.append(row['rva'])
    check.require(any(pc >= callback['continuationRva'] for pc in callback_r11_uses), 'Callback R11 remains live beyond copied prefix')
    check.require(any(0x78f5fc <= row['rva'] <= 0x78f64c and any(m['base'] == 'r11' for m in row['memoryOperands'])
                      for row in callback['instructions']), 'Callback fixed downstream R11 stack addressing witness')
    check.require(sha(image.source.read_bytes()) == IMAGE_SHA, 'Input unchanged after complete disk inspection')
    for f in functions:
        text.append(f"\n# {f['name']} runtime-function/CHAININFO records")
        for row in f['runtimeFunctions']:
            text.append(json.dumps(row, sort_keys=True))
    for value in sorted(selected_unwinds.values(), key=lambda row: row['rva']):
        text.append('\n# UNWIND ' + json.dumps(value, sort_keys=True))
    result = {'buildId': 'steam-25977441', 'image': {
        'path': str(image.source), 'fileBytes': IMAGE_BYTES, 'sha256': IMAGE_SHA,
        'machine': 0x8664, 'optionalHeaderMagic': 0x20b, 'preferredImageBase': IMAGE_BASE,
        'imageSize': IMAGE_SIZE, 'timeDateStamp': image.pe.FILE_HEADER.TimeDateStamp,
        'entryRva': image.pe.OPTIONAL_HEADER.AddressOfEntryPoint,
        'exceptionRva': image.directory.VirtualAddress, 'exceptionBytes': image.directory.Size},
        'functions': functions, 'unwinds': sorted(selected_unwinds.values(), key=lambda row: row['rva']),
        'sites': sites, 'physicalWindows': physical, 'conditionalBypasses': bypasses,
        'preservedCalls': calls, 'allSelectedCallInstructions': [row for row in decoded.values() if row['isCall']],
        'directInteriorEdgesBeforeExpansion': hazards,
        'directInteriorEdgesRequiringRelayRewriteAfterExpansion': physical_hazards,
        'directEdgeScanScope': 'Only direct CALL/JMP/Jcc instructions in the 13 complete selected bodies; no external or indirect caller closure claim.',
        'callbackScratchRegisterFacts': {'wholeBodyR10AliasAbsent': True,
            'checkedAliases': sorted(r10_aliases), 'registerAccessSource': 'Capstone regs_access plus every explicit register and memory base/index',
            'r11UseRvas': callback_r11_uses, 'r11LiveAfterPrefix': True,
            'proposedTailScratch': 'r10', 'proposedTailRuntimeUnwindVerified': False},
        'counts': {'completeFunctions': 13, 'functionRuntimeFunctionRows': sum(len(f['runtimeFunctions']) for f in functions),
                   'instructions': len(decoded), 'logicalSites': 23, 'physicalSourceWindows': 36,
                   'enlargedPhysicalBlocks': 3, 'requiredOriginalCalls': 6,
                   'additionalPreservedOriginalCalls': 2, 'configureFamilies': 12},
        'limitations': ['No handler-specific exception-language data interpretation.',
                        'Original unwind bytes and chains are disk identities, not a proof of new relay/stub unwind.',
                        'Three replay/bypass records are plans, not generated or executed relay code.',
                        'No external/indirect entry, all-writer, all-dispatcher, future-source, cancellation, or first-execution-window closure.'],
        'boundaries': dict(FALSE_BOUNDARIES)}
    return result, '\n'.join(text) + '\n'


def generate_header(facts, facts_sha, inspector_sha):
    lines = ['#pragma once', '#include <cstddef>', '#include <cstdint>',
             '// Generated exclusively by inspect-production-transport.py from the pinned disk PE.',
             '// Facts only. No Configure, Attach, source write, relay publication, or Ready authority.',
             '// Handler-specific LSDA is not interpreted; new-stub/relay runtime unwind is not proved.',
             'namespace RizEngineTransportGameProfile {',
             'inline constexpr std::uint32_t Version = 1;',
             'inline constexpr bool FactsOnly = true;',
             'inline constexpr bool ProductionFirstInstallAuthorized = false;',
             'inline constexpr bool ProductionAdmissionVerified = false, CompleteStartupAdmissionKnown = false;',
             'inline constexpr bool GameRuntimeVerified = false, CancellationVerified = false;',
             'inline constexpr bool AllWritersVerified = false, AllDispatchersVerified = false, FutureProducersClosed = false;',
             'inline constexpr bool RuntimeUnwindVerified = false, RelaysInstalled = false;',
             f'inline constexpr char FactsSha256[] = "{facts_sha}";',
             f'inline constexpr char InspectorSha256[] = "{inspector_sha}";',
             'inline constexpr char BuildId[] = "steam-25977441";',
             'inline constexpr wchar_t UnityModule[] = L"UnityPlayer.dll";',
             f'inline constexpr char UnitySha256[] = "{IMAGE_SHA}";',
             f'inline constexpr std::uint64_t UnityFileBytes = {IMAGE_BYTES}ull, PreferredImageBase = 0x{IMAGE_BASE:x}ull;',
             f'inline constexpr std::uint32_t ImageSize = 0x{IMAGE_SIZE:x}, Machine = 0x8664;',
             f"inline constexpr std::uint32_t TimeDateStamp = 0x{facts['image']['timeDateStamp']:x}, EntryRva = 0x{facts['image']['entryRva']:x};",
             f"inline constexpr std::uint32_t ExceptionRva = 0x{facts['image']['exceptionRva']:x}, ExceptionBytes = 0x{facts['image']['exceptionBytes']:x};",
             'inline constexpr std::uint32_t TypedCount = 13, SiteCount = 23, PhysicalWindowCount = 36, ConfigureFamilyCount = 12;',
             'struct RuntimeFunction { std::uint32_t beginRva, endRva, unwindRva; };',
             'struct UnwindRecord { std::uint32_t rva, byteCount, version, flags, prologBytes, codeSlots, frameRegister, frameOffsetBytes;',
             '    RuntimeFunction chain; std::uint32_t handlerRva; const unsigned char* raw; const char* sha256; };',
             'struct Function { const char* name; std::uint32_t index, beginRva, endRva, prefixBytes, bodyBytes, continuationRva, runtimeFunctionCount;',
             '    const unsigned char* prefix; const unsigned char* body; const char* bodySha256; const RuntimeFunction* runtimeFunctions; };',
             'struct PhysicalWindow { std::uint32_t index, role, roleIndex, observationRva, beginRva, endRva, byteCount, functionIndex;',
             '    RuntimeFunction runtimeFunction; const unsigned char* bytes; const char* sha256; };',
             'struct Site { const char* name; std::uint32_t index, family, familyIndex, observationRva, logicalSpanBytes, patchRva, patchSpanBytes,',
             '    logicalContinuationRva, physicalContinuationRva, branchTargetRva, physicalWindowIndex; bool expandedPhysicalBlock; };',
             'struct ConditionalBypass { std::uint32_t observationRva, patchRva, patchBytes, originalBranchRva, originalZeroTargetRva,',
             '    zeroReplayRva, zeroReplayBytes, zeroContinuationRva, nonzeroContinuationRva; const unsigned char* zeroReplay;',
             '    bool skipsReferenceReturnedProbe, commonRipMov; };',
             'struct PreservedCall { const char* name; std::uint32_t callRva, byteCount, returnPcRva, directTargetRva;',
             '    const unsigned char* bytes; bool requiredSixCallSet; };']

    def blob(name, value):
        raw = bytes.fromhex(value)
        lines.append('inline constexpr unsigned char ' + name + '[]{')
        for offset in range(0, len(raw), 16):
            lines.append('    ' + ', '.join(f'0x{b:02x}' for b in raw[offset:offset + 16]) + ',')
        lines.append('};')

    def triple(row):
        return '{' + ', '.join(f'0x{value:x}' for value in row) + '}'

    for value in facts['unwinds']:
        blob(f"Unwind_{value['rva']:x}", value['raw'])
    lines.append('inline constexpr UnwindRecord Unwinds[]{')
    for value in facts['unwinds']:
        fields = [value[key] for key in ('rva', 'byteCount', 'version', 'flags', 'prologBytes', 'codeSlots', 'frameRegister', 'frameOffsetBytes')]
        lines.append('    {' + ', '.join(f'0x{x:x}' for x in fields) + ', ' + triple(value['chain'])
                     + f", 0x{value['handlerRva']:x}, Unwind_{value['rva']:x}, \"{value['sha256']}\"" + '},')
    lines.append('};')
    for f in facts['functions']:
        name = f['name']
        blob(name + 'Prefix', f['prefix'])
        blob(name + 'Body', f['body'])
        lines.append('inline constexpr RuntimeFunction ' + name + 'RuntimeFunctions[]{')
        for row in f['runtimeFunctions']:
            lines.append('    ' + triple([row['beginRva'], row['endRva'], row['unwindRva']]) + ',')
        lines.append('};')
        lines.append(f"static_assert(sizeof({name}Body) == {f['bodyBytes']} && sizeof({name}Prefix) == {f['prefixBytes']});")
    lines.append('inline constexpr Function Functions[]{')
    for f in facts['functions']:
        name = f['name']
        fields = [f[key] for key in ('index', 'beginRva', 'endRva', 'prefixBytes', 'bodyBytes', 'continuationRva')]
        fields.append(len(f['runtimeFunctions']))
        lines.append('    {"' + name + '", ' + ', '.join(f'0x{x:x}' for x in fields)
                     + f", {name}Prefix, {name}Body, \"{f['bodySha256']}\", {name}RuntimeFunctions" + '},')
    lines.append('};')
    for p in facts['physicalWindows']:
        blob(f"Physical_{p['index']}", p['bytes'])
    lines.append('inline constexpr PhysicalWindow PhysicalWindows[]{')
    for p in facts['physicalWindows']:
        fields = [p['index'], 0 if p['role'] == 'TypedEntry' else 1, p['roleIndex'], p['observationRva'],
                  p['beginRva'], p['endRva'], p['byteCount'], p['functionIndex']]
        lines.append('    {' + ', '.join(f'0x{x:x}' for x in fields) + ', ' + triple(p['runtimeFunction'])
                     + f", Physical_{p['index']}, \"{p['sha256']}\"" + '},')
    lines.append('};')
    lines.append('inline constexpr Site Sites[]{')
    for s in facts['sites']:
        fields = [s['index'], s['family'], s['familyIndex'], s['observationRva'], s['logicalWindow']['byteCount'],
                  s['patchRva'], s['patchSpanBytes'], s['logicalContinuationRva'], s['physicalContinuationRva'],
                  s['branchTargetRva'], s['physicalWindowIndex']]
        lines.append('    {"' + s['name'] + '", ' + ', '.join(f'0x{x:x}' for x in fields)
                     + ', ' + str(s['expandedPhysicalBlock']).lower() + '},')
    lines.append('};')
    for b in facts['conditionalBypasses']:
        blob(f"ZeroReplay_{b['index']}", b['zeroReplay']['bytes'])
    lines.append('inline constexpr ConditionalBypass ConditionalBypasses[]{')
    for b in facts['conditionalBypasses']:
        fields = [b['observationRva'], b['physicalBlock']['beginRva'], b['physicalBlock']['byteCount'],
                  b['originalBranchRva'], b['originalZeroTargetRva'], b['zeroReplay']['beginRva'],
                  b['zeroReplay']['byteCount'], b['zeroContinuationRva'], b['nonzeroContinuationRva']]
        lines.append('    {' + ', '.join(f'0x{x:x}' for x in fields) + f", ZeroReplay_{b['index']}, "
                     + str(b['zeroSkipsReferenceReturnedProbe']).lower() + ', ' + str(b['commonRipMovReplayed']).lower() + '},')
    lines.append('};')
    for index, call in enumerate(facts['preservedCalls']):
        blob(f'CallBytes_{index}', call['bytes'])
    lines.append('inline constexpr PreservedCall PreservedCalls[]{')
    for index, call in enumerate(facts['preservedCalls']):
        fields = [call['callRva'], call['byteCount'], call['returnPcRva'], call['directTargetRva'] or 0]
        lines.append('    {"' + call['name'] + '", ' + ', '.join(f'0x{x:x}' for x in fields)
                     + f', CallBytes_{index}, ' + str(call['requiredSixCallSet']).lower() + '},')
    lines.extend(['};', 'inline constexpr bool CallbackWholeBodyR10AliasAbsent = true;',
                  'inline constexpr bool CallbackR11LiveAfterPrefix = true;',
                  'static_assert(sizeof(Functions) / sizeof(Functions[0]) == TypedCount);',
                  'static_assert(sizeof(Sites) / sizeof(Sites[0]) == SiteCount);',
                  'static_assert(sizeof(PhysicalWindows) / sizeof(PhysicalWindows[0]) == PhysicalWindowCount);',
                  'static_assert(sizeof(ConditionalBypasses) / sizeof(ConditionalBypasses[0]) == 3);',
                  'static_assert(!ProductionFirstInstallAuthorized && !ProductionAdmissionVerified);',
                  '} // namespace RizEngineTransportGameProfile', ''])
    return '\n'.join(lines)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--image', default='D:/SteamLibrary/steamapps/common/Rizline/UnityPlayer.dll')
    parser.add_argument('--output-directory', '--output', dest='output_directory', required=True,
                        help='Fresh absolute directory; parent must exist. Existing paths are refused.')
    args = parser.parse_args()
    source, output = Path(args.image), Path(args.output_directory)
    if not source.is_absolute() or not output.is_absolute():
        parser.error('Image and output directory must be absolute paths.')
    source = source.resolve(strict=True)
    output = output.parent.resolve(strict=True) / output.name
    if output.exists() or not output.parent.is_dir() or not source.is_file():
        parser.error('Use an input file and a nonexistent output directory with an existing parent.')
    if source == output or output in source.parents:
        parser.error('Output cannot contain the input image.')
    output.mkdir(exist_ok=False)
    check = Checks()
    inspector = file_identity(Path(__file__))
    result = {'kind': 'fixed-engine-production-transport-contract', 'schemaVersion': 1,
              'passed': False, 'factsOnly': True, 'input': {'path': str(source)},
              'inspector': inspector, 'boundaries': dict(FALSE_BOUNDARIES)}
    try:
        check.require(pefile.__version__ == '2024.8.26', 'Pinned pefile decoder version')
        check.require(capstone.__version__ == '5.0.6', 'Pinned Capstone decoder version')
        tools = {'python': file_identity(sys.executable), 'pythonVersion': sys.version,
                 'pefileVersion': pefile.__version__, 'pefileSource': file_identity(pefile.__file__),
                 'capstoneVersion': capstone.__version__, 'capstoneSource': file_identity(capstone.__file__)}
        native_decoder = Path(str(capstone._cs._name))
        if native_decoder.is_file():
            tools['capstoneNativeDecoder'] = file_identity(native_decoder)
        result['tools'] = tools
        image = DiskImage(source, check)
        result['input'] = file_identity(source)
        facts, disassembly = inspect(image, check)
        canonical = json.dumps(facts, ensure_ascii=False, sort_keys=True, separators=(',', ':')).encode('utf-8')
        facts_sha = sha(canonical)
        header = generate_header(facts, facts_sha, inspector['sha256']).encode('utf-8')
        check.require(file_identity(Path(__file__)) == inspector, 'Inspector source unchanged')
        check.require(file_identity(source)['sha256'] == IMAGE_SHA, 'Input unchanged before output commit')
        disasm_path, header_path = output / '13-functions.disasm.txt', output / 'EngineTransportGameProfile.h'
        atomic_new(disasm_path, disassembly.encode('utf-8'))
        atomic_new(header_path, header)
        result.update({'passed': True, 'checks': check.count, 'factsPayloadSha256': facts_sha,
                       'facts': facts, 'generatedHeader': file_identity(header_path),
                       'fullDisassembly': file_identity(disasm_path)})
        image.pe.close()
        exit_code = 0
    except Exception as error:
        result.update({'passed': False, 'checks': check.count, 'failedCheck': check.last,
                       'error': {'type': type(error).__name__, 'message': str(error)},
                       'traceback': traceback.format_exc()})
        try:
            result['input'] = file_identity(source)
        except OSError:
            pass
        exit_code = 1
    target = output / 'production-transport-facts.json'
    atomic_new(target, (json.dumps(result, ensure_ascii=False, indent=2) + '\n').encode('utf-8'))
    summary = {'passed': result['passed'], 'checks': check.count, 'output': str(target),
               'sha256': sha(target.read_bytes()), 'factsOnly': True, **FALSE_BOUNDARIES}
    if result['passed']:
        summary.update(result['facts']['counts'])
        summary['generatedHeaderSha256'] = result['generatedHeader']['sha256']
    else:
        summary['failedCheck'] = result.get('failedCheck')
        summary['error'] = result.get('error')
    print(json.dumps(summary, ensure_ascii=False))
    return exit_code


if __name__ == '__main__':
    sys.exit(main())
