"""Freeze complete1172 plus current SDK distribution helpers; adapt original14 controls."""
import ast
import difflib
import hashlib
import json
from pathlib import Path
import shutil

ROOT = Path(__file__).resolve().parents[2]
WORK = Path(__file__).resolve().parent
OLD = ROOT / 'work/sdk-current-integration-20261114'
BASE = Path('D:/riz-build-cache-next/runtime-tick-admission-20261115/run-r2')
SUP = BASE.parent / 'run-r2-supervision'
sha = lambda p: hashlib.sha256(Path(p).read_bytes()).hexdigest()
read = lambda p: json.loads(Path(p).read_text(encoding='utf-8-sig'))


def require(ok, reason):
    if not ok: raise RuntimeError(reason)


def replace(body, old, new, count=1):
    require(body.count(old) == count, 'Control anchor differs: ' + old)
    return body.replace(old, new)


def write(path, body):
    require(not path.exists(), 'Preserve existing control: ' + str(path))
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(body, encoding='utf-8', newline='\n')


require(not (WORK / 'source-manifest.json').exists(), 'Preserve prior frozen controls')
require(sha(BASE / 'result.json') == 'fbb63a9cd13d61b7df8a1625afc7ebaa54b849f568c24168e950ce24f2b39538', 'Exact startup result differs')
candidate = read(BASE / 'result.json'); waited = read(SUP / 'controller-exit.json')
expected = candidate['fullFrozenInputs']; auxiliary = candidate['auxiliaryInputs']
require(len(expected) == 1172 and candidate['passed'] and candidate['fullFrozenInputsUnchanged']
    and candidate['controllerPid'] == waited['controllerPid'] == waited['resultControllerPid'] == 43772
    and waited['supervisorPid'] == 34372 and waited['controllerExited'] and waited['actualControllerExitCode'] == 0
    and waited['resultSha256'] == sha(BASE / 'result.json')
    and waited['exitObservedBy'] == 'original subprocess.Popen.wait process handle', 'Base startup is not naturally terminal')
for name, digest in {**expected, **auxiliary}.items():
    require(sha(BASE / 'source' / name) == digest, 'Base frozen source differs: ' + name)
distribution = sorted(p.relative_to(ROOT).as_posix() for p in (ROOT / 'tests/RizToolbox.Distribution.Tests').rglob('*')
    if p.is_file() and p.suffix in ('.cs', '.csproj', '.props', '.targets', '.json')
    and not any(part in ('bin', 'obj', '.git') for part in p.parts))
require(len(distribution) == 2 and all(name in expected and sha(ROOT / name) == expected[name] for name in distribution),
    'Distribution source must overlap complete1172 exactly')
names = ['scripts/Pack-Sdk.ps1', 'scripts/Test-SdkDistribution.ps1', 'packaging/sdk/README.md']
names += sorted(p.relative_to(ROOT).as_posix() for p in (ROOT / 'templates/riz-plugin').rglob('*')
    if p.is_file() and not any(part in ('bin', 'obj', '.git') for part in p.parts))
additional = dict(auxiliary)
for name in names:
    source = ROOT / name; target = WORK / 'frozen-current-sources' / name
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(source, target); require(sha(source) == sha(target), 'Current helper changed during freeze')
    additional[name] = sha(target)
old_state = read(Path('D:/riz-build-cache-next/sdk-current-integration-20261114/run-r1/result.json'))
pins = old_state['dependencies']
require(len(pins) == 4, 'Four original framework pins required')
for row in pins.values(): require(sha(row['original']) == row['sha256'], 'Original framework package changed')
manifest = dict(schema='sdk-startup-complete1172-source-manifest/v1', expectedInputs=expected,
    auxiliaryInputs=auxiliary, additionalInputs=additional, compilerInputs=1172, additionalCompilerInputs=0,
    distributionCompilerInputsOverlap=2, distinctCompilerInputs=1172,
    baseResultSha256=sha(BASE / 'result.json'), baseSupervisionSha256=sha(SUP / 'controller-exit.json'),
    baseControllerPid=43772, baseSupervisorPid=34372, baseActualExitCode=0, dependencyPins=pins)
write(WORK / 'source-manifest.json', json.dumps(manifest, indent=2) + '\n')
for origin, name in ((BASE / 'result.json', 'base-startup-result.json'), (SUP / 'controller-exit.json', 'base-startup-controller-exit.json')):
    shutil.copyfile(origin, WORK / name)
old_inputs = old_state['frozenInputs']
diff = dict(schema='sdk-startup-source-diff/v1', previousCompilerInputs=len(old_inputs), compilerInputs=len(expected),
    addedInputs={k:v for k,v in expected.items() if k not in old_inputs}, removedInputs={k:v for k,v in old_inputs.items() if k not in expected},
    changedInputs={k:dict(previous=old_inputs[k], current=v) for k,v in expected.items() if k in old_inputs and old_inputs[k] != v},
    original14RunnerUnchanged=old_state['originalRunnerSha256'] == additional['scripts/Test-SdkDistribution.ps1'],
    currentHelpersUnchangedSincePrevious={k:old_state['additionalInputs'].get(k) == v for k,v in additional.items()},
    oldResultsReused=False)
write(WORK / 'source-diff.json', json.dumps(diff, indent=2) + '\n')

original = (OLD / 'run_candidate.py').read_text(encoding='utf-8'); body = original
body = body.replace('on1171', 'on1172').replace('sdk-current-integration-20261114', 'sdk-startup-candidate-20261115')
body = replace(body, "BASE = Path('D:/riz-build-cache-next/foundation-current-integration-20261114/run-r2')", "BASE = Path('D:/riz-build-cache-next/runtime-tick-admission-20261115/run-r2')")
body = replace(body, "MANIFEST = ROOT / 'work/foundation-current-integration-next-20261114/candidate-manifest.json'", "MANIFEST = WORK / 'source-manifest.json'")
start = body.index("    require(sha(MANIFEST)"); end = body.index("    OUT.mkdir(parents=True)", start)
body = body[:start] + f'''    require(sha(MANIFEST) == '{sha(WORK / 'source-manifest.json')}', 'Prepared manifest differs')
    declared = read(MANIFEST); candidate = read(BASE / 'result.json')
    waited = read(BASE.parent / 'run-r2-supervision/controller-exit.json')
    require(sha(BASE / 'result.json') == declared['baseResultSha256']
        and sha(BASE.parent / 'run-r2-supervision/controller-exit.json') == declared['baseSupervisionSha256']
        and waited['controllerExited'] and waited['actualControllerExitCode'] == 0
        and waited['controllerPid'] == waited['resultControllerPid'] == candidate['controllerPid'] == 43772
        and waited['supervisorPid'] == 34372
        and waited['exitObservedBy'] == 'original subprocess.Popen.wait process handle'
        and candidate['passed'] and candidate['fullFrozenInputsUnchanged']
        and candidate['fullFrozenInputs'] == declared['expectedInputs'] and len(declared['expectedInputs']) == 1172,
        'Complete1172 base source/original wait differs')
    require(DOTNET.is_file() and PWSH.is_file(), 'Known CLI tools are unavailable')
    expected = declared['expectedInputs']; extras = declared['additionalInputs']
''' + body[end:]
body = replace(body, 'Additional source conflicts with1171 snapshot', 'Additional source conflicts with1172 snapshot')
body = replace(body, "else ROOT / name, target)", "else WORK / 'frozen-current-sources' / name, target)")
body = replace(body, "        path = Path(package); require(path.is_file(), 'Required cached framework package is unavailable: ' + name)",
    "        path = Path(package); require(path.is_file() and sha(path) == declared['dependencyPins'][path.name]['sha256'], 'Pinned original framework package differs: ' + name)")
body = replace(body, " + '\"/></packageSources></configuration>'", " + '\"/></packageSources><config><add key=\"globalPackagesFolder\" value=\"' + escape(str(OUT / 'formal-host-nuget-cache')) + '\"/></config></configuration>'")
body = replace(body, "    shutil.copyfile(MANIFEST, controls / 'source-manifest.json')",
    "    shutil.copyfile(MANIFEST, controls / 'source-manifest.json')\n    for name in ('base-startup-result.json', 'base-startup-controller-exit.json', 'source-diff.json', 'control-adaptation.diff', 'prepare_controls.py', 'supervise.py'):\n        shutil.copyfile(WORK / name, controls / name)")
body = replace(body, 'compilerInputs=1171', 'compilerInputs=1172')
body = replace(body, 'baseCurrentControllerPid=28536, baseCurrentValidationAccepted=False', "baseCurrentControllerPid=43772, baseStartupValidationPassed=True, baseResultSha256=declared['baseResultSha256'], baseSupervisionSha256=declared['baseSupervisionSha256']")
body = replace(body, 'additionalCompilerInputs=2,', 'additionalCompilerInputs=0, distributionCompilerInputsOverlap=2, distinctCompilerInputs=1172,')
body = replace(body, 'templateEngineIsolated=True, installed=False, gameStarted=False)', "templateEngineIsolated=True, formalHostCacheIsolated=True, environmentOverrides={key:env[key] for key in ('DOTNET_GENERATE_ASPNET_CERTIFICATE', 'DOTNET_ADD_GLOBAL_TOOLS_TO_PATH', 'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE', 'DOTNET_NOLOGO')}, installed=False, gameStarted=False)")
body = replace(body, 'stderr=subprocess.STDOUT)', 'stderr=subprocess.STDOUT, creationflags=subprocess.CREATE_NO_WINDOW)')
body = replace(body, "        row.update(finishedUtc=now(), logSha256=sha(log),", "        row.update(finishedUtc=now(), creationFlags=subprocess.CREATE_NO_WINDOW, hidden=True, logSha256=sha(log),")
ast.parse(body); write(WORK / 'run_candidate.py', body)

old_supervisor = (OLD / 'supervise.py').read_text(encoding='utf-8'); supervisor = old_supervisor
supervisor = supervisor.replace('sdk-current-integration-20261114', 'sdk-startup-candidate-20261115')
supervisor = supervisor.replace('current-sdk-supervised-root/v1', 'startup-sdk-supervised-root/v1').replace('current-sdk-supervisor-launch/v1', 'startup-sdk-supervisor-launch/v1')
supervisor = replace(supervisor, 'stdout=stream, stderr=subprocess.STDOUT)', 'stdout=stream, stderr=subprocess.STDOUT, creationflags=subprocess.CREATE_NO_WINDOW)')
supervisor = replace(supervisor, "data.update(controllerPid=child.pid, childCreatedUtc=now())", "data.update(controllerPid=child.pid, childCreatedUtc=now(), creationFlags=subprocess.CREATE_NO_WINDOW, hidden=True)")
supervisor = replace(supervisor, 'subprocess.DETACHED_PROCESS | subprocess.CREATE_NEW_PROCESS_GROUP | subprocess.CREATE_BREAKAWAY_FROM_JOB', 'subprocess.CREATE_NO_WINDOW | subprocess.CREATE_NEW_PROCESS_GROUP | subprocess.CREATE_BREAKAWAY_FROM_JOB')
supervisor = replace(supervisor, "    SUP.mkdir()", "    SUP.parent.mkdir(parents=True, exist_ok=True)\n    SUP.mkdir()")
ast.parse(supervisor); write(WORK / 'supervise.py', supervisor)
diff_text = ''.join(difflib.unified_diff(original.splitlines(keepends=True), body.splitlines(keepends=True), fromfile='previous/run_candidate.py', tofile='current/run_candidate.py'))
diff_text += ''.join(difflib.unified_diff(old_supervisor.splitlines(keepends=True), supervisor.splitlines(keepends=True), fromfile='previous/supervise.py', tofile='current/supervise.py'))
write(WORK / 'control-adaptation.diff', diff_text)
for name in ('run_candidate.py', 'supervise.py'): shutil.copyfile(OLD / name, WORK / ('previous-' + name))
print(json.dumps(dict(prepared=True, executed=False, compilerInputs=1172, additionalCompilerInputs=0,
    changedInputs=len(diff['changedInputs']), addedInputs=len(diff['addedInputs']),
    manifestSha256=sha(WORK / 'source-manifest.json'), controls={name:sha(WORK / name) for name in ('run_candidate.py', 'supervise.py')})))
