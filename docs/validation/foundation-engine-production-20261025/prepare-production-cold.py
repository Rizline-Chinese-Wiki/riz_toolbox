"""Bind a self-owned cold caller to one completed production candidate, offline."""
from pathlib import Path
import argparse
import hashlib
import json
import re
import struct

NAMES = ['winhttp.dll', 'RizToolbox.EngineBootstrap.dll', 'RizToolbox.EngineNodeKernel.dll']
WINHTTP = ['WinHttpOpen', 'WinHttpGetProxyForUrl', 'WinHttpGetIEProxyConfigForCurrentUser', 'WinHttpCloseHandle']
BOOT = ['RizEngineFirstInstall_' + x for x in ['GetVersion', 'GetInfoV4', 'VerifyV4', 'AbortAfterPublication']]
KERNEL = ['RizEngineNodeContext_' + x for x in ['GetAbiVersion', 'GetLimit', 'GetFrameSize', 'GetCompletionSize', 'GetEntry', 'Configure', 'Attach', 'GetInfo']]
SYSTEM_WINHTTP_SHA256 = '2509edaea64d4ed9e9993b80fa370f431f37117401ef77cf367533a31b82c180'
SNAPSHOT_BYTES, PHASE_BYTES, CALLS_OFFSET, CALL_BYTES = 42512, 14072, 42256, 64

def require(value, message):
    if not value:
        raise ValueError(message)

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

class Pe:
    def __init__(self, path, system_reference=False):
        self.path = path.resolve()
        self.system_reference = system_reference
        self.data = path.read_bytes()
        require(0 < len(self.data) <= 64 * 1024 * 1024, 'bounded native image required')
        require(self.data[:2] == b'MZ', 'missing DOS header')
        nt = self.u(0x3c)
        require(0 < nt <= 0x100000 and self.data[nt:nt+4] == b'PE\0\0', 'missing NT header')
        optional = nt + 24
        require(self.h(nt+4) == 0x8664 and self.h(optional) == 0x20b and self.u(optional+108) >= 16, 'AMD64 PE32+ required')
        self.base = self.q(optional+24)
        self.size = self.u(optional+56)
        self.timestamp = self.u(nt+8)
        self.entry = self.u(optional+16)
        self.headers = self.u(optional+60)
        self.directories = [(self.u(optional+112+i*8), self.u(optional+116+i*8)) for i in range(16)]
        self.sections = []
        at = optional + self.h(nt+20)
        for i in range(self.h(nt+6)):
            pos = at + i * 40
            row = dict(name=self.data[pos:pos+8].rstrip(b'\0').decode('ascii'), virtualSize=self.u(pos+8),
                       rva=self.u(pos+12), rawSize=self.u(pos+16), raw=self.u(pos+20), characteristics=self.u(pos+36))
            require(row['raw'] + row['rawSize'] <= len(self.data), 'truncated section')
            self.sections.append(row)
        require(self.size >= 4096 and self.timestamp and self.sections, 'incomplete native image')
        self.exports = self.read_exports()
        self.imports = self.read_imports()

    def h(self, at): return struct.unpack_from('<H', self.data, at)[0]
    def u(self, at): return struct.unpack_from('<I', self.data, at)[0]
    def q(self, at): return struct.unpack_from('<Q', self.data, at)[0]
    def offset(self, rva, count):
        require(count > 0 and 0 <= rva <= self.size - count, 'out-of-image RVA')
        if rva + count <= self.headers:
            require(rva + count <= len(self.data), 'truncated header')
            return rva
        matches = [s for s in self.sections if s['rva'] <= rva and rva-s['rva']+count <= s['rawSize']]
        require(len(matches) == 1, 'RVA lacks a unique complete file-backed range')
        return matches[0]['raw'] + rva - matches[0]['rva']
    def bytes(self, rva, count):
        at = self.offset(rva, count)
        return self.data[at:at+count]
    def name(self, rva):
        out = bytearray()
        for i in range(512):
            value = self.bytes(rva+i, 1)[0]
            if not value:
                return out.decode('ascii')
            require(0x20 <= value <= 0x7e, 'non-ASCII native name')
            out.append(value)
        raise ValueError('unterminated native name')
    def read_exports(self):
        rva, size = self.directories[0]
        if not rva:
            return []
        header = self.bytes(rva, 40)
        ordinal_base, count, named, functions, names, ordinals = struct.unpack_from('<6I', header, 16)
        require(0 < count <= 65536 and named <= count, 'unbounded export table')
        rows = []
        for i in range(named):
            name_rva = struct.unpack('<I', self.bytes(names+i*4, 4))[0]
            ordinal = struct.unpack('<H', self.bytes(ordinals+i*2, 2))[0]
            require(ordinal < count, 'bad export ordinal')
            function = struct.unpack('<I', self.bytes(functions+ordinal*4, 4))[0]
            rows.append(dict(name=self.name(name_rva), ordinal=ordinal_base+ordinal, rva=function,
                             forwarded=rva <= function < rva+size))
        require(len({r['name'] for r in rows}) == len(rows), 'duplicate export name')
        return rows
    def read_imports(self):
        rva, size = self.directories[1]
        if not rva:
            return []
        require(20 <= size <= 1024*1024, 'unbounded import table')
        rows = []
        ended = False
        for at in range(0, size-19, 20):
            descriptor = self.bytes(rva+at, 20)
            if not any(descriptor):
                ended = True
                break
            ilt, _, _, name, iat = struct.unpack('<5I', descriptor)
            require(ilt and iat, 'independent ordinary ILT/IAT required')
            module = self.name(name)
            for index in range(65537):
                value = struct.unpack('<Q', self.bytes(ilt+index*8, 8))[0]
                if not value:
                    break
                if value & 0x8000000000000000:
                    require(self.system_reference and value & 0x7fffffffffff0000 == 0, 'named ordinary imports required')
                    rows.append(dict(module=module, ordinal=value & 0xffff, iltRva=ilt+index*8, iatRva=iat+index*8))
                else:
                    require(value <= 0xffffffff, 'named ordinary imports required')
                    rows.append(dict(module=module, symbol=self.name(value+2), iltRva=ilt+index*8, iatRva=iat+index*8))
            else:
                raise ValueError('unterminated ILT')
        require(ended, 'unterminated import descriptors')
        require(self.system_reference or not self.directories[13][0], 'delay imports are outside this fixture contract')
        return rows
    def edge(self, module, symbols):
        rows = [r for r in self.imports if r['module'].lower() == module.lower()]
        require(len(rows) == len(symbols) and {r['symbol'] for r in rows} == set(symbols), 'wrong ordinary edge: '+module)
        return rows
    def facts(self):
        return dict(path=str(self.path), sha256=sha(self.path), fileBytes=len(self.data), imageBase=self.base,
                    imageSize=self.size, timeDateStamp=self.timestamp, entryRva=self.entry,
                    sections=self.sections, exports=self.exports, imports=self.imports, directories=self.directories, machine=0x8664)

def ordinary(path):
    require(path.is_absolute() and path.is_file(), 'absolute ordinary input required: '+str(path))
    for part in [path, *path.parents]:
        require(not part.is_symlink() and not (hasattr(part, 'is_junction') and part.is_junction()), 'linked input refused')
    return path.resolve()

def prepare(root, output, header, system_path):
    manifest_path = ordinary(root/'production-build-manifest.json')
    manifest = json.loads(manifest_path.read_text(encoding='utf-8-sig'))
    require(manifest['kind'] == 'fixed-game-engine-production-candidate-build' and manifest['schemaVersion'] == 1, 'wrong completed candidate')
    for field in ['productionAdmissionProved', 'gameExecuted', 'installed']:
        require(manifest[field] is False, 'candidate must remain unadmitted/unexecuted')
    rows = {Path(row['path']).name: row for row in manifest['files']}
    require(len(rows) == len(manifest['files']) == 7, 'candidate file inventory differs')
    for row in rows.values():
        path = ordinary(Path(row['path']))
        require(path.parent == root and sha(path) == row['sha256'], 'candidate input identity changed')
    images = [Pe(ordinary(root/name)) for name in NAMES]
    proxy, bootstrap, kernel = images
    require(len(proxy.exports) == 82 and not any(r['name'].startswith('Riz') for r in proxy.exports), '82 public WinHTTP exports required')
    require({r['name'] for r in bootstrap.exports} == set(BOOT) and len(bootstrap.exports) == 4,
            'the existing four Bootstrap exports must remain exact')
    require({r['name'] for r in kernel.exports} == set(KERNEL) and len(kernel.exports) == 8,
            'the existing eight Kernel exports must remain exact')
    require(all(not image.directories[9][0] for image in images), 'candidate chain must not add a TLS initializer')
    exports = [next(r for r in proxy.exports if r['name'] == name) for name in WINHTTP]
    require(not any(r['forwarded'] for r in exports) and proxy.entry and bootstrap.entry and not kernel.entry, 'wrong actual entry/forwarder shape')
    proxy.edge(NAMES[1], BOOT)
    bootstrap.edge(NAMES[2], KERNEL)
    map_path = ordinary(root/'doorstop.map')
    text = map_path.read_text(encoding='utf-8-sig')
    found = []
    for line in text.splitlines():
        match = re.match(r'^\s+[0-9A-Fa-f]{4}:[0-9A-Fa-f]{8,16}\s+RizEngineDoorstopRecord\s+([0-9A-Fa-f]{16})\b', line)
        if match:
            found.append(int(match[1], 16)-proxy.base)
    require(len(found) == 1, 'missing/ambiguous private MAP record')
    record = found[0]
    info_matches = []
    for line in text.splitlines():
        match = re.match(r'^\s+[0-9A-Fa-f]{4}:[0-9A-Fa-f]{8,16}\s+RizWinHttpForward_GetInfo\s+([0-9A-Fa-f]{16})\b', line)
        if match:
            info_matches.append(int(match[1], 16)-proxy.base)
    require(len(info_matches) == 1, 'missing/ambiguous private WinHTTP GetInfo MAP symbol')
    info_rva = info_matches[0]
    require(0 < info_rva < proxy.size and any(s['rva'] <= info_rva < s['rva']+s['virtualSize']
            and s['characteristics'] & 0xe0000000 == 0x60000000 for s in proxy.sections), 'private WinHTTP GetInfo must be RX code')
    system = Pe(ordinary(system_path), system_reference=True)
    require(sha(system.path) == SYSTEM_WINHTTP_SHA256, 'previously fixed system WinHTTP identity changed')
    system_exports = [next(r for r in system.exports if r['name'] == name) for name in WINHTTP]
    for row in system_exports:
        require(not row['forwarded'] and any(s['rva'] <= row['rva'] < s['rva']+s['virtualSize']
                and s['characteristics'] & 0xe0000000 == 0x60000000 for s in system.sections), 'system WinHTTP EAT must contain direct RX code')
    sections = [s for s in proxy.sections if s['rva'] <= record and record+12448 <= s['rva']+max(s['virtualSize'], s['rawSize'])]
    require(0 < record <= proxy.size-12448 and len(sections) == 1 and sections[0]['characteristics'] & 0xe0000000 == 0xc0000000,
            'private record must fit complete non-executable RW image storage')
    for row in exports:
        code = proxy.bytes(row['rva'], 11)
        require(code[:3] == b'\x4c\x8b\x1d', 'unexpected WinHTTP typed stub')
    require(not output.exists() and not header.exists(), 'preserve completed generated contracts')
    lines = ['#pragma once', '#include <cstdint>', 'namespace RizEngineProductionColdContract {',
             'struct Image { std::uint32_t imageSize,timeDateStamp; std::uint64_t fileBytes; unsigned char sha256[32]; };',
             f'inline constexpr std::uint32_t RecordRva=0x{record:x}u;',
             f'inline constexpr std::uint32_t ProxyGetInfoRva=0x{info_rva:x}u;',
             f'inline constexpr std::uint32_t SystemExportDirectoryRva=0x{system.directories[0][0]:x}u;',
             f'inline constexpr std::uint32_t SystemExportDirectoryBytes=0x{system.directories[0][1]:x}u;',
             'inline constexpr Image Images[4]={']
    for image in [*images, system]:
        digest = ','.join(f'0x{x:02x}' for x in bytes.fromhex(sha(image.path)))
        lines.append(f'{{0x{image.size:x}u,0x{image.timestamp:x}u,{len(image.data)}ull,{{{digest}}}}},')
    lines += ['};', 'inline constexpr std::uint32_t ExportRvas[4]={'+','.join(f'0x{r["rva"]:x}u' for r in exports)+'};',
              'inline constexpr std::uint32_t SystemExportRvas[4]={'+','.join(f'0x{r["rva"]:x}u' for r in system_exports)+'};', '}']
    header.write_text('\n'.join(lines)+'\n', encoding='utf-8', newline='\n')
    result = dict(kind='owned-production-engine-cold-contract', schemaVersion=2, candidateManifest=dict(path=str(manifest_path), sha256=sha(manifest_path)),
                  images=[p.facts() for p in images], privateMap=dict(path=str(map_path), sha256=sha(map_path)), recordRva=record, recordBytes=12448,
                  proxyGetInfoRva=info_rva, systemImage=system.facts(), snapshotBytes=SNAPSHOT_BYTES,
                  phaseProofBytes=PHASE_BYTES, callsOffset=CALLS_OFFSET, callBytes=CALL_BYTES,
                  generatedHeader=dict(path=str(header), sha256=sha(header)), gameExecuted=False, targetImagesLoaded=False, productionAdmissionProved=False)
    output.write_text(json.dumps(result, indent=2)+'\n', encoding='utf-8', newline='\n')

def save(path, value):
    require(path.is_absolute() and not path.exists(), 'new absolute evidence output required')
    path.write_text(json.dumps(value, indent=2)+'\n', encoding='utf-8', newline='\n')

def cfg(image):
    rva, size = image.directories[10]
    require(rva and size >= 148, 'owned caller CFG load configuration required')
    raw = image.bytes(rva, 148)
    declared = struct.unpack_from('<I', raw)[0]
    check, dispatch, table, count = struct.unpack_from('<4Q', raw, 112)
    flags = struct.unpack_from('<I', raw, 144)[0]
    require(declared >= 148 and check and dispatch and table and 0 < count <= 65536
            and flags & 0x100 and flags & 0x400, 'owned caller lost actual CFG instrumentation')
    stride = 4 + ((flags >> 28) & 15)
    rows = image.bytes(table-image.base, count*stride)
    targets = [struct.unpack_from('<I', rows, i*stride)[0] for i in range(count)]
    require(targets == sorted(set(targets)), 'CFG table must be unique and sorted')
    for target in targets:
        require(any(s['rva'] <= target < s['rva']+s['virtualSize'] and s['characteristics'] & 0x20000000
                    for s in image.sections), 'CFG target must be image executable code')
    return dict(rva=rva, flags=flags, functionRvas=targets)

def inspect_build(root, contract_path, output):
    contract = json.loads(ordinary(contract_path).read_text(encoding='utf-8-sig'))
    require(contract['kind'] == 'owned-production-engine-cold-contract' and contract['schemaVersion'] == 2 and contract['recordBytes'] == 12448,
            'wrong fixed cold contract')
    system = Pe(ordinary(Path(contract['systemImage']['path'])), system_reference=True)
    require(sha(system.path) == contract['systemImage']['sha256'] == SYSTEM_WINHTTP_SHA256, 'held system WinHTTP identity changed')
    images = [Pe(ordinary(root/name)) for name in NAMES]
    for image, row in zip(images, contract['images']):
        require(sha(image.path) == row['sha256'], 'copied candidate identity differs')
    importer = Pe(ordinary(root/'RizProductionCold.Importer.dll'))
    host = Pe(ordinary(root/'RizProductionCold.Host.exe'))
    importer_names = ['RizProductionCold_Run', 'RizProductionCold_GetSnapshot']
    edges = [dict(source=host.path.name, destination=importer.path.name, symbols=host.edge(importer.path.name, importer_names)),
             dict(source=importer.path.name, destination=NAMES[0], symbols=importer.edge(NAMES[0], WINHTTP)),
             dict(source=NAMES[0], destination=NAMES[1], symbols=images[0].edge(NAMES[1], BOOT)),
             dict(source=NAMES[1], destination=NAMES[2], symbols=images[1].edge(NAMES[2], KERNEL))]
    require(len(importer.exports) == 2 and {r['name'] for r in importer.exports} == set(importer_names),
            'wrong owned Importer export set')
    require(not any(row['module'].casefold() in {name.casefold() for name in NAMES[1:]}
                    for image in (host, importer) for row in image.imports),
            'the caller must not obtain its own Bootstrap ABI imports')
    tls_rva, tls_bytes = importer.directories[9]
    require(tls_rva and tls_bytes >= 40, 'actual owned Importer TLS directory required')
    callback_table = struct.unpack_from('<Q', importer.bytes(tls_rva, 40), 24)[0]
    callbacks = []
    for index in range(32):
        value = struct.unpack('<Q', importer.bytes(callback_table-importer.base+index*8, 8))[0]
        if not value:
            break
        target = value-importer.base
        require(any(s['rva'] <= target < s['rva']+s['virtualSize'] and s['characteristics'] & 0x20000000
                    for s in importer.sections), 'TLS callback must be actual executable image code')
        callbacks.append(target)
    else:
        raise ValueError('unbounded owned TLS callback table')
    require(callbacks and importer.entry and host.entry, 'actual TLS, DllMain and executable entry required')
    for image in (host, importer):
        require(image.directories[3][0] and image.directories[3][1] >= 12, 'actual owned unwind metadata required')
    save(output, dict(kind='owned-production-engine-cold-pe-proof', schemaVersion=2,
                      contract=dict(path=str(contract_path), sha256=sha(contract_path)), candidateImages=[p.facts() for p in images],
                      importer=importer.facts(), host=host.facts(), ordinaryEdges=edges,
                      importerTlsCallbackRvas=callbacks, importerCfg=cfg(importer), hostCfg=cfg(host),
                      recordRva=contract['recordRva'], recordBytes=12448, proxyGetInfoRva=contract['proxyGetInfoRva'],
                      systemImage=system.facts(), snapshotBytes=SNAPSHOT_BYTES, phaseProofBytes=PHASE_BYTES,
                      callsOffset=CALLS_OFFSET, callBytes=CALL_BYTES,
                      fixedGameSeventeenEdgesProved=False, gameExecuted=False, productionAdmissionProved=False))

RECORD_FIELDS = ('version byteSize stage attempts threadId processId attachReason startupStatic '
                 'entryLastError versionLastError infoLastError verifyLastError afterInfoLastError exitLastError nativeVersion infoResult '
                 'verifyResult afterInfoResult admission result failure abortAttempts abortLastError abortInfoResult '
                 'abortInfoLastError bodyLastError exceptionCode detachAttempts retainedResponsibility selfPinned rejected processTermination '
                 'verifyAttempts infoReads proxyIatAttempts proxyIatResponsibility').split()

def verify_case(case_path, proof_path, pid, output):
    proof = json.loads(ordinary(proof_path).read_text(encoding='utf-8-sig'))
    case = json.loads(ordinary(case_path).read_text(encoding='utf-8-sig'))
    checks = 0
    def check(value, message):
        nonlocal checks
        checks += 1
        require(value, message)
    check(proof['kind'] == 'owned-production-engine-cold-pe-proof' and proof['schemaVersion'] == 2, 'wrong independently inspected PE proof')
    check(case['kind'] == 'owned-engine-production-cold-case' and case['schemaVersion'] == 2, 'wrong actual child artifact')
    check(case['actualProcessId'] == pid and pid > 0, 'actual child PID differs')
    check(case['passed'] is True and not case['error'] and case['sehCode'] == 0, 'owned child reported a failure')
    check((case['initialResult'], case['initialPhaseMask'], case['initialCalls'], case['runResult'], case['snapshotResult']) == (0, 6, 2, 0, 0),
          'actual TLS/DllMain observations must precede main')
    for name in ('httpRequestsIssued', 'hostInitializedBootstrap', 'importerCalledBootstrapAbi', 'gameExecuted', 'installed'):
        check(case['scope'][name] is False, 'fixture scope changed: '+name)
    raw_name = Path(case['raw']['path'])
    check(raw_name.name == str(raw_name) and not raw_name.is_absolute(), 'raw evidence must be a sibling basename')
    raw_path = ordinary(case_path.parent/raw_name)
    raw = raw_path.read_bytes()
    check(len(raw) == case['raw']['bytes'] == SNAPSHOT_BYTES and sha(raw_path) == case['raw']['sha256'], 'actual version-2 snapshot bytes differ')
    u = lambda at: struct.unpack_from('<I', raw, at)[0]
    q = lambda at: struct.unpack_from('<Q', raw, at)[0]
    check(struct.unpack_from('<6I', raw) == (2, SNAPSHOT_BYTES, 14, 3, 4, 0), 'raw snapshot envelope differs')
    check(q(24) > 0 and q(32) == 0, 'raw native checks/failures differ')
    expected_images = proof['candidateImages']
    phases = []
    record_values = []
    system_closes = []
    for index in range(3):
        at = 40+index*PHASE_BYTES
        phase, thread, before_error, after_error, matched, reserved = struct.unpack_from('<6I', raw, at)
        proxy, bootstrap, kernel, *addresses = struct.unpack_from('<8Q', raw, at+24)
        record_address = addresses.pop()
        check(phase == index+1 and thread == case['mainThreadId'] and matched == 1 and reserved == 0,
              'actual phase/thread/identity proof differs')
        check(before_error == after_error == 0x6c810001+index, 'read-only observation changed LastError')
        bases = [proxy, bootstrap, kernel]
        check(len(set(bases)) == 3 and all(bases), 'actual candidate modules must be distinct and loaded')
        check(addresses == [proxy+next(row['rva'] for row in expected_images[0]['exports'] if row['name'] == name) for name in WINHTTP],
              'ordinary WinHTTP IAT functions do not point to exact final-candidate EAT RVAs')
        check(record_address == proxy+proof['recordRva'], 'private record address differs from final MAP')
        for module in range(3):
            pos = at+88+module*64
            base, file_bytes, image_size, stamp, same, zero = struct.unpack_from('<2Q4I', raw, pos)
            actual_sha = raw[pos+32:pos+64].hex()
            expected = expected_images[module]
            check((base, file_bytes, image_size, stamp, same, zero, actual_sha) ==
                  (bases[module], expected['fileBytes'], expected['imageSize'], expected['timeDateStamp'], 1, 0, expected['sha256']),
                  'runtime SHA/PE proof differs from held final candidate')
        check((u(at+280), u(at+284)) == (12448, 0), 'private raw record is incomplete')
        record = raw[at+288:at+12736]
        fields = dict(zip(RECORD_FIELDS, struct.unpack_from('<36I', record)))
        check((fields['version'], fields['byteSize'], fields['stage'], fields['attempts']) == (1, 12448, 6, 1), 'actual Doorstop entry did not complete once')
        check((fields['threadId'], fields['processId'], fields['attachReason'], fields['startupStatic']) == (thread, pid, 1, 1), 'actual static attach ownership differs')
        check((fields['nativeVersion'], fields['infoResult'], fields['afterInfoResult'], fields['admission'], fields['result']) == (4, 0, 0, 2, 1),
              'actual no-write downgrade admission differs')
        check(fields['verifyResult'] == 0xffffffff and fields['verifyAttempts'] == 0 and fields['infoReads'] == 3,
              'the fixture must not mutate native ABI read/verify counters')
        for field in ('failure', 'abortAttempts', 'abortLastError', 'abortInfoResult', 'abortInfoLastError', 'exceptionCode',
                      'detachAttempts', 'retainedResponsibility', 'rejected', 'processTermination', 'proxyIatAttempts', 'proxyIatResponsibility'):
            check(fields[field] == 0, 'unexpected Doorstop responsibility/failure: '+field)
        check(fields['entryLastError'] == fields['versionLastError'] == fields['infoLastError'], 'initial Bootstrap ABI calls changed LastError')
        check(fields['bodyLastError'] == fields['afterInfoLastError'] == fields['exitLastError'], 'terminal Bootstrap ABI calls changed LastError')
        self_module, reserved_pointer = struct.unpack_from('<2Q', record, 144)
        check(self_module == proxy and reserved_pointer != 0, 'actual static Doorstop module/reserved pointer differs')
        before, after, terminal = record[160:4256], record[4256:8352], record[8352:12448]
        check(before == after and not any(terminal), 'immutable no-write publication changed or has a failure terminal')
        v = lambda pos: struct.unpack_from('<I', before, pos)[0]
        check((v(0), v(4), v(8)) == (4, 4096, 5) and 0 < v(12) <= 19, 'Bootstrap must actually publish Disabled for this owned non-game host')
        check((v(24), v(28), v(32), v(36), v(40), v(44)) == (thread, pid, 1, 1, 1, 2), 'raw V4 attach facts differ')
        check(not any(before[48:72]), 'configured/bound/commit/source/protection responsibility must all remain zero')
        check(0 < v(72) <= 64 and v(76) == 0 and v(124) == 0 and not any(before[3840:]), 'raw V4 event/reserved shape differs')
        check(struct.unpack_from('<Q', before, 80)[0] != 0, 'raw V4 requires the actual static attach pointer')
        for kind in range(4):
            row = before[512+kind*320:832+kind*320]
            check(struct.unpack_from('<Q', row, 48)[0] == 0, 'source entry responsibility must remain zero')
            check(not any(row[80:128]), 'source/protection counters must remain zero')
            check(not any(row[128:144]), 'helper ownership must remain zero')
        record_values.append(record)
        get_info_address, info_result, info_before, info_after, reference_matched = struct.unpack_from('<Q4I', raw, at+12736)
        check(get_info_address == proxy+proof['proxyGetInfoRva'] and info_result == 0 and reference_matched == 1,
              'actual private resolver GetInfo ABI proof differs')
        check(info_before == info_after, 'private resolver GetInfo changed LastError')
        pos = at+12760
        sys_base, sys_bytes, sys_size, sys_stamp, sys_same, sys_zero = struct.unpack_from('<2Q4I', raw, pos)
        system = proof['systemImage']
        check(system['sha256'] == SYSTEM_WINHTTP_SHA256, 'system reference lost its previous fixed identity')
        check(sys_base not in bases and sys_base and (sys_bytes, sys_size, sys_stamp, sys_same, sys_zero, raw[pos+32:pos+64].hex()) ==
              (system['fileBytes'], system['imageSize'], system['timeDateStamp'], 1, 0, SYSTEM_WINHTTP_SHA256),
              'actual system module SHA/PE/RX reference identity differs')
        resolver = raw[at+12824:at+14072]
        check(struct.unpack_from('<8I', resolver) == (1, 1248, 2, 0, 7, 63, 1, 1), 'raw resolver publication is not exact Ready V1')
        sys_module, held_file, self_module, active, initial, poison, resolved, file_index, file_bytes = struct.unpack_from('<9Q', resolver, 32)
        volume, sys_chars, loaded_chars, machine, image_size, stamp, export_rva, export_bytes, load_flags, count, last_error, unknown = struct.unpack_from('<12I', resolver, 104)
        check(sys_module == sys_base and held_file and self_module == proxy and active == resolved and active
              and len({active, initial, poison}) == 3 and initial and poison and file_index and volume and file_bytes == sys_bytes,
              'raw resolver file/table ownership proof differs')
        check((machine, image_size, stamp, export_rva, export_bytes, load_flags, count, last_error, unknown) ==
              (0x8664, sys_size, sys_stamp, *system['directories'][0], 0x800, 4, 0, 0), 'raw resolver PE/export publication differs')
        system_addresses = list(struct.unpack_from('<4Q', resolver, 152))
        expected_addresses = [sys_base+next(row['rva'] for row in system['exports'] if row['name'] == name) for name in WINHTTP]
        check(system_addresses == expected_addresses and struct.unpack_from('<4I', resolver, 184) == (0x20,)*4,
              'raw system function addresses/protections differ from the fixed system EAT')
        check(struct.unpack_from('<2I', resolver, 200) == (0, 0), 'raw resolver contains an unexpected fixture selector')
        check(0 < sys_chars < 260 and sys_chars == loaded_chars, 'raw resolver path lengths differ')
        system_path = resolver[208:208+sys_chars*2].decode('utf-16-le')
        loaded_path = resolver[728:728+loaded_chars*2].decode('utf-16-le')
        check(system_path.casefold() == loaded_path.casefold() == system['path'].casefold()
              and resolver[208+sys_chars*2:210+sys_chars*2] == b'\0\0'
              and resolver[728+loaded_chars*2:730+loaded_chars*2] == b'\0\0', 'raw resolver is not the fixed System32 image path')
        system_closes.append(system_addresses[3])
        phases.append(dict(phase=phase, actualThreadId=thread, modules=bases, privateRecordAddress=record_address,
                           bootstrapState=v(8), bootstrapFailure=v(12), verifyAttempts=fields['verifyAttempts'], infoReads=fields['infoReads'],
                           proxyGetInfoAddress=get_info_address, systemModule=sys_base, systemCloseAddress=system_addresses[3],
                           resolverInfoSha256=hashlib.sha256(resolver).hexdigest(),
                           rawRecordSha256=hashlib.sha256(record).hexdigest()))
    check(record_values[0] == record_values[1] == record_values[2], 'TLS, DllMain and main must observe the same immutable disabled record')
    calls = []
    for index in range(4):
        at = CALLS_OFFSET+index*CALL_BYTES
        phase, kind, thread, incoming, outgoing, zero, address, handle, result, system_address, system_result, system_error = struct.unpack_from('<6I4Q2I', raw, at)
        check((phase, kind, thread, zero) == ((1, 2, case['mainThreadId'], 0), (2, 2, case['mainThreadId'], 0),
                                            (3, 1, case['mainThreadId'], 0), (3, 2, case['mainThreadId'], 0))[index], 'actual call ordering differs')
        check(address == struct.unpack_from('<Q', raw, 40+(phase-1)*PHASE_BYTES+48+(0 if kind == 1 else 3)*8)[0],
              'actual call did not use the held ordinary IAT target')
        if index < 2:
            check(handle == 0 and result == 0 and system_address == system_closes[phase-1]
                  and result == system_result and outgoing == system_error,
                  'early forwarded Close(NULL) differs from the independently called exact system function at the same phase')
        elif index == 2:
            check(handle == 0 and result != 0, 'main NO_PROXY session creation failed')
            check((system_address, system_result, system_error) == (0, 0, 0), 'main Open must not issue an extra system reference call')
        else:
            check(handle == calls[2]['rawReturn'] and result != 0, 'main must close its actual owned session')
            check((system_address, system_result, system_error) == (0, 0, 0), 'main must not close its session twice')
        check(incoming == (0x6c830000 if kind == 1 else 0x6c820000)+phase, 'actual LastError test sentinel differs')
        calls.append(dict(phase=phase, kind=kind, threadId=thread, apiAddress=address, handle=handle,
                          rawReturn=result, lastErrorBefore=incoming, lastErrorAfter=outgoing,
                          systemReferenceAddress=system_address, systemReferenceReturn=system_result, systemReferenceLastError=system_error))
    save(output, dict(kind='owned-production-engine-cold-verified-case', schemaVersion=2, passed=True, checks=checks,
                      actualChildPid=pid, case=dict(path=str(case_path), sha256=sha(case_path)),
                      raw=dict(path=str(raw_path), sha256=sha(raw_path)), phases=phases, calls=calls,
                      actualProductionDoorstopEntryExecuted=True, actualBootstrapDisabledObserved=True,
                      fixedGameSeventeenEdgesProved=False, productionAdmissionProved=False, gameExecuted=False, installed=False))

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--candidate', type=Path)
    parser.add_argument('--header', type=Path)
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--inspect', type=Path)
    parser.add_argument('--inspect-build', type=Path)
    parser.add_argument('--contract', type=Path)
    parser.add_argument('--case', type=Path)
    parser.add_argument('--pe-proof', type=Path)
    parser.add_argument('--child-pid', type=int)
    parser.add_argument('--system-winhttp', type=Path)
    args = parser.parse_args()
    require(args.output.is_absolute() and not args.output.exists(), 'new absolute output required')
    if args.case:
        require(args.pe_proof and args.child_pid, 'actual PE proof and child PID required')
        verify_case(args.case, args.pe_proof, args.child_pid, args.output)
    elif args.inspect_build:
        require(args.contract, 'fixed contract required')
        inspect_build(args.inspect_build, args.contract, args.output)
    elif args.inspect:
        image = Pe(ordinary(args.inspect))
        args.output.write_text(json.dumps(image.facts(), indent=2)+'\n', encoding='utf-8', newline='\n')
    else:
        require(args.candidate and args.candidate.is_absolute() and args.header and args.header.is_absolute(), 'fixed candidate/header paths required')
        require(args.system_winhttp and args.system_winhttp.is_absolute(), 'fixed absolute system WinHTTP path required')
        prepare(args.candidate.resolve(), args.output, args.header, args.system_winhttp)

if __name__ == '__main__':
    main()
