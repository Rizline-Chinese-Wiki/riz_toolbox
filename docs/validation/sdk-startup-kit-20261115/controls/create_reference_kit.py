"""Deliver the exact accepted complete1172 SDK and its pinned offline template feed."""
import hashlib
import json
from pathlib import Path
import re
import zipfile

ROOT = Path(__file__).resolve().parents[2]
WORK = Path(__file__).resolve().parent
BASE = ROOT / 'docs/validation/sdk-startup-candidate-20261115'
FEED = Path('D:/riz-build-cache-next/sdk-startup-candidate-20261115/run-r1/dependency-feed')
OUT = ROOT / 'artifacts/delivery-20261115'
ZIP = OUT / 'RizToolbox-SDK-1.2.0-preview-20261115.zip'
sha = lambda p: hashlib.sha256(Path(p).read_bytes()).hexdigest()
read = lambda p: json.loads(Path(p).read_text(encoding='utf-8-sig'))


def require(value, reason):
    if not value:
        raise RuntimeError(reason)


require(not ZIP.exists() and not (WORK / 'delivery-receipt.json').exists(), 'Preserve existing kits/receipts')
require(sha(BASE / 'validation-summary.json') == '854f8c21176bc05d5b8f554b8e248af3e802d4af4a1e3942574f221e9818ad25', 'Current SDK archive changed')
state = read(BASE / 'run/result.json')
accepted_path = ROOT / 'work/sdk-startup-candidate-20261115/acceptance-receipt.json'
accepted = read(accepted_path)
require(state['passed'] and state['checksPassed'] == accepted['checksPassed'] == 14
        and state['frozenInputsUnchanged'] and len(state['frozenInputs']) == 1172
        and accepted['accepted'] and accepted['resultSha256'] == sha(BASE / 'run/result.json')
        and accepted['sdkPackageSha256'] == '7932f5773d4813a18ed5b5c72a0774dc042cc199fa48ebf8b07f180d0a25cfa1',
        'This complete1172 SDK is not accepted')
members = {}
pins = {}


def add(path, name, digest=None):
    path = Path(path)
    require(path.is_file() and name not in members, 'Missing/duplicate kit member')
    data = path.read_bytes()
    actual = hashlib.sha256(data).hexdigest()
    require(digest is None or actual == digest, 'Frozen kit source differs: ' + name)
    members[name] = data
    pins[name] = dict(sha256=actual, bytes=len(data))


def generated(name, text):
    require(name not in members, 'Duplicate generated member')
    data = text.encode('utf-8')
    members[name] = data
    pins[name] = dict(sha256=hashlib.sha256(data).hexdigest(), bytes=len(data))


add(BASE / 'package/RizToolbox.Sdk.1.2.0.nupkg', 'feed/RizToolbox.Sdk.1.2.0.nupkg', accepted['sdkPackageSha256'])
add(BASE / 'package/sdk-manifest.json', 'evidence/sdk-manifest.json')
fixity = read(BASE / 'dependency-feed/fixity.json')
for name, row in fixity['packages'].items():
    require((FEED / name).stat().st_size == row['bytes'], 'Dependency length differs')
    add(FEED / name, 'feed/' + name, row['sha256'])
template_inputs = {}
for name, digest in state['additionalInputs'].items():
    if name.startswith('templates/riz-plugin/'):
        member = 'template/riz-plugin/' + name[len('templates/riz-plugin/'):]
        add(BASE / 'source' / name, member, digest)
        template_inputs[member] = digest
require(len(template_inputs) == 5, 'Exact accepted template inventory differs')
add(BASE / 'source/global.json', 'global.json', state['frozenInputs']['global.json'])
add(BASE / 'source/packaging/sdk/README.md', 'SDK-PACKAGE.md', state['additionalInputs']['packaging/sdk/README.md'])
api_inputs = {}
rows = []
for name, digest in state['frozenInputs'].items():
    if name.startswith('src/RizToolbox.Sdk/') and name.endswith('.cs'):
        member = 'api/' + name[len('src/RizToolbox.Sdk/'):]
        add(BASE / 'source' / name, member, digest)
        api_inputs[member] = digest
        types = re.findall(r'public interface ([A-Za-z_][A-Za-z0-9_]*)', members[member].decode('utf-8-sig'))
        if types:
            rows.append('| [' + Path(member).name + '](' + member + ') | ' + ', '.join('`' + x + '`' for x in types) + ' |')
add(accepted_path, 'evidence/sdk14-acceptance.json')
add(BASE / 'run/result.json', 'evidence/sdk14-result.json')
add(BASE / 'supervision/controller-exit.json', 'evidence/sdk14-controller-exit.json')
add(BASE / 'dependency-feed/fixity.json', 'evidence/original-sdk14-feed-fixity.json')
generated('NuGet.Config', '<configuration><packageSources><clear/><add key="RizStartupPreview" value="feed"/></packageSources></configuration>\n')
generated('API-SURFACE.md', '# 当前SDK合同入口\n\nSDK的公共服务合同不局限于示例插件。以下索引来自本包精确编译源码，供IDE查看；api目录不是待编译插件源码，请勿复制进MyMod。\n\n接口可编译不表示当前游戏已实现或验收该能力。插件每次Enable通过GetService查询能力和结构化错误；实际可用状态以宿主status和对应能力为准。Speed/Seek与完整LOCAL engine仍未开放。\n\n| 合同源码 | 公开接口 |\n|---|---|\n' + '\n'.join(rows) + '\n')
generated('README.zh-CN.md', '''# RizToolbox SDK 1.2.0：当前启动候选开发包

用本包离线生成并编译第一个插件。它对应完整1172候选及已通过的双独立插件14项检查；引用nupkg SHA为7932f5773d4813a18ed5b5c72a0774dc042cc199fa48ebf8b07f180d0a25cfa1。
运行时另用同目录的RizToolbox-0.3.0-startup-preview-20261115.zip。SDK开发包不含宿主或游戏，完整SDK/B01–B19、正常通关、真实Speed/Seek和LOCAL engine生产准入仍未完成。

完整解压本ZIP，使用已安装的.NET8 SDK，在解压目录打开PowerShell，依次执行：

```powershell
dotnet new install .\\template\\riz-plugin --debug:custom-hive .\\.template-hive
dotnet new riz-plugin --name MyMod --PluginId author.mymod --output .\\MyMod --debug:custom-hive .\\.template-hive
dotnet restore .\\MyMod\\MyMod.csproj --configfile .\\NuGet.Config --packages .\\.nuget-cache -p:NuGetAudit=false
dotnet build .\\MyMod\\MyMod.csproj -c Release --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false
```

模板与NuGet缓存都在此目录，NuGet.Config仅使用随包feed，不修改全局模板或用户设置。global.json按本轮构建的.NET8/latestFeature选择SDK。四个原样.NET6引用/host包随包提供，许可保留在各nupkg内；原SDK14证据归档只记录依赖pin，这个交付包另外补入已逐字节核验的依赖字节。

产物是MyMod/bin/Release/net6.0/MyMod.dll与plugin.json。游戏关闭时将这两个文件放入BepInEx/RizToolbox/mods/author.mymod/。启动宿主后按template/riz-plugin/README.md用唯一command id启用author.mymod，并查看status.json。只复制插件自己的文件；SDK引用包不应随插件带入宿主DLL。

模板示例读取场景和订阅场景变化。API-SURFACE.md列出当前公开服务合同及精确源码入口；框架能力需要逐项查询，不以这一个示例代表SDK全部功能。游戏/音频/视觉资源操作应通过当前宿主提供的服务，并按scope清理与主线程合同调用。

本交付以candidateOnly发布，托管主目录整合和父长回归还在进行。证据14项属于SDK包/模板/正式托管宿主；本机预览运行时另已验证启动、设置页就绪及一次Failed失败结算，不能由这些结果推定完整SDK或全部游戏流程通过。manifest.json逐项记录本ZIP的内容与来源。
''')
manifest = dict(schema='current-startup-sdk-reference-kit/v1', candidateOnly=True, referenceKitOnly=True,
                sdkVersion='1.2.0', buildCompilerInputs=1172, fullCompilerTreeIncluded=False,
                sdkPackageSha256=accepted['sdkPackageSha256'], sdk14Checks=14,
                sdk14ArchiveSummarySha256=accepted['summarySha256'], sdk14ResultSha256=sha(BASE / 'run/result.json'),
                templateInputs=template_inputs, apiInputs=api_inputs, dependencyPackageBytesIncluded=True,
                runtimeIncluded=False, mainApplied=False, sdkGoalComplete=False, files=pins)
members['manifest.json'] = (json.dumps(manifest, ensure_ascii=False, indent=2) + '\n').encode('utf-8')
OUT.mkdir(parents=True, exist_ok=True)
with zipfile.ZipFile(ZIP, 'x', compression=zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
    for name, data in sorted(members.items()):
        archive.writestr(name, data)
with zipfile.ZipFile(ZIP) as archive:
    require(set(archive.namelist()) == set(members) and archive.testzip() is None, 'ZIP inventory or CRC differs')
    require(all(archive.read(name) == data for name, data in members.items()), 'Reopened ZIP bytes differ')
receipt = dict(schema='current-startup-sdk-reference-kit-receipt/v1', zip=str(ZIP), bytes=ZIP.stat().st_size,
               sha256=sha(ZIP), members=len(members), dependencyPackages=4, apiSourceFiles=len(api_inputs),
               reopenedZipChecked=True, sdk14Checks=14, compilerInputs=1172,
               deliveredFlowSmokePassed=False, candidateOnly=True, runtimeIncluded=False, sdkGoalComplete=False)
(WORK / 'delivery-receipt.json').write_text(json.dumps(receipt, indent=2) + '\n', encoding='utf-8')
(OUT / 'sdk-preview-receipt.json').write_text(json.dumps(receipt, indent=2) + '\n', encoding='utf-8')
print(json.dumps(receipt))
