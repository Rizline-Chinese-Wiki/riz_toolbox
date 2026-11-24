# 独立 Kernel 入口与磁盘表复核

正式 full 23528 的入口证据通过有限只读复核。结论来自本批正式归档 DLL、最终字段名 `mappedHeaderImageBase` 的实际 child 原件、父 runner 与离线 reader，没有沿用预检作为正式结果。实际 128 个入口、256 次拒绝、四张只读表与 128 项 DIR64 均一致，正式包内 Kernel 与被验证 DLL 同哈希；未发现本审查范围内的新阻断点。这是独立运行核入口合同的验收，不代表实际游戏底层全部完成。

## 证据范围

范围继承本批 [scope.md](scope.md)：root 唯一构建和运行自有宿主；本次审查只读文件，不构建、运行 child、加载游戏、安装或修改 599 个冻结输入。入口宿主由本审查代理编写，故对其实现属于作者自检；对父 [Test-EngineNodeKernel.ps1](../../../scripts/Test-EngineNodeKernel.ps1)、[verify-engine-kernel.py](verify-engine-kernel.py)及其真实输出属于有限交叉审查。

正式执行终态来自 [build-run.json](build-run.json)：root 实际观察 session 23528 terminal exit 0；[build-summary.json](build-summary.json) 为 25 套、1617/1617。正式 cache 为 `D:/RizToolbox-build-cache/engine-kernel-full-final-20261025`，原件归档于本批 `native-build/`；入口子目录实际名为 `entries`。本次将 cache 与归档的 Kernel、入口 EXE、入口结果、父 proof 和 runner 结果逐份比较，五份均同哈希。

| E-id | 实际原件 | SHA-256 |
|---|---|---|
| E-001 | [实际入口结果](native-build/engine-node-kernel-cases/entries/fixture-result.json) | `cd1923315339e7ee9f6473a72006eb795fbcaee885b95d24c441991e78d80421` |
| E-002 | [正式 Kernel DLL](native-build/engine-node-kernel/RizToolbox.EngineNodeKernel.dll) | `923486d2901ab6f66ea6d90d99cc65b1afafbc3275c338f0fd71ea5d22bc5662` |
| E-003 | [独立父入口 proof](native-build/engine-node-kernel-cases/kernel-entry-proof.json) | `877472589e3b115a7ce2eae9d9818c3890df3a64dcab56868c8f21ef5f20ec58` |
| E-004 | [正式 Kernel runner 结果](native-build/engine-node-kernel-cases/fixture-result.json) | `590e002910c449315c8aafa29b66593745113688dea49cf8e5ea8dcbaea77620` |
| E-005 | [KernelEntryFixture.cpp](../../../native/RizToolbox.EngineNodeKernel/KernelEntryFixture.cpp) | `31de496a18ce7578d380ed98d0d2fbd2a9f337d2796a9a96600062824b2d3f55` |
| E-006 | [父 runner](../../../scripts/Test-EngineNodeKernel.ps1) / [独立 reader](verify-engine-kernel.py) | runner `6e6e856be53e581fb70409c894457ef72d1d1f264d79fc17c757d825c61f66f0`；reader `6977e6fd14ddc086bfa82f14adbb6e4c1e29623bf8b984a05145cb6491c26c38` |

正式入口 EXE SHA-256 为 `7412781715c577a5c6231d09f2479425db9b8e3fd88858b509f867503a289ad3`。本次复核使用 Python 3.14.0、pefile 2024.8.26 和磁盘字节读取，没有通过运行 verifier 或 fixture 来制造第二次子进程结果。早期 [预检终态](history/runner-preflight/run.json) 仅保留为历史，不是本表的证据来源。

## 实际入口与拒绝结果

E-001 的 `passed=true`、`finished=true`、`checks=1606`、`failures=[]`、`sehCode=0`、`witnessCalls=0`。[入口 process 原件](native-build/engine-node-kernel-cases/entries/process.json) 为退出 0、未超时，且 stdout/stderr 为空。逐行检查没有只使用其总通过标志。

| 已复核项目 | 实际数量及内容 |
|---|---|
| 导出/API | 8 个 GetProcAddress 地址与 E-002 磁盘 EAT RVA 一致，全部来自该加载映像的 RX 页 |
| ABI | transport 2、limit 32、frame 112、completion 32 |
| typed 入口 | 4×32 共 128 个非零互异地址；地址减 actual module base 等于磁盘 executable、非 writable 节中的 RVA |
| 实际页属性 | 128 入口均 `MEM_COMMIT`、`MEM_IMAGE`、`PAGE_EXECUTE_READ=0x20`，allocationBase 为同一精确 Kernel 模块 |
| Fresh 信息 | 512 份：初始 128、每次拒绝后 256、最终 128；均 status 2，真实 kind/slot/entry，未配置、未附加且活动/故障计数全 0 |
| IsEntry 拒绝 | 每个实际入口分别填 Configure.target 与 original，共 256 次；另一参数非零、不同且不是入口，目的 kind 为源 kind 的下一种，均返回 1、保持目的槽 Fresh |
| 非法坐标 | 12 组，每组 GetEntry 返回 0，GetInfo/Configure/Attach 都返回 1，共 48 个拒绝返回检查 |
| 原函数/观察者执行 | witnessCalls 为 0；没有调用未配置 typed 入口，没有发布 hook |

非法坐标 GetInfo 的输出仍为输入 `0xa5` 毒值，只有返回码具有意义；这些字段不属于真实 Info 或上述 Fresh 的 512 份记录。不能把拒绝前尚未写出的 kind/slot/counter 当成 Kernel 状态。

## mapped header 与磁盘重定位

E-001 本次实际加载地址为 `0x7ff813110000`，`module.mappedHeaderImageBase` 也为该值。另从 E-002 磁盘 OptionalHeader 独立读取的 ImageBase 为 `0x180000000`，真实加载差值为 `0x7ff693110000`，不是 0。

此前首个描述符样本曾把加载后头字段命名为 `preferredBase`。只读复核发现它与磁盘值不同后，已最小更名为 `mappedHeaderImageBase` 并加来源注释；旧原件没有改写。本次新版 JSON 不含旧键。这个修正是证据字段解释修正，不是入口执行逻辑修正。

以下 RVA 是本次两侧独立比对的结果，没有加入宿主或 reader 的固定偏移：

| kind | 唯一只读 `.rdata` 表 RVA | 指针数 | 首/尾入口 RVA | 表指针 DIR64 |
|---|---|---:|---|---:|
| Start 0 | `0x9020` | 32 | `0x1000` / `0x2930` | 32 |
| Execute 1 | `0x9120` | 32 | `0x2a00` / `0x4330` | 32 |
| Release 2 | `0x9220` | 32 | `0x4400` / `0x5d30` | 32 |
| Detach 3 | `0x9320` | 32 | `0x5e00` / `0x7730` | 32 |

本次另用 pefile 读取磁盘 section bytes 和 base-relocation 目录，按每类实际 GetEntry 减 actual base 得出的 32 个 RVA，使用磁盘 ImageBase 组成完整 256 字节指针序列，在只读、非执行、非写入磁盘节中扫描；四类各只有一个命中。128 个表槽逐项满足：磁盘值等于 `disk ImageBase + entry RVA`；宿主实际读取值等于 `actual base + entry RVA`；每个表槽都有其独立 DIR64 项。没有用 JSON 的 mapped header 代替磁盘 ImageBase。

## 父验证器的独立性

E-006 的 reader 在 `inspect_pe` 从文件字节取 OptionalHeader ImageBase。`verify_entries` 第 230–231 行重新读 DLL 并核哈希，第 300–312 行用该磁盘基址和真实入口 RVA 拼完整 `<32Q` 模式并扫描磁盘只读节；第 323–328 行再读取每个槽的磁盘 8 字节、核 DIR64 集合及实际映射指针。宿主自报表 RVA 只有与独立唯一候选一致才能通过。它不是仅计数 `staticEntryTablesVerified` 或照抄宿主表地址。

父 runner 第 53–60 行使用 `FileShare.Read` 保持声明输入句柄，核 build manifest 的精确路径/名称/哈希；第 97–131 行先静态 PE 验证、再等待真实入口 child 终态和结果、最后运行独立表验证。结束时重算 held-stream 哈希。E-004 实际 held 输入 31 份在 Kernel runner 运行前后全部相等；入口父 proof 实际断言 1486 项。这是同批固定输入下的独立磁盘解释与实际地址对照，不意味着操作系统全部依赖或生产加载路径已经验证。

E-004 还记录 1 个原生回归进程通过 3402 项检查、136 次原调用，以及 5 个托管联合进程通过 889 项检查、44 次原调用。加上入口进程是 7 个自有子进程。本次有限入口审查没有逐条重新审计这 136/44 次原调用的全部业务观察，不把父汇总延伸成实际游戏结果。

## 正式输入、包身份与后续输出覆写

本次逐项读取 [source-manifest.json](source-manifest.json)、[build-inputs-before.json](build-inputs-before.json)、当前 599 个声明源文件和 [source-inputs.zip](source-inputs.zip)，路径/数量/哈希全部一致；另外逐项读取 [package-manifest.json](package-manifest.json) 与 [staged-package.zip](staged-package.zip)，23 项全部一致。源 ZIP SHA-256 为 `a48d274857eb0b86a2c8530611282b374301e6ed73df17bdea537ccf8ef77532`，包 ZIP 为 `cbcb7f4b07f451717106435196f1f871040ae5ef45a9a5b97af1cc5a75067d6d`。

包内 `BepInEx/plugins/RizToolbox/RizToolbox.EngineNodeKernel.dll` 的实际解压字节与 E-002 同为 `923486d2901ab6f66ea6d90d99cc65b1afafbc3275c338f0fd71ea5d22bc5662`。因此被验证 Kernel 与本次声明包是同一文件身份。包是 staged 产物，未安装到游戏。

[kernel-post-run-files.json](kernel-post-run-files.json) 区分两个时间点：31 个 held 输入在 Kernel runner 结束时均未改变；随后统一构建继续执行 `Test-FirstInstallManaged.ps1`，第 29 行 Copy-Item、30 行校验，将 Runtime.Tests 输出目录中的旧 `RizToolbox.JudgeContext.dll` 副本从当时的 `01e5171d2e7a824308e7e9053f6959fa7cbea98bb2c2742e1571d8bf75a66f48` 覆写为本次旧 helper 的 `398d1c1ba90cbf55442c42a0b2b0197804b82ef1f575f0a26ee7942063809ec9`。这是 runner 释放锁以后的一个构建输出副本变化，不是 599 源文件漂移，也不是 Kernel DLL 改变。

[kernel-supplied-files.json](kernel-supplied-files.json) 与 `native-build/engine-node-kernel-supplied-files/` 保留了当时的 19 份实际供应二进制/托管输出，本次全部逐项核过。上述 `01e517…` 旧 helper 从前批 73 宿主的已归档原文件恢复，恢复文件与原来源都保持完全相同 SHA-256；没有用后来的 `398d…` 去改写历史 held-file 证据。当前输出副本与 post-run 记录的后续值也一致。工具链、Python/.NET/Windows 等仍是外部前置条件，不能把这 19 文件称作所有系统依赖闭包。

## 发现与剩余路径

| F-id | 结论 | evidence_ids | severity / confidence / status |
|---|---|---|---|
| F-001 | 新版最终字段 schema 区分实际映像头与磁盘 preferred，非零重定位情况下的 128 个表槽仍逐项一致 | E-001, E-002, E-003, E-005, E-006 | n/a_re / high / observed-final |
| F-002 | 128 个入口的 target/original 指针均在跨 kind 配置前被拒绝，目的永久槽未被占用 | E-001, E-005, E-006 | n/a_re / high / observed-final |
| F-003 | 父 reader 实际独立读取磁盘四表及 128 DIR64，而非仅接受宿主自报 | E-002, E-003, E-006 | n/a_re / high / reviewed-final |

`P-001`，`path_type=callflow`：root 以 599 冻结输入正式构建固定身份 Kernel（E-002）→ descriptor child 读取全部入口/状态及拒绝路径（E-001，F-002）→ 父读取磁盘 preferred、四段 literal pointer 和 relocation，连接 actual 地址（E-003，F-001/F-003）→ 本次从正式归档及声明包核对同一 DLL 身份并保留两个时间点的供应文件证据。后续仍需单独实现与证明生产加载及实际游戏接线。

范围尚未覆盖实际游戏首发、完整静态依赖链、WinHTTP 系统转发执行、生产 helper/Kernel 接线与 V4 采用、实际 IL2CPP/GC、coordinator、scheduler/async/Start 内联清理、取消或退休。入口宿主特意不调用未配置 typed 入口；它的 1606 项不证明四种原函数的全部执行语义，也不单独证明底层已经完成。

时间线：2026-10-25 首样本实际 1606 通过后发现 mapped/disk 基址解释差异 → 保留旧原件、最小更名后冻结源码 → root 新版预检实际退出 0 → 599 输入正式 full 23528 实际 terminal 0 → 599 源/23 包和新 Kernel 原件归档 → 本次只读复核正式样本、四表重定位、held 时点与后续覆写、包同一身份。审查结论限定本页入口合同范围。
