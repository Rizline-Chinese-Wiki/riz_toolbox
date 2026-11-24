"""Bounded continuation after the original legacy process exhausted commit.

Only the test runner may differ. Original production and all test bodies remain
hash-bound; old source/basis results are explicitly composite, not new execution.
"""
import argparse
import datetime as dt
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[2]
BASE = Path('D:/riz-build-cache-next/line-ring-source-20261113/root-r3')
SOURCE = BASE / 'source-r1'
FINAL = BASE / 'final-r1'
RUNNER = 'tests/RizToolbox.Judgements.Tests/Program.cs'
SIX = ['RizToolbox.' + name for name in ('Sdk', 'Core', 'Runtime', 'Adapter', 'Hosting', 'Loader')]
TESTS = ['RizToolbox.' + name + '.Tests' for name in ('Judgements', 'Runtime', 'AudioContent')]
DOTNET = Path('C:/Program Files/dotnet/dotnet.exe')
spec = importlib.util.spec_from_file_location('original_validation', Path(__file__).with_name('run_validation.py'))
original = importlib.util.module_from_spec(spec)
spec.loader.exec_module(original)


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def sha(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def write(path, value):
    Path(path).write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def files(folder):
    return {str(path.relative_to(folder)).replace('\\', '/'): sha(path)
            for path in sorted(folder.rglob('*')) if path.is_file()}


def product_hashes(artifacts):
    return {name: {ext: sha(artifacts / 'bin' / name / 'release' / (name + ext))
                   for ext in ('.dll', '.pdb')} for name in SIX}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=Path, required=True)
    opt = parser.parse_args()
    out = opt.output.resolve()
    if not out.is_relative_to(BASE.parent.resolve()):
        raise RuntimeError('Recovery output must remain in the named validation workspace.')
    out.mkdir(parents=True, exist_ok=False)
    shutil.copyfile(__file__, out / 'recovery-control.py')
    report = {'scope': 'Composite source/basis validation with unchanged production and isolated original legacy cases',
              'controllerPid': os.getpid(), 'startedUtc': dt.datetime.now(dt.timezone.utc).isoformat(),
              'steps': [], 'passed': False, 'acceptedThisBatch': False, 'sdkGoalComplete': False,
              'default332SingleProcessExecuted': False, 'gameStarted': False, 'installed': False,
              'completeSeekPermitted': False, 'minimumAvailableCommitBytes': 512 * 1024 * 1024,
              'childTimeoutSeconds': 2700, 'controlSha256': sha(__file__)}
    before = original.inputs()
    write(out / 'inputs-before.json', before)
    env = os.environ.copy()
    env.update({'DOTNET_CLI_TELEMETRY_OPTOUT': '1', 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE': '1',
                'DOTNET_GENERATE_ASPNET_CERTIFICATE': 'false', 'DOTNET_ADD_GLOBAL_TOOLS_TO_PATH': 'false',
                'DOTNET_NOLOGO': '1'})

    def save():
        write(out / 'result.json', report)

    def step(name, arguments, expected=None, expected_exit=0, exact_case=None):
        if original.inputs() != before:
            raise RuntimeError('Current frozen input graph changed before ' + name)
        if original.available_commit() < report['minimumAvailableCommitBytes']:
            raise RuntimeError('Insufficient commit before starting owned child ' + name)
        log = out / (name + '.log')
        started = dt.datetime.now(dt.timezone.utc).isoformat()
        resource_stop = None
        timed_out = False
        print('START ' + name, flush=True)
        with log.open('wb') as stream:
            child = subprocess.Popen([str(arg) for arg in arguments], cwd=ROOT, env=env,
                                     stdin=subprocess.DEVNULL, stdout=stream, stderr=subprocess.STDOUT)
            report['activeChild'] = {'name': name, 'pid': child.pid, 'startedUtc': started, 'log': str(log)}
            save()
            deadline = time.monotonic() + report['childTimeoutSeconds']
            while child.poll() is None:
                try:
                    child.wait(timeout=2)
                except subprocess.TimeoutExpired:
                    remaining = original.available_commit()
                    if remaining < report['minimumAvailableCommitBytes']:
                        resource_stop = {'reason': 'Less than 512 MiB of system commit remains.',
                                         'availableCommitBytes': remaining, 'ownedPid': child.pid}
                        child.kill()
                        child.wait()
                        break
                    if time.monotonic() >= deadline:
                        timed_out = True
                        child.kill()
                        child.wait()
                        break
        content = log.read_text(encoding='utf-8-sig', errors='replace')
        totals = re.findall(r'(\d+)/(\d+) audio/content checks passed\.' if name == 'audio-content'
                            else r'(\d+)/(\d+) passed', content)
        passes = re.findall(r'^PASS (.+)$', content, re.MULTILINE)
        runs = re.findall(r'^RUN (.+)$', content, re.MULTILINE)
        good = child.returncode == expected_exit and not timed_out and resource_stop is None
        good = good and (expected is None or totals == [(str(expected), str(expected))])
        if exact_case is not None:
            good = good and passes == [exact_case] and runs == [exact_case]
        if expected_exit != 0:
            good = good and not runs and not passes and not totals
        row = {'name': name, 'arguments': [str(arg) for arg in arguments], 'startedUtc': started,
               'finishedUtc': dt.datetime.now(dt.timezone.utc).isoformat(), 'exitCode': child.returncode,
               'expectedExitCode': expected_exit, 'timedOut': timed_out, 'resourceStop': resource_stop,
               'totals': totals, 'passNames': passes, 'runNames': runs,
               'log': str(log), 'logSha256': sha(log), 'matchesExpected': good}
        report['steps'].append(row)
        report.pop('activeChild', None)
        save()
        print('END ' + name + ' exit=' + str(child.returncode) + ' expected=' + str(good), flush=True)
        if not good:
            raise RuntimeError('Owned child did not meet its exact gate: ' + name)
        return content

    try:
        prior = read(FINAL / 'result.json')
        source = read(SOURCE / 'result.json')
        old_inputs = read(SOURCE / 'inputs-before.json')
        if not source['passed'] or not source['inputsUnchanged'] or not prior['inputsUnchanged'] or prior['passed']:
            raise RuntimeError('Original source pass and failed resource-guard matrix must be preserved.')
        if old_inputs != read(SOURCE / 'inputs-after.json') or old_inputs != read(FINAL / 'inputs-before.json') or old_inputs != read(FINAL / 'inputs-after.json'):
            raise RuntimeError('Original frozen graphs differ.')
        changed = [key for key in before if before[key] != old_inputs.get(key)]
        if set(before) != set(old_inputs) or changed != [RUNNER]:
            raise RuntimeError('Only the test runner may differ from the original 871 inputs: ' + repr(changed))
        runner_proof = read(Path(__file__).with_name('runner-change-review.json'))
        if not runner_proof['oldBytesReconstructedExactly'] or runner_proof['oldSha256'] != old_inputs[RUNNER] or runner_proof['currentSha256'] != before[RUNNER]:
            raise RuntimeError('Exact inverse-byte proof of the runner-only change is required.')
        shutil.copyfile(Path(__file__).with_name('runner-change-review.json'), out / 'runner-change-review.json')
        shutil.copyfile(Path(__file__).with_name('runner-only-change.diff'), out / 'runner-only-change.diff')
        last = prior['steps'][-1]
        if last['name'] != '7-judgements-without-basis' or not last['resourceStop'] or last['timedOut'] or last['exitCode'] == 0:
            raise RuntimeError('Recovery is restricted to the exact resource-guard interruption.')
        for result in (source, prior):
            for row in result['steps']:
                if sha(row['log']) != row['logSha256']:
                    raise RuntimeError('Original log changed: ' + row['name'])
                if row is not last and row != last and (row['exitCode'] != 0 or row['timedOut'] or row.get('resourceStop') or not row['matchesExpected']):
                    raise RuntimeError('Retained prefix lacks a natural pass: ' + row['name'])
        source_cases = source['steps'][1:]
        basis_cases = [row for row in prior['steps'] if row['name'].startswith('6-canvas-basis-case-')]
        if [row['name'] for row in source_cases] != ['6a-line-ring-source-' + str(i).zfill(2) for i in range(1, 17)]:
            raise RuntimeError('Missing exact original source cases.')
        if [row['name'] for row in basis_cases] != ['6-canvas-basis-case-' + str(i).zfill(2) for i in range(1, 17)]:
            raise RuntimeError('Missing exact original basis cases.')
        if any(row['totals'] != [['1', '1']] for row in source_cases + basis_cases):
            raise RuntimeError('Original source/basis results must each be 1/1.')
        artifacts = Path(prior['artifactsDirectory'])
        if artifacts != Path(source['artifactsDirectory']):
            raise RuntimeError('Original production/test artifact path differs.')
        original_products = product_hashes(artifacts)
        old_judgements = {ext: sha(artifacts / 'bin' / TESTS[0] / 'release' / (TESTS[0] + ext)) for ext in ('.dll', '.pdb')}
        if old_judgements != prior['completedSourceRun']['originalTestAssembly']:
            raise RuntimeError('Original executing Judgements assembly no longer matches recorded pre-completion identity.')
        for component, values in prior['completedSourceRun']['originalProducts'].items():
            if original_products[component] != values:
                raise RuntimeError('Original product changed: ' + component)
        # Preserve the entire old execution directory before the exact same
        # deterministic obj/bin paths are rebuilt. No old result/log is edited.
        backup = out / 'original-compiled' / 'bin'
        old_bin_manifest = files(artifacts / 'bin')
        shutil.copytree(artifacts / 'bin', backup)
        if files(backup) != old_bin_manifest:
            raise RuntimeError('Original compiled execution directory backup is incomplete.')
        write(out / 'original-compiled-manifest.json', old_bin_manifest)
        for label, old_dir in (('original-source', SOURCE), ('original-final', FINAL)):
            dst = out / label
            dst.mkdir()
            for name in ('result.json', 'inputs-before.json', 'inputs-after.json'):
                shutil.copyfile(old_dir / name, dst / name)
        prefix_content = Path(last['log']).read_text(encoding='utf-8-sig')
        prefix_passes = re.findall(r'^PASS (.+)$', prefix_content, re.MULTILINE)
        prefix_runs = re.findall(r'^RUN (.+)$', prefix_content, re.MULTILINE)
        if len(prefix_passes) != 256 or len(prefix_runs) != 257 or re.search(r'^FAIL ', prefix_content, re.MULTILINE):
            raise RuntimeError('Original interrupted legacy evidence differs from observed boundary.')
        report.update({'artifactsDirectory': str(artifacts), 'onlyChangedInput': RUNNER,
            'runnerChangeReview': runner_proof,
            'oldInputCount': len(old_inputs), 'inputCount': len(before),
            'oldInputManifestSha256': sha(SOURCE / 'inputs-before.json'),
            'originalMatrixResultSha256': sha(FINAL / 'result.json'), 'originalSourceResultSha256': sha(SOURCE / 'result.json'),
            'originalCompiledBackup': str(backup), 'originalProducts': original_products,
            'originalTestAssembly': old_judgements, 'originalResourceStop': last,
            'originalLegacyPassedPrefixCount': len(prefix_passes), 'interruptedPrefixAcceptedAs300': False,
            'retainedSourceCases': source_cases, 'retainedBasisCases': basis_cases,
            'retainedCaseExecutionGraph': str(SOURCE / 'inputs-before.json'),
            'retainedCasesExecutedByNewTestAssembly': False, 'testBodiesAndNativeFixturesChanged': False,
            'noNewCliHome': True, 'sameArtifactPathsRebuiltAfterCompleteBackup': True})
        fixtures = prior['nativeFixtures']
        if fixtures != source['nativeFixtures']:
            raise RuntimeError('Original fixtures differ.')
        for key, fixture in fixtures.items():
            if sha(fixture['path']) != fixture['sha256']:
                raise RuntimeError('Native fixture changed: ' + key)
            env[key] = fixture['path']
        report['nativeFixtures'] = fixtures
        save()
        # Solution covers the six products and all three current test projects.
        step('build-solution', [DOTNET, 'build', ROOT / 'RizToolbox.sln', '-c', 'Release',
             '--artifacts-path', artifacts, '--verbosity', 'minimal', '--disable-build-servers', '-m:1',
             '-p:UseSharedCompilation=false', '-p:NuGetAudit=false'])
        current_products = product_hashes(artifacts)
        report['productionProductsByteIdentical'] = current_products == original_products
        report['currentTestAssembly'] = {ext: sha(artifacts / 'bin' / TESTS[0] / 'release' / (TESTS[0] + ext)) for ext in ('.dll', '.pdb')}
        report['testRunnerAssemblyChanged'] = report['currentTestAssembly'] != old_judgements
        if current_products != original_products or not report['testRunnerAssemblyChanged']:
            raise RuntimeError('Unchanged production and changed test runner DLL/PDB are required.')
        dll = lambda test: artifacts / 'bin' / test / 'release' / (test + '.dll')
        listing = json.loads(step('list-legacy-cases', [DOTNET, dll(TESTS[0]), '--list-legacy-cases']))
        if len(listing) != 300 or [row['index'] for row in listing] != list(range(1, 301)) or len({row['name'] for row in listing}) != 300:
            raise RuntimeError('Legacy listing must contain exactly 300 unique names in ordinal order.')
        if [row['name'] for row in listing[:256]] != prefix_passes or listing[256]['name'] != prefix_runs[-1]:
            raise RuntimeError('Legacy selection differs from the original 256 passes and next actual RUN.')
        write(out / 'legacy-case-list.json', listing)
        for value in ('0', '301', 'not-an-int'):
            step('invalid-legacy-' + value, [DOTNET, dll(TESTS[0]), '--legacy-case', value], expected_exit=2)
        # Test the formerly interrupted tail first, then all earlier groups.
        # Every one of the exact 300 cases is executed by one natural child.
        order = list(range(257, 301)) + list(range(1, 257))
        report['legacyExecutionOrder'] = order
        save()
        for index in order:
            step('legacy-case-' + str(index).zfill(3), [DOTNET, dll(TESTS[0]), '--legacy-case', str(index)],
                 expected=1, exact_case=listing[index - 1]['name'])
        step('renderer-abi', [DOTNET, dll(TESTS[1]), '--renderer-abi-checks'], expected=51)
        step('audio-content', [DOTNET, dll(TESTS[2])], expected=1001)
        step('exact-static-read', [DOTNET, dll(TESTS[1]), '--exact-static-field-read-checks'], expected=6)
        report['products'] = product_hashes(artifacts)
        report['dependencyCopies'] = [{'test': test, 'component': component, 'extension': ext,
            'sha256': sha(artifacts / 'bin' / test / 'release' / (component + ext)),
            'matches': sha(artifacts / 'bin' / test / 'release' / (component + ext)) == original_products[component][ext]}
            for test in TESTS for component in SIX[:4] for ext in ('.dll', '.pdb')]
        report['dependenciesMatch'] = len(report['dependencyCopies']) == 24 and all(row['matches'] for row in report['dependencyCopies'])
        legacy = [row for row in report['steps'] if row['name'].startswith('legacy-case-')]
        report['legacy300UniqueNaturalPasses'] = len(legacy) == 300 and len({row['name'] for row in legacy}) == 300 and all(row['matchesExpected'] for row in legacy)
        report['passed'] = report['legacy300UniqueNaturalPasses'] and report['dependenciesMatch'] and report['products'] == original_products
    except Exception as error:
        report['failure'] = str(error)
    finally:
        after = original.inputs()
        write(out / 'inputs-after.json', after)
        report['inputsUnchanged'] = before == after
        report['passed'] = report['passed'] and report['inputsUnchanged']
        # Publication/review is a separate operation after this real terminal.
        report['acceptedThisBatch'] = False
        report['finishedUtc'] = dt.datetime.now(dt.timezone.utc).isoformat()
        save()
        print(json.dumps({key: report.get(key) for key in ('passed', 'inputsUnchanged', 'failure')}), flush=True)
    return 0 if report['passed'] else 1


if __name__ == '__main__':
    sys.exit(main())
