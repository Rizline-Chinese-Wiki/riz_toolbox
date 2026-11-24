"""Build the frozen camera/scene candidate and run its five real retained-path cases."""
from concurrent.futures import ThreadPoolExecutor
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
import threading
import time

WORK = Path(__file__).resolve().parent
ROOT = WORK.parents[1]
CANDIDATE = ROOT / 'work/line-camera-scene-next-20261113'
PRIOR = ROOT / 'docs/validation/line-ring-world-read-20261113'
HELPER = ROOT / 'work/line-ring-source-validation-20261113/run_validation.py'

def require(ok, why):
    if not ok: raise RuntimeError(why)

def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()

def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))

def main():
    parser = argparse.ArgumentParser(); parser.add_argument('--attempt', required=True); parser.add_argument('--case', type=int)
    options = parser.parse_args(); require(re.fullmatch(r'root-r[1-9][0-9]*', options.attempt), 'Unexpected attempt')
    out = Path('D:/riz-build-cache-next/line-camera-scene-read-20261113') / options.attempt
    require(not out.exists(), 'Preserve earlier execution attempts')
    require(sha(HELPER) == '61c2983ae117322b25ff96ad75b659b870fdadf9aada9274727248cd5ca98fd4', 'Scanner changed')
    spec = importlib.util.spec_from_file_location('scene_shadow_inputs', HELPER)
    scanner = importlib.util.module_from_spec(spec); spec.loader.exec_module(scanner)
    before = scanner.inputs()
    require(len(before) == 892 and before == read(PRIOR / 'run/inputs-after.json'), 'Accepted 892 main changed')
    require(sha(PRIOR / 'validation-summary.json') == 'ce8b17aa85be2ad8923c9792abfb375c38389ee34ee2b8c17fe2c5f6126cdc9b', 'Prior acceptance changed')
    manifest = read(CANDIDATE / 'candidate-manifest.json')
    require(sha(CANDIDATE / 'candidate-manifest.json') == 'bc3e4e119041ad1eef20a0efb0a80ab4f0f0c92fb1b74375d8fafc64710aa006', 'Frozen candidate manifest changed')
    candidate = {row['candidatePath']: sha(CANDIDATE / row['candidatePath']) for row in manifest['files']}
    require(candidate == {row['candidatePath']: row['candidateSha256'] for row in manifest['files']}, 'Candidate bytes changed')
    require(len(candidate) == 10, 'Unexpected scene candidate scope')
    controls = {str(p): sha(p) for p in WORK.iterdir() if p.is_file() and p.suffix in ('.py', '.props', '.csproj', '.Config', '.cs')}
    pins = read(PRIOR / 'artifact-manifest.json')
    references = {str(PRIOR / 'binaries/production' / (name + ext)): pins['binaries/production/' + name + ext]
                  for name in ('RizToolbox.Sdk', 'RizToolbox.Core', 'RizToolbox.Runtime') for ext in ('.dll', '.pdb')}
    require(all(sha(path) == digest for path, digest in references.items()), 'Accepted shadow references changed')
    registry = (CANDIDATE / 'tests/RizToolbox.Judgements.Tests/LineRingSource/NativeSeekLineRingSourceChecks.cs').read_text(encoding='utf-8')
    names = re.findall(r'\("([^"\r\n]+)",', registry.split('[] Cases =', 1)[1].split('};', 1)[0])
    require(len(names) == 24, 'Expected 19 prior source plus five new scene cases')
    indexes = [options.case] if options.case else list(range(20, 25))
    require(all(1 <= index <= len(names) for index in indexes), 'Invalid case index')
    fixtures = read(PRIOR / 'prior/fixtures/native-source-record.json')['nativeFixtures']
    environment = os.environ.copy()
    environment.update(DOTNET_GENERATE_ASPNET_CERTIFICATE='false', DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false',
                       DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1', DOTNET_NOLOGO='1')
    for key, row in fixtures.items():
        require(sha(row['path']) == row['sha256'], 'Native fixture changed'); environment[key] = row['path']
    out.mkdir(parents=True); artifacts = out / 'artifacts'; lock = threading.Lock()
    state = {'controllerPid': os.getpid(), 'startedUtc': dt.datetime.now(dt.timezone.utc).isoformat(),
             'passed': False, 'mainInputsBefore': before, 'candidateInputsBefore': candidate, 'controls': controls,
             'references': references, 'nativeFixtures': fixtures, 'selectedCases': indexes,
             'caseNames': names, 'steps': [], 'activeChildren': [], 'mainApplied': False,
             'sdkGoalComplete': False, 'completeSeekPermitted': False, 'atomicSnapshot': False,
             'gameStarted': False, 'installed': False}
    def save():
        staged = out / 'result.json.pending'; staged.write_text(json.dumps(state, indent=2) + '\n', encoding='utf-8'); os.replace(staged, out / 'result.json')
    def stable():
        return (scanner.inputs() == before and all(sha(CANDIDATE / p) == h for p, h in candidate.items())
                and all(sha(p) == h for p, h in {**controls, **references}.items()))
    def step(name, argv, expected=None):
        require(stable() and scanner.available_commit() >= 512 * 1024 * 1024, 'Changed bytes or low commit before child')
        stdout, stderr = out / (name + '.stdout.log'), out / (name + '.stderr.log')
        print('START ' + name, flush=True)
        with stdout.open('xb') as output, stderr.open('xb') as errors:
            child = subprocess.Popen(list(map(str, argv)), cwd=ROOT, env=environment, stdin=subprocess.DEVNULL,
                                     stdout=output, stderr=errors, creationflags=subprocess.CREATE_NO_WINDOW)
            with lock: state['activeChildren'].append({'name': name, 'pid': child.pid}); save()
            forced = False; deadline = time.monotonic() + 1800
            while child.poll() is None:
                try: child.wait(timeout=2)
                except subprocess.TimeoutExpired:
                    if time.monotonic() > deadline or scanner.available_commit() < 512 * 1024 * 1024:
                        forced = True; child.kill(); child.wait(); break
        body, errors = stdout.read_text(encoding='utf-8-sig'), stderr.read_text(encoding='utf-8-sig')
        passes, runs = re.findall(r'^PASS (.+)$', body, re.M), re.findall(r'^RUN (.+)$', body, re.M)
        good = child.returncode == 0 and not forced and not errors and not re.search(r'^FAIL\b', body, re.M)
        if expected is None:
            good = good and ('已成功生成。' in body or 'Build succeeded.' in body)
            good = good and all(re.findall(r'^\s*(\d+) (?:个' + cn + r'|' + en + r'\(s\))\s*$', body, re.M) == ['0']
                                for cn, en in (('警告', 'Warning'), ('错误', 'Error')))
        else:
            good = good and passes == runs == [expected] and re.findall(r'(\d+)/(\d+) passed\.', body) == [('1', '1')]
        with lock:
            state['steps'].append({'name': name, 'arguments': list(map(str, argv)), 'pid': child.pid,
                                   'exitCode': child.returncode, 'naturalExit': not forced, 'matchesExpected': bool(good),
                                   'passNames': passes, 'stdoutSha256': sha(stdout), 'stderrSha256': sha(stderr)})
            state['activeChildren'] = [row for row in state['activeChildren'] if row['name'] != name]; save()
        print('END ' + name + ' exit=' + str(child.returncode), flush=True)
        return bool(good)
    try:
        require(step('build', [scanner.DOTNET, 'build', WORK / 'Judgements.SceneShadow.csproj', '-c', 'Release',
                '--artifacts-path', artifacts, '--verbosity', 'minimal', '--disable-build-servers', '-m:1',
                '-p:UseSharedCompilation=false', '-p:NuGetAudit=false', '-p:RestoreConfigFile=' + str(WORK / 'NuGet.Config')]), 'Shadow build failed')
        state['binariesBefore'] = {str(p): sha(p) for p in sorted((artifacts / 'bin').rglob('*')) if p.suffix in ('.dll', '.pdb')}
        assembly = artifacts / 'bin/Judgements.SceneShadow/release/RizToolbox.Judgements.Tests.dll'
        require(assembly.exists() and state['binariesBefore'], 'Missing shadow output')
        with ThreadPoolExecutor(max_workers=2) as pool:
            outcomes = list(pool.map(lambda index: step(f'scene-{index:02}', [scanner.DOTNET, assembly, 'source', index], names[index - 1]), indexes))
        require(all(outcomes), 'One or more selected scene checks failed')
        require(len(state['steps']) == len(indexes) + 1 and stable(), 'Incomplete or changed execution')
        require(all(sha(p) == h for p, h in state['binariesBefore'].items()), 'Tested binaries changed')
        state['passed'] = True
    except Exception as error:
        state['failure'] = str(error)
    finally:
        state['sourceAndControlsUnchanged'] = stable()
        state['fixturesUnchanged'] = all(sha(row['path']) == row['sha256'] for row in fixtures.values())
        state['passed'] = state['passed'] and state['sourceAndControlsUnchanged'] and state['fixturesUnchanged']
        state['finishedUtc'] = dt.datetime.now(dt.timezone.utc).isoformat(); save()
        print(json.dumps({'passed': state['passed'], 'steps': len(state['steps']), 'failure': state.get('failure'),
                          'resultSha256': sha(out / 'result.json')}), flush=True)
    return 0 if state['passed'] else 1

if __name__ == '__main__': sys.exit(main())
