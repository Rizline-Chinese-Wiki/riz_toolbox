# RizToolbox.JudgeContext

内部用的 Windows x64 原生入口上下文辅助库，专门服务于以下精确 IL2CPP 签名：

`void(receiver RCX, int32/enum32 EDX, float32 XMM2, MethodInfo* R9)`

目录内包含 C++/MASM 源码和一个真实的原生 ABI 测试夹具。本库**不负责**解析游戏符号，也不自己打补丁——安装归 `ExactHookBroker` 管，音符身份解析和面向 SDK 的服务发布归适配层的判定模块管。完整构建和安装流程已经包含这个 DLL。

## 构建与离线测试

使用本机已安装的 Visual Studio 2019 x64 工具链编译并运行夹具：

```powershell
pwsh -NoProfile -File scripts/Build-JudgeContext.ps1
```

脚本会调用 `vcvars64`、`ml64`、`cl` 和 `dumpbin`，只往指定输出目录写入产物（默认 `native/RizToolbox.JudgeContext/out`），然后运行独立的测试可执行文件。它不会跑 `dotnet`，也不会往游戏目录装任何文件。相关验证记录见 [validation.md](../../docs/validation/judge-context-20261020/validation.md)。

## 职责边界与运行约束

### 1. 寄存器捕获与栈帧
- `JudgeEntry.asm` 会在任何 C++/CLR 代码执行之前，先保存全部 16 个通用寄存器（GPR）和 `XMM0..15` 的完整 128 位。首条指令只分配栈空间，保存前不破坏任何寄存器。
- `gpr[Rsp]` 和 `entryStackPointer` 记录的是进入时的原始 `RSP`，`returnAddress` 是从该栈顶读出的原始返回地址。本版本不保存 RFLAGS、AVX/YMM 高半部分、x87 状态和 MXCSR；目标函数必须遵守标准 Windows x64 ABI。
- 栈分配大小为 `0x1c8`：32 字节 shadow space + 16 字节对齐的 `0x1a0` 帧 + 8 字节对齐补齐。所有非易失 GPR 和 `XMM6..15` 都带标准 MASM unwind 指令。只使用普通 `CALL`/`RET`，不伪造返回地址；编译时启用了 `/CETCOMPAT` 和 CFG 元数据（不代表测试机一定开启了硬件 CET）。

### 2. 调用与异常语义
- C++ 分发器先拷贝 4 个目标参数再调 `Enter`，随后**精确调用一次**配置好的原函数，并在原生 `__finally` 中调用 `Exit`。
- 原函数外层不套 `__except`，它的 SEH/C++ 异常会原样抛回给调用方。`Enter`/`Exit` 观察者自身的异常会被单独兜住，`Exit` 失败不会覆盖原函数的异常。
- 传入回调的 `FrameV1` 仅在当前栈帧有效且只读：回调里需要什么数值就当场拷走，**绝不能**保留帧指针。
- `Enter` 返回的每个 token（包括 `0`）都会配对收到一次 `Exit`。如果 `Enter` 还没返回就异常了，它自己负责清理半途分配的资源，辅助库会以 `token = 0` 且带 `EnterFaulted` 标志调用 `Exit`。
- 托管回调必须在跨互操作边界返回前自行捕获托管异常（SEH 兜底无法修复已损坏的内存）。

### 3. 生命周期与 Dobby 发布顺序
- 配置一旦写入即不可变，生命周期与进程相同，**没有**卸载或重置接口。DLL、原函数跳板、回调委托和 `userContext` 必须在进程退出前始终保持存活（rooted）。
- `SetEnabled(0)` 只阻止后续新进入的调用开启观测，**不是**静默/卸载屏障：已经进入观测的调用仍会走完 `Exit`，并发进入的调用也可能在 `SetEnabled` 返回后才进到 `Enter`。回调自身也必须检查模块活跃状态。统计计数是独立原子值，不能拿来判断是否可以释放委托。
- **必须先 `Configure`，再允许 `Entry` 被执行**；未配置就进入会直接 fail-fast。
- `ExactHookBroker` 先用固定版 Dobby 的 `Prepare` 创建跳板（此时目标函数字节尚未改动），配置并固定好本库与托管回调后，再调 `Commit` 发布补丁。允许的 Dobby SHA-256 为：
  `8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded`
- 如果原生安装过程出现不确定失败，地址、辅助库和回调会继续由进程持有，并拒绝二次安装。逻辑订阅者可以随时取消订阅，但原生入口和回调根在进程存活期间永不卸载。

发布测试夹具会加载这个真实 Dobby DLL，校验 `Commit` 前字节未改、`Commit` 后补丁生效，并用 4 个工作线程跑通跳板（正常安装发生在游戏主线程开打前，本测试不声称改写函数序言对并发执行该序言的线程是原子的）。静态证据见 `docs/validation/judge-context-publication-20261020/dobby-static.json`，完整集成证据见 `docs/validation/runtime-judgement-identity-20261020/`。

## 导出函数与 Frame ABI v1

所有导出和回调均使用 Windows x64 调用约定（C# 侧标注 `Cdecl` 即可）。结构体大小和偏移在 `JudgeContext.h` 中做了编译期断言，并由夹具校验。导出名无修饰：

| 导出函数 | 约定 |
| --- | --- |
| `RizJudgeContext_GetEntry()` | 返回替换入口地址；不会触发配置或打补丁 |
| `RizJudgeContext_GetFrameSize()` | 返回 `0x1a0` |
| `RizJudgeContext_Configure(ConfigV1*)` | 单次拷贝配置；`0` 成功，`1` 配置无效，`2` 已配置过 |
| `RizJudgeContext_SetEnabled(uint)` | `0` 成功；`1` 值非法或未配置；仅接受 `0` 或 `1` |
| `RizJudgeContext_GetStats(StatsV1*)` | 调用方先填 `version=1, size=0x30`；`0` 成功，`1` 头部无效 |

回调签名与退出标志：
- `Enter`：`uintptr_t Enter(const FrameV1*, void* user)`
- `Exit`：`void Exit(const FrameV1*, uintptr_t token, uint32 flags, void* user)`
- `flags`：`OriginalReturned = 1`，`OriginalAbnormal = 2`，`EnterFaulted = 4`（前两者互斥）

### Frame 字节布局（小端序，总长 `0x1a0`，地址 16 字节对齐）

| 偏移 | 类型 | 含义 |
| --- | --- | --- |
| `0x000` | `uint32` | ABI 版本 `1` |
| `0x004` | `uint32` | 字节大小 `0x1a0` |
| `0x008` | `uint64` | 原始调用方返回地址 |
| `0x010` | `uint64` | 进入时的原始 `RSP` |
| `0x018` | `uint64` | 保留字段（`0`） |
| `0x020` | `16 × uint64` | `RAX, RCX, RDX, RBX, RSP, RBP, RSI, RDI, R8, R9, R10, R11, R12, R13, R14, R15` |
| `0x0a0` | `16 × 16 bytes` | `XMM0..15`，每个寄存器先存低 `uint64` 再存高 `uint64` |

- **Config 布局（总长 `0x30`）**：`version` (`uint32@0`)、`size` (`uint32@4`)、`original` (`ptr@8`)、`enter` (`ptr@0x10`)、`exit` (`ptr@0x18`)、`userContext` (`ptr@0x20`)、`initiallyEnabled` (`uint32@0x28`)、`reserved` (`uint32@0x2c = 0`)。
- **Stats 布局（总长 `0x30`）**：`version` (`uint32@0`)、`size` (`uint32@4`)、`entered` (`uint64@8`)、`exited` (`uint64@0x10`)、`inFlight` (`uint64@0x18`)、`enterFaults` (`uint64@0x20`)、`exitFaults` (`uint64@0x28`)。

内部适配层 / Runtime 集成用的 C# 结构体定义：

```csharp
[StructLayout(LayoutKind.Explicit, Size = 0x1a0, Pack = 8)]
internal unsafe struct JudgeFrameV1
{
    [FieldOffset(0)] internal uint AbiVersion;
    [FieldOffset(4)] internal uint ByteSize;
    [FieldOffset(8)] internal ulong ReturnAddress;
    [FieldOffset(0x10)] internal ulong EntryStackPointer;
    [FieldOffset(0x18)] internal ulong Reserved;
    [FieldOffset(0x20)] internal fixed ulong Gpr[16];
    [FieldOffset(0xa0)] internal fixed ulong XmmQwords[32];
}
[StructLayout(LayoutKind.Sequential, Pack = 8, Size = 0x30)]
internal struct JudgeConfigV1
{
    internal uint AbiVersion, ByteSize;
    internal IntPtr Original, Enter, Exit, UserContext;
    internal uint InitiallyEnabled, Reserved;
}
[StructLayout(LayoutKind.Sequential, Pack = 8, Size = 0x30)]
internal struct JudgeStatsV1
{
    internal uint AbiVersion, ByteSize;
    internal ulong Entered, Exited, InFlight, EnterFaults, ExitFaults;
}
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate nuint JudgeEnter(IntPtr frame, IntPtr userContext);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void JudgeExit(IntPtr frame, nuint token, uint flags, IntPtr userContext);
```

### 字段读取要点
- 原始 `enum32` 取 `Gpr[2]` 的低 32 位；原始 `float32` 取 `XmmQwords[4]` 的低 32 位按位转成 `float`（**不要**直接做 `uint64` 到 `float` 的数值强转）。
- `receiver` 是 `Gpr[1]`，`MethodInfo*` 是 `Gpr[9]`。
- 提取来源对象和 `NoteInformation` 仍需结合固定版本的返回地址 RVA 表、类型/字段校验和当前局注册表匹配。未知来源必须返回 `0`/空语义上下文，同时照常放行原函数；任何原始指针、`InstanceID` 或帧地址都不能泄漏到公开 SDK。

## 集成现状与待实机覆盖项

`ExactHookBroker` 已实现同一地址共用单个补丁，并在内层 `NormalMode` 原函数调用期间保留调用方上下文；适配层会校验 12 处返回地址、音符来源、对象图与当前局，外层 `levelOver` 空转不会产生内层判定事件。

托管测试与原生夹具已分别覆盖未知来源、重入、撤回、异常原函数、ABI、SEH/C++ 展开和 Dobby 准备发布，但这些离线检查不能替代真实游戏中全部 12 处来源的覆盖测试，也不能替代实测时间偏差单位的校准。
