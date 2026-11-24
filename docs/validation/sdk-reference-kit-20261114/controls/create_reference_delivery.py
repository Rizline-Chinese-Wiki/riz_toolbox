"""Create a new local SDK reference kit, without replacing any runtime release."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import zipfile

ROOT = Path(__file__).resolve().parents[2]
WORK = Path(__file__).resolve().parent
ARCHIVE = ROOT / 'docs/validation/sdk-current-hold-20261114'
DEST = ROOT / 'artifacts/sdk-reference-candidate-20261114-hold939'
ZIP = DEST.with_suffix('.zip')
sha = lambda path: hashlib.sha256(Path(path).read_bytes()).hexdigest()
read = lambda path: json.loads(Path(path).read_text(encoding='utf-8-sig'))
if DEST.exists() or ZIP.exists(): raise RuntimeError('Preserve delivery outputs')
if (WORK / 'delivery-receipt.json').exists(): raise RuntimeError('Preserve delivery receipts')
if sha(ARCHIVE / 'validation-summary.json') != '012350764368913ea506e06d57b636cd9a02374091c7468af0e90e88d67724ff':
    raise RuntimeError('Verified SDK archive differs')
state = read(ARCHIVE / 'run/result.json')
if not state['passed'] or state['checksPassed'] != 14 or not state['candidateOnly']:
    raise RuntimeError('SDK candidate scope is incomplete')
staging = DEST.with_name(DEST.name + '.staging')
if not all(path.resolve().is_relative_to(ROOT.resolve()) for path in (DEST, ZIP, staging)):
    raise RuntimeError('Delivery paths escaped the checked workspace')
if staging.exists(): raise RuntimeError('Preserve staging')
staging.mkdir(parents=True)


def copy(source, name, digest=None):
    source = Path(source); target = staging / name
    if not target.resolve().is_relative_to(staging.resolve()) or target.exists(): raise RuntimeError('Invalid/duplicate delivery')
    digest = digest or sha(source); target.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(source, target)
    if sha(source) != digest or sha(target) != digest: raise RuntimeError('Delivery bytes changed')


copy(ARCHIVE / 'package/RizToolbox.Sdk.1.2.0.nupkg', 'feed/RizToolbox.Sdk.1.2.0.nupkg', state['package']['sha256'])
for name, row in state['dependencies'].items(): copy(ARCHIVE / 'dependency-feed' / name, 'feed/' + name, row['sha256'])
for name, digest in state['additionalInputs'].items():
    if name.startswith('templates/riz-plugin/'):
        copy(ARCHIVE / 'source' / name, 'template/riz-plugin/' + name[len('templates/riz-plugin/'):], digest)
for author in ('AuthorOne', 'AuthorTwo'):
    for name in ('Plugin.cs', 'DistributionProbe.cs', author + '.csproj', 'plugin.json', 'packages.lock.json'):
        copy(ARCHIVE / 'distribution/generated' / author / name, 'verification-examples/' + author + '/' + name)
(staging / 'NuGet.Config').write_text('<configuration><packageSources><clear/><add key="RizCandidate" value="feed"/></packageSources></configuration>\n', encoding='utf-8')
(staging / 'README.zh-CN.md').write_text('''# RizToolbox SDK 参考包：候选939

这是给模组作者编译、检查模板的开发参考包。SDK合同版本1.2.0；来源为939输入的当前隔离候选。它不含游戏运行时安装包，新Hold实现尚未合主，完整SDK/B01–B19未验收。真实Speed/Seek、生产启动与HoldIdle仍关闭。

本包带实际验证过的SDK引用nupkg、原始插件模板和本机缓存中原样复制的四个.NET6框架引用包，可以离线还原。第三方包未改动，许可证保留在各nupkg内。verification-examples是两份分发链校验源码，含专用错误合同探针，不能当作游戏功能模组。

在本目录打开PowerShell，使用已安装的.NET8 SDK：

```powershell
dotnet new install .\\template\\riz-plugin --debug:custom-hive .\\.template-hive
dotnet new riz-plugin --name MyMod --PluginId author.mymod --output .\\MyMod --debug:custom-hive .\\.template-hive
dotnet restore .\\MyMod\\MyMod.csproj --configfile .\\NuGet.Config
dotnet build .\\MyMod\\MyMod.csproj -c Release --no-restore
```

模板只装入此目录的独立hive。新建项目会得到只读场景示例；游戏加载仍需要另行验收的兼容运行时。本次没有安装或启动游戏。

同一候选SDK的两份独立插件已通过正式宿主14/14检查；这是编译引用、模板和托管宿主证据，不是完整游戏或SDK验收。详细证据位于项目docs/validation/sdk-current-hold-20261114，清单摘要和所有交付文件SHA见manifest.json。旧881输入预览ZIP保持不变。
''', encoding='utf-8', newline='\n')
files = {path.relative_to(staging).as_posix(): dict(bytes=path.stat().st_size, sha256=sha(path)) for path in sorted(staging.rglob('*')) if path.is_file()}
manifest = dict(schema='sdk-reference-candidate-delivery/v1', compilerInputs=939, sdkContractVersion='1.2.0',
    candidateOnly=True, referenceKitOnly=True, distributionChecks=14, sdkGoalComplete=False, mainApplied=False,
    runtimeIncluded=False, fullManagedMatrix=False, installed=False, gameStarted=False,
    archiveSummarySha256=sha(ARCHIVE / 'validation-summary.json'), packageSha256=state['package']['sha256'], files=files)
(staging / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
os.rename(staging, DEST)
with zipfile.ZipFile(ZIP, 'x', compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
    for path in sorted(DEST.rglob('*')):
        if path.is_file():
            entry = zipfile.ZipInfo(DEST.name + '/' + path.relative_to(DEST).as_posix(), date_time=(2026, 11, 14, 0, 0, 0))
            entry.compress_type = zipfile.ZIP_DEFLATED; archive.writestr(entry, path.read_bytes(), compresslevel=9)
with zipfile.ZipFile(ZIP) as archive:
    expected = set(files) | {'manifest.json'}
    actual = {name[len(DEST.name) + 1:] for name in archive.namelist()}
    if actual != expected: raise RuntimeError('ZIP inventory differs')
    for name in expected:
        if archive.read(DEST.name + '/' + name) != (DEST / name).read_bytes(): raise RuntimeError('ZIP content differs')
receipt = dict(schema='sdk-reference-candidate-delivery-receipt/v1', archive=str(ZIP), bytes=ZIP.stat().st_size,
    sha256=sha(ZIP), inventoryChecked=True, sdkGoalComplete=False, mainApplied=False, referenceKitOnly=True)
target = WORK / 'delivery-receipt.json'
if target.exists(): raise RuntimeError('Preserve delivery receipts')
target.write_text(json.dumps(receipt, indent=2) + '\n', encoding='utf-8')
print(json.dumps(receipt))
