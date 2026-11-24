# 生产传输：真实 Kernel 的 12-family Configure 验证

2026-10-26。**12-family 私有配置组件已通过六个独立自有进程验证：48 次实际 Configure、1 次单路 Node Attach、274 项父检查、806 项子检查。** 完整 V5 publisher、生产 readonly owner 和首次全 family 采用仍未接通；本页不是生产安装、完整 cold 启动或游戏验收。

最终原件为 [owned r2/test-result.json](D:/riz-build-cache-next/engine-transport-families-owned-r2-20261026/test-result.json)，`passed=true`，build exit 0，6/6 child exit 0，源与输入 hash 前后不变。SHA256 为 `bdd919809bd90be5989096276ba0982421e626bcc46a70d9caaeec089b6387b2`。唯一 builder 实际构建/运行；本次文档工作只读核对并新增本页，没有重跑或改写这些原件。

使用本会话已读的 docs-generator 规则，普通固定 PE 逆向/自有验证，`flavor=null`。范围继承 [scope.md](C:/Users/71957/Desktop/riz_toolbox/docs/validation/foundation-engine-production-20261025/scope.md)：用户授权继续兼容底层及多代理，要求“先继续代码，实测稍后”；真实游戏目录只读。网络行为为无。前置的 13 函数固定合同、九个原函数桩及 V5 布局验证见 [O 批 README](C:/Users/71957/Desktop/riz_toolbox/docs/validation/foundation-engine-transport-production-20261026/README.md)，本页不把其历史数量重复计入。

## 六个实际进程证明了什么

| 模式 | 实际 PID / exit | 子检查 | 实际配置与拒绝结果 |
|---|---|---:|---|
| [normal](D:/riz-build-cache-next/engine-transport-families-owned-r2-20261026/normal/case.json) | 45636 / 0 | 160 | 12 Configure 成功；4 个 family mask 均为 4095；Verify 成功且 receipt 字节不变 |
| [bad-site](D:/riz-build-cache-next/engine-transport-families-owned-r2-20261026/bad-site/case.json) | 68208 / 0 | 72 | 把实际 relay return PC 加 1，返回 BadSite=10；0 Configure、0 retained claim；拒绝保持 sticky |
| [production-gate](D:/riz-build-cache-next/engine-transport-families-owned-r2-20261026/production-gate/case.json) | 65336 / 0 | 72 | FixedGame profile 返回 AdmissionUnproven=16；首个 Configure 前拒绝，mask 全 0 |
| [receipt-tamper](D:/riz-build-cache-next/engine-transport-families-owned-r2-20261026/receipt-tamper/case.json) | 64068 / 0 | 163 | 先真实完成 12 Configure，再篡改回执 config SHA；ReceiptChanged=14 永久拒绝；恢复原 receipt 也不解除；仍保留 4095 claims |
| [repeat-attempt](D:/riz-build-cache-next/engine-transport-families-owned-r2-20261026/repeat-attempt/case.json) | 28348 / 0 | 162 | 第二次调用返回 AlreadyAttempted=3；实际原 receipt 完全不变，没有新增 Configure |
| [node-attach](D:/riz-build-cache-next/engine-transport-families-owned-r2-20261026/node-attach/case.json) | 63532 / 0 | 177 | 12 Configure 后只实际 Attach Node Start 一路；Verify 接受 Attached 0→1；observerCalls=0 |

六种模式与六个 PID 均唯一，没有超时或被 kill 的子进程。成功配置模式共 4 个，每个独立进程 12 次 Configure，合计 **48**；另两场是预配置拒绝。`node-attach` 只验证一路准确 epoch 的 Attach 和后续只读验证，**不证明全 family adoption、dispatcher 接线或任何真实调用分流**。

每场保存实际 `case.json / child-result.json / stdout.log / stderr.log / request.bin / receipt.bin / plan.bin / after-attempt.bin`。`case.json` 包含 PID、exit、参数、时间、文件 hash；其 `files` 列表不含自身，共 7 项。父级核 stdout 与持久化 child JSON 一致，核 raw 长度/hash 与逐配置 SHA，失败或超时保留原件；超时只允许终止它自己创建的 Process。

## 12 个配置如何对应实际 Kernel

私有 API 位于 [EngineTransportFamilies.h](C:/Users/71957/Desktop/riz_toolbox/native/RizToolbox.EngineBootstrap/EngineTransportFamilies.h) / [EngineTransportFamilies.cpp](C:/Users/71957/Desktop/riz_toolbox/native/RizToolbox.EngineBootstrap/EngineTransportFamilies.cpp)：

```cpp
RizEngineTransportFamilies::Result PrepareAndConfigure(const Request*, Receipt*);
RizEngineTransportFamilies::Result Verify(const Request*, const Receipt*);
```

没有新增 export、caller Ready 或准入 setter；组件不 Attach、不写 source、不加载 DLL。Request 只描述模块基址、13 targets/originals、23 对逻辑 PC/实际 relay return PC、slot、main OS TID 和独立 near allocation。FixedGame 请求在独立关闭的生产 gate 下返回 AdmissionUnproven。Owned 请求必须使用准确 MEM_PRIVATE 固定映射，原桩、Kernel 与普通 import 所在 EXE 仍必须是真实 MEM_IMAGE。

| family index | 真实配置 ABI | 配置字节 | 身份和观察点 |
|---|---|---:|---|
| 0..3 | Node Start / Execute / Release / Detach | 各 24 | Node ABI2；target/original/entry/epoch；该配置没有 main TID 字段 |
| 4 | aggregate Inline | 88 | 同一实际 Start slot/target/epoch；3 对 Inline logical/relay PC |
| 5..10 | Scheduler Register / Remove / Consume / Pump / Drain / Callback | 各 64 | ABI1；实际 main OS TID；仅 Drain 有 2 对 row PC，其余 site bytes 为 0 |
| 11 | aggregate Wait | 432 | ABI1；Wait3 original/target＋Node4＋Scheduler6 targets；18 对 phase PC、实际 main OS TID |

全部 12 份零模板、13 个准确 descriptor、35 个 API、23 个实际 CALL/literal 与 fresh slot 在首个 Configure 前完成校验。Inline 的 `expectedStartEpoch` 是唯一真实依赖：先实际 Configure Start，再从 GetInfo 取得 epoch，补入已准备模板的该 8 字节字段并计算最终 SHA，随后才 Configure Inline；不提前猜 epoch。所有配置保留准确原字节和 SHA，包括未使用尾部的 0。

Kernel 是普通依赖加载的真实 `RizToolbox.EngineNodeKernel.dll`，SHA256 `1541feaa8fe7b37d0d3a544fbb315387ea93e3f6b96a4dc955dc76dc9c30e38a`，133,120 字节；import lib SHA256 `eb1a8bde36b6e7ff2039e5fc5e69d428ced8ae5279046b1a1a0aa82d1eadce1c`。runner 核既有 [kernel-manifest.json](D:/riz-build-cache-next/engine-wait-kernel-r2-20261026/kernel-manifest.json)，复制 DLL/lib 到 fresh 输出并持有拒写锁，不重新构建 Kernel。

本次另外从最终自有 EXE 的 PE 磁盘表只读核对：**恰好 35 个唯一普通 Kernel imports**。真实 Families 组件在任何 API 调用前逐项核 EAT、无 forward、RX 地址及实际 ordinary ILT/IAT；版本/size/limit 由实际 API 返回值核对，Attach exports 也保留普通 imports，但配置组件从不调用它们。独立 Node Attach 只由自有测试场景发起。测试 EXE 为持久化、BCrypt 和异常处理使用常规 CRT，不把它称为无 CRT 的生产 DLL。

## 映射、展开表与永久责任

夹具只读固定 Unity 文件，SHA256 `43fb0aea1b80c74963dae88ec7fc1c3c1993d893ce94d12972000fcc9b653ad1`，按原 PE section 复制为私有映射；不通过 LoadLibrary 加载游戏，不做 relocation 后执行。13 原函数桩链接真实四 Node ASM＋新增九 ASM，各 continuation 绑定到这个准确私有映射的 `target+prefix`，但从未调用任何 original。

near arena 是**单独 VirtualAlloc 的 reservation**，实际 `AllocationBase==near`，不是与 image 共用 reservation。Relay Prepare 使用 13 个真实 typed GetEntry 和 3 个真实 probe Entry，实际生成 near bytes；随后切 RX、flush、RtlAddFunctionTable，并逐项用 RtlLookupFunctionEntry 找到 23 个实际 table tuple。配置时使用原逻辑 observation PC，绝不以扩大物理块起点替代；每个 return PC 来自真实 FF15 CALL 末尾，literal 指向对应真实 Kernel probe。

这些结果只证明准备字节、表身份与配置描述符。**没有跳入映射、original 或 relay，没有执行机器重放、实际 zero bypass、typed callback 或 phase callback。** 成功后的真实 Info 计数均为 0，`observerCalls=0`。

Configure 是永久 claim。夹具不调用 RtlDeleteFunctionTable、VirtualFree 或 FreeLibrary；已分配 image/near、注册表和真实 helper 一直保留到自有进程自然退出，由 OS 回收。`runtimeTableDeleted=false / ownedMappingsFreed=false / retainedUntilProcessExit=true`。不能把正常子进程退出包装为“运行中所有消费者已结束、根可释放”。

## Raw、源归档与独立固定性核对

Request 为 **640 字节**，Receipt 为 **9,792 字节**，Plan 为 **13,184 字节**。Receipt 的 12×432 配置区从 offset **2448** 开始；实际 Info snapshots 单独保存。Verify 前后外部 receipt byte-for-byte 不变；后续 Attached 观察保存在组件私有状态，不重写原回执。

本次文档核验做了只读交叉检查：

- 40 项主结果引用的当前源、输入和根产物 SHA/长度全部相符；6 个 `case.json` 的 SHA 与主结果相符，各 case 的 7 项原件亦相符。
- ZIP 的 15 个 entry CRC、逐 entry SHA/长度均与冻结 source manifest 相符。
- 四个成功配置进程的 **48 份** raw 配置独立按 Request、真实 Start epoch 和各 ABI 重建，全部字节连同零尾一致；独立 SHA 与 child/receipt 中的配置 SHA 一致。
- 6×23 个 Request/Plan 的 logical/relay PC 对照相符；bad-site 仅第一对实际 return PC 按场景加 1，其余准确。

这些是原件完整性复核，不新增 Configure 次数或游戏执行证据。

| 证据 | SHA256 |
|---|---|
| [最终实际结果 r2](D:/riz-build-cache-next/engine-transport-families-owned-r2-20261026/test-result.json) | `bdd919809bd90be5989096276ba0982421e626bcc46a70d9caaeec089b6387b2` |
| [15 源 ZIP](D:/riz-build-cache-next/engine-transport-families-owned-r2-20261026/families-fixture-sources.zip) | `6fda6ef8a440eb16f7ea4dd59657a0b8dae5768398d729677c8d609687ac9e08` |
| [自有 EXE](D:/riz-build-cache-next/engine-transport-families-owned-r2-20261026/EngineTransportFamilies.Fixture.exe) | `08abee3802da8b5be935f2814695101a774519db53250468cfac816cabc18db4` |
| 冻结 Families header，4,198 字节 | `e699573a35485d272fbc34d6d8fa124d98e323a06fcbe2be16951554bb123bd7` |
| 冻结 Families CPP，42,018 字节 | `4a7b2b38461f6186b083d25f98dc452bcd27e560c33fb82027315e738944a124` |
| 实际 fixture CPP，27,604 字节 | `e7bad27acbc90f32c87f1034b2479b5ff05d5c1d6b0deea0298ad317ea6e37b8` |
| 实际 Test runner，19,517 字节 | `e2ff564839292d66587c14c94ba64d9fbc6ee40dd8f72f2201851149e29d4b72` |
| [严格 core object 编译 r2](D:/riz-build-cache-next/engine-transport-core-compile-r2-20261026/compile-result.json) | `2f9ab3ddab729dbbda121bd597dd06e3d9322d53342c04adcd9831f4a60a8287` |
| [首失败构建 r1](D:/riz-build-cache-next/engine-transport-families-owned-r1-20261026/test-result.json) | `7254667acb5dd0c8cb0b92a64582681726a0ab4babda89d74f047f64a7133d2d` |

core r2 独立按 `/O2 /W4 /WX /GS- /GR- /Zl /guard:cf` 编译 Families/Relays/SourceWrites object，`passed=true / sourceHashesUnchanged=true / forbiddenCrtSymbols=0`。Families object 未依赖 memset/memcpy/chkstk 或 CRT helper。它是 object 编译与符号核验，不是最终 production DLL 的 PE、依赖图或 loader 验证；与本页自有 CRT 测试 EXE 分列。

## Evidence → Finding → Path

`observed_at=2026-10-26`，`source_type=file`，`linked_workitem=transport-families`；实际 source_ref/hash 取上表和每个 case 链接。复现命令为下节 runner；历史失败原件只读，不覆盖。

| E / F | severity / status / confidence | 证据与有限结论 |
|---|---|---|
| E1→F1 | `n/a_re / validated(owned) / high` | 实际 r2 父/子/raw＋冻结源：四个进程各实际配置 12 family，准确覆盖 23 site/36 window 的配置责任；不执行 source |
| E2→F2 | `n/a_re / validated(owned) / high` | bad-site/gate 的独立子进程＋raw：首个 Configure 前拒绝；不是靠 caller bool 获准 |
| E3→F3 | `n/a_re / validated(owned) / high` | tamper/repeat/Node Attach 的独立进程＋raw/private-owner 实现：回执不可替代，失败保留全部已取得 claim，重复不重配，单路 Attached 可单调采用 |
| E4→F4 | `n/a_re / validated(compile-only) / high` | core r2 命令/日志/符号与源 SHA：正式组件严格编译且无禁止 CRT helper；没有最终生产链接结果 |

`P1, path_type=callflow`：固定磁盘只读拷贝→独立 near arena→真实 Kernel entries 的 relay Prepare/RX/RF 注册→组件核 35 API 与全部模板→真实 Node4→取实际 Start epoch→Inline→Scheduler6→Wait→实际 Info/配置字节核验→只读 Verify/反证→保留永久责任至进程退出。步骤对应 E1/E2/E3。共同 residual risk：没有完整 producer/readonly owner、源指令执行、全路 Attach、启动窗口、消费者关闭或取消/最终释放证明。

## 时间线与复跑

首轮 [owned r1](D:/riz-build-cache-next/engine-transport-families-owned-r1-20261026/test-result.json) 于 13:31:43 UTC 开始，构建因 fixture 中 `MAXULONG` 未声明而 exit 1，0 个 child。仅把测试 Hash 长度上限改为 `static_cast<size_t>(0xffffffffu)` 后，r2 于 13:33:07–13:33:19 UTC 严格构建并完成六个真实进程。Families 组件两源始终未改；失败日志、旧源快照和目录保留。这是夹具编译修复，不是放宽配置断言。

以下由唯一 builder 执行；它会严格构建测试 EXE，再依次启动六个自有子进程，每个最长 60 秒。输出必须是不存在的绝对目录；本页使用新的 r3 示例，若已经存在需换新编号。本次文档工作未执行该命令。

```powershell
pwsh -NoProfile -File C:/Users/71957/Desktop/riz_toolbox/scripts/Test-EngineTransportFamilies.ps1 -KernelDirectory D:/riz-build-cache-next/engine-wait-kernel-r2-20261026 -UnityPath D:/SteamLibrary/steamapps/common/Rizline/UnityPlayer.dll -OutputDirectory D:/riz-build-cache-next/engine-transport-families-owned-r3-20261026 -TimeoutSeconds 60
```

下一步才是把真实 Families 回执、relay 资源与完整 36-window 提交器接成 V5 publisher，再实现独立只读托管 owner 并做全链验证。此读取时点 V5 四个 API 仍只有声明，不能称实际安装已完成。中途原生 Configure 失败后的部分 claim 保留有实现，但本六场没有在第若干 Configure 内制造实际原生失败，不把 tamper-after-12 扩称逐 Configure 故障矩阵。

`fullFamilyAdoptionVerified=false`、`sourceWrites=0`、`sourcePatched=false`、`originalsExecuted=false`、`relayCodeExecuted=false`、`mappedImageExecuted=false`、`installerExecuted=false`、`productionAdmissionVerified=false`、`gameExecuted=false`。真实游戏首次写窗口、all writers/consumers、future producer 封口、取消完成、最终根释放以及整局/重试/返回菜单均未由本页闭合。
