# Importer 初始化顺序的有限只读复核

2026-10-25。作者以 Python 标准库直接读取 COFF section/symbol/relocation 原件，另只读实际 PE；未构建、加载 DLL、启动 child 或修改冻结源码。以下是独立源码/磁盘证据复核，root 仍是唯一构建与宿主执行者。

## 证据、发现与修复方向

原 [history/importer-before.cpp](history/importer-before.cpp) 的 namespace 全局 `Api api{&WinHttpOpen,...}` 不是在 TLS 之前可靠可用的常量表。实际 r2 COFF 明确生成 `??__Eapi@?A0x8f6a9311@@YAXXZ` 动态初始化函数，并把它放入 `.CRT$XCU`；`Tls` 则位于 loader TLS callback `.CRT$XLB`。TLS 调用 `Run` 时会读尚未由该 CRT 函数填写的 `api`，所以不能用源码花括号初始化推断其早期已准备完成。

当前 `Importer.cpp` 把表改为 `ImportedApi()` 的当前栈值。r3 COFF 已没有 `api` 全局、`??__Eapi` 动态初始化函数或其 `.CRT$XCU` 条目；实际 `Run` 开头直接通过四个 IAT relocation 取地址。此修复消除了该具体动态初始化依赖。

**同时发现了第二个真实问题：** 两版 COFF 都没有显式 `CInitializer/CppInitializer`、`cInitializer/cppInitializer` 符号，也没有两者要求的 `.CRT$XIC/.CRT$XCU` 条目。源码 namespace 内的 const 分配指针没有强制保留，编译产物已经把它们省去；这不是对“可能没有 /INCLUDE”的猜测。r3 只剩 TLS 自定义条目，不能达到四初始化阶段、mask `0xf0` 及后续 12 行合同。

root 随后实际观察 session 49482 terminal exit 1、child 59236 exit 1，完整 JSON 无 SEH，缺失 C/C++ 两阶段触发断言失败。本页未执行该 child；该运行事实来自 root 的真实终态交接，不能当作本作者额外独立运行。最小接续是给两指针明确 external C 名称与 `/INCLUDE` 保留，再核实际对象段/符号、CFG 与真实阶段行，保留原严格断言。此页不预签后续修复通过。

## COFF 原件与可复核偏移

| 原件 | SHA256 |
|---|---|
| `D:/RizToolbox-build-cache/winhttp-forward-r2-20261025/binaries/WinHttpOwnedImporter.obj` | `3e9226e3930fca672cd74eb3e8e16a1ae3e705ff94d30df34ad702db2d3f65f1` |
| `D:/RizToolbox-build-cache/winhttp-forward-r3-20261025/binaries/WinHttpOwnedImporter.obj` | `64cb16961addc0e12c1c9acd10d9695d48dd0cb588307c42eefffe2ba81f510d` |

COFF section index 下文均从 1 开始，relocation offset 相对对应 section raw 数据；类型 `0x0001` 为 AMD64 ADDR64、`0x0004` 为 REL32。

- r2 共 99 sections。section 4 `.CRT$XCU` 长 8，offset 0 的 ADDR64 指向 symbol 140 `??__Eapi@?A0x8f6a9311@@YAXXZ`（section 10）。section 6 `.CRT$XLB` 长 8，offset 0 的 ADDR64 指向 symbol 16 `?Tls@?A0x8f6a9311@@YAXPEAXK0@Z`（section 5）。没有 `.CRT$XIC` 或第二条显式 C++ 初始化项。
- r2 动态初始化函数 section 10 长 57 字节；REL32 offsets `0x03/0x11/0x1f/0x2d` 读四个 `__imp_WinHttp*`，`0x0a/0x18/0x26/0x34` 写 symbol 310 `?api@?A0x8f6a9311@@3UApi@RizWinFixture@@A`（section 97，value `0x2860`）。r2 `Run` section 42 在 `0x108/0x1ee` 仍引用此全局表。
- r3 共 100 sections。唯一 `.CRT$*` 是 section 5 `.CRT$XLB`，长 8，offset 0 的 ADDR64 指向 `Tls`（section 4）。没有任何 `.CRT$XIC/.CRT$XCU`。符号表中显式 C/Cpp 初始化函数/指针及旧 `??__Eapi` 均不存在。
- r3 `ImportedApi` 为 section 33，raw `0x6a2b`、长度 `0x2f`、relocation table `0x6a5a`，四 REL32 offsets `0x03/0x0d/0x18/0x23` 分别指向 IAT symbols 117 Open、119 ProxyForUrl、120 IeConfig、118 Close。
- r3 `Run` 为 section 41，raw `0x7075`、长度 `0x312`、relocation table `0x7387`。实际内联 IAT 读取 REL32 offsets `0x37/0x48/0x54/0x60`（磁盘 raw `0x70ac/0x70bd/0x70c9/0x70d5`）顺序同上。这里没有再次依赖一个需要 CRT 初始化的全局 Api。
- 两对象仍正常引用 `memcpy/memset`、security cookie 与 `__guard_dispatch_icall_fptr`；Importer 是明示 `/MD` 的自有测试调用者，不属于 canonical forward 无 CRT 组件。r3 实际 DLL `DllCharacteristics=0x4160`，load-config `GuardFlags=0x10017500`、GuardCFFunctionCount=8，普通导入模块 `RizToolbox.WinHttpForward.dll` 下恰有四个 SDK 名字。该事实不关闭所有 CRT/loader 内部路径。

其余 globals 为零初始化 POD；本次未发现第二个被 CRT 填写后才可读的全局 Api。阶段运行仍只允许 TLS/C/C++/DllMain 的两个同步负例，四个完整 API 在 loader 返回后执行。后续修复不能把这套受控调用扩写为任意 WinHTTP API 的 DllMain 安全证明，也不授实际游戏/V4。

## r4 实际对象及既有运行原件复核

root 后续实际观察 session 40901 terminal exit 0、21 cases 全过；原件位于 `D:/RizToolbox-build-cache/winhttp-forward-r4-20261025`。本作者只读取该次现有 COFF/PE 与两份 normal snapshot，没有另行运行 child。本段补充 r4 的有限证明，不改写前述 r2/r3 红色历史。

- r4 `binaries/WinHttpOwnedImporter.obj` SHA256 `09a852554d7384924ebf4799f8dcc2a9b648b07cfc597c465da237aba499c889`，108 sections。section 7 `.CRT$XLB` raw `0x283d`、section 8 `.CRT$XIC` raw `0x284f`、section 9 `.CRT$XCU` raw `0x2861` 均长 8，offset 0 均有 ADDR64 relocation，分别指向 `Tls`（symbol 13/section 4）、`CInitializer`（symbol 16/section 5）、`CppInitializer`（symbol 19/section 6）。
- 两强保留外部 C 指针实际存在：`RizWinHttpImporterCInitializer` symbol 25/section 8、`RizWinHttpImporterCppInitializer` symbol 28/section 9。旧 `api` 全局、`api$initializer$` 和 `??__Eapi` 符号仍为零项；修复没有重新引入原来的动态全局 API 表。
- r4 `binaries/RizWinHttpOwnedImporter.dll` SHA256 `fd624ee086bd08b108951febe226f9d5e4160da304f25668106cf61f5a6188d4`。实际 TLS callback RVA `0x1000`；实际 C 初始化函数 RVA `0x1010` 把 ECX 置 5 并调用 Run RVA `0x26d0`，实际 C++ 初始化函数 RVA `0x1030` 把 ECX 置 6 再跳到同一 Run。映像内三个指针分别位于 RVA `0x51c8/0x51b0/0x5198`（磁盘 raw `0x3fc8/0x3fb0/0x3f98`）。
- CFG 仍开启：DllCharacteristics `0x4160`，GuardFlags `0x10017500`，GuardCFFunctionCount 从 r3 的 8 增至 10。实际 GFIDs 包含 `0x1000/0x1010/0x1030`，没有通过关闭 CFG 来让新增初始化调用绕过检查。

两 normal 原件均读取并核对 10272 字节 `case.importer.bin`，原 header 为 version 1/size 10272/mask `2288=0x8f0`/rowCount 12/checks 64/failures 0，与 `case.json.importerSnapshot` 的 header/SHA 一致：

| case | 既有 child PID/exit | 实际 snapshot SHA256 | 实际行顺序 |
|---|---|---|---|
| production-normal | 62856 / 0 | `93f5b69fc7b66dfeb5cdee3bd5f461cc071634a956dac88b8e3e03fe250148f9` | phase 4/5/6/7 各 Close、Proxy 两行；phase 11 为 Open、IE、Proxy、Close 四行 |
| fixture-normal | 61648 / 0 | `61b0360cfb2f2ac40bad286556d87c5e9ef05d90663f52735299f09ff72ce3ec` | 同上 |

两 case 的 process 原件 exit 0、独立 result 与 host JSON 均记录 passed true；本段从 binary row 自行解码阶段/kind，不只重复 host 的 passed 声明。这一有限复核确认两初始化槽确已保留、实际阶段记录齐全，并不重复签发全部 21-case 原生执行、系统 loader 闭包或实际游戏准入。
