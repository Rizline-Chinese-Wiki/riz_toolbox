"""Bounded fixed-image wait contract; disk PE/Capstone only, no target execution."""
import argparse
import bisect
import hashlib
import json
from pathlib import Path
import struct
import sys
import capstone
import pefile

IMAGE_SHA = '43fb0aea1b80c74963dae88ec7fc1c3c1993d893ce94d12972000fcc9b653ad1'
# An interior anchor deliberately resolves the publication function from pdata.
# Neither 790000 nor 790069 is treated as a new function entry.
FUNCTIONS = [
    ('yield_publish', 0x790069, 0x78fc50, 0x79014b, '938b7a48664df2e0b0195fa2075f130f285a9498e0ce87693269dc2c6db3696f'),
    ('wait_dispatch', 0x5a5260, 0x5a5260, 0x5a5343, '7143eab8a6af6b584a341b1b41857b7ce9f74a4108987508ca803bdab1d11ccf'),
    ('owner_lookup', 0x152540, 0x152540, 0x1525d1, '301335687736833f3e3d8c4044d6ce95bc108353cf973fae9ba65e42e2c64237'),
    ('detach_owner_revoke', 0x793840, 0x793840, 0x793a5b, '942344fdd9c188c5e9b8371d6e03ce8fb8f944bbfcd520cadd3ef5715dc5edf7'),
    ('release_field_revoke', 0x78f680, 0x78f680, 0x78f768, '771128974dd1a81b49b7f6a9743a492a752cf30aa42bd95073df111cf159d1a3'),
    ('start_inline_revoke', 0x792240, 0x792240, 0x7929bf, '6a24dc40b10797a33529428b7d82821f1cb646a72d9108cccb884bfe2f8d7721'),
    ('batch_wait_cleanup', 0x5da510, 0x5da510, 0x5da6db, '01f8cbd43685808116a8c8e34fe4cf7053de54595988ae8ade76676b351d6657'),
    ('known_manager_notification', 0x5dacd0, 0x5dacd0, 0x5dafd2, '2e17bb4737d2a10ae76126c8c8507748a757f52a4bf8ff0eb0795492ce317ffa'),
    ('shared_reference_return', 0x342400, 0x342400, 0x3426f6, 'fcac2902e4f66db63d05767341038d12af5429463bf9254a9e2d0396efb71e60'),
]
# Exact observed bytes, not wildcard signatures or guessed semantic addresses.
POINTS = [
    ('publish_native_wait', 0x78ffc9, '488b7f10'),
    ('publish_node_reference', 0x78ffd6, '41ff4660'),
    ('publish_is_done', 0x78ffe0, 'ff5008'),
    ('publish_done_to_tree', 0x78ffe5, '0f85be000000'),
    ('publish_slot_occupied', 0x78ffeb, '48837f1000'),
    ('publish_slot_empty_branch', 0x78fff0, '746b'),
    ('publish_owner_id', 0x790069, '895f28'),
    ('publish_callback_address', 0x79006c, '488d056df5ffff'),
    ('publish_callback_store', 0x790073, '48894710'),
    ('publish_cleanup_address', 0x790077, '488d0502f6ffff'),
    ('publish_cleanup_store', 0x79007e, '48894718'),
    ('publish_payload_store', 0x790082, '4c897720'),
    ('publish_old_wait', 0x790086, '498b4e78'),
    ('publish_old_reference_return', 0x790093, 'e86823bbff'),
    ('publish_node_wait_store', 0x790098, '49897e78'),
    ('publish_wait_shared_reference', 0x79009c, 'f0ff470c'),
    ('publish_committed_branch', 0x7900a0, 'e989000000'),
    ('dispatch_has_callback', 0x5a5269, '4883791000'),
    ('dispatch_no_callback_branch', 0x5a5271, '742f'),
    ('dispatch_owner_lookup', 0x5a5277, 'e8c4d2baff'),
    ('dispatch_owner_null_branch', 0x5a527f, '740a'),
    ('dispatch_callback_payload', 0x5a5281, '488b5720'),
    ('dispatch_callback', 0x5a5288, 'ff5710'),
    ('dispatch_reread_cleanup', 0x5a528b, '488b4718'),
    ('dispatch_no_cleanup_branch', 0x5a5292, '7406'),
    ('dispatch_reread_payload', 0x5a5294, '488b4f20'),
    ('dispatch_cleanup', 0x5a5298, 'ffd0'),
    ('dispatch_clear_callback', 0x5a529a, '48c7471000000000'),
    ('dispatch_after_clear', 0x5a52a2, '837f3802'),
    ('dispatch_managed_notification', 0x5a530e, 'e8fdeee0ff'),
    ('lookup_owner_zero_branch', 0x15254a, '7508'),
    ('lookup_owner_zero_return', 0x152553, 'c3'),
    ('detach_clear_node_owner', 0x793883, '48896f58'),
    ('detach_nested_wait_owner', 0x7938b3, '896828'),
    ('detach_direct_wait_owner', 0x793983, '896828'),
    ('release_load_wait', 0x78f6ec, '488b4378'),
    ('release_clear_wait_owner', 0x78f6f5, '897828'),
    ('release_clear_wait_callback', 0x78f6f8, '48897810'),
    ('release_clear_wait_cleanup', 0x78f6fc, '48897818'),
    ('release_clear_wait_payload', 0x78f700, '48897820'),
    ('release_wait_reference_return', 0x78f70c, 'e8ef2cbbff'),
    ('release_clear_node_wait', 0x78f711, '48897b78'),
    ('start_load_wait', 0x79251e, '488b4378'),
    ('start_clear_wait_owner', 0x792527, '897828'),
    ('start_clear_wait_callback', 0x79252a, '48897810'),
    ('start_clear_wait_cleanup', 0x79252e, '48897818'),
    ('start_clear_wait_payload', 0x792532, '48897820'),
    ('start_wait_reference_return', 0x79253e, 'e8bdfebaff'),
    ('start_clear_node_wait', 0x792543, '48897b78'),
    ('batch_load_wait', 0x5da637, '488b2cf8'),
    ('batch_has_callback', 0x5da63b, '4c397510'),
    ('batch_no_callback_branch', 0x5da63f, '740b'),
    ('batch_cleanup_payload', 0x5da641, '488b4d20'),
    ('batch_cleanup', 0x5da645, 'ff5518'),
    ('batch_clear_callback', 0x5da648, '4c897510'),
    ('batch_reload_container_wait', 0x5da653, '488b0cf8'),
    ('batch_wait_reference_return', 0x5da65b, 'e8a07dd6ff'),
    ('batch_after_reference_return', 0x5da660, '48ffc7'),
    ('manager_object_completed', 0x5dafb2, '41c7464802000000'),
    ('manager_virtual_notification', 0x5dafba, 'ff5038'),
]

def sha(data):
    return hashlib.sha256(data).hexdigest()

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--image', default='D:/SteamLibrary/steamapps/common/Rizline/UnityPlayer.dll')
    parser.add_argument('--output', required=True, help='Fresh absolute directory; an existing directory is refused.')
    args = parser.parse_args()
    source, output = Path(args.image).resolve(), Path(args.output)
    if not output.is_absolute() or output.exists():
        raise ValueError('Output must be a fresh absolute directory.')
    if not output.parent.is_dir():
        raise ValueError('Output parent must already exist.')
    raw = source.read_bytes()
    if sha(raw) != IMAGE_SHA:
        raise ValueError('Fixed UnityPlayer SHA256 differs.')
    pe = pefile.PE(data=raw, fast_load=True)
    assert pe.FILE_HEADER.Machine == 0x8664 and pe.OPTIONAL_HEADER.Magic == 0x20b
    assert pe.OPTIONAL_HEADER.ImageBase == 0x180000000
    directory = pe.OPTIONAL_HEADER.DATA_DIRECTORY[3]
    pdata = pe.get_data(directory.VirtualAddress, directory.Size)
    assert len(pdata) == directory.Size and len(pdata) % 12 == 0
    functions = [struct.unpack_from('<III', pdata, n) for n in range(0, len(pdata), 12)]
    assert functions == sorted(functions) and all(lo < hi for lo, hi, _ in functions)
    starts = [r[0] for r in functions]

    def chain(row):
        seen, result = set(), []
        for _ in range(16):
            assert row not in seen
            seen.add(row)
            lo, hi, unwind = row
            header = pe.get_data(unwind, 4)
            assert len(header) == 4
            vf, prolog, codes, frame = header
            version, flags = vf & 7, vf >> 3
            assert version in (1, 2) and not (flags & 4 and flags & 3)
            size = 4 + ((codes + 1) & ~1) * 2
            extra = 12 if flags & 4 else 4 if flags & 3 else 0
            data = pe.get_data(unwind, size + extra)
            assert len(data) == size + extra
            record = dict(beginRva=lo, endRva=hi, unwindRva=unwind, version=version, flags=flags,
                          prologBytes=prolog, unwindCodeSlots=codes, frameRegister=frame & 15,
                          frameOffsetBytes=(frame >> 4) * 16, bytes=data.hex(), sha256=sha(data))
            result.append(record)
            if flags & 4:
                row = struct.unpack('<III', data[size:size + 12])
                assert row in functions
                record['chainedRuntimeFunction'] = list(row)
            else:
                if flags & 3:
                    record['handlerRva'] = struct.unpack('<I', data[size:size + 4])[0]
                    record['handlerSpecificDataDecoded'] = False
                return row, result
        raise ValueError('CHAININFO exceeds finite inspection bound.')

    # Resolve metadata only. Instructions are decoded solely for nine selected
    # groups; no text/signature/xref scan across unrelated executable code.
    roots = [chain(row)[0] for row in functions]
    md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
    md.detail = True
    selected, decoded, lines = [], {}, []
    for label, anchor, expected_begin, expected_end, expected_sha in FUNCTIONS:
        i = bisect.bisect_right(starts, anchor) - 1
        assert i >= 0 and functions[i][0] <= anchor < functions[i][1]
        root = roots[i]
        group = [row for row, parent in zip(functions, roots) if parent == root]
        merged = []
        for lo, hi, _ in sorted(group):
            if merged and lo <= merged[-1][1]:
                merged[-1][1] = max(hi, merged[-1][1])
            else:
                merged.append([lo, hi])
        assert merged == [[expected_begin, expected_end]], (label, merged)
        segments, instructions = [], []
        lines.append(f'\n## {label}: anchor={anchor:x}, actual={expected_begin:x}..{expected_end:x}')
        for lo, hi in merged:
            section = pe.get_section_by_rva(lo)
            assert section and section.Characteristics & 0x20000000
            assert not section.Characteristics & 0x80000000
            body = pe.get_data(lo, hi - lo)
            assert len(body) == hi - lo and sha(body) == expected_sha
            ins = list(md.disasm(body, lo))
            assert ins and sum(x.size for x in ins) == len(body)
            segments.append(dict(beginRva=lo, endRva=hi, bytes=body.hex(), sha256=sha(body)))
            for x in ins:
                row = dict(rva=x.address, size=x.size, bytes=x.bytes.hex(), mnemonic=x.mnemonic, operands=x.op_str)
                if x.group(capstone.CS_GRP_CALL):
                    row['returnPcRva'] = x.address + x.size
                if x.group(capstone.CS_GRP_CALL) or x.group(capstone.CS_GRP_JUMP):
                    row['directTargetRva'] = x.operands[0].imm if x.operands[0].type == capstone.CS_OP_IMM else None
                if x.mnemonic == 'lea' and len(x.operands) == 2 and x.operands[1].type == capstone.CS_OP_MEM:
                    memory = x.operands[1].mem
                    if memory.base == capstone.x86.X86_REG_RIP:
                        row['ripAddressRva'] = x.address + x.size + memory.disp
                decoded[x.address] = row
                instructions.append(row)
                lines.append(f'{x.address:08x} {x.bytes.hex():<30} {x.mnemonic:<9} {x.op_str}')
        selected.append(dict(label=label, anchorRva=anchor, actualBeginRva=expected_begin, actualEndRva=expected_end,
                             primaryRuntimeFunction=list(root), segments=segments, instructions=instructions,
                             runtimeFunctions=[dict(beginRva=row[0], endRva=row[1], unwindRva=row[2], chain=chain(row)[1]) for row in group]))
    points = []
    for label, rva, expected in POINTS:
        row = decoded[rva]
        assert row['bytes'] == expected, (label, row)
        points.append(dict(label=label, **row))
    assert decoded[0x79006c]['ripAddressRva'] == 0x78f5e0
    assert decoded[0x790077]['ripAddressRva'] == 0x78f680
    assert decoded[0x5a527f]['directTargetRva'] == 0x5a528b
    assert decoded[0x790093]['directTargetRva'] == decoded[0x78f70c]['directTargetRva'] == 0x342400
    assert decoded[0x79253e]['directTargetRva'] == decoded[0x5da65b]['directTargetRva'] == 0x342400
    pointers = [dict(pointerRva=0x199ee50, targetRva=0x5a5260)]
    for row in pointers:
        assert struct.unpack('<Q', pe.get_data(row['pointerRva'], 8))[0] == pe.OPTIONAL_HEADER.ImageBase + row['targetRva']
    result = dict(schemaVersion=1, kind='fixed-engine-wait-known-sites-contract', passed=True,
                  input=dict(path=str(source), sha256=IMAGE_SHA, bytes=len(raw), machine=pe.FILE_HEADER.Machine,
                             imageBase=pe.OPTIONAL_HEADER.ImageBase, imageSize=pe.OPTIONAL_HEADER.SizeOfImage),
                  inspector=dict(path=str(Path(__file__).resolve()), sha256=sha(Path(__file__).read_bytes()),
                                 python=sys.version, pefile=pefile.__version__, capstone=capstone.__version__),
                  functions=selected, points=points, knownDataPointers=pointers,
                  boundaries=dict(selectedFunctionsOnly=True, completeSelectedChainGroups=True,
                                  allWritersProven=False, allDispatchersProven=False, coherentConcurrentSnapshotsProven=False,
                                  futureSourcesClosed=False, gameCodeExecuted=False, nativeFixtureExecuted=False,
                                  firstInstallSafetyVerified=False, engineCancellationCompleted=False))
    output.mkdir()
    artifact = output / 'fixed-wait-sites.json'
    artifact.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    assembly = output / 'wait-disassembly.txt'
    assembly.write_text('\n'.join(lines) + '\n', encoding='utf-8')
    print(json.dumps(dict(passed=True, output=str(artifact), sha256=sha(artifact.read_bytes()),
                         disassembly=str(assembly), disassemblySha256=sha(assembly.read_bytes()),
                         functions=len(selected), instructions=sum(len(f['instructions']) for f in selected),
                         exactPoints=len(points), gameCodeExecuted=False)))

if __name__ == '__main__':
    main()
