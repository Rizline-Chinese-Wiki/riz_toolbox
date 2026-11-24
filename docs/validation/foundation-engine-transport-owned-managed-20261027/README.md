# V5 同进程原生 → C# 全通道采用

## 后续 r6：活动读取与三种实际篡改

[r6 实际父回执](D:/riz-build-cache-next/engine-transport-owned-managed-r6-20261027/test-result.json) 顶层通过，SHA256 `fbcdfec5e3188d40b687560a1a4cceb78645283d5b04f04dbaf51a7214423969`；七个独立进程全部退出 0，实际构建零警告、零错误。r5 和 r4 的原件保留，各自只证明当时源码。

| 模式 | PID | 托管检查 | 实际结果 |
|---|---:|---:|---|
| adopt | 49544 | 109 | 12 组真实采用与第二消费者共享 |
| duplicate | 68540 | 138 | 拒绝重复全采用，保留物理责任 |
| live-tamper | 63952 | 108 | 采用内部的实际源字节变化被拒绝 |
| activity-cost | 72148 | 139 | 20 次实际协调器读取，全部 Known/idle |
| activity-source-tamper | 27428 | 140 | 下次活动读取拒绝真实源字节变化 |
| activity-protection-tamper | 66556 | 140 | 下次活动读取拒绝真实 RX→R 变化 |
| activity-export-tamper | 70176 | 142 | 下次活动读取拒绝真实 Kernel EAT 槽低字节变化 |

每个活动负例都先完成真实采用，再修改本进程自有映像，且在下一次 `ReadActivity()` 前不调用完整 native Verify。托管 owner/coordinator 随拒绝永久 Unknown；finally 实际恢复原字节和保护，随后完整 native Verify 确认原生 Ready、原始发布字节、12 组物理绑定和保留资源不变。EAT 槽由独立持有 Kernel PE 解析，并在修改前核对真实命名导出的地址；不执行被修改的导出。

活动读取使用绑定实际 owner/coordinator 的同步专用路径，不接受 caller delegate、logger、异步许可或可传递 token。两边界仍核实时映像头、44 条依赖边、13 函数/36 窗口、原函数、23 relay、67 RF、保护和全部 Kernel 身份；内部桥核对实时完整发布与各自 GetInfo。完整文件 SHA/native Verify 留在采用、共享与变更边界。静态导出解析和固定缓冲复用不代表复用之前的实际观测结果。

| 实际版本 | p50 | p95 | p99 | 每次平均托管分配 |
|---|---:|---:|---:|---:|
| r5 | 29466.7 μs | 37184 μs | 39971.7 μs | 6366352 B |
| r6 | 8206.1 μs | 12299.7 μs | 12771.5 μs | 263328 B |

两组各为同进程 20 次原始样本、nearest-rank 百分位和当前线程分配；父进程重新核算统计量。**r6 仍不适合逐帧底层调用**，不能因测试通过宣称达到实时性能预算；它也不是游戏性能测量。进一步依赖图读取计划及 IAT/ILT 负例正在独立 r7 中验证，本文尚不记录 r7 为通过。

与 r6 对应的完整 Runtime/Audio 源码快照 [core-r6 父回执](D:/riz-build-cache-next/engine-active-retirement-core-r6-20261027/test-result.json) 实际通过 Audio/content 796/796、协调器 28/28、对象身份 10/10；SHA256 `52ee773a691c02da57bd8ac2436fb19b9a6be46d8937bdd6df8ad9e5d4b2cd07`。Runtime 零警告、Audio 夹具保留原 17 条警告，均零错误。这不是全 24 套回归，也未将新性能代码打入 package-r2。

## 后续 r4：实时映像校验与实际篡改拒绝

最新 [r4 实际父回执](D:/riz-build-cache-next/engine-transport-owned-managed-r4-20261027/test-result.json) 覆盖三个独立进程：adopt PID 48892，109 条检查、29313 ms；duplicate PID 50736，138 条检查、31719 ms；live-tamper PID 48472，108 条检查、6672 ms。三者均退出 0，源码和输入哈希前后不变。下面 r3 原件仍作为历史保留，不回填新代码。

审阅发现，r3 同步组合内部只检查发布记录和 Kernel 身份，内部可调用的 logger 可能临时修改并恢复映像字节，从而逃过前后采样。本轮补齐每次内部验证的独立映像头/图、13 个完整函数和 36 窗口、原函数桩、23 个 relay 与 67 个 RF/Windows 注册/保护检查；仅省去重复的五份完整文件 SHA 与原生完整 Verify，两者仍在整个操作的前后真实执行。

[实际 live-tamper 回执](D:/riz-build-cache-next/engine-transport-owned-managed-r4-20261027/live-tamper/managed-result.json) 记录：在采用流程的 logger 回调内，将自有目标首个真实补丁字节从 255 改为 254，读回确认，刷新指令缓存并恢复 RX 保护后才返回；下一次内部验证因源窗口不符立即拒绝，尚无一个 family Attach。finally 将字节真实恢复为 255、保护恢复为 32，再由原生宿主完整 Verify。托管 owner 此后永久 Unknown，重新获取也拒绝；原生快照、36 个补丁责任、12 个 Configure、67 个 RF 和保留资源均不变。只修改自有 DLL 的内存副本，没有执行该处原游戏指令或修改磁盘。

该负例证明一次真实内部采样能够拒绝实际源字节篡改，不证明所有类型的瞬时篡改在任意时刻都不可发生。内部同步作用域仍不能当作停止原生线程的工具，也不允许把它跨异步边界传播。

## 历史 r3：首次联合采用

2026-10-27。两个自有冷启动进程均完成真实 12-family Attach。正常共享与重复全采用拒绝分别验收；未安装或运行游戏，未执行复制的原游戏函数，未开放生产准入。

| 模式 | 实际 PID | 托管检查 | 原生宿主耗时 | 结果 |
|---|---:|---:|---:|---|
| adopt | 6124 | 109 | 25953 ms | 全 12 组 Attached 从 0 变 1，第二获取共享同一 owner/coordinator |
| duplicate | 46332 | 138 | 28188 ms | 全 12 组采用后，第二次完整采用被拒；托管永久 Unknown，原生声明、映射和函数表继续保留 |

这组检查没有实际触发原游戏 typed 函数、23 个 relay 或账本 Complete 回调。逻辑订阅、退订、GC 及物理委托根保留在真实 CLR 中执行；它们不能代替旧调用跨退订后的 Complete/Dispose 机器证据，也不证明取消终态或整局资源已释放。

## 实际调用链与独立核验

宿主普通导入自有目标的 UnityMain2，目标普通导入 WinHTTP，Doorstop 普通导入 Bootstrap，Bootstrap 普通导入 Kernel。真实加载器完成该五模块链，之后 wmain 才加载 hostfxr。生成的自有入口/TLS/UnityMain2 只执行短函数。

父进程从最终五份 PE、构建 manifest、13 个固定函数、27 个静态 RF 与 44 条实际普通 ILT/IAT/EAT 边生成独立 facts JSON，并将该 JSON 的 SHA256 传入托管入口。期望 hash 不取自运行时发布记录。受测程序集由 Runtime、SDK 和 UCO 入口的独立源码快照构建，零警告、零错误。

C# 使用实际已加载的五模块及各自持有文件，调用发布器 GetInfo → Verify → GetInfo；独立复核文件身份、整个固定函数、36 个补丁窗口、23 个逻辑站点、67 个 RF/完整展开元数据、near RX 区和实际 Windows 函数表、12 个实际 Kernel 配置。随后实际一次 Attach 全部 12 组，前后计数不变，重复共享不再 Attach。

原生宿主在 CLR 前、CLR 初始化后且入口调用前、托管入口返回后各做一次完整读验读；父进程检查三处顺序、实际状态/返回码、LastError、CLR 是否已加载、hostfxr 返回状态及末次保留责任。每个进程的初始与最终 91280 字节 raw 完全相同，托管记录的 publication SHA 与原生 raw 相等。

## 耗时修复与失败历史

首次联合 r1 在 60 秒、r2 在 180 秒由父进程终止，均保留失败原件，没有计为通过。r2 阶段记录证明实际 12 组 Attach 在约 79 秒完成，但后续汇总读取反复执行全文件校验，未产生最终验收回执。

独立原生 benchmark PID 45648 实际执行五次完整 Verify，平均 801409.94 μs，最大 869060.4 μs，五次均验证整个 raw 不变。瓶颈为托管组合调用倍增。

`NativeEngineTransportFirstInstall.VerifyOperation` 将同一次同步组合的完整原生和独立映像校验置于前后边界，嵌套桥仍实时读取原始发布记录及全部 Kernel 身份/活动；没有缓存 Ready、修改 admission 或让可传递令牌授权后续操作。owner 的同线程 monitor 只串行化托管读者，不能当作停止原生执行的证明。首次全采用依然独立要求全部未 Attached、在途为零且整个采用期间真实活动计数不变。静态 RF ordinal 改为从持有 PE 派生的索引；运行时展开指针、元数据和字节仍实际核验。

本机 26/28 秒是冷启动夹具总耗时，不是游戏帧性能数据。

## 原件与冻结

- [联合父回执](D:/riz-build-cache-next/engine-transport-owned-managed-r3-20261027/test-result.json)：SHA256 `f73667659b3c8e96d7e108810612e9b00a3c7518b5f7356c6257df4b58c06836`。
- [独立五模块 facts](D:/riz-build-cache-next/engine-transport-owned-managed-r3-20261027/OwnedTransportBuildFacts.json)：`80b50a309b4dab2a6ff0a9d3c2b526ef46698b77b5ef5f1ad8dc55e0b5618544`。
- [声明源码归档](D:/riz-build-cache-next/engine-transport-owned-managed-r3-20261027/managed-inputs.zip)：`00d185a5f7cb3a807bc6fe888673a28ee813954816bc68123b15c07e9f5eddb2`；同目录 `source-snapshot` 是实际编译输入及独立测试项目，不是全项目验收构建。
- [adopt 托管回执](D:/riz-build-cache-next/engine-transport-owned-managed-r3-20261027/adopt/managed-result.json)、[原生回执](D:/riz-build-cache-next/engine-transport-owned-managed-r3-20261027/adopt/native-result.json)；raw SHA `f3f77d9ec19c77d04b4dbcdac7c973bcb4ad30732f3118e52908f45e41a71a71`。
- [duplicate 托管回执](D:/riz-build-cache-next/engine-transport-owned-managed-r3-20261027/duplicate/managed-result.json)、[原生回执](D:/riz-build-cache-next/engine-transport-owned-managed-r3-20261027/duplicate/native-result.json)；raw SHA `bdf481c3afccbfa914ce1a846735756d004f273125a054c719694c889728e748`。
- [实际完整 Verify benchmark](D:/riz-build-cache-next/engine-transport-owned-verify-benchmark-r1-20261027/test-result.json)。
- [r1 超时原件](D:/riz-build-cache-next/engine-transport-owned-managed-r1-20261027/test-result.json)、[r2 超时及阶段记录](D:/riz-build-cache-next/engine-transport-owned-managed-r2-20261027/test-result.json)。
- [原生宿主实际构建](D:/riz-build-cache-next/engine-transport-owned-managed-candidate-r2-20261027/owned-candidate-build.json)。源与最终 PE 以该冻结归档为准；后续生产构建对 constexpr 检查的修正不回填此历史证据。

## 尚未证明

此组没有观测目标 TLS 之前的首次执行窗口，没有运行真实 Unity 代码，也没有证明生产首次写入安全。全部 12 组 Attach 不等于全部写者/消费者已经覆盖；L/M/N 与真实 Stop→Detach 的同节点联合终态、来源封口、整局 Close、所有根释放及 B01–B19 的其余验收继续进行。生产 gate 继续关闭，游戏实测依用户安排暂缓。
