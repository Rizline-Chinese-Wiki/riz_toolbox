"""Collect the current increment only after every original SDK process exits.

This script does not launch tests, change product sources, or replace old results.
--check-ready is read-only; --collect additionally verifies all frozen artifacts.
"""
from pathlib import Path
import argparse
import datetime
import hashlib
import json
import shutil
import sys
import traceback

R = Path(__file__).resolve().parent
MAIN = Path('C:/Users/71957/Desktop/riz_toolbox')
SDK = R / 'formal-r2/test-result.json'
EXPECTED_MODES = {
    'player-sdk-normal', 'direct-source-first', 'direct-source-same',
    'direct-source-cross', 'direct-sdk-foreign', 'direct-sdk-worker',
    *('direct-sdk-' + kind + '-' + str(i)
      for kind in ('native', 'runtime') for i in range(6)),
}
EXPECTED_MANIFEST = '88fe4b79d5f2c320429a50d754c839706cd914615cab19842ccbe89b34b40333'
EXPECTED_INPUTS = {
    'RizToolbox.PlaybackStockFixture.dll': '604ebbddb5c7fb19974b1b329a1a5bc6f43d10e6fe6ebfd0e77b6e9e59e186a3',
    'RizToolbox.SingleReturn.Prototype.dll': '21d888694b361ef38a99558d0590511385c04b77bc490aa17f4163274e318a52',
    'RizToolbox.PlaybackSourcePrototype.dll': '75a41e3e715e7025386a75a993e369e8e22aef3e66eb5de9becd5899a0424490',
    'dobby.dll': '8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded',
}

def read(p):
    return json.loads(p.read_text(encoding='utf-8-sig'))

def sha(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()

def fact(p):
    return {'path': str(p), 'sha256': sha(p), 'bytes': p.stat().st_size}

def save(p, value):
    p.write_text(json.dumps(value, indent=2), encoding='utf-8')

def ready(sdk):
    rows = sdk.get('rows', [])
    names = [row['mode'] for row in rows]
    return (
        sdk.get('passed') is True and sdk.get('inputsUnchanged') is True
        and sdk.get('runnerPid') == 4104
        and sdk.get('startedAtUtc') == '2026-11-02T13:44:07.145028+00:00'
        and sdk.get('finishedAtUtc') is not None
        and len(rows) == len(EXPECTED_MODES) and set(names) == EXPECTED_MODES
        and not sdk.get('pending') and not sdk.get('livePids')
        and all(row.get('actualExitCode') == 0 and row.get('passed') is True
                and row.get('finishedAtUtc') is not None
                and row.get('actual', {}).get('passed') is True
                and row.get('actual', {}).get('mode') == row['mode']
                and row.get('actual', {}).get('checks', 0) > 0 for row in rows)
    )

def collect(sdk):
    assert ready(sdk), 'The original current SDK matrix is not complete.'
    out = R / 'integration-closure-r1'
    doc = MAIN / 'docs/validation/playback-direct-audio-20261102/stock-stop-integration-closure-r1'
    assert not out.exists() and not doc.exists(), 'Preserve existing closure attempts.'
    out.mkdir()
    verified = {}
    result = {
        'kind': 'applied-stock-stop-n3-current-increment-validation-closure',
        'passed': False,
        'observedAtUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
        'sourceFiles': 894, 'productInputs': 482, 'mainApplied': True,
        'completedScopes': [], 'formalSdkMatrixComplete': False,
        'currentIncrementValidationComplete': False,
        'gameExecuted': False, 'installed': False,
        'productionAdmission': False, 'fullBCompleted': False,
    }

    def verify(path, digest, length, origin):
        p = Path(path)
        assert p.is_absolute(), (origin, 'Expected an absolute artifact path', path)
        key = (str(p), digest)
        if key in verified:
            return
        assert p.is_file() and sha(p) == digest, (origin, str(p))
        if length is not None:
            assert p.stat().st_size == length, (origin, str(p), 'length')
        verified[key] = {'origin': origin, **fact(p)}

    def visit(value, origin):
        if isinstance(value, dict):
            if isinstance(value.get('path'), str) and isinstance(value.get('sha256'), str):
                # Some scope-local records deliberately use relative names. Their
                # containing scope's own strict review remains the authority.
                if Path(value['path']).is_absolute():
                    verify(value['path'], value['sha256'], value.get('bytes'), origin)
            for key, digest_key in [('image', 'imageSha256'), ('pdb', 'pdbSha256'), ('object', 'sha256')]:
                if isinstance(value.get(key), str) and isinstance(value.get(digest_key), str):
                    verify(value[key], value[digest_key], None, origin)
            for child in value.values():
                visit(child, origin)
        elif isinstance(value, list):
            for child in value:
                visit(child, origin)

    try:
        frozen_path = R / 'positive-r3/source-manifest.json'
        assert sha(frozen_path) == EXPECTED_MANIFEST
        candidate_path = R / 'candidate-review-r1/manifest.json'
        candidate = read(candidate_path)
        frozen = read(frozen_path)
        assert len(candidate['sourceFiles']) == len(frozen['files']) == 894
        identity = lambda rows: {row['path']: (row['sha256'], row['bytes']) for row in rows}
        assert identity(candidate['sourceFiles']) == identity(frozen['files'])
        for row in frozen['files']:
            verify(str(MAIN / row['path']), row['sha256'], row['bytes'], 'current-main')
            verify(str(R / 'positive-r3/source' / row['path']), row['sha256'], row['bytes'], 'SDK-frozen-source')
        assert sha(MAIN / 'README.md') == candidate['mainReadmeBefore']['sha256']
        assert candidate['productInputs'] == 482 and candidate['productCSharp'] == 475
        assert sdk['sourceManifest']['sha256'] == EXPECTED_MANIFEST
        assert {Path(row['path']).name: row['sha256'] for row in sdk['nativeInputs']} == EXPECTED_INPUTS
        assert sdk['products'] == read(R / 'positive-r3/build-products.json')
        assert sdk.get('processes') == 18
        assert sdk['checks'] == sum(row['actual']['checks'] for row in sdk['rows'])
        assert sdk.get('gameExecuted') is False and sdk.get('installed') is False
        assert sdk.get('productionAdmission') is False and sdk.get('fullBCompleted') is False
        visit(sdk, 'current SDK matrix')
        for row in sdk['rows']:
            mode = row['mode']
            assert str(R / 'positive-r3/source/tests/RizToolbox.PlaybackStartInitialization.Tests/bin/Release/net8.0/RizToolbox.Gameplay.Tests.dll') in row['command']
            assert row['command'][2] == mode
            assert read(R / 'formal-r2' / (mode + '.result.json')) == row
            assert int((R / 'formal-r2' / (mode + '.exit.txt')).read_text()) == 0
            actual = json.loads(Path(row['stdout']['path']).read_text(encoding='utf-8-sig').strip().splitlines()[-1])
            assert actual == row['actual']
        progress_path = R / 'integration-progress-r1/test-result.json'
        progress = read(progress_path)
        assert progress['passed'] is True and len(progress['completedScopes']) == 21
        scopes = []
        for scope in progress['completedScopes']:
            original = scope['original']
            verify(original['path'], original['sha256'], original['bytes'], 'previously completed scope')
            scopes.append(Path(original['path']))
        scopes.append(SDK)
        retained = out / 'retained'
        retained.mkdir()
        for i, path in enumerate(scopes):
            data = read(path)
            assert data['passed'] is True, str(path)
            visit(data, str(path))
            target = retained / (f'{i:02d}__' + path.parent.name + '__' + path.name)
            shutil.copy2(path, target)
            assert sha(target) == sha(path)
            result['completedScopes'].append({'original': fact(path), 'retained': fact(target), 'retainedFile': target.name})
        sdk_retained = retained / 'formal-sdk-originals'
        sdk_retained.mkdir()
        for row in sdk['rows']:
            mode = row['mode']
            for suffix in ('.stdout.log', '.stderr.log', '.launch.json', '.result.json', '.exit.txt'):
                original = R / 'formal-r2' / (mode + suffix)
                target = sdk_retained / original.name
                shutil.copy2(original, target)
                assert sha(original) == sha(target)
        result.update(
            exactCurrentMainSourceMatch=True, mainReadmeUnchanged=True,
            sourceManifest=fact(frozen_path), candidateManifest=fact(candidate_path),
            previousProgressIndex=fact(progress_path), currentSdkMatrix=fact(SDK),
            sdkProcesses=18, sdkChecks=sdk['checks'],
            formalSdkMatrixComplete=True, currentIncrementValidationComplete=True,
            completedArtifactIntegrityPassed=True, passed=True,
        )
    except BaseException:
        result['exception'] = traceback.format_exc()
    finally:
        save(out / 'verified-artifacts.json', list(verified.values()))
        result['verifiedArtifactCount'] = len(verified)
        result['verifiedArtifacts'] = fact(out / 'verified-artifacts.json')
        result['script'] = fact(Path(__file__))
        save(out / 'test-result.json', result)
    # A failed collection stays in its own attempt directory and never creates a
    # completed main-worktree index. It must be diagnosed before a fresh attempt.
    if result['passed']:
        shutil.copytree(out / 'retained', doc)
        shutil.copy2(out / 'verified-artifacts.json', doc / 'verified-artifacts.json')
        shutil.copy2(out / 'test-result.json', doc / 'scope-index.json')
        shutil.copy2(Path(__file__), doc / Path(__file__).name)
    print(json.dumps({'passed': result['passed'], 'scopes': len(result['completedScopes']),
                      'artifacts': len(verified), 'sdkChecks': result.get('sdkChecks'),
                      'exception': result.get('exception')}))
    return 0 if result['passed'] else 1

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument('--check-ready', action='store_true')
    group.add_argument('--collect', action='store_true')
    args = parser.parse_args()
    sdk = read(SDK)
    if args.check_ready:
        completed = [row for row in sdk['rows'] if row.get('actualExitCode') is not None]
        print(json.dumps({'ready': ready(sdk), 'expectedModes': len(EXPECTED_MODES),
                          'actualCompleted': len(completed),
                          'actualFailed': [row['mode'] for row in completed if row.get('passed') is not True],
                          'livePids': sdk.get('livePids'), 'pending': sdk.get('pending')}))
        return 0 if ready(sdk) else 3
    return collect(sdk)

if __name__ == '__main__':
    sys.exit(main())
