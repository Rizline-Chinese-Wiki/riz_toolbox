"""Read-only fixed-image evidence for six scheduler entries and two drain sites."""
import argparse
import hashlib
import json
from pathlib import Path
import struct
import capstone
import pefile

def sha(value):
    return hashlib.sha256(value).hexdigest()

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--image', required=True)
    parser.add_argument('--output', required=True)
    args = parser.parse_args()
    source, output = Path(args.image).resolve(), Path(args.output).resolve()
    if output.exists():
        raise ValueError('Use a fresh output file.')
    data = source.read_bytes()
    assert sha(data) == '43fb0aea1b80c74963dae88ec7fc1c3c1993d893ce94d12972000fcc9b653ad1'
    pe = pefile.PE(data=data)
    assert pe.FILE_HEADER.Machine == 0x8664 and pe.OPTIONAL_HEADER.Magic == 0x20b
    md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
    md.detail = True
    old_root = Path(__file__).resolve().parent.parent / 'foundation-engine-binding-20261025/research'
    old_records, inputs = {}, []
    for name, expected in [
        ('scheduler-evidence.json', '64ed08c89d488ab8dba3572c406a848e558e52e946cbe04567353b1db3e9f65f'),
        ('scheduler-consumption.json', 'e4d2f484362fee99ebd37efa0c30e7a94f1d2c6d0c55c5d77d8a8e7581e8ac76'),
    ]:
        path = old_root / name
        raw = path.read_bytes()
        assert sha(raw) == expected
        inputs.append(dict(path=str(path), sha256=expected))
        for row in json.loads(raw.decode('utf-8-sig'))['functions']:
            for segment in row['segments']:
                key = (int(segment['begin'], 16), int(segment['end'], 16))
                if key in old_records:
                    assert old_records[key] == segment['sha256']
                old_records[key] = segment['sha256']

    def rx(rva):
        section = pe.get_section_by_rva(rva)
        assert section is not None and section.Characteristics & 0x20000000 and not section.Characteristics & 0x80000000

    def chain(rva):
        rows = [item.struct for item in pe.DIRECTORY_ENTRY_EXCEPTION
                if item.struct.BeginAddress <= rva < item.struct.EndAddress]
        assert len(rows) == 1
        row = rows[0]
        current, seen, result = (row.BeginAddress, row.EndAddress, row.UnwindData), set(), []
        for _ in range(16):
            assert current not in seen
            seen.add(current)
            begin, end, unwind = current
            vf, prolog, count, frame = pe.get_data(unwind, 4)
            version, flags = vf & 7, vf >> 3
            assert version == 1 and not (flags & 4 and flags & 3)
            size = 4 + ((count + 1) & ~1) * 2
            extra = 12 if flags & 4 else 4 if flags & 3 else 0
            raw = pe.get_data(unwind, size + extra)
            assert len(raw) == size + extra
            result.append(dict(beginRva=begin, endRva=end, unwindRva=unwind,
                flags=flags, prologBytes=prolog, frameRegister=frame & 15,
                frameOffsetBytes=(frame >> 4) * 16, raw=raw.hex(), sha256=sha(raw)))
            if not flags & 4:
                return result
            current = struct.unpack('<III', raw[size:size + 12])
        raise ValueError('Unwind chain exceeds bound.')

    functions, decoded = [], {}
    for kind, begin, end in [
        ('Register', 0x44d630, 0x44d756), ('Remove', 0x447ad0, 0x447c26),
        ('Consume', 0x447c30, 0x447d60), ('Pump', 0x447d60, 0x447faf),
        ('Drain', 0x447750, 0x4478ac), ('Callback', 0x78f5e0, 0x78f673),
    ]:
        body = pe.get_data(begin, end - begin)
        assert sha(body) == old_records[(begin, end)]
        instructions = list(md.disasm(body, begin))
        assert sum(item.size for item in instructions) == len(body)
        assert instructions[0].address == begin
        rx(begin)
        decoded.update({item.address: item for item in instructions})
        functions.append(dict(kind=kind, beginRva=begin, endRva=end,
            bodySha256=sha(body), instructions=len(instructions), runtimeFunctionChain=chain(begin),
            machineCarrier='four opaque integer register inputs, full raw RAX',
            semanticallyProvedReturn='output-slot pointer' if kind == 'Register'
                else 'uint32 EAX' if kind == 'Callback' else 'source-language type unproved'))
    sites = []
    for kind, rva, raw_hex, continuation, branch in [
        ('DrainRowBegin', 0x447787, 'f64354407464', 0x44778d, 0x4477f1),
        ('DrainRowEnd', 0x447866, '48837d3800', 0x44786b, None),
    ]:
        raw = bytes.fromhex(raw_hex)
        assert pe.get_data(rva, len(raw)) == raw and rva in decoded and continuation in decoded
        position, instructions = rva, []
        while position < rva + len(raw):
            item = decoded[position]
            instructions.append(dict(rva=item.address, bytes=item.bytes.hex(), mnemonic=item.mnemonic, operands=item.op_str))
            position += item.size
        assert position == rva + len(raw)
        if branch is not None:
            assert decoded[rva + 4].mnemonic == 'je' and decoded[rva + 4].operands[0].imm == branch and branch in decoded
        rx(rva)
        sites.append(dict(kind=kind, siteRva=rva, sourceBytes=raw_hex, patchBytes=len(raw),
            continuationRva=continuation, conditionalBranchRva=branch, runtimeFunctionChain=chain(rva),
            replay='TEST and relocated JE plus fallthrough transfer' if branch is not None else 'CMP before original JNE',
            savedEntryRegister='RBX', savedManagerRegister='RBP',
            afterCleanupMayReadEntry=False))
    assert sha(source.read_bytes()) == sha(data)
    result = dict(schemaVersion=1, kind='fixed-engine-scheduler-entry-and-row-contract', passed=True,
        image=str(source), imageSha256=sha(data), imageBytes=len(data), inputs=inputs,
        functions=functions, drainSites=sites, gameExecuted=False, installed=False,
        runtimeUnwindVerified=False, probePublicationVerified=False,
        productionAdmissionVerified=False, engineCancellationCompleted=False,
        allSchedulerWritersVerified=False, allFutureProducersClosed=False)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(dict(passed=True, functions=len(functions), sites=len(sites), output=str(output), sha256=sha(output.read_bytes()))))

if __name__ == '__main__':
    main()
