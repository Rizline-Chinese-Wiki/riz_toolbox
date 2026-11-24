"""Apply the four frozen endpoint files after the preceding batch is archived.

Validation is deliberately incremental: the new class has no existing native
caller. It compiles the current solution and executes the endpoint and adjacent
numeric/source checks. It does not claim a new full 300-case regression run.
"""
import argparse
import datetime as dt
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import time

WORK = Path(__file__).resolve().parent
ROOT = WORK.parents[1]
PRIOR = ROOT / 'docs/validation/line-ring-target-primitives-20261113'
OUT = Path('D:/riz-build-cache-next/line-ring-endpoint-integration-20261113/main-r1')
BUILD = 'scripts/Build.ps1'
PROGRAM = 'tests/RizToolbox.Judgements.Tests/Program.cs'
TARGETS = {
    'src/RizToolbox.Adapter/Gameplay/NativeSeekLineEndpointLocalTarget.cs':
        (None, '980264063b778394518869c94ec804e07f7b2f81d703d452d3287c6cf3a3a74b'),
    'tests/RizToolbox.Judgements.Tests/LineRingNumeric/NativeSeekLineEndpointLocalChecks.cs':
        (None, 'cabf698b7ff6567d173cdc4906dde013d667a5d90c7b331c7440822767b1791e'),
    PROGRAM: ('9ec557252ffef81b85128bbbfa2cc17e495cd846b6c4016b0f6e641d708acc71',
              '879d1802d27b02dda601430443c70ee0ff8e2c7f3997f5cf68ba0def11414409'),
    BUILD: ('f9e008d3c1d1f4f9a42bc00e0c5770104823ce41101b5bca07cb47d00c0b9011',
            'e9e08174d5a26cf8ab011bee95b7b514a64fd82247fb1bd52999b4842038ade9'),
}
CONTROL_SHA = {
    'merge-manifest.json': '964d7f23b2161e888b26e883185cea717ff72f14b656e061937e6fc978fa4246',
    'payload.patch': '0b2da8f0b0849caac2c2c0b51fbefe9324fe03c4507ff8622a8e3d9950f4e606',
    'handoff.md': '406581f7069a04441c77642f095808c58fde1d0efd0dc1a4c9cbe55f82563092',
}
HELPER = ROOT / 'work/line-ring-source-validation-20261113/run_validation.py'
CAPTURE_TARGET = Path('D:/riz-build-cache-next/delivery-20261113-r1/Capture-FinalSixInputs.targets')
CAPTURE_TARGET_SHA = '9e18340a8e12a7aa9e991459b9e5175dfbb40b4e2966af6f2bf284ed19f3b7b4'

def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()

def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))

def write(path, value):
    Path(path).write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

def require(ok, reason):
    if not ok:
        raise RuntimeError(reason)

def now():
    return dt.datetime.now(dt.timezone.utc).isoformat()

def helper():
    require(sha(HELPER) == '61c2983ae117322b25ff96ad75b659b870fdadf9aada9274727248cd5ca98fd4', 'Input helper changed.')
    spec = importlib.util.spec_from_file_location('endpoint_current_input_helper', HELPER)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module

def safe_target(name):
    require(name in TARGETS, 'Target outside four-file whitelist.')
    path = ROOT
    for part in name.split('/'):
        path /= part
        require(not path.is_symlink(), 'Target path became a symlink.')
    require(path.resolve().is_relative_to(ROOT), 'Target path escaped workspace.')
    return path

def check_payload():
    require(ROOT == Path('C:/Users/71957/Desktop/riz_toolbox').resolve(), 'Unexpected workspace.')
    require(all(sha(WORK / name) == digest for name, digest in CONTROL_SHA.items()), 'Frozen payload control changed.')
    rows = read(WORK / 'merge-manifest.json')['files']
    require(len(rows) == 4 and {row['target'] for row in rows} == set(TARGETS), 'Payload inventory differs.')
    for row in rows:
        name = row['target']
        old, new = TARGETS[name]
        require(row['baseSha256'] == old and row['payloadSha256'] == new
                and sha(WORK / 'payload' / name) == new, 'Payload byte identity differs: ' + name)
        if old:
            require(sha(WORK / 'before' / name) == old, 'Before copy differs: ' + name)

def apply():
    check_payload()
    require(not (WORK / 'application-record.json').exists(), 'Application already recorded.')
    base = helper()
    summary = read(PRIOR / 'validation-summary.json')
    require(summary['acceptedThisBatch'] is True and summary['compilerInputs'] == 879
            and summary['naturalSteps'] == 362 and summary['sdkGoalComplete'] is False,
            'Preceding actual 879 batch must be accepted first.')
    inventory = read(PRIOR / 'artifact-manifest.json')
    require(all(sha(PRIOR / name) == digest for name, digest in inventory.items()), 'Accepted prior evidence changed.')
    before = read(PRIOR / 'run/inputs-after.json')
    require(len(before) == 879 and base.inputs() == before, 'Current source differs from accepted base.')
    require(sha(ROOT / BUILD) == TARGETS[BUILD][0], 'Base Build control changed.')
    after = dict(before)
    for name, (_, new) in TARGETS.items():
        if name != BUILD:
            after[name] = new
    require(len(after) == 881, 'Expected exactly two new compiler inputs.')
    changed = []
    record = {'startedUtc': now(), 'appliedToMain': False, 'priorAcceptedSummarySha256': sha(PRIOR / 'validation-summary.json'),
              'priorFullMatrixSourceCount': 879, 'newInputCount': 881,
              'newFullMatrixExecuted': False, 'newIncrementalValidationPassed': False,
              'sdkGoalComplete': False, 'completeSeekPermitted': False}
    try:
        expected = dict(before)
        for name, (old, new) in TARGETS.items():
            require(base.inputs() == expected, 'Concurrent compiler input change.')
            target = safe_target(name)
            require((sha(target) if target.exists() else None) == old, 'Target base changed: ' + name)
            current_build = TARGETS[BUILD][1] if BUILD in changed else TARGETS[BUILD][0]
            require(sha(ROOT / BUILD) == current_build, 'Concurrent Build control change.')
            data = (WORK / 'payload' / name).read_bytes()
            require(hashlib.sha256(data).hexdigest() == new, 'Payload changed before write.')
            if old is None:
                with target.open('xb') as stream:
                    stream.write(data)
            else:
                with tempfile.NamedTemporaryFile(dir=WORK, delete=False) as stream:
                    staged = Path(stream.name)
                    stream.write(data)
                try:
                    require(sha(safe_target(name)) == old, 'Concurrent target changed during staging.')
                    os.replace(staged, target)
                finally:
                    staged.unlink(missing_ok=True)
            changed.append(name)
            require(sha(target) == new, 'Written bytes differ.')
            if name != BUILD:
                expected[name] = new
        require(base.inputs() == after and sha(ROOT / BUILD) == TARGETS[BUILD][1], 'Post-application graph differs.')
        write(WORK / 'inputs-before.json', before)
        write(WORK / 'inputs-after.json', after)
        record['appliedToMain'] = True
        record['finishedUtc'] = now()
        write(WORK / 'application-record.json', record)
    except Exception as error:
        record['failure'] = str(error)
        record['rollback'] = []
        for name in reversed(changed):
            try:
                target = safe_target(name)
                old, new = TARGETS[name]
                require(sha(target) == new, 'Concurrent target left untouched.')
                if old is None:
                    target.unlink()
                else:
                    data = (WORK / 'before' / name).read_bytes()
                    require(hashlib.sha256(data).hexdigest() == old, 'Before copy changed.')
                    with tempfile.NamedTemporaryFile(dir=WORK, delete=False) as stream:
                        staged = Path(stream.name)
                        stream.write(data)
                    try:
                        require(sha(safe_target(name)) == new, 'Concurrent target left untouched.')
                        os.replace(staged, target)
                    finally:
                        staged.unlink(missing_ok=True)
                record['rollback'].append({'target': name, 'restored': True})
            except Exception as rollback_error:
                record['rollback'].append({'target': name, 'restored': False, 'reason': str(rollback_error)})
        write(WORK / 'application-attempt.json', record)
        raise
    print(json.dumps(record), flush=True)

def validate():
    check_payload()
    base = helper()
    inputs = read(WORK / 'inputs-after.json')
    require(read(WORK / 'application-record.json')['appliedToMain'] and base.inputs() == inputs and len(inputs) == 881,
            'Apply the exact 881 graph before validation.')
    require(sha(ROOT / BUILD) == TARGETS[BUILD][1] and not OUT.exists(), 'Changed Build or reused validation output.')
    OUT.mkdir(parents=True)
    artifacts = OUT / 'artifacts'
    state = {'startedUtc': now(), 'controllerPid': os.getpid(), 'passed': False, 'steps': [], 'activeChild': None,
             'scope': 'Incremental endpoint-local class and test registration; prior 879 full matrix is separate evidence',
             'compilerInputs': 881, 'defaultExpectedCases': 361, 'old300ExecutedThisBatch': False,
             'sdkGoalComplete': False, 'completeSeekPermitted': False}
    controls = {str(WORK / name): sha(WORK / name) for name in (*CONTROL_SHA, 'application-record.json', 'inputs-after.json')}
    controls[str(Path(__file__).resolve())] = sha(__file__)
    controls[str(ROOT / BUILD)] = TARGETS[BUILD][1]
    require(sha(CAPTURE_TARGET) == CAPTURE_TARGET_SHA, 'Actual Csc capture target changed.')
    controls[str(CAPTURE_TARGET)] = CAPTURE_TARGET_SHA
    state['controlsBefore'] = controls
    write(OUT / 'inputs-before.json', inputs)
    environment = os.environ.copy()
    environment.update({'DOTNET_GENERATE_ASPNET_CERTIFICATE': 'false', 'DOTNET_ADD_GLOBAL_TOOLS_TO_PATH': 'false',
                        'DOTNET_SKIP_FIRST_TIME_EXPERIENCE': '1', 'DOTNET_CLI_TELEMETRY_OPTOUT': '1', 'DOTNET_NOLOGO': '1'})
    fixtures = read(ROOT / 'docs/validation/canvas-basis-shared-source-20261112/build-test-result.json')['nativeFixtures']
    require(all(sha(row['path']) == row['sha256'] for row in fixtures.values()), 'Native fixture differs.')
    for key, row in fixtures.items():
        environment[key] = row['path']
    state['nativeFixtures'] = fixtures
    def save():
        write(OUT / 'result.json', state)
    def step(name, arguments, total=None, prefix=None, expected_exit=0):
        require(base.inputs() == inputs and all(sha(path) == digest for path, digest in controls.items()), 'Inputs or controls changed.')
        require(base.available_commit() >= 512 * 1024 * 1024, 'System commit below 512 MiB.')
        log = OUT / (name + '.log')
        print('START ' + name, flush=True)
        with log.open('xb') as stream:
            child = subprocess.Popen(list(map(str, arguments)), cwd=ROOT, env=environment,
                                     stdin=subprocess.DEVNULL, stdout=stream, stderr=subprocess.STDOUT)
            state['activeChild'] = {'name': name, 'pid': child.pid}
            save()
            deadline = time.monotonic() + 2700
            forced = False
            while child.poll() is None:
                try:
                    child.wait(timeout=2)
                except subprocess.TimeoutExpired:
                    if time.monotonic() >= deadline or base.available_commit() < 512 * 1024 * 1024:
                        forced = True
                        child.kill()
                        child.wait()
                        break
        body = log.read_text(encoding='utf-8-sig', errors='replace')
        passes = re.findall(r'^PASS (.+)$', body, re.MULTILINE)
        runs = re.findall(r'^RUN (.+)$', body, re.MULTILINE)
        totals = re.findall(r'(\d+)/(\d+) passed', body)
        good = child.returncode == expected_exit and not forced and not re.search(r'^FAIL\b', body, re.MULTILINE)
        if total is not None:
            good = good and totals == [(str(total), str(total))] and len(passes) == total and runs == passes
            good = good and len(set(passes)) == total and (not prefix or all(value.startswith(prefix) for value in passes))
        state['steps'].append({'name': name, 'arguments': list(map(str, arguments)), 'exitCode': child.returncode,
                               'naturalExit': not forced, 'matchesExpected': bool(good), 'passNames': passes,
                               'logSha256': sha(log)})
        state['activeChild'] = None
        save()
        require(good, 'Step did not meet gate: ' + name)
        print('END ' + name + ' exit=' + str(child.returncode), flush=True)
        return body
    try:
        dotnet = base.DOTNET
        offline = ROOT / 'work/ring-x-numeric-20261113/NuGet.Config'
        for name, project in (('six-release', ROOT / 'src/RizToolbox.Loader/RizToolbox.Loader.csproj'),
                              ('solution', ROOT / 'RizToolbox.sln')):
            arguments = [dotnet, 'build', project, '-c', 'Release', '--artifacts-path', artifacts, '--verbosity', 'minimal',
                         '--disable-build-servers', '-m:1', '-p:UseSharedCompilation=false', '-p:NuGetAudit=false',
                         '-p:RestoreConfigFile=' + str(offline)]
            if name == 'six-release':
                arguments += ['--no-incremental', '-p:ProvideCommandLineArgs=true', '-p:SkipCompilerExecution=false',
                              '-p:CustomAfterMicrosoftCommonTargets=' + str(CAPTURE_TARGET),
                              '-p:BuildProjectReferences=true', '-p:DebugType=portable', '-p:ChecksumAlgorithm=SHA256',
                              '-p:RizCompilationStage=graph', '-p:RizCaptureRoot=' + str(OUT / 'csc-inputs'),
                              '-p:RizEngineProductionProfileSource=', '-p:RizEngineTransportProfileSource=']
            step(name, arguments)
            if name == 'six-release':
                state['productsAfterSix'] = base.product_hashes(artifacts)
                capture_files = sorted((OUT / 'csc-inputs').rglob('*'))
                state['actualCscCaptureFiles'] = {str(path): sha(path) for path in capture_files if path.is_file()}
                require(len([path for path in capture_files if path.name == 'csc-command-line.txt']) == 6,
                        'The real six compiler command lines were not all captured.')
                save()
        state['productsBefore'] = base.product_hashes(artifacts)
        require(state['productsBefore'] == state['productsAfterSix'], 'Solution build changed captured production bytes.')
        assembly = artifacts / 'bin/RizToolbox.Judgements.Tests/release/RizToolbox.Judgements.Tests.dll'
        state['testAssemblyBefore'] = {ext: sha(assembly.with_suffix(ext)) for ext in ('.dll', '.pdb')}
        copies = {str(assembly.parent / (component + ext)): digest for component, values in state['productsBefore'].items()
                  if component in base.SIX[:4] for ext, digest in values.items()}
        require(len(copies) == 8 and all(sha(path) == digest for path, digest in copies.items()), 'Test dependencies differ.')
        state['dependencyCopies'] = copies
        listing = json.loads(step('legacy-listing', [dotnet, assembly, '--list-legacy-cases']))
        require(listing == read(PRIOR / 'run/legacy-case-list.json') and len(listing) == 300, 'Legacy coverage/order changed.')
        write(OUT / 'legacy-case-list.json', listing)
        for name, selector, total, prefix in (
            ('endpoint-local', '--line-ring-endpoint-local-checks', 6, 'Line ring numeric endpoint '),
            ('numeric', '--line-ring-numeric-checks', 13, 'Line ring numeric '),
            ('ring-x', '--line-ring-ring-x-numeric-checks', 7, 'Line ring numeric ring ')):
            step(name, [dotnet, assembly, selector], total, prefix)
        for case in (17, 18, 19):
            step('canvas-owned-source-' + str(case), [dotnet, assembly, '--line-ring-source-case', case], 1, 'Line ring source ')
        usage = re.findall(r'Console\.Error\.WriteLine\("(Usage: [^"\r\n]+)"\);', (ROOT / PROGRAM).read_text(encoding='utf-8'))
        require(len(usage) == 1, 'Ambiguous Program usage.')
        for name, arguments in (('invalid-endpoint-name', ['--line-ring-endpoint-local-check']),
                                 ('invalid-endpoint-arity', ['--line-ring-endpoint-local-checks', 'extra'])):
            body = step(name, [dotnet, assembly, *arguments], expected_exit=2)
            require(body == usage[0] + '\n', 'Invalid endpoint selector returned another failure.')
        state['productsAfter'] = base.product_hashes(artifacts)
        state['compiledBytesUnchanged'] = (state['productsAfter'] == state['productsBefore']
            and all(sha(assembly.with_suffix(ext)) == digest for ext, digest in state['testAssemblyBefore'].items())
            and all(sha(path) == digest for path, digest in copies.items()))
        require(state['compiledBytesUnchanged'], 'Compiled bytes changed during testing.')
        state['actualCscCaptureUnchanged'] = all(sha(path) == digest for path, digest in state['actualCscCaptureFiles'].items())
        require(state['actualCscCaptureUnchanged'], 'Actual compiler evidence changed during testing.')
        require(all(sha(row['path']) == row['sha256'] for row in fixtures.values()), 'Native fixture changed.')
        require(len(state['steps']) == 11 and all(row['matchesExpected'] and row['naturalExit'] for row in state['steps']), 'Incomplete focused execution.')
        state['passed'] = True
    except Exception as error:
        state['failure'] = str(error)
    finally:
        after = base.inputs()
        write(OUT / 'inputs-after.json', after)
        state['inputsUnchanged'] = after == inputs
        state['controlsUnchanged'] = all(sha(path) == digest for path, digest in controls.items())
        state['passed'] = state['passed'] and state['inputsUnchanged'] and state['controlsUnchanged'] and state['activeChild'] is None
        state['finishedUtc'] = now()
        save()
        print(json.dumps(state), flush=True)
    return 0 if state['passed'] else 1

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('operation', choices=('apply', 'validate'))
    options = parser.parse_args()
    if options.operation == 'apply':
        apply()
    else:
        sys.exit(validate())
