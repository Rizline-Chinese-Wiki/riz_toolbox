"""Compile the exact isolated939 solution; no full-test or main acceptance claim."""
import datetime as dt
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[2]
WORK = Path(__file__).resolve().parent
BASE = WORK / 'candidate-evidence-r2'
OUT = Path('D:/riz-build-cache-next/hold-discovery-guard-20261114/solution-r1')
DOTNET = 'C:/Program Files/dotnet/dotnet.exe'
sha = lambda path: hashlib.sha256(Path(path).read_bytes()).hexdigest()
parser = argparse.ArgumentParser(); parser.add_argument('--out', type=Path, required=True)
parser.add_argument('--hosting-project', type=Path); args = parser.parse_args()
OUT = args.out.resolve()
if OUT.parent != Path('D:/riz-build-cache-next/hold-discovery-guard-20261114').resolve(): raise RuntimeError('Use a fresh scoped directory')
if OUT.exists(): raise RuntimeError('Preserve solution validation outputs')
if sha(BASE / 'summary.json') != '0c9a8d8c302cea6cf2799f7967bbe0d500a85b0e6f575f4a30c39d937e719401': raise RuntimeError('Current939 differs')
candidate = json.loads((BASE / 'run/result.json').read_text(encoding='utf-8'))
expected = candidate['frozenInputs']
if not candidate['passed'] or len(expected) != 939: raise RuntimeError('Current candidate is incomplete')
additional = {}
for base in ('src', 'tests', 'examples'):
    for path in (ROOT / base).rglob('*'):
        if path.is_file() and not any(part in ('bin', 'obj', 'artifacts', '.git', 'node_modules') for part in path.relative_to(ROOT).parts):
            name = path.relative_to(ROOT).as_posix()
            if name not in expected: additional[name] = sha(path)
for name in ('.editorconfig', 'Directory.Build.targets'):
    if (ROOT / name).is_file() and name not in expected: additional[name] = sha(ROOT / name)
original_additional = dict(additional); overlay = {}
if args.hosting_project:
    name = 'tests/RizToolbox.Hosting.Tests/RizToolbox.Hosting.Tests.csproj'
    candidate_project = args.hosting_project.resolve()
    if not candidate_project.is_relative_to(ROOT) or not candidate_project.is_file() or name not in additional:
        raise RuntimeError('Hosting overlay escaped the additional solution scope')
    overlay[name] = dict(path=str(candidate_project), baseSha256=additional[name], sha256=sha(candidate_project))
    additional[name] = sha(candidate_project)
OUT.mkdir(parents=True); source = OUT / 'source'; controls = OUT / 'controls'; controls.mkdir()
for name, digest in expected.items():
    target = source / name; target.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(BASE / 'source' / name, target)
    if sha(target) != digest: raise RuntimeError('Frozen source changed')
for name, digest in additional.items():
    origin = Path(overlay[name]['path']) if name in overlay else ROOT / name
    target = source / name; target.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(origin, target)
    if sha(origin) != sha(target) or sha(target) != digest or sha(ROOT / name) != original_additional[name]: raise RuntimeError('Additional solution source changed')
projects = re.findall(r'"([^"\r\n]+\.csproj)"', (source / 'RizToolbox.sln').read_text(encoding='utf-8-sig'))
if not projects or not all((source / name.replace('\\', '/')).is_file() for name in projects):
    raise RuntimeError('Solution project closure is incomplete')
shutil.copyfile(__file__, controls / Path(__file__).name)
if overlay:
    shutil.copyfile(args.hosting_project, controls / 'Hosting.Tests.candidate.csproj')
config = OUT / 'NuGet.Config'; config.write_text('<configuration><packageSources><clear/></packageSources></configuration>', encoding='utf-8')
env = os.environ.copy(); env.update(DOTNET_GENERATE_ASPNET_CERTIFICATE='false', DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false',
    DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1', DOTNET_NOLOGO='1')
argv = [DOTNET, 'build', str(source / 'RizToolbox.sln'), '-c', 'Release', '--artifacts-path', str(OUT / 'artifacts'),
    '--verbosity', 'minimal', '--disable-build-servers', '-m:1', '-p:UseSharedCompilation=false', '-p:NuGetAudit=false', '--configfile', str(config)]
state = dict(schema='hold939-isolated-solution-build/v1', controllerPid=os.getpid(), startedUtc=dt.datetime.now(dt.timezone.utc).isoformat(),
    passed=False, compilerInputs=939, frozenInputs=expected, additionalSolutionInputs=additional,
    originalAdditionalInputs=original_additional, solutionOverlay=overlay,
    completeSolutionInputCount=len(expected)+len(additional), solutionProjects=len(projects), candidateSummarySha256=sha(BASE / 'summary.json'), argv=argv,
    naturalExit=False, buildOnly=True, fullManagedMatrix=False, mainApplied=False, sdkGoalComplete=False, installed=False, gameStarted=False)


def save(): (OUT / 'result.json').write_text(json.dumps(state, indent=2) + '\n', encoding='utf-8')


save(); print('controllerPid=' + str(os.getpid()), flush=True)
try:
    log = OUT / 'solution.log'
    with log.open('xb') as stream:
        child = subprocess.Popen(argv, cwd=source, env=env, stdin=subprocess.DEVNULL, stdout=stream, stderr=subprocess.STDOUT)
        state['childPid'] = child.pid; save(); state['exitCode'] = child.wait(); state['naturalExit'] = True
    body = log.read_text(encoding='utf-8-sig', errors='replace'); state['logSha256'] = sha(log)
    totals = re.findall(r'\b(\d+) Warning\(s\)\s+(\d+) Error\(s\)', body) or re.findall(r'(\d+)\s+个警告\s+(\d+)\s+个错误', body)
    if len(totals) != 1: raise RuntimeError('Missing build totals')
    state.update(warnings=int(totals[0][0]), errors=int(totals[0][1]), frozenInputsUnchanged=all(sha(source / name) == digest for name, digest in expected.items()),
        additionalSolutionInputsUnchanged=all(sha(source / name) == digest for name, digest in additional.items()))
    if state['exitCode'] != 0 or state['errors'] != 0 or not state['frozenInputsUnchanged'] or not state['additionalSolutionInputsUnchanged']: raise RuntimeError('Current939 solution failed')
    state['products'] = {path.relative_to(OUT).as_posix(): sha(path) for path in (OUT / 'artifacts/bin').rglob('*')
        if path.is_file() and path.suffix in ('.dll', '.pdb', '.json')}
    state['passed'] = True
except Exception as error:
    state['failure'] = str(error); raise
finally:
    state['finishedUtc'] = dt.datetime.now(dt.timezone.utc).isoformat(); save()
    print(json.dumps({key:state.get(key) for key in ('passed', 'warnings', 'errors', 'failure')}), flush=True)
