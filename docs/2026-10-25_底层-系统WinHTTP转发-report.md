# 系统 WinHTTP 四 API 转发实现与验证

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
| E-001 | 实际正式终态 | [terminal.json](validation/foundation-winhttp-forward-20261025/terminal.json) | `49cb6cac3ee71aeb905367080006f7cee1ff088f16db150c6ad19bb7790d24e8` | 读取JSON；执行命令见下方复现步骤 |
| E-002 | 逐进程与ABI核验 | [raw/cases/fixture-result.json](validation/foundation-winhttp-forward-20261025/raw/cases/fixture-result.json) | `2abfa1b194e9b4abab444c2787097231f5f3f6ff294a820448f4ddbf2f64d9c3` | 重新运行独立宿主矩阵 |
| E-003 | PE导入导出与真实系统文件 | [raw/cases/pe-proof.json](validation/foundation-winhttp-forward-20261025/raw/cases/pe-proof.json) | `abb28846ca7415e71f66b57f1112e59cfc9357aa74d2b24b71eca87248eb6e8c` | Test-WinHttpForwardFixture.ps1 自动执行 inspect |
| E-004 | TLS/CRT失败与修复 | [importer-initializer-review.md](validation/foundation-winhttp-forward-20261025/importer-initializer-review.md) | `d8bab5e1d3b53e42473085d4e00613f971ea5ffc5611808fae04f9bf4ac4817a` | 读取保留COFF与审查中的实际段/符号/重定位 |
| E-005 | 613声明源码身份 | [source-final-manifest.json](validation/foundation-winhttp-forward-20261025/source-final-manifest.json) | `96c7b39379ca732c934ec7828b2f5020bbd988c86b2d9b2ee77e96231f8b052a` | 对照 source-final-inputs.zip 逐文件计算SHA256 |
| E-006 | 最终归档与范围 | [summary.json](validation/foundation-winhttp-forward-20261025/summary.json) | `dff1132f82d0b225a9598e782a3510e6bb5f313f77e2022a04763a340ef2acc8` | 读取 summary.json 并对照 raw 与成功attempt |

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

系统 WinHTTP：SHA256 `2509edaea64d4ed9e9993b80fa370f431f37117401ef77cf367533a31b82c180`，1253704 B。四个交付形态及哈希见 [binary-manifest](validation/foundation-winhttp-forward-20261025/binary-manifest.json)；[源码ZIP](validation/foundation-winhttp-forward-20261025/source-final-inputs.zip) SHA256 `ab5337683eb06df481a5101afadf3f6d4df04b10406d547fe46c431e28817ecc`。未重跑此前25套1617，未安装新包，不发生产V4凭据，不宣称EngineCancelled或资源全局退休。后续按 [production-next.md](validation/foundation-winhttp-forward-20261025/production-next.md) 接入生产启动链，再处理对象、GC、调度与整局流程。
