# 四个引擎节点入口：首次安装时机与 receiver 来源

2026-10-25。**当前 Doorstop 的 pre-il2cpp_init 点尚不足以证明 UnityPlayer 四个节点入口在此前从未执行，不能据此普通晚期首 patch。** 固定 EXE 确实存在更早的 UnityMain2 调用边界，但该边界晚于 UnityPlayer CRT 初始化。已实际读到 8 个非空 C initializer 和 3006 个非空 C++ initializer 表项，没有关闭其全部间接行为；本研究不签发 V4/Ready，也没有修改游戏入口。

另已确认 StartCoroutineManaged2 的 native owner 来自受 GC write barrier 保护的 managed receiver 的 +0x10 缓存字段，绝非 managed 对象地址本身。scheduler 实际消费完整 EAX，故本批修正 Execute 为 uint32 返回；此前仅 AL 的传输不足生产使用。

## Evidence 与复核方式

本研究应用 ida-reverse 和 docs-generator，flavor=null。只读取磁盘 PE 和已冻结 Doorstop corresponding source，没有映射 DLL、运行游戏、操作 UI、安装或构建。当前没有可调用 IDA MCP，使用已有 Python 3.14、pefile 2024.8.26、Capstone 5.0.6；脚本只重写新 research 目录的 JSON。范围承接[前批已授权代码与只读研究边界](../foundation-engine-node-20261025/scope.md)，完整目标仍为 B01–B19。

| E-id / source_ref | 现场 SHA-256 | 实际读取范围 |
| --- | --- | --- |
| E-script：[inspect-engine-installation.py](research/inspect-engine-installation.py) | `9e59d9e6e55dbfb437071641fff948c91b1b2baaa6a0070d26184b64101a72f9` | 固定输入、primary RF/CHAININFO、有限 RIP 引用及直接 call/jmp 边界核实 |
| E-json：[engine-installation-evidence.json](research/engine-installation-evidence.json) | `c44de32c5abdc88b5f75d34c056bf4972f0bda7429505676bd6c2a5c08e2441b` | 32 个完整 UnityPlayer RF 区间均完整 linear 解码，另一个无 RF leaf 的 32 字节入口窗口；17 条四入口直接引用、EXE 启动、TLS、CRT 表及 Doorstop 实际 source |
| E-unity：`D:/SteamLibrary/steamapps/common/Rizline/UnityPlayer.dll` | `43fb0aea1b80c74963dae88ec7fc1c3c1993d893ce94d12972000fcc9b653ad1` | AMD64，ImageBase 180000000，ImageSize 1E65000；原始机器码/pdata/unwind |
| E-exe：同目录 Rizline.exe | `f9c37f1078619d02c62a7a5815ca48179666efb2dfe7e6e698bfff7858c26e5e` | EXE entry 1264、CRT main、leaf 1000、静态导入 IAT |
| E-game：同目录 GameAssembly.dll | `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79` | exports/imports、entry/TLS；未执行 init |
| E-doorstop：E-json 的 doorstopSourceArchive | 归档 hash 和四个 source hash 均在 JSON 内 | 从上批实际暂存 corresponding-source ZIP 读取 entrypoint.c/bootstrap.c/riz_preinit.c/riz_init_call.c；未修改历史档案 |

从仓库根复核：

```powershell
C:/Python314/python.exe docs/validation/foundation-engine-binding-20261025/research/inspect-engine-installation.py
```

命令重读固定 DLL 并重写 JSON；DLL hash 不同即拒绝。脚本只读原生输入，不将字节搜索命中直接当作完整调用闭包。新打包可能改变 corresponding-source ZIP 的归档 hash，应同时核 JSON 中单文件 source hash。

## F-startup：当前点缺少的是具体哪一段证明

severity=n/a_re，category=design，status=validated 静态事实；evidence_ids=E-json/E-unity/E-exe/E-doorstop。

P-startup，path_type=callflow：

1. EXE entry 1264 调 14C8 后在 1271 跳 `[10F0,1261)`；CRT main 在 11F1 调 leaf 1000。leaf bytes `33D248FF2537D20000` 即 xor edx,edx 后跳 EXE+E240 的 IAT，静态名称为 UnityPlayer.dll!UnityMain2。
2. UnityMain 和 UnityMain2 两个 export 实际同指 `[808690,8086B6)`；808696 的 `E8D5DFFFFF` 调 `[806670,80868A)`。两个名字不能当两个独立安装入口。
3. 主体 807985 的 `E836E3FFFF` 调 `[805CC0,8065E5)`。后者用 GameAssembly.dll 字符串构造输入，805D13 的 `E858CFF7FF` 调 `[782C70,784F85)`。
4. 该函数经 `[6B60C0,6B67FF)` 的 6B625D 实际调用 LoadLibraryW，之后在 782C8F 取 il2cpp_init 字符串 19B4918，782C98 调统一 resolver `[6B68F0,6B6A1D)`。resolver 的 6B692C 经 UnityPlayer IAT +18647C0 调 GetProcAddress；782C9D 把返回值写到 +1D1CC68。
5. 当前 Doorstop DllEntry 的 inject 修改 UnityPlayer GetProcAddress 等 IAT；名字为 il2cpp_init 时返回 init_il2cpp。真正 RizBeforeIl2CppInit 直到该 wrapper 被调用才执行。它没有替换 EXE 的 UnityMain2 IAT，也没有四节点早期调用历史。

到步骤 5 前，UnityPlayer 已经历 PE entry 17E7C08、CRT 调度 `[17E7AD4,17E7C05)` 和进程 attach 路径 `[17E7938,17E7A4E)`。17E799F/17E79A6 给初始化执行函数范围 `[186AE80,186AEC8)`，9 slots 中 8 非空；17E79BF/17E79C6 给另一范围 `[1865070,186AE68)`，3007 slots 中 3006 非空。用户 DllMain leaf 33E500 自身为 `B801000000C3`，并不排除前面的 CRT initializer 间接行为。

TLS callback 17E7708 仅 EDX=2 进入主体，17E7780 仅 EDX=0/3 进入主体；进程 attach 的 EDX=1 下两者直接返回。这只能关闭这两个直接 TLS 分支，不能据此省略 CRT 表。

**没有证明 pre-init 已经太晚并且四入口确实被调用，也没有证明它足够早。** 当前缺口为 CRT/间接调用闭包、此前线程和节点历史，结论是 Unknown。GameAssembly 原 init 尚未执行不能代替这部分证据。

## F-callers：实际直接引用比一个 managed wrapper 更广

status=validated 静态引用；evidence_ids=E-script/E-json/E-unity。脚本对四入口的 17 条 E8/E9 引用逐条验证了完整 primary RF 中的真实指令边界。

| 实际 source RVA | 目标 RVA | 来源 |
| --- | --- | --- |
| 100A47、100E14、102E1C | 793840 Detach | 三种 Stop binding |
| 102B12 | 792240 Start | IEnumerator Start binding |
| 78F6B1 | 78F680 Release | Release 递归 |
| 78F988、78F9EC、78F9F7 | 78F680 Release | Execute 内释放 |
| 78F9E4 | 78F840 Execute | Execute 内 nested 执行 |
| 78FE9D | 792240 Start | `[78FC50,79014B)` |
| 7924BA | 78F840 Execute | Start 同步首执行 |
| 7924DD、7925A5 | 78F680 Release | Start 清理分支 |
| 792A21 | 792240 Start | `[7929C0,792A44)` |
| 793C56 | 793840 Detach | IEnumerator 匹配节点 |
| 794EC4 | 793840 Detach | `[794E80,794EF0)` |
| 78F66E | 78F840 Execute | scheduler callback 尾跳 |

全部每条 source bytes、parent RF/body hash 在 JSON 中。该扫描没有枚举所有地址取值、vtable、absolute pointer、动态 icall 缓存或内联写者；不能将 17 条 direct reference 描述成全部 producer 已封闭。只控制一个 managed Start/Stop wrapper 不足以覆盖已发现的其他真实 native 来源。

## F-owner：receiver 与 native owner 的精确关系

status=validated 固定机器码；evidence_ids=E-json/E-unity。

StartCoroutineManaged2 `[1029F0,102C54)` 的 102A04 `488BF1` 保存原 RCX 到 RSI，即 managed receiver；随后经函数指针 +1D1CF28 多次写入实际局部保护槽。该指针来源不是猜测：783D31 `488D1550302301` 指向 il2cpp_gc_wbarrier_set_field 字符串 +19B6D88，783D38 `E8B32BF3FF` 调前述实际 GetProc resolver，783D3D `488905E4915901` 把返回指针写到 +1D1CF28。

102AA2 `488B442430` 取 RAX=[rsp+30]，即 receiver 保护槽；102AB2 `488B7810` 令 RDI=[RAX+10]。cached 分支取 [rsp+38]，必要的再次转换位于 102ADD，同为 `488B7810`。102AEB 从这个 native owner 的 +30 取值检查，最终 102B0F `488BCF` 把 RCX=RDI，102B12 调实际 Start。IEnumerator 来自另一 barrier 局部槽，经 R11 在 102B0C 送 RDX；R8 清零，R9 为实际 node 输出槽。

运行时仍需实际 metadata 验证 UnityEngine.Object.m_CachedPtr 的属性、offset 和对象类链，保留真实 receiver root/run/slot/generation，再在合法原生窗口复制 owner。固定 +10 的机器码事实不授权 CLR 任意裸读，不证明销毁/GC 竞争闭合，也不允许直接把 managed receiver 地址充当 native owner。

## F-eax：Execute 的返回宽度必须修正

status=validated 固定机器码；evidence_ids=E-json/E-unity。scheduler pump `[447D60,447FAF)` body hash `cafab343388f190607027203284728c3ce672cfe89115a50257e0faae66e3d7c` 在 447EEA `41FFD7` 调 callback，447EF2 `83F801` 明确为 cmp eax,1，再条件调用 cleanup。callback 78F5E0 尾跳 Execute，后者正常以 EAX=0/1 返回。因此 Execute 契约是 uint32(node,byte* output)，完整 EAX 不能从 AL 低字节复原。

Start/Release 保持 uint8，Detach 保持 void。旧 engine-node 批冻结证据只验证当时低 byte 样本，不改写历史；新 helper 增加 GetAbiVersion=2，scalar record Version1、Frame112/Completion32/Info112 不变。新 bridge 须拒绝缺版本 export 的旧 helper。该 ABI 版本仅修正传输，不授生产 Ready；本批真正动态结果由 root 构建和原件单独记录。

## P-admission：下一条可实施的证据路线

当前候选是比 init wrapper 更早的 UnityMain2 之前的原生启动门；**尚未验证、尚未用于游戏首写**。它必须先用同形状自有宿主和 DLL 证明以下协议，不能通过在已运行入口安装一个 gate 来反证该 gate 自己的第一次写安全。

1. 从进程创建、模块加载和每个新线程开始前建立完整调用准入历史，覆盖 CRT initializer、main 前 worker 和四节点第一次执行；不能使用事后一次线程 snapshot 代替历史。Windows 官方 debug event 会暂停受影响进程全部线程，新线程事件在用户态开始前产生，提供可测试的原生控制基础，但其具体 loader 时序仍需实证。[Microsoft Debugging Events](https://learn.microsoft.com/en-us/windows/win32/debug/debugging-events)
2. 自有宿主在 initializer 或抢先 worker 调用四入口时，门必须真实发现并拒绝发布；没有完整早期历史、存在不能管理的线程或当前 PC/未完成责任处于待改区间时，保留 Unknown。UnityMain2 IAT 门本身不能抹除早期入口事实。
3. 证明所有原执行线程保持准入关闭期间完成全部准备和不可变 Configure，再发布并生成每行 module/RVA/kind/slot/target/original/entry/epoch/bytes/protection 证据。写后失败保留全部实际责任，不能假回滚或恢复执行。逐第 N 写故障、线程创建和错误恢复必须由自有 fixture 覆盖。
4. 复杂初始化应在 DllMain 外完成，不将加载 helper/Dobby 和跨线程等待塞入 loader 初始化窗口。这里仍缺安全 bootstrap 的实际实现和 loader 边界证明，故没有可签发的 V4。[Microsoft DllMain](https://learn.microsoft.com/en-us/windows/win32/dlls/dllmain)、[DLL Best Practices](https://learn.microsoft.com/en-us/windows/win32/dlls/dynamic-link-library-best-practices)

实施顺序是先取得上述自有宿主的真实拒绝/正常发布/故障保留证据，再核固定游戏冷启动是否满足同一合同。当前 Doorstop 的 V3 GameAssembly 18 行/27 写、当前引擎低层已配置通道和 Prepare/Commit 本身都不能替代这份首次准入证明。

## 本次边界

本次已产出启动 RF/bytes、17 条直接引用、CRT 实际表、receiver+10 和真实 write barrier 绑定，以及完整 EAX 消费者证据。没有找到可凭当前证据立即安全发布四入口的现成 Doorstop 点；更早门仍为 candidate。本批没有给游戏四入口做首次 patch，EngineCancelled=false、Pending 保留、RequestPlay 关闭，完整 B01–B19 目标不变。
