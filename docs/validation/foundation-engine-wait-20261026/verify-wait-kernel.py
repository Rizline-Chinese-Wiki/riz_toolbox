"""Verify the wait kernel without changing the frozen scheduler or inline verifiers."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import re
from capstone import x86_const as x86

BASELINE_SHA = '58dbaa71077d8f546a234d5f82167a999759f7498f93f4692b2c9ada335d9633'
baseline_path = Path(__file__).resolve().parent.parent / 'foundation-engine-inline-20261026/verify-inline-kernel.py'
assert hashlib.sha256(baseline_path.read_bytes()).hexdigest() == BASELINE_SHA
spec = importlib.util.spec_from_file_location('inline_disk_verifier', baseline_path)
base = importlib.util.module_from_spec(spec)
spec.loader.exec_module(base)
SCHEDULER_EXPORTS = {'RizEngineSchedulerContext_' + suffix for suffix in (
    'GetAbiVersion', 'GetLimit', 'GetFrameSize', 'GetCompletionSize', 'GetRowFrameSize',
    'GetEntry', 'GetDrainRowEntry', 'Configure', 'Attach', 'GetInfo')}
WAIT_EXPORTS = {'RizEngineWaitContext_' + suffix for suffix in ('GetAbiVersion', 'GetLimit', 'GetFrameSize', 'GetCompletionSize', 'GetPhaseFrameSize', 'GetEntry', 'GetPhaseEntry', 'Configure', 'Attach', 'GetInfo')}
OBJECTS = ('EngineNodeContext.obj', 'EngineInlineProbeEntry.obj', 'EngineSchedulerContext.obj', 'EngineSchedulerEntry.obj', 'EngineWaitContext.obj', 'EngineWaitEntry.obj')
SCHEDULER_VERIFIER_SHA = '776c92a4beef5a3b40db6b369512edeadeecc41d205939c1cff64df0846df73f'

class Verifier(base.Verifier):
    def leaf(self, name):
        if not name.startswith(('RizEngineSchedulerContext_', 'RizEngineWaitContext_')):
            return super().leaf(name)
        rva = self.exports[name]
        instructions = []
        for instruction in self.md.disasm(self.read(rva, 16), rva):
            instructions.append(instruction)
            if instruction.mnemonic == 'ret':
                break
        self.require(len(instructions) == 2 and instructions[-1].mnemonic == 'ret'
                     and not instructions[-1].operands, 'Exact two-instruction independent ABI getter')
        return instructions

    def verify_manifest(self):
        data = self.file(self.directory / 'kernel-manifest.json')
        value = json.loads(data.decode('utf-8-sig'))
        self.require(value.get('schemaVersion') == 1 and value.get('kind') == 'engine-node-kernel-build', 'Exact kernel manifest schema')
        self.require(value.get('inlineSideAbi') == 1 and value.get('legacyNodeTransportAbi') == 2
                     and value.get('schedulerObservationAbi') == 1 and value.get('schedulerTypedChannels') == 6
                     and value.get('schedulerFrameSize') == 256 and value.get('schedulerRowFrameSize') == 224
                     and value.get('sourceHashesUnchanged') is True, 'Independent scheduler ABI and build input fixity')
        self.require(value.get('waitObservationAbi') == 1 and value.get('waitTypedChannels') == 3
                     and value.get('waitPhysicalKinds') == 13 and value.get('waitPhaseSites') == 18
                     and value.get('waitFrameSize') == 240 and value.get('waitPhaseFrameSize') == 288, 'Exact independent wait layout and physical kinds')
        for flag in ('fixtureExecuted', 'gameExecuted', 'installed', 'productionFirstInstallVerified', 'engineCancellationCompleted'):
            self.require(value.get(flag) is False, 'No execution or admission inferred from build: ' + flag)
        self.require(type(value.get('entryFixtureBuilt')) is bool, 'Explicit optional entry fixture')
        names = {'RizToolbox.EngineNodeKernel.dll'}
        if value['entryFixtureBuilt']:
            names.add('EngineNodeKernel.EntryFixture.exe')
        self.require(len(value.get('files', [])) == len(names) and {row['name'] for row in value['files']} == names, 'Exact declared binaries')
        for row in value['files']:
            path = self.directory / row['name']
            self.require(Path(row['path']).resolve() == path.resolve(), 'Exact selected output path')
            self.file(path, row['sha256'])
        repo = Path(__file__).resolve().parents[3]
        names = {'native/RizToolbox.JudgeContext/' + name for name in (
            'EngineNodeContext.cpp', 'EngineNodeContext.h', 'EngineInlineProbe.h', 'EngineInlineProbeEntry.asm',
            'EngineSchedulerContext.cpp', 'EngineSchedulerContext.h', 'EngineSchedulerEntry.asm',
            'EngineWaitContext.cpp', 'EngineWaitContext.h', 'EngineWaitEntry.asm', 'MidContext.h')}
        names.add('scripts/Build-EngineNodeKernel.ps1')
        if value['entryFixtureBuilt']:
            names.add('native/RizToolbox.EngineNodeKernel/KernelEntryFixture.cpp')
        self.require(len(value.get('sources', [])) == len(names)
                     and {Path(row['path']).resolve() for row in value['sources']} == {(repo / name).resolve() for name in names}, 'Exact expanded compiler source set')
        for row in value['sources']:
            self.file(row['path'], row['sha256'])
        self.result.update(manifestSha256=base.sha(data), manifestFiles=len(value['files']), manifestSources=len(value['sources']))

    def verify_link_command(self):
        data = self.file(self.directory / 'build-engine-node-kernel.cmd')
        lines = [line.strip() for line in data.decode('utf-8-sig').splitlines() if re.match(r'^\s*link\s', line, re.I)]
        self.require(len(lines) == 1, 'One actual kernel linker command')
        line = lines[0]
        self.require(line.count('"') % 2 == 0 and not re.search(r'[&|<>^%\r\n]', line), 'Literal bounded link command')
        tokens = [item.replace('"', '') for item in re.findall(r'(?:[^\s"]|"[^"]*")+', line)]
        self.require(tokens[0].lower() == 'link', 'Exact linker tool')
        switches = {item.lower() for item in tokens[1:] if item.startswith(('/', '-'))}
        fixed = {'/nologo', '/dll', '/noentry', '/nodefaultlib', '/machine:x64', '/incremental:no', '/opt:ref',
                 '/opt:noicf', '/dynamicbase', '/nxcompat', '/cetcompat', '/guard:no', '/debug'}
        path_switches = {'/out:': 'RizToolbox.EngineNodeKernel.dll', '/implib:': 'RizToolbox.EngineNodeKernel.lib', '/pdb:': 'RizToolbox.EngineNodeKernel.pdb'}
        self.require(switches == fixed | {prefix + str(self.directory / name).lower() for prefix, name in path_switches.items()}, 'Exact no-entry no-default-library linker options')
        actual = [item for item in tokens[1:] if not item.startswith(('/', '-'))]
        expected = [str(self.directory / name) for name in OBJECTS + ('seh.lib',)] + ['kernel32.lib']
        self.require(len(actual) == len(expected) and [item.lower() for item in actual] == [item.lower() for item in expected], 'Only six owned objects, SEH thunk and kernel32 linked')
        self.file(self.directory / 'seh.lib')
        self.default_library_resolution_disabled = True
        self.result['linkCommand'] = dict(path=str(self.directory / 'build-engine-node-kernel.cmd'), sha256=base.sha(data), defaultLibraryHintsDisabled=True, objectFiles=list(OBJECTS))

    def verify_exports_imports(self):
        expected = base.ORIGINAL_EXPORTS | base.INLINE_EXPORTS | SCHEDULER_EXPORTS | WAIT_EXPORTS
        symbols = self.pe.DIRECTORY_ENTRY_EXPORT.symbols
        self.require(len(symbols) == 35 and self.pe.DIRECTORY_ENTRY_EXPORT.struct.NumberOfFunctions == 35
                     and self.pe.DIRECTORY_ENTRY_EXPORT.struct.NumberOfNames == 35, 'Exactly thirty-five exported function slots')
        self.require({item.name.decode('ascii') for item in symbols} == expected and len({item.address for item in symbols}) == 35
                     and {item.ordinal for item in symbols} == set(range(1, 36)), 'Exact original and new ABI exports')
        self.exports = {}
        for item in symbols:
            self.require(item.forwarder is None and item.address > 0, 'No export forwarder or hole')
            self.rx(item.address)
            self.exports[item.name.decode('ascii')] = item.address
        imports = {}
        for descriptor in self.pe.DIRECTORY_ENTRY_IMPORT:
            dll = descriptor.dll.decode('ascii').lower()
            self.require(dll in base.EXPECTED_IMPORTS and dll not in imports, 'One exact allowed import DLL')
            names = set()
            for item in descriptor.imports:
                self.require(not item.import_by_ordinal and item.name is not None, 'Named system import')
                name = item.name.decode('ascii')
                self.require(name not in names, 'Unique imported symbol')
                names.add(name)
                rva = item.address - self.pe.OPTIONAL_HEADER.ImageBase
                self.ro(rva, 8)
                self.import_iat[(dll, name)] = rva
            imports[dll] = names
        self.require(imports == base.EXPECTED_IMPORTS, 'Same bounded seven system imports without CRT')
        self.result['exports'] = [{'name': name, 'rva': rva} for name, rva in sorted(self.exports.items())]
        self.result['imports'] = {dll: sorted(names) for dll, names in imports.items()}

    def verify_scheduler(self):
        for suffix, value in [('GetAbiVersion', 1), ('GetLimit', 32), ('GetFrameSize', 256), ('GetCompletionSize', 48), ('GetRowFrameSize', 224)]:
            instructions = self.leaf('RizEngineSchedulerContext_' + suffix)
            instruction = instructions[0]
            self.require(instruction.mnemonic == 'mov' and len(instruction.operands) == 2
                         and instruction.operands[0].type == x86.X86_OP_REG and instruction.operands[0].reg == x86.X86_REG_EAX
                         and instruction.operands[1].type == x86.X86_OP_IMM and instruction.operands[1].imm == value, 'Actual scheduler getter constant: ' + suffix)
        original = self.exports['RizEngineNodeContext_GetInlineEntry']
        self.exports['RizEngineNodeContext_GetInlineEntry'] = self.exports['RizEngineSchedulerContext_GetDrainRowEntry']
        old_result = self.result['inlineEntry']
        try:
            self.verify_inline_assembly()
            self.result['schedulerRowEntry'] = self.result['inlineEntry']
        finally:
            self.exports['RizEngineNodeContext_GetInlineEntry'] = original
            self.result['inlineEntry'] = old_result

    def verify_wait(self):
        for suffix, value in [('GetAbiVersion', 1), ('GetLimit', 32), ('GetFrameSize', 240), ('GetCompletionSize', 48), ('GetPhaseFrameSize', 288)]:
            instructions = self.leaf('RizEngineWaitContext_' + suffix)
            instruction = instructions[0]
            self.require(instruction.mnemonic == 'mov' and len(instruction.operands) == 2
                         and instruction.operands[0].type == x86.X86_OP_REG and instruction.operands[0].reg == x86.X86_REG_EAX
                         and instruction.operands[1].type == x86.X86_OP_IMM and instruction.operands[1].imm == value, 'Actual wait getter constant: ' + suffix)
        original = self.exports['RizEngineNodeContext_GetInlineEntry']
        self.exports['RizEngineNodeContext_GetInlineEntry'] = self.exports['RizEngineWaitContext_GetPhaseEntry']
        old_result = self.result['inlineEntry']
        try:
            self.verify_inline_assembly()
            self.result['waitPhaseEntry'] = self.result['inlineEntry']
        finally:
            self.exports['RizEngineNodeContext_GetInlineEntry'] = original
            self.result['inlineEntry'] = old_result

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--directory', required=True)
    parser.add_argument('--output', required=True)
    args = parser.parse_args()
    directory, output = Path(args.directory).resolve(), Path(args.output).resolve()
    if output.exists():
        raise ValueError('Use a fresh output file.')
    result = dict(schemaVersion=1, kind='engine-wait-kernel-disk-verification', passed=False,
        checks=0, inputs=[], coff=[], directory=str(directory),
        verifierPath=str(Path(__file__).resolve()), verifierSha256=base.sha(Path(__file__).read_bytes()),
        diskOnly=True, dllLoaded=False, fixtureExecuted=False,
        gameRuntimeVerified=False, productionAdmissionVerified=False, cancellationVerified=False,
        inheritedVerifier=dict(path=str(baseline_path), sha256=BASELINE_SHA))
    verifier = Verifier(directory, result)
    try:
        verifier.file(baseline_path, BASELINE_SHA)
        verifier.file(Path(__file__).resolve().parent.parent / 'foundation-engine-scheduler-20261026/verify-scheduler-kernel.py', SCHEDULER_VERIFIER_SHA)
        verifier.verify_manifest()
        verifier.verify_link_command()
        for name in OBJECTS:
            verifier.verify_coff(name)
        verifier.verify_pe()
        verifier.verify_scheduler()
        verifier.verify_wait()
        verifier.unchanged()
        result['passed'] = True
    except Exception as error:
        result['error'] = type(error).__name__ + ': ' + str(error)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(dict(passed=result['passed'], checks=result['checks'], output=str(output), sha256=base.sha(output.read_bytes()))))
    return 0 if result['passed'] else 1

if __name__ == '__main__':
    raise SystemExit(main())
