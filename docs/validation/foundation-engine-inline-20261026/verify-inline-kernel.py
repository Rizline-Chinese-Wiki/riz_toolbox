"""Read-only PE/COFF/manifest verification; never load or execute the kernel.

This verifies a disk artifact and its present source hashes. It grants no game
admission, cancellation completion, runtime unwind proof, or publication right.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import struct
import sys

import capstone
from capstone import x86
import pefile


ORIGINAL_EXPORTS = {
    'RizEngineNodeContext_' + name for name in
    ('GetAbiVersion', 'GetLimit', 'GetFrameSize', 'GetCompletionSize',
     'GetEntry', 'Configure', 'Attach', 'GetInfo')
}
INLINE_EXPORTS = {
    'RizEngineNodeContext_' + name for name in
    ('GetInlineAbiVersion', 'GetInlineLimit', 'GetInlineFrameSize',
     'GetInlineEntry', 'ConfigureInline', 'AttachInline', 'GetInlineInfo')
}
EXPECTED_IMPORTS = {
    'ntdll.dll': {'__C_specific_handler'},
    'kernel32.dll': {'GetCurrentThreadId', 'GetLastError', 'SetLastError',
                     'AcquireSRWLockExclusive', 'ReleaseSRWLockExclusive',
                     'RaiseFailFastException'},
}
READ, WRITE, EXECUTE = 0x40000000, 0x80000000, 0x20000000
NONVOLATILE = {3, 5, 6, 7, 12, 13, 14, 15}
GPR_NUMBERS = {'rax': 0, 'rcx': 1, 'rdx': 2, 'rbx': 3, 'rsp': 4,
               'rbp': 5, 'rsi': 6, 'rdi': 7, 'r8': 8, 'r9': 9,
               'r10': 10, 'r11': 11, 'r12': 12, 'r13': 13,
               'r14': 14, 'r15': 15}


def sha(data):
    return hashlib.sha256(data).hexdigest()


class InvalidArtifact(ValueError):
    pass


class Verifier:
    def __init__(self, directory, result):
        self.directory = directory
        self.result = result
        self.data = b''
        self.pe = None
        self.functions = []
        self.unwind_cache = {}
        self.import_iat = {}
        self.default_library_resolution_disabled = False
        self.md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
        self.md.detail = True

    def require(self, condition, label):
        self.result['checks'] += 1
        if not condition:
            self.result['failedCheck'] = label
            raise InvalidArtifact(label)

    def file(self, path, expected=None):
        path = Path(path)
        self.require(path.is_absolute() and path.is_file(), 'Absolute regular input file: ' + str(path))
        content = path.read_bytes()
        digest = sha(content)
        if expected is not None:
            self.require(isinstance(expected, str) and re.fullmatch('[0-9a-f]{64}', expected) is not None,
                         'Canonical SHA256 in manifest: ' + str(path))
            self.require(digest == expected, 'Present file SHA256 matches manifest: ' + str(path))
        self.result['inputs'].append({'path': str(path.resolve()), 'bytes': len(content), 'sha256': digest})
        return content

    def section(self, rva, size=1):
        self.require(isinstance(rva, int) and isinstance(size, int) and rva > 0 and size > 0
                     and rva + size <= self.pe.OPTIONAL_HEADER.SizeOfImage,
                     'Positive image-bounded RVA range')
        matches = [s for s in self.pe.sections
                   if s.VirtualAddress <= rva and rva + size <= s.VirtualAddress + max(s.Misc_VirtualSize, s.SizeOfRawData)]
        self.require(len(matches) == 1, 'RVA range belongs to one unambiguous section')
        return matches[0]

    def read(self, rva, size):
        section = self.section(rva, size)
        delta = rva - section.VirtualAddress
        self.require(delta + size <= section.SizeOfRawData, 'RVA range has actual file bytes')
        offset = section.PointerToRawData + delta
        self.require(offset + size <= len(self.data), 'RVA file span is bounded')
        return self.data[offset:offset + size]

    def rx(self, rva, size=1):
        section = self.section(rva, size)
        self.require(section.Characteristics & EXECUTE and section.Characteristics & READ
                     and not section.Characteristics & WRITE, 'Code range is RX without W')
        self.read(rva, size)

    def ro(self, rva, size):
        section = self.section(rva, size)
        self.require(section.Characteristics & READ and not section.Characteristics & (WRITE | EXECUTE),
                     'Metadata range is read-only non-executable')
        return self.read(rva, size)

    def verify_manifest(self):
        manifest_path = self.directory / 'kernel-manifest.json'
        manifest_data = self.file(manifest_path)
        manifest = json.loads(manifest_data.decode('utf-8-sig'))
        self.require(manifest.get('schemaVersion') == 1 and manifest.get('kind') == 'engine-node-kernel-build',
                     'Kernel manifest schema and kind')
        self.require(manifest.get('inlineSideAbi') == 1 and manifest.get('legacyNodeTransportAbi') == 2,
                     'Manifest records independent inline ABI1 and unchanged typed transport ABI2')
        for flag in ('fixtureExecuted', 'gameExecuted', 'installed', 'productionFirstInstallVerified',
                     'engineCancellationCompleted'):
            self.require(manifest.get(flag) is False, 'Build manifest is not runtime evidence: ' + flag)
        self.require(type(manifest.get('entryFixtureBuilt')) is bool, 'Explicit entry fixture build choice')
        expected_files = {'RizToolbox.EngineNodeKernel.dll'}
        if manifest['entryFixtureBuilt']:
            expected_files.add('EngineNodeKernel.EntryFixture.exe')
        files = manifest.get('files', [])
        self.require(isinstance(files, list) and len(files) == len(expected_files), 'Complete bounded manifest file rows')
        self.require({row.get('name') for row in files} == expected_files, 'Exact manifest output names')
        for row in files:
            actual = self.directory / row['name']
            self.require(Path(row.get('path', '')).is_absolute() and Path(row['path']).resolve() == actual.resolve(),
                         'Manifest output path is the selected directory: ' + row['name'])
            self.file(actual, row.get('sha256'))
        repo = Path(__file__).resolve().parents[3]
        source_names = {
            'native/RizToolbox.JudgeContext/EngineNodeContext.cpp',
            'native/RizToolbox.JudgeContext/EngineNodeContext.h',
            'native/RizToolbox.JudgeContext/EngineInlineProbe.h',
            'native/RizToolbox.JudgeContext/EngineInlineProbeEntry.asm',
            'native/RizToolbox.JudgeContext/MidContext.h',
            'scripts/Build-EngineNodeKernel.ps1',
        }
        if manifest['entryFixtureBuilt']:
            source_names.add('native/RizToolbox.EngineNodeKernel/KernelEntryFixture.cpp')
        expected_sources = {(repo / name).resolve() for name in source_names}
        sources = manifest.get('sources', [])
        self.require(isinstance(sources, list) and len(sources) == len(expected_sources), 'Complete bounded source rows')
        actual_sources = {Path(row.get('path', '')).resolve() for row in sources}
        self.require(actual_sources == expected_sources, 'Exact canonical source/header/build-script set')
        for row in sources:
            self.file(row['path'], row.get('sha256'))
        self.result['manifestSha256'] = sha(manifest_data)
        self.result['manifestFiles'] = len(files)
        self.result['manifestSources'] = len(sources)

    def verify_link_command(self):
        path = self.directory / 'build-engine-node-kernel.cmd'
        data = self.file(path)
        lines = data.decode('utf-8-sig').splitlines()
        link_lines = [line.strip() for line in lines if re.match(r'^\s*link\s', line, re.IGNORECASE)]
        self.require(len(link_lines) == 1, 'One actual standalone kernel link command')
        line = link_lines[0]
        self.require(line.count('"') % 2 == 0 and not re.search(r'[&|<>^%\r\n]', line),
                     'Bounded literal link command without shell substitutions')
        # Build input paths forbid embedded quotes. Preserve quoted path pieces
        # inside switches such as /OUT:"C:\\folder name\\kernel.dll".
        token_pattern = r'(?:[^\s"]|"[^"]*")+'
        self.require(not re.sub(token_pattern, '', line).strip(), 'Complete literal link argument tokenization')
        tokens = [part.replace('"', '') for part in re.findall(token_pattern, line)]
        self.require(tokens and tokens[0].lower() == 'link', 'Actual linker command token')
        switches = [token.lower() for token in tokens[1:] if token.startswith(('/', '-'))]
        fixed_switches = {
            '/nologo', '/dll', '/noentry', '/nodefaultlib', '/machine:x64',
            '/incremental:no', '/opt:ref', '/opt:noicf', '/dynamicbase',
            '/nxcompat', '/cetcompat', '/guard:no', '/debug',
        }
        path_switches = {
            '/out:': self.directory / 'RizToolbox.EngineNodeKernel.dll',
            '/implib:': self.directory / 'RizToolbox.EngineNodeKernel.lib',
            '/pdb:': self.directory / 'RizToolbox.EngineNodeKernel.pdb',
        }
        self.require(len(switches) == len(fixed_switches) + len(path_switches),
                     'No extra link option can add an explicit CRT or whole archive')
        self.require(fixed_switches <= set(switches), 'Actual DLL link uses unconditional /NODEFAULTLIB and /NOENTRY')
        for prefix, expected in path_switches.items():
            matches = [token[len(prefix):] for token in tokens[1:] if token.lower().startswith(prefix)]
            self.require(len(matches) == 1 and Path(matches[0]).is_absolute()
                         and Path(matches[0]).resolve() == expected.resolve(), 'Exact linker output path: ' + prefix)
        inputs = [token for token in tokens[1:] if not token.startswith(('/', '-'))]
        self.require(len(inputs) == 4 and inputs[-1].lower() == 'kernel32.lib',
                     'Exact explicit inputs: two owned objects, SEH import archive, kernel32 import archive')
        expected_inputs = [self.directory / name for name in
                           ('EngineNodeContext.obj', 'EngineInlineProbeEntry.obj', 'seh.lib')]
        for actual, expected in zip(inputs[:-1], expected_inputs):
            self.require(Path(actual).is_absolute() and Path(actual).resolve() == expected.resolve(),
                         'Exact explicit object/import-archive input: ' + expected.name)
        self.file(expected_inputs[-1])
        self.default_library_resolution_disabled = True
        self.result['linkCommand'] = {
            'path': str(path), 'sha256': sha(data), 'arguments': tokens[1:],
            'nodefaultlib': True, 'noentry': True,
            'explicitCrtLibraries': False, 'defaultLibraryHintsHonored': False,
            'scope': 'Recorded command is checked alongside actual final PE imports and object initializer records.',
        }

    def verify_coff(self, name):
        data = self.file(self.directory / name)
        self.require(len(data) >= 20, 'COFF header is bounded: ' + name)
        machine, count, _, symbol_offset, symbol_count, optional_size, _ = struct.unpack_from('<HHIIIHH', data)
        self.require(machine == 0x8664 and optional_size == 0 and 0 < count <= 4096,
                     'Standard AMD64 COFF object: ' + name)
        self.require(20 + count * 40 <= len(data) and 0 < symbol_count <= 1000000
                     and symbol_offset >= 20 + count * 40 and symbol_offset + symbol_count * 18 + 4 <= len(data),
                     'COFF section and symbol tables are bounded: ' + name)
        string_offset = symbol_offset + symbol_count * 18
        string_size = struct.unpack_from('<I', data, string_offset)[0]
        self.require(string_size >= 4 and string_offset + string_size <= len(data), 'COFF string table bounded: ' + name)
        strings = data[string_offset:string_offset + string_size]

        def string_at(offset):
            self.require(4 <= offset < len(strings), 'COFF long-name offset bounded')
            end = strings.find(b'\0', offset)
            self.require(end != -1, 'COFF long name terminated')
            return strings[offset:end].decode('ascii')

        sections = []
        directives = []
        for i in range(count):
            header = struct.unpack_from('<8sIIIIIIHHI', data, 20 + i * 40)
            short, _, _, size, offset, reloc_offset, _, reloc_count, _, characteristics = header
            section_name = short.split(b'\0', 1)[0].decode('ascii')
            if section_name.startswith('/'):
                section_name = string_at(int(section_name[1:]))
            self.require(not section_name.lower().startswith(('.crt', '.tls')), 'No COFF CRT/TLS initializer section')
            self.require(not characteristics & EXECUTE or not characteristics & WRITE, 'COFF sections obey W^X')
            if size:
                # Uninitialized .bss has a virtual length without raw bytes.
                uninitialized = bool(characteristics & 0x80)
                self.require((offset == 0 and uninitialized) or (offset >= 20 + count * 40 and offset + size <= len(data)),
                             'COFF section contents bounded')
                content = data[offset:offset + size] if offset else b''
                if characteristics & WRITE:
                    self.require(not any(content), 'No nonzero writable global initialization in COFF')
                if section_name == '.drectve':
                    directives.append(content.decode('ascii'))
            if reloc_count:
                self.require(reloc_offset > 0 and reloc_offset + reloc_count * 10 <= len(data), 'COFF relocation table bounded')
            sections.append(section_name)
        symbol_names = []
        i = 0
        while i < symbol_count:
            start = symbol_offset + i * 18
            raw_name = data[start:start + 8]
            if raw_name[:4] == b'\0' * 4:
                symbol_name = string_at(struct.unpack_from('<I', raw_name, 4)[0])
            else:
                symbol_name = raw_name.split(b'\0', 1)[0].decode('ascii')
            auxiliary = data[start + 17]
            self.require(i + 1 + auxiliary <= symbol_count, 'COFF auxiliary symbol records bounded')
            self.require(not re.search(r'^\?\?__[EF]|_GLOBAL__sub_I|__static_initialization_and_destruction_0|'
                                       r'_initterm|__scrt|__crt|__dyn_tls|_tls_used|_tls_index|_Init_thread_|'
                                       r'(^|_)atexit$|(^|_)onexit$|__xc_[az]', symbol_name),
                         'No COFF dynamic-global/TLS/CRT initializer symbol')
            symbol_names.append(symbol_name)
            i += 1 + auxiliary
        joined = '\n'.join(directives)
        hints = [match.strip('"') for match in re.findall(r'(?:^|\s)[/\-]DEFAULTLIB\s*:("[^"]*"|[^\s]+)',
                                                         joined, re.IGNORECASE)]
        # A compiler-emitted default-library pragma is a linker hint, not an
        # actual dependency or initializer. The verified standalone link uses
        # unconditional /NODEFAULTLIB and exactly the two owned objects plus
        # SEH/kernel32 import archives. Final PE imports and init evidence are
        # still checked independently below; no source change is required to
        # remove an inert libcpmt hint.
        self.require(self.default_library_resolution_disabled, 'Recorded link disables all COFF default-library resolution')
        self.result['coff'].append({'name': name, 'sha256': sha(data), 'sections': count,
                                   'symbols': len(symbol_names), 'dynamicInitializers': False,
                                   'defaultLibraryHints': hints,
                                   'defaultLibraryResolutionDisabled': True})

    def verify_pe(self):
        self.data = self.file(self.directory / 'RizToolbox.EngineNodeKernel.dll')
        self.pe = pefile.PE(data=self.data, fast_load=False)
        pe = self.pe
        self.require(pe.FILE_HEADER.Machine == 0x8664 and pe.OPTIONAL_HEADER.Magic == 0x20b, 'AMD64 PE32+')
        self.require(pe.FILE_HEADER.Characteristics & 0x2000 and pe.OPTIONAL_HEADER.AddressOfEntryPoint == 0,
                     'DLL with an exact zero loader entry point')
        self.require(pe.FILE_HEADER.PointerToSymbolTable == 0 and pe.FILE_HEADER.NumberOfSymbols == 0, 'No unexpected PE COFF symbol payload')
        self.require(pe.OPTIONAL_HEADER.SizeOfHeaders <= len(self.data)
                     and pe.OPTIONAL_HEADER.NumberOfRvaAndSizes == 16
                     and len(pe.OPTIONAL_HEADER.DATA_DIRECTORY) == 16, 'PE optional header and sixteen directories bounded')
        self.require(pe.OPTIONAL_HEADER.DllCharacteristics & 0x160 == 0x160, 'ASLR/high-entropy/NX characteristics')
        self.require({s.Name.rstrip(b'\0') for s in pe.sections} == {b'.text', b'.rdata', b'.data', b'.pdata', b'.reloc'}
                     and len(pe.sections) == 5, 'Exact five sections without hidden CRT/TLS sections')
        previous_end = pe.OPTIONAL_HEADER.SizeOfHeaders
        sections = []
        for section in pe.sections:
            start, size = section.VirtualAddress, max(section.Misc_VirtualSize, section.SizeOfRawData)
            self.require(size > 0 and start >= previous_end and start + size <= pe.OPTIONAL_HEADER.SizeOfImage,
                         'Ordered nonoverlapping image-bounded sections')
            self.require(start % pe.OPTIONAL_HEADER.SectionAlignment == 0, 'Section virtual alignment')
            self.require(section.Characteristics & READ and not (section.Characteristics & EXECUTE and section.Characteristics & WRITE),
                         'Readable sections with no writable executable mapping')
            if section.SizeOfRawData:
                self.require(section.PointerToRawData >= pe.OPTIONAL_HEADER.SizeOfHeaders
                             and section.PointerToRawData + section.SizeOfRawData <= len(self.data), 'Section raw data bounded')
                if section.Characteristics & WRITE:
                    self.require(not any(self.data[section.PointerToRawData:section.PointerToRawData + section.SizeOfRawData]),
                                 'Writable PE globals are zero initialized')
            elif section.Characteristics & WRITE:
                self.require(section.PointerToRawData == 0, 'Uninitialized writable PE section has no disk initializer bytes')
            sections.append({'name': section.Name.rstrip(b'\0').decode('ascii'), 'rva': start,
                             'virtualBytes': section.Misc_VirtualSize, 'rawBytes': section.SizeOfRawData,
                             'characteristics': section.Characteristics})
            previous_end = start + size
        self.result['sections'] = sections
        for index, directory in enumerate(pe.OPTIONAL_HEADER.DATA_DIRECTORY):
            if directory.VirtualAddress or directory.Size:
                self.require(directory.VirtualAddress > 0 and directory.Size > 0, 'Nonempty directory has a bounded pair')
                if index == 4:
                    self.require(directory.VirtualAddress + directory.Size <= len(self.data), 'Security file-offset directory bounded')
                else:
                    self.read(directory.VirtualAddress, directory.Size)
        for index, name in ((9, 'TLS'), (10, 'load configuration'), (11, 'bound imports'),
                            (13, 'delay imports'), (14, 'CLR'), (15, 'reserved')):
            directory = pe.OPTIONAL_HEADER.DATA_DIRECTORY[index]
            self.require(directory.VirtualAddress == 0 and directory.Size == 0, 'No ' + name + ' directory')
        self.verify_exports_imports()
        self.verify_all_unwind()
        self.verify_inline_assembly()
        self.verify_getters()
        debug_entries = getattr(pe, 'DIRECTORY_ENTRY_DEBUG', [])
        cet = []
        for entry in debug_entries:
            info = entry.struct
            content = self.read(info.AddressOfRawData, info.SizeOfData)
            self.require(info.PointerToRawData + info.SizeOfData <= len(self.data)
                         and self.data[info.PointerToRawData:info.PointerToRawData + info.SizeOfData] == content,
                         'Debug RVA and file-offset contents agree')
            if info.Type == 20:
                self.require(len(content) == 4, 'Extended DLL characteristics record bounded')
                cet.append(struct.unpack('<I', content)[0])
        self.require(len(cet) == 1 and cet[0] & 1, 'One actual CET-compatible extended characteristic')
        self.result['kernel'] = {'sha256': sha(self.data), 'bytes': len(self.data), 'machine': pe.FILE_HEADER.Machine,
                                 'entryPointRva': 0, 'imageBase': pe.OPTIONAL_HEADER.ImageBase,
                                 'imageBytes': pe.OPTIONAL_HEADER.SizeOfImage, 'cetCompatible': True,
                                 'tls': False, 'crtImports': False, 'loaderGlobalInitializers': False}

    def verify_exports_imports(self):
        pe = self.pe
        self.require(hasattr(pe, 'DIRECTORY_ENTRY_EXPORT') and hasattr(pe, 'DIRECTORY_ENTRY_IMPORT'), 'Actual export/import directories parsed')
        symbols = pe.DIRECTORY_ENTRY_EXPORT.symbols
        expected = ORIGINAL_EXPORTS | INLINE_EXPORTS
        self.require(len(symbols) == 15 and pe.DIRECTORY_ENTRY_EXPORT.struct.NumberOfFunctions == 15
                     and pe.DIRECTORY_ENTRY_EXPORT.struct.NumberOfNames == 15, 'Exactly fifteen named function slots')
        self.require(all(item.name for item in symbols) and {item.name.decode('ascii') for item in symbols} == expected,
                     'Exact original eight plus independent seven side exports')
        self.require({item.ordinal for item in symbols} == set(range(1, 16)) and len({item.address for item in symbols}) == 15,
                     'Export ordinals and unique function addresses')
        self.exports = {}
        for item in symbols:
            self.require(item.forwarder is None and item.address > 0, 'No export forwarding or holes')
            self.rx(item.address)
            self.exports[item.name.decode('ascii')] = item.address
        actual = {}
        for descriptor in pe.DIRECTORY_ENTRY_IMPORT:
            library = descriptor.dll.decode('ascii').lower()
            self.require(library in EXPECTED_IMPORTS and library not in actual, 'Exact unique allowed import DLL')
            names = set()
            for item in descriptor.imports:
                self.require(not item.import_by_ordinal and item.name is not None, 'Named imports only')
                name = item.name.decode('ascii')
                self.require(name not in names, 'No duplicate import symbol')
                names.add(name)
                iat = item.address - pe.OPTIONAL_HEADER.ImageBase
                self.ro(iat, 8)
                self.import_iat[(library, name)] = iat
            actual[library] = names
        self.require(actual == EXPECTED_IMPORTS, 'Exact ntdll handler and six kernel32 imports; no CRT/loader imports')
        self.result['exports'] = [{'name': name, 'rva': self.exports[name]} for name in sorted(self.exports)]
        self.result['imports'] = {dll: sorted(names) for dll, names in sorted(actual.items())}

    def function_for(self, rva):
        matches = [entry for entry in self.functions if entry[0] <= rva < entry[1]]
        self.require(len(matches) == 1, 'One actual runtime function covers code RVA')
        return matches[0]

    def decode_unwind(self, triple, ancestors=()):
        self.require(triple not in ancestors and len(ancestors) < 32, 'Acyclic bounded runtime-function unwind chain')
        if triple in self.unwind_cache:
            return self.unwind_cache[triple]
        begin, end, unwind_rva = triple
        self.require(begin < end and unwind_rva % 4 == 0, 'Runtime function range and unwind alignment')
        self.rx(begin, end - begin)
        version_flags, prolog, count, frame = self.ro(unwind_rva, 4)
        version, flags = version_flags & 7, version_flags >> 3
        self.require(version == 1 and flags <= 7 and not (flags & 4 and flags & 3), 'Valid v1 unwind flags without chain/handler conflict')
        self.require(prolog <= end - begin, 'Unwind prologue bounded by function')
        self.require((frame & 15) in NONVOLATILE or frame == 0, 'Nonvolatile unwind frame register or none')
        encoded_size = 4 + ((count + 1) & ~1) * 2
        encoded = self.ro(unwind_rva, encoded_size)
        codes, i, previous = [], 0, 256
        while i < count:
            offset, operation = encoded[4 + i * 2:6 + i * 2]
            opcode, info = operation & 15, operation >> 4
            self.require(offset <= prolog and offset <= previous, 'Unwind codes are descending and prologue bounded')
            previous = offset
            slots, value = 1, 0
            if opcode == 0:
                self.require(info in NONVOLATILE, 'PUSH_NONVOL uses a nonvolatile GPR'); value = 8
            elif opcode == 1:
                self.require(info in (0, 1), 'ALLOC_LARGE encoding')
                slots = 2 if info == 0 else 3
                self.require(i + slots <= count, 'ALLOC_LARGE extra slots bounded')
                value = struct.unpack_from('<H' if info == 0 else '<I', encoded, 6 + i * 2)[0] * (8 if info == 0 else 1)
                self.require(value > 0 and value % 8 == 0, 'ALLOC_LARGE size aligned')
            elif opcode == 2:
                value = info * 8 + 8
            elif opcode == 3:
                # UWOP_SET_FPREG derives its register and offset from the
                # UNWIND_INFO frame byte; its operation-info nibble is reserved
                # (Microsoft x64 exception-handling specification). Preserve
                # the raw nibble. This artifact contract accepts canonical zero
                # or MASM's observed copy of the frame-register number, rather
                # than mistaking the latter for a different unwind operation.
                frame_register = frame & 15
                self.require(frame_register in NONVOLATILE and info in (0, frame_register),
                             'SET_FPREG descriptor matches the actual header frame register')
                value = (frame >> 4) * 16
            elif opcode in (4, 5, 8, 9):
                self.require(info in NONVOLATILE if opcode in (4, 5) else 6 <= info <= 15, 'SAVE uses a nonvolatile register')
                slots = 2 if opcode in (4, 8) else 3
                self.require(i + slots <= count, 'SAVE extra slots bounded')
                value = struct.unpack_from('<H' if slots == 2 else '<I', encoded, 6 + i * 2)[0]
                value *= (8 if opcode == 4 else 16 if opcode == 8 else 1)
            elif opcode == 10:
                self.require(info in (0, 1), 'PUSH_MACHFRAME encoding'); value = 40 + info * 8
            else:
                self.require(False, 'No reserved/unsupported v1 unwind opcode')
            self.require(i + slots <= count, 'Complete encoded unwind operation')
            codes.append({'offset': offset, 'opcode': opcode, 'info': info, 'value': value, 'slots': slots})
            i += slots
        row = {'beginRva': begin, 'endRva': end, 'unwindRva': unwind_rva, 'version': version,
               'flags': flags, 'prologSize': prolog, 'codeCount': count,
               'frameRegister': frame & 15, 'frameOffset': (frame >> 4) * 16, 'codes': codes}
        if flags & 4:
            raw_chain = self.ro(unwind_rva + encoded_size, 12)
            chain = struct.unpack('<III', raw_chain)
            row['chain'] = list(chain)
            self.decode_unwind(chain, ancestors + (triple,))
            encoded += raw_chain
        elif flags & 3:
            handler = struct.unpack('<I', self.ro(unwind_rva + encoded_size, 4))[0]
            self.rx(handler, 6)
            instruction = next(self.md.disasm(self.read(handler, 6), handler, count=1), None)
            self.require(instruction is not None and instruction.mnemonic == 'jmp' and len(instruction.operands) == 1
                         and instruction.operands[0].type == x86.X86_OP_MEM
                         and instruction.operands[0].mem.base == x86.X86_REG_RIP,
                         'Exception handler is an actual RIP-relative import jump')
            target = instruction.address + instruction.size + instruction.operands[0].mem.disp
            self.require(target == self.import_iat[('ntdll.dll', '__C_specific_handler')], 'Exception handler resolves to exact imported __C_specific_handler')
            table_rva = unwind_rva + encoded_size + 4
            scope_count = struct.unpack('<I', self.ro(table_rva, 4))[0]
            self.require(0 < scope_count <= 4096, 'Bounded nonempty C-specific scope table')
            scope_data = self.ro(table_rva, 4 + scope_count * 16)
            scopes = []
            for scope in range(scope_count):
                first, last, filter_or_finally, jump = struct.unpack_from('<IIII', scope_data, 4 + scope * 16)
                self.require(first < last, 'Ordered C-specific scope range'); self.rx(first, last - first)
                if filter_or_finally not in (0, 1):
                    self.rx(filter_or_finally)
                else:
                    self.require(jump != 0, 'A finally scope has an actual handler function')
                if jump:
                    self.rx(jump)
                scopes.append({'beginRva': first, 'endRva': last, 'handlerRvaOrConstant': filter_or_finally, 'targetRva': jump})
            row['handlerRva'] = handler; row['scopes'] = scopes
            encoded += struct.pack('<I', handler) + scope_data
        row['rawBytes'] = len(encoded); row['rawSha256'] = sha(encoded)
        self.unwind_cache[triple] = row
        return row

    def verify_all_unwind(self):
        directory = self.pe.OPTIONAL_HEADER.DATA_DIRECTORY[3]
        self.require(directory.VirtualAddress > 0 and directory.Size > 0 and directory.Size % 12 == 0,
                     'Actual bounded exception directory consists of complete runtime records')
        table = self.ro(directory.VirtualAddress, directory.Size)
        self.functions = [struct.unpack_from('<III', table, i) for i in range(0, len(table), 12)]
        self.require(0 < len(self.functions) <= 4096, 'Bounded runtime-function count')
        previous_end = 0
        for triple in self.functions:
            self.require(triple[0] >= previous_end and triple[0] < triple[1], 'Ordered nonoverlapping runtime functions')
            previous_end = triple[1]
            self.decode_unwind(triple)
        self.result['runtimeFunctions'] = len(self.functions)
        self.result['unwindRecords'] = [self.unwind_cache[key] for key in sorted(self.unwind_cache)]
        self.result['exceptionDirectory'] = {'rva': directory.VirtualAddress, 'bytes': directory.Size, 'sha256': sha(table)}

    def leaf(self, name):
        rva = self.exports['RizEngineNodeContext_' + name]
        instructions = []
        # Read only one bounded section span, then stop at the first RET.
        for instruction in self.md.disasm(self.read(rva, 16), rva):
            instructions.append(instruction)
            if instruction.mnemonic == 'ret':
                break
        self.require(len(instructions) == 2 and instructions[-1].mnemonic == 'ret' and len(instructions[-1].operands) == 0,
                     'Exact two-instruction leaf getter: ' + name)
        return instructions

    def verify_getters(self):
        getters = {'GetAbiVersion': 2, 'GetLimit': 32, 'GetFrameSize': 112, 'GetCompletionSize': 32,
                   'GetInlineAbiVersion': 1, 'GetInlineLimit': 32, 'GetInlineFrameSize': 144}
        for name, expected in getters.items():
            instruction, _ = self.leaf(name)
            self.require(instruction.mnemonic == 'mov' and len(instruction.operands) == 2
                         and instruction.operands[0].type == x86.X86_OP_REG and instruction.reg_name(instruction.operands[0].reg) == 'eax'
                         and instruction.operands[1].type == x86.X86_OP_IMM and instruction.operands[1].imm == expected,
                         'Actual leaf getter immediate: ' + name)
        self.result['getterConstants'] = getters

    def verify_inline_assembly(self):
        getter, _ = self.leaf('GetInlineEntry')
        self.require(getter.mnemonic == 'lea' and len(getter.operands) == 2
                     and getter.operands[0].type == x86.X86_OP_REG and getter.operands[0].reg == x86.X86_REG_RAX
                     and getter.operands[1].type == x86.X86_OP_MEM and getter.operands[1].mem.base == x86.X86_REG_RIP
                     and getter.operands[1].mem.index == 0, 'GetInlineEntry really returns an image-relative address')
        entry = getter.address + getter.size + getter.operands[1].mem.disp
        triple = self.function_for(entry)
        self.require(triple[0] == entry, 'GetInlineEntry targets the beginning of an actual runtime function')
        body = self.read(entry, triple[1] - entry)
        instructions = list(self.md.disasm(body, entry))
        self.require(sum(i.size for i in instructions) == len(body), 'Entire inline ASM body decodes without gaps')
        expected, unwind_expected = [], []

        def reg(name):
            return ('reg', name)

        def mem(base, offset, size):
            return ('mem', base, offset, size)

        def imm(value):
            return ('imm', value)

        def emit(mnemonic, *operands, unwind=None):
            expected.append((mnemonic, operands, unwind))

        emit('push', reg('r13'), unwind=(0, 13, 8))
        emit('pushfq', unwind=(2, 0, 8))
        emit('sub', reg('rsp'), imm(0x1e8), unwind=(1, 0, 0x1e8))
        # The precise MASM artifact uses 0xd3 for this code word's second
        # byte. Keep that raw reserved nibble in the exact ASM comparison;
        # runtime frame semantics still come from the header's R13/0x80.
        emit('lea', reg('r13'), mem('rsp', 0x80, 8), unwind=(3, 13, 0x80))
        for name, offset in [('rbx', 0x58), ('rbp', 0x68), ('rsi', 0x70), ('rdi', 0x78),
                             ('r12', 0xa0), ('r14', 0xb0), ('r15', 0xb8)]:
            emit('mov', mem('rsp', offset, 8), reg(name), unwind=(4, GPR_NUMBERS[name], offset))
        for index in range(6, 16):
            offset = 0x120 + (index - 6) * 16
            emit('movdqa', mem('rsp', offset, 16), reg('xmm' + str(index)), unwind=(8, index, offset))
        prolog_instructions = len(expected)
        emit('cld')
        for name, offset in [('rax', 0x40), ('rcx', 0x48), ('rdx', 0x50), ('r8', 0x80),
                             ('r9', 0x88), ('r10', 0x90), ('r11', 0x98)]:
            emit('mov', mem('rsp', offset, 8), reg(name))
        for index in range(6):
            emit('movdqa', mem('rsp', 0xc0 + index * 16, 16), reg('xmm' + str(index)))
        emit('mov', mem('rsp', 0x20, 4), imm(1)); emit('mov', mem('rsp', 0x24, 4), imm(432))
        emit('lea', reg('rax'), mem('rsp', 0x1f8, 8)); emit('mov', mem('rsp', 0x30, 8), reg('rax'))
        emit('mov', reg('rax'), mem('rax', 0, 8)); emit('mov', mem('rsp', 0x28, 8), reg('rax'))
        emit('mov', reg('rax'), mem('rsp', 0x1e8, 8)); emit('mov', mem('rsp', 0x38, 8), reg('rax'))
        emit('lea', reg('rax'), mem('rsp', 0x200, 8)); emit('mov', mem('rsp', 0x60, 8), reg('rax'))
        emit('mov', reg('rax'), mem('rsp', 0x1f0, 8)); emit('mov', mem('rsp', 0xa8, 8), reg('rax'))
        emit('stmxcsr', mem('rsp', 0x1c0, 4)); emit('fnstcw', mem('rsp', 0x1c4, 2))
        emit('mov', mem('rsp', 0x1c6, 2), imm(0)); emit('mov', mem('rsp', 0x1c8, 8), imm(0))
        emit('lea', reg('rcx'), mem('rsp', 0x20, 8)); emit('call', ('dispatcher',))
        emit('ldmxcsr', mem('rsp', 0x1c0, 4)); emit('fldcw', mem('rsp', 0x1c4, 2))
        for index in range(16):
            emit('movdqa', reg('xmm' + str(index)), mem('rsp', 0xc0 + index * 16, 16))
        for name, offset in [('rax', 0x40), ('rcx', 0x48), ('rdx', 0x50), ('rbx', 0x58), ('rbp', 0x68),
                             ('rsi', 0x70), ('rdi', 0x78), ('r8', 0x80), ('r9', 0x88), ('r10', 0x90),
                             ('r11', 0x98), ('r12', 0xa0), ('r14', 0xb0), ('r15', 0xb8)]:
            emit('mov', reg(name), mem('rsp', offset, 8))
        emit('push', mem('rsp', 0x1e8, 8)); emit('popfq')
        emit('lea', reg('rsp'), mem('r13', 0x170, 8)); emit('pop', reg('r13')); emit('ret')
        self.require(len(instructions) == len(expected), 'Exact full-state ASM instruction count')
        dispatcher = None
        for instruction, (mnemonic, operands, unwind) in zip(instructions, expected):
            self.require(instruction.mnemonic == mnemonic and len(instruction.operands) == len(operands),
                         'Expected inline ASM instruction at RVA ' + hex(instruction.address))
            for actual, wanted in zip(instruction.operands, operands):
                if wanted[0] == 'reg':
                    valid = actual.type == x86.X86_OP_REG and instruction.reg_name(actual.reg) == wanted[1]
                elif wanted[0] == 'mem':
                    valid = (actual.type == x86.X86_OP_MEM and instruction.reg_name(actual.mem.base) == wanted[1]
                             and actual.mem.index == 0 and actual.mem.segment == 0 and actual.mem.disp == wanted[2]
                             and actual.size == wanted[3])
                elif wanted[0] == 'imm':
                    valid = actual.type == x86.X86_OP_IMM and actual.imm == wanted[1]
                else:
                    valid = actual.type == x86.X86_OP_IMM
                    if valid:
                        dispatcher = actual.imm; self.rx(dispatcher)
                        self.require(self.function_for(dispatcher)[0] == dispatcher, 'ASM CALL reaches an actual dispatcher function entry')
                self.require(valid, 'Exact inline ASM operand at RVA ' + hex(instruction.address))
            if unwind:
                opcode, info, value = unwind
                unwind_expected.append({'offset': instruction.address + instruction.size - entry,
                                        'opcode': opcode, 'info': info, 'value': value})
        row = self.unwind_cache[triple]
        prolog_size = instructions[prolog_instructions - 1].address + instructions[prolog_instructions - 1].size - entry
        self.require(row['flags'] == 0 and row['frameRegister'] == 13 and row['frameOffset'] == 0x80
                     and row['prologSize'] == prolog_size and row['codeCount'] == 39,
                     'Inline ASM has the complete exact native prologue/unwind frame')
        actual_codes = [{key: code[key] for key in ('offset', 'opcode', 'info', 'value')} for code in row['codes']]
        self.require(actual_codes == list(reversed(unwind_expected)), 'Every inline native unwind operation matches its actual instruction')
        self.require(dispatcher is not None and body[-10:] == bytes.fromhex('498da570010000415dc3'),
                     'One native dispatcher CALL and legal flag-preserving frame-pointer epilogue')
        self.result['inlineEntry'] = {'getterRva': getter.address, 'entryRva': entry, 'endRva': triple[1],
                                     'bodyBytes': len(body), 'bodySha256': sha(body), 'dispatcherRva': dispatcher,
                                     'instructionCount': len(instructions), 'prologBytes': prolog_size,
                                     'unwindRva': triple[2], 'unwindCodeSlots': row['codeCount'],
                                     'allGpr': True, 'allXmm': True, 'rflags': True, 'mxcsr': True,
                                     'fpcsr': True, 'savedDirectionFlagRestored': True,
                                     'actualCallReturnSlotUsed': True, 'runtimeUnwindExecuted': False,
                                     'instructions': [{'rva': i.address, 'bytes': i.bytes.hex(),
                                                       'mnemonic': i.mnemonic, 'operands': i.op_str} for i in instructions]}

    def unchanged(self):
        for row in self.result['inputs']:
            self.require(sha(Path(row['path']).read_bytes()) == row['sha256'], 'Input remained unchanged during disk verification: ' + row['path'])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--directory', required=True, help='Absolute existing kernel build directory')
    parser.add_argument('--output', required=True, help='Absolute fresh JSON output file')
    args = parser.parse_args()
    directory, output = Path(args.directory), Path(args.output)
    if not directory.is_absolute() or not output.is_absolute():
        parser.error('--directory and --output must be absolute paths')
    directory, output = directory.resolve(), output.resolve()
    if not directory.is_dir() or output.exists():
        parser.error('Use an existing build directory and a fresh nonexistent output file')
    result = {'schemaVersion': 1, 'kind': 'engine-inline-kernel-disk-verification', 'passed': False,
              'checks': 0, 'directory': str(directory), 'inputs': [], 'coff': [],
              'pefileVersion': pefile.__version__, 'capstoneVersion': capstone.__version__,
              'verifierPath': str(Path(__file__).resolve()), 'verifierSha256': sha(Path(__file__).read_bytes()),
              'diskOnly': True, 'dllLoaded': False, 'fixtureExecuted': False,
              'gameRuntimeVerified': False, 'productionAdmissionVerified': False,
              'cancellationVerified': False, 'engineCancellationCompleted': False,
              'limitations': ['Present file/source hashes and native disk structure are checked independently of manifest claims.',
                              'No DLL, native entry, fixture or game is loaded or executed.',
                              'Static unwind completeness is not a successful runtime unwind or publication receipt.']}
    try:
        verifier = Verifier(directory, result)
        verifier.verify_manifest()
        verifier.verify_link_command()
        verifier.verify_coff('EngineNodeContext.obj')
        verifier.verify_coff('EngineInlineProbeEntry.obj')
        verifier.verify_pe()
        verifier.unchanged()
        result['passed'] = True
    except Exception as error:
        result['error'] = type(error).__name__ + ': ' + str(error)
    output.parent.mkdir(parents=True, exist_ok=True)
    encoded = (json.dumps(result, ensure_ascii=False, indent=2) + '\n').encode('utf-8')
    with output.open('xb') as stream:
        stream.write(encoded)
    print(json.dumps({'passed': result['passed'], 'checks': result['checks'], 'output': str(output),
                      'sha256': sha(encoded), 'gameRuntimeVerified': False,
                      'productionAdmissionVerified': False, 'cancellationVerified': False}))
    return 0 if result['passed'] else 1


if __name__ == '__main__':
    sys.exit(main())
