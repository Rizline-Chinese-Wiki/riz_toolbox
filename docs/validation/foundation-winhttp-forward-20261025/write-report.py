"""Write the completed component's report from already archived observations."""
from pathlib import Path
import hashlib
import json

batch=Path(__file__).resolve().parent
repo=batch.parents[2]
def read(path):return json.loads(path.read_text(encoding='utf-8-sig'))
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def new(path,text):
    assert not path.exists(), 'Preserve an existing document: '+str(path)
    path.write_text(text,encoding='utf-8',newline='\n')
s=read(batch/'summary.json')
assert s['passed'] and s['cases']==21 and s['actualTerminalExitCode']==0
assert s['selectedEvidenceDirectory']=='attempts/r4'
report=repo/'docs/2026-10-25_底层-系统WinHTTP转发-report.md'
new(batch/'README.md',f'''# 固定系统 WinHTTP 转发：已通过自有宿主验证

2026-10-25。21 个独立进程全部通过，root 实际观察 session 40901 终态 0；5653 条父核验、2426 条宿主检查、456 次 typed API 调用及 132 组直接/转发对照分列。生产形态和测试形态各完成真实 TLS/C/C++/DllMain 四阶段以及 loader 外调用。

这完成了独立转发组件，尚未接入生产 Doorstop 或真实游戏。系统加载/解析只在 loader 外执行；初始化阶段仅测 Close(NULL) 与无 PAC/WPAD 的本地无效参数调用，不能推论任意 WinHTTP 调用在 DllMain 内安全。

- [正式报告](../../2026-10-25_底层-系统WinHTTP转发-report.md)、[汇总](summary.json)、[实际终态](terminal.json)、[成功命令](attempts/r4/command-result.json)。
- [613 项源码清单](source-final-manifest.json) / [ZIP](source-final-inputs.zip)：`{s['sourceArchiveSha256']}`；此前 604 项均保持原字节。
- [4 项二进制](binary-manifest.json) / [ZIP](binaries.zip)：`{s['binaryArchiveSha256']}`。
- [260 份原件](raw-manifest.json) / [ZIP](raw-inputs.zip)：`{s['rawArchiveSha256']}`；[21 进程原始汇总](raw/cases/fixture-result.json)、[PE 证明](raw/cases/pe-proof.json)。
- [初始化失败及修复审查](importer-initializer-review.md)、[验证器审查](review-final.md)、[后续接入](production-next.md)、[范围](scope.md)。

初编 SDK 警告、r2 的 TLS/CRT 初始化顺序错误、r3 的 C/C++ 入口被优化丢弃均保留于 history，分别记录实际失败，未计入通过。23 项已有包及游戏 18 项安装文件、4 项原始文件 hash 均一致；本批未重跑完整 1617 项回归，未安装或运行游戏。
''')
new(batch/'production-next.md','''# 下一步：接入生产启动链

独立四 API 转发已通过 21 个自有进程。当前生产 Doorstop 仍是旧代理路径，新运行核仅在自有启动链采用。下面是后续实施顺序，未标为完成。

1. 在 `native/RizToolbox.Doorstop` 增加明确的生产适配层。保持现有 82 个代理导出和其既有 ABI；不能把独立测试 DLL 的 7 个导出直接当作游戏代理替换包。四个实际 WinHTTP 导入须按名称绑定，不能沿用系统与代理不一致的序号。
2. 审查生产入口的 CRT、日志、路径及现有 load_proxy 调用顺序，确定经证实的系统加载位置。独立 resolver 的 outside-loader 成功不证明能在 DllMain 中加载；也不能把 UnityPlayer 的 TLS/静态初始化首次调用假定晚于 il2cpp_init。先用自有宿主复现生产入口关系，证明可行路径与拒绝路径，再谈生产首次发布凭据。
3. 将新 Kernel 的固定发布记录与生产版本、模块文件身份、四入口前缀和首次发布时机绑定；补齐独立 V4 证明与 CLR 只读采用。保留旧 helper 的独立身份，不把测试模块或记录混入生产。
4. 接 coordinator、真实对象根与 GC、Unity 线程与调度器；处理 async/Start 内联返回、取消完成与全部消费者结束后资源最终释放。责任未结束则 Pending 保留，不能提前宣布 EngineCancelled 或开放 RequestPlay。
5. 完成代码和自有宿主验收后再恢复游戏实测。按当前用户指示，先继续代码、游戏实测稍后；后续真实整局、失败、重试、暂停、返回与多插件共存的结果分别记录。

完整验收继续以 `.docs/foundation-expansion/SPEC.md` 的 B01–B19 为准。
''')
evidence=[
 ('E-001','实际正式终态','terminal.json','读取JSON；执行命令见下方复现步骤'),
 ('E-002','逐进程与ABI核验','raw/cases/fixture-result.json','重新运行独立宿主矩阵'),
 ('E-003','PE导入导出与真实系统文件','raw/cases/pe-proof.json','Test-WinHttpForwardFixture.ps1 自动执行 inspect'),
 ('E-004','TLS/CRT失败与修复','importer-initializer-review.md','读取保留COFF与审查中的实际段/符号/重定位'),
 ('E-005','613声明源码身份','source-final-manifest.json','对照 source-final-inputs.zip 逐文件计算SHA256'),
 ('E-006','最终归档与范围','summary.json','读取 summary.json 并对照 raw 与成功attempt'),
]
evidence_text='\n'.join(f'| {ident} | {title} | [{path}](validation/foundation-winhttp-forward-20261025/{path}) | `{sha(batch/path)}` | {repro} |' for ident,title,path,repro in evidence)
new(report,f'''# 系统 WinHTTP 四 API 转发实现与验证

2026-10-25，普通兼容性工程报告，flavor=null。目标是补齐 PC 模组公共底层的固定系统转发组件。生产与测试形态、独立 Importer 和宿主均已构建，21 个独立进程通过。首次失败暴露测试组件的 TLS/CRT 顺序及初始化入口保留问题，已修复且失败原件完整保留。组件尚未接入生产启动链，真实对象、GC、调度、取消及完整生命周期仍待完成。

## 范围和结果

授权与边界见 [scope.md](validation/foundation-winhttp-forward-20261025/scope.md)。本批仅运行自有宿主，在 loader 外解析和加载固定 System32/winhttp.dll；不安装或操作游戏。宿主不调用 Connect/OpenRequest/SendRequest，不开启 PAC/WPAD，不修改设置；设置字符串仅保留进程内加盐摘要。没有捕获网络流量，不能推断系统内部零流量。系统文件路径与身份验证也不能证明它在本进程所有历史时刻从未被替换。

| 观察 | 实际结果 | 范围 |
|---|---|---|
| 正式 root 命令 | session40901，exit0 | attempts/r4；r1–r3 失败另列 |
| 进程矩阵 | 2 正常形态 + 19 故障 = 21 | 每个实际 PID、退出码和独立父核验均保留 |
| 检查 | 父 5653，宿主 2426 | 两种计数分列，不折算能力覆盖率 |
| 调用 | 456 次 typed 调用，132 组直接/转发对照 | 含未初始化/poison 的本地拒绝 |
| 初始化阶段 | 两正常均 mask0x8f0、12行、0失败 | TLS/C/C++/DllMain 各两次本地拒绝，loader 外四 API |
| 冻结与保全 | 613 源、4 二进制、260 原件；17 项输入持只读句柄 | 此前 604 源、23 项旧包和游戏18+4文件均不变 |

## 实现和调用路径

四个 typed 入口是 WinHttpOpen、WinHttpGetProxyForUrl、WinHttpGetIEProxyConfigForCurrentUser、WinHttpCloseHandle。生产 DLL 只有这四个与三个控制导出，测试 DLL 额外一个固定 fault selector；两者无入口、TLS、CRT 初始化或任意路径输入，导入仅 Kernel32 与 ntdll 的异常支持。Import/Export、RIP 相对 ASM 尾转发和静态表均由冻结离线 PE 解析器核对，没有使用 IDA 或执行目标代码来做静态检查。

P-001，path_type=callflow：固定 System32 路径 → 持有禁止写删的文件句柄 → 绝对路径 LoadLibraryExW → 核对实际模块路径和文件身份 → PE 名称/EAT/归属/可执行页逐项检查 → 四槽一次原子发布 → typed 尾转发。重复或重入初始化永久转入 poison；已保留的模块、文件和表不释放，不伪装成可再次初始化。对应 E-002/E-003、F-001/F-002。

成功对照覆盖 Open 第五参数、完整32位 BOOL、LastError、每个实际 session 的配对关闭、系统输出 GlobalFree 以及三个真实 worker 的线程与调用行。故障矩阵涵盖路径、开文件、加载前后、四个名称、四个 self-address、文件身份、发布前和实际重入；均保持不得部分发布的约束。loader 内两个无效参数工作负载的成功只支持这两个具体调用，不能授予 resolver 或其它系统 API 在 DllMain 内安全的结论。

## Evidence → Finding → Path

下表 observed_at=2026-10-25，source_type=file，artifact_path/source_ref 为链接；linked_workitem=B02（部分），supersedes=none。原始运行与文件哈希分开记录。

| E-id | title | source_ref / artifact_path | content_hash SHA256 | repro_command / 限制 |
|---|---|---|---|---|
{evidence_text}

| F-id | severity | evidence_ids | confidence | location | status |
|---|---|---|---|---|---|
| F-001 | n/a_re | E-001,E-002,E-003 | high | riz_winhttp.c / riz_winhttp_entry.asm | 自有宿主验证完成；生产尚未接入 |
| F-002 | n/a_re | E-002,E-003 | high | 四槽发布与 repeat/reenter 分支 | 正常与19故障拒绝已验证；不是所有并发时序证明 |
| F-003 | n/a_re | E-004,E-002 | high | Importer.cpp | TLS 早读动态全局表已改栈内IAT；显式C/C++初始化槽以具名符号保留，CFG保持 |
| F-004 | n/a_re | E-005,E-006 | high | 归档与版本边界 | 成功r4独立归档；613声明源不覆盖初次失败证据；B01–B19仍active |

## 失败和修复时间线

2026-10-25，按实际执行顺序：初编 session62712/exit1，被旧SDK的C5105/WX阻挡，未起测试子进程；保留后加入兼容预处理器选项。r2 session17519/exit1，子进程49576因TLS早于CRT读取零函数指针触发CFG；单独观察器session64495的driver为0而目标仍0xc0000409，不计成功。r3 session49482/exit1，子进程59236正常输出失败原件，TLS已通过，但C/C++两个内部const槽被优化丢弃。r4改为具名外部符号和 /INCLUDE，session40901/exit0，21进程全部成功，实际二进制三槽与CFG独立核对。失败归档见 [history](validation/foundation-winhttp-forward-20261025/history/attempt-r2/terminal.json) 和 [r3](validation/foundation-winhttp-forward-20261025/history/attempt-r3/terminal.json)，首编另存 [attempt-1](validation/foundation-winhttp-forward-20261025/history/attempt-1/terminal.json)。

## 复现与交付

从仓库根在 PowerShell 7.2+ 运行以下命令，使用新输出目录，保留旧证据。需要本机 MSVC 2019 x64 工具与 Python 3.14；父脚本在实际运行期间持有关键输入。系统版本或编译结果变化时应作为新批记录，不覆写这次冻结原件。

```powershell
pwsh -NoProfile -File scripts/Build-WinHttpForwardFixture.ps1 -OutputDirectory D:/RizToolbox-build-cache/winhttp-reproduce/binaries
pwsh -NoProfile -File scripts/Test-WinHttpForwardFixture.ps1 -BinaryDirectory D:/RizToolbox-build-cache/winhttp-reproduce/binaries -OutputDirectory D:/RizToolbox-build-cache/winhttp-reproduce/cases
```

系统 WinHTTP：SHA256 `{s['systemWinHttp']['sha256']}`，{s['systemWinHttp']['bytes']} B。四个交付形态及哈希见 [binary-manifest](validation/foundation-winhttp-forward-20261025/binary-manifest.json)；[源码ZIP](validation/foundation-winhttp-forward-20261025/source-final-inputs.zip) SHA256 `{s['sourceArchiveSha256']}`。未重跑此前25套1617，未安装新包，不发生产V4凭据，不宣称EngineCancelled或资源全局退休。后续按 [production-next.md](validation/foundation-winhttp-forward-20261025/production-next.md) 接入生产启动链，再处理对象、GC、调度与整局流程。
''')
progress=repo/'.docs/PROGRESS.md'
text=progress.read_text(encoding='utf-8')
old='后续顺序是：实现并验证固定系统 WinHTTP 转发 → 接入真实游戏启动和新核 → 补齐对象、调度、取消和资源释放 → 验收整局、重试、返回与多插件共存。'
assert old in text
text=text.replace(old,'最新一批：固定系统 WinHTTP 四 API 转发已通过 21 个独立自有进程，覆盖直接/转发对照、四种真实初始化阶段、线程及失败拒绝。测试组件的 TLS/CRT 顺序和初始化入口保留问题已修复。组件尚未接入生产 Doorstop，未安装或运行游戏。\n\n后续顺序是：接入生产启动链和新核 → 补齐对象、GC、调度、取消和资源释放 → 验收整局、重试、返回与多插件共存。')
text+='\n最新证据：[系统 WinHTTP 转发](../docs/validation/foundation-winhttp-forward-20261025/README.md)。\n'
progress.write_text(text,encoding='utf-8',newline='\n')
targets=[
 (repo/'README.md','docs/validation/foundation-winhttp-forward-20261025/README.md'),
 (repo/'.docs/CURRENT-CHECKPOINT.md','../docs/validation/foundation-winhttp-forward-20261025/README.md'),
 (repo/'docs/IMPLEMENTATION-STATUS.md','validation/foundation-winhttp-forward-20261025/README.md'),
 (repo/'.docs/foundation-expansion/CHECKLIST.md','../../docs/validation/foundation-winhttp-forward-20261025/README.md'),
]
for path,link in targets:
    text=path.read_text(encoding='utf-8')
    assert 'WINHTTP-FORWARD-LATEST:BEGIN' not in text
    marker='<!-- EARLY-KERNEL-LATEST:BEGIN -->'
    assert marker in text
    block=f'''<!-- WINHTTP-FORWARD-LATEST:BEGIN -->
最新补充：[固定系统 WinHTTP 转发已通过独立宿主验证]({link})。root实际session40901/exit0，21进程、5653父核验；613声明源/4二进制/260原件归档。TLS与C/C++入口保留问题已修复，CFG保持。该组件尚未接入生产Doorstop或游戏；下一步是生产首次发布与新核采用，然后对象GC、调度、取消和全部消费者退休。此前1617属于旧完整构建，本批未重跑；游戏实测仍稍后，B01–B19验收状态不变。
<!-- WINHTTP-FORWARD-LATEST:END -->

以下新核启动链为本批之前的历史时点，其中关于WinHTTP尚未实现的描述已由上方接续。

'''
    text=text.replace(marker,block+marker,1)
    if path.name=='CURRENT-CHECKPOINT.md':
        text=text.replace('下一步是固定系统 WinHTTP 转发和生产启动接入。','固定系统 WinHTTP 转发也已通过独立验证；下一步是生产启动接入。',1)
    path.write_text(text,encoding='utf-8',newline='\n')
print(json.dumps(dict(report=str(report),reportSha256=sha(report),updatedProgress=True,updatedEntryDocuments=4),ensure_ascii=False))
