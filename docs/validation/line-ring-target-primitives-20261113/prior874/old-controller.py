"""Resume only the unfinished children of the interrupted 874-input numeric matrix.

This is a NEW controller and output directory. It never builds, rewrites the
original run, or infers an exit code for its two interrupted children. A parent
must review this file before running it. No work occurs when it is imported.
"""
from concurrent.futures import ThreadPoolExecutor
import ast
import ctypes
import datetime as dt
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import subprocess
import sys
import threading
import time


HERE = Path(__file__).resolve().parent
WORK = HERE.parent
ROOT = HERE.parents[2]
ORIGINAL = Path('D:/riz-build-cache-next/line-ring-numeric-integration-20261113/main-r1')
OUT = ORIGINAL.parent / 'recovery-r1'
OBSERVATION = WORK / 'interruption-observation-r1.json'
ORIGINAL_RESULT_SHA = 'ad6a6cd989ba8a4f36578ddc98819989b9747bcb727f0692de0a7c8304ca92ee'
OBSERVATION_SHA = '764325acf1103d9dc876057f588ae697cf7a734686f3789166b943c6487848af'
DOTNET = Path('C:/Program Files/dotnet/dotnet.exe')
PREVIOUS = ROOT / 'docs/validation/line-ring-actual-source-20261113'
FIXTURE_RECORD = ROOT / 'docs/validation/canvas-basis-shared-source-20261112/build-test-result.json'
SIX = tuple('RizToolbox.' + name for name in ('Sdk', 'Core', 'Runtime', 'Adapter', 'Hosting', 'Loader'))
TESTS = tuple('RizToolbox.' + name + '.Tests' for name in ('Judgements', 'Runtime', 'AudioContent'))
BUILD = ['build-six'] + ['build-' + test for test in TESTS] + ['build-solution']
INITIAL = BUILD + ['list-legacy-cases', 'invalid-legacy-0', 'invalid-legacy-301', 'invalid-legacy-not-an-int', 'numeric-13']
SOURCE = [f'source-{index:02}' for index in range(1, 17)]
BASIS = [f'basis-{index:02}' for index in range(1, 17)]
LEGACY = [f'legacy-{index:03}' for index in range(1, 301)]
TAIL = ['renderer-abi', 'audio-content', 'exact-static-read']
RETAINED_SET = set(INITIAL + SOURCE + BASIS[:5])
REMAINING = BASIS[5:] + LEGACY + TAIL
ALL_NAMES = set(INITIAL + SOURCE + BASIS + LEGACY + TAIL)
SHA_PATTERN = re.compile(r'[0-9a-f]{64}\Z')
MIN_COMMIT = 512 * 1024 * 1024
CHILD_TIMEOUT = 2700
MAX_CHILDREN = 2


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def sha(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, 'Duplicate JSON key: ' + key)
        result[key] = value
    return result


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'), object_pairs_hook=unique_object)


def write(path, value):
    path = Path(path)
    temporary = path.with_name(path.name + '.tmp')
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    os.replace(temporary, path)


def timestamp(value):
    require(isinstance(value, str), 'Timestamp is missing.')
    parsed = dt.datetime.fromisoformat(value)
    require(parsed.tzinfo is not None, 'Timestamp has no zone: ' + value)
    return parsed.astimezone(dt.timezone.utc)


def now():
    return dt.datetime.now(dt.timezone.utc).isoformat()


def relative_name(value):
    require(isinstance(value, str) and '\\' not in value, 'Unsafe relative name.')
    path = PurePosixPath(value)
    require(value == path.as_posix() and not path.is_absolute() and path.parts
            and not any(part in ('.', '..') or ':' in part for part in path.parts),
            'Unsafe relative name: ' + value)
    return value


def source_file(name):
    path = (ROOT / relative_name(name)).resolve()
    require(path.is_relative_to(ROOT.resolve()), 'Source escaped workspace: ' + name)
    return path


def contract():
    path = WORK / 'reviewed_main_contract.py'
    require(sha(path) == '376bdab1d06c34d169522c7be26a2762047dffc457d5f65d4846196b62a95cd4',
            'Reviewed contract bytes changed before parsing.')
    tree = ast.parse(path.read_text(encoding='utf-8'), filename=str(path))
    values = {}
    for node in tree.body:
        if isinstance(node, ast.Assign) and len(node.targets) == 1 and isinstance(node.targets[0], ast.Name):
            if node.targets[0].id in ('EXPECTED_MANIFESTS', 'EXPECTED_TARGETS', 'COMPILER_INPUT_COUNT'):
                values[node.targets[0].id] = ast.literal_eval(node.value)
    require(set(values) == {'EXPECTED_MANIFESTS', 'EXPECTED_TARGETS', 'COMPILER_INPUT_COUNT'},
            'Reviewed contract shape differs.')
    values['BUILD_CONTROLS'] = {'scripts/Build.ps1': values['EXPECTED_TARGETS']['scripts/Build.ps1']}
    return values


def current_inputs():
    """Exactly the enumeration in validate_main.py, including newly added files."""
    names = set(read(ROOT / 'docs/validation/canvas-basis-shared-source-20261112/inputs-before.json'))
    names.update(('RizToolbox.sln', 'Directory.Build.props', 'global.json'))
    for base, projects in (('src', SIX), ('tests', TESTS)):
        for project in projects:
            for folder, dirs, files in os.walk(ROOT / base / project):
                dirs[:] = [name for name in dirs if name not in ('bin', 'obj', 'artifacts', '.git')]
                for filename in files:
                    path = Path(folder) / filename
                    if path.suffix in ('.cs', '.csproj', '.props', '.targets', '.json'):
                        names.add(path.relative_to(ROOT).as_posix())
    return {name: sha(source_file(name)) for name in sorted(names)}


def available_commit():
    class MemoryStatus(ctypes.Structure):
        _fields_ = [('length', ctypes.c_ulong), ('load', ctypes.c_ulong),
                    ('totalPhysical', ctypes.c_ulonglong), ('availablePhysical', ctypes.c_ulonglong),
                    ('totalPageFile', ctypes.c_ulonglong), ('availablePageFile', ctypes.c_ulonglong),
                    ('totalVirtual', ctypes.c_ulonglong), ('availableVirtual', ctypes.c_ulonglong),
                    ('availableExtendedVirtual', ctypes.c_ulonglong)]
    value = MemoryStatus()
    value.length = ctypes.sizeof(value)
    require(ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(value)), 'Cannot read system commit.')
    return value.availablePageFile


def pid_identity(pid, observed_at):
    """An absent PID is safe; a present PID is safe only if reused after audit."""
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    kernel.OpenProcess.argtypes = (ctypes.c_ulong, ctypes.c_int, ctypes.c_ulong)
    kernel.OpenProcess.restype = ctypes.c_void_p
    kernel.GetProcessTimes.argtypes = (ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p,
                                       ctypes.c_void_p, ctypes.c_void_p)
    kernel.GetProcessTimes.restype = ctypes.c_int
    kernel.CloseHandle.argtypes = (ctypes.c_void_p,)
    kernel.CloseHandle.restype = ctypes.c_int

    class FileTime(ctypes.Structure):
        _fields_ = [('low', ctypes.c_ulong), ('high', ctypes.c_ulong)]

    handle = kernel.OpenProcess(0x1000, 0, pid)  # PROCESS_QUERY_LIMITED_INFORMATION
    if not handle:
        error = ctypes.get_last_error()
        require(error in (87, 1168), f'PID {pid}: cannot establish absence (Win32 {error}).')
        return {'pid': pid, 'status': 'absent'}
    try:
        creation = FileTime()
        exit_time = FileTime()
        kernel_time = FileTime()
        user_time = FileTime()
        require(kernel.GetProcessTimes(handle, ctypes.byref(creation), ctypes.byref(exit_time),
                                       ctypes.byref(kernel_time), ctypes.byref(user_time)),
                f'PID {pid}: cannot read creation time.')
        ticks = (creation.high << 32) | creation.low
        created = dt.datetime(1601, 1, 1, tzinfo=dt.timezone.utc) + dt.timedelta(microseconds=ticks // 10)
        require(created > observed_at, f'PID {pid} may still be an original controller/child.')
        return {'pid': pid, 'status': 'reusedAfterObservation', 'creationUtc': created.isoformat()}
    finally:
        kernel.CloseHandle(handle)


def artifacts_dir(original):
    path = Path(original['artifactsDirectory']).resolve()
    require(path == (ORIGINAL / 'artifacts').resolve(), 'Original artifact path changed.')
    return path


def dll(artifacts, test):
    return artifacts / 'bin' / test / 'release' / (test + '.dll')


def expected_arguments(name, artifacts):
    projects = {'build-six': ROOT / 'src/RizToolbox.Loader/RizToolbox.Loader.csproj',
                'build-solution': ROOT / 'RizToolbox.sln'}
    projects.update({'build-' + test: ROOT / 'tests' / test / (test + '.csproj') for test in TESTS})
    if name in projects:
        return list(map(str, (DOTNET, 'build', projects[name], '-c', 'Release', '--artifacts-path', artifacts,
                              '--verbosity', 'minimal', '--disable-build-servers', '-m:1',
                              '-p:UseSharedCompilation=false', '-p:NuGetAudit=false')))
    selected = dll(artifacts, TESTS[0])
    if name in SOURCE:
        return [str(DOTNET), str(selected), '--line-ring-source-case', str(SOURCE.index(name) + 1)]
    if name in BASIS:
        return [str(DOTNET), str(selected), '--canvas-basis-case', str(BASIS.index(name) + 1)]
    if name in LEGACY:
        return [str(DOTNET), str(selected), '--legacy-case', str(LEGACY.index(name) + 1)]
    if name.startswith('invalid-legacy-'):
        return [str(DOTNET), str(selected), '--legacy-case', name.removeprefix('invalid-legacy-')]
    return {'list-legacy-cases': [str(DOTNET), str(selected), '--list-legacy-cases'],
            'numeric-13': [str(DOTNET), str(selected), '--line-ring-numeric-checks'],
            'renderer-abi': [str(DOTNET), str(dll(artifacts, TESTS[1])), '--renderer-abi-checks'],
            'audio-content': [str(DOTNET), str(dll(artifacts, TESTS[2]))],
            'exact-static-read': [str(DOTNET), str(dll(artifacts, TESTS[1])), '--exact-static-field-read-checks']}[name]


def listing_gate(listing):
    require(isinstance(listing, list) and len(listing) == 300, 'Legacy listing count differs.')
    require([row['index'] for row in listing] == list(range(1, 301))
            and len({row['name'] for row in listing}) == 300
            and all(isinstance(row['name'], str) and row['name'] for row in listing),
            'Legacy listing indices or names differ.')
    require(listing == read(PREVIOUS / 'legacy-case-list.json'), 'Legacy case order changed from accepted source.')


def step_gate(row, owner, artifacts, listing, require_natural=False, controller_start=None, controller_end=None):
    """Bind command, log bytes, exit, RUN/PASS names and totals to one child."""
    name = row['name']
    require(name in ALL_NAMES, 'Unexpected step: ' + name)
    require(row.get('arguments') == expected_arguments(name, artifacts), 'Command differs: ' + name)
    require(Path(row['log']).resolve() == (owner / (name + '.log')).resolve(), 'Log path differs: ' + name)
    require(SHA_PATTERN.fullmatch(row['logSha256']) and sha(row['log']) == row['logSha256'],
            'Log bytes changed: ' + name)
    expected_exit = 2 if name.startswith('invalid-legacy-') else 0
    require(type(row.get('exitCode')) is int and row['exitCode'] == expected_exit
            and row.get('expectedExitCode') == expected_exit and row.get('matchesExpected') is True
            and row.get('timedOut') is False and row.get('resourceStop') is None,
            'Step has no natural expected terminal: ' + name)
    if require_natural:
        require(row.get('naturalExit') is True, 'Recovery child did not naturally exit: ' + name)
    started, finished = timestamp(row['startedUtc']), timestamp(row['finishedUtc'])
    require(started <= finished and (controller_start is None or controller_start <= started)
            and (controller_end is None or finished <= controller_end), 'Step timestamps differ: ' + name)
    body = Path(row['log']).read_text(encoding='utf-8-sig', errors='replace')
    total_pattern = r'(\d+)/(\d+) audio/content checks passed\.' if name == 'audio-content' else r'(\d+)/(\d+) passed'
    totals = [list(pair) for pair in re.findall(total_pattern, body)]
    runs = re.findall(r'^RUN (.+)$', body, re.MULTILINE)
    passes = re.findall(r'^PASS (.+)$', body, re.MULTILINE)
    require(row.get('totals') == totals and row.get('runNames') == runs and row.get('passNames') == passes
            and not re.search(r'^FAIL\b', body, re.MULTILINE), 'Log text differs from step record: ' + name)
    count = (13 if name == 'numeric-13' else 1 if name in SOURCE or name in BASIS or name in LEGACY
             else {'renderer-abi': 51, 'audio-content': 1001, 'exact-static-read': 6}.get(name))
    require(totals == ([[str(count), str(count)]] if count is not None else []), 'Total differs: ' + name)
    if name == 'list-legacy-cases':
        require(not runs and not passes and json.loads(body, object_pairs_hook=unique_object) == listing,
                'Actual legacy listing log differs.')
    elif name.startswith('invalid-legacy-') or name in BUILD:
        require(not runs and not passes, 'Unexpected case lines: ' + name)
    elif name in SOURCE or name in BASIS:
        prefix = 'Line ring source ' if name in SOURCE else 'Canvas basis '
        require(len(runs) == 1 and runs == passes and runs[0].startswith(prefix),
                'Case RUN/PASS differs: ' + name)
    elif name in LEGACY:
        expected_name = listing[LEGACY.index(name)]['name']
        require(runs == [expected_name] and passes == [expected_name], 'Legacy case name differs: ' + name)
    elif name == 'numeric-13':
        require(len(passes) == 13 and len(set(passes)) == 13 and runs == passes
                and all(value.startswith('Line ring numeric ') for value in passes), 'Numeric names differ.')
    else:
        require(not runs and len(passes) == count and len(set(passes)) == count,
                'Tail PASS names/count differ: ' + name)


def source_gate(original, baseline):
    applied_path = WORK / 'main-r1/application-record.json'
    applied = read(applied_path)
    spec = contract()
    require(applied.get('appliedToMain') is True and applied.get('sourceBatchAccepted') is True
            and applied.get('compilerInputCountAfter') == 874, 'Main application record is not the reviewed 874 graph.')
    require(sha(applied_path) == original['applicationRecordSha256']
            and sha(WORK / 'reviewed_main_contract.py') == original['reviewedContractSha256'] == applied['reviewedContractSha256'],
            'Application/contract changed since original build.')
    require(baseline == read(WORK / 'main-r1/main-inputs-after.json') == current_inputs()
            and len(baseline) == spec['COMPILER_INPUT_COUNT'] == 874, 'Current exact compiler inputs differ.')
    expected = read(PREVIOUS / 'inputs-after.json')
    for name, digest in spec['EXPECTED_TARGETS'].items():
        if name != 'scripts/Build.ps1':
            expected[name] = digest
    require(baseline == expected, '874 source inputs differ from reviewed source graph plus numeric targets.')
    require(all(sha(source_file(name)) == digest for name, digest in spec['EXPECTED_TARGETS'].items()),
            'A reviewed source/build target changed.')
    require(all(sha(WORK / name / 'patch-manifest.json') == digest for name, digest in spec['EXPECTED_MANIFESTS'].items()),
            'A reviewed package manifest changed.')
    require(applied['nonCompilerControlsAfter'] == spec['BUILD_CONTROLS'] == original['nonCompilerControlsBefore']
            and all(sha(source_file(name)) == digest for name, digest in spec['BUILD_CONTROLS'].items()),
            'Build control changed.')
    require(sha(PREVIOUS / 'validation-summary.json') == applied['sourceAcceptanceSummarySha256'],
            'Original accepted source scope changed.')


def compiled_gate(original, artifacts):
    products = original['productsBeforeTests']
    assemblies = original['testAssemblies']
    require(set(products) == set(SIX) and set(assemblies) == set(TESTS), 'Compiled graph identity differs.')
    for group, names in ((products, SIX), (assemblies, TESTS)):
        for name in names:
            require(set(group[name]) == {'.dll', '.pdb'}, 'Compiled pair incomplete: ' + name)
            for ext, digest in group[name].items():
                require(SHA_PATTERN.fullmatch(digest)
                        and sha(artifacts / 'bin' / name / 'release' / (name + ext)) == digest,
                        'Current compiled byte changed: ' + name + ext)
    copies = original['dependencyCopies']
    expected = {(test, component, ext) for test in TESTS for component in SIX[:4] for ext in ('.dll', '.pdb')}
    require(len(copies) == 24 and {(row['test'], row['component'], row['extension']) for row in copies} == expected,
            '24 dependency identities differ.')
    for row in copies:
        require(row['matches'] is True and row['sha256'] == products[row['component']][row['extension']]
                and sha(artifacts / 'bin' / row['test'] / 'release' / (row['component'] + row['extension'])) == row['sha256'],
                'Dependency copy differs: ' + row['test'] + '/' + row['component'])
    golden = original['numericGoldenFixture']
    golden_source = ROOT / 'tests/RizToolbox.Judgements.Tests/LineRingNumeric/single-rounding-goldens.json'
    golden_copy = dll(artifacts, TESTS[0]).parent / 'line-ring-numeric-goldens.json'
    require(Path(golden['outputPath']).resolve() == golden_copy.resolve()
            and golden['sourceSha256'] == golden['outputSha256'] == sha(golden_source) == sha(golden_copy),
            'Current golden fixture differs.')
    fixtures = read(FIXTURE_RECORD)['nativeFixtures']
    require(fixtures == original['nativeFixtures'] and len(fixtures) == 3, 'Native fixture record differs.')
    for key, row in fixtures.items():
        require(sha(row['path']) == row['sha256'], 'Native fixture changed: ' + key)
    return {'products': products, 'testAssemblies': assemblies, 'dependencyCopies': copies,
            'numericGoldenFixture': golden, 'nativeFixtures': fixtures}


def audit_original(check_commit=True):
    """Read-only full audit. The original has no controller terminal and no Case06/07 exit."""
    observation = read(OBSERVATION)
    require(sha(OBSERVATION) == OBSERVATION_SHA
            and sha(ORIGINAL / 'result.json') == observation['originalResultSha256'] == ORIGINAL_RESULT_SHA
            and Path(observation['originalResultPath']).resolve() == (ORIGINAL / 'result.json').resolve(),
            'The original unfinished result changed after the absence observation.')
    original = read(ORIGINAL / 'result.json')
    require(original.get('finishedUtc') is None and original.get('passed') is False
            and original.get('failure') is None and original.get('acceptedThisBatch') is False
            and original.get('sdkGoalComplete') is False and original.get('completeSeekPermitted') is False,
            'Original result acquired a terminal or acceptance claim.')
    require(original['maximumConcurrentTestChildren'] == MAX_CHILDREN
            and original['childTimeoutSeconds'] == CHILD_TIMEOUT
            and original['minimumAvailableCommitBytes'] == MIN_COMMIT,
            'Original resource controls differ.')
    require(sha(ORIGINAL / 'validation-control.py') == original['controlSha256'] == sha(WORK / 'validate_main.py')
            and sha(ORIGINAL / 'reviewed-main-contract.py') == original['reviewedContractSha256'],
            'Original actual controller or reviewed contract differs.')
    active = original['activeChildren']
    require(set(active) == {'basis-06', 'basis-07'} and len(original['steps']) == len(RETAINED_SET)
            and len({row['name'] for row in original['steps']}) == len(RETAINED_SET)
            and {row['name'] for row in original['steps']} == RETAINED_SET,
            'Original completed/active step boundary differs.')
    require([row['name'] for row in original['steps'][:len(INITIAL)]] == INITIAL
            and [row['name'] for row in original['steps'][-5:]] == BASIS[:5],
            'Original completed step prefix differs.')
    observed_at = timestamp(observation['observedAt'])
    require(observed_at > max(timestamp(row['startedUtc']) for row in active.values()),
            'Absence observation predates active children.')
    ids = {original['controllerPid']} | {row['pid'] for row in active.values()}
    rows = observation['pidObservations']
    require(len(rows) == 3 and {row['pid'] for row in rows} == ids
            and all(row['exists'] is False and row['creationDate'] is None for row in rows),
            'Observation did not establish all original PID absences.')
    pid_checks = [pid_identity(pid, observed_at) for pid in sorted(ids)]
    artifacts = artifacts_dir(original)
    baseline = read(ORIGINAL / 'inputs-before.json')
    source_gate(original, baseline)
    listing = read(ORIGINAL / 'legacy-case-list.json')
    listing_gate(listing)
    compiled_gate(original, artifacts)
    for row in original['steps']:
        step_gate(row, ORIGINAL, artifacts, listing, controller_start=timestamp(original['startedUtc']),
                  controller_end=observed_at)
    for name in ('basis-06', 'basis-07'):
        row = active[name]
        log = ORIGINAL / (name + '.log')
        require(Path(row['log']).resolve() == log.resolve() and type(row['pid']) is int,
                'Interrupted child identity differs: ' + name)
        body = log.read_text(encoding='utf-8-sig', errors='replace')
        require(len(re.findall(r'^RUN Canvas basis .+$', body, re.MULTILINE)) == 1
                and not re.search(r'^(PASS|FAIL) ', body, re.MULTILINE)
                and not re.search(r'\d+/\d+ passed', body),
                'Interrupted child log has an unrecorded terminal: ' + name)
    if check_commit:
        require(available_commit() >= MIN_COMMIT, 'Less than 512 MiB of system commit remains.')
    return original, observation, baseline, listing, pid_checks


def recovery_gate(recovery, original, observation, listing, require_terminal=False):
    """Shared publication audit of the new controller's completed child records."""
    artifacts = artifacts_dir(original)
    require(recovery['originalResultSha256'] == sha(ORIGINAL / 'result.json') == observation['originalResultSha256']
            and recovery['observationSha256'] == sha(OBSERVATION)
            and recovery['originalInputSha256'] == sha(ORIGINAL / 'inputs-before.json'),
            'Recovery provenance changed.')
    require(sha(OUT / 'recovery-control.py') == recovery['controlSha256'] == sha(HERE / 'resume.py'),
            'Executed recovery control differs from reviewed control.')
    require(recovery['retainedSteps'] == original['steps'] and recovery['originalActiveChildren'] == original['activeChildren']
            and recovery['maximumConcurrentTestChildren'] == MAX_CHILDREN
            and recovery['childTimeoutSeconds'] == CHILD_TIMEOUT
            and recovery['minimumAvailableCommitBytes'] == MIN_COMMIT,
            'Recovery retained boundary/resource controls differ.')
    require(read(OUT / 'original-result.json') == original
            and sha(OUT / 'original-result.json') == recovery['originalResultSha256'],
            'Byte-preserved original result differs.')
    require(read(OUT / 'inputs-before.json') == read(ORIGINAL / 'inputs-before.json'),
            'Recovery compiler input baseline differs.')
    require(recovery['compiledBaseline'] == compiled_gate(original, artifacts),
            'Recovery compiled baseline differs.')
    for name, digest in recovery['originalInterruptedLogs'].items():
        require(name in ('basis-06', 'basis-07') and sha(ORIGINAL / (name + '.log')) == digest
                and sha(OUT / 'interrupted-logs' / (name + '.log')) == digest,
                'Original partial child log changed: ' + name)
    require(set(recovery['originalInterruptedLogs']) == {'basis-06', 'basis-07'},
            'Missing original partial child logs.')
    steps = recovery['steps']
    names = [row['name'] for row in steps]
    require(len(names) == len(set(names)) and set(names) <= set(REMAINING), 'New child set contains duplicates/extras.')
    for row in steps:
        step_gate(row, OUT, artifacts, listing, require_natural=True,
                  controller_start=timestamp(recovery['startedUtc']),
                  controller_end=timestamp(recovery['finishedUtc']) if require_terminal else None)
    if require_terminal:
        require(recovery.get('finishedUtc') and recovery.get('passed') is True
                and not recovery.get('failure') and not recovery.get('activeChildren')
                and set(names) == set(REMAINING) and len(names) == len(REMAINING)
                and recovery.get('inputCount') == 874 and recovery.get('inputsUnchanged') is True
                and recovery.get('compiledBytesUnchangedDuringTests') is True
                and recovery.get('dependencyCopiesUnchangedDuringTests') is True
                and recovery.get('nativeFixturesUnchangedDuringTests') is True
                and recovery.get('numericGoldenFixtureUnchangedDuringTests') is True
                and recovery.get('applicationRecordUnchangedDuringTests') is True
                and recovery.get('reviewedContractUnchangedDuringTests') is True
                and recovery.get('nonCompilerControlsUnchanged') is True
                and recovery.get('originalEvidenceUnchanged') is True,
                'Recovery has no full natural successful terminal.')
        require(read(OUT / 'inputs-after.json') == read(OUT / 'inputs-before.json') == current_inputs(),
                'The exact source graph changed during recovery.')
        require(recovery['productsAfterTests'] == original['productsBeforeTests']
                and recovery['testAssembliesAfterTests'] == original['testAssemblies'],
                'Final compiled hashes differ.')
        require({row['name'] for row in original['steps'] + steps} == ALL_NAMES
                and len(original['steps'] + steps) == len(ALL_NAMES) == 345,
                'Combined 345-step matrix is incomplete.')
        for prefix, count in (('source-', 16), ('basis-', 16), ('legacy-', 300)):
            group = [row for row in original['steps'] + steps if row['name'].startswith(prefix)]
            require(len(group) == count and len({row['passNames'][0] for row in group}) == count,
                    'Unique case coverage differs: ' + prefix)
        require(recovery.get('acceptedThisBatch') is False and recovery.get('sdkGoalComplete') is False
                and recovery.get('completeSeekPermitted') is False and recovery.get('gameStarted') is False
                and recovery.get('installed') is False and recovery.get('default345SingleProcessExecuted') is False,
                'Offline/SDK acceptance boundary differs.')


def main():
    require(not OUT.exists(), 'Recovery output already exists; never overwrite a run.')
    original, observation, baseline, listing, pid_checks = audit_original()
    artifacts = artifacts_dir(original)
    require(not OUT.exists(), 'Recovery output appeared during preflight.')
    OUT.mkdir(parents=True, exist_ok=False)
    shutil.copyfile(__file__, OUT / 'recovery-control.py')
    shutil.copyfile(ORIGINAL / 'result.json', OUT / 'original-result.json')
    shutil.copyfile(ORIGINAL / 'inputs-before.json', OUT / 'inputs-before.json')
    shutil.copyfile(OBSERVATION, OUT / 'interruption-observation.json')
    (OUT / 'interrupted-logs').mkdir()
    interrupted_logs = {}
    for name in ('basis-06', 'basis-07'):
        source = ORIGINAL / (name + '.log')
        interrupted_logs[name] = sha(source)
        shutil.copyfile(source, OUT / 'interrupted-logs' / (name + '.log'))
        require(sha(OUT / 'interrupted-logs' / (name + '.log')) == interrupted_logs[name],
                'Original partial log changed during copy: ' + name)
    state = {'schema': 'line-ring-numeric-recovery-r1',
             'scope': 'Same compiled 874-input main numeric matrix; interrupted basis06/07 restarted, remaining children executed',
             'controllerPid': os.getpid(), 'startedUtc': now(), 'originalRun': str(ORIGINAL),
             'originalResultSha256': sha(ORIGINAL / 'result.json'), 'observationSha256': sha(OBSERVATION),
             'originalInputSha256': sha(ORIGINAL / 'inputs-before.json'), 'controlSha256': sha(__file__),
             'artifactsDirectory': str(artifacts), 'retainedSteps': original['steps'],
             'originalActiveChildren': original['activeChildren'], 'originalInterruptedLogs': interrupted_logs,
             'originalPidIdentityAtStart': pid_checks, 'originalUnfinishedOutcomePreserved': True,
             'compiledBaseline': compiled_gate(original, artifacts),
             'applicationRecordSha256': original['applicationRecordSha256'],
             'reviewedContractSha256': original['reviewedContractSha256'],
             'steps': [], 'activeChildren': {}, 'passed': False, 'acceptedThisBatch': False,
             'sdkGoalComplete': False, 'completeSeekPermitted': False, 'gameStarted': False,
             'installed': False, 'default345SingleProcessExecuted': False,
             'maximumConcurrentTestChildren': MAX_CHILDREN, 'childTimeoutSeconds': CHILD_TIMEOUT,
             'minimumAvailableCommitBytes': MIN_COMMIT}
    lock = threading.RLock()
    failed = threading.Event()
    env = os.environ.copy()
    env.update({'DOTNET_CLI_TELEMETRY_OPTOUT': '1', 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE': '1',
                'DOTNET_GENERATE_ASPNET_CERTIFICATE': 'false',
                'DOTNET_ADD_GLOBAL_TOOLS_TO_PATH': 'false', 'DOTNET_NOLOGO': '1'})
    for key, row in original['nativeFixtures'].items():
        env[key] = row['path']

    def save():
        with lock:
            write(OUT / 'result.json', state)

    def preflight():
        require(not failed.is_set(), 'Sibling failed before launch.')
        require(sha(ORIGINAL / 'result.json') == state['originalResultSha256']
                and sha(OBSERVATION) == state['observationSha256']
                and all(sha(ORIGINAL / (name + '.log')) == digest
                        for name, digest in interrupted_logs.items()),
                'Original interruption evidence changed.')
        require(current_inputs() == baseline, 'Frozen compiler graph changed before a child.')
        require(sha(WORK / 'main-r1/application-record.json') == state['applicationRecordSha256']
                and sha(WORK / 'reviewed_main_contract.py') == state['reviewedContractSha256']
                and all(sha(source_file(name)) == digest
                        for name, digest in original['nonCompilerControlsBefore'].items()),
                'Application/build controls changed before a child.')
        require(compiled_gate(original, artifacts) == state['compiledBaseline'],
                'Compiled bytes/fixtures changed before a child.')
        require(available_commit() >= MIN_COMMIT, 'Less than 512 MiB of system commit remains.')

    def step(name):
        try:
            preflight()
            arguments = expected_arguments(name, artifacts)
            log = OUT / (name + '.log')
            started = now()
            timed_out = False
            resource_stop = None
            forced_stop = False
            print('START ' + name, flush=True)
            with log.open('xb') as stream:
                child = subprocess.Popen(arguments, cwd=ROOT, env=env, stdin=subprocess.DEVNULL,
                                         stdout=stream, stderr=subprocess.STDOUT)
                with lock:
                    state['activeChildren'][name] = {'pid': child.pid, 'startedUtc': started, 'log': str(log)}
                    save()
                deadline = time.monotonic() + CHILD_TIMEOUT
                while child.poll() is None:
                    try:
                        child.wait(timeout=2)
                    except subprocess.TimeoutExpired:
                        commit = available_commit()
                        if failed.is_set() or commit < MIN_COMMIT or time.monotonic() >= deadline:
                            timed_out = time.monotonic() >= deadline
                            if not timed_out:
                                resource_stop = {'reason': 'Sibling failure' if failed.is_set() else
                                                 'Less than 512 MiB of system commit remains.',
                                                 'availableCommitBytes': commit, 'ownedPid': child.pid}
                            failed.set()
                            forced_stop = True
                            child.kill()
                            child.wait()
                            break
            body = log.read_text(encoding='utf-8-sig', errors='replace')
            pattern = r'(\d+)/(\d+) audio/content checks passed\.' if name == 'audio-content' else r'(\d+)/(\d+) passed'
            count = 1 if name in BASIS or name in LEGACY else {'renderer-abi': 51, 'audio-content': 1001,
                                                                'exact-static-read': 6}[name]
            totals = [list(pair) for pair in re.findall(pattern, body)]
            passes = re.findall(r'^PASS (.+)$', body, re.MULTILINE)
            runs = re.findall(r'^RUN (.+)$', body, re.MULTILINE)
            good = child.returncode == 0 and not timed_out and resource_stop is None and not forced_stop
            good = good and totals == [[str(count), str(count)]] and not re.search(r'^FAIL\b', body, re.MULTILINE)
            if name in BASIS:
                good = good and len(runs) == 1 and runs == passes and runs[0].startswith('Canvas basis ')
            elif name in LEGACY:
                expected_name = listing[LEGACY.index(name)]['name']
                good = good and runs == passes == [expected_name]
            else:
                good = good and not runs and len(passes) == count and len(set(passes)) == count
            row = {'name': name, 'arguments': arguments, 'startedUtc': started, 'finishedUtc': now(),
                   'pid': child.pid, 'exitCode': child.returncode, 'naturalExit': not forced_stop,
                   'expectedExitCode': 0, 'timedOut': timed_out, 'resourceStop': resource_stop,
                   'totals': totals, 'passNames': passes, 'runNames': runs, 'matchesExpected': bool(good),
                   'log': str(log), 'logSha256': sha(log)}
            with lock:
                state['steps'].append(row)
                state['activeChildren'].pop(name, None)
                save()
            print('END ' + name + ' exit=' + str(child.returncode) + ' expected=' + str(good), flush=True)
            require(good, 'Child did not meet its exact gate: ' + name)
        except Exception:
            failed.set()
            raise

    def parallel(names):
        with ThreadPoolExecutor(max_workers=MAX_CHILDREN) as pool:
            futures = [pool.submit(step, name) for name in names]
            try:
                for future in futures:
                    future.result()
            except Exception:
                failed.set()
                for future in futures:
                    future.cancel()
                raise

    save()
    try:
        require(state['originalResultSha256'] == observation['originalResultSha256'],
                'Original report changed while recovery was prepared.')
        preflight()
        parallel(BASIS[5:])
        parallel(LEGACY)
        for name in TAIL:
            step(name)
        preflight()
        state['productsAfterTests'] = {name: {ext: sha(artifacts / 'bin' / name / 'release' / (name + ext))
                                              for ext in ('.dll', '.pdb')} for name in SIX}
        state['testAssembliesAfterTests'] = {name: {ext: sha(artifacts / 'bin' / name / 'release' / (name + ext))
                                                   for ext in ('.dll', '.pdb')} for name in TESTS}
        state['compiledBytesUnchangedDuringTests'] = (state['productsAfterTests'] == original['productsBeforeTests']
                                                        and state['testAssembliesAfterTests'] == original['testAssemblies'])
        state['dependencyCopiesUnchangedDuringTests'] = all(
            sha(artifacts / 'bin' / row['test'] / 'release' / (row['component'] + row['extension'])) == row['sha256']
            for row in original['dependencyCopies'])
        state['nativeFixturesUnchangedDuringTests'] = all(sha(row['path']) == row['sha256']
                                                          for row in original['nativeFixtures'].values())
        state['numericGoldenFixtureUnchangedDuringTests'] = (
            sha(original['numericGoldenFixture']['outputPath']) == original['numericGoldenFixture']['sourceSha256'])
        state['applicationRecordUnchangedDuringTests'] = (
            sha(WORK / 'main-r1/application-record.json') == state['applicationRecordSha256'])
        state['reviewedContractUnchangedDuringTests'] = (
            sha(WORK / 'reviewed_main_contract.py') == state['reviewedContractSha256'])
        state['originalEvidenceUnchanged'] = (sha(ORIGINAL / 'result.json') == state['originalResultSha256']
                                               and sha(OBSERVATION) == state['observationSha256']
                                               and all(sha(ORIGINAL / (name + '.log')) == digest
                                                       for name, digest in interrupted_logs.items()))
        combined = original['steps'] + state['steps']
        require(len(combined) == len(ALL_NAMES) == 345 and {row['name'] for row in combined} == ALL_NAMES,
                'The required 345-step matrix is incomplete.')
        for prefix, count in (('source-', 16), ('basis-', 16), ('legacy-', 300)):
            group = [row for row in combined if row['name'].startswith(prefix)]
            require(len(group) == count and len({row['passNames'][0] for row in group}) == count,
                    'Current unique case coverage differs: ' + prefix)
        state['passed'] = all(state[key] for key in (
            'compiledBytesUnchangedDuringTests', 'dependencyCopiesUnchangedDuringTests',
            'nativeFixturesUnchangedDuringTests', 'numericGoldenFixtureUnchangedDuringTests',
            'applicationRecordUnchangedDuringTests', 'reviewedContractUnchangedDuringTests',
            'originalEvidenceUnchanged')) and not failed.is_set()
    except Exception as error:
        failed.set()
        state['failure'] = str(error)
    finally:
        after = current_inputs()
        write(OUT / 'inputs-after.json', after)
        state['inputCount'] = len(baseline)
        state['inputsUnchanged'] = baseline == after
        state['nonCompilerControlsUnchanged'] = all(
            sha(source_file(name)) == digest for name, digest in original['nonCompilerControlsBefore'].items())
        state['passed'] = (state['passed'] and state['inputsUnchanged']
                           and state['nonCompilerControlsUnchanged'] and not state['activeChildren'])
        state['finishedUtc'] = now()
        save()
        if state['passed']:
            try:
                recovery_gate(state, original, observation, listing, require_terminal=True)
            except Exception as error:
                state['passed'] = False
                state['failure'] = 'Terminal self-audit: ' + str(error)
                save()
        print(json.dumps({key: state.get(key) for key in ('passed', 'inputCount', 'inputsUnchanged', 'failure')}), flush=True)
    return 0 if state['passed'] else 1


if __name__ == '__main__':
    sys.exit(main())
