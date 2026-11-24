"""Freeze the three pinned instruction-site contracts; never map or run game code."""
import argparse
import hashlib
import json
from pathlib import Path
import struct

import capstone
import pefile


def sha(data):
    return hashlib.sha256(data).hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--image', required=True)
    parser.add_argument('--output', required=True)
    args = parser.parse_args()
    source = Path(args.image).resolve()
    output = Path(args.output).resolve()
    if output.exists():
        raise ValueError('A fresh output file is required.')
    data = source.read_bytes()
    expected = '43fb0aea1b80c74963dae88ec7fc1c3c1993d893ce94d12972000fcc9b653ad1'
    if sha(data) != expected:
        raise ValueError('The fixed UnityPlayer image changed.')
    pe = pefile.PE(data=data)
    if pe.FILE_HEADER.Machine != 0x8664 or pe.OPTIONAL_HEADER.Magic != 0x20b:
        raise ValueError('The AMD64 PE32+ contract is absent.')
    md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
    begin, end = 0x792240, 0x7929bf
    body = pe.get_data(begin, end - begin)
    if sha(body) != '6a24dc40b10797a33529428b7d82821f1cb646a72d9108cccb884bfe2f8d7721':
        raise ValueError('The full fixed Start body changed.')
    instructions = {ins.address: ins for ins in md.disasm(body, begin)}

    def unwind_chain(site):
        matches = [entry.struct for entry in pe.DIRECTORY_ENTRY_EXCEPTION
                   if entry.struct.BeginAddress <= site < entry.struct.EndAddress]
        if len(matches) != 1:
            raise ValueError('Each instruction site needs one actual runtime function.')
        current, rows, visited = matches[0], [], set()
        triple = (current.BeginAddress, current.EndAddress, current.UnwindData)
        for _ in range(16):
            if triple in visited:
                raise ValueError('Unwind chain cycle.')
            visited.add(triple)
            first, last, rva = triple
            version_flags, prolog, count, frame = pe.get_data(rva, 4)
            version, flags = version_flags & 7, version_flags >> 3
            if version != 1 or flags & 4 and flags & 3:
                raise ValueError('Unsupported or conflicting unwind metadata.')
            encoded_size = 4 + ((count + 1) & ~1) * 2
            extra = 12 if flags & 4 else 4 if flags & 3 else 0
            raw = pe.get_data(rva, encoded_size + extra)
            rows.append({'beginRva': first, 'endRva': last, 'unwindRva': rva,
                         'version': version, 'flags': flags, 'prologSize': prolog,
                         'codeCount': count, 'frameRegister': frame & 15,
                         'frameOffset': frame >> 4, 'raw': raw.hex(), 'sha256': sha(raw)})
            if not flags & 4:
                return rows
            triple = struct.unpack('<III', raw[encoded_size:encoded_size + 12])
        raise ValueError('Unwind chain exceeds the bounded contract.')

    sites = []
    for kind, rva, expected_bytes, continuation in [
        ('Begin', 0x7924cd, 'ff4b60488b4b68', 0x7924d4),
        ('EndFree', 0x79257d, 'e91c040000', 0x79299e),
        ('EndRetained', 0x79259d, 'e9fc030000', 0x79299e),
    ]:
        raw = bytes.fromhex(expected_bytes)
        if pe.get_data(rva, len(raw)) != raw or rva not in instructions:
            raise ValueError('The pinned site bytes or instruction boundary changed.')
        offset, decoded = rva, []
        while offset < rva + len(raw):
            ins = instructions[offset]
            decoded.append({'rva': ins.address, 'bytes': ins.bytes.hex(),
                            'mnemonic': ins.mnemonic, 'operands': ins.op_str})
            offset += ins.size
        if offset != rva + len(raw) or continuation not in instructions:
            raise ValueError('Replay/continuation is not a complete fixed instruction boundary.')
        if kind != 'Begin' and (decoded[0]['mnemonic'] != 'jmp'
                               or decoded[0]['operands'] != hex(continuation)):
            raise ValueError('End continuation changed.')
        section = pe.get_section_by_rva(rva)
        if section is None or not section.Characteristics & 0x20000000 or section.Characteristics & 0x80000000:
            raise ValueError('Site must be in the fixed executable non-writable section.')
        sites.append({'kind': kind, 'siteRva': rva, 'originalBytes': raw.hex(),
                      'patchSpanBytes': len(raw), 'continuationRva': continuation,
                      'relayReplaysOriginal': kind == 'Begin', 'instructions': decoded,
                      'runtimeFunctionChain': unwind_chain(rva),
                      'nodeMayAlreadyBeFreed': kind == 'EndFree',
                      'nodeReadAfterCleanupPermitted': False})
    imports = [{'dll': item.dll.decode('ascii'), 'symbols': len(item.imports)}
               for item in pe.DIRECTORY_ENTRY_IMPORT]
    result = {'kind': 'fixed-engine-inline-instruction-site-contract', 'schemaVersion': 1,
              'image': str(source), 'imageSha256': sha(data), 'imageBytes': len(data),
              'imageBase': pe.OPTIONAL_HEADER.ImageBase, 'imageSize': pe.OPTIONAL_HEADER.SizeOfImage,
              'machine': pe.FILE_HEADER.Machine, 'imports': imports,
              'startBeginRva': begin, 'startEndRva': end, 'startBodySha256': sha(body),
              'sites': sites, 'probePublicationProved': False, 'relaysInstalled': False,
              'runtimeUnwindProved': False, 'executionWindowProved': False,
              'gameExecuted': False, 'productionAdmissionGranted': False,
              'implementationRule': 'Source JMP -> permanent near relay CALL -> full-state probe -> replay/continuation; never rewrite a CALL return slot.'}
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({'sites': len(sites), 'importGroups': len(imports),
                      'output': str(output), 'sha256': sha(output.read_bytes()), 'gameExecuted': False}))


if __name__ == '__main__':
    main()
