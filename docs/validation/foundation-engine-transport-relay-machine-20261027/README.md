# 生产传输：23 个 relay 与实际源 E9 的机器验证

2026-10-27。**最终 r4 在一个自有进程中完成 31 组实际路线对照，9742 条子检查与 4019 条父检查全部通过。** 298 份真实 CONTEXT/栈遍历记录包含 208 份 relay/control 单步、62 份实际源补丁 E9 单步，以及 28 次从真实 Entry 回调展开至 relay 返回 PC 的栈遍历。四个 Dispatch 观察点的部分展开信息已改为保留原 source Begin/End 身份的显式 synthetic partial-primary；源 E9 和 relay E9 均经过实际 Windows 展开验证。

本页只证明这批固定机器传输的有界行为。三份实际 Entry ASM 链接的是**自有 fixture Dispatch**，没有运行 Kernel ledger、全 family 采用、完整 V5 publisher 或真实游戏。SEH/C++ 异常传播、生产首次写入窗口和整局资源退休仍未在本夹具验证，相关标记保持 false。

最终原件为 [r4/test-result.json](D:/riz-build-cache-next/engine-transport-relay-machine-r4-20261027/test-result.json)，SHA256 `11bab49fa22bcfb1460639e45ea61a0a4b40a5dd1d5685e8e32c39b018191be9`。实际 PID **29688**，build exit **0**、child exit **0**，没有 timeout/kill；[stderr.log](D:/riz-build-cache-next/engine-transport-relay-machine-r4-20261027/stderr.log) 为 0 字节。唯一 builder 由根代理执行；本次文档工作仅读取既有原件并新增本页，没有重跑测试或改写历史证据。

## 范围与证据归属

普通固定 PE 逆向及自有机器验证，`flavor=null`，沿用 [scope.md](C:/Users/71957/Desktop/riz_toolbox/docs/validation/foundation-engine-production-20261025/scope.md)。用户授权继续完善 PC Mod 公共底层与多代理协作，后续安排为“先继续代码，实测稍后”。本轮仅读取真实游戏磁盘，未写游戏目录、未启动或停止游戏。

固定输入为 `steam-25977441` 的 AMD64 `UnityPlayer.dll`，31,006,120 字节，SHA256 `43fb0aea1b80c74963dae88ec7fc1c3c1993d893ce94d12972000fcc9b653ad1`。完整 13 函数、27 个源 RF、23 个 logical site、36 个物理窗口来自先前的 [固定合同与原函数桩记录](C:/Users/71957/Desktop/riz_toolbox/docs/validation/foundation-engine-transport-production-20261026/README.md)。本页不把先前九个 original 桩、36 窗口提交器或 12-family Configure 的检查数量重复计入。

本批报告使用 docs-generator 的 Evidence → Finding → Path 规则。网络读取仅用于查阅微软公开展开实现与 API 说明；这些公开代码用于提出修正假设，不是本机 OS 二进制与它完全相同的证明。最终结论以本页归档的实际 `RtlLookupFunctionEntry / RtlVirtualUnwind`、机器状态和进程输出为依据。

## 最终 r4 具体通过了什么

| 范围 | 实际结果 | 证明边界 |
|---|---|---|
| 路线与对照 | 23 个 site、31 条路线，每条 control/relay 各执行一次 | 八个条件点覆盖两路；不证明固定游戏的所有间接入口 |
| 源补丁执行 | 每路线两份实际源 E9 CONTEXT，共 62 份 | 实际从私有映像 physical patch PC 经 E9 跳入 control/relay；使用原 source RF 展开 |
| 近块逐指令 | 208 份实际 TF CONTEXT | 每条路线的全部 required instruction PC 恰好出现一次；含真实返回 PC 与 terminal E9 |
| 回调栈遍历 | 28 次实际 Entry → relay return PC → owned caller | C++ 内用实际 `RtlCaptureContext` 起步，没有手填 CONTEXT 或改返回槽 |
| 状态保真 | 31/31 stateEqual、memoryEqual、LastError 保留 | 对照 GPR16、XMM16、除 TF 外全部 flags、MXCSR、FP control 和重放引起的内存写 |
| 展开 | 298/298 记录恢复准确 caller RIP/RSP、8 个独立 NV seed 与 XMM6..15 | 不是“每 PC 抛异常”的证明；没有声明 SEH/C++ 传播已覆盖 |
| Dispatch 部分帧 | 四 site、六路线全部 poisonVerified | 实际 `[sourceRsp+168h]` 保持 poison，展开保留正确 live RBX，不读取尚未执行的 late save |
| 三个扩大块 | site 9、11、14 的零路径各 0 次 probe，非零路径各 1 次 | 实际走专用 zero replay；不虚构 Begin/Returned 观察 |
| 表寿命 | 31 次 source table Add/Delete、31 次 near table Add/Delete | source 表每次 27 项，near 表每次 24 项；失败保留对应存储，成功删除后才释放 |

实际记录见 [child-result.json](D:/riz-build-cache-next/engine-transport-relay-machine-r4-20261027/child-result.json)，SHA256 `8ca6d6fb32f85a63bf61bc47f7e6b0c1fa3664dd1db57dbf901732416a36fb64`；[stdout.log](D:/riz-build-cache-next/engine-transport-relay-machine-r4-20261027/stdout.log) 与持久化 child JSON 去掉外层空白后完全相同。父 runner 核实际 PID、schema、route 唯一性、每份 required/observed PC、逐个恢复寄存器值、表计数与文件哈希，不能只接受顶层 `passed=true`。

两份额外源 E9 记录不挪用既有 near requiredPoints 的键：每个 variant 必须有且只有一个 `source="actual-source-patch"`，PC 等于该路线真实物理补丁地址；记录的 actual RF Begin/End/Unwind 必须匹配原 source RF，并覆盖这个 PC。其余 208 份 `single-step` 与 28 份 `actual-probe-stackwalk` 保留原核验。三类相加恰为 **298**。

## 真实执行路径与机器帧

[RelayMachineTargets.asm](C:/Users/71957/Desktop/riz_toolbox/native/RizToolbox.EngineTransportRelayMachineFixture/RelayMachineTargets.asm) 先在实际 ASM caller 中写入独立的八个 NV seed 和 XMM seed，再用真实 PUSH/SUB/MOV 保存形成下列 source 栈形；没有在 C++ 中构造假栈或伪造返回地址。

| source 栈形 | 相对 source entry 的实际栈变化 | 对应 site |
|---|---:|---|
| Start | `168h` | 0、1、2、14、15、16 |
| Drain | `38h` | 3、4 |
| Yield | `B8h` | 5、6、7、8 |
| Detach | `28h` | 9、10 |
| Release | `28h` | 11、12、13 |
| Dispatch | `158h` | 17、18、19、20 |
| Batch | `58h` | 21、22 |

进入真实 source E9 时 `sourceRsp` 为 16 字节对齐，wrapper 使用 JMP 而非 CALL 进入它；source E9 再 JMP 到当前 control/relay。真正 probe CALL 把 RSP 变成模 16 为 8 的 ABI Entry 状态，三份既有 Entry ASM 实际保存完整机器帧、调用 fixture Dispatch 并恢复。未保存的 source NV 寄存器保持 caller seed，已保存的寄存器可承载该 source 的 live 值；四 Dispatch 的 RBX 尚未保存，不能为了方便而假执行晚到的 RBX save。

八个有两路的 site 为 `3,6,9,11,14,17,19,21`，故 `23+8=31` 条路线。三条 expanded-zero 路线为 `(9,0)、(11,0)、(14,0)`；六条 poison 路线为 `(17,0)、(17,1)、(18,0)、(19,0)、(19,1)、(20,0)`。这些集合由最终 JSON 逐路线核对，没有把重复路线计作新增 site。

`control` 在 near arena 的专属 unused 区重放同一固定 window、准确 RIP-relative 操作数与分支目的地，省去 probe；relay 是实际 `Prepare` 的产物。独立 control emitter 同时生成执行 PC 要求并逐字节核 relay 机器码。落地点是私有映像中的明确自有 capture CALL＋cleanup 跳转，未进入原游戏 continuation 指令流；near 中执行的 displaced-instruction 副本仍属本次被测语义，不能说“没有执行任何重放字节”。

TF 在真实 ASM `POPFQ` 后生效；VEH 记录实际 Windows CONTEXT。到达 fixture C++ Dispatch 时暂时清 TF，Entry 在此前真实 `PUSHFQ` 保存的 TF 随其正常 `POPFQ` 自然恢复；没有手改机器帧、返回地址或 debug register。28 次自然恢复与 28 次真实探针对应，`cppDispatcherSingleStepped=false`。flags 对照只排除 instrumentation 的 TF 一位，没有顺带屏蔽分支 flags。

## 为什么保留 source RF 和 synthetic partial-primary

### r1 到 r3：先修夹具缺项

r1 的私有 PE 不是已加载的 MEM_IMAGE，只有 23 relay＋1 control 的 near table 被登记，原 source 的 27 个 RF 没有向 Windows 登记。近块 E9 跳回 source continuation 时，Windows 无法从目标 PC 找到原函数身份。r1 仍实际执行完全部 31 路线、28 次 probe walk；31 组状态/内存/LastError 对照均一致，但 55 个 CONTEXT 在 E9 位置展开失败，另带来 3 项 return-PC 和 4 项 poison 汇总失败。

Stage1 把 27 个固定 source RF 复制到单独、稳定的 private table，排序、核非重叠后改为只读，再实际登记；不把 .pdata 内存可读误当作 OS 已登记。逐项 start/end、全部 source continuation/branch target、三个 bypass continuation，以及 near 表登记后的当前落点都核 `RtlLookupFunctionEntry` 的实际表指针、image base 和 RF tuple。source 与 near 表分别计数并删除。

r3 修正后，19 个原 source CHAININFO relay 的实际展开通过；剩余失败只有四 Dispatch 非零/直线路线的 terminal E9：8 份 control/relay CONTEXT 和 4 项 poison 汇总检查。这个递进表明不能把 r1 所有 E9 失败直接宣布为生产代码缺陷，也不能因补齐 source 表就签四个独立 partial 记录正确。

### r4：四个部分帧保持原函数身份

真实 Dispatch RF 为 `5A5260..5A5343`，完整 source prolog 长 `4Eh`。晚到的 RBX 保存指令在 `5A52A6..5A52AE`；site 17..20 的 source PC 在此前，只有 PUSH RDI 和 SUB RSP,150h 已完成。直接链完整 source unwind 会错误读取尚未写入的 RBX home；单独的 partial unwind 虽在普通 near PC 正确，却把 near relay 变成不同 primary 身份，r3 的外跳 E9 仍展开失败。

当前四个 relay 的外层记录统一为 16 字节 CHAININFO：

```text
slot+176: 21 00 00 00
          parent.BeginAddress = 005A5260
          parent.EndAddress   = 005A5343
          parent.UnwindData   = relayRva + 192

slot+192: 01 00 03 00 00 01 2A 00 00 70 00 00
          Version1 / Flags0 / Prolog0 / 3 UWOP slots
          ALLOC_LARGE 150h, PUSH_NONVOL RDI; no RBX save
```

这个父 RF 是**显式合成记录**：它保留真实 source 的 Begin/End，UnwindData 指向 near 中的 12 字节部分展开信息，并不冒称原 source unwind RVA。其余 19 个 relay 继续链到准确原 source RF。Fixture 逐字节核外层与 nested partial；control RF 也引用同一个实际 `plan.site.unwindRva`，不复制一个错误的相对位置。

微软公开 [CoreCLR AMD64 unwinder](https://raw.githubusercontent.com/dotnet/runtime/main/src/coreclr/unwinder/amd64/unwinder.cpp) 中的 `SameFunction`/E9 处理曾用于提出身份假设；[RtlVirtualUnwind API 说明](https://learn.microsoft.com/en-us/windows/win32/api/winnt/nf-winnt-rtlvirtualunwind)也列出跨函数直接跳转的 epilog 识别条件。公开实现不是驻留 Windows OS 二进制的等价性证据。**r4 的 298 份实际 Windows 展开，尤其新增的 62 个 source E9 PC，才是当前方案在本机这个固定合同上的验收依据。**

生产 V5 需要完整描述的 RF 库存因此为 `27 source + 13 original + 23 relay + 4 synthetic partial-primary = 67`。本机器夹具没有链接或执行那 13 个 original；它实际登记 `source27 + near24` 两表，其中 near24 含额外 control1，另有 4 个 CHAININFO 内嵌父记录，合计 55 个描述符。不能把“后续发布记录需容纳 67 项”写成“同一夹具实际登记或执行了 67 项”。先前 original 桩证据继续属于它自己的冻结构建。

## 四轮原件与时间线

时间取各 manifest 的 UTC；目录中的日期不替代实际进程时间。

| Evidence / 原件 | UTC / 实际进程 | 原始结果 | 本轮解释 |
|---|---|---|---|
| E1 [r1 manifest](D:/riz-build-cache-next/engine-transport-relay-machine-r1-20261026/test-result.json)、[child](D:/riz-build-cache-next/engine-transport-relay-machine-r1-20261026/child-result.json)、[stderr](D:/riz-build-cache-next/engine-transport-relay-machine-r1-20261026/stderr.log) | 10-26 13:52:59..13:53:11；PID 68644 / exit 1 | build 0；31 路线；4489 子检查、62 失败；near 表 31/31 | 55 展开＋3 return-PC＋4 poison；source 表未登记 |
| E2 [r2 manifest](D:/riz-build-cache-next/engine-transport-relay-machine-r2-20261027/test-result.json)、[build.log](D:/riz-build-cache-next/engine-transport-relay-machine-r2-20261027/build.log) | 10-26 23:56:41..23:56:50；没有 child | build 1；父检查 13 | C4244：landing RVA 的 U64→U32 转换被 /WX 拒绝；修为显式范围检查后转换 |
| E3 [r3 manifest](D:/riz-build-cache-next/engine-transport-relay-machine-r3-20261027/test-result.json)、[child](D:/riz-build-cache-next/engine-transport-relay-machine-r3-20261027/child-result.json)、[stderr](D:/riz-build-cache-next/engine-transport-relay-machine-r3-20261027/stderr.log) | 10-26 23:57:16..23:57:27；PID 44548 / exit 1 | build 0；31 路线；8241 子检查、12 失败；source/near 各 31/31 | source27 登记后，仅四 Dispatch terminal E9 的 8 CONTEXT＋4 poison 失败 |
| E4 [r4 manifest](D:/riz-build-cache-next/engine-transport-relay-machine-r4-20261027/test-result.json)、[child](D:/riz-build-cache-next/engine-transport-relay-machine-r4-20261027/child-result.json)、[stdout](D:/riz-build-cache-next/engine-transport-relay-machine-r4-20261027/stdout.log) | 10-27 05:23:08..05:23:23；PID 29688 / exit 0 | build 0；31 路线；9742 子检查、4019 父检查、0 失败 | source E9 实际执行与 nested partial-primary 共同验收；298 份实际记录全通过 |

E1/E3 的父 runner 在 child exit 非零时停止成功解析，因此顶层 `child=null / childChecks=0`；独立保存的 `child-result.json`、stdout 与实际 exit 仍是失败诊断原件，不能把 manifest 未解析改写成“child 没有跑”。E2 没有 child，不能补写或推测机器结果。四轮均没有 timeout 或强制终止；原件没有以 r4 覆盖。

| 原件 | manifest SHA256 | source ZIP SHA256 |
|---|---|---|
| r1 | `1cf57e0423b04691ee006394e4d69a8a2b37fd011b57a31a640696682240383c` | `a8820fbc1716329b0f93bf03cfa69b7495f923462643088c120c38e6251e9166` |
| r2 | `561a7565d2945464c4ce5c28a8615ff4b93cee196d505325f772d718357b0623` | `be7f878dcacaf4290a487fa60d68773c8b41f815cdde306b939940211705aaae` |
| r3 | `de48653d4e24ea08b435410ff728d7e50fda85d900ec4223657e7f65aa3173cc` | `c4e4d3e891c1e94a19ef015d87639fe1d94c3a17f903f9a47c3cb424a6f0d4f2` |
| r4 | `11bab49fa22bcfb1460639e45ea61a0a4b40a5dd1d5685e8e32c39b018191be9` | `c1ca38694f6fc3dc842ded54b44cc1d48512b365985d4f2f6906136da58adbf3` |

本次文档核对了四个 manifest 声明的 **25、7、25、25** 项输出，全部 SHA/长度一致；四份 ZIP 各 10 个成员，CRC 与逐成员来源 SHA 均吻合。游戏文件未收入 source ZIP。`source-before.json` 是 10 项源码＋1 项游戏文件，`source-after.json` 只列 10 项源码，游戏另存 `unity-disk-after.json`；按相同来源对齐后，四轮源码均无变化，游戏 SHA/字节数亦一致。不能机械比较 11 项与 10 项清单而误报来源漂移。

## 冻结输入与可复核产物

最终 [source-before.json](D:/riz-build-cache-next/engine-transport-relay-machine-r4-20261027/source-before.json)、[source-after.json](D:/riz-build-cache-next/engine-transport-relay-machine-r4-20261027/source-after.json)及 [relay-machine-fixture-sources.zip](D:/riz-build-cache-next/engine-transport-relay-machine-r4-20261027/relay-machine-fixture-sources.zip)对应以下 10 项源码；本次报告读取时 live SHA 也逐项匹配。后续源码修改应使用新冻结构建，不倒填本表。

| 源文件 | 字节 | SHA256 |
|---|---:|---|
| [RelayMachine.cpp](C:/Users/71957/Desktop/riz_toolbox/native/RizToolbox.EngineTransportRelayMachineFixture/RelayMachine.cpp) | 49133 | `6571c983c802fec174b6f0750e933216c781231e75daf35d5b458f0611bd4fe5` |
| [RelayMachineTargets.asm](C:/Users/71957/Desktop/riz_toolbox/native/RizToolbox.EngineTransportRelayMachineFixture/RelayMachineTargets.asm) | 11110 | `cb8a89e570ef658e31dbd51373255d0e47d483e77b11d38b48910133c7ca993c` |
| [EngineTransportRelays.cpp](C:/Users/71957/Desktop/riz_toolbox/native/RizToolbox.EngineBootstrap/EngineTransportRelays.cpp) | 24846 | `4e7f10d10f9d328a59622f43adfd5e333a294a3f4a2ed6fb89ca6f080316f781` |
| [EngineTransportRelays.h](C:/Users/71957/Desktop/riz_toolbox/native/RizToolbox.EngineBootstrap/EngineTransportRelays.h) | 4795 | `587066825d688ac344d3a104977b744a7c368b5a8c2b43a7a0c2dfa60d7c87b1` |
| [EngineTransportGameProfile.h](C:/Users/71957/Desktop/riz_toolbox/native/RizToolbox.EngineBootstrap/EngineTransportGameProfile.h) | 80885 | `de2c9593aaf0d4ef11765ebf0521b2ba4a3a855af3f4420365cb497f534c213c` |
| [MidContext.h](C:/Users/71957/Desktop/riz_toolbox/native/RizToolbox.JudgeContext/MidContext.h) | 3782 | `080a44623dd0fad4f5facdb16d9b8307becc0f06bac56446643885bdfb01e989` |
| [EngineInlineProbeEntry.asm](C:/Users/71957/Desktop/riz_toolbox/native/RizToolbox.JudgeContext/EngineInlineProbeEntry.asm) | 3897 | `215e73400335765f5a7deab9da8bd437704eb9c962fd0c5a86f4024d7da18518` |
| [EngineSchedulerEntry.asm](C:/Users/71957/Desktop/riz_toolbox/native/RizToolbox.JudgeContext/EngineSchedulerEntry.asm) | 3903 | `0859cf6d48a2f7cccb33aa3559165f973d933396bc396d03b59e90e701508c9a` |
| [EngineWaitEntry.asm](C:/Users/71957/Desktop/riz_toolbox/native/RizToolbox.JudgeContext/EngineWaitEntry.asm) | 3891 | `a81eb3b60a3d422cfdd4bdc4e57b81a43e9974fb777aabcdf439418e3b170334` |
| [Test-EngineTransportRelayMachine.ps1](C:/Users/71957/Desktop/riz_toolbox/scripts/Test-EngineTransportRelayMachine.ps1) | 24927 | `1d63e7cef1b99e0fa987c5c24975832bd525bfd6c7817e05d318062c0d8ee42c` |

最终 EXE 为 [EngineTransportRelayMachine.Fixture.exe](D:/riz-build-cache-next/engine-transport-relay-machine-r4-20261027/EngineTransportRelayMachine.Fixture.exe)，142336 字节，SHA256 `0016ccba945d3dfb985df31432c89443ce84ced0ef7a183400756f06b6b0de6a`。实际命令保存在 [build-relay-machine-fixture.cmd](D:/riz-build-cache-next/engine-transport-relay-machine-r4-20261027/build-relay-machine-fixture.cmd)，PE/展开原件在 [fixture.pe.txt](D:/riz-build-cache-next/engine-transport-relay-machine-r4-20261027/fixture.pe.txt)。编译为 MSVC VS2019、C++17、`/W4 /WX /O2 /EHa /MD /guard:cf`，四份 ASM，链接 `/OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF`。这是带常规测试 CRT 的自有 EXE，不冒称生产无 CRT DLL。

## Evidence → Finding → Path

以上 E1–E4 均为 `source_type=file/log`；`source_ref` 为对应绝对原件链接，`observed_at` 为 manifest UTC，`linked_workitem=transport-relay-machine`。源码与冻结输入为 E5；公开展开实现仅为 E6 的研究参考。所有 validated 结论均至少引用一份实际进程证据，不以公开实现或顶层状态代替实际 PC/寄存器记录。

| Finding | 状态 / 严重性 | Evidence | 影响与处置 |
|---|---|---|---|
| F1 私有映像必须登记 source RF 才能模拟真实跨区域身份查询 | validated / n/a_re | E1、E2、E3 | 补齐 source27 后，19 个原 chain 的实际展开通过；保留失败原件，后续夹具不能省略这个来源 |
| F2 四个早期 Dispatch 需要部分帧展开与一致 source 身份 | validated / n/a_re | E3、E4、E5 | 外层 CHAININFO 指向明确 synthetic partial-primary；实际 poison、source E9、terminal E9 展开均通过，不伪用 late RBX save |
| F3 源 E9 的正确性必须在源 PC 上取证 | validated / n/a_re | E4、E5 | 62 份实际 CONTEXT 使用原 source RF；不以 near PC 展开正确推断源补丁 PC 正确 |
| F4 机器保真不能代替真实 Kernel/生产生命周期闭环 | validated / n/a_re | E4、E5 | 三 Entry ASM 的 fixture Dispatch 只验证保存/恢复与栈；真实 Kernel ledger、完整 V5 发布、只读 CLR owner 和游戏验收另行完成 |

P1，`path_type=callflow`：只读固定磁盘 SHA → 私有映像/arena 分配与实际 Prepare → 核 19 原 chain＋4 synthetic partial-primary → 登记 source27 与 near24 → 私有 source 写自有 E9/owned landing → 真实 ASM 栈与 JMP → source TF/relay TF/Entry 栈遍历及状态对照 → 两表实际 Delete 后释放。每步有 E4/E5 对应字段与字节核验；F1–F3 的终点是本有界传输正确性，不是生产 Ready。

本页没有新增 IOC、ATT&CK 或外部攻击结论；任务为用户授权的本地游戏 Mod 兼容底层开发，相关恶意样本分类为不适用。

## 复核与后续接线

在同一 Windows x64 主机、相同固定 DLL 和冻结源码下，使用**尚不存在的新输出目录**运行；以下 r5 目录在本次报告检查时不存在：

```powershell
& 'C:/Users/71957/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe' -NoProfile `
  -File 'C:/Users/71957/Desktop/riz_toolbox/scripts/Test-EngineTransportRelayMachine.ps1' `
  -OutputDirectory 'D:/riz-build-cache-next/engine-transport-relay-machine-r5-20261027'
```

脚本会锁定 10 项源码与真实游戏文件、归档 source ZIP、严格构建、运行一个最长 60 秒的自有 child，再核实际 JSON/PC/寄存器/哈希。拒绝覆盖已有输出；超时只允许终止本次创建的 Process。此命令在本次文档工作中没有执行，归档也没有冻结整个 Windows/MSVC/SDK 环境，不能据此保证换机产物逐字节相同。

后续将这份机器证据与 [36 窗口提交器](C:/Users/71957/Desktop/riz_toolbox/docs/validation/foundation-engine-transport-source-writes-20261026/README.md)、[真实 Kernel 的 12-family Configure](C:/Users/71957/Desktop/riz_toolbox/docs/validation/foundation-engine-transport-families-20261026/README.md)接入同一个实际 V5 发布链。完整 native publisher、44 普通依赖图、67 项 RF 发布身份、只读 CLR owner、首次全 family Attach 与真实执行窗口必须各自验证；不能把本页的 private table 删除扩张为已配置 Kernel 永久 claim 的释放许可。游戏实测继续按用户安排暂缓。
