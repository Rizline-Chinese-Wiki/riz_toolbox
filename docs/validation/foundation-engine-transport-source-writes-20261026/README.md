# 固定传输层写源组件：私有映像验证记录

日期：2026-10-26。报告类型：普通逆向与工程验证，`flavor = null`。

本批已实际验证固定事实驱动的 relay 字节准备，以及在自有 `MEM_PRIVATE` 映像中提交 36 个物理窗口的写源组件。最终 r4 完成 226/226 场景，子进程 79,756 项检查、父进程 27,827 项检查均通过；保留 r1/r2/r3 的独立历史结果。测试真实调用保护变更、内存写入、读回、保护恢复、缓存刷新和函数表登记/删除；216 个故障边界由夹具显式注入，不代表 Windows API 实际失败。**组件仍是私有实现，`Complete` 仅表示这次写源责任完成；生产 profile 2 的门继续为 false，没有发布 Ready，也没有安装或执行游戏。**

## 范围与授权

本页范围是用户 Rizline PC 兼容底层建设任务中的自有验证进程；上层设计边界见[下一步生产传输设计](../foundation-engine-wait-20261026/next-production-transport.md)。本页自 r3 记录更新至 r4，旧轮次的原始证据保留，没有改其他验证报告或 `PROGRESS`。

- 固定磁盘输入：`D:/SteamLibrary/steamapps/common/Rizline/UnityPlayer.dll`，31,006,120 字节，SHA256 `43fb0aea1b80c74963dae88ec7fc1c3c1993d893ce94d12972000fcc9b653ad1`。读取锁、运行前后哈希和夹具内实际 SHA256 均一致。
- 游戏 DLL 没有通过 `LoadLibrary` 加载；夹具按 PE 头和各 section 将磁盘字节复制进自己 `VirtualAlloc` 的私有映像。写入对象是这些私有页，磁盘原文件始终只读。
- 写源夹具会把私有 source/near 页改为 RX，以满足实际 `Commit` 的内存前置条件；**没有执行这些映像或 relay 字节**。Prepare-only 夹具则始终保留非可执行页，不能把两者的保护状态混为一谈。
- 16 个 literal entry 是独立 EXE 内的不同函数地址，检查其真实 `MEM_IMAGE`/RX 属性，作为准备和写源验证的目标字面量。没有调用这些目标，也没有证明真实 Kernel family 身份。
- 不调用生产 installer，不 Configure/Attach family，不改 V4，不开放生产准入、游戏执行、未来生产者关闭或取消完成权限。

## 实际结果和保留的失败

所有时间取原始 manifest 的 UTC 字段；北京时间为 UTC+8。失败目录、日志和 ZIP 均保留，没有将失败改写成通过。

| 证据 | 时间窗口 UTC | 实际结果 | 主要记录 |
|---|---|---|---|
| E1 写源 r1 | 2026-10-26T12:41:40.0284396Z → 2026-10-26T12:41:49.3935682Z | 严格编译 exit 1；C4389 被 /WX 提升为错误；没有启动子进程 | [manifest](D:/riz-build-cache-next/engine-transport-sourcewrites-r1-20261026/test-result.json) / [build.log](D:/riz-build-cache-next/engine-transport-sourcewrites-r1-20261026/build.log) |
| E2 写源 r2 | 2026-10-26T12:42:06.6654609Z → 2026-10-26T12:42:30.3918944Z | PID 63572，exit 0，224/224；child 71,467，parent 27,350 | [manifest](D:/riz-build-cache-next/engine-transport-sourcewrites-r2-20261026/test-result.json) / [build.log](D:/riz-build-cache-next/engine-transport-sourcewrites-r2-20261026/build.log) / [child](D:/riz-build-cache-next/engine-transport-sourcewrites-r2-20261026/child-result.json) |
| E3 写源 r3 | 2026-10-26T12:43:30.3461427Z → 2026-10-26T12:43:56.1615377Z | PID 23012，exit 0，224/224；child 79,279，parent 27,350 | [manifest](D:/riz-build-cache-next/engine-transport-sourcewrites-r3-20261026/test-result.json) / [build.log](D:/riz-build-cache-next/engine-transport-sourcewrites-r3-20261026/build.log) / [child](D:/riz-build-cache-next/engine-transport-sourcewrites-r3-20261026/child-result.json) |
| E4 Prepare r2 | 2026-10-26T12:30:54.3288043Z → 2026-10-26T12:31:04.0922370Z | PID 66976，exit 0，14/14；child 554，parent 41；只准备字节 | [manifest](D:/riz-build-cache-next/engine-transport-relay-preparation-r2-20261026/test-result.json) / [build.log](D:/riz-build-cache-next/engine-transport-relay-preparation-r2-20261026/build.log) / [child](D:/riz-build-cache-next/engine-transport-relay-preparation-r2-20261026/child-result.json) |

| E5 写源 r4 | 2026-10-26T13:16:27.2097112Z → 2026-10-26T13:16:58.3655081Z | PID 15840，exit 0，226/226；child 79,756，parent 27,827 | [manifest](D:/riz-build-cache-next/engine-transport-sourcewrites-r4-20261026/test-result.json) / [build.log](D:/riz-build-cache-next/engine-transport-sourcewrites-r4-20261026/build.log) / [child](D:/riz-build-cache-next/engine-transport-sourcewrites-r4-20261026/child-result.json) |

r1 的实际错误位于自有夹具 `Mapping::Memory` 的有符号/无符号比较，见其 `build.log`。r2 将比较类型明确为 `DWORD` 后严格构建与全矩阵通过。r3 又给 writer 增加操作系统页大小驱动的 `protectionBase/protectionBytes` 记录，并由夹具对 217 个主矩阵场景的每个 36 行分别独立计算、核对实际页面范围，新增 7,812 项子检查，因此 71,467 → 79,279。该计数变化不能回填到 r2。

r2/r3 的完整通过均包含：正常场景提交 36 个窗口、合计写 380 字节；全矩阵累计实际写入私有映像 47,606 字节；每个子进程实际登记 223 个动态函数表并成功删除 223 个，随后才释放相应自有分配。`table-missing` 场景没有登记表，它的 `tableDeleted=true` 仅表示没有残留表责任，不计作第 224 次删除。

## 226 个场景如何覆盖实际责任

r4 保留原主矩阵：1 个正常场景，加 6 个边界 × 36 个物理窗口，共 217 个；预检/终态场景从 r2/r3 的 7 个扩为 9 个，最终合计 226 个。36 个窗口包括 13 个 typed prefix 与 23 个 side site，不能称为 36 个函数。

| 场景边界 | 数量 | 实际动作和有意注入的差别 |
|---|---:|---|
| 正常 | 1 | 全部 36 窗口真实保护变更、完整写入、实际读回、恢复 RX、实际缓存刷新；最终 `committedWindows=68719476735`、`sourceWriteCompletions=36`、outstanding=0 |
| `ProtectBegin`，stage 2 | 36 | 当前窗口的保护调用由故障开关跳过，返回模拟 ProtectFailed；此前窗口的真实提交仍保留 |
| `Protected`，stage 3 | 36 | 当前窗口真实改为可写后注入失败；Abort 真实恢复保护，不写该窗口 |
| `WriteBegin`，stage 4 | 36 | 当前窗口真实只写 1 字节后模拟 WriteFailed；精确保留 1 字节进度并真实恢复保护 |
| `Readback`，stage 6 | 36 | 当前窗口完整真实写入后，夹具将第一个字节 XOR 1；实际读回发现差异并真实恢复保护；不是声称硬件读回自然损坏 |
| `Restored`，stage 7 | 36 | 在正常恢复边界前注入 RestoreFailed；Abort 随后真实恢复 RX 成功；不宣称实际 VirtualProtect 恢复失败 |
| `Flushed`，stage 8 | 36 | 当前窗口已真实读回并恢复 RX，缓存刷新调用由开关跳过后模拟失败；此前窗口的 FlushInstructionCache 已真实成功 |

每个失败目标窗口之前恰有 `faultWindow` 个已提交窗口；后窗保持原字节。夹具独立核对整个私有映像，而非只看状态位：已提交区等于 patch、部分写区仅首字节改变、注入读回损坏区等于明确的损坏字节、其余字节保持输入原件。每个 case 的 36 行实际字节十六进制、protect/restore/flush 进度、实际写入量、事件次序、位图和结果均保存在 child JSON；各 case 的计数由执行后得到。

7 个预检/终态场景为 `bad-request`、`bad-plan`、`bad-source`、`production-gate`、`table-missing`、`source-not-RX`、`nonfresh-receipt`。它们的源写入尝试/完成数均为 0；其中 profile 2 实际返回 `AdmissionUnproven`。`source-not-RX` 由夹具先把一页改为 READONLY，验证 Commit 保持其预检状态，再由夹具显式恢复 RX；其 `fixtureResetPreflightProtection=true` 不计入 writer 恢复成功。

r4 在上述原 7 个之外新增两个已实际执行的预检反例；两者各有已登记的函数表，实际 near 字节在调用前后都与 caller 的 Plan 一致，仍返回 `BadPlan=3`，`sourceWriteAttempts/sourceWriteCompletions/sourceBytesWritten/committedWindows` 全为 0。所有源页保护保持 RX、LastError 保留、终态第二次 Commit 不改变 receipt，实际函数表删除成功。

| r4 新增反例 | 实际构造 | 原始 child 事实 |
|---|---|---|
| `coherent-relay-tamper` | site0 relay+6 的实际 `FF 4B 60`（DEC3 字节）与 `Plan.preparedBytes` 对应字节同步改为 `90 90 90`，改写过程只针对自有 near 页，随后恢复 RX；原 CALL/literal/RF 不变 | `coherentRelayTamper=true`、`returnPcAtRfEnd=false`、`actualNearMatchesPlan=true`；128 项 case 检查；实际 BadPlan、0 源写 |
| `returnpc-at-rf-end` | 将 site0 `relayReturnPc` 实际改成 `relay+codeBytes == fixedImageBase+RF.endRva`，测试 exclusive end 的非法边界 | `returnPcAtRfEnd=true`、`coherentRelayTamper=false`、`actualNearMatchesPlan=true`；125 项 case 检查；实际 BadPlan、0 源写 |

只比较 caller Plan 与 near 内存会让同步篡改自洽。r4 的 `Relays::VerifyPrepared` 使用私有 BSS `canonicalPlan`，通过非等待 Interlocked gate 在忙时立即返回 `Busy`；从已固定事实和实际原件 bytes 重新生成完整 metadata 与全部 6,400 字节，调用 `PrepareCore(..., false)`，不写 near 或 source。仅规范化成功拷贝对应的三个进度字段，再逐字节比较整个 Plan 和实际 near，不能让两个一起被改的副本互相作证。该 gate 的并发 Busy 路径有源码边界，本轮未声明额外并发实测。

writer 在进入任何源保护变更前调用此独立重发核验，并要求真实返回 PC 严格小于 `relay+codeBytes`；登记核验改为对 `relayReturnPc` 本身调用 `RtlLookupFunctionEntry`，而不是只看 `returnPc-1`。新 `returnpc-at-rf-end` case 会首先被 canonical metadata 比较拒绝，因此它证明非法 end 候选整体被拒绝；不能声称它单独隔离验证了后继 strict-bound 条件。正常路径则实际核真实 return PC 的登记范围。

r4 仍保留正常 36 窗口/380 字节与主矩阵 47,606 字节总写入量；两新预检没有增加源写入。新增 2 次 actual table Add/Delete，最终 225/225，不能改写 r2/r3 的 223/223 历史。19 个 chain 与 4 个 Dispatch partial unwind 字节合同保持不变；canonical 重发通过也不代表实际 relay 已执行。

每个 case 都实际再次调用 `Commit`：已完成、已失败或非空 receipt 返回 `InvalidInput`，整个 receipt 逐字节不变；调用者的 LastError 保持。失败没有回滚已经写入的源码字节，保护恢复也不等于代码回滚。自有夹具只在真实 `RtlDeleteFunctionTable` 成功后释放其分配；这不是对未来生产永久 owner 的卸载/释放授权。

## Relay Prepare r2 与 Dispatch 部分展开修复

E4 的正常准备结果为 23 sites、36 windows、23 RF、6,400 字节 near 输出，near 最低容量 8,192 字节；余下容量保持 A5 sentinel。14/14 包括缺 entry、重复 entry、范围/容量、完整 body/RF/unwind/PE/section 属性、不可读 image 和真实第 2 页 NOACCESS 导致仅写成功 4,096 字节的场景。

Dispatch 的 site index 17–20 位于原函数 offset `1C/21/2F/3A`，早于原函数 offset `4E` 才完成的 RBX 保存。若无条件 CHAININFO 链到完整 source prolog，外部 relay PC 会错误应用尚未执行的 RBX 保存。因此最终字节准备为 **19 个 CHAININFO + 4 个独立部分 unwind**；四个 Dispatch record 均为 12 字节，slot 剩余 4 字节为零：

```text
01 00 03 00 00 01 2A 00 00 70 00 00
```

含义为 Version 1、Flags 0、Prolog 0、CountOfCodes 3、无 frame register；CodeOffset 0 的 `ALLOC_LARGE 150h` 和 `PUSH RDI`。进入 relay 前这两项已执行，不能复制 source 的 2/9 偏移，也不能纳入晚到的 RBX 保存。Prepare 夹具同时核对固定和实际 mapped source unwind 字节、各 site 物理区间及实际 near 输出的 19+4 分布。

**E4 只验证准备出的字节和事实，未登记函数表、未调用 relay、未执行 `RtlVirtualUnwind`。** 写源 r3 又实际登记并用 `RtlLookupFunctionEntry` 校验这些表项及 unwind 字节，但仍未执行 relay 或证明其逐 PC 机器展开。不能把它们与此前九个 original 桩的独立执行验收合并称为“23 个 relay 已实际通过”。

## Evidence → Finding → Path

各 Evidence 的 `source_type=file/log`；`source_ref` 为上表和下表链接，`observed_at` 为对应 manifest 时间；`linked_workitem=P1-private-source-writes`。r2 对 r1 的编译结果、r3 的新增页面责任检查、r4 的 canonical 重发与两反例构成递进，不覆盖旧原件。

| Finding | 状态 / 置信度 | Evidence | 具体结论及位置 | 下一路径 |
|---|---|---|---|---|
| F1 私有映像写源矩阵 | validated / high | E2、E3、E5 及其各自 8 源 ZIP | `RizEngineTransportSourceWrites::Commit` 在 owned profile 中完成 36 窗口写源及精确失败责任记录 | P1：保留此组件，接入未来 V5 owner 前仍需独立模块、family、启动窗口和永久资源证据 |
| F2 Dispatch 只准备 active unwind | validated（字节范围）/ high | E4、E3 与固定 source unwind | 19 chain + 4 partial 正确反映本批字节合同；尚未签运行时机器展开 | P2：自有真实调用携带尚未保存的 RBX/毒化 home slot，逐 PC 取真实 CONTEXT 验证，再覆盖 3 个 expanded zero/nonzero 路径和全部 relay CALL 保真 |
| F3 生产门保持拒绝 | validated / high | E3/E5 的 `production-gate` case、固定 `ProductionFirstInstallAuthorized=false` 与 Core gate 源码 | profile 2 返回 AdmissionUnproven，源尝试为 0；组件没有 Ready API | P3：继续全 family 配置/采用和完整依赖、固定启动窗口证据；不得从本页通过数推导生产权限 |
| F4 两份一致副本不等于固定 replay | validated / high | E5 两个新实际反例、独立 re-emit 与真实 return-PC 登记源码 | 同步 NOP 篡改与 end-boundary 候选在保护/源写前 BadPlan；验证只发生在 owned 映像 | P1：保留 canonical 校验和 exclusive RF 边界，再做后继真实 relay 机器执行，不能以 caller 自洽取代固定事实 |

P1（`path_type=callflow`）：只读固定磁盘 SHA → 私有 PE 映射 → actual Prepare → 实际 RtlAddFunctionTable → source/near RX → Commit → 逐字节/页范围/事件/终态核验 → 实际 Delete → 释放自有映像。各步对应 E3/E5；r4 在 Commit 预检增加只读 canonical 重发。目标仅为有界 owned source-write 验收。

P2（`path_type=callflow`）：固定源 unwind 与 site offset → 19+4 字节准备 → 后续真实 relay/native Mid 调用与异常/展开验收。前两步对应 E4/E3；后一步尚未完成，是 residual risk，不能标记为验证通过。

## 冻结、归档与完整性

本页整理时重新只读核验了全部五个目录：manifest 中声明的输出文件逐个存在、字节数与 SHA 均一致；ZIP SHA、成员数、无重复成员和每个源成员的字节/SHA 与 `sources`、`source-before`、`source-after` 完全一致。写源四轮 ZIP 各 8 项，Prepare ZIP 为 5 项，均不包含游戏磁盘文件；游戏只记录并核对独立的 SHA/字节数。

输出文件核验数分别为写源 r1 的 7 项、r2/r3/r4 各 19 项、Prepare r2 的 17 项。r2/r3/r4/Prepare 的 stdout 与 persisted child JSON 一致，child 与父 manifest 内嵌记录、PID、checks、结果一致；r1 没有 child。以下 SHA 均来自实际文件，不是目录名推断。

| Evidence 文件 | SHA256 |
|---|---|
| [E1 写源 r1 test-result.json](D:/riz-build-cache-next/engine-transport-sourcewrites-r1-20261026/test-result.json) | `5d019e7648b8d86e42c3f226d209242b11bd7928c871ff844158c48c40f1a4cb` |
| [E1 写源 r1 source ZIP](D:/riz-build-cache-next/engine-transport-sourcewrites-r1-20261026/source-write-fixture-sources.zip) | `bd7d9f8efecdf686414492bc482728d2bf96c7de0f4b1302bbf9f2d5afbea2bc` |
| [E2 写源 r2 test-result.json](D:/riz-build-cache-next/engine-transport-sourcewrites-r2-20261026/test-result.json) | `5faf555b0559bda9e3dbdadf06976095267d660b31774eda51b8eec508008ac2` |
| [E2 写源 r2 child-result.json](D:/riz-build-cache-next/engine-transport-sourcewrites-r2-20261026/child-result.json) | `8d55d3b85d4c311a53a86dd61ced589a8d4336c95de00bd06f8e48b63b69cd06` |
| [E2 写源 r2 source ZIP](D:/riz-build-cache-next/engine-transport-sourcewrites-r2-20261026/source-write-fixture-sources.zip) | `1bc6d0ed1393527c301a8afa8d272f000720104c35a6a42ff01e8e2ff8d36f08` |
| [E2 写源 r2 owned EXE](D:/riz-build-cache-next/engine-transport-sourcewrites-r2-20261026/EngineTransportSourceWrites.Fixture.exe) | `2e051f824cab72622ad4f263f89d61a037145e24f523b0d2e3a686fb8c0f1bd9` |
| [E3 写源 r3 test-result.json](D:/riz-build-cache-next/engine-transport-sourcewrites-r3-20261026/test-result.json) | `9c90a18b795db9d9950d18eda3255e519cdd378a1d6a6fca5120cd2783d626fe` |
| [E3 写源 r3 child-result.json](D:/riz-build-cache-next/engine-transport-sourcewrites-r3-20261026/child-result.json) | `95020afaf76130f3033d1757122e6b3645c9c447e3212e1cacfd6cb71da92066` |
| [E3 写源 r3 source ZIP](D:/riz-build-cache-next/engine-transport-sourcewrites-r3-20261026/source-write-fixture-sources.zip) | `10bbb86f06c1bcee9913b2435de03cdff58a60f4b076a75d5e39a3f2c83ec8cd` |
| [E3 写源 r3 owned EXE](D:/riz-build-cache-next/engine-transport-sourcewrites-r3-20261026/EngineTransportSourceWrites.Fixture.exe) | `fc3ccfe393cc38b94dfa2e732af6538c143b715675e485586232ba6e614645ba` |
| [E4 Prepare r2 test-result.json](D:/riz-build-cache-next/engine-transport-relay-preparation-r2-20261026/test-result.json) | `b675f9dfa5666a7c73420e9e403ad4d632f1d66eb6030cfc0ab25a37c287b06b` |
| [E4 Prepare r2 child-result.json](D:/riz-build-cache-next/engine-transport-relay-preparation-r2-20261026/child-result.json) | `8d21f50bb7aca02223fe75b6a311dfe323876b6e54f54f918bb683f9645203a0` |
| [E4 Prepare r2 source ZIP](D:/riz-build-cache-next/engine-transport-relay-preparation-r2-20261026/relay-preparation-sources.zip) | `5c9673d9ea2322541c36d913e090247112cb1df652535fe16ad6d06228ca74ea` |
| [E4 Prepare r2 owned EXE](D:/riz-build-cache-next/engine-transport-relay-preparation-r2-20261026/EngineTransportRelayPreparation.Fixture.exe) | `550cd7c8c25bc2eead0eb979a95d4bcfec631ada2370fd205fc8cf82381f9372` |

| [E5 写源 r4 test-result.json](D:/riz-build-cache-next/engine-transport-sourcewrites-r4-20261026/test-result.json) | `df500a8999109155ea6f55933c9f94717348ed371d1f3a3772ac9b51f7606ad1` |
| [E5 写源 r4 child-result.json](D:/riz-build-cache-next/engine-transport-sourcewrites-r4-20261026/child-result.json) | `5edc25cf4d44945c712b5e75fd950cd0b08f689617375264df3c477c1bcb1c41` |
| [E5 写源 r4 source ZIP](D:/riz-build-cache-next/engine-transport-sourcewrites-r4-20261026/source-write-fixture-sources.zip) | `ce57fc553f21aa1cb1c9c00280a18a71d6838afb946e6f960faec86ce5bdbb67` |
| [E5 写源 r4 owned EXE](D:/riz-build-cache-next/engine-transport-sourcewrites-r4-20261026/EngineTransportSourceWrites.Fixture.exe) | `7bb41696fd3fbef76c8e1f398511cf5dfdcea903e238694de92964210a9e5c57` |

历史写源 r3 的 8 个源输入如下；它们由 r3 ZIP 保全，r4 已改变其中部分 live 源，不能再声称 live 文件仍为 r3。后续修改须另生成 fresh 证据目录，不能用 live 文件替代历史 ZIP。

| 源文件 | 字节 | SHA256 |
|---|---:|---|
| [native/RizToolbox.EngineTransportSourceWriteFixture/SourceWrites.cpp](../../../native/RizToolbox.EngineTransportSourceWriteFixture/SourceWrites.cpp) | 27653 | `9e233d10f25714a3ca69dd4f2cdc53083540a655b3facc2e37cd6f912c887ee8` |
| [native/RizToolbox.EngineBootstrap/EngineTransportSourceWrites.cpp](../../../native/RizToolbox.EngineBootstrap/EngineTransportSourceWrites.cpp) | 21439 | `86dfba8515e77b81a8de3b4d308b9b9939d34206a60d0bc5a0688e6523b60350` |
| [native/RizToolbox.EngineBootstrap/EngineTransportSourceWrites.h](../../../native/RizToolbox.EngineBootstrap/EngineTransportSourceWrites.h) | 2608 | `45c78f3f74f0069d4cca879817d256ba0f34f24ab8fda092148965bc38c50354` |
| [native/RizToolbox.EngineBootstrap/EngineTransportRelays.cpp](../../../native/RizToolbox.EngineBootstrap/EngineTransportRelays.cpp) | 22507 | `4d1f7a7dc56d62db6bfaf5db707692a5b5110aff7af3244b2d668d4947b1b023` |
| [native/RizToolbox.EngineBootstrap/EngineTransportRelays.h](../../../native/RizToolbox.EngineBootstrap/EngineTransportRelays.h) | 4330 | `2e1e2e27489a6e5054c6ddf087bd863b8aedef9bc1fd646656f87c45773387bf` |
| [native/RizToolbox.EngineBootstrap/EngineTransportGameProfile.h](../../../native/RizToolbox.EngineBootstrap/EngineTransportGameProfile.h) | 80885 | `de2c9593aaf0d4ef11765ebf0521b2ba4a3a855af3f4420365cb497f534c213c` |
| [native/RizToolbox.EngineBootstrap/EngineTransportPublication.h](../../../native/RizToolbox.EngineBootstrap/EngineTransportPublication.h) | 20382 | `ee42b113e557ae49f39675e9475b58ea4472a7c4b325e1e6517f79e80b461035` |
| [scripts/Test-EngineTransportSourceWrites.ps1](../../../scripts/Test-EngineTransportSourceWrites.ps1) | 20636 | `f4f29ac2f20b69dd559c4cc7613c0943e158e1c599012925cd86e376e033cf4a` |

r1 夹具 SHA 为 `92af556d21eab4a753148f5a756fae6c2e0b2da15150eab64cd87017a5abd38c`；r2 修类型后的夹具 SHA 为 `3958ff3fc0c06abca525fdbc9389d753f23848e70a79cb34d2d41a646d0d230c`。两轮 writer SHA 均为 `92fa68781180b809bda37b62992aefcced580c4bb1a5ad9e2339e176c7d72a94`。r3 的页面责任补全同时改变 writer 与夹具 SHA，如上表；其余 6 输入保持一致。

Prepare r2 的专属夹具 SHA 为 `fae07e157022b7d9d146921647ef0c640aadf8ec6116ad0fc1059b332dda3228`，runner SHA 为 `14c73dc2086528c4095e874e89016a65d9da7048196db2c1a95f2daf69f7900f`，其共用 Relays h/cpp 与固定 Facts SHA 对应上述历史 r3 表；r4 的 Relays h/cpp 已升级 canonical 核验，不能倒填到 Prepare r2。

## 最终 r4 的独立冻结输入

本页更新时重新核验了 r4 的 8 源 ZIP 每个成员、`sources/source-before/source-after` 三方记录、全部 19 个声明输出，以及当前 live 8 源的 SHA/字节数；均与 r4 一致。游戏磁盘实际 SHA 仍为开头的固定值。以下表是当前 r4 结果的来源，不能用上面的 r3 表替代。

| r4 源文件 | 字节 | SHA256 |
|---|---:|---|
| [native/RizToolbox.EngineTransportSourceWriteFixture/SourceWrites.cpp](../../../native/RizToolbox.EngineTransportSourceWriteFixture/SourceWrites.cpp) | 29634 | `8ef761e71e4922921a2789e500ac0370d58caed00df2f0690a86ce4bf3c75616` |
| [native/RizToolbox.EngineBootstrap/EngineTransportSourceWrites.cpp](../../../native/RizToolbox.EngineBootstrap/EngineTransportSourceWrites.cpp) | 21923 | `49511047f8f33fd27041026379703d76eb2b3dc52392e15a60820e106fbed649` |
| [native/RizToolbox.EngineBootstrap/EngineTransportSourceWrites.h](../../../native/RizToolbox.EngineBootstrap/EngineTransportSourceWrites.h) | 2608 | `45c78f3f74f0069d4cca879817d256ba0f34f24ab8fda092148965bc38c50354` |
| [native/RizToolbox.EngineBootstrap/EngineTransportRelays.cpp](../../../native/RizToolbox.EngineBootstrap/EngineTransportRelays.cpp) | 24828 | `540a2a64f641230b7f82aee5e075555dfc4b3bb01bb05af0fa2bd84880280999` |
| [native/RizToolbox.EngineBootstrap/EngineTransportRelays.h](../../../native/RizToolbox.EngineBootstrap/EngineTransportRelays.h) | 4701 | `6058ef42159a11ccf497e5d00c4ee659b6cfdd10a7ea9a0445a3eab8a58c55e4` |
| [native/RizToolbox.EngineBootstrap/EngineTransportGameProfile.h](../../../native/RizToolbox.EngineBootstrap/EngineTransportGameProfile.h) | 80885 | `de2c9593aaf0d4ef11765ebf0521b2ba4a3a855af3f4420365cb497f534c213c` |
| [native/RizToolbox.EngineBootstrap/EngineTransportPublication.h](../../../native/RizToolbox.EngineBootstrap/EngineTransportPublication.h) | 20382 | `ee42b113e557ae49f39675e9475b58ea4472a7c4b325e1e6517f79e80b461035` |
| [scripts/Test-EngineTransportSourceWrites.ps1](../../../scripts/Test-EngineTransportSourceWrites.ps1) | 21698 | `3dc31f8339eb2f396981746a6b47af91726b988d14984b15d017fdabc3e46085` |

r4 仍未提供游戏启动窗口、真实 Kernel family 采用或 relay 的运行时机器证明。本页通过范围未扩大为生产 Ready。

## 复现与仍需完成的验收

本批实际环境使用 Windows x64、VS2019 BuildTools 的 `vcvars64.bat`、PowerShell 7 和 `/std:c++17 /W4 /WX /O2 /EHa /MD /guard:cf`；链接启用 CFG/CET、`/OPT:NOICF`，显式链接 bcrypt/kernel32。夹具宏 `RIZ_ENGINE_TRANSPORT_COMMIT_FIXTURE` 对三个翻译单元一致启用，只存在于独立 owned EXE；它不是生产 Request 的故障设置 API。

下列命令会严格构建并运行**新的** owned 证据目录；输出目录必须尚不存在。它们没有在本报告整理过程中再次执行。第三方离线复核需要保全上面的 ZIP/日志和合法取得的相同 SHA 固定 DLL；本机 D 盘证据链接不是仓库可移植文件。

```powershell
& 'C:/Users/71957/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe' -NoProfile -File `
  'C:/Users/71957/Desktop/riz_toolbox/scripts/Test-EngineTransportSourceWrites.ps1' `
  -OutputDirectory 'D:/riz-build-cache-next/engine-transport-sourcewrites-recheck-20261026' `
  -UnityPath 'D:/SteamLibrary/steamapps/common/Rizline/UnityPlayer.dll' -TimeoutSeconds 60
```

Prepare-only 的复现入口为 [Test-EngineTransportRelayPreparation.ps1](../../../scripts/Test-EngineTransportRelayPreparation.ps1)，采用相同 UnityPath/fresh OutputDirectory/TimeoutSeconds 约定；固定 byte profile 为 [EngineTransportGameProfile.h](../../../native/RizToolbox.EngineBootstrap/EngineTransportGameProfile.h)。

仍需独立完成：23 relay 的真实 native Mid 调用与寄存器/flags/LastError/FP 保真、真实逐 PC 展开与异常；3 个扩大物理块的 zero/nonzero 路径且不误发 probe；V5 全模块和 Kernel family 身份、不可卸载资源责任、全部 Configure/首次 CLR adoption 与实际启动窗口；随后才讨论固定游戏候选。当前通过不证明取消完成、未来 producer 封口、整局/重试/返回菜单或全部 root 可释放。
