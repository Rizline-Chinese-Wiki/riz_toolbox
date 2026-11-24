"""Continue the interrupted 892-input run without changing its original evidence.

Only rows whose actual child exit was recorded by the original controller are
reused. The unrecorded writer-15/16 attempts are rerun on the exact same DLLs.
"""
from concurrent.futures import ThreadPoolExecutor
import ctypes
import datetime as dt
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import sys

WORK = Path(__file__).resolve().parent
ROOT = WORK.parents[1]
ORIGINAL = Path('D:/riz-build-cache-next/line-endpoint-parent-integration-20261113/main-r1')
OUT = ORIGINAL.parent / 'continuation-r1'
ORIGINAL_RESULT_SHA = '8de722ada3bcd136895b814a5cead57ee35cf39add77d3f94082113170ac260d'
VALIDATOR_SHA = '2c9be1109e470be0eaca729e70a90d8974e62b4a620df42989e0a2caac854c4a'


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def main():
    validator = load('parent_original_validator', WORK / 'validate_main.py')
    require, sha, read, write = validator.require, validator.sha, validator.read, validator.write
    require(sha(WORK / 'validate_main.py') == VALIDATOR_SHA, 'Original validator changed')
    require(not OUT.exists(), 'Preserve previous continuation')
    require(sha(ORIGINAL / 'result.json') == ORIGINAL_RESULT_SHA, 'Interrupted original result changed')
    old = read(ORIGINAL / 'result.json')
    require(old['controllerPid'] == 40464 and old['passed'] is False
            and 'finishedUtc' not in old and not (ORIGINAL / 'controller-handle-exit.json').exists(),
            'Original run is not the identified incomplete run')
    # A missing original PID is a prerequisite, not evidence of a successful exit.
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    kernel.OpenProcess.argtypes = (ctypes.c_ulong, ctypes.c_int, ctypes.c_ulong)
    kernel.OpenProcess.restype = ctypes.c_void_p
    kernel.CloseHandle.argtypes = (ctypes.c_void_p,)
    handle = kernel.OpenProcess(0x1000, False, old['controllerPid'])
    if handle:
        kernel.CloseHandle(handle)
        raise RuntimeError('Original PID exists; do not run a competing controller')
    require(ctypes.get_last_error() == 87, 'Cannot establish absence of original PID')
    stage = validator.load_stage()
    scanner = stage.input_scanner()
    manifest = read(WORK / 'payload-manifest.json')
    inputs = read(ORIGINAL / 'inputs-before.json')
    expected_inputs = read(WORK / 'inputs-before.json')
    for row in manifest['files']:
        expected_inputs[row['path']] = row['payloadSha256']
    require(inputs == expected_inputs == scanner.inputs() and len(inputs) == 892,
            'Applied main graph differs from the exact interrupted build')
    require(old['writerCases'] == manifest['writerCases'] == 31
            and old['defaultExpectedCases'] == manifest['defaultCases'] == 403,
            'Interrupted registry or final payload differs')
    require(all(sha(path) == digest for path, digest in old['controlsBefore'].items()),
            'Original run controls changed')
    require(old['controlsBefore'][str(WORK / 'validate_main.py')] == VALIDATOR_SHA,
            'Original result does not name the frozen validator')
    artifacts = ORIGINAL / 'artifacts'
    products = old['productsBeforeTests']
    require(products == old['productsAfterSix'] == scanner.product_hashes(artifacts),
            'The exact fresh production DLL/PDB bytes no longer match')
    require(len(old['testPairs']) == 4 and len(old['dependencies']) == 12
            and all(sha(path) == digest for path, digest in {**old['testPairs'], **old['dependencies']}.items()),
            'Interrupted test/dependency DLL/PDB bytes changed')
    require(all(sha(row['path']) == row['sha256'] for row in old['nativeFixtures'].values()),
            'Original native fixtures changed')
    writer_names = manifest['writerCaseNames']
    prefix_names = ['six-release', 'solution', 'legacy-listing'] + [f'writer-{i:02}' for i in range(1, 15)]
    require([row['name'] for row in old['steps']] == prefix_names,
            'Only the identified 17 completed original steps may be reused')
    test = artifacts / 'bin/RizToolbox.Judgements.Tests/release/RizToolbox.Judgements.Tests.dll'
    runtime = artifacts / 'bin/RizToolbox.Runtime.Tests/release/RizToolbox.Runtime.Tests.dll'
    for row in old['steps']:
        name = row['name']
        require(row['naturalExit'] is True and row['exitCode'] == 0 and row['matchesExpected'] is True,
                'Original child lacks a recorded natural successful exit: ' + name)
        stdout, stderr = ORIGINAL / (name + '.stdout.log'), ORIGINAL / (name + '.stderr.log')
        require(sha(stdout) == row['stdoutSha256'] and sha(stderr) == row['stderrSha256'],
                'Recorded completed child logs changed: ' + name)
        body, errors = stdout.read_text(encoding='utf-8-sig'), stderr.read_text(encoding='utf-8-sig')
        passes, runs = re.findall(r'^PASS (.+)$', body, re.M), re.findall(r'^RUN (.+)$', body, re.M)
        require(not errors and row['passNames'] == passes and row['runNames'] == runs
                and not re.search(r'^FAIL\b', body, re.M), 'Original logs do not support recorded success')
        if name in ('six-release', 'solution'):
            project = ROOT / ('src/RizToolbox.Loader/RizToolbox.Loader.csproj' if name == 'six-release' else 'RizToolbox.sln')
            argv = [str(scanner.DOTNET), 'build', str(project), '-c', 'Release', '--artifacts-path', str(artifacts),
                    '--verbosity', 'minimal', '--disable-build-servers', '-m:1', '-p:UseSharedCompilation=false',
                    '-p:NuGetAudit=false', '-p:RestoreConfigFile=' + str(ROOT / 'work/ring-x-numeric-20261113/NuGet.Config')]
            require(row['arguments'] == argv and not passes and not runs
                    and ('已成功生成。' in body or 'Build succeeded.' in body)
                    and re.findall(r'^\s*(\d+) (?:个错误|Error\(s\))\s*$', body, re.M) == ['0'],
                    'Original actual fresh build differs')
            if name == 'six-release':
                require(re.findall(r'^\s*(\d+) (?:个警告|Warning\(s\))\s*$', body, re.M) == ['0'],
                        'Production build has warnings')
        elif name == 'legacy-listing':
            require(row['arguments'] == [str(scanner.DOTNET), str(test), '--list-legacy-cases']
                    and json.loads(body) == read(stage.ACCEPTED / 'run/legacy-case-list.json')
                    and not passes and not runs, 'Original legacy listing differs')
        else:
            index = int(name[7:])
            require(row['arguments'] == [str(scanner.DOTNET), str(test), '--line-ring-endpoint-writer-case', str(index)]
                    and passes == runs == [writer_names[index - 1]]
                    and re.findall(r'(\d+)/(\d+) passed\.', body) == [('1', '1')],
                    'Original writer child names or count differ')
    environment = os.environ.copy()
    environment.update(DOTNET_GENERATE_ASPNET_CERTIFICATE='false', DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false',
                       DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1', DOTNET_NOLOGO='1')
    for key, row in old['nativeFixtures'].items():
        environment[key] = row['path']
    controls = dict(old['controlsBefore'])
    controls[str(Path(__file__).resolve())] = sha(__file__)
    controls[str(WORK / 'observe.py')] = sha(WORK / 'observe.py')
    OUT.mkdir(parents=True)
    validator.OUT = OUT
    write(OUT / 'inputs-before.json', inputs)
    shutil.copyfile(ORIGINAL / 'result.json', OUT / 'interrupted-original-result.json')
    interruption = {'observedUtc': dt.datetime.now(dt.timezone.utc).isoformat(),
                    'originalControllerPid': 40464, 'originalControllerNoLongerExists': True,
                    'originalControllerExitCode': None, 'originalControllerExitCodeKnown': False,
                    'originalResultSha256': ORIGINAL_RESULT_SHA, 'originalBatchAccepted': False,
                    'completedChildRowsReused': len(prefix_names), 'unconfirmedAttemptsRerun': ['writer-15', 'writer-16'],
                    'sameExact892SourceAndBuildBytesVerified': True, 'reasonForOriginalInterruption': 'Unknown'}
    write(OUT / 'interruption-observation.json', interruption)
    state = {**old, 'schema': 'line-endpoint-parent-main-continuation/v1',
             'startedUtc': dt.datetime.now(dt.timezone.utc).isoformat(), 'controllerPid': os.getpid(),
             'passed': False, 'steps': [{**row, 'logSegment': 'original'} for row in old['steps']],
             'activeChildren': [], 'controlsBefore': controls, 'originalControlsBefore': old['controlsBefore'],
             'continuedOriginalRun': str(ORIGINAL), 'originalResultSha256': ORIGINAL_RESULT_SHA,
             'originalBatchAccepted': False, 'reusedCompletedChildRows': len(prefix_names),
             'continuationExecutedSteps': 0, 'originalControllerExitCodeKnown': False}
    def save():
        write(OUT / 'result.json', state)
    def stable():
        return (scanner.inputs() == inputs and sha(ORIGINAL / 'result.json') == ORIGINAL_RESULT_SHA
                and all(sha(path) == digest for path, digest in controls.items())
                and scanner.product_hashes(artifacts) == products
                and all(sha(path) == digest for path, digest in {**old['testPairs'], **old['dependencies']}.items()))
    def record(result):
        row = result[0]
        state['steps'].append({**row, 'logSegment': 'continuation'})
        state['continuationExecutedSteps'] += 1
        save()
        require(row['matchesExpected'], 'Continuation child failed: ' + row['name'])
    def run(name, argv, **options):
        require(stable(), 'Source/control/binary drift before ' + name)
        state['activeChildren'] = [name]; save()
        result = validator.execute(name, argv, environment, scanner, **options)
        state['activeChildren'] = []; record(result)
        require(stable(), 'Source/control/binary drift after ' + name)
        return result[1:]
    save()
    try:
        with ThreadPoolExecutor(max_workers=2) as pool:
            for start in range(14, len(writer_names), 2):
                require(stable(), 'Drift before writer pair')
                jobs = []
                for index in range(start, min(start + 2, len(writer_names))):
                    name = f'writer-{index + 1:02}'
                    argv = [scanner.DOTNET, test, '--line-ring-endpoint-writer-case', index + 1]
                    jobs.append((name, pool.submit(validator.execute, name, argv, environment, scanner,
                                                   count=1, exact_name=writer_names[index])))
                state['activeChildren'] = [name for name, _ in jobs]; save()
                results = [future.result() for _, future in jobs]
                state['activeChildren'] = []
                for result in results:
                    record(result)
                require(stable(), 'Drift after writer pair')
        for name, selector, count, prefix in (
            ('target-provider', '--line-ring-target-provider-checks', 6, 'Line ring source target provider '),
            ('chart-occurrence', '--line-chart-occurrence-checks', 5, 'Line chart occurrence '),
            ('endpoint-local', '--line-ring-endpoint-local-checks', 6, 'Line ring numeric endpoint '),
            ('numeric', '--line-ring-numeric-checks', 13, 'Line ring numeric '),
            ('ring-x', '--line-ring-ring-x-numeric-checks', 7, 'Line ring numeric ring ')):
            run(name, [scanner.DOTNET, test, selector], count=count, prefix=prefix)
        program = (ROOT / 'tests/RizToolbox.Judgements.Tests/Program.cs').read_text(encoding='utf-8-sig')
        usage = re.findall(r'Console\.Error\.WriteLine\("(Usage: [^"\r\n]+)"\);', program)
        require(len(usage) == 1, 'Ambiguous selector usage')
        for name, argv in (('invalid-writer-name', ['--line-ring-endpoint-writer-cases']),
                           ('invalid-writer-arity', ['--line-ring-endpoint-writer-case', '1', 'extra'])):
            body, errors = run(name, [scanner.DOTNET, test, *argv], expected_exit=2)
            require(not body and errors == usage[0] + '\n', 'Invalid writer selector usage differs')
        for index in (13, 14, 17, 18, 19):
            run(f'source-{index}', [scanner.DOTNET, test, '--line-ring-source-case', index],
                count=1, prefix='Line ring source ')
        run('basis-01', [scanner.DOTNET, test, '--canvas-basis-case', 1], count=1, prefix='Canvas basis ')
        run('renderer-abi', [scanner.DOTNET, runtime, '--renderer-abi-checks'], count=51, runs_expected=False)
        require(stable() and len(state['steps']) == 48 and state['continuationExecutedSteps'] == 31
                and sum(len(row['passNames']) for row in state['steps']) == 125,
                'Combined scope, unchanged bytes or completed step total differs')
        state['productsAfterTests'] = scanner.product_hashes(artifacts)
        state['compiledBytesUnchanged'] = True
        state['passed'] = True
    except Exception as error:
        state['failure'] = str(error)
    finally:
        after = scanner.inputs()
        write(OUT / 'inputs-after.json', after)
        state['inputsUnchanged'] = after == inputs
        state['controlsUnchanged'] = all(sha(path) == digest for path, digest in controls.items())
        state['originalResultUnchanged'] = sha(ORIGINAL / 'result.json') == ORIGINAL_RESULT_SHA
        state['nativeFixturesUnchanged'] = all(sha(row['path']) == row['sha256'] for row in old['nativeFixtures'].values())
        state['passed'] = state['passed'] and all(state[key] for key in (
            'inputsUnchanged', 'controlsUnchanged', 'originalResultUnchanged', 'nativeFixturesUnchanged'))
        state['finishedUtc'] = dt.datetime.now(dt.timezone.utc).isoformat()
        state['activeChildren'] = []; save()
        print(json.dumps({'passed': state['passed'], 'steps': len(state['steps']),
                          'continuationSteps': state['continuationExecutedSteps'],
                          'failure': state.get('failure'), 'resultSha256': sha(OUT / 'result.json')}), flush=True)
    return 0 if state['passed'] else 1


if __name__ == '__main__':
    sys.exit(main())
