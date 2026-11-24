# Kernel 早期链：固定发布器编译配置

2026-10-25。当前只实现 canonical 发布器的编译期身份分离，尚未构建或执行子进程。复用同一份 FixedPublisher.cpp，不复制发布逻辑；基线是 [kernel-install组合599源码](../foundation-kernel-install-20261025/final-source-inputs.zip)，历史归档保持原件。

## 接口决定

新增 [FixedPublisherProfile.h](../../../native/RizToolbox.FixedPublisher/FixedPublisherProfile.h)，由 canonical [FixedPublisher.h](../../../native/RizToolbox.FixedPublisher/FixedPublisher.h)包含。未定义 `RIZ_EARLY_ENGINE_KERNEL` 为旧配置；只允许显式 `RIZ_EARLY_ENGINE_KERNEL=1` 选择新配置，定义为0或其他数值在预处理期#error，避免调用者以为关掉宏却编出另一身份。

| 项目 | 默认配置 | Kernel配置 |
| --- | --- | --- |
| Publisher模块 | `RizFixedPublisher.dll` | `RizKernelFixedPublisher.dll` |
| helper模块 | `RizToolbox.JudgeContext.dll` | `RizToolbox.EngineNodeKernel.dll` |
| `RizFixedVersion` / GetVersion | 1 | 2 |
| Config / Binding / Info / Receipt.version | 1 | 2 |
| 目标模块、marker | `RizFixedOwnedTargets.dll` / `RIZFIXEDOWNEDV1\0` | 相同 |

六项API导出名继续为 `RizFixedPublisher_{Prepare,Bind,Commit,GetInfo,GetVersion,GetInfoSize}`；不改为RizKernelFixedPublisher_。现有original符号/continuation及总体导出ABI不变。结构名保留V1表示历史布局，Config128、Binding120、Event80、Receipt272、Info21640及offset均未变，没有复用reserved字段。新配置的记录头值2与Early共享wire版本3分别检查；不能用同一个数字覆盖所有层。

helper仍为调用返回ABI2、scalar record1、slot28。Bind中`actual.abiVersion=1`、GetAbiVersion()==2保持原语义，不随Publisher记录版本2改变。目标仍严格核四个owned导出、固定15/18/16/15字节非NOP前缀及只读16字节marker；没有允许UnityPlayer、任意module/target或游戏写入的开关。

## 实际代码检查

[FixedPublisher.cpp](../../../native/RizToolbox.FixedPublisher/FixedPublisher.cpp)所有Publisher版本点统一使用`RizFixedVersion`：InitializeInfo和四Receipt、Prepare输入、Bind输入、GetVersion、GetInfo输入。GetInfo错版本返回3且不伪造成功快照；Prepare/Bind错版本保留原来永久InvalidRecord拒绝及责任语义，没有跨版本自动翻译。其余字节/页保护/缓存/故障/失败路径不改。

Prepare的self查询和helper查询来自编译配置。四个original stub的可执行范围仍必须属于所选self模块；仅加载一个同名旧发布器不能替代当前stub allocation验证。Bind首先要求`binding.helperModule`等于精确所选GetModuleHandle结果，随后核GetAbiVersion/GetEntry/GetInfo三个已知导出都属于该模块的可执行映像；每个entry同样须属于该模块并等于GetEntry(kind,28)实际值。Info还须匹配本次target/original/entry/helperEpoch，且未Attach、无Unknown、计数全0。新配置不能用旧JudgeContext entry混配，旧配置也不会自动接受Kernel entry。

版本2足以阻止旧record被静默当作新profile，但**版本不等于身份凭据**。调用者仍需固定Publisher/Kernel文件hash和实际模块、导出/entry allocation、发布器与helper独立epoch及每行字节/保护结果；不能仅GetVersion()==2就放行。Info/Receipt没有新增helperModule字段，正确解读依赖当前受核验Publisher实例和它在Bind时的严格模块判断。文件放置、loader链核验及生产首次准入凭据由独立层完成，不靠可伪造的整数version授权。

调用者各编译单元必须使用相同选择宏，才能让Config/Binding/GetInfo记录与实际导入的DLL配对。新import library应指向`RizKernelFixedPublisher.dll`和Kernel；保留默认构建以验证旧profile仍使用原DLL名/版本。新配置需单独保留Prepare/Bind/GetInfo旧version拒绝及旧helper模块/entry混用拒绝的自有进程证据；本页不提前称这些运行验证已完成。

## 本次静态边界

源码自查覆盖所有硬编码DLL名、version输入/输出及helper record版本，默认替换常量后与组合599基线逻辑相同。没有增加堆、CRT初始化、LoadLibrary、动态解码、任意回调或等待；故障selector、partial write、保护未恢复、写后Unknown和永久槽责任均沿原实现。静态判断不能替代root后续两个profile的真实构建、73模式与ABI运行原件。

本次不修改目标/marker以泛化游戏，不签发production V4，不授EngineCancelled，不安装或运行游戏。新Kernel早期链与真实游戏首次接入仍须各自验收；旧profile的历史73进程结果不会被改写为新profile证据。
