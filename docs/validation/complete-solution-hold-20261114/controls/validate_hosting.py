"""Verify fixture resolution in both relocated and normal build output modes."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parents[2]
WORK = Path(__file__).resolve().parent
SOLUTION = Path('D:/riz-build-cache-next/hold-discovery-guard-20261114/solution-r4')
DOTNET = 'C:/Program Files/dotnet/dotnet.exe'
sha = lambda path: hashlib.sha256(Path(path).read_bytes()).hexdigest()
parser = argparse.ArgumentParser(); parser.add_argument('--mode', choices=('artifacts', 'default'), required=True); args = parser.parse_args()
OUT = Path('D:/riz-build-cache-next/hosting-fixture-output-20261114') / (args.mode + '-r1')
if OUT.exists(): raise RuntimeError('Preserve validation results')
solution = json.loads((SOLUTION / 'result.json').read_text(encoding='utf-8'))
if not solution['passed'] or solution['errors'] != 0 or not solution['additionalSolutionInputsUnchanged']: raise RuntimeError('Complete solution is not accepted for testing')
source = SOLUTION / 'source'; expected = {**solution['frozenInputs'], **solution['additionalSolutionInputs']}
if not all(sha(source / name) == digest for name, digest in expected.items()): raise RuntimeError('Frozen complete solution changed')
OUT.mkdir(parents=True); config = OUT / 'NuGet.Config'; config.write_text('<configuration><packageSources><clear/></packageSources></configuration>', encoding='utf-8')
env = os.environ.copy(); env.update(DOTNET_GENERATE_ASPNET_CERTIFICATE='false', DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false',
    DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1', DOTNET_NOLOGO='1')
state = dict(schema='hosting-fixture-output-validation/v1', mode=args.mode, controllerPid=os.getpid(), passed=False,
    solutionResultSha256=sha(SOLUTION / 'result.json'), fullFrozenInputs=expected, steps=[], mainApplied=False,
    sdkGoalComplete=False, installed=False, gameStarted=False)


def save(): (OUT / 'result.json').write_text(json.dumps(state, indent=2) + '\n', encoding='utf-8')


def run(name, argv, expected_count=None):
    row = dict(id=name, argv=list(map(str, argv)), naturalExit=False); state['steps'].append(row); save()
    log = OUT / (name + '.log')
    with log.open('xb') as stream:
        child = subprocess.Popen(row['argv'], cwd=source, env=env, stdin=subprocess.DEVNULL, stdout=stream, stderr=subprocess.STDOUT)
        row['pid'] = child.pid; save(); row['exitCode'] = child.wait(); row['naturalExit'] = True
    body = log.read_text(encoding='utf-8-sig', errors='replace')
    row.update(logSha256=sha(log), passNames=re.findall(r'^PASS (.+)$', body, re.M), failNames=re.findall(r'^FAIL (.+)$', body, re.M)); save()
    if row['exitCode'] != 0 or row['failNames']: raise RuntimeError('Hosting validation failed: ' + name)
    if expected_count is not None and len(row['passNames']) != expected_count: raise RuntimeError('Hosting check count differs')
    print(name + ': exit0 checks=' + str(len(row['passNames'])), flush=True)


save(); print('controllerPid=' + str(os.getpid()), flush=True)
try:
    if args.mode == 'default':
        run('default-build', [DOTNET, 'build', source / 'tests/RizToolbox.Hosting.Tests/RizToolbox.Hosting.Tests.csproj',
            '-c', 'Release', '--verbosity', 'minimal', '--disable-build-servers', '-m:1', '-p:UseSharedCompilation=false', '-p:NuGetAudit=false', '--configfile', config])
        assembly = source / 'tests/RizToolbox.Hosting.Tests/bin/Release/net8.0/RizToolbox.Hosting.Tests.dll'
    else:
        assembly = SOLUTION / 'artifacts/bin/RizToolbox.Hosting.Tests/release/RizToolbox.Hosting.Tests.dll'
        if sha(assembly) != solution['products'][assembly.relative_to(SOLUTION).as_posix()]: raise RuntimeError('Relocated assembly changed')
    run('hosting20', [DOTNET, assembly], 20)
    state['assemblySha256'] = sha(assembly)
    state['fixtureOutputs'] = {path.relative_to(assembly.parent).as_posix(): sha(path) for path in (assembly.parent / 'Fixtures').rglob('*.dll')}
    state['fullFrozenInputsUnchanged'] = all(sha(source / name) == digest for name, digest in expected.items())
    if not state['fullFrozenInputsUnchanged']: raise RuntimeError('Frozen inputs changed during Hosting validation')
    state['passed'] = True
except Exception as error:
    state['failure'] = str(error); raise
finally:
    save(); print(json.dumps({key:state.get(key) for key in ('passed', 'mode', 'failure')}), flush=True)
