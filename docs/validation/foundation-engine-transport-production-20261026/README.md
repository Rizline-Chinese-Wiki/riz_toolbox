# O 批：固定传输合同、九个原函数桩与 V5 原生布局

2026-10-26。**P0 固定磁盘合同、P1 九个原函数桩的自有机器验证，以及 V5 native/CLR 布局验证已通过。完整生产传输仍未接入。** 本页以 P1 `build r3 / cases r4` 为最终结果；relay preparer 后续仅通过 object 编译，23 个 relay 和 CLR 生产采用尚无传输运行结果。

| 已验证部分 | 实际结果 | 证据 |
|---|---|---|
| P0 固定 Unity 磁盘合同 | 876,709 项检查；13 个完整函数、27 条 RF、1,838 条指令；23 个逻辑点、36 个非重叠物理窗口、3 个扩大块 | [production-transport-facts.json](D:/riz-build-cache-next/engine-transport-production-contract-r1-20261026/production-transport-facts.json) |
| P1 构建与磁盘 PE | MSVC 严格构建 exit 0，无警告；119 项 PE 检查；54 个自有 EXE 导出标记 | [构建声明](D:/riz-build-cache-next/engine-transport-original-fixture-r3-20261026/transport-original-fixture-build.json)、[PE 核验](D:/riz-build-cache-next/engine-transport-original-fixture-r3-20261026/originals-pe-verification-r1.json) |
| P1 真实自有调用 | PID 20440，exit 0；874 项子检查、959 项父检查；108 次调用＝54 baseline＋54 original | [最终运行结果](D:/riz-build-cache-next/engine-transport-original-cases-r4-20261026/fixture-result.json) |
| P1 Windows 展开与异常 | 202 个真实 TF 单步 CONTEXT；每种输入分支的 prefix/MOV/JMP 指令边界完整覆盖；36 次 SEH、36 次 C++ 异常 | [子回执](D:/riz-build-cache-next/engine-transport-original-cases-r4-20261026/child-result.json)、[原始二进制](D:/riz-build-cache-next/engine-transport-original-cases-r4-20261026/receipt.bin) |
| V5 原生声明布局 | 实际编译及自有 EXE exit 0；version 5，91,280 字节，alignment 8；13 typed / 23 site / 36 window / 12 family | [独立布局结果](D:/riz-build-cache-next/engine-transport-publication-layout-r1-20261026/layout-result.json) |
| V5 CLR 布局与既有回归 | Runtime build 0 warning/error；native/CLR 布局 7/7、coordinator 28/28、receiver 10/10；缺证据负向实际 exit 1 | [独立托管布局结果](D:/riz-build-cache-next/engine-transport-managed-layout-r1-20261026/test-result.json) |
| 轻量证据包 | 44 份原件副本；含最终构建、运行、P0、V5 布局、失败 r1 及实际脚本 | [evidence-index.json](D:/riz-build-cache-next/engine-transport-evidence-r2-20261026/evidence-index.json) |
| 后续 relay preparer 编译 | r2 严格 `/W4 /WX /O2 /GS- /GR- /Zl /guard:cf` object 编译 exit 0，输入哈希不变；没有执行 relay | [独立编译结果](D:/riz-build-cache-next/engine-transport-relays-compile-r2-20261026/compile-result.json) |

使用本会话已读的 docs-generator 规则整理；普通固定 PE 逆向，`flavor = null`。范围继承 [scope.md](C:/Users/71957/Desktop/riz_toolbox/docs/validation/foundation-engine-production-20261025/scope.md)。真实游戏目录只读；本批没有启动游戏、加载 UnityPlayer、安装候选或写入游戏指令。完整证据链及失败经过见[正式报告](C:/Users/71957/Desktop/riz_toolbox/docs/2026-10-26_底层-生产传输原函数桩-report.md)。N 批受控验证见其[独立 README](C:/Users/71957/Desktop/riz_toolbox/docs/validation/foundation-engine-wait-20261026/README.md)，数量不合并。

## 这些结果解决了哪一层

P0 把固定 DLL 的真实字节、完整函数边界、异常展开链和覆盖窗口固化成可重跑检查器与工具生成头。P1 实现并实际调用新增九个 typed 通道所需的原函数前缀桩，验证拆分后的行为与同字节 baseline 一致。V5 已验证 native 声明的大小/偏移和 CLR wire 一致，以及 full-width roundtrip、旧 schema/越界/保留位拒绝；没有实际 producer 或首次安装。

```mermaid
flowchart LR
    P0["P0 磁盘合同已验证"] --> P1["P1 九种前缀桩已自有验证"]
    P0 --> V5["V5 native/CLR 布局已验证"]
    P1 --> Pending["尚缺：23 relay、完整 writer、CLR 生产采用"]
    V5 --> Pending
    Pending --> Window["尚缺：固定游戏首次安装窗口证据"]
```

“已验证前缀桩”不包含执行完整游戏函数。自有 tail 代表必要寄存器、flags、输出槽和异常行为；不会替代未知的完整函数语义。202 个 CONTEXT 来自正在执行的自有指令，验证恢复实际 caller RIP/RSP 和八个非易失 GPR；异常测试在自有 tail 内抛出。`exceptionsAtEveryPrefixPcVerified=false`，没有逐前缀 PC 注入异常的结论。

## 固定原件与覆盖合同

输入为 `D:/SteamLibrary/steamapps/common/Rizline/UnityPlayer.dll`，31,006,120 字节，AMD64 PE32+，SHA256 `43fb0aea1b80c74963dae88ec7fc1c3c1993d893ce94d12972000fcc9b653ad1`。P0 仅反汇编选定 13 个完整函数，全 `.pdata` 遍历只用于 CHAININFO 归属解析；不证明外部或间接入口封闭。

| 九个新增桩，固定顺序 | 源 RVA | 最短完整前缀字节数 | continuation 的 scratch |
|---|---|---:|---|
| Register | `44D630` | 15 | R11 |
| Remove | `447AD0` | 14 | R11 |
| Consume | `447C30` | 14 | R11 |
| Pump | `447D60` | 15 | R11 |
| Drain | `447750` | 14 | R11 |
| Callback | `78F5E0` | 17 | R10 |
| Yield | `78FC50` | 15 | R11 |
| Dispatch | `5A5260` | 14 | R11 |
| Batch | `5DA510` | 14 | R11 |

Callback 原函数在前缀之后继续使用 R11 定位栈槽，因此桩以 R10 做 7 字节 RIP-relative MOV＋3 字节 register JMP。P0 完整 body 检查 R10/R10D/R10W/R10B 的显式、隐式及内存寻址使用；P1 实际走 owner-match/mismatch、自有 R11 栈写及高 EAX。Consume / Drain / Dispatch 的比较 flags 经尾跳保持，最终自有 ASM 已实际执行 JNZ taken/not-taken 两条路径；不只复制 ZF 数值。

23 个逻辑观察点由 Inline 3、Scheduler 2、Wait 18 组成。三个 covered-tail 必须分别处理：

| 逻辑点 | 物理覆盖 | 原空路径目标 | 仍待实际 relay 实现的动作 |
|---|---|---|---|
| `7938B3` | `7938AA..7938BD`，19 字节 | `7938B6` | 空路径跳过 Probe/store，共同重放 RIP MOV |
| `78F6F5` | `78F6F0..78F6FC`，12 字节 | `78F715` | 空路径只重放 CMP，再到 `78F719`；跳过 Returned Probe/node.wait store |
| `792527` | `792522..79252E`，12 字节 | `792547` | 空路径只重放 CMP，再到 `79254C`；跳过 Returned Probe/node.wait store |

检查器区分 observation PC、物理覆盖区间和尚未生成的 relay return PC；生成头不授予任何安装权限。六处必要 CALL `790093 / 78F70C / 79253E / 5A5288 / 5A5298 / 5DA645` 均不在提议覆盖区内，保留原 CALL/return PC。扩大块、零路径及实际 relay 的动态展开尚未验证。

## 冻结证据与失败历史

最终 P1 `transport-original-sources.zip` 含 **9 项构建输入**，包括实际 runner、verifier 和工具生成的 profile。SHA256 为 `6f8c93fc8bfb8624b0d4669eb07d2556a88f60dd8edfa3056377b97149974181`。本次写文档时只读复核 ZIP CRC、逐 entry 的 SHA/长度和当前九项源，均吻合。V5 布局的三份输入单独归档，未混入这份 ZIP。

| 原件 | SHA256 |
|---|---|
| P0 facts JSON | `260166cd28f603239f68c63595efc6fb1eacf7d382d0befa64fac54d40c657ee` |
| P0 生成头 | `de2c9593aaf0d4ef11765ebf0521b2ba4a3a855af3f4420365cb497f534c213c` |
| 最终 P1 EXE | `799eb87b5ac92ff9b103410471c1c7eee9f62fb1185bf2250478d621b272034a` |
| 最终 P1 PE 核验 | `9be3107b9902a91b98a21c41aed02ddc62fda6f17fead1e5e9fea89a6f6ce0e3` |
| 最终 P1 父结果 | `8319fd9e1bdb19bcc1f24827e09889dcec607bd08e8ea1d4fe7aff0e89fcd3d0` |
| V5 native 布局结果 | `0836a918ab0aa71a5c25b6b490cd8dc3e66b374e89d323fcccb81ea317b7a208` |
| V5 managed 布局结果 | `10b5a702fdeb8c0783f280a0c8266f4edb9ed5d31b2fe1bd87fdedb6e2890667` |
| 44 份原件证据索引 | `19439d9c04a82c6e6eae12698fdaca01beee8797a790ae3fcd976ac62658a540` |

首个实际 `cases r1` 失败仍保留：108 次调用和 36＋36 次异常已执行，但单步仪器未在 Windows 交付异常后重新设置 TF，只取得 36 个 offset 0 CONTEXT，37 项失败。修复是重新 arm TF，原前缀桩未因该问题改写；r2 达到 202 个 CONTEXT。r3 父 runner 强化到逐 route/offset 唯一与全覆盖，仍使用 r2 EXE。最终 build r3 / cases r4 另把自有 tail 的 SETZ 改为真实 JNZ 分支并重新冻结 9 项输入；此前成功与失败目录均保留。证据包 r1 因路径规范化拒绝而未成功复制，r2 才是这里引用的 44 份证据归档；这不是原生调用失败。

后续 managed layout 的 **479 输入**前后哈希不变，独立 ZIP SHA256 `ff3ccfed0baac139b38d8d05834bf53e5633ab897db016f24762b975a9b1e7d2`，不在此前 44 份证据包中。缺 `RIZ_ENGINE_TRANSPORT_LAYOUT_JSON` 的真实负向进程为 6/7、exit 1，只有 native evidence 必须存在这一项失败；成功进程实际读取原生布局回执后 7/7。该检查不声称 Native→CLR Wait/Scheduler 传输，相关字段仍 false。

## 独立复跑

由唯一 builder 执行。先核冻结源与依赖；所有输出必须是不存在的绝对目录或文件。以下路径在本页核验时尚不存在，复跑时若已存在需另换新编号；本次文档工作没有运行这些命令。

```powershell
C:/Python314/python.exe C:/Users/71957/Desktop/riz_toolbox/docs/validation/foundation-engine-transport-production-20261026/inspect-production-transport.py --image D:/SteamLibrary/steamapps/common/Rizline/UnityPlayer.dll --output-directory D:/riz-build-cache-next/engine-transport-production-contract-r2-20261026

pwsh -NoProfile -File C:/Users/71957/Desktop/riz_toolbox/scripts/Build-EngineTransportOriginalFixture.ps1 -OutputDirectory D:/riz-build-cache-next/engine-transport-original-fixture-r5-20261026

C:/Python314/python.exe C:/Users/71957/Desktop/riz_toolbox/docs/validation/foundation-engine-transport-production-20261026/verify-transport-originals.py --directory D:/riz-build-cache-next/engine-transport-original-fixture-r5-20261026 --output D:/riz-build-cache-next/engine-transport-original-fixture-r5-20261026/originals-pe-verification-r1.json

pwsh -NoProfile -File C:/Users/71957/Desktop/riz_toolbox/scripts/Test-EngineTransportOriginalFixture.ps1 -BinaryDirectory D:/riz-build-cache-next/engine-transport-original-fixture-r5-20261026 -OutputDirectory D:/riz-build-cache-next/engine-transport-original-cases-r5-20261026

pwsh -NoProfile -File C:/Users/71957/Desktop/riz_toolbox/scripts/Test-EngineTransportPublicationLayout.ps1 -OutputDirectory D:/riz-build-cache-next/engine-transport-publication-layout-r2-20261026

pwsh -NoProfile -File C:/Users/71957/Desktop/riz_toolbox/scripts/Test-EngineTransportManagedLayout.ps1 -NativeLayoutResult D:/riz-build-cache-next/engine-transport-publication-layout-r2-20261026/layout-result.json -OutputDirectory D:/riz-build-cache-next/engine-transport-managed-layout-r2-20261026
```

## 下一步与明确未完成项

按[生产传输路线](C:/Users/71957/Desktop/riz_toolbox/docs/validation/foundation-engine-wait-20261026/next-production-transport.md)继续接完整 publisher、23 relay 与三块 zero bypass、12 family 首次采用、Doorstop 后继失败责任和独立自有冷启动全链。relay preparer 的首轮 object 编译因局部名 `near` 被 Windows 宏展开而失败；只改为 `nearOutput` 后 r2 编译通过，CPP SHA256 为 `d9da0562c601ee650f8026483b67e9ede98c0ecb1d54b22f0f0214902a3914d9`，编译结果 SHA256 为 `dcffb12f1e761b6b9d4d4068e0e9f9739e890083378be197a06dad40766b242c`。它不在上述 44 份证据包或 P1 九源 ZIP 内，不代表任何一个 relay 运行或安装。CLR V5 仅有布局与既有模型验证结果，尚无生产采用或运行传输结果。

`productionAdmissionVerified=false`、`sourcePatched=false`、`gameExecuted=false`。完整生产 installer、AdoptPublished、真实主线程来源与首次写窗口、全部 writers/consumers、future producer 封口、取消完成、最终根释放及整局/重试/返回菜单仍无本批验收。V5 的 Ready、producer/owner/main TID 等字段只是合同声明，尚无实际 producer 填充并完成安装的凭据。
