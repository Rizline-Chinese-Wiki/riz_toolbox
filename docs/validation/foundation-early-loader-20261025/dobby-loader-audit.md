# 固定 Dobby 的 loader 阶段调用闭包：有限静态审查

2026-10-25。**现有固定 Dobby 可以继续作为自有早期加载 fixture 的被测实现；本审查不能批准把它放入游戏的生产 loader 阶段。** Prepare 的正常新记录路径依赖 CRT 堆和动态路由对象，错误/日志分支存在通向迟到加载、CRT 文件流和锁的真实指令链，下游仍有未解间接调用。39 个自有 child 成功或 debug 观察到 DLL attach 返回，都不能关闭未走分支、OS API 内部与全部并发状态。

采用 ida-reverse 的工具核验流程；当前工具索引无 callable IDA，使用本机 Python/pefile/Capstone 进行固定磁盘 PE 有界分析。作者同时实现本批 Proxy/Recorder，本稿是作者自查及依赖有限研究，不称独立第三方审计。本 agent 未构建、执行 Dobby、启动游戏、安装或操作 UI。范围见 [scope](scope.md)。

## Evidence 与方法

source_type=file，observed_at=2026-10-25；[研究脚本](research/inspect-dobby-loader.py) SHA256 `a650021139c8803110b80931ac5735a8a12045564a691ef16a4f460c9e19bc25`，[完整原始 JSON](research/dobby-loader-evidence.json) SHA256 `54ff9f326226f0e2fef9d4ad3d8f88de1264378f47b9f75d2a5e3f4c6d1b04ac`。复核命令为 `C:/Python314/python.exe docs/validation/foundation-early-loader-20261025/research/inspect-dobby-loader.py`，仅读磁盘 DLL并重写该 JSON，不映射或执行目标。

输入为 `.tools/bepinex-789/BepInEx/core/dobby.dll`，205312 字节，SHA256 `8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded`，ImageBase `180000000`、SizeOfImage `38000`、entry `554c`，TLS directory 和 delay-import directory 均为0。普通导入只来自 KERNEL32；这既不代表全部被用到，也不等于“没有静态 CRT”：大量 CRT 实现在本 DLL 内。

脚本实读 `.pdata/.xdata`，递归合并 CHAININFO，保留每段原字节、hash、完整 RF/unwind header。**每个 RF 分段单独取 bytes，不把多个范围间隙算进函数。** 指令采用分段入口和直接内部分支 CFG；ret/无条件跳转/未解间接跳转处停止，避免把内嵌 switch table 线性解成调用。568 个被请求 region 中无 CFG 指令解码错误；raw RF 的部分线性扫不完整是内嵌数据诊断，不作为 CFG 完整或间接表已恢复的主张。64字节 leaf 只作为有界基本块；超出范围及未知分支仍未证明。

| 图 | 已访问函数/leaf | 未解间接 transfer | 含义 |
| --- | --- | --- | --- |
| Prepare 直接图 | 391 | 24 | 从3060出发，普通直接call/branch；包含错误分支，未证明条件同时可满足 |
| Commit 直接图 | 382 | 22 | 从2d30出发，同上 |
| Prepare + 明示 fresh vtable 补充 | 445 | 68 | 只补四个有构造证据的候选vcall；不代表一般虚调用闭包 |
| Commit + 同表补充 | 384 | 24 | 仍有对象/缓冲区虚调用、guard dispatch等未知 |
| CRT entry直接图 | 258 | 47 | 不含所有 initializer表/guard dispatch 的动态目标，不能叫完整初始化证明 |

各条 import site 在 JSON 保存真实指令 bytes、IAT RVA、所在 primary RF 和最短静态路径；这些是比“导入表有该函数”更具体的候选证据，仍不是路径可行性或运行事件。

## Findings

### F-heap：新记录和 singleton 明确依赖 CRT 堆

status=source-reviewed，severity=n/a_re，category=design。

Prepare 新记录路径 `3176 mov ecx,78 → 317b call4d38`，之后 `31bd mov ecx,40 → 31cf call4d38`。`4d38` 是分配/失败重试形式，`4d52 call e4e0`，`e4e0` tail-jmp `114b8`；后者 `114ea` 从全局 `32bc8` 取堆句柄，`114f6` 实际调用 IAT `1f170` 的 HeapAlloc。失败路径又进入 `edbc` 等 new-handler/error 逻辑。不是只因 DLL 导入 HeapAlloc 而推断调用。

`3d10` 与 `3d50` 分别首读 `31a08/31a10`，零时分配16字节、写全局并自连链表；没有在这段代码中看到一次性原子发布/互斥。Commit 的 `2d80 call3d10` 也依赖同一单例。静态 `.data` 对这些地址是 loader zero-fill，只证明初始存储，不证明运行中仍为零或不存在其他线程。

`15e00` 的 `15e04` 实际调用 GetProcessHeap，`15e0d` 写同一个 `32bc8`；初始化/清理函数指针对位于 `21c00/21c08`，完整原字节保留在 JSON。CRT entry `554c→5418→522c` 对 process attach 分支继续到 `527c`；其 C/C++初始化范围经 `fe88/fe44` 间接调表。这里没有将未解析的全部表/guard dispatch 自动算成“堆已初始化”。root 的额外 debug child 可以观测本次 attach 返回、真实 Prepare 成功；它不能关闭内存失败/new-handler、全局状态变化或锁顺序的所有情形。

### F-log：4700 合链后确有 CRT、锁与迟到解析候选

status=source-reviewed，severity=n/a_re，category=design。

`4700` 不能以 RF `[4700,473b)` 截断。实际 primary group 有五段：`[4700,473b)`、`[473b,4758)`、`[4758,482d)`、`[482d,4881)`、`[4881,489b)`。后四段通过 CHAININFO 归属同一 primary；第一段 hash 为 `3f1f4a9d5f14729cdae26bbee0fbde49b159ce9e5c5e93a04ff0221f6674c068`，其余段各 hash 在 JSON。

Prepare 的 null/duplicate 分支在 `30a1/30b2/310f/3123` 调4700；Commit 对应 `2d61/2d72/2dbf/2dd3`。`4728` 比较日志阈值，`4734/483d/4846` 读取日志配置，之后可进入文件或 console 分支，不能把入口参数检查后剩下的错误路径当空操作。

已验证的保守候选链包括：

| 路径 | 实际终点及条件边界 |
| --- | --- |
| `3060→4700→46b0→e994→e8d8→13a88→13afc→10f28→10cb0`（Commit可经2d61进入同链） | `10d50` 实际 IAT LoadLibraryExW，flags=800；失败兼容分支 `10da4` 再调用。依赖 logger file配置、CRT流/锁和动态函数缓存条件，当前正常 fixture 不一定走到 |
| `3060→4700→e278→b23c→b1e4→f278→f0d0→f1f0` | `f252` 实际 FreeLibrary；不是普通导入表推断。仍是包含错误/格式化状态的保守路径 |
| `3060→4700→e870→e668→b850` | `b854` 实际 EnterCriticalSection；临界区既有初始化与锁顺序仍是依赖 |
| `3060→4700→e870→e668→e794→e708→13660→13780` | `139dd` 实际 WriteFile，另有标准流、console及编码辅助逻辑 |

`10cb0` 还会在 `10e2c/10e38` 调 FreeLibrary/GetProcAddress，维护已编码缓存指针。logger `46b0` 读取 `31a28` filename、`31a30` FILE*并可懒开文件；`48a0` 回落标准流格式化。默认数据阈值 `30b08=1`，其余日志配置在磁盘为 zero-fill；不能由磁盘默认值断言 runtime所有分支不可达。Prepare/Commit 直接图未发现到 GetStringTypeW 的已解析路径，CRT entry图存在它；**因此既不声称它已从 Prepare 调用，也不声称所有间接路径都排除了它。**

Microsoft 明确限制 DllMain 中直接或间接 LoadLibrary/LoadLibraryEx、部分字符串分类API和跨线程同步；CRT和锁也必须满足初始化/锁序条件。这里的判断依据是上述实际候选路径以及尚未关闭的分支，不是单看导入表。[DLL best practices](https://learn.microsoft.com/en-us/windows/win32/dlls/dynamic-link-library-best-practices)、[DllMain](https://learn.microsoft.com/en-us/windows/win32/dlls/dllmain)（2026-10-25实际访问）

### F-virtual：已补固定表仍不足以闭合一般路由

status=source-reviewed，severity=n/a_re，category=design。

Prepare 在 `31fc/3203` 把固定 vtable `20590` 写入新对象，四项确为 `2c90/29a0/2810/2c70`。因此能有条件补出 `3214→29a0`、`321d→2c90`，以及同类路由对象的 `2883→2810`、`2d1f→2c70`。它们只适用于构造来源保持一致的 fresh record；没有把任意寄存器虚调用当这个表。

下游 `2c9c/2ca6`、`2810` 内 `2838/2843`、`2940` 内 `2970` 等仍是虚调用。泛用重定位/分配代码还使用 switch/函数指针，CRT有guard dispatch、new-handler、异常和初始化/清理回调。脚本保留这些 actual sites，不以直接图已扫完代替间接闭包。

补表后的 Commit 路径 `2d30→2880→2810→3aa0` 可达实际写页函数：`3aee VirtualProtect`，失败后 `3af8 GetCurrentProcessId → 3b06 OpenProcess → 3b29 VirtualProtectEx`，`3b4c` 复制字节，之后恢复保护和 CloseHandle。这解释了当前实现的真实 fallback，而不是建议 loader 中打开其他进程。Prepare 也可能对新 trampoline/relocated buffer执行该写页例程，不能仅因它调用 VirtualProtect 就称其提前改了四个源 entry；本 fixture 的源前缀回读是另一份实测事实。

## 下一段应替换的具体部分

当前自有 fixture 保留固定 Dobby，用来实测依赖排序、错误退出和四个真实发布。**生产 loader 路径应另做最小 native publisher 的自有版本，再决定是否映射游戏。** 不把“预热一次”或禁日志配置当所有故障路径已安全；以下是新增实现要求，尚未完成：

1. 用编译期四项 POD 描述、固定容量原始/发布字节与永久槽，替代 `3d10/3d50` lazy list、`4d38/114b8` new/HeapAlloc、泛用路由对象和动态虚表工厂。容量/重复claim直接拒绝，无new-handler、std容器和动态CRT依赖。
2. 将本固定目标的指令截取/重定位计划在 loader 外离线生成并审查，实际入口只核固定模块/hash/RVA/完整原字节和已声明修正。用已映射模块内的固定原生stub/预留存储证明地址和容量，避免 runtime泛用指令解码、近址循环分配和未解switch/vcall。不能把本 fixture 的32个NOP原样套到UnityPlayer；游戏真实前缀、RIP修正、栈/unwind/异常及所有四ABI必须另有实际字节和自有非NOP测试。
3. 错误通路只写编译期定长原始事件和数值状态，移除 `4700/46b0/48a0` 及所有 stdio、文件流、locale、动态函数缓存/LoadLibrary/FreeLibrary依赖。先后顺序和溢出本身也须实测，不能先声称无日志故障。
4. 写页只保留已审查的当前自有进程、固定范围、显式保护/恢复和指令缓存处理；不用 `3aa0` 的 OpenProcess/VirtualProtectEx fallback或不透明动态分配。每次真实尝试、结果、回读均记账；写尝试后的错误维持Unknown并有限退出，禁止重试、假回滚、卸载或放行Importer。这里没有授任何游戏进程首写权限。
5. 对新publisher重新核其普通导入和实际调用闭包，完成TLS/CRT/首调用、worker/迟到、每写前后失败、异常和非NOP trampoline/unwind自有矩阵。之后仍须独立证明游戏四入口首调用之前时序、正式凭据、真正对象/GC及scheduler/futureStart等；不能复用本次39绿或旧V3变为游戏Ready。

这些要求消除的是已识别的依赖来源，仍不自动证明 OS API 内部、冷启动线程历史或任意异常上下文全部安全。JSON 的 loaderLockSafeProved、fullIndirectClosureProved、allFailurePathsSafeProved、gameReady 均保持 false；`EngineCancelled=false`，Pending 和完整 B01–B19 目标不变。
