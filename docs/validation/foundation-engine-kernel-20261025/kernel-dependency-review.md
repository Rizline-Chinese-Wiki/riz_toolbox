# EngineNode Kernel 依赖与恒量表有限复核

2026-10-25。**轻量 Kernel 使用同一份 canonical 引擎实现，已完成正式统一构建及原生、托管联合调用；最终 DLL 只有两个系统导入，没有入口、TLS 或 CRT。** root 实际观察 full session 23528 终态0；本复核已核归档终态记录、599项源码、23项包及 Kernel 原件。25套1617项和 Kernel 7个独立进程分别计数，不将它们合成同一运行链。

复核人也是本批 canonical 表改动作者，因此这是有限作者自查，不是独立全审。只读取磁盘 PE/COFF、机器码、冻结源码和既有预检 JSON；没有加载 DLL、构建、运行子进程、操作游戏或改冻结输入。逐项结果见 [kernel-dependency-review.json](kernel-dependency-review.json)。

## 当前检查的实际二进制

实际归档文件为 [RizToolbox.EngineNodeKernel.dll](native-build/engine-node-kernel/RizToolbox.EngineNodeKernel.dll)，39424 字节，SHA256 `923486d2901ab6f66ea6d90d99cc65b1afafbc3275c338f0fd71ea5d22bc5662`；原始缓存位于 `D:/RizToolbox-build-cache/engine-kernel-full-final-20261025/engine-node-kernel`。归档 DLL/obj 与该最终缓存逐字节哈希一致，DLL 同时与 [kernel-manifest](native-build/engine-node-kernel/kernel-manifest.json)和 [23项包清单](package-manifest.json)中 staged-package.zip 的对应条目一致。

专项预检 runner-first 的 Kernel SHA为 `41e01c80fd14f0db2eb8932303db3984f5aee4ea93a8a9e65f422a47a2e687a3`，COFF为 `d41501c49eb694bd5d00874caeccf4aaf8ec624373a20059145ff2a4a78ce444`；它们与正式文件哈希不同，不能替代最终原件。本次重新解析正式 PE/COFF，而非只替换报告哈希；四表布局和 GetEntry 全段机器码与预检逐项比较相等，先前真实预检身份仍保留在复核 JSON。

独立 pefile 读取结果：PE entry、TLS、delay-import、load-config 目录全0；恰8项 `RizEngineNodeContext_` 导出：GetAbiVersion、GetLimit、GetFrameSize、GetCompletionSize、GetEntry、Configure、Attach、GetInfo。普通导入严格为 `KERNEL32!RaiseFailFastException` 和 `ntdll!__C_specific_handler`，没有 JudgeContext、CRT、Dobby、Heap、LoadLibrary、memset/memcpy 或其他业务模块导入。四个 scalar getter 的真实机器码分别是 `mov eax,2/20h/70h/20h; ret`。

该 DLL 独立编译 [EngineNodeContext.cpp](../../../native/RizToolbox.JudgeContext/EngineNodeContext.cpp)，没有复制另一份传输实现。与[旧576归档](../foundation-early-loader-20261025/source-inputs.zip)提取的同一文件比较，旧 SHA 为 `e75de998e6f34ea708ee603993fa6f83e72ba8564050666c5348ba6b4884b6a5`，当前为 `d34321875604a5514c4a4ec54b4619a9d3e0b8ce5f60908f8759158705bf228d`。差异只有2 hunks、+22/-16：四种 typed function pointer alias、四个 constexpr factory/表，以及 GetEntry/IsEntry 在运行时边界做 void* 转换。Dispatch、Before/After、SEH、计数、Configure/Attach/GetInfo 逻辑原样。

## 四张表是加载重定位后的只读恒量

实际 GetEntry 机器码按 kind 读取下表，kind>3 或 slot>=32 返回0。每项在最终磁盘 PE 都有独立 DIR64 base relocation，128个地址互不相同且落在可执行节；所在 `.rdata` 不可写。

| kind | 表 RVA | 项数 | 首/末入口 RVA | DIR64 |
| --- | --- | --- | --- | --- |
| Start | `9020` | 32 | `1000` / `2930` | 32 |
| Execute | `9120` | 32 | `2A00` / `4330` | 32 |
| Release | `9220` | 32 | `4400` / `5D30` | 32 |
| Detach | `9320` | 32 | `5E00` / `7730` | 32 |

正式 [COFF EngineNodeContext.obj](native-build/engine-node-kernel/EngineNodeContext.obj) SHA256 为 `9c580749f77e6bfa8450d093347391f2f57a3b31333837bb000b06eb1373f7d1`。独立解码符号/节/重定位：四表在只读 `.rdata` 的0/256/512/768偏移，共1024字节；各32项 `IMAGE_REL_AMD64_ADDR64` 指向相应模板实例，没有 `.CRT$XCU` 节或 `??__E/??__F` 自动初始化符号。constexpr 类型表使恒量初始化成为编译约束；最后的真实 PE 进一步证明不需执行初始化器填表。

**对象仍含 `/DEFAULTLIB:"libcpmt"` 和 RuntimeLibrary=MT_StaticRelease 指令。** 它们来自 STL 头的 pragma，不能说 COFF 没有默认库声明。本次 `/NODEFAULTLIB` 链接抑制默认库，最终成功产物没有 CRT；COFF 的未解析外部仅为 RaiseFailFastException、SEH handler 和链接器提供的 ImageBase，没有 CxxFrameHandler、memset、memcpy、chkstk 或 guard_dispatch 依赖。无入口不代表所有组件随意移除初始化，而是这个最终组件已由字节和依赖事实验证。

## 正式运行原件、归档一致性与 ABI 边界

[build-run.json](build-run.json)记录 root 对 session 23528 的实际终态观察；本复核没有持有或轮询该会话。[wrapper终态](build.log.run.json)同为0，独立重算 [build.log](build.log) SHA为 `1eac7b960d4559f8f36d45567f1300b2ac0c32268d1862dad10741516877a109`，与两份终态记录一致。[milestone-summary](milestone-summary.json)记录25套1617/1617及独立 Kernel 7进程；当前核正式 [Kernel运行原件](native-build/engine-node-kernel-cases/fixture-result.json)，没有用预检 session 96386 的 JSON 充当最终结果。

Entry fixture 1606 checks，独立 PE/实际映射交叉验证1486 checks，128项静态表重定位、256次把任意entry作为target/original的拒绝以及12个无效坐标请求均有结果。Entry fixture 本身不调用 typed entries，不把它与后续调用测试混为一项。

另一个原生子进程仍用实际 Dobby 给自有目标准备/发布四入口，3402 checks、136次原函数、124对 observer；Dobby 仅属于该测试发布器，Kernel DLL 没有 Dobby 导入。五个托管联合进程为 normal、original-seh、original-cpp、detach、observer-fault，checks分别227/158/154/203/147，共889；44次原函数。合计180次原函数不含 Entry fixture 的0次调用。该计数是独立自有进程，不能拼成游戏首装或同一个对象的一条完整运行链。

独立读取 [source-inputs.zip](source-inputs.zip)全部599条及 [staged-package.zip](staged-package.zip)全部23条，与各清单逐项匹配；599项当前源码也全部与清单相符。源码 ZIP SHA为 `a48d274857eb0b86a2c8530611282b374301e6ed73df17bdea537ccf8ef77532`，包 ZIP SHA为 `cbcb7f4b07f451717106435196f1f871040ae5ef45a9a5b97af1cc5a75067d6d`。归档记录与 kernel-manifest 内本次源码 hash一致；本复核没有修改599项冻结输入。

[held-inputs.json](native-build/engine-node-kernel-cases/held-inputs.json)保留31项实际共享只读句柄及运行前后哈希，全都在 Kernel runner 持有期间一致。当前复核仍有30项匹配，其中 Runtime.Tests 输出目录的旧 `RizToolbox.JudgeContext.dll` 在 runner 释放句柄后由 [Test-FirstInstallManaged.ps1:29](../../../scripts/Test-FirstInstallManaged.ps1)正常复制为本次 legacy helper。其 during hash为 `01e5171d2e7a824308e7e9053f6959fa7cbea98bb2c2742e1571d8bf75a66f48`，later hash为 `398d1c1ba90cbf55442c42a0b2b0197804b82ef1f575f0a26ee7942063809ec9`；不能把结束后差异改写成期间漂移或当前全31项仍相同。

[kernel-post-run-files.json](kernel-post-run-files.json)明确记录这一时序；[归档 supplied helper](native-build/engine-node-kernel-supplied-files/RizToolbox.JudgeContext.dll)独立与上批73进程归档同文件逐字节匹配，准确恢复 during hash。19项 supplied 原件均按 [kernel-supplied-files.json](kernel-supplied-files.json)哈希核过；历史 runner 原件没有被修改。Kernel DLL 自身始终为上述正式 `923486...`，没有被该后续 legacy 复制替换。

ABI继续为调用返回版本2、scalar record1、每kind32永久槽、Frame112/Completion32/Info112。Execute保留完整 uint32 EAX，Start/Release保留 uint8 AL，Detach void；没有布尔归一化或完整RAX承诺。原函数异常经原生 `__finally` 执行 Exit，再按原生异常机制继续；没有改成 C++ catch 或穿入 CLR 的异常转换。外部 observer 仍是确切签名的回调地址，Kernel 的小导入表并不能证明回调闭包安全。

CopyBefore 的有界字段读、Start实际R9输出槽读及observer异常会设置永久 Unknown；原异常在尚无observer时也会保留 Unknown。After不回读可能释放的node，referenceCount仍不是执行计数。一次Attach的根和配置继续保留到进程退出，没有新detach/reset/取消完成或根释放能力。OS RaiseFailFastException/SEH handler 内部和所有回调仍有自己的行为边界；这不妨碍本批先取得可实施的小内核，但不能把只含两个系统导入叫作全系统传递调用图已证明。

## 后续接入边界

本批尚未把新 Kernel 接入早期 producer。既有 [FixedPublisher.cpp](../../../native/RizToolbox.FixedPublisher/FixedPublisher.cpp)的 Bind 精确查找 `RizToolbox.JudgeContext.dll`，上批 EarlyFixed 的普通导入和配置也保持旧helper身份。因此必须另建精确绑定 `RizToolbox.EngineNodeKernel.dll` 的生产 bootstrap/新自有加载链及凭据，不能用8-export DLL覆盖59-export旧helper、合并两模块状态或复用旧epoch。旧73进程证据仍属于旧helper链。

游戏固定映像准入、实际WinHTTP转发、早于UnityPlayer初始化的安全首次写、生产V4、scheduler/async/内联清理及真实coordinator身份链仍未在本批完成。Kernel没有取消完成凭据，`EngineCancelled=false`、Pending保留、RequestPlay关闭，无安装或游戏执行；完整 B01–B19 范围保持。
