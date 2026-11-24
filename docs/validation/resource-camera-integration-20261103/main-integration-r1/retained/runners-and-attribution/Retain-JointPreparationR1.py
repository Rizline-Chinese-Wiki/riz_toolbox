"""Retain the staged compile and rollback sources, with no functional admission claim."""
from pathlib import Path
import datetime
import hashlib
import json
import shutil

P = Path(__file__).resolve().parent
MAIN = Path('C:/Users/71957/Desktop/riz_toolbox')
O = MAIN / 'docs/validation/resource-camera-integration-20261103/preparation-r2'
FAILED = Path('D:/riz-build-cache-next/resource-camera-integration-preparation-failure-r1-20261103')
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
fact = lambda p: dict(path=str(p), sha256=sha(p), bytes=p.stat().st_size)
read = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))


def save(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def main():
    assert not O.exists(), 'Preserve the original staged integration package.'
    prepared = read(P / 'preparation-result.json')
    compiled = read(P / 'joint-compile-r1/test-result.json')
    attribution = read(P / 'joint-compile-r1/source-and-product-attribution.json')
    assert prepared['passed'] and prepared['stageOnly'] and prepared['lifecycleDisposePreserved']
    assert compiled['passed'] and compiled['actualBuildExitCode'] == 0 and compiled['errors'] == 0
    assert attribution['passed'] and attribution['byteIdenticalFiles'] == 12
    for manifest in ['main-before-manifest.json','resource-after-sdk-manifest.json','joint-candidate-manifest.json']:
        data = read(P / manifest)
        for row in data['files']:
            assert sha(Path(data['source']) / row['path']) == row['sha256']
    assert sha(MAIN / 'README.md') == 'cfbac4691d36edc5daba513aa62d5856ae61e29bf553d0dc2979832750847364'
    O.mkdir(parents=True)
    originals = {p: 'retained/' + p.relative_to(P).as_posix() for p in P.iterdir() if p.is_file()}
    for p in (P / 'joint-compile-r1').iterdir():
        if p.is_file():
            originals[p] = 'retained/' + p.relative_to(P).as_posix()
    for p in FAILED.iterdir():
        if p.is_file():
            originals[p] = 'retained/failed-preparation-r1/' + p.name
    for p in [Path('D:/riz-build-cache-next/Prepare-ResourceCameraIntegrationR1.py'),
              Path('D:/riz-build-cache-next/Prepare-ResourceCameraIntegrationR2.py')]:
        originals[p] = 'retained/runners/' + p.name
    for row in attribution['compared']:
        p = Path(row['freshJoint']['path'])
        assert sha(p) == row['freshJoint']['sha256']
        originals[p] = 'retained/joint-current-products/' + p.name
    files = []
    for p, rel in sorted(originals.items()):
        target = O / rel
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(p, target)
        assert sha(target) == sha(p)
        files.append(dict(original=fact(p), retained=fact(target)))
    save(O / 'retained-artifacts.json', dict(passed=True, files=files, count=len(files)))
    report = '''# 资源连续性＋相机缩放联合准备

联合915输入已物理组装并通过fresh solution编译：0错误／56既有警告。相对已通过验证的V，唯一源码差异是资源SDK测试中显式释放旧prepared reservation的Dispose修补；485产品输入及48原生输入保持相同。六组件的12份DLL/PDB与V的实际solution产物逐字节一致，本包没有重复跑旧功能矩阵。

待合差分为U53路径，再合V9路径；重叠1路径后总共61路径（21新增／40修改）。[三个源码ZIP](retained/preparation-result.json)分别保存主目录改前894、包含Dispose的U913、完整联合915；[分阶段差分](retained/staged-deltas.json)和01／02补丁明确各自before／after hash。不能用旧V全树ZIP覆盖该修补。

本包passed只表示准备完整性和编译结果，`mainApplied=false`、`jointBehaviorValidationComplete=false`、`integrationAdmissionVerified=false`。原准备r1曾误读映像证据索引的passed字段，实际exit1且尚未创建候选目录；原件在failed-preparation-r1，修正r2按实际325检查、六个actual0、最终hash guards和completed audit验证，没有放宽映像要求。

下一先等U当前SDK及独立审计、T完整18SDK及closure的四个真实结果，并逐项核原件。全部通过后仍需按顺序精确合主、fresh整合验证；本包没有应用文件，也不运行或安装游戏。B01–B19、完整generation／未知lower／production／Seek／effect仍未完成。
'''
    (O / 'README.md').write_text(report, encoding='utf-8')
    index = dict(kind='retained-resource-camera-joint-preparation-r2', passed=True, integrityOnly=True,
                 createdAtUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
                 mainInputs=894, resourceInputs=913, jointInputs=915,
                 resourceChanges=53, cameraChanges=9, combinedChanges=61, addedFiles=21, modifiedFiles=40,
                 lifecycleDisposePreserved=True, jointCompileActualExitCode=0, jointCompileErrors=0,
                 jointCompileWarnings=56, byteIdenticalProductFiles=12, sourceDifferenceFromValidatedV=attribution['sourceDifferenceFromV'],
                 retainedOriginals=len(files), artifacts=fact(O / 'retained-artifacts.json'),
                 mainApplied=False, sourceCandidatesModified=False, newFunctionalTestsExecuted=False,
                 jointBehaviorValidationComplete=False, integrationAdmissionVerified=False, mainApplicationReady=False,
                 gameExecuted=False, installed=False, productionAdmission=False, fullBCompleted=False)
    save(O / 'scope-index.json', index)
    manifest = [fact(p) for p in sorted(O.rglob('*')) if p.is_file()]
    save(O / 'package-hash-manifest.json', dict(passed=True, files=manifest))
    for row in manifest:
        assert sha(Path(row['path'])) == row['sha256']
    print(json.dumps(dict(passed=True, retainedOriginals=len(files), packageFiles=len(manifest), mainApplied=False, output=str(O))))


if __name__ == '__main__':
    main()
