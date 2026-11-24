"""Document this completed candidate batch without expanding its proof scope."""
from pathlib import Path
import hashlib
import json
import shutil
import zipfile

batch=Path(__file__).resolve().parent
repo=batch.parents[2]
def read(path):return json.loads(path.read_text(encoding='utf-8-sig'))
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def new(path,text):
    assert not path.exists(),str(path)
    path.write_text(text,encoding='utf-8',newline='\n')
s=read(batch/'summary.json');assert s['passed'] and s['actualTerminalExitCode']==0
history=batch/'history';history.mkdir(exist_ok=True)
for name,directory,session,kind in [
    ('initial-ordinal','doorstop-cold-preflight-20261025',12772,'Build succeeded; exact 82-name/ordinal check rejected 10 changed ordinals.'),
    ('cold-preflight-build','doorstop-cold-r2-20261025',89552,'Candidate and caller built; later modifications document edit makes this preflight unsuitable as final frozen source.'),
    ('cold-preflight-run','doorstop-cold-r2-preflight-20261025',None,'Owned child PID56976 exited0; preliminary actual startup, separately from formal session80976.'),
]:
    destination=history/name;destination.mkdir(exist_ok=False)
    root=Path('C:/Users/71957/riz-build-cache')/directory
    chosen=[path for path in root.rglob('*') if path.is_file() and 'tools' not in path.relative_to(root).parts
        and (path.suffix.lower() in ('.json','.log','.zip','.map','.dll','.exe','.bin','.h') or path.name=='dll.def')]
    rows=[]
    with zipfile.ZipFile(destination/'preflight.zip','x',compression=zipfile.ZIP_DEFLATED) as archive:
        for path in chosen:
            name_in_zip=path.relative_to(root).as_posix();archive.write(path,name_in_zip)
            rows.append(dict(path=name_in_zip,sha256=sha(path)))
    new(destination/'observation.json',json.dumps(dict(sessionId=session,actualTerminalExitCode=0,
        scope=kind,selectedArtifactFiles=len(rows),files=rows,gameExecuted=False,installed=False),ensure_ascii=False,indent=2)+'\n')
new(batch/'production-next.md','''# 下一步：生产 V4 与新核采用

固定四 API 已接入实际 Doorstop DllEntry，并在自有普通依赖调用链通过冷启动。保持本批候选开关和历史默认路径可区分。下列是下一实施包，尚未完成。

1. 增加独立 `RizToolbox.EngineBootstrap.dll` 生产 owner，静态依赖无入口的新 Kernel，代理普通依赖 Bootstrap。三者部署必须真实满足 Windows 普通依赖搜索；不能在 DllMain 中临时加载 Kernel 或改搜索目录。Bootstrap 无 CRT/TLS/动态初始化。
2. 直接接真实 DllEntry 的 reason 与 reserved 参数，区分静态启动和动态加载。动态加载不授首次写。对固定 EXE/UnityPlayer 验文件与映像、四完整函数和六份实际 RF/unwind，使用本批 EngineGameProfile 的定长常量；原 fixture marker 检查保留在原路径，不能删除后改成泛用 patch API。
3. 内部固定四 source/RVA，静态 originals/continuation，四 kind/slot28 Configure 后读真实 epoch/entry，再 Bind 与四次按序 Commit。V4 分开记录首次准入依据、producer epoch、Kernel ABI2/scalar1、四行身份、所有写尝试/完成和保护责任；新V4不能是fixture publisher2或wire3改名。
4. 发布过程中任何源写尝试或保护责任后失败都保留 Unknown 并阻止卸载后继续执行，后续代理初始化失败也要合并责任。不得留下源跳转进入已卸载模块。完全无写且无责任时才允许 DisabledNoSourceWrite。
5. 增加 CLR 私有构造的 NativeEngineFirstInstall owner 与 bridge/dispatcher AdoptPublished。只用已加载 Bootstrap/Kernel 的 GetModuleHandle 与 GetExport，逐行复核凭据及实际映像；Attach前后再验。不能在 CLR 中 Load/Configure/补patch，新核和旧GameAssembly V3仍分别核对。
6. GameAssembly 不在固定 EXE/UnityPlayer 普通依赖中，过早阶段不能要求它已加载，也不能为满足检查提前加载。其实际模块与 IL2CPP 条件留给现有 pre-init/CLR 接续；必要的磁盘身份单独验证。
7. 使用自有等形目标验证完整 producer→Kernel→CLR、四首次调用、重入/故障/保护与失败不卸载；随后在用户恢复实测后观察真实游戏的发布先于 Unity TLS/入口/首次引擎调用。静态依赖、已映射模块和非空 reserved 各自只有有限含义，不能单独当成所有历史执行者闭合证明。

之后接真实对象根与 GC、coordinator、scheduler/async/Start 内联收尾、取消完成及所有消费者资源退休，再验整局、重试、返回和多插件。完整 B01–B19 保持原范围。
''')
new(batch/'README.md',f'''# 实际 Doorstop 加载入口：冷启动接线已验证

2026-10-25。root 实际观察 session80976 终态0。新候选保持旧代理全部82项名称和序号，四个WinHTTP入口使用canonical typed转发；真正DllEntry在CRT/日志前完成初始化。自有Host通过普通依赖加载Importer和该候选，真实TLS/C/C++/DllMain随后观察到Ready。

- [5个冷启动/身份拒绝结果](raw/cases/fixture-result.json)：3正常、2明确拒绝，5240父核验、432调用、198对照；41项输入持只读句柄。
- [原21转发回归](raw/standalone-cases/fixture-result.json)：5653父核验，默认独立7/8导出合同保持。旧默认Doorstop另行构建，82导出与已安装基线一致。
- [正式报告](../../2026-10-25_底层-Doorstop冷启动接线-report.md)、[汇总](summary.json)、[真实终态](terminal.json)、[正式命令](attempts/r3/command-result.json)、[交叉审查](fixture-cross-review.md)。
- [626源码清单](source-final-manifest.json) / [ZIP](source-final-inputs.zip)：`{s['sourceArchiveSha256']}`；旧613中4项明确修改，13项新增。
- [8二进制](binary-manifest.json) / [ZIP](binaries.zip)：`{s['binaryArchiveSha256']}`；候选与旧默认区分保存，不是安装包替换。
- [585份运行/构建原件](raw-manifest.json) / [ZIP](raw-inputs.zip)：`{s['rawArchiveSha256']}`。2780份提取后的xmake工具文件仅存哈希，工具原归档由上游锁文件固定；源代码、编译对象、日志及所有case原件保留。
- [四引擎入口固定资料](research/engine-game-profile.md)、[完整事实JSON](research/engine-game-profile.json)、[下一步生产采用](production-next.md)、[范围](scope.md)。

最初候选编译成功但10个旧ordinal变化，严格核验拒绝后修为ASCII名称序；失败/预检保存在[history](history/initial-ordinal/observation.json)。其他78转发保持既有逻辑，不代表全部82个签名/调用已验收。两种负例仅证明各自部署条件下干净拒绝，没有记录被拒模块路径，不能把它们扩大为模块映射来源证明。

本批未安装或运行游戏；18安装项、4原始文件、23已有包项均hash不变。V4、新核生产采用、对象GC/调度/取消与全资源退休仍待实现，完整B01–B19没有因此勾完。此前25套1617没有在本批重跑。
''')
report=repo/'docs/2026-10-25_底层-Doorstop冷启动接线-report.md'
new(report,f'''# Doorstop 冷启动转发接线实现与验证

2026-10-25，普通兼容性工程，flavor=null。新增可单独构建的固定系统WinHTTP候选，把已有四API转发接到真正Doorstop加载入口，并保持旧82项公开名称和序号。自有普通静态依赖链完成3次冷启动，以及缺代理、旧代理两种干净拒绝；原21转发回归和默认Doorstop构建也通过。当前没有把候选部署到游戏，新核生产发布和完整生命周期仍未完成。[授权、范围及网络边界](validation/foundation-doorstop-cold-20261025/scope.md)。

## 交付与实际结果

root唯一正式构建session80976实际exit0，归档命令也实际exit0。5个冷启动进程分列为5240条父核验、432次实际typed调用、198组直接/转发对照；21个独立转发进程为5653条父核验、456次调用、132组对照。默认Doorstop另构建验证82项导出，不能把编译当作游戏运行或把检查数换算覆盖率。

候选4名直接导出canonical ASM尾跳。内部GetInfo、加载入口记录和Attach只作为MAP符号保留，没有添加到DLL导出表。最初误用proxylist行号分配ordinal，前19项中的10项发生变化，严格82项PE比对真实拒绝；修复为Ordinal名称排序后与旧已安装代理完全一致。原名之外的78个代理入口保持原生成逻辑和来源选择，17个Version名称不因此获得系统WinHTTP中不存在的实现。

P-001，path_type=callflow：Host普通导入Importer → Importer普通按名导入WINHTTP四API → 候选真实DllEntry调用wrapper → 固定绝对System32路径加载/核身份与4名称 → 四槽一次发布并封存入口记录 → 返回loader → Importer真实TLS/C/C++/DllMain分别读到完整Ready并运行Close(NULL)/本地Proxy(NULL) → loader外四API及3worker直接对照 → 同一绝对代理再次LoadLibrary仅增加引用，不重跑首次初始化。此路径通过E-001–E-004支持，不等于真实UnityPlayer已经运行。

每个正常调用者在使用隐藏MAP RVA前验证准确磁盘SHA、PE标头、4个IAT/导出地址及内存归属/保护。五阶段各保留1248字节live Info和1304字节入口记录，Importer完整16832字节；三次宿主快照为2592字节，调用行320字节。父独立逐字段解码并比较JSON/原件、实际PID、文件身份、系统EAT、table、32位BOOL、Open第五参数、LastError及输出释放。初始化阶段仅两个具体负例，完整Open/设置查询和线程都在loader外；fixture不发HTTP请求、不启PAC/WPAD、不改设置，私密字符串仅保存加盐摘要，无网络抓包。

## Evidence → Finding → Path

以下observed_at=2026-10-25，source_type=file，artifact_path/source_ref为链接，linked_workitem=B02部分，supersedes=none。复现脚本从当前仓库根运行，原件固定于本批独立目录。

| E-id | source_ref / artifact_path | content_hash SHA256 | repro_command / 限制 |
|---|---|---|---|
| E-001 | [真实终态](validation/foundation-doorstop-cold-20261025/terminal.json) | `{sha(batch/'terminal.json')}` | 只读终态；正式命令见attempts/r3 |
| E-002 | [5进程原件](validation/foundation-doorstop-cold-20261025/raw/cases/fixture-result.json) | `{sha(batch/'raw/cases/fixture-result.json')}` | Test-WinHttpColdFixture.ps1，需新输出目录 |
| E-003 | [PE/MAP/静态依赖证明](validation/foundation-doorstop-cold-20261025/raw/cases/pe-proof.json) | `{sha(batch/'raw/cases/pe-proof.json')}` | verify-cold.py --inspect --binary；只读、不加载目标 |
| E-004 | [交叉审查](validation/foundation-doorstop-cold-20261025/fixture-cross-review.md) | `{sha(batch/'fixture-cross-review.md')}` | 只读源码及原件；审查者是caller作者，非外部审计 |
| E-005 | [完整汇总](validation/foundation-doorstop-cold-20261025/summary.json) | `{sha(batch/'summary.json')}` | 原件及ZIP逐项重算SHA |
| E-006 | [固定游戏事实](validation/foundation-doorstop-cold-20261025/research/engine-game-profile.json) | `{sha(batch/'research/engine-game-profile.json')}` | extract-engine-game-profile.py，仅磁盘读取 |

| F-id | severity | evidence_ids | confidence | location | status |
|---|---|---|---|---|---|
| F-001 | n/a_re | E-001,E-002,E-003 | high | Build-Doorstop / embedded wrapper | 实际DllEntry自有冷启动验证完成，生产游戏未采用 |
| F-002 | n/a_re | E-002,E-004 | high | Cold Importer/Host | 两错误部署条件干净拒绝；没有被拒模块path/fileID记录 |
| F-003 | n/a_re | E-003,E-005 | high | DEF生成、canonical profile | 82名称/ordinal保持；默认21转发和旧构建回归通过 |
| F-004 | n/a_re | E-006 | high | EngineGameProfile.h | 固定四完整函数与6份unwind事实已生成，FactsOnly且不授权V4 |

## 时间线与保留边界

按实际执行顺序：session12772编译exit0，但严格导出核验随后exit1；修复ordinal后session89552完成候选及调用者构建exit0。PID56976初步冷启动exit0；之后仅修改对应源码说明文档，因此旧r2不充作最终源码版本。正式r3冻结626项后，session80976的四阶段全部完成exit0；原件中的三个normal分别PID4576/13588/43968、exit0，两个拒绝PID45848/57436、exit1且sehCode0，超时不算通过。归档实际退出0，初次失败和预检另存history。

CFG被构建与PE/GFID检查保持，实际调用成功；本批不声称所有进程CFG策略已启用。C/C++槽有具名符号和/INCLUDE、真实阶段记录，未再从独立MAP还原所有槽归属。核验器按阶段分组查调用，但未直接自动断言前8行全局阶段顺序；交叉审查读取三份实际行，均为4,4,5,5,6,6,7,7，其后11的4行。没有将这些边界改写为未发生的全覆盖证明。

固定四原始文件SHA前后相同；Start/Execute/Release/Detach完整body长度1919/494/232/539，Release保留三段真实RF，其中两段CHAININFO回primary，共6份unwind原始字节。metadata磁盘头不是标准magic，format未知、version=null。头文件仅给事实，不授首次准入；静态导入与模块已映射不能单独证明真实游戏所有早期执行历史。

## 复现与下一步

Windows x64，MSVC2019 14.29.30133/SDK10.0.19041.0、固定xmake2.6.1、Python3.14。在仓库根使用全新用户目录运行：

```powershell
pwsh -NoProfile -File scripts/Build-WinHttpColdFixture.ps1 -OutputDirectory C:/Users/71957/riz-build-cache/cold-reproduce/binaries
pwsh -NoProfile -File scripts/Test-WinHttpColdFixture.ps1 -BinaryDirectory C:/Users/71957/riz-build-cache/cold-reproduce/binaries -OutputDirectory C:/Users/71957/riz-build-cache/cold-reproduce/cases
```

[626源码ZIP](validation/foundation-doorstop-cold-20261025/source-final-inputs.zip) SHA `{s['sourceArchiveSha256']}`；候选对应LGPL源码和完整说明随raw/binaries/doorstop保留。旧613源中4项修改、13项新增，8二进制/585运行构建原件归档，2780个提取工具文件保留hash并由上游归档锁定。23旧包、18安装文件和4原始文件均保持，未重跑此前25套1617、未安装或启动游戏。

接下来按[生产接续](validation/foundation-doorstop-cold-20261025/production-next.md)实现独立V4 owner和已加载Kernel的CLR只读采用，再补对象/GC/coordinator/scheduler/取消与资源全局退休以及完整游玩流程。EngineCancelled和RequestPlay准入仍按已有保守合同；B01–B19继续active。
''')
# Entry documents are not frozen implementation inputs.
targets=[(repo/'README.md','docs/validation/foundation-doorstop-cold-20261025/README.md'),
    (repo/'.docs/CURRENT-CHECKPOINT.md','../docs/validation/foundation-doorstop-cold-20261025/README.md'),
    (repo/'docs/IMPLEMENTATION-STATUS.md','validation/foundation-doorstop-cold-20261025/README.md'),
    (repo/'.docs/foundation-expansion/CHECKLIST.md','../../docs/validation/foundation-doorstop-cold-20261025/README.md')]
for path,link in targets:
    text=path.read_text(encoding='utf-8');marker='<!-- WINHTTP-FORWARD-LATEST:BEGIN -->';assert marker in text and 'DOORSTOP-COLD-LATEST:BEGIN' not in text
    block=f'''<!-- DOORSTOP-COLD-LATEST:BEGIN -->
最新补充：[实际Doorstop冷启动接线已验证]({link})。session80976实际exit0：3正常冷启动+2错误部署拒绝、原21转发回归、旧默认Doorstop构建均通过。82名称/ordinal保持，候选真实DllEntry初始化先于自有Importer的TLS/C/C++/DllMain；626源/8二进制/585原件归档。固定游戏四引擎入口profile事实已补全。尚未安装或运行游戏，生产V4/新核只读采用、对象GC、调度、取消与全部资源退休仍待完成；B01–B19状态不变。
<!-- DOORSTOP-COLD-LATEST:END -->

以下为前一批独立转发组件的历史时点；其“尚未接入Doorstop”已由上述候选接续，仍未接入真实游戏。

'''
    text=text.replace(marker,block+marker,1)
    if path.name=='CURRENT-CHECKPOINT.md':text=text.replace('下一步是生产启动接入。','实际Doorstop冷启动候选也已通过；下一步是生产V4和新核采用。',1)
    path.write_text(text,encoding='utf-8',newline='\n')
progress=repo/'.docs/PROGRESS.md';text=progress.read_text(encoding='utf-8')
old='最新一批：固定系统 WinHTTP 四 API 转发已通过 21 个独立自有进程，覆盖直接/转发对照、四种真实初始化阶段、线程及失败拒绝。测试组件的 TLS/CRT 顺序和初始化入口保留问题已修复。组件尚未接入生产 Doorstop，未安装或运行游戏。'
assert old in text
text=text.replace(old,'最新一批：四 API 转发已接进真正 Doorstop 的加载入口，候选保持原82项导出名称和序号。3次自有冷启动、缺代理/旧代理两种拒绝均通过；原21转发回归与旧默认Doorstop构建也通过。固定游戏四个引擎入口的函数体、ABI和展开信息已提取为可用常量。新候选尚未安装到游戏。')
text=text.replace('后续顺序是：接入生产启动链和新核 →','后续顺序是：实现生产V4发布与新核只读采用 →')
text+='\n最新启动接线证据：[Doorstop冷启动](../docs/validation/foundation-doorstop-cold-20261025/README.md)。\n'
progress.write_text(text,encoding='utf-8',newline='\n')
print(json.dumps(dict(report=str(report),reportSha256=sha(report),entryDocumentsUpdated=5,historyPreserved=True),ensure_ascii=False))
