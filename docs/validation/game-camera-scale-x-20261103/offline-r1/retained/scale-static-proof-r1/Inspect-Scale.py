"""Read the pinned PE as bytes. Never map, load or execute the game image."""
from pathlib import Path
import hashlib
import json
import re
import capstone
import pefile

O = Path(__file__).resolve().parent
R = O.parent
IMAGE = Path('D:/riz-build-cache-next/playback-direct-audio-managed-r1-20261102/mapped-r4/image/GameAssembly.dll')
DUMP = Path('C:/Users/71957/Desktop/绿豆诡计v2/output/il2cpp_output/dump.cs')
SYMBOLS = DUMP.with_name('script.json')
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
fact = lambda p: dict(path=str(p), sha256=sha(p), bytes=p.stat().st_size)


def main():
    assert not (O / 'test-result.json').exists(), 'Preserve the original static proof.'
    assert sha(IMAGE) == '853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79'
    assert sha(DUMP) == '092cdb96914ccb7af3f31d9d2e703e00dd6ddff63fd559f11ac6b42e6635c1cb'
    raw = IMAGE.read_bytes()
    pe = pefile.PE(data=raw, fast_load=True)
    pe.parse_data_directories(directories=[pefile.DIRECTORY_ENTRY['IMAGE_DIRECTORY_ENTRY_EXCEPTION']])
    assert pe.FILE_HEADER.Machine == 0x8664
    names = {}
    for row in json.loads(SYMBOLS.read_text(encoding='utf-8-sig'))['ScriptMethod']:
        names.setdefault(row['Address'], []).append(row['Name'])
    dump = DUMP.read_text(encoding='utf-8-sig')
    transform = re.search(r'// Namespace: UnityEngine\npublic class Transform : Component\n\{(.*?)\n\}', dump, re.S)
    rig = re.search(r'// Namespace: InLevel\npublic class CameraControl : MonoBehaviour\n\{(.*?)\n\}', dump, re.S)
    assert transform and rig
    signatures = ['UnityEngine.Vector3 get_localScale(); // RVA: 0x1561780',
                  'System.Void set_localScale(UnityEngine.Vector3 value); // RVA: 0x1561E10']
    assert all(transform[1].count(signature) == 1 for signature in signatures)
    assert 'System.Void SetScale(); // RVA: 0x3E2E00' in rig[1]
    assert 'System.Void Update(); // RVA: 0x3E32A0' in rig[1]
    (O / 'dump-excerpts.txt').write_text('// Namespace: UnityEngine\nTransform : Component\n' + '\n'.join(signatures)
                                      + '\n\n// Namespace: InLevel\nCameraControl : MonoBehaviour\n' + rig[1] + '\n', encoding='utf-8')
    expected = [
        ('InLevel.CameraControl$$SetScale', 0x3E2E00, 608, '8dbdb6bd01875bd0fe24ae9ef9428d7deb2fb9f411cd504aec4890ea00886b1e'),
        ('InLevel.CameraControl$$Update', 0x3E32A0, 97, 'b3620bbcbe97763df88ed70329fe0fbe3fbeedbfe1003bb26e605acbba1eb447'),
        ('UnityEngine.Transform$$get_localScale', 0x1561780, 77, 'b66b367ce9aed4b21ff93eafcf4030d029cb06609badf7933448a39eb2f598f6'),
        ('UnityEngine.Transform$$set_localScale', 0x1561E10, 66, '63359448f2b94edab40c898c9220292bb3ecd08702c4e9319c03808e4fdeb04f')]
    decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
    rows = []
    by_address = {}
    for name, start, count, body_hash in expected:
        assert name in names[start]
        next_method = min(address for address in names if address > start)
        fragments = sorted((row.struct for row in pe.DIRECTORY_ENTRY_EXCEPTION
                            if start <= row.struct.BeginAddress < next_method), key=lambda row: row.BeginAddress)
        assert fragments and fragments[0].BeginAddress == start
        assert all(a.EndAddress == b.BeginAddress for a, b in zip(fragments, fragments[1:]))
        assert max(row.EndAddress for row in fragments) == start + count <= next_method
        body = pe.get_data(start, count)
        assert len(body) == count and hashlib.sha256(body).hexdigest() == body_hash
        decoded = list(decoder.disasm(body, start))
        assert sum(i.size for i in decoded) == count
        instructions = [dict(rva=hex(i.address), bytes=bytes(i.bytes).hex(), mnemonic=i.mnemonic, operands=i.op_str) for i in decoded]
        by_address.update({i.address: (i.mnemonic, i.op_str) for i in decoded})
        rf = []
        for row in fragments:
            unwind = pe.get_data(row.UnwindData, 4)
            rf.append(dict(begin=hex(row.BeginAddress), end=hex(row.EndAddress), unwindRva=hex(row.UnwindData),
                           unwindVersion=unwind[0] & 7, unwindFlags=unwind[0] >> 3,
                           sha256=hashlib.sha256(pe.get_data(row.BeginAddress, row.EndAddress - row.BeginAddress)).hexdigest()))
        if start in (0x1561780, 0x1561E10):
            assert len(rf) == 1 and rf[0]['unwindRva'] == '0x1ce4d20' and rf[0]['unwindFlags'] == 0
        (O / (hex(start) + '.bin')).write_bytes(body)
        (O / (hex(start) + '.asm')).write_text('\n'.join(i['rva'] + ' ' + i['mnemonic'] + ' ' + i['operands'] for i in instructions) + '\n', encoding='utf-8')
        rows.append(dict(name=name, rva=hex(start), end=hex(start + count), nextMethodRva=hex(next_method), bytes=count,
                         sha256=body_hash, runtimeFunctions=rf, instructions=instructions))
    required = {
        0x3E2FC5: ('call', '0x1536440'),
        0x3E300C: ('movss', 'xmm0, dword ptr [rbx + 0x34]'),
        0x3E301C: ('mulss', 'xmm1, xmm0'), 0x3E3020: ('mulss', 'xmm2, xmm0'), 0x3E3024: ('mulss', 'xmm3, xmm0'),
        0x3E304A: ('call', '0x1561e10'), 0x3E32E1: ('call', '0x3e2e00'),
        0x156178C: ('mov', 'rdi, rdx'), 0x1561792: ('mov', 'rbx, rcx'),
        0x15617B7: ('mov', 'rdx, rbx'), 0x15617BA: ('mov', 'rcx, rdi'), 0x15617BF: ('mov', 'rax, rbx'),
        0x1561E21: ('mov', 'rbx, rdx'), 0x1561E24: ('mov', 'rdi, rcx'),
        0x1561E3F: ('mov', 'rdx, rbx'), 0x1561E42: ('mov', 'rcx, rdi')}
    assert all(by_address[address] == value for address, value in required.items())
    backend = R / 'source/src/RizToolbox.Adapter/GameCamera/NativeGameCameraBackend.cs'
    source = backend.read_text(encoding='utf-8-sig')
    for _, start, count, body_hash in expected:
        assert f'(0x{start:X}, {count}, "{body_hash}")' in source
    result = dict(kind='fixed-file-camera-scale-x-complete-runtime-function-static-proof', passed=True,
                  gameImage=fact(IMAGE), dump=fact(DUMP), symbols=fact(SYMBOLS), backend=fact(backend),
                  declaredMethods=signatures, completeMethodCount=4, completeMethodBytes=sum(r['bytes'] for r in rows), scopes=rows,
                  verifiedConsumerInstructions=[dict(rva=hex(address), mnemonic=value[0], operands=value[1]) for address, value in required.items()],
                  inference=dict(scalar='Transform.localScale.x', unit='dimensionless', minimum=None, maximum=None,
                                 uniformZoomProved=False, nativeWrapperAbi='getter: hidden Vector3 out RCX, Transform RDX; setter: Transform RCX, Vector3 address RDX',
                                 shippedInvocation='IL2CPP runtime_invoke with exact current declared metadata, boxed value validation and rooted receivers',
                                 perFrameGameWriter=True, updateHostRenderOrderProved=False, sustainedVisualRestorationProved=False),
                  gameImageLoaded=False, gameExecuted=False, installed=False, mainApplied=False,
                  tools=dict(pefile=pefile.__version__, capstone=capstone.__version__), runner=fact(Path(__file__).resolve()))
    (O / 'test-result.json').write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    files = [fact(p) for p in sorted(O.iterdir()) if p.is_file()]
    (O / 'hash-manifest.json').write_text(json.dumps(dict(files=files, passed=True), ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(dict(passed=True, completeMethods=4, completeBytes=result['completeMethodBytes'], gameExecuted=False)))


if __name__ == '__main__':
    main()
