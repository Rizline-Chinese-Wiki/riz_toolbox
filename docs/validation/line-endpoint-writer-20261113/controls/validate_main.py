"""Fresh 891-input offline writer validation, not a full SDK/Seek admission."""
import datetime as dt
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import time

WORK = Path(__file__).resolve().parent
ROOT = WORK.parents[1]
OUT = Path('D:/riz-build-cache-next/line-endpoint-writer-integration-20261113/main-r1')
PRIOR = ROOT / 'docs/validation/line-ring-target-provider-20261113'


def sha(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def require(ok, why):
    if not ok:
        raise RuntimeError(why)


def write(path, value):
    staged = Path(str(path) + '.pending')
    staged.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    os.replace(staged, path)


def main():
    require(not OUT.exists(), 'Preserve existing main validation attempt')
    helper = ROOT / 'work/line-ring-source-validation-20261113/run_validation.py'
    require(sha(helper) == '61c2983ae117322b25ff96ad75b659b870fdadf9aada9274727248cd5ca98fd4', 'Input scanner changed')
    spec = importlib.util.spec_from_file_location('writer_main_input_scan', helper)
    scanner = importlib.util.module_from_spec(spec); spec.loader.exec_module(scanner)
    before = read(WORK / 'inputs-after.json')
    require(scanner.inputs() == before and len(before) == 891
            and read(WORK / 'application-record.json')['appliedToMain'] is True, 'Exact 891 graph not applied')
    require(sha(PRIOR / 'validation-summary.json') == '2ae690323504cb557142aef81618b1f9a1d10a3de9e15e8e9be40053cee22663'
            and read(PRIOR / 'validation-summary.json')['acceptedThisBatch'] is True, 'Prior 885 batch is not accepted')
    controls = {str(p): sha(p) for p in (Path(__file__).resolve(), WORK / 'apply.py', WORK / 'payload-manifest.json',
        WORK / 'payload.patch', WORK / 'application-record.json', ROOT / 'scripts/Build.ps1')}
    OUT.mkdir(parents=True)
    artifacts = OUT / 'artifacts'
    write(OUT / 'inputs-before.json', before)
    state = {'startedUtc': dt.datetime.now(dt.timezone.utc).isoformat(), 'controllerPid': os.getpid(), 'passed': False,
             'compilerInputs': 891, 'defaultExpectedCases': 389, 'old300ExecutedThisBatch': False,
             'default389SingleProcessExecuted': False, 'fullSource19ExecutedThisBatch': False, 'fullBasis16ExecutedThisBatch': False,
             'scope': 'Parent-equal observational endpoint commands and selected source/lifetime regressions; not atomic Unity writes',
             'sdkGoalComplete': False, 'completeSeekPermitted': False, 'atomicConditionalWrites': False,
             'gameStarted': False, 'installed': False, 'steps': [], 'activeChild': None, 'controlsBefore': controls}
    fixtures = read(ROOT / 'docs/validation/canvas-basis-shared-source-20261112/build-test-result.json')['nativeFixtures']
    environment = os.environ.copy()
    environment.update(DOTNET_GENERATE_ASPNET_CERTIFICATE='false', DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false',
        DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1', DOTNET_NOLOGO='1')
    for key, row in fixtures.items():
        require(sha(row['path']) == row['sha256'], 'Native fixture changed'); environment[key] = row['path']
    state['nativeFixtures'] = fixtures
    def save():
        write(OUT / 'result.json', state)
    def stable():
        return scanner.inputs() == before and all(sha(path) == digest for path, digest in controls.items())
    def run(name, argv, count=None, prefix=None, expected_exit=0, runs_expected=True):
        require(stable(), 'Source/control change before execution')
        require(scanner.available_commit() >= 512 * 1024 * 1024, 'Available commit below 512 MiB')
        stdout = OUT / (name + '.stdout.log'); stderr = OUT / (name + '.stderr.log')
        print('START ' + name, flush=True)
        with stdout.open('xb') as out, stderr.open('xb') as err:
            child = subprocess.Popen(list(map(str, argv)), cwd=ROOT, env=environment, stdin=subprocess.DEVNULL,
                stdout=out, stderr=err, creationflags=subprocess.CREATE_NO_WINDOW)
            state['activeChild'] = {'name': name, 'pid': child.pid}; save()
            forced = False; deadline = time.monotonic() + 1800
            while child.poll() is None:
                try:
                    child.wait(timeout=2)
                except subprocess.TimeoutExpired:
                    if time.monotonic() >= deadline or scanner.available_commit() < 512 * 1024 * 1024:
                        forced = True; child.kill(); child.wait(); break
        body = stdout.read_text(encoding='utf-8-sig'); errors = stderr.read_text(encoding='utf-8-sig')
        passes = re.findall(r'^PASS (.+)$', body, re.M); runs = re.findall(r'^RUN (.+)$', body, re.M)
        good = not forced and child.returncode == expected_exit and not re.search(r'^FAIL\b', body + errors, re.M)
        if expected_exit == 0:
            good = good and not errors
        if count is not None:
            good = (good and len(passes) == count and len(set(passes)) == count
                and re.findall(r'(\d+)/(\d+) passed', body) == [(str(count), str(count))]
                and (runs == passes if runs_expected else not runs)
                and (not prefix or all(value.startswith(prefix) for value in passes)))
        if name in ('six-release', 'solution'):
            good = good and ('已成功生成。' in body or 'Build succeeded.' in body)
            good = good and re.findall(r'^\s*(\d+) (?:个错误|Error\(s\))\s*$', body, re.M) == ['0']
            if name == 'six-release':
                good = good and re.findall(r'^\s*(\d+) (?:个警告|Warning\(s\))\s*$', body, re.M) == ['0']
        state['steps'].append({'name': name, 'arguments': list(map(str, argv)), 'exitCode': child.returncode,
            'naturalExit': not forced, 'matchesExpected': bool(good), 'passNames': passes,
            'stdoutSha256': sha(stdout), 'stderrSha256': sha(stderr)})
        state['activeChild'] = None; save()
        print('END ' + name + ' exit=' + str(child.returncode), flush=True)
        require(good, 'Execution gate failed: ' + name)
        return body, errors
    try:
        dotnet = scanner.DOTNET
        for name, project in (('six-release', ROOT / 'src/RizToolbox.Loader/RizToolbox.Loader.csproj'),
                              ('solution', ROOT / 'RizToolbox.sln')):
            run(name, [dotnet, 'build', project, '-c', 'Release', '--artifacts-path', artifacts, '--verbosity', 'minimal',
                '--disable-build-servers', '-m:1', '-p:UseSharedCompilation=false', '-p:NuGetAudit=false',
                '-p:RestoreConfigFile=' + str(ROOT / 'work/ring-x-numeric-20261113/NuGet.Config')])
            if name == 'six-release':
                state['productsAfterSix'] = scanner.product_hashes(artifacts)
        state['productsBeforeTests'] = scanner.product_hashes(artifacts)
        require(state['productsAfterSix'] == state['productsBeforeTests'], 'Solution replaced six release bytes')
        test = artifacts / 'bin/RizToolbox.Judgements.Tests/release/RizToolbox.Judgements.Tests.dll'
        runtime_test = artifacts / 'bin/RizToolbox.Runtime.Tests/release/RizToolbox.Runtime.Tests.dll'
        state['testPairs'] = {str(p): sha(p) for assembly in (test, runtime_test) for p in (assembly, assembly.with_suffix('.pdb'))}
        state['dependencies'] = {str(test.parent / (component + ext)): digest
            for component, values in state['productsBeforeTests'].items() if component in scanner.SIX[:4]
            for ext, digest in values.items()}
        state['dependencies'].update({str(runtime_test.parent / (component + ext)): digest
            for component, values in state['productsBeforeTests'].items() if component in ('RizToolbox.Runtime', 'RizToolbox.Sdk')
            for ext, digest in values.items()})
        require(len(state['dependencies']) == 12 and all(sha(path) == digest for path, digest in state['dependencies'].items()), 'Actual test dependencies differ')
        listing, _ = run('legacy-listing', [dotnet, test, '--list-legacy-cases'])
        require(json.loads(listing) == read(PRIOR / 'run/legacy-case-list.json'), 'Old 300 registry names/order changed')
        write(OUT / 'legacy-case-list.json', json.loads(listing))
        for name, selector, count, prefix in (
            ('endpoint-writer', '--line-ring-endpoint-writer-checks', 17, 'Line ring source endpoint writer '),
            ('target-provider', '--line-ring-target-provider-checks', 6, 'Line ring source target provider '),
            ('chart-occurrence', '--line-chart-occurrence-checks', 5, 'Line chart occurrence '),
            ('endpoint-local', '--line-ring-endpoint-local-checks', 6, 'Line ring numeric endpoint '),
            ('numeric', '--line-ring-numeric-checks', 13, 'Line ring numeric '),
            ('ring-x', '--line-ring-ring-x-numeric-checks', 7, 'Line ring numeric ring ')):
            run(name, [dotnet, test, selector], count, prefix)
        usage = re.findall(r'Console\.Error\.WriteLine\("(Usage: [^"\r\n]+)"\);',
                           (ROOT / 'tests/RizToolbox.Judgements.Tests/Program.cs').read_text(encoding='utf-8'))
        require(len(usage) == 1, 'Program usage is ambiguous')
        for name, argv in (('invalid-writer-name', ['--line-ring-endpoint-writer-check']),
                            ('invalid-writer-arity', ['--line-ring-endpoint-writer-checks', 'extra'])):
            body, errors = run(name, [dotnet, test, *argv], expected_exit=2)
            require(not body and errors == usage[0] + '\n', 'Illegal selector did not return exact usage')
        for index in (13, 14, 17, 18, 19):
            run('source-' + str(index), [dotnet, test, '--line-ring-source-case', index], 1, 'Line ring source ')
        run('basis-01', [dotnet, test, '--canvas-basis-case', 1], 1, 'Canvas basis ')
        run('renderer-abi', [dotnet, runtime_test, '--renderer-abi-checks'], 51, runs_expected=False)
        require(stable(), 'Source/control changed during testing')
        state['productsAfterTests'] = scanner.product_hashes(artifacts)
        state['compiledBytesUnchanged'] = (state['productsAfterTests'] == state['productsBeforeTests']
            and all(sha(path) == digest for path, digest in {**state['testPairs'], **state['dependencies']}.items()))
        require(state['compiledBytesUnchanged'], 'Tested production/test/dependency bytes changed')
        require(len(state['steps']) == 18 and sum(len(row['passNames']) for row in state['steps']) == 111,
                'Scoped execution count differs')
        state['passed'] = True
    except Exception as error:
        state['failure'] = str(error)
    finally:
        after = scanner.inputs(); write(OUT / 'inputs-after.json', after)
        state['inputsUnchanged'] = before == after
        state['controlsUnchanged'] = all(sha(path) == digest for path, digest in controls.items())
        state['nativeFixturesUnchanged'] = all(sha(row['path']) == row['sha256'] for row in fixtures.values())
        state['passed'] = state['passed'] and state['inputsUnchanged'] and state['controlsUnchanged'] and state['nativeFixturesUnchanged']
        state['finishedUtc'] = dt.datetime.now(dt.timezone.utc).isoformat(); save()
        print(json.dumps({'passed': state['passed'], 'steps': len(state['steps']), 'failure': state.get('failure'),
                          'resultSha256': sha(OUT / 'result.json')}), flush=True)
    return 0 if state['passed'] else 1


if __name__ == '__main__':
    sys.exit(main())
