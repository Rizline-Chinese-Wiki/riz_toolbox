"""Fresh complete rebased graph: six/solution, Hold62, producer3 and27 suites."""
import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
WORK = Path(__file__).resolve().parent
BASE = ROOT / 'docs/validation/gameplay-descriptor-readiness-20261114'
PARENT = Path('D:/riz-build-cache-next/line-producer-control-20261114/main-r4')
CACHE = Path('D:/riz-build-cache-next/foundation-current-integration-20261114')
DOTNET = 'C:/Program Files/dotnet/dotnet.exe'
SIX = ['RizToolbox.' + name for name in ('Sdk', 'Core', 'Runtime', 'Adapter', 'Hosting', 'Loader')]
SUITES = ['Core', 'Adapter', 'Hosting', 'Game', 'Runtime', 'Sdk', 'Scope', 'Diagnostics', 'PackageLifecycle',
    'Module', 'Mutation', 'Data', 'Storage', 'Services', 'GameResources', 'GameAppearance', 'GameCamera',
    'Hooks', 'Gameplay', 'GameplaySourceGeneration', 'Settings', 'Visual', 'Performance', 'AudioContent', 'UiInput', 'RunModification', 'SingleEvent']


def sha(path):
    with Path(path).open('rb') as stream: return hashlib.file_digest(stream, 'sha256').hexdigest()


def read(path): return json.loads(Path(path).read_text(encoding='utf-8-sig'))
def now(): return dt.datetime.now(dt.timezone.utc).isoformat()


def require(value, reason):
    if not value: raise RuntimeError(reason)


def main():
    parser = argparse.ArgumentParser(); parser.add_argument('--out', type=Path, required=True); args = parser.parse_args()
    out = args.out.resolve()
    require(out.parent == CACHE.resolve() and not out.exists(), 'Use a fresh current integration output')
    manifest_path = WORK / 'candidate-manifest.json'
    require(sha(manifest_path) == '7b8cea0b1a3a5d7cf06c5821374a50e2433402d9e80c2f6383c1a48bc6dc18d4', 'Candidate manifest differs')
    manifest = read(manifest_path); expected = manifest['expectedInputs']; overlay = {row['target']:row for row in manifest['files']}
    require(sha(BASE / 'validation-summary.json') == manifest['descriptorSummarySha256']
        and len(expected) == 1171 and len(overlay) == 21 and not manifest['mainApplied'], 'Current graph provenance differs')
    require(all(sha(ROOT / name) == digest for name, digest in manifest['parentFullInputs'].items())
        and sha(ROOT / 'scripts/Build.ps1') == manifest['scriptBaseSha256'], 'Current main changed')
    script = 'scripts/Build.ps1'; auxiliary = {script:overlay[script]['sha256']}
    controls = {name:sha(WORK / name) for name in ('candidate-manifest.json', 'prepare_candidate.py', 'validate_candidate_r2.py', 'source-review.md', 'run-r1-interruption-receipt.json')}
    out.mkdir(parents=True); source = out / 'source'; control_dir = out / 'controls'; control_dir.mkdir()
    for name, digest in {**expected, **auxiliary}.items():
        if name in overlay: origin = WORK / 'overlay' / name
        elif name == manifest['rebasePreservedTestFixture']: origin = PARENT / 'source' / name
        else: origin = BASE / 'source' / name
        target = source / name; target.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(origin, target)
        require(sha(origin) == sha(target) == digest, 'Current frozen source differs')
    for name, digest in controls.items():
        shutil.copyfile(WORK / name, control_dir / name); require(sha(control_dir / name) == digest, 'Frozen controller differs')
    config = out / 'NuGet.Config'; config.write_text('<configuration><packageSources><clear/></packageSources></configuration>', encoding='utf-8')
    env = os.environ.copy(); env.update(DOTNET_GENERATE_ASPNET_CERTIFICATE='false', DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false',
        DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1', DOTNET_NOLOGO='1')
    fixtures = {}
    for key, row in read(BASE / 'run/result.json')['nativeFixtures'].items():
        path = BASE / 'fixtures' / key / Path(row['path']).name
        require(sha(path) == row['sha256'], 'Archived owned native fixture differs'); env[key] = str(path)
        fixtures[key] = dict(path=str(path), sha256=row['sha256'])
    held = [('discovery', '--hold-discovery-guard-case', 14), ('alive', '--hold-alive-guard-case', 6),
        ('guard', '--hold-source-guard-case', 10), ('owner', '--hold-native-owner-case', 15), ('existing-hold', '--hold-observation-case', 17)]
    planned = ['six-release', 'solution', 'matrix-script-syntax'] + [kind + '-' + str(case) for kind, _, count in held for case in range(1, count+1)]
    planned += ['producer-' + str(case) for case in (11, 12, 8)] + ['suite-' + name for name in SUITES]
    require(len(planned) == 95, 'Current affected scope differs')
    state = dict(schema='current-foundation-rebased-validation/v1', controllerPid=os.getpid(), startedUtc=now(), passed=False,
        manifestSha256=sha(manifest_path), controls=controls, fullFrozenInputs=expected, auxiliaryInputs=auxiliary,
        completeSolutionInputs=1171, scopedInputs=939, additionalSolutionInputs=232, solutionProjects=45,
        parentSourceOnly=True, parentControllerPid=44740, rebasePreservedTestFixture=manifest['rebasePreservedTestFixture'],
        nativeFixtures=fixtures, expectedSteps=planned, steps=[], plannedSuites=SUITES, selectedHoldChecks=62, selectedProducerCases=[11, 12, 8],
        fullManagedMatrix=False, fullJudgementsRerun=False, parentRendererSuiteRerun=False,
        mainApplied=False, sdkGoalComplete=False, completeSeekPermitted=False, installed=False, gameStarted=False)

    def save():
        temp = out / 'result.json.pending'; temp.write_text(json.dumps(state, indent=2) + '\n', encoding='utf-8'); os.replace(temp, out / 'result.json')

    def stable():
        return (all(sha(source / name) == digest for name, digest in {**expected, **auxiliary}.items())
            and all(sha(WORK / 'overlay' / name) == row['sha256'] for name, row in overlay.items())
            and all(sha(WORK / name) == digest for name, digest in controls.items()))

    artifacts = out / 'artifacts'

    def production():
        return {name:{ext:sha(artifacts / 'bin' / name / 'release' / (name + ext)) for ext in ('.dll', '.pdb')} for name in SIX}

    def run(name, argv, count=None):
        require(stable(), 'Current input/control changed before ' + name)
        row = dict(id=name, argv=list(map(str, argv)), startedUtc=now(), naturalExit=False); state['steps'].append(row); save()
        log = out / (name + '.log')
        with log.open('xb') as stream:
            child = subprocess.Popen(row['argv'], cwd=source, env=env, stdin=subprocess.DEVNULL, stdout=stream, stderr=subprocess.STDOUT)
            row['pid'] = child.pid; save(); row['exitCode'] = child.wait(); row['naturalExit'] = True
        body = log.read_text(encoding='utf-8-sig', errors='replace')
        row.update(finishedUtc=now(), logSha256=sha(log), passNames=re.findall(r'^PASS (.+)$', body, re.M), failNames=re.findall(r'^FAIL (.+)$', body, re.M))
        row['passCount'] = len(row['passNames']); save()
        require(row['exitCode'] == 0 and not row['failNames'] and stable(), 'Current integration step failed: ' + name)
        if name in ('six-release', 'solution'):
            totals = re.findall(r'\b(\d+) Warning\(s\)\s+(\d+) Error\(s\)', body) or re.findall(r'(\d+)\s+个警告\s+(\d+)\s+个错误', body)
            require(len(totals) == 1 and int(totals[0][1]) == 0, 'Current build summary differs')
            row.update(warnings=int(totals[0][0]), errors=0)
            if name == 'six-release': require(row['warnings'] == 0, 'Current six Release warnings')
        else: require(row['passCount'] > 0 and (count is None or row['passCount'] == count), 'Current direct case count differs')
        save(); print(name + ': exit0 checks=' + str(row['passCount']), flush=True)

    save(); print('controllerPid=' + str(os.getpid()), flush=True)
    try:
        for name, project in (('six-release', 'src/RizToolbox.Loader/RizToolbox.Loader.csproj'), ('solution', 'RizToolbox.sln')):
            run(name, [DOTNET, 'build', source / project, '-c', 'Release', '--artifacts-path', artifacts, '--verbosity', 'minimal',
                '--disable-build-servers', '-m:1', '-p:UseSharedCompilation=false', '-p:NuGetAudit=false', '--configfile', config])
            if name == 'six-release': state['productionAfterSix'] = production(); save()
        require(production() == state['productionAfterSix'], 'Current solution changed six production pairs')
        quoted_script = str(source / script).replace("'", "''")
        parse = "$t=$null; $e=$null; [void][System.Management.Automation.Language.Parser]::ParseFile('" + quoted_script + "',[ref]$t,[ref]$e); if($e.Count){$e | ForEach-Object {$_.Message}; exit 1}; Write-Output 'PASS current managed matrix has valid PowerShell syntax'"
        run('matrix-script-syntax', ['powershell.exe', '-NoProfile', '-NonInteractive', '-Command', parse], 1)
        judgements = artifacts / 'bin/RizToolbox.Judgements.Tests/release/RizToolbox.Judgements.Tests.dll'
        for kind, selector, count in held:
            for case in range(1, count+1): run(kind + '-' + str(case), [DOTNET, judgements, selector, case], 1)
        for case in (11, 12, 8): run('producer-' + str(case), [DOTNET, judgements, '--line-producer-control-case', case], 1)
        for suite in SUITES:
            project_name = 'RizToolbox.' + suite + '.Tests'
            project = source / 'tests' / project_name / (project_name + '.csproj')
            assembly_name = next((node.text for node in ET.fromstring(project.read_bytes()).iter('AssemblyName') if node.text), project_name)
            assembly = artifacts / 'bin' / project_name / 'release' / (assembly_name + '.dll')
            run('suite-' + suite, [DOTNET, assembly], 14 if suite == 'GameplaySourceGeneration' else 175 if suite == 'Gameplay' else None)
        require([row['id'] for row in state['steps']] == planned and stable(), 'Current completed scope differs')
        state['fullFrozenInputsAfter'] = {name:sha(source / name) for name in expected}
        state['fullFrozenInputsUnchanged'] = state['fullFrozenInputsAfter'] == expected
        state['mainSourceUnchanged'] = all(sha(ROOT / name) == digest for name, digest in manifest['parentFullInputs'].items()) and sha(ROOT / script) == manifest['scriptBaseSha256']
        require(state['fullFrozenInputsUnchanged'] and state['mainSourceUnchanged'], 'Current source graph changed')
        state['productionAfterTests'] = production(); require(state['productionAfterTests'] == state['productionAfterSix'], 'Current production pairs changed during tests')
        state['products'] = {path.relative_to(out).as_posix():sha(path) for path in (artifacts / 'bin').rglob('*') if path.is_file()}
        state['suiteCount'] = len(SUITES); state['suitePassCount'] = sum(row['passCount'] for row in state['steps'] if row['id'].startswith('suite-'))
        state['passed'] = True
    except Exception as error:
        state['failure'] = str(error); raise
    finally:
        state['finishedUtc'] = now(); save(); print(json.dumps({name:state.get(name) for name in ('passed', 'suiteCount', 'suitePassCount', 'failure')}), flush=True)


if __name__ == '__main__': main()
