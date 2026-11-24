"""Freeze the standalone SDK distribution scope; never certify the full SDK."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil

ROOT = Path(__file__).resolve().parents[2]
WORK = Path(__file__).resolve().parent
RUN = Path('D:/riz-build-cache-next/sdk-current-hold-20261114/run-r3')
DEST = ROOT / 'docs/validation/sdk-current-hold-20261114'
sha = lambda path: hashlib.sha256(Path(path).read_bytes()).hexdigest()
read = lambda path: json.loads(Path(path).read_text(encoding='utf-8-sig'))


def require(ok, reason):
    if not ok: raise RuntimeError(reason)


def local(base, name):
    raw = base / name
    for part in (raw, *raw.parents):
        if part == base: break
        require(not part.is_symlink() and not getattr(part.lstat(), 'st_file_attributes', 0) & 0x400, 'Reparse archive path')
    path = raw.resolve(); require(path.is_relative_to(base) and path.is_file(), 'Artifact escaped archive'); return path


def check(base):
    base = base.resolve(); summary = read(local(base, 'validation-summary.json'))
    require(summary['schema'] == 'sdk-candidate-distribution-evidence/v1' and summary['passed']
        and summary['candidateOnly'] and not summary['mainApplied'] and not summary['fullManagedMatrix']
        and not summary['sdkGoalComplete'], 'SDK archive scope differs')
    for name, row in summary['artifacts'].items():
        path = local(base, name); require(sha(path) == row['sha256'] and path.stat().st_size == row['bytes'], 'Artifact differs: ' + name)
    require({path.relative_to(base).as_posix() for path in base.rglob('*') if path.is_file()}
        == set(summary['artifacts']) | {'validation-summary.json'}, 'Inventory differs')
    state = read(local(base, 'run/result.json')); witness = read(local(base, 'controls/run-r3-root-exit.json'))
    require(state['passed'] and state['checksPassed'] == 14 and state['frozenInputsUnchanged'] and state['compilerInputs'] == 939
        and state['additionalCompilerInputs'] == 2 and len(state['frozenInputs']) == 939, 'Current input scope differs')
    require(witness['actualToolExitCode'] == 0 and witness['controllerPid'] == state['controllerPid']
        and witness['resultSha256'] == sha(local(base, 'run/result.json')), 'Original successful root exit differs')
    require([row['id'] for row in state['steps']] == ['formal-host-build', 'sdk-distribution'], 'Step scope differs')
    for row in state['steps']:
        require(row['naturalExit'] and row['exitCode'] == 0 and not row['failNames'], 'Natural step failed')
        require(sha(local(base, 'run/' + row['id'] + '.log')) == row['logSha256'], 'Step log differs')
    require(len(state['steps'][1]['passNames']) == len(set(state['steps'][1]['passNames'])) == 14, 'Formal plugin checks differ')
    for name, digest in {**state['frozenInputs'], **state['additionalInputs']}.items():
        require(sha(local(base, 'source/' + name)) == digest, 'Frozen SDK input differs')
    require(sha(local(base, 'controls/Test-SdkDistribution.Offline.ps1')) == state['offlineRunnerSha256'], 'Offline runner differs')
    require(sha(local(base, 'package/RizToolbox.Sdk.1.2.0.nupkg')) == state['package']['sha256'], 'SDK reference package differs')
    for name, row in state['dependencies'].items(): require(sha(local(base, 'dependency-feed/' + name)) == row['sha256'], 'Cached dependency differs')
    proof = read(local(base, 'distribution/result.json'))
    require(proof['checksPassed'] == 14 and not proof['runtimeGameVerification'] and proof['packageSha256'] == state['package']['sha256'], 'Distribution proof differs')
    require(not state['fullManagedMatrix'] and not state['sdkGoalComplete'] and not state['runtimeGameVerification']
        and not state['installed'] and not state['gameStarted'], 'Current SDK result overclaims scope')
    print(json.dumps(dict(passed=True, archiveOnly=True, candidateOnly=True, checks=14, files=len(summary['artifacts']) + 1,
        summarySha256=sha(base / 'validation-summary.json'))))


def publish():
    require(not DEST.exists(), 'Preserve evidence archives'); state = read(RUN / 'result.json')
    witness = read(WORK / 'run-r3-root-exit.json')
    require(state['passed'] and witness['actualToolExitCode'] == 0 and witness['resultSha256'] == sha(RUN / 'result.json'), 'SDK run not successful')
    staging = DEST.with_name(DEST.name + '.staging'); require(not staging.exists(), 'Preserve staging'); staging.mkdir()

    def copy(source, name, digest=None):
        source = Path(source); target = staging / name
        require(source.is_file() and not source.is_symlink() and target.resolve().is_relative_to(staging.resolve())
            and not target.exists(), 'Invalid/duplicate evidence')
        digest = digest or sha(source); target.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(source, target)
        require(sha(source) == sha(target) == digest, 'Artifact changed during copy')

    for name, digest in {**state['frozenInputs'], **state['additionalInputs']}.items(): copy(RUN / 'source' / name, 'source/' + name, digest)
    copy(RUN / 'result.json', 'run/result.json')
    for row in state['steps']: copy(RUN / (row['id'] + '.log'), 'run/' + row['id'] + '.log', row['logSha256'])
    for path in (RUN / 'controls').iterdir():
        if path.is_file(): copy(path, 'controls/' + path.name)
    for name in ('run-r3-root-exit.json', 'publish_evidence.py', 'source-review.md'): copy(WORK / name, 'controls/' + name)
    copy(state['package']['path'], 'package/RizToolbox.Sdk.1.2.0.nupkg', state['package']['sha256'])
    copy(RUN / 'source/artifacts/sdk/sdk-manifest.json', 'package/sdk-manifest.json')
    copy(state['distributionProof']['path'], 'distribution/result.json', state['distributionProof']['sha256'])
    proof = RUN / 'source/artifacts/sdk-distribution-proof'
    for path in (proof / 'generated').rglob('*'):
        if path.is_file() and (path.suffix in ('.cs', '.csproj', '.json', '.dll', '.pdb', '.props', '.targets')):
            copy(path, 'distribution/generated/' + path.relative_to(proof / 'generated').as_posix())
    for path in (proof / 'mods').rglob('*'):
        if path.is_file(): copy(path, 'distribution/mods/' + path.relative_to(proof / 'mods').as_posix())
    copy(proof / 'NuGet.config', 'distribution/NuGet.config')
    host = RUN / 'source/tests/RizToolbox.Distribution.Tests/bin/Release/net8.0'
    for path in host.iterdir():
        if path.is_file(): copy(path, 'formal-host/' + path.name)
    for name, row in state['dependencies'].items(): copy(RUN / 'dependency-feed' / name, 'dependency-feed/' + name, row['sha256'])
    for label in ('run-r1', 'run-r2'):
        prior = RUN.parent / label; old = read(prior / 'result.json')
        require(old['frozenInputs'] == state['frozenInputs'] and not old['passed'], 'Failed staging history differs')
        copy(prior / 'result.json', 'history/' + label + '/result.json')
        for row in old['steps']: copy(prior / (row['id'] + '.log'), 'history/' + label + '/' + row['id'] + '.log', row['logSha256'])
        for path in (prior / 'controls').iterdir():
            if path.is_file(): copy(path, 'history/' + label + '/controls/' + path.name)
        copy(WORK / (label + '-root-exit.json'), 'history/' + label + '/root-exit.json')
    summary = dict(schema='sdk-candidate-distribution-evidence/v1', passed=True, candidateOnly=True, compilerInputs=939,
        additionalCompilerInputs=2, checks=14, mainApplied=False, fullManagedMatrix=False, sdkGoalComplete=False,
        installed=False, gameStarted=False, artifacts={path.relative_to(staging).as_posix(): dict(bytes=path.stat().st_size,
            sha256=sha(path)) for path in sorted(staging.rglob('*')) if path.is_file()})
    (staging / 'validation-summary.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    check(staging); os.rename(staging, DEST); print('Published ' + str(DEST))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(); parser.add_argument('--check-only', type=Path); args = parser.parse_args()
    check(args.check_only) if args.check_only else publish()
