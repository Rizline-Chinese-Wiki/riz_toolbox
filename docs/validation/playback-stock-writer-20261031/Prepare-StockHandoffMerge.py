"""Prepare a byte-checked three-way S6 merge while retaining the integrated S5."""
import difflib
import hashlib
import json
import subprocess
import zipfile
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
CACHE = Path('D:/riz-build-cache-next')
S5 = CACHE / 'playback-stock-provider-s5-r1-20261031/handoff-r1'
S6 = CACHE / 'playback-stock-handoff-s6-r1-20261031/snapshot-r2'
OUT = CACHE / 'playback-stock-handoff-main-integration-r2-20261031'
ROUTER = 'src/RizToolbox.Adapter/Gameplay/NativePlaybackStockClockRouter.cs'


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def digest(data):
    return hashlib.sha256(data).hexdigest()


def row(path):
    path = Path(path).resolve()
    data = path.read_bytes()
    return dict(path=str(path), sha256=digest(data), bytes=len(data))


def write(path, value):
    Path(path).write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def unzip(path):
    with zipfile.ZipFile(path) as archive:
        return {item.filename: archive.read(item) for item in archive.infolist() if not item.is_dir()}


def save_tree(root, data):
    for name, body in data.items():
        path = (root / name).resolve()
        assert path.is_relative_to(root), name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(body)


def archive(path, data):
    with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED) as target:
        for name, body in sorted(data.items()):
            target.writestr(name, body)


def call(command, cwd, label):
    write(OUT / (label + '.command.json'), dict(argv=command, cwd=str(cwd)))
    completed = subprocess.run(command, cwd=cwd, capture_output=True)
    (OUT / (label + '.stdout.log')).write_bytes(completed.stdout)
    (OUT / (label + '.stderr.log')).write_bytes(completed.stderr)
    (OUT / (label + '.exit.txt')).write_text(str(completed.returncode), encoding='utf-8')
    return completed


def run():
    assert not OUT.exists(), 'Preserve earlier integrations.'
    s6 = read(S6 / 'test-result.json')
    assert s6['passed'] and s6['sourceFiles'] == 772 and s6['changedFiles'] == 23
    assert s6['sharedBridgeCorrectionExcluded'] and s6['s5ModuleReversionExcluded']
    for key in ('sourceArchive', 'beforeArchive', 'patch', 'changes'):
        assert row(s6[key]['path'])['sha256'] == s6[key]['sha256']
    ours = unzip(S5 / 'implemented-source-snapshot.zip')
    base = unzip(s6['beforeArchive']['path'])
    theirs = unzip(s6['sourceArchive']['path'])
    assert len(ours) == 762 and len(base) == 760 and len(theirs) == 772
    for item in s6['sourceFilesManifest']:
        assert digest(theirs[item['relativePath']]) == item['sha256']
    for item in read(S5 / 'source-manifest.json'):
        assert digest(ours[item['path']]) == item['sha256']
    before = dict(base)
    before.update(ours)
    assert len(before) == 766
    for name, body in before.items():
        assert (REPO / name).read_bytes() == body, name
    for name in set(theirs) - set(base):
        assert name not in before and not (REPO / name).exists(), name
    overlaps = [name for name in theirs if name in base and name in ours
                and theirs[name] != base[name] and ours[name] != base[name]]
    assert overlaps == [ROUTER], overlaps
    OUT.mkdir()
    merged = dict(before)
    for name, body in theirs.items():
        if name not in base or body != base[name]:
            merged[name] = body
    inputs = OUT / 'router-three-way'
    inputs.mkdir()
    normalization = []
    for name, body in (('ours.cs', ours[ROUTER]), ('base.cs', base[ROUTER]), ('theirs.cs', theirs[ROUTER])):
        # The S4 archive uses LF while both implemented descendants use CRLF.
        # Normalize only temporary merge inputs; preserve exact archive bytes.
        normalized = body.replace(b'\r\n', b'\n').replace(b'\n', b'\r\n')
        assert normalized.replace(b'\r\n', b'\n') == body.replace(b'\r\n', b'\n')
        (inputs / name).write_bytes(normalized)
        normalization.append(dict(name=name, originalSha256=digest(body),
                                  mergeInputSha256=digest(normalized), onlyLineEndingsChanged=True))
    write(OUT / 'router-input-normalization.json', normalization)
    result = call(['git', 'merge-file', '-p', str(inputs / 'ours.cs'), str(inputs / 'base.cs'),
                   str(inputs / 'theirs.cs')], OUT, 'router-merge')
    assert result.returncode == 0, 'Review the preserved exact three-way conflict before proceeding.'
    merged[ROUTER] = result.stdout
    assert len(merged) == 778
    # The only composition point is this private Router; all other S6 writes
    # equal its frozen bytes, and every non-overlapping S5 write stays intact.
    for name, body in ours.items():
        if name != ROUTER and (name not in base or name not in theirs or theirs[name] == base[name]):
            assert merged[name] == body, name
    for name, body in theirs.items():
        if name != ROUTER and (name not in base or body != base[name]):
            assert merged[name] == body, name
    assert digest(merged['src/RizToolbox.Runtime/Hooks/NativePlaybackSingleReturnBridge.cs']) == \
        '79e4947c066ad91ae56cba29db50cf61f8d091777d4456569a2f2afad3450f07'
    changes = []
    patch = []
    for name, body in sorted(merged.items()):
        old = before.get(name)
        if old == body:
            continue
        patch.append('diff --git a/' + name + ' b/' + name + '\n')
        if old is None:
            patch.append('new file mode 100644\n')
        patch.extend(difflib.unified_diff((old or b'').decode().splitlines(keepends=True),
                                         body.decode().splitlines(keepends=True),
                                         fromfile='a/' + name if old is not None else '/dev/null',
                                         tofile='b/' + name))
        changes.append(dict(relativePath=name, beforeSha256=digest(old) if old is not None else None,
                            afterSha256=digest(body)))
    assert len(changes) == 23
    patch_path = OUT / 'combined-s6.patch'
    patch_path.write_bytes(''.join(patch).encode())
    archive(OUT / 'before-source.zip', before)
    archive(OUT / 'combined-source.zip', merged)
    source = OUT / 'source'
    source.mkdir()
    save_tree(source, merged)
    review = OUT / 'patch-review'
    review.mkdir()
    save_tree(review, before)
    for mode, command in (('check', ['git', 'apply', '--check', '--whitespace=nowarn', str(patch_path)]),
                          ('apply', ['git', 'apply', '--whitespace=nowarn', str(patch_path)])):
        completed = call(command, review, 'copy-' + mode)
        assert completed.returncode == 0, completed.stderr.decode(errors='replace')
    for name, body in merged.items():
        assert (review / name).read_bytes() == body, name
    write(OUT / 'source-manifest.json', [dict(relativePath=name, sha256=digest(body), bytes=len(body))
                                       for name, body in sorted(merged.items())])
    write(OUT / 'changed-files.json', changes)
    write(OUT / 'prepared-result.json', dict(kind='s5-s6-three-way-source-merge', passed=True,
          sourceFiles=778, changedFiles=23, overlappingFiles=overlaps, beforeArchive=row(OUT / 'before-source.zip'),
          sourceArchive=row(OUT / 'combined-source.zip'), patch=row(patch_path),
          s5Proof=row(S5 / 'test-result.json'), s6Snapshot=row(S6 / 'test-result.json'),
          currentMainModified=False, copiedActualApplyCheck=0, copiedActualApply=0,
          integratedSourceExecutionProven=False, gameExecuted=False, installed=False))
    print(json.dumps(dict(passed=True, sourceFiles=778, changedFiles=23,
                          result=row(OUT / 'prepared-result.json'))))


if __name__ == '__main__':
    run()
