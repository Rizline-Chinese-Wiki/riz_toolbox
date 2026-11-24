"""Read the captured final C-workspace six build and emit raw Csc/PDB metadata.

Run only after the 881 main-r1 controller passed. This does not build or test.
It freezes actual Csc sources under metadata/graph while retaining their true C
paths in compiler argv and source records.
"""
import argparse
import hashlib
import json
import os
import re
import shutil
import xml.etree.ElementTree as ET
from pathlib import Path

from ManagedMetadataReader import portable, codeview

ROOT = Path('C:/Users/71957/Desktop/riz_toolbox').resolve()
SCOPE = Path('D:/riz-build-cache-next/line-ring-endpoint-integration-20261113/main-r1').resolve()
ARTIFACTS = SCOPE / 'artifacts'
CAPTURE = SCOPE / 'csc-inputs'
OUTPUT = SCOPE / 'metadata/graph'
PRODUCTS = {f'RizToolbox.{part}' for part in ('Sdk', 'Core', 'Runtime', 'Adapter', 'Hosting', 'Loader')}
PDB_SHA256 = '8829d00f-11b8-4213-878b-770e8597ac16'


def require(value, message):
    if not value:
        raise ValueError(message)


def fact(path):
    path = Path(path).resolve()
    raw = path.read_bytes()
    return {'path': str(path), 'bytes': len(raw), 'sha256': hashlib.sha256(raw).hexdigest()}


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def key(path):
    return os.path.normcase(str(Path(path).resolve()))


def within(path, root):
    return Path(root).resolve() in Path(path).resolve().parents


def captured(folder, group):
    entries = {}
    path = folder / (group + '.sha256')
    for line in path.read_text(encoding='utf-8-sig').splitlines():
        if not line.strip():
            continue
        value, digest = line.rsplit('|', 1)
        actual = fact(value)
        require(actual['sha256'] == digest.lower(), 'Captured ' + group + ' changed: ' + value)
        identity = key(value)
        require(identity not in entries, 'Duplicate captured input: ' + value)
        entries[identity] = actual
    return entries


def properties(folder):
    result = {}
    for line in (folder / 'properties.txt').read_text(encoding='utf-8-sig').splitlines():
        if '|' in line:
            name, value = line.split('|', 1)
            require(name not in result, 'Duplicate captured property.')
            result[name] = value
    return result


def argv(folder, project, groups, framework):
    path = folder / 'csc-command-line.txt'
    args = path.read_text(encoding='utf-8-sig').splitlines()
    require(args, 'Actual Csc argv missing: ' + str(project))
    options = {'/reference:': 'references', '/analyzer:': 'analyzers',
               '/additionalfile:': 'additional', '/analyzerconfig:': 'editorconfigs'}
    observed = {name: [] for name in ('compile', *options.values())}
    outputs = []
    for raw in args:
        arg = raw.strip().strip('"')
        require(not arg.startswith('@'), 'Unexpanded Csc response file.')
        if not arg.startswith('/') and arg.lower().endswith('.cs'):
            path_value = Path(arg) if Path(arg).is_absolute() else project.parent / arg
            observed['compile'].append(key(path_value))
        for prefix, name in options.items():
            if arg.lower().startswith(prefix):
                observed[name].append(key(arg[len(prefix):].strip('"')))
        if arg.lower().startswith('/out:'):
            path_value = Path(arg[5:].strip('"'))
            outputs.append(path_value if path_value.is_absolute() else project.parent / path_value)
    flags = {arg.strip().strip('"').lower() for arg in args}
    require({'/debug:portable', '/checksumalgorithm:sha256', '/deterministic+', '/optimize+'} <= flags,
            'Actual Csc flags differ.')
    require(len(outputs) == 1 and within(outputs[0], ARTIFACTS / 'obj'), 'Actual Csc output escaped obj.')
    late = set(observed['editorconfigs']) - set(groups['editorconfigs'])
    sdk_default = Path('C:/Program Files/dotnet/sdk/8.0.418/Sdks/Microsoft.NET.Sdk/analyzers/build/config') / (
        'analysislevel_' + framework[3:].split('.')[0] + '_default.globalconfig')
    require(late == {key(sdk_default)}, 'Only the exact SDK late editorconfig may supplement capture.')
    late_fact = fact(sdk_default)
    groups['editorconfigs'][key(sdk_default)] = late_fact
    for name, values in observed.items():
        require(len(values) == len(set(values)) and set(values) == set(groups[name]),
                'Actual Csc argv differs from captured ' + name + '.')
    return {'file': fact(path), 'argumentCount': len(args),
            'inputCounts': {name: len(values) for name, values in observed.items()},
            'actualLateSdkEditorconfigs': [late_fact],
            'requiredFlagsObserved': ['/debug:portable', '/checksumalgorithm:SHA256', '/deterministic+', '/optimize+'],
            'objDll': str(outputs[0].resolve())}


def document_path(value):
    normalized = value.replace('\\', '/')
    prefix = '/_/riz_toolbox/'
    return ROOT / normalized[len(prefix):] if normalized.startswith(prefix) else Path(value)


def project_row(name):
    folder = CAPTURE / name
    project = ROOT / 'src' / name / (name + '.csproj')
    props = properties(folder)
    require(key(props['Project']) == key(project) and props['AssemblyName'] == name
            and props['TargetFramework'] == 'net6.0' and props['NETCoreSdkVersion'] == '8.0.418'
            and props['UseSharedCompilation'] == 'false' and props['DebugType'] == 'portable'
            and props['ChecksumAlgorithm'] == 'SHA256' and props['BuildProjectReferences'] == 'true'
            and props['RizCompilationStage'] == 'graph'
            and props['RizEngineProductionProfileSource'] == props['RizEngineTransportProfileSource'] == '',
            'Captured canonical project/toolchain properties differ: ' + name)
    groups = {group: captured(folder, group) for group in
              ('compile', 'references', 'resolved-references', 'analyzers',
               'additional', 'editorconfigs', 'imports')}
    args = argv(folder, project, groups, props['TargetFramework'])
    authored_paths = {key(path) for path in project.parent.rglob('*.cs')
                      if not {'bin', 'obj'}.intersection(path.relative_to(project.parent).parts)}
    require(authored_paths <= set(groups['compile']) and len(set(groups['compile']) - authored_paths) == 3,
            'Actual compiler sources differ from exact C authored tree: ' + name)
    frozen = []
    for ordinal, identity in enumerate(sorted(groups['compile'])):
        source = groups['compile'][identity]
        generated = identity not in authored_paths
        require(within(source['path'], ARTIFACTS / 'obj') if generated else within(source['path'], project.parent),
                'Compiler source escaped allowed root.')
        target = OUTPUT / 'frozen-csc-source' / name / f'{ordinal:04}.cs'
        target.parent.mkdir(parents=True, exist_ok=True)
        require(not target.exists(), 'Frozen source output already exists.')
        shutil.copyfile(source['path'], target)
        copied = fact(target)
        require((copied['bytes'], copied['sha256']) == (source['bytes'], source['sha256']),
                'Frozen Csc source copy differs.')
        frozen.append({**source, 'generated': generated, 'frozen': copied})
    dll = Path(props['TargetPath']).resolve()
    obj = Path(args['objDll']).resolve()
    pdb = dll.with_suffix('.pdb')
    obj_pdb = obj.with_suffix('.pdb')
    require(within(dll, ARTIFACTS / 'bin') and dll.name == name + '.dll'
            and fact(dll)['sha256'] == fact(obj)['sha256']
            and fact(pdb)['sha256'] == fact(obj_pdb)['sha256'],
            'Final obj/bin PE or PDB bytes differ: ' + name)
    raw_pdb, cv = portable(pdb), codeview(dll)
    require(cv['guidBytes'] == raw_pdb['id'][:32]
            and cv['stamp'] == int.from_bytes(bytes.fromhex(raw_pdb['id'][32:]), 'little')
            and cv['age'] == 1 and key(cv['path']) == key(obj_pdb),
            'Portable PDB and PE CodeView identity differ: ' + name)
    source_index = {key(row['path']): row for row in frozen}
    documents, seen = [], set()
    for entry in raw_pdb['documents']:
        actual = document_path(entry['document'])
        identity = key(actual)
        require(identity in source_index and identity not in seen
                and entry['hashAlgorithm'] == PDB_SHA256
                and entry['sha256'] == source_index[identity]['sha256'],
                'Raw PDB document differs from actual Csc source: ' + name)
        seen.add(identity)
        documents.append({**entry, 'actualCompilePath': str(actual.resolve()),
                          'generated': source_index[identity]['generated'],
                          'frozen': source_index[identity]['frozen']})
    require(seen == set(groups['compile']), 'Raw PDB document set is incomplete: ' + name)
    for group in ('references', 'resolved-references'):
        for row in groups[group].values():
            if Path(row['path']).stem in PRODUCTS:
                require(within(row['path'], ARTIFACTS), 'Historical product reference detected.')
    return {'projectName': name, 'project': fact(project), 'assemblyName': name,
            'targetFramework': props['TargetFramework'], 'properties': props,
            'dll': fact(dll), 'objDll': fact(obj), 'pdb': fact(pdb), 'objPdb': fact(obj_pdb),
            'portablePdbIdHex': raw_pdb['id'], 'codeView': cv,
            'authoredSources': len(authored_paths), 'generatedSources': 3,
            'compilerSourceCount': len(frozen), 'pdbDocumentCount': len(documents),
            'sources': frozen, 'documents': documents,
            'dependencies': {group: list(rows.values()) for group, rows in groups.items() if group != 'compile'},
            'actualCscArguments': args, 'exactCompleteSourceSet': True,
            'captureFiles': [fact(path) for path in sorted(folder.iterdir()) if path.is_file()]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--focused-result', required=True, help='Final main-r1/result.json')
    args = parser.parse_args()
    focused_path = Path(args.focused_result).resolve()
    require(focused_path == SCOPE / 'result.json' and not OUTPUT.exists(),
            'Use only the final 881 controller result and a fresh metadata scope.')
    focused = read(focused_path)
    steps = {row['name']: row for row in focused['steps']}
    require(focused['passed'] is True and focused['compilerInputs'] == 881
            and focused['inputsUnchanged'] is True and focused['controlsUnchanged'] is True
            and focused['compiledBytesUnchanged'] is True
            and focused['actualCscCaptureUnchanged'] is True
            and steps['six-release']['naturalExit'] is True
            and steps['six-release']['exitCode'] == 0 and steps['six-release']['matchesExpected'] is True
            and focused['productsAfterSix'] == focused['productsBefore'] == focused['productsAfter'],
            'Captured six build is not the exact accepted final product byte set.')
    require(CAPTURE.is_dir() and {p.name for p in CAPTURE.iterdir() if p.is_dir()} == PRODUCTS,
            'Actual six-product Csc capture directories differ.')
    actual_capture = {str(path): fact(path)['sha256'] for path in CAPTURE.rglob('*') if path.is_file()}
    require(actual_capture == focused['actualCscCaptureFiles'],
            'Actual Csc capture file set/hash differs from the accepted controller.')
    build_log = SCOPE / 'six-release.log'
    log = build_log.read_text(encoding='utf-8-sig', errors='replace')
    english = ('Build succeeded.' in log
               and re.findall(r'^\s*(\d+) Warning\(s\)\s*$', log, re.M) == ['0']
               and re.findall(r'^\s*(\d+) Error\(s\)\s*$', log, re.M) == ['0'])
    chinese = ('已成功生成。' in log
               and re.findall(r'^\s*(\d+)\s*个警告\s*$', log, re.M) == ['0']
               and re.findall(r'^\s*(\d+)\s*个错误\s*$', log, re.M) == ['0'])
    require(fact(build_log)['sha256'] == steps['six-release']['logSha256']
            and (english or chinese),
            'Actual captured six build did not show a single accepted W0/E0 summary.')
    OUTPUT.mkdir(parents=True)
    projects = [project_row(name) for name in sorted(PRODUCTS)]
    edges, copies = [], []
    by_name = {row['projectName']: row for row in projects}
    for row in projects:
        consumer = row['projectName']
        owner_names = set()
        for node in ET.parse(row['project']['path']).getroot().iter('ProjectReference'):
            owner = (Path(row['project']['path']).parent / node.attrib['Include'].replace('\\', '/')).resolve().stem
            require(owner in PRODUCTS and owner != consumer and owner not in owner_names,
                    'Project reference graph differs.')
            owner_names.add(owner)
            reference = [item for item in row['dependencies']['references'] if Path(item['path']).stem == owner]
            require(len(reference) == 1, 'Fresh producer Csc reference differs.')
            refint = ARTIFACTS / 'obj' / owner / 'release/refint' / (owner + '.dll')
            require(within(reference[0]['path'], ARTIFACTS / 'obj')
                    and fact(refint)['sha256'] == reference[0]['sha256'],
                    'Fresh reference/refint identity differs.')
            producer = by_name[owner]
            edges.append({'consumer': consumer, 'producer': owner, 'reference': reference[0],
                          'newProducer': producer['dll'], 'refint': fact(refint)})
            for field in ('dll', 'pdb'):
                artifact = producer[field]
                copy = ARTIFACTS / 'bin' / consumer / 'release' / Path(artifact['path']).name
                copy_fact = fact(copy)
                require(copy_fact['sha256'] == artifact['sha256'], 'Copy-local PE/PDB differs.')
                copies.append({'consumer': consumer, 'producer': owner,
                               'copied': copy_fact, 'newProducer': artifact})
    totals = {'projects': 6, 'authored': sum(p['authoredSources'] for p in projects),
              'generated': sum(p['generatedSources'] for p in projects),
              'pdbDocuments': sum(p['pdbDocumentCount'] for p in projects),
              'freshProductReferenceEdges': len(edges), 'copyLocalPePdbFiles': len(copies)}
    require(500 <= totals['authored'] <= 700 and totals['generated'] == 18
            and totals['pdbDocuments'] == totals['authored'] + 18
            and len(edges) == 11 and len(copies) == 22,
            'Bounded final six project totals differ.')
    result = {'kind': 'delivery-final-six-raw-metadata-v1', 'passed': True,
              'sourceRoot': str(ROOT), 'artifactRoot': str(ARTIFACTS),
              'captureRoot': str(CAPTURE), 'metadataOnly': True,
              'actualCaptureFileCount': len(actual_capture),
              'allCscFrozenPdbCompleteAndSha256Equal': True,
              'historicalProductReferences': 0, 'totals': totals,
              'warnings': 0, 'errors': 0, 'projects': projects,
              'freshProductEdges': edges, 'copyLocalPePdbFiles': copies,
              'actualBuildProcess': fact(focused_path),
              'casesRun': 0, 'nativeCalls': 0, 'nativeDllsLoaded': 0, 'assembliesExecuted': 0,
              'runtimeAdmission': False, 'productionAdmission': False,
              'metadataReaderScript': fact(__file__)}
    output = OUTPUT / 'result.json'
    require(not output.exists(), 'Preserve existing metadata receipt.')
    with output.open('xb') as stream:
        stream.write((json.dumps(result, ensure_ascii=False, indent=2) + '\n').encode('utf-8'))
        stream.flush(); os.fsync(stream.fileno())
    print(json.dumps({'passed': True, 'graphMetadata': fact(output), 'totals': totals}))


if __name__ == '__main__':
    main()
