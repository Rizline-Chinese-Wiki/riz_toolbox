# 新旧 profile 构建与父验证脚本：静态审查

2026-10-25。只读审查三个脚本，未发现本次修改导致默认 legacy 失配或新 Kernel 链无法保持其声明身份的实质阻断。本页仅说明源码合同；session31803尚由root运行，未读取半写的fixture-result，也不声明测试已通过。复核者没有构建、启动子进程、操作游戏或编辑冻结源码。

三个当前文件均与 [604项冻结输入](inputs-before.json)中的哈希相同：

| 脚本 | SHA256 |
| --- | --- |
| [Build-FixedPublisherFixture.ps1](../../../scripts/Build-FixedPublisherFixture.ps1) | `7d512c095879b4ee7c50d7b0b4fe184179797e3c742bb0fbb95605961aff3a1a` |
| [Build-EarlyFixedFixture.ps1](../../../scripts/Build-EarlyFixedFixture.ps1) | `305c6213789e8ba7939d75826810f5d1d2cb15ed7d05d9a112338940495b2e40` |
| [Test-EarlyFixedFixture.ps1](../../../scripts/Test-EarlyFixedFixture.ps1) | `92be0c2d083fe324e4df823ff1e30b8eb45dc74d2648457e25965028499d330c` |

## 构建身份与兼容性

FixedPublisher builder默认空profile宏，保持RizFixedPublisher.dll/JudgeContext.dll和旧host构建。`-EngineKernel`必须同时`-PublisherOnly`，因此不会把记录版本1的standalone host链接到版本2发布器。新发布器的`/OUT`与`.def LIBRARY`均为RizKernelFixedPublisher.dll；本地import library仍叫RizFixedPublisher.lib，只是磁盘构建产物名，不会改变PE内的DLL导入身份。两种构建均保留六API名称、原始ASM/continuation符号及no-entry/no-CRT链接约束。

Early builder默认使用原RizOwned名称、helper及publisher；新profile精确使用RizKernelOwned Host/Proxy/Recorder/Debugger、RizToolbox.EngineNodeKernel.dll和RizKernelFixedPublisher.dll，Targets仍为RizFixedOwnedTargets.dll。所有包含profile的C++编译单元一致收到`/DRIZ_EARLY_ENGINE_KERNEL=1`，原Targets ASM无需该宏。链接用到的Recorder/Proxy import library虽仍为旧本地文件名，其嵌入的DLL名来自新`/OUT`，不会因此意外加载旧DLL。默认分支新增profile常量后的运行兼容性仍需实际旧链回归，不能只由静态相同名称代替。

命令行路径均绝对化或Resolve-Path，生成cmd的动态路径使用双引号，且拒绝换行、引号、百分号及shell元字符；可变DLL前缀和宏均来自固定switch分支，不来自任意命令字符串。没有新增进程名查杀、真实游戏安装或隐含旧host运行。

## 新配置的传递来源与锁定

Early builder的Kernel分支先读取完成的kernel-manifest和publisher fixture-build-manifest，核profile/版本及对应文件行恰1条；实际绝对路径和文件hash都必须等于所选Kernel/publisher。随后合并两个manifest的sources及这两份manifest自身的hash，并核每项来源为绝对路径、64位hash且实际文件相同。

最终sources同时包含Early8文件、新旧profile头、FixedTargets、FixedPublisher头及其profile、EngineNode头和本builder，以及合并而来的Publisher.cpp/originals/build脚本、Kernel canonical cpp/build脚本等。无需把新Kernel源码手工重复拷入另一份实现；也没有仅凭同名DLL声称旧来源为新完成构建。

父验证器新配置核manifest.profile=engine-kernel、publisherVersion2、wireVersion3，要求7个精确命名二进制（SkipDebugger时6个）对应唯一完成hash与路径，并持有同一个FileShare.Read句柄至全部子进程结束。新配置继续锁manifest声明的全部source、父脚本、新verifier、两份hash固定的历史verifier、完成manifest及Python文件；结束前用同一只读句柄重算hash。重复path只锁一次；本批builder在合并前已逐行核source一致，且root全局604freeze再次约束来源，没有用跳过重复路径来忽略实际不同版本。

默认legacy分支保持原先二进制锁定行为，不把新配置新增的传递source锁宣称为历史已具备。新的独立 [verify-early-kernel.py](verify-early-kernel.py)适配wire3/publisher2，Kernel仍为返回ABI2/scalar record1；ActualMemory记录版本1保持独立。历史两份verifier没有被修改。

## 运行与证据边界

选择器仍为baseline14先行、其余15模式、56个内部fault及附加debug，共73个进程（SkipDebugger为72）。同binary/system的未发布异常baseline是跨child比较，不能称同对象同run。父程序仅结束其创建的Process对象，超时明确失败；共享映射449120字节和mode15永久Busy协议未改。

本页不复用构建进行中的部分结果签成功。root终态后仍须对新链73、旧publisher57及旧链73分别核实际原件、PE依赖图、字节/保护/异常、source锁和before/after；本轮不是25套managed完整重建。去掉庞大JudgeContext普通依赖边是自有链进展，不授真实WinHTTP、生产首次准入/V4、coordinator采用或EngineCancelled。
