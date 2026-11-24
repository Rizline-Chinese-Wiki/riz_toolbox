"""Archive only the completed complete1172 SDK14 run; check without original paths."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil

ROOT = Path(__file__).resolve().parents[2]
WORK = Path(__file__).resolve().parent
RUN = Path('D:/riz-build-cache-next/sdk-startup-candidate-20261115/run-r1')
DEST = ROOT / 'docs/validation/sdk-startup-candidate-20261115'
RESULT_SHA = '4813c991852dd9cf54d75777cbe693c769df980b867bca14a0e643efc8380a95'
sha = lambda p: hashlib.sha256(Path(p).read_bytes()).hexdigest()
read = lambda p: json.loads(Path(p).read_text(encoding='utf-8-sig'))


def require(ok, message):
    if not ok:
        raise RuntimeError(message)


def local(base, name):
    raw = base / name
    for part in (raw, *raw.parents):
        if part == base:
            break
        require(not part.is_symlink() and not getattr(part.lstat(), 'st_file_attributes', 0) & 0x400, 'Reparse archive path')
    path = raw.resolve()
    require(path.is_relative_to(base) and path.is_file(), 'Artifact escaped archive')
    return path


def check(base):
    base = base.resolve()
    summary = read(local(base, 'validation-summary.json'))
    require(summary['schema'] == 'sdk-startup-candidate-distribution-evidence/v1'
        and summary['passed'] and summary['candidateOnly'] and not summary['mainApplied']
        and not summary['fullManagedMatrix'] and not summary['sdkGoalComplete']
        and not summary['dependencyPackageBytesIncluded'] and not summary['independentRebuildFromArchive'], 'Archive scope differs')
    for name, row in summary['artifacts'].items():
        path = local(base, name)
        require(sha(path) == row['sha256'] and path.stat().st_size == row['bytes'], 'Artifact differs: ' + name)
    require({p.relative_to(base).as_posix() for p in base.rglob('*') if p.is_file()}
        == set(summary['artifacts']) | {'validation-summary.json'}, 'Inventory differs')
    state = read(local(base, 'run/result.json'))
    witness = read(local(base, 'controls/root-exit.json'))
    waited = read(local(base, 'supervision/controller-exit.json'))
    require(sha(local(base, 'run/result.json')) == RESULT_SHA == waited['resultSha256'] == witness['resultSha256'], 'Actual result changed')
    require(state['passed'] and state['checksPassed'] == 14 and state['frozenInputsUnchanged']
        and len(state['frozenInputs']) == state['compilerInputs'] == state['distinctCompilerInputs'] == 1172
        and state['additionalCompilerInputs'] == 0 and state['distributionCompilerInputsOverlap'] == 2, 'Compiler input scope differs')
    require(waited['controllerExited'] and waited['actualControllerExitCode'] == witness['actualControllerExitCode'] == 0
        and waited['controllerPid'] == waited['resultControllerPid'] == witness['controllerPid'] == state['controllerPid'] == 15816
        and waited['supervisorPid'] == witness['supervisorPid'] == 34952 and waited['resultPassed'] and waited['resultInputsUnchanged']
        and waited['exitObservedBy'] == witness['exitObservedBy'] == 'original subprocess.Popen.wait process handle'
        and waited['controllerControlSha256'] == sha(local(base, 'controls/run_candidate.py'))
        and waited['supervisorControlSha256'] == sha(local(base, 'controls/supervise.py'))
        and witness['supervisionSha256'] == sha(local(base, 'supervision/controller-exit.json')), 'Original wait differs')
    require([s['id'] for s in state['steps']] == ['formal-host-build', 'sdk-distribution'], 'Step scope differs')
    for row in state['steps']:
        log = local(base, 'run/' + row['id'] + '.log')
        body = log.read_text(encoding='utf-8-sig')
        require(row['naturalExit'] and row['exitCode'] == 0 and not row['failNames']
            and row['hidden'] and row['creationFlags'] == 0x08000000 and sha(log) == row['logSha256']
            and re.findall(r'^PASS (.+)$', body, re.M) == row['passNames']
            and not re.findall(r'^FAIL (.+)$', body, re.M), 'Natural step/log differs')
    require(len(state['steps'][1]['passNames']) == len(set(state['steps'][1]['passNames'])) == 14, 'SDK checks differ')
    manifest = read(local(base, 'controls/source-manifest.json'))
    require(state['sourceManifestSha256'] == sha(local(base, 'controls/source-manifest.json'))
        and state['frozenInputs'] == manifest['expectedInputs'] and state['additionalInputs'] == manifest['additionalInputs'], 'Frozen manifest differs')
    for name, digest in {**state['frozenInputs'], **state['additionalInputs']}.items():
        require(sha(local(base, 'source/' + name)) == digest, 'Frozen input differs: ' + name)
    require(sum(n.startswith('tests/RizToolbox.Distribution.Tests/') for n in state['frozenInputs']) == 2, 'Distribution overlap differs')
    base_state = read(local(base, 'controls/base-startup-result.json'))
    base_wait = read(local(base, 'controls/base-startup-controller-exit.json'))
    require(state['baseStartupValidationPassed'] and state['baseCurrentControllerPid'] == base_state['controllerPid'] == base_wait['controllerPid'] == base_wait['resultControllerPid'] == 43772
        and base_wait['supervisorPid'] == 34372 and base_wait['controllerExited'] and base_wait['actualControllerExitCode'] == 0
        and base_wait['exitObservedBy'] == 'original subprocess.Popen.wait process handle'
        and base_state['passed'] and base_state['fullFrozenInputsUnchanged'] and base_state['fullFrozenInputs'] == state['frozenInputs']
        and sha(local(base, 'controls/base-startup-result.json')) == state['baseResultSha256'] == manifest['baseResultSha256'] == base_wait['resultSha256']
        and sha(local(base, 'controls/base-startup-controller-exit.json')) == state['baseSupervisionSha256'] == manifest['baseSupervisionSha256'], 'Exact startup base differs')
    require(sha(local(base, 'controls/Test-SdkDistribution.Offline.ps1')) == state['offlineRunnerSha256']
        and sha(local(base, 'controls/Test-SdkDistribution.original.ps1')) == state['originalRunnerSha256']
        == sha(local(base, 'source/scripts/Test-SdkDistribution.ps1')), 'Actual distribution controls differ')
    fixity = read(local(base, 'dependency-feed/fixity.json'))
    require(len(fixity['packages']) == len(state['dependencies']) == 4 and not fixity['packageBytesIncluded']
        and not fixity['independentRebuildFromArchive'] and fixity['archiveCheckVerifiesRecordedPinsOnly'], 'Dependency scope differs')
    for name, row in state['dependencies'].items():
        require(row['sha256'] == manifest['dependencyPins'][name]['sha256'] == fixity['packages'][name]['sha256']
            and fixity['packages'][name]['bytes'] > 0, 'Frozen dependency pin differs')
    proof = read(local(base, 'distribution/result.json'))
    require(sha(local(base, 'distribution/result.json')) == state['distributionProof']['sha256']
        and proof['checksPassed'] == 14 and not proof['runtimeGameVerification']
        and proof['packageSha256'] == state['package']['sha256'] == sha(local(base, 'package/RizToolbox.Sdk.1.2.0.nupkg')), 'Distribution output differs')
    for author, mod in (('AuthorOne', 'author.one'), ('AuthorTwo', 'author.two')):
        require(sha(local(base, f'distribution/generated/{author}/bin/Release/net6.0/{author}.dll'))
            == sha(local(base, f'distribution/mods/{mod}/{author}.dll')), 'Actual packaged plugin differs')
    for name in ('RizToolbox.Distribution.Tests.dll', 'RizToolbox.Hosting.dll', 'RizToolbox.Core.dll', 'RizToolbox.Sdk.dll'):
        local(base, 'formal-host/' + name)
    require(state['templateEngineIsolated'] and state['formalHostCacheIsolated']
        and state['environmentOverrides'] == dict(DOTNET_GENERATE_ASPNET_CERTIFICATE='false', DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false',
            DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1', DOTNET_NOLOGO='1')
        and not state['fullManagedMatrix'] and not state['sdkGoalComplete'] and not state['runtimeGameVerification']
        and not state['installed'] and not state['gameStarted'] and state['candidateOnly'] and not state['mainApplied'], 'SDK result overclaims scope')
    print(json.dumps(dict(passed=True, archiveOnly=True, candidateOnly=True, checks=14, compilerInputs=1172,
        additionalCompilerInputs=0, distributionCompilerInputsOverlap=2, files=len(summary['artifacts']) + 1,
        dependencyPackageBytesIncluded=False, sdkGoalComplete=False, summarySha256=sha(base / 'validation-summary.json'))))


def publish():
    require(not DEST.exists(), 'Preserve existing archive')
    state = read(RUN / 'result.json')
    require(sha(RUN / 'result.json') == RESULT_SHA and state['passed'], 'Completed run differs')
    staging = DEST.with_name(DEST.name + '.staging')
    require(not staging.exists(), 'Preserve existing staging')
    staging.mkdir()

    def copy(source, name, digest=None):
        source = Path(source)
        target = staging / name
        require(source.is_file() and not source.is_symlink() and target.resolve().is_relative_to(staging.resolve())
            and not target.exists(), 'Invalid/duplicate artifact: ' + name)
        digest = digest or sha(source)
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, target)
        require(sha(source) == sha(target) == digest, 'Artifact changed during copy')

    for name, digest in {**state['frozenInputs'], **state['additionalInputs']}.items():
        copy(RUN / 'source' / name, 'source/' + name, digest)
    copy(RUN / 'result.json', 'run/result.json', RESULT_SHA)
    for row in state['steps']:
        copy(RUN / (row['id'] + '.log'), 'run/' + row['id'] + '.log', row['logSha256'])
    for path in (RUN / 'controls').iterdir():
        if path.is_file():
            copy(path, 'controls/' + path.name)
    for name in ('root-exit.json', 'record_exit.py', 'publish_evidence.py', 'previous-run_candidate.py', 'previous-supervise.py'):
        copy(WORK / name, 'controls/' + name)
    copy(WORK / 'dependency-feed-fixity.json', 'dependency-feed/fixity.json')
    copy(state['package']['path'], 'package/RizToolbox.Sdk.1.2.0.nupkg', state['package']['sha256'])
    copy(RUN / 'source/artifacts/sdk/sdk-manifest.json', 'package/sdk-manifest.json')
    copy(state['distributionProof']['path'], 'distribution/result.json', state['distributionProof']['sha256'])
    proof = RUN / 'source/artifacts/sdk-distribution-proof'
    for folder in ('generated', 'mods'):
        for path in (proof / folder).rglob('*'):
            if path.is_file() and (folder == 'mods' or path.suffix in ('.cs', '.csproj', '.json', '.dll', '.pdb', '.props', '.targets')):
                copy(path, 'distribution/' + folder + '/' + path.relative_to(proof / folder).as_posix())
    copy(proof / 'NuGet.config', 'distribution/NuGet.config')
    copy(RUN / 'source/NuGet.Config', 'controls/formal-host.NuGet.Config')
    for path in (RUN / 'source/tests/RizToolbox.Distribution.Tests/bin/Release/net8.0').iterdir():
        if path.is_file():
            copy(path, 'formal-host/' + path.name)
    for name in ('launch.json', 'supervision.json', 'controller-exit.json', 'controller.log', 'supervisor.log'):
        copy(RUN.parent / 'run-r1-supervision' / name, 'supervision/' + name)
    summary = dict(schema='sdk-startup-candidate-distribution-evidence/v1', passed=True, candidateOnly=True,
        compilerInputs=1172, distinctCompilerInputs=1172, additionalCompilerInputs=0, distributionCompilerInputsOverlap=2,
        checks=14, mainApplied=False, fullManagedMatrix=False, sdkGoalComplete=False, installed=False, gameStarted=False,
        dependencyPackageBytesIncluded=False, independentRebuildFromArchive=False,
        artifacts={p.relative_to(staging).as_posix(): dict(bytes=p.stat().st_size, sha256=sha(p))
            for p in sorted(staging.rglob('*')) if p.is_file()})
    (staging / 'validation-summary.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    check(staging)
    os.rename(staging, DEST)
    print('Published ' + str(DEST))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--check-only', type=Path)
    args = parser.parse_args()
    check(args.check_only) if args.check_only else publish()
