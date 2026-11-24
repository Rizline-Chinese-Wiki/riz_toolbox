# Doorstop 冷启动转发接线实现与验证

后续勘误：生成 profile 的枚举类型与缓存字段偏移应为 type `0x18`、cache `0x20`；实际 Kernel 一直按此正确读取。旧提取器只查数字存在而未绑定字段类型，修正后的提取器增加精确类型/赋值核验。旧626归档保持原时点，修正及四份旧原件见[勘误](validation/foundation-engine-publication-20261025/history/profile-cache-type-erratum/erratum.json)。这不改变本批真实冷启动/转发运行结果。

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
| E-001 | [真实终态](validation/foundation-doorstop-cold-20261025/terminal.json) | `987f86e279af25f9558e901781c07c1f41b4252cf5193914cbbd2bf3189cadd8` | 只读终态；正式命令见attempts/r3 |
| E-002 | [5进程原件](validation/foundation-doorstop-cold-20261025/raw/cases/fixture-result.json) | `47f1dc9abd32e03a630a3d0e48491ccd7e5b15438456505dc5ad1a7c7aecfa5d` | Test-WinHttpColdFixture.ps1，需新输出目录 |
| E-003 | [PE/MAP/静态依赖证明](validation/foundation-doorstop-cold-20261025/raw/cases/pe-proof.json) | `4112fe8e61bfbb9e78517519065c20f042f6d80da35f22b6ece736b0ff87042f` | verify-cold.py --inspect --binary；只读、不加载目标 |
| E-004 | [交叉审查](validation/foundation-doorstop-cold-20261025/fixture-cross-review.md) | `f5d9fd2c579ddc31954aa63c86577b5cfd41055c27a82d186b2f973cff7bf7d6` | 只读源码及原件；审查者是caller作者，非外部审计 |
| E-005 | [完整汇总](validation/foundation-doorstop-cold-20261025/summary.json) | `1f8a51e6cf0d66d6ea4d02c20f208fcb6247465379a7550e101789734de62c71` | 原件及ZIP逐项重算SHA |
| E-006 | [固定游戏事实](validation/foundation-doorstop-cold-20261025/research/engine-game-profile.json) | `bff37c73acbd5775b9eb75e59e24e4094d32192b026121d121d742ec8a97430e` | extract-engine-game-profile.py，仅磁盘读取 |

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

[626源码ZIP](validation/foundation-doorstop-cold-20261025/source-final-inputs.zip) SHA `9375dd8195c33adc16ba38897e3715137f2ad59661a59c35343629f7834d757f`；候选对应LGPL源码和完整说明随raw/binaries/doorstop保留。旧613源中4项修改、13项新增，8二进制/585运行构建原件归档，2780个提取工具文件保留hash并由上游归档锁定。23旧包、18安装文件和4原始文件均保持，未重跑此前25套1617、未安装或启动游戏。

接下来按[生产接续](validation/foundation-doorstop-cold-20261025/production-next.md)实现独立V4 owner和已加载Kernel的CLR只读采用，再补对象/GC/coordinator/scheduler/取消与资源全局退休以及完整游玩流程。EngineCancelled和RequestPlay准入仍按已有保守合同；B01–B19继续active。
