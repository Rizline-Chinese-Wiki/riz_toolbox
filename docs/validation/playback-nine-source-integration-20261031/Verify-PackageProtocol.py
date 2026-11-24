"""Check the current package reader against the actual helper ABI, without packing or installing."""
import argparse
import hashlib
import importlib.util
import json
import re
from pathlib import Path


def digest(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def fact(path):
    path = Path(path).resolve()
    return dict(path=str(path), sha256=digest(path), bytes=path.stat().st_size)


def run(output):
    output = Path(output).resolve()
    if output.exists():
        raise ValueError('Preserve existing evidence; choose a fresh output directory.')
    output.mkdir(parents=True)
    repo = Path(__file__).resolve().parents[3]
    native = Path('D:/riz-build-cache-next/foundation-private-native-r3-20261031')
    manifest_path = native / 'native-manifest.json'
    manifest = json.loads(manifest_path.read_text(encoding='utf-8-sig'))
    assert manifest['passed'] is True and manifest['buildExitCode'] == 0
    names = ('playbackAttestationVersion', 'playbackAttestationHeaderBytes',
             'playbackAttestationRangeBytes', 'playbackAttestationOriginalCount')
    assert tuple(manifest[name] for name in names) == (1, 120, 184, 28)
    validator_path = repo / 'scripts/verify-engine-transport-production-package.py'
    spec = importlib.util.spec_from_file_location('current_package_validator', validator_path)
    validator = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(validator)
    archive = Path(manifest['sourceArchive']['path'])
    assert digest(archive) == manifest['sourceArchive']['sha256']
    sources = validator.archived_sources(archive, manifest['sources'])
    script = sources['scripts/Build-JudgeContext.ps1'].decode('utf-8-sig')
    block = re.search(r'\$expectedExports\s*=\s*@\((.*?)\n\)', script, re.S)
    assert block is not None
    expected = re.findall(r"'([A-Za-z0-9_]+)'", block.group(1))
    assert expected == manifest['expectedExports'] and len(expected) == len(set(expected)) == 85
    helper = native / 'RizToolbox.JudgeContext.dll'
    actual = validator.Image(helper).exports(expected, 85)
    assert validator.PLAYBACK_ATTESTATION_EXPORTS.issubset(actual)
    assert validator.PLAYBACK_ATTESTATION_SOURCE_INPUTS.issubset(sources)
    helper_rows = [row for row in manifest['files'] if Path(row['path']).name == helper.name]
    assert len(helper_rows) == 1 and digest(helper) == helper_rows[0]['sha256']
    paths = [repo / 'scripts' / name for name in (
        'EngineTransportPackage.Common.ps1', 'Pack-EngineTransportProductionCandidate.ps1',
        'Test-EngineTransportCandidateInstall.ps1', 'verify-engine-transport-production-package.py')]
    for path in paths:
        text = path.read_text(encoding='utf-8-sig')
        assert all(name in text for name in names), path
    result = dict(kind='current-package-playback-protocol-check', passed=True,
                  inputs=[fact(path) for path in [Path(__file__), manifest_path, archive, helper] + paths],
                  helperExportCount=len(actual), actualExports=actual,
                  playbackAttestation=dict(zip(names, (1, 120, 184, 28))),
                  archivedCompilationSources=len(sources),
                  validationScope='Actual current PE/EAT and archived compilation inputs consumed by the current package reader; no complete package validation.',
                  candidatePacked=False, candidateInstalled=False, gameExecuted=False,
                  productionAdmissionVerified=False)
    (output / 'test-result.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print('Actual helper package protocol: 85 executable exports; private attestation 1/120/184/28. No package created.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', required=True)
    run(parser.parse_args().output)
