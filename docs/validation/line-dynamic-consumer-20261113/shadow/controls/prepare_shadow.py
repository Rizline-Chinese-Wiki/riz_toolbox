"""Prepare an isolated dynamic-consumer shadow after scene archive acceptance.

This writes controls in this work directory only. It does not build, test, or
create a D: run directory. The repaired candidate hash is supplied explicitly.
"""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import re

WORK = Path(__file__).resolve().parent
ROOT = WORK.parents[1]
CANDIDATE = ROOT / 'work/line-dynamic-consumer-next-20261113'
BASIS = ROOT / 'docs/validation/line-camera-scene-source-20261113'
SCANNER = ROOT / 'work/line-ring-source-validation-20261113/run_validation.py'
SCANNER_SHA = '61c2983ae117322b25ff96ad75b659b870fdadf9aada9274727248cd5ca98fd4'
SOURCE_TEST = 'tests/RizToolbox.Judgements.Tests/LineRingSource/NativeSeekLineRingSourceChecks.cs'
TARGETS = {
    'src/RizToolbox.Adapter/Gameplay/NativeSeekCanvasBasisCommands.LineRingDynamic.cs',
    'src/RizToolbox.Adapter/Gameplay/NativeSeekLineEndpointAccess.cs',
    'src/RizToolbox.Adapter/Gameplay/NativeSeekLineEndpointCommands.cs',
    'src/RizToolbox.Adapter/Gameplay/NativeSeekLineRingSourceCapture.Scene.cs',
    'src/RizToolbox.Adapter/Gameplay/NativeSeekLineRingDynamicConsumer.cs',
    'src/RizToolbox.Adapter/Judgements/NativeJudgementBackend.LineRingSource.Scene.cs',
    SOURCE_TEST,
    'tests/RizToolbox.Judgements.Tests/LineRingSource/NativeSeekLineRingSourceChecks.Dynamic.cs',
}
NEW_FILES = {
    'src/RizToolbox.Adapter/Gameplay/NativeSeekCanvasBasisCommands.LineRingDynamic.cs',
    'src/RizToolbox.Adapter/Gameplay/NativeSeekLineRingDynamicConsumer.cs',
    'tests/RizToolbox.Judgements.Tests/LineRingSource/NativeSeekLineRingSourceChecks.Dynamic.cs',
}
GENERATED = (
    'Directory.Build.props', 'NuGet.Config', 'Adapter.DynamicShadow.csproj',
    'Judgements.DynamicShadow.csproj', 'Program.cs',
    'root-review-receipt.json', 'prepared.json',
)


def require(ok, why):
    if not ok:
        raise RuntimeError(why)


def sha(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def digest(data):
    return hashlib.sha256(data).hexdigest()


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def scanner_module():
    require(sha(SCANNER) == SCANNER_SHA, 'Compiler-input scanner changed')
    spec = importlib.util.spec_from_file_location('dynamic_shadow_inputs', SCANNER)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def registry(path):
    body = Path(path).read_text(encoding='utf-8-sig')
    require(body.count('[] Cases =') == 1, 'Source registry declaration changed')
    block = body.split('[] Cases =', 1)[1].split('};', 1)[0]
    names = re.findall(r'\("([^"\r\n]+)",', block)
    require(len(names) == 30 and len(set(names)) == 30
            and block.count('("') == 30
            and all(name.startswith('Line ring source ') for name in names),
            'Expected 24 accepted and six dynamic source names')
    return names


def accepted_basis(expected_summary_sha):
    summary_path = BASIS / 'validation-summary.json'
    result_path = BASIS / 'run/result.json'
    graph_path = BASIS / 'run/inputs-after.json'
    require(BASIS.is_dir() and sha(summary_path) == expected_summary_sha,
            'Accepted scene archive or explicit summary hash differs')
    summary, result, graph = read(summary_path), read(result_path), read(graph_path)
    require(summary['schema'] == 'line-camera-scene-main-evidence/v1'
            and summary['acceptedThisBatch'] is True
            and summary['compilerInputs'] == 895
            and summary['defaultExpectedCases'] == 413
            and summary['sourceCases'] == 24 and summary['writerCases'] == 36
            and summary['naturalSteps'] == 72 and summary['focusedPassedCases'] == 149
            and summary['resultSha256'] == sha(result_path)
            and summary['inputGraphSha256'] == sha(graph_path)
            and result['schema'] == 'line-ring-scene-main-validation/v1'
            and result['passed'] is True
            and result['fullSource24ExecutedThisBatch'] is True
            and result['inputsUnchanged'] is True
            and result['controlsUnchanged'] is True
            and result['compiledBytesUnchanged'] is True
            and len(result['steps']) == 72
            and sum(len(row['passNames']) for row in result['steps']) == 149
            and len(graph) == 895,
            'Scene archive lacks accepted fresh 895-input evidence')
    pins = read(BASIS / 'artifact-manifest.json')
    for name, path in (('validation-summary.json', summary_path),
                       ('run/result.json', result_path),
                       ('run/inputs-after.json', graph_path)):
        require(pins.get(name) == sha(path), 'Archived basis manifest differs: ' + name)
    for name, key in (('run/controller-handle-exit.json', 'handleExitSha256'),
                      ('run/root-exit.json', 'rootExitSha256'),
                      ('run/publication-pin.json', 'publicationPinSha256')):
        require(sha(BASIS / name) == summary[key]
                and pins.get(name) == summary[key],
                'Accepted scene exit or publication pin differs: ' + name)
    handle = read(BASIS / 'run/controller-handle-exit.json')
    root_exit = read(BASIS / 'run/root-exit.json')
    pin = read(BASIS / 'run/publication-pin.json')
    require(handle['schema'] == 'original-controller-handle-exit/v1'
            and handle['actualExitCode'] == 0
            and handle['handleKeptUntilExit'] is True
            and handle['controllerPid'] == result['controllerPid']
            and handle['resultSha256AtExit'] == summary['resultSha256']
            and root_exit['observedBy'] == 'root'
            and root_exit['observedRootExec']['actualExitCode'] == 0
            and root_exit['resultSha256'] == summary['resultSha256']
            and pin['authoredBy'] == 'root'
            and pin['resultSha256'] == summary['resultSha256']
            and pin['handleExitSha256'] == summary['handleExitSha256']
            and pin['rootExitSha256'] == summary['rootExitSha256'],
            'Accepted scene original-controller or root pin chain differs')
    references = {}
    for component in ('RizToolbox.Sdk', 'RizToolbox.Core', 'RizToolbox.Runtime'):
        for ext in ('.dll', '.pdb'):
            name = 'binaries/production/' + component + ext
            path = BASIS / name
            value = result['productsBeforeTests'][component][ext]
            require(sha(path) == value and pins.get(name) == value,
                    'Accepted production reference differs: ' + name)
            references[str(path)] = value
    fixtures = result['nativeFixtures']
    require(len(fixtures) == 3, 'Accepted scene native fixture map differs')
    archive_fixtures = {}
    for key, row in fixtures.items():
        path = BASIS / 'fixtures/native' / key / Path(row['path']).name
        require(sha(path) == row['sha256'] and pins.get(path.relative_to(BASIS).as_posix()) == row['sha256'],
                'Accepted native fixture differs: ' + key)
        archive_fixtures[key] = {'path': str(path), 'sha256': row['sha256']}
    golden = BASIS / 'fixtures/line-ring-numeric-goldens.json'
    goldens_input = 'tests/RizToolbox.Judgements.Tests/LineRingNumeric/single-rounding-goldens.json'
    require(sha(golden) == graph[goldens_input]
            and pins.get(golden.relative_to(BASIS).as_posix()) == graph[goldens_input],
            'Accepted numeric golden differs')
    return summary, graph, references, archive_fixtures, golden


def candidate_snapshot(explicit_manifest_sha, before):
    manifest_path = CANDIDATE / 'candidate-manifest.json'
    require(sha(manifest_path) == explicit_manifest_sha,
            'Explicit candidate manifest SHA-256 differs')
    manifest = read(manifest_path)
    require(manifest['schema'] == 'line-ring-observed-dynamic-producer-consumer/v4'
            and manifest['mainApplied'] is False
            and manifest['built'] is False and manifest['testsExecuted'] is False
            and manifest['accepted'] is False
            and manifest['completeSeekPermitted'] is False
            and len(manifest['files']) == 8,
            'Expected repaired eight-file candidate manifest')
    rows = manifest['files']
    require({row['candidatePath'] for row in rows} == TARGETS
            and {row['targetPath'] for row in rows} == TARGETS
            and all(row['candidatePath'] == row['targetPath'] for row in rows),
            'Dynamic candidate target whitelist differs')
    actual = {path.relative_to(CANDIDATE).as_posix()
              for folder in ('src', 'tests') for path in (CANDIDATE / folder).rglob('*.cs')}
    require(actual == TARGETS, 'Candidate C# file inventory differs')
    hashes = {}
    after = dict(before)
    for row in rows:
        name = row['targetPath']
        path = CANDIDATE / name
        require(path.is_file() and not path.is_symlink()
                and row['observedMainBaseSha256'] == before.get(name)
                and (name in NEW_FILES) == (name not in before)
                and row['candidateSha256'] == sha(path),
                'Candidate base or payload differs: ' + name)
        hashes[name] = row['candidateSha256']
        after[name] = row['candidateSha256']
    require(len(after) == 898 and len(NEW_FILES - set(before)) == 3,
            'Shadow graph must add exactly three C# inputs')
    snapshot = digest(json.dumps(hashes, sort_keys=True, separators=(',', ':')).encode('utf-8'))
    names = registry(CANDIDATE / SOURCE_TEST)
    require(names[:24] == registry_accepted(before), 'Accepted 24 source names or order changed')
    return manifest, hashes, snapshot, after, names


def registry_accepted(before):
    require(before[SOURCE_TEST] == sha(ROOT / SOURCE_TEST), 'Main source registry differs')
    text = (ROOT / SOURCE_TEST).read_text(encoding='utf-8-sig')
    block = text.split('[] Cases =', 1)[1].split('};', 1)[0]
    names = re.findall(r'\("([^"\r\n]+)",', block)
    require(len(names) == 24 and len(set(names)) == 24, 'Accepted source registry count differs')
    return names


def review_receipt(path, explicit_sha, manifest_sha, hashes, snapshot, basis_sha):
    data = Path(path).read_bytes()
    require(digest(data) == explicit_sha, 'Explicit root review receipt SHA-256 differs')
    receipt = json.loads(data.decode('utf-8-sig'))
    require(receipt.get('reviewedBy') == 'root'
            and receipt.get('readyForShadow') is True
            and receipt.get('candidateManifestSha256') == manifest_sha
            and receipt.get('candidateFilesSha256') == hashes
            and receipt.get('candidateSnapshotSha256') == snapshot
            and receipt.get('basisSummarySha256') == basis_sha
            and receipt.get('expectedMainInputs') == 895
            and receipt.get('expectedShadowInputs') == 898
            and receipt.get('sourceCases') == 30,
            'Root receipt did not approve exact repaired candidate and basis')
    note = receipt.get('review')
    require(isinstance(note, dict) and isinstance(note.get('path'), str)
            and isinstance(note.get('sha256'), str), 'Independent candidate review missing')
    note_path = (ROOT / note['path']).resolve()
    require(note_path.is_relative_to(ROOT.resolve())
            and note_path.is_file() and sha(note_path) == note['sha256'],
            'Independent dynamic candidate review changed or escaped workspace')
    return data


def write_new(name, data):
    path = WORK / name
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open('xb') as stream:
        stream.write(data)


def project(name, folder, framework, selected, output=False, tests=False):
    relative = '../../' + folder
    excludes = [relative + '/bin/**', relative + '/obj/**']
    excludes += ['../../' + path for path in selected]
    if tests:
        excludes.append(relative + '/Program.cs')
    includes = [f'<Compile Include="../line-dynamic-consumer-next-20261113/{path}" />'
                for path in sorted(selected)]
    if tests:
        includes.append('<Compile Include="Program.cs" />')
    references = [
        f'<Reference Include="{component}"><HintPath>../../docs/validation/line-camera-scene-source-20261113/binaries/production/{component}.dll</HintPath></Reference>'
        for component in ('RizToolbox.Sdk', 'RizToolbox.Core', 'RizToolbox.Runtime')
    ]
    if tests:
        references.append('<ProjectReference Include="Adapter.DynamicShadow.csproj" />')
        references.append('<None Include="../../tests/RizToolbox.Judgements.Tests/LineRingNumeric/single-rounding-goldens.json" Link="line-ring-numeric-goldens.json" CopyToOutputDirectory="PreserveNewest" />')
    assembly = folder.rsplit('/', 1)[1]
    body = f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>{framework}</TargetFramework><AssemblyName>{assembly}</AssemblyName>{'<OutputType>Exe</OutputType>' if output else ''}<EnableDefaultCompileItems>false</EnableDefaultCompileItems><AllowUnsafeBlocks>true</AllowUnsafeBlocks></PropertyGroup>
  <ItemGroup>
    <Compile Include="{relative}/**/*.cs" Exclude="{';'.join(excludes)}" />
    {' '.join(includes)}
    {' '.join(references)}
  </ItemGroup>
</Project>
'''
    return body.encode('utf-8')


PROGRAM = '''using RizToolbox.Judgements.Tests.CanvasBasis;
using RizToolbox.Judgements.Tests.LineRingSource;
using RizToolbox.Sdk;
if (args.Length != 2 || !int.TryParse(args[1], out var index)) return 2;
var cases = args[0] == "source" ? NativeSeekLineRingSourceChecks.Cases
    : args[0] == "writer" ? NativeSeekLineRingSourceChecks.EndpointWriterCases
    : args[0] == "basis" ? NativeSeekCanvasBasisChecks.Cases
    : Array.Empty<(string Name, Action Run)>();
if (index < 1 || index > cases.Length) return 2;
var item = cases[index - 1]; Console.WriteLine("RUN " + item.Name);
try { item.Run(); }
catch (Exception error) { Console.Error.WriteLine("FAIL " + item.Name + ": " + error); return 1; }
Console.WriteLine("PASS " + item.Name); Console.WriteLine("Dynamic shadow checks: 1/1 passed."); return 0;
sealed class QuietLog : ILogger { public void Log(LogLevel level, string message, Exception? exception = null) { } }
'''


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--candidate-sha256', required=True)
    parser.add_argument('--basis-summary-sha256', required=True)
    parser.add_argument('--review-receipt', type=Path, required=True)
    parser.add_argument('--review-sha256', required=True)
    opt = parser.parse_args()
    require(all(re.fullmatch(r'[0-9a-f]{64}', value)
                for value in (opt.candidate_sha256, opt.basis_summary_sha256, opt.review_sha256)),
            'Explicit SHA-256 arguments are required')
    require(not any((WORK / name).exists() for name in GENERATED),
            'Preserve previous dynamic shadow preparation')
    summary, before, references, fixtures, golden = accepted_basis(opt.basis_summary_sha256)
    scanner = scanner_module()
    require(scanner.inputs() == before, 'Main is not the accepted 895-input scene graph')
    manifest, hashes, snapshot, after, names = candidate_snapshot(opt.candidate_sha256, before)
    receipt = review_receipt(opt.review_receipt, opt.review_sha256, opt.candidate_sha256,
                             hashes, snapshot, opt.basis_summary_sha256)
    adapter = {name for name in TARGETS if name.startswith('src/RizToolbox.Adapter/')}
    tests = TARGETS - adapter
    contents = {
        'Directory.Build.props': b'<Project><Import Project="../../Directory.Build.props" /></Project>\n',
        'NuGet.Config': b'<?xml version="1.0" encoding="utf-8"?><configuration><packageSources><clear /></packageSources></configuration>\n',
        'Adapter.DynamicShadow.csproj': project('Adapter.DynamicShadow', 'src/RizToolbox.Adapter', 'net6.0', adapter),
        'Judgements.DynamicShadow.csproj': project('Judgements.DynamicShadow', 'tests/RizToolbox.Judgements.Tests', 'net8.0', tests, True, True),
        'Program.cs': PROGRAM.encode('utf-8'),
        'root-review-receipt.json': receipt,
    }
    require(len(names) == 30 and len(after) == 898
            and scanner.inputs() == before
            and all(sha(CANDIDATE / name) == value for name, value in hashes.items())
            and all(sha(path) == value for path, value in references.items())
            and all(sha(row['path']) == row['sha256'] for row in fixtures.values())
            and sha(golden) == before['tests/RizToolbox.Judgements.Tests/LineRingNumeric/single-rounding-goldens.json'],
            'Source, reference, fixture or golden changed before preparation')
    for name, data in contents.items():
        write_new(name, data)
    prepared = {
        'schema': 'line-ring-dynamic-shadow-prepared/v1',
        'candidateManifestSha256': opt.candidate_sha256,
        'basisSummarySha256': opt.basis_summary_sha256,
        'basisResultSha256': summary['resultSha256'],
        'basisInputGraphSha256': summary['inputGraphSha256'],
        'reviewReceiptSha256': opt.review_sha256,
        'candidateFilesSha256': hashes,
        'candidateSnapshotSha256': snapshot,
        'mainInputs': 895, 'shadowInputs': 898,
        'sourceCases': 30, 'selectedSourceCases': list(range(25, 31)),
        'selectedSceneCases': list(range(20, 25)), 'selectedBasisCases': [1],
        'referenceFilesSha256': references,
        'nativeFixtures': fixtures,
        'goldenSha256': sha(golden),
        'generatedControlsSha256': {name: digest(data) for name, data in contents.items()},
        'mainApplied': False, 'built': False, 'testsExecuted': False,
        'sdkGoalComplete': False, 'completeSeekPermitted': False,
    }
    write_new('prepared.json', (json.dumps(prepared, ensure_ascii=False, indent=2) + '\n').encode('utf-8'))
    print(json.dumps({'prepared': True, 'candidateSnapshotSha256': snapshot,
                      'preparedSha256': sha(WORK / 'prepared.json'),
                      'mainInputs': 895, 'shadowInputs': 898,
                      'selectedChecks': 12, 'mainChanged': False}, indent=2))


if __name__ == '__main__':
    main()
